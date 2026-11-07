using System.Collections.Generic;
using UnityEngine;

namespace KOM
{
    public sealed class Fighter : MonoBehaviour
    {
        [SerializeField] string characterId;

        public string CharacterId => characterId;
        public CharacterDef Def { get; private set; }
        public Fighter Opponent { get; private set; }
        public CombatController Combat { get; private set; }
        public bool IsAI { get; private set; }
        /// <summary>Mandos de este luchador. Null en los controlados por la maquina.</summary>
        public FighterControls Controls { get; private set; }
        public Animator Animator { get; private set; }
        public float Health { get; private set; }
        public int RoundWins { get; private set; }
        public bool ControlsEnabled { get; set; }
        // Solo para pruebas y capturas: mantiene la guardia sin depender del teclado.
        public bool DebugHoldBlock { get; set; }
        // Solo para pruebas: fuerza el eje de movimiento (NaN = leer el teclado).
        public float DebugMoveAxis { get; set; } = float.NaN;
        public bool IsAttacking => _attack != null;
        public bool IsBlocking { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsKO { get; private set; }
        public bool IsGrounded => _airHeight <= 0.001f;
        public float AirHeight => _airHeight;
        // Estado interno del salto para las sondas de verificacion.
        public string DebugBufferState =>
            $"buffered={(_bufferedAttack != null ? _bufferedAttack.type.ToString() : "-")} t={_bufferedAge:F2} " +
            $"combo={(_pendingCombo != null ? _pendingCombo.type.ToString() : "-")} ct={_pendingComboAge:F2} " +
            $"atk={(_attack != null ? _attack.type.ToString() : "-")}";

        public string DebugJumpState =>
            $"active={_jumpActive} t={_jumpTimer:F3} v={_verticalVelocity:F2} h={_airHeight:F3} pb={_playback:F2}";
        public float Facing => _facing;
        public bool Initialized { get; private set; }
        public bool CurrentAttackHigh => _attack != null && _attack.high;
        public AttackSpec CurrentAttack => _attack;
        public bool IsSprinting => _sprintTimer > 0f;
        public float CurrentHurtboxRadius => Def != null ? Def.hurtboxRadius : CombatTuning.DefaultHurtboxRadius;
        public float CurrentHurtboxHeight => Def != null
            ? (IsCrouching && IsGrounded ? Def.crouchHurtboxHeight : Def.hurtboxHeight)
            : CombatTuning.DefaultHurtboxHeight;
        public float HurtboxBottom => _airHeight;
        public bool IsHit => _hurtTimer > 0f;
        public bool IsStunned => _stunTimer > 0f;
        public bool IsBlockStunned => _blockStunTimer > 0f;
        public bool IsFrozen => _freezeTimer > 0f;
        public float GuardFraction => _guardMeter;
        public float SuperFraction => _superMeter;
        public bool SuperReady => _superMeter >= CombatTuning.SuperFull - 0.0001f;
        public bool IsCastingSpecial => _activeSpecial != null;
        public SpecialDef ActiveSpecial => _activeSpecial;
        /// <summary>
        /// El instrumento del especial en curso. `null` significa dos cosas distintas y la
        /// sonda las separa: que el poder no usa prop, o que el prop se perdio por el camino.
        /// </summary>
        public GameObject ActiveProp => _prop;
        public bool SpecialUsesProp => _activeSpecial != null && !string.IsNullOrEmpty(_activeSpecial.propResource);
        public SpecialDef SelectedSpecial => SelectSpecial();
        public float SpecialRange
        {
            get
            {
                var s = SelectSpecial();
                return s != null ? s.range : 0f;
            }
        }
        public bool CanCastSpecial
        {
            get
            {
                var s = SelectSpecial();
                // `!Opponent.IsKO`: sin esta guarda la jefa lanzaba un especial en el mismisimo
                // frame del K.O., antes de que CombatController le quitara el control. El
                // rival ya esta tumbado y el golpe no puede conectar, pero el gesto y el VFX
                // salen igual: el jugador lo lee como "lanza el guitarrazo al eliminarme, sin
                // razon". Medido en Play al noquear con DevKill.
                return Initialized && s != null && s.IsCombatSpecial && SuperReady &&
                    ControlsEnabled && !IsKO && Opponent != null && !Opponent.IsKO && !_victory &&
                    IsGrounded && _attack == null &&
                    !_jumpActive && _downTimer <= 0f && _recoveryLock <= 0f &&
                    _hurtTimer <= 0f && _stunTimer <= 0f && _activeSpecial == null &&
                    !string.IsNullOrEmpty(s.animatorState) && Animator != null &&
                    Animator.HasState(CombatTuning.BaseLayer, Animator.StringToHash(s.animatorState));
            }
        }

        /// <summary>Cuantos poderes tiene disponibles AHORA (tras absorber, no los declarados).</summary>
        public int AvailableSpecialCount
            => _availableSpecials != null ? _availableSpecials.Length
               : (Def != null ? Def.SpecialCount : 0);

        public SpecialDef AvailableSpecial(int index)
        {
            if (Def == null || index < 0) return null;
            return Def.GetSpecial(_availableSpecials != null ? _availableSpecials[index] : index);
        }

        /// <summary>Los indices del catálogo que le quedan a la jefa para el orden que se le asigne.</summary>
        public int[] AvailableSpecialIndices => _availableSpecials;

        /// <summary>
        /// La jefa hereda el poder de los rivales DERROTADOS de esta partida. Se llama con el
        /// orden de rivales ya sorteado (`GameSession.DefeatedOpponents`), asi que en dos
        /// partidas con la misma rotacion la jefa tiene exactamente los mismos poderes.
        /// Su poder propio siempre esta disponible.
        /// </summary>
        public void AbsorbSpecials(string[] defeatedIds)
        {
            if (Def == null || Def.SpecialCount == 0) return;
            if (defeatedIds == null || defeatedIds.Length == 0)
            {
                // Sin derrotados, solo su poder base: el primero de la lista declarada.
                _availableSpecials = new[] { 0 };
                return;
            }
            var byType = new Dictionary<string, int>();
            for (int i = 0; i < Def.SpecialCount; i++)
            {
                var s = Def.GetSpecial(i);
                if (s == null || !s.IsCombatSpecial) continue;
                if (!string.IsNullOrEmpty(s.type) && !byType.ContainsKey(s.type)) byType[s.type] = i;
            }
            var picked = new List<int> { 0 };        // su poder propio, siempre
            foreach (var id in defeatedIds)
            {
                var d = KomData.GetCharacter(id);
                var own = d != null ? d.special : null;
                if (own == null || !own.IsCombatSpecial) continue;
                int idx;
                if (byType.TryGetValue(own.type, out idx) && !picked.Contains(idx)) picked.Add(idx);
            }
            _availableSpecials = picked.ToArray();
        }

        // Anclajes exactos para VFX: muneca y palma (entre la muneca y el nudillo).
        public Vector3 RightPalmPosition => PalmPosition(_handRight, _fingerRight);
        public Vector3 LeftPalmPosition => PalmPosition(_handLeft, _fingerLeft);
        // Eje de la mano (muneca -> nudillo). La cadena sale del puño por aqui, no por
        // una recta al rival: asi el latigo nace de la pose real.
        public Vector3 RightHandDirection =>
            _handRight != null && _fingerRight != null
                ? (_fingerRight.position - _handRight.position)
                : Vector3.zero;
        public Vector3 HandMidPosition
        {
            get
            {
                if (_handRight != null && _handLeft != null)
                    return (RightPalmPosition + LeftPalmPosition) * 0.5f;
                if (_handRight != null) return RightPalmPosition;
                return transform.position + Vector3.up * 1.15f + transform.forward * 0.35f;
            }
        }

        const float Gravity = 22f;
        // El impulso se DERIVA del tiempo de aire, no al reves: asi la parabola aterriza
        // exactamente cuando el clip llega al tramo de aterrizaje.
        static float JumpVelocity => Gravity * CombatTuning.JumpAirtime * 0.5f * 1.02f;
        const float MinSeparation = 0.82f;

        static readonly int IdleHash = Animator.StringToHash("Idle");

        // AAA autoridad unica: la capa Base(0) manda la pose COMPLETA. La capa 1
        // (ReservedSpecials) queda a peso 0 permanente y no participa. No existe capa
        // de upper body: mezclar dos poses full-body por mascara promediaba pelvis con
        // columna y desgarraba la malla. Un solo dueno de la pose = cero mezcla.
        const float FadeLocomotion = CombatTuning.FadeLocomotion;
        const float FadeGuard = CombatTuning.FadeGuard;
        const float FadeAttack = CombatTuning.FadeAttack;
        const float FadeAttackExit = CombatTuning.FadeAttackExit;
        const float FadeHit = CombatTuning.FadeHit;
        const float FadeHitOut = CombatTuning.FadeHitOut;
        const float FadeRun = CombatTuning.FadeRun;
        const float FadeKO = CombatTuning.FadeKO;

        IFighterBrain _brain;
        float _airHeight;
        float _verticalVelocity;
        // Salto en tres tramos: carga (aun en el suelo), aire y aterrizaje. Mientras
        // _jumpActive el luchador esta comprometido: no ataca ni bloquea hasta acabar.
        float _jumpTimer;
        bool _jumpActive;
        bool _jumpLaunched;
        // Recuperacion desde el suelo tras una reaccion que acaba tumbado.
        float _downTimer;
        // Candado de recuperacion tras acabar un ataque (ver AttackRecoveryLock).
        float _recoveryLock;
        // Cancelaciones seguidas: se corta la cadena a las dos para que no exista un
        // bucle infinito de golpes encadenados contra un rival en hitstun.
        int _cancelChain;
        float _facing = 1f;
        float _lastAppliedFacing = 999f;
        float _moveSignal;

        // --- Sensacion de movimiento e impacto ---------------------------------
        // _moveVel: velocidad real de desplazamiento. La rampa la lleva hacia el
        // objetivo con MoveAccel/MoveDecel en vez de saltar a tope en un frame.
        float _moveVel;
        // Velocidad pedida este frame. Se consume en UpdatePhysics y se limpia: si
        // nadie la declara (ataque, hitstun), el luchador frena solo.
        float _moveTarget;
        // _knockVel: empuje recibido, que decae. Sustituye al teletransporte.
        float _knockVel;
        // Congelacion local por impacto (hitstop). Se congela SOLO el luchador: los
        // VFX, la camara y la interfaz siguen a velocidad real, que es lo que hace
        // que el golpe se lea como un impacto y no como un tirón del juego entero.
        float _freezeTimer;
        // Velocidad de reproduccion del Animator (1 = la horneada por KomSetup).
        float _playback = 1f;
        // Aturdimiento por bloqueo: bloquea de verdad, pero no gratis.
        float _blockStunTimer;
        // Estado de reaccion en curso, para no reiniciar el clip en cada impacto de
        // un ataque multiple (QuadPunch golpea 4 veces en 0.30 s).
        string _hitState = string.Empty;
        // La proxima transicion de salida de reaccion usa el fundido largo.
        bool _reactionExit;

        AttackSpec _attack;
        float _attackTimer;
        int _nextHit;
        // ---------------------------------------------------------------------
        // INTENCION PENDIENTE (buffer de entrada)
        // ---------------------------------------------------------------------
        // Modelo de juego de pelea, en tres reglas:
        //  1. UNA SOLA orden pendiente. Toda pulsacion escribe en uno de los dos huecos y
        //     borra el otro, asi que no existe una cola donde apilar ordenes. Esto es lo
        //     que impide que "se encole" el golpe anterior.
        //  2. LA ULTIMA GANA. La pulsacion nueva sustituye a la que esperaba; nunca se
        //     ejecutan dos ordenes viejas en fila.
        //  3. CADUCIDAD corta y medida desde la pulsacion (AttackBufferTime). Si la tecla
        //     no puede salir dentro de la ventana, se descarta.
        //
        // Combo ya reconocido que espera al primer frame accionable. CADUCA
        // (ComboPendingMax): antes no lo hacia y un G+G+G pulsado durante un golpe se
        // ejecutaba un segundo despues, sin control para el jugador.
        AttackSpec _pendingCombo;
        AttackButton _pendingComboButton;
        float _pendingComboAge;
        // Pulsacion guardada y su reloj. `_bufferedHolds` distingue la pulsacion de un
        // boton (que puede sobrevivir mientras el dedo siga ahi) de una orden contextual
        // (sprint, agachado) que no tiene tecla que consultar.
        AttackSpec _bufferedAttack;
        float _bufferedAge;
        AttackButton _bufferedButton;
        bool _bufferedHolds;
        // Edad (en tiempo del luchador) a la que se SOLTO el boton. -1 = sigue apretado o
        // la orden no viene de un boton.
        float _bufferedReleasedAt = -1f;
        // Frame en el que ya se leyo el boton de ataque (ver UpdatePlayerAttackInput).
        int _inputFrame = -1;

        float _hurtTimer;
        float _sprintTimer;
        float _sprintCooldown;
        float _sprintDirection;
        // Ultimo eje de movimiento leido, para detectar un FLANCO (empezar a caminar)
        // y no confundirlo con mantener la direccion mientras se ataca.
        float _lastMoveAxis;

        float _guardMeter = 1f;
        float _guardRegenDelay;
        float _stunTimer;

        bool _victory;
        bool _paused;
        string _baseRequested = string.Empty;

        float _superMeter;
        SpecialDef _activeSpecial;
        // PODERES ABSORBIDOS (la jefa). El JSON declara la lista COMPLETA de poderes porque la
        // tuberia de assets necesita los seis estados y sus seis clips horneados; en combate
        // solo estan disponibles los de los rivales ya derrotados. `null` = usar la lista
        // completa del personaje (todos los demas luchadores).
        int[] _availableSpecials;
        // Cursor del reparto rotatorio (ver SelectSpecialIndex).
        int _specialCursor;
        float _specialTimer;
        bool _specialReleased;
        bool _specialHit;
        SpecialChargeFx _chargeFx;
        ChainWhipFx _chainFx;
        SpecialProjectileFx _projectileFx;
        SoundWaveFx _waveFx;

        // Historial de pulsaciones de ataque para los combos por secuencia. Se alimenta con
        // el input CRUDO del jugador (no con el intent): la secuencia se dialoga justo
        // mientras el golpe anterior esta en curso, y en ese momento ApplyIntent no corre.
        const int ComboHistoryMax = 8;
        readonly int[] _comboHistory = new int[ComboHistoryMax];
        int _comboHistoryCount;
        float _comboIdleTime;
        GroundSlamFx _slamFx;
        SuperAuraFx _superAura;
        BlockShieldFx _shield;
        // Destello de impacto sobre el cuerpo. Se crea una vez (cascaras sobre copia
        // de los SkinnedMeshRenderer) y se enciende por golpe: crear cascaras en cada
        // impacto generaria basura a 8 golpes por segundo.
        HitFlashFx _hitFlash;
        GameObject _prop;
        InstrumentChargeFx _instrumentFx;
        ClubFx _clubFx;
        BoomerangFx _boomerangFx;
        int _clubHitIndex;
        Transform _handRight;
        Transform _handLeft;
        Transform _fingerRight;
        Transform _fingerLeft;
        // Pecho: referencia del agarre de la guitarra (KomClubGrip alinea el cuerpo del prop
        // con la linea pecho -> palma, que es como se empuna un arma a dos manos).
        Transform _chest;

        void Awake()
        {
            Animator = GetComponentInChildren<Animator>();
            if (characterId == null) characterId = "";
        }

        public void Initialize(CharacterDef def, Fighter opponent, bool isAi, string aiProfile, int seed, CombatController combat,
            FighterControls controls = null)
        {
            Def = def;
            Opponent = opponent;
            IsAI = isAi;
            Combat = combat;
            Health = def != null ? def.maxHealth : 100f;
            _superMeter = 0f;
            // Cada luchador humano lee SU mando: sin esto los dos jugadores compartirian
            // teclado y el segundo moveria al primero.
            Controls = controls ?? FighterControls.First();
            _brain = isAi ? (IFighterBrain)new AiFighterBrain(aiProfile, seed) : new PlayerFighterBrain(Controls);
            if (Animator != null)
            {
                Animator.applyRootMotion = false;
                Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Animator.Rebind();
                Animator.speed = 1f;
                // Base manda la pose; la capa reservada queda a 0 permanente.
                // Locomocion discreta: Idle o WalkGuard, nunca una mezcla.
                Animator.Play(IdleHash, CombatTuning.BaseLayer, 0f);
                Animator.SetLayerWeight(CombatTuning.BaseLayer, 1f);
                if (Animator.layerCount > CombatTuning.ReservedLayer)
                    Animator.SetLayerWeight(CombatTuning.ReservedLayer, 0f);
                _baseRequested = "Idle";
            }
            _playback = 1f;
            CacheHands();
            _guardMeter = 1f;
            CreateGroundFx();
            // El aura usa el color del especial (dorado en Juan/Flavia, plata en Peso):
            // el aura y el ataque deben leerse como el mismo poder.
            _superAura = KomSpecialFx.CreateSuperAura(transform, KomData.Accent(def));
            // El bloqueo se lee por el escudo, no por el clip: se crea aqui y se
            // enciende/apaga con IsBlocking (con destello al encajar un golpe).
            _shield = BlockShieldFx.Attach(this, KomData.Accent(def));
            // El golpe tiene que ENTRAR en el cuerpo: la silueta se quema a blanco
            // durante 2-4 frames. Sin esto el dano se lee "atravesado".
            _hitFlash = HitFlashFx.Attach(this);
            Initialized = true;
        }

        void CacheHands()
        {
            _handRight = FindBone("DEF-hand.R");
            _handLeft = FindBone("DEF-hand.L");
            _fingerRight = FindBone("DEF-f_index.01.R");
            _fingerLeft = FindBone("DEF-f_index.01.L");
            _chest = FindBone("DEF-spine.003") ?? FindBone("DEF-spine.002") ?? FindBone("DEF-spine");
        }

        Transform FindBone(string boneName)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == boneName) return t;
            return null;
        }

        static Vector3 PalmPosition(Transform hand, Transform finger)
        {
            if (hand == null) return Vector3.zero;
            if (finger == null) return hand.position;
            return Vector3.Lerp(hand.position, finger.position, 0.6f);
        }

        public void SetCharacterId(string id) => characterId = id;
        public void SetRoundWins(int wins) => RoundWins = wins;

        void CreateGroundFx()
        {
            // Solo la sombra de contacto. El anillo de aura en el suelo se quito por
            // decision de arte: competia con el escenario y ensuciaba los pies.
            KomFx.CreateGroundShadow(transform, 1.15f, 0.65f);
        }

        public void ResetForRound(Vector3 position, float facing)
        {
            transform.position = position;
            transform.localScale = Vector3.one;
            _airHeight = 0f;
            _verticalVelocity = 0f;
            _facing = facing;
            _lastAppliedFacing = 999f;
            ApplyFacing(true);
            Health = Def != null ? Def.maxHealth : 100f;
            IsKO = false;
            _victory = false;
            ControlsEnabled = false;
            _attack = null;
            _attackTimer = 0f;
            _nextHit = 0;
            _bufferedAttack = null;
            _bufferedAge = 0f;
            _bufferedHolds = false;
            _pendingCombo = null;
            _pendingComboAge = 0f;
            _hurtTimer = 0f;
            _sprintTimer = 0f;
            _sprintCooldown = 0f;
            _moveSignal = 0f;
            _lastMoveAxis = 0f;
            // La orden de movimiento se consume en UpdatePhysics; si sobrevive al reset,
            // el luchador arranca la ronda siguiente andando hacia donde iba antes
            // (y en las sondas mueve al rival aunque sus controles esten apagados).
            _moveTarget = 0f;
            _moveVel = 0f;
            _knockVel = 0f;
            _freezeTimer = 0f;
            _blockStunTimer = 0f;
            _jumpTimer = 0f;
            _jumpActive = false;
            _jumpLaunched = false;
            _downTimer = 0f;
            _recoveryLock = 0f;
            _cancelChain = 0;
            if (_shield != null) _shield.Reset();
            if (_hitFlash != null) _hitFlash.Clear();
            _reactionExit = false;
            _hitState = string.Empty;
            _guardMeter = 1f;
            _guardRegenDelay = 0f;
            _stunTimer = 0f;
            IsBlocking = false;
            IsCrouching = false;
            CancelSpecial(true);
            // El medidor de super se conserva entre rondas: es energia ganada en la pelea.
            if (_superAura != null) _superAura.SetCharged(false);
            _playback = 1f;
            if (Animator != null)
            {
                // Sin Rebind: el Animator ya esta enlazado desde Initialize. Rebindear
                // en cada ronda recompilaba todos los bindings y congelaba el combate.
                Animator.enabled = true;
                Animator.speed = 1f;
                Animator.Play(IdleHash, CombatTuning.BaseLayer, 0f);
                Animator.SetLayerWeight(CombatTuning.BaseLayer, 1f);
                if (Animator.layerCount > CombatTuning.ReservedLayer)
                    Animator.SetLayerWeight(CombatTuning.ReservedLayer, 0f);
                _baseRequested = "Idle";
            }
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            // Una pausa a mitad de hitstop dejaria el Animator a velocidad 0 al
            // reanudar: se limpia la congelacion al salir.
            if (!paused) _freezeTimer = 0f;
            if (Animator != null)
            {
                Animator.enabled = !paused;
                if (!paused) ApplyPlayback();
            }
        }

        // Hitstop local. Congela SOLO a este luchador: pose, timers y avance. La
        // camara y los VFX siguen su curso, que es lo que da la sensacion de impacto.
        public void Freeze(float seconds)
        {
            if (seconds <= 0.0005f || IsKO) return;
            _freezeTimer = Mathf.Max(_freezeTimer, seconds);
            if (Animator != null) Animator.speed = 0f;
        }

        /// <summary>
        /// Destello del cuerpo al recibir un golpe. `rim` 0 = silueta quemada,
        /// 1 = solo contorno (bloqueo). Lo llama HitFeedback con una intensidad
        /// derivada del dano, no cada ataque por su cuenta.
        /// </summary>
        public void FlashImpact(float strength, float rim, float decay)
        {
            if (_hitFlash != null) _hitFlash.Flash(strength, rim, decay);
        }

        // Velocidad de reproduccion del Animator. Se aplica por estado (locomocion,
        // ataque, reaccion) y es el unico punto que escribe Animator.speed.
        void SetPlayback(float rate)
        {
            _playback = rate > 0.01f ? rate : 1f;
            ApplyPlayback();
        }

        void ApplyPlayback()
        {
            if (Animator == null) return;
            Animator.speed = _freezeTimer > 0f ? 0f : _playback;
        }

        void Update()
        {
            // Opponent puede estar destruido durante el desmontaje de la escena.
            if (!Initialized || _paused || IsKO || Opponent == null) return;

            // Hitstop LOCAL: se congela la pose y se ignoran los timers del luchador.
            // La camara, los VFX y la interfaz siguen a velocidad real, asi que el
            // mundo vive alrededor del impacto en vez de congelarse entero.
            if (_freezeTimer > 0f)
            {
                _freezeTimer -= Time.deltaTime;
                if (Animator != null) Animator.speed = 0f;
                // El hitstop congela al LUCHADOR, no al jugador. La pulsacion se captura
                // aqui y espera en el buffer: sin esto cada impacto dejaba 0.05-0.12 s de
                // sordera justo en el momento en que el jugador esta machacando, y los
                // golpes se sentian "tragados" sin motivo aparente. Solo se captura: un
                // ataque que arrancase con el Animator a velocidad 0 se quedaria plantado.
                if (!IsAI && ControlsEnabled) CaptureAttackPress();
                if (_freezeTimer > 0f) return;
                ApplyPlayback();
            }

            float dt = Time.deltaTime;
            UpdateTimers(dt);
            UpdateJump(dt);
            // UNICA lectura de los botones de ataque del jugador. Antes habia tres sitios
            // leyendo la misma pulsacion (historial de combo, cerebro y buffer), asi que un
            // solo toque podia disparar dos ataques distintos.
            if (!IsAI && ControlsEnabled)
            {
                // El movimiento se lee ANTES que los botones: si en este mismo frame el
                // jugador empieza a caminar y pulsa un ataque, gana el ataque (es el
                // input mas nuevo); si el ataque ya estaba esperando, el flanco lo tira.
                if (!DevIgnoreInput) UpdatePlayerBufferCancel();
                UpdatePlayerAttackInput(dt);
            }

            bool canAct = ControlsEnabled && _hurtTimer <= 0f && _attack == null &&
                _stunTimer <= 0f && _activeSpecial == null && _blockStunTimer <= 0f &&
                _downTimer <= 0f && _recoveryLock <= 0f;
            // Los temporizadores del cerebro corren siempre (ver IFighterBrain.Tick).
            // El null es defensivo: un Fighter instanciado sin pasar por Initialize (prefab
            // colocado en escena, reconstruccion de mundo) no tiene cerebro y soltaba un
            // NullReference por frame.
            if (_brain != null) _brain.Tick(dt);
            if (canAct && _brain != null)
            {
                var intent = _brain.Think(this, Opponent, dt);
                ApplyIntent(intent, dt);
            }
            else
            {
                _moveSignal = 0f;
                if (_hurtTimer <= 0f && _attack == null && _victory == false && _stunTimer <= 0f &&
                    _blockStunTimer <= 0f)
                {
                    IsBlocking = false;
                    IsCrouching = false;
                }
            }

            // Resuelve la UNICA orden pendiente que puede haber (combo reconocido o
            // pulsacion guardada). Se hace antes de avanzar el ataque para que el
            // encadenado entre en el mismo frame en que se abre la ventana.
            ResolvePendingInput(dt);

            if (_attack != null) UpdateAttack(dt);
            if (_activeSpecial != null) UpdateSpecial(dt);

            UpdatePhysics(dt);
            UpdateFacing();
            UpdateAura(dt);
            UpdateShield(dt);
            UpdateAnimationState();
            EnforceReservedLayer();
            DrawDebugBoxes();
        }

        void UpdateTimers(float dt)
        {
            if (_hurtTimer > 0f)
            {
                _hurtTimer = Mathf.Max(0f, _hurtTimer - dt);
                // Al salir del hitstun el clip de reaccion se corta antes de su cola:
                // la salida se funde en vez de saltar a la pose neutra.
                if (_hurtTimer <= 0f && _hitState.Length > 0)
                {
                    // GroinHit y Stunned terminan en el suelo: si se cortan y se vuelve a
                    // Idle, el luchador aparece de pie de un frame a otro. Se levanta.
                    if (IsDownReaction(_hitState)) StartDownRecovery();
                    else _reactionExit = true;
                }
            }
            if (_downTimer > 0f)
            {
                _downTimer = Mathf.Max(0f, _downTimer - dt);
                if (_downTimer <= 0f && IsKO == false)
                {
                    _hitState = string.Empty;
                    SetPlayback(1f);
                    PlayBaseState(DesiredBaseState(), FadeHitOut);
                }
            }
            if (_sprintTimer > 0f) _sprintTimer = Mathf.Max(0f, _sprintTimer - dt);
            if (_sprintCooldown > 0f) _sprintCooldown = Mathf.Max(0f, _sprintCooldown - dt);
            if (_recoveryLock > 0f) _recoveryLock = Mathf.Max(0f, _recoveryLock - dt);
            if (_guardRegenDelay > 0f) _guardRegenDelay = Mathf.Max(0f, _guardRegenDelay - dt);
            if (_blockStunTimer > 0f) _blockStunTimer = Mathf.Max(0f, _blockStunTimer - dt);

            if (_stunTimer > 0f)
            {
                _stunTimer = Mathf.Max(0f, _stunTimer - dt);
                if (_stunTimer <= 0f && IsKO == false)
                {
                    // Al salir del aturdimiento devolvemos media barra: la guardia rota
                    // no puede volver a bloquear de inmediato.
                    _guardMeter = Mathf.Max(_guardMeter, CombatTuning.GuardMeterAfterBreak);
                    StartDownRecovery();
                }
            }

            bool canRegen = IsBlocking == false && _stunTimer <= 0f && _hurtTimer <= 0f && _guardRegenDelay <= 0f;
            if (canRegen) _guardMeter = Mathf.MoveTowards(_guardMeter, 1f, dt * CombatTuning.GuardRegenPerSecond);
        }

        // Prioridad de entrada del jugador: si EMPIEZA a caminar, la orden de ataque que
        // estaba esperando su turno se descarta. Sin esto, pulsar dos veces y luego decidir
        // moverse ejecutaba los dos golpes igual: el juego obedecia una orden que el
        // jugador ya habia cambiado.
        //
        // Se mide como FLANCO (de reposo a pulsado). Mantener la direccion mientras se
        // ataca es lo normal y NO debe tirar el buffer: solo cuenta empezar a moverse.
        void UpdatePlayerBufferCancel()
        {
            bool forced = !float.IsNaN(DebugMoveAxis);
            float axis = forced ? DebugMoveAxis : Controls.MoveAxis();
            bool pressedKey = !forced && (Controls.MoveLeftPressed() || Controls.MoveRightPressed());
            bool fresh = pressedKey ||
                         (Mathf.Abs(axis) > CombatTuning.MoveCancelThreshold &&
                          Mathf.Abs(_lastMoveAxis) <= CombatTuning.MoveCancelRelease);
            _lastMoveAxis = axis;
            if (!fresh) return;
            if (_bufferedAttack == null && _pendingCombo == null) return;
            _bufferedAttack = null;
            _bufferedAge = 0f;
            _bufferedHolds = false;
            // El combo por secuencia tampoco sobrevive a un cambio de idea.
            _pendingCombo = null;
            _pendingComboAge = 0f;
        }

        // Secuencias de combo: G+G+G, H+H+H, J+J+J+J (un boton repetido).
        // Tokens: 1=G (golpe rapido), 2=H (fuerte), 3=J (patada rapida), 4=K (patada fuerte).
        //
        // Una pulsacion produce EXACTAMENTE una accion: o el ataque del boton, o el combo si
        // con esa tecla se completa la secuencia. El buffer guarda una sola cosa, asi que un
        // toque no puede disparar dos ataques.
        void UpdatePlayerAttackInput(float dt)
        {
            if (DevIgnoreInput) return;
            // El hitstop ya pudo capturar la pulsacion de ESTE frame. Sin esta guarda, el
            // frame exacto en que el congelado termina leeria la misma pulsacion dos veces
            // y un solo toque metia dos tokens en la secuencia.
            if (_inputFrame == Time.frameCount) return;
            _inputFrame = Time.frameCount;

            _comboIdleTime += dt;
            // Si pasa demasiado tiempo entre pulsaciones, la secuencia se reinicia.
            if (_comboIdleTime > CombatTuning.ComboChainStep && _comboHistoryCount > 0)
                _comboHistoryCount = 0;

            var button = ReadAttackButton();
            if (!button.HasValue) return;
            HandleAttackPress(button.Value);
        }

        AttackButton? ReadAttackButton()
        {
            if (Controls.LightPunchPressed()) return AttackButton.LightPunch;
            if (Controls.HeavyPunchPressed()) return AttackButton.HeavyPunch;
            if (Controls.LightKickPressed()) return AttackButton.LightKick;
            if (Controls.HeavyKickPressed()) return AttackButton.HeavyKick;
            return null;
        }

        // Lectura durante el hitstop: solo captura, nunca arranca (ver Update).
        void CaptureAttackPress()
        {
            if (_inputFrame == Time.frameCount) return;
            _inputFrame = Time.frameCount;
            var button = ReadAttackButton();
            if (!button.HasValue) return;
            HandleAttackPress(button.Value, true);
        }

        // Punto unico de entrada de un ataque del jugador. Lo usan el teclado y las pruebas.
        //
        // `captureOnly` = la pulsacion llega con el luchador congelado por hitstop: se
        // guarda pero no se intenta arrancar nada este frame.
        public void HandleAttackPress(AttackButton button, bool captureOnly = false)
        {
            _comboIdleTime = 0f;

            // UNICA excepcion contextual del jugador, y es un ataque EXTRA, no un cambio de
            // significado: mientras corre, K lanza la patada voladora. No entra en el
            // alfabeto de los combos (no consume token), asi que las secuencias siguen
            // intactas cuando se esta parado.
            if (button == AttackButton.HeavyKick && _sprintTimer > 0f)
            {
                _sprintTimer = 0f;
                QueueOrStartAttack(Attacks.DropKick, button, captureOnly);
                KomSpecialDiag.Log($"DROP_KICK|{CharacterId}");
                return;
            }

            // PRIORIDAD AL ULTIMO INPUT. Si habia un combo esperando su frame:
            //   · la MISMA tecla -> es el mismo mashing; el combo ya esta decidido y la
            //     pulsacion sobrante se ignora. Antes se encolaba un ataque suelto DETRAS
            //     del combo, que es justo lo que el jugador no habia pedido.
            //   · OTRA tecla -> el jugador cambio de idea: el combo se tira y manda la nueva.
            if (_pendingCombo != null)
            {
                if (_pendingComboButton == button) return;
                _pendingCombo = null;
                _pendingComboAge = 0f;
            }

            if (_comboHistoryCount >= ComboHistoryMax)
            {
                for (int i = 1; i < ComboHistoryMax; i++) _comboHistory[i - 1] = _comboHistory[i];
                _comboHistoryCount = ComboHistoryMax - 1;
            }
            _comboHistory[_comboHistoryCount++] = Attacks.Token(button);

            var combo = Attacks.MatchCombo(_comboHistory, _comboHistoryCount);
            if (combo != null)
            {
                // La secuencia se consume y MANDA: se descarta lo que hubiera en el buffer
                // para que no se cuele el ataque suelto de la ultima pulsacion. Con el
                // invariante de "una sola orden pendiente" no puede quedar nada detras.
                _comboHistoryCount = 0;
                _bufferedAttack = null;
                _bufferedAge = 0f;
                _bufferedHolds = false;
                _pendingCombo = combo;
                _pendingComboButton = button;
                _pendingComboAge = 0f;
                // Con el luchador congelado no se arranca nada (el Animator esta a 0):
                // el combo se resuelve en el primer frame con control.
                if (!captureOnly) TryStartPendingCombo();
                KomSpecialDiag.Log($"COMBO_SEQ|{CharacterId}|{combo.animatorState}|hits={combo.hitCount}");
                return;
            }

            QueueOrStartAttack(Attacks.FromButton(button), button, captureOnly);
        }

        // ---------------------------------------------------------------------
        // Resolucion de la intencion pendiente
        // ---------------------------------------------------------------------
        // Se llama UNA vez por frame, despues de leer el input y antes de avanzar el
        // ataque. Aqui vive toda la politica de entrada, en un solo sitio y sin numeros
        // precalculados: la pulsacion no "reserva" un hueco futuro, se queda esperando y
        // cada frame se pregunta si ya puede salir.
        void ResolvePendingInput(float dt)
        {
            // 1) Combo reconocido. Manda sobre el buffer porque es la pulsacion mas
            //    reciente (cuando se reconocio se vacio el buffer).
            if (_pendingCombo != null)
            {
                if (TryStartPendingCombo()) return;
                _pendingComboAge += dt;
                if (_pendingComboAge > CombatTuning.ComboPendingMax)
                {
                    // No llego a entrar a tiempo. Es la regla que convierte "se me encolo
                    // el combo" en "aquel combo ya no era lo que queria".
                    _pendingCombo = null;
                    _pendingComboAge = 0f;
                    KomSpecialDiag.Log($"COMBO_EXPIRE|{CharacterId}");
                }
                return;
            }

            if (_bufferedAttack == null) return;
            _bufferedAge += dt;

            // 2) Cancelacion en cadena: los golpes del ataque en curso ya se resolvieron y
            //    la recuperacion se puede cortar. El encadenado se siente inmediato.
            if (CanCancelInto(_bufferedAttack))
            {
                var pending = _bufferedAttack;
                ClearBuffer();
                _attack = null;
                _nextHit = 0;
                _cancelChain++;
                StartAttack(pending);
                return;
            }

            // 3) En reposo (o con el candado de recuperacion ya vencido).
            if (_attack == null && CanStartAttack(_bufferedAttack))
            {
                var pending = _bufferedAttack;
                ClearBuffer();
                StartAttack(pending);
                return;
            }

            // 4) Caducidad. Dos relojes: una pulsacion normal vive AttackBufferTime; si el
            //    jugador SIGUE apretando el boton vive AttackBufferHold (modelo de Smash
            //    Ultimate) y, al soltarlo, le quedan otros AttackBufferTime desde la
            //    SUELTA (no desde la pulsacion: si no, mantener el dedo 0.3 s y soltar
            //    mataba la orden en el acto). Pasado el tope se descarta aunque siga
            //    apretando: un golpe que sale un segundo despues no se reconoce como propio.
            bool held = _bufferedHolds && Controls != null && Controls.AttackHeld(_bufferedButton);
            if (!held && _bufferedReleasedAt < 0f) _bufferedReleasedAt = _bufferedAge;
            float life = held
                ? CombatTuning.AttackBufferHold
                : Mathf.Min(CombatTuning.AttackBufferHold, _bufferedReleasedAt + CombatTuning.AttackBufferTime);
            if (_bufferedAge > life) ClearBuffer();
        }

        void ClearBuffer()
        {
            _bufferedAttack = null;
            _bufferedAge = 0f;
            _bufferedHolds = false;
            _bufferedReleasedAt = -1f;
        }

        // El combo espera al primer frame accionable. Si le golpean antes, se descarta (ver
        // ReceiveHit): no se bufferiza a traves del hitstun.
        // Devuelve true si arranco (y por tanto consumio la orden).
        bool TryStartPendingCombo()
        {
            if (_pendingCombo == null) return false;
            var combo = _pendingCombo;

            // 1) Si el ataque en curso ya se puede cortar, el combo entra YA. Esperar al
            //    ultimo frame hacia que la secuencia se sintiera atascada medio segundo
            //    despues de la ultima pulsacion.
            if (_attack != null)
            {
                if (!CanCancelInto(combo)) return false;
                _attack = null;
                _nextHit = 0;
                _cancelChain++;
                if (!StartAttack(combo)) return false;
                _pendingCombo = null;
                _pendingComboAge = 0f;
                KomSpecialDiag.Log($"COMBO_START|{CharacterId}|{combo.animatorState}|hits={combo.hitCount}|cancel=True");
                return true;
            }

            // 2) En reposo: las MISMAS condiciones que exige StartAttack, incluido el
            //    candado de recuperacion. Si no, el combo se consumia en el primer frame
            //    con `_attack == null` (justo despues de un golpe, con 0.10 s de candado
            //    todavia activo), StartAttack lo rechazaba y la secuencia se perdia.
            if (_hurtTimer > 0f || _stunTimer > 0f || _blockStunTimer > 0f || !IsGrounded ||
                _recoveryLock > 0f || _downTimer > 0f || _jumpActive || IsBlocking) return false;

            // Solo se descarta si de verdad arranco: si el ataque se rechaza, el combo
            // sigue pendiente para el frame siguiente.
            if (!StartAttack(combo)) return false;
            _pendingCombo = null;
            _pendingComboAge = 0f;
            KomSpecialDiag.Log($"COMBO_START|{CharacterId}|{combo.animatorState}|hits={combo.hitCount}|cancel=False");
            return true;
        }

        // Solo para pruebas: inyecta una pulsacion como si el jugador la hubiera hecho.
        public void DebugPushComboToken(int token)
        {
            if (token < 1 || token > 4) return;
            HandleAttackPress((AttackButton)(token - 1));
            KomSpecialDiag.Log($"DEBUG_PRESS|{CharacterId}|token={token}|attack={(_attack != null ? _attack.type.ToString() : "none")}");
        }

        // Ejecuta una secuencia de tokens con separacion fija. Solo para pruebas: permite
        // verificar los combos con tiempos reales sin depender de la latencia del teclado.
        Coroutine _debugSeq;
        public void DebugRunSequence(string tokens, float step)
        {
            if (_debugSeq != null) StopCoroutine(_debugSeq);
            _debugSeq = StartCoroutine(DebugSequenceRoutine(tokens, step));
        }

        System.Collections.IEnumerator DebugSequenceRoutine(string tokens, float step)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                int t = tokens[i] - '0';
                if (t >= 1 && t <= 4) DebugPushComboToken(t);
                yield return new WaitForSeconds(step);
            }
            _debugSeq = null;
        }

        // Solo para pruebas: arranca un sprint sin depender del teclado.
        public void DebugSprint(float direction)
        {
            StartSprint(Mathf.Abs(direction) < 0.01f ? 1f : Mathf.Sign(direction));
        }

        // Deja al luchador en un estado limpio para el siguiente caso de prueba.
        public void DebugResetCombatState()
        {
            if (_debugSeq != null) { StopCoroutine(_debugSeq); _debugSeq = null; }
            _attack = null;
            _bufferedAttack = null;
            _bufferedAge = 0f;
            _bufferedHolds = false;
            _pendingCombo = null;
            _pendingComboAge = 0f;
            _comboHistoryCount = 0;
            _comboIdleTime = 0f;
            _hurtTimer = 0f;
            _stunTimer = 0f;
            _activeSpecial = null;
            IsBlocking = false;
            IsCrouching = false;
            _sprintTimer = 0f;
            _attackTimer = 0f;
            _nextHit = 0;
            _hurtTimer = 0f;
            _blockStunTimer = 0f;
            _freezeTimer = 0f;
            // Aislamiento entre casos: el candado y la cadena de cancelaciones son estado
            // del ataque anterior y hacian que un caso dependiera del anterior.
            _recoveryLock = 0f;
            _cancelChain = 0;
            _knockVel = 0f;
            _moveVel = 0f;
            _moveTarget = 0f;
            _hitState = string.Empty;
            _reactionExit = false;
            SetPlayback(1f);
            ClearAttackTrace();
            Health = Def != null ? Def.maxHealth : 100f;
            PlayBaseState("Idle", 0f);
        }

        bool CanStartAttack(AttackSpec spec)
        {
            if (ControlsEnabled == false || IsGrounded == false || _hurtTimer > 0f || _attack != null ||
                _stunTimer > 0f || _blockStunTimer > 0f || IsBlocking || _jumpActive || _downTimer > 0f ||
                _recoveryLock > 0f)
                return false;
            return true;
        }

        // Se puede cortar la recuperacion del ataque en curso si ya se resolvieron
        // TODOS sus golpes y se paso el punto de cancelacion. Nunca antes del impacto:
        // si no, el ataque perderia su ventana activa.
        bool CanCancelInto(AttackSpec spec)
        {
            if (spec == null || _attack == null) return false;
            if (_hurtTimer > 0f || _stunTimer > 0f || _blockStunTimer > 0f) return false;
            if (!IsGrounded || IsBlocking || _jumpActive || _downTimer > 0f || _recoveryLock > 0f) return false;
            if (_nextHit < _attack.hitCount) return false;
            if (_cancelChain >= CombatTuning.MaxCancelChain) return false;
            return _attackTimer >= _attack.Window * CombatTuning.AttackCancelAt;
        }

        void ApplyIntent(FighterIntent intent, float dt)
        {
            _moveSignal = 0f;

            if (intent.special)
            {
                if (CanCastSpecial)
                {
                    StartSpecial();
                    return;
                }
                Combat?.OnSpecialDenied(this);
            }

            // En el aire, y durante la carga del salto, solo se controla el
            // desplazamiento: ni ataque, ni guardia, ni agacharse.
            if (!IsGrounded || _jumpActive)
            {
                IsBlocking = false;
                IsCrouching = false;
                float airSpeed = Def != null ? Def.walkSpeed * 0.62f * CombatTuning.WalkSpeedScale : 1.2f;
                Move(intent.move, airSpeed, dt);
                _moveSignal = intent.move;
                return;
            }

            if (intent.block || DebugHoldBlock)
            {
                IsBlocking = true;
                IsCrouching = false;
                _sprintTimer = 0f;
                _guardRegenDelay = 0f;
                // Caminar con la guardia alta es el MISMO movimiento que andar normal:
                // mismo clip y misma velocidad. Bloquear quieto es el unico estado Block.
                Move(intent.move, (Def != null ? Def.walkSpeed : 1.6f) * CombatTuning.WalkSpeedScale, dt);
                _moveSignal = intent.move;
                return;
            }

            IsBlocking = false;

            if (_sprintTimer > 0f)
            {
                IsCrouching = false;
                Move(_sprintDirection, (Def != null ? Def.dashSpeed : 3.1f) * CombatTuning.DashSpeedScale, dt);
                _moveSignal = _sprintDirection;
                // Sprint + patada fuerte = patada en carrera.
                var sprintSpec = Attacks.FromIntent(intent, true, false);
                if (sprintSpec != null)
                {
                    _sprintTimer = 0f;
                    QueueOrStartAttack(sprintSpec);
                }
                return;
            }

            if (intent.crouch)
            {
                IsCrouching = true;
                var crouchSpec = Attacks.FromIntent(intent, false, true);
                if (crouchSpec != null) QueueOrStartAttack(crouchSpec);
                return;
            }

            IsCrouching = false;

            if (intent.jump)
            {
                Jump();
                return;
            }

            if ((intent.dashLeft || intent.dashRight) && _sprintCooldown <= 0f)
            {
                StartSprint(intent.dashLeft ? -1f : 1f);
                Move(_sprintDirection, (Def != null ? Def.dashSpeed : 3.1f) * CombatTuning.DashSpeedScale, dt);
                _moveSignal = _sprintDirection;
                // Sprint + patada fuerte en el mismo input: patada en carrera inmediata.
                var sprintSpec = Attacks.FromIntent(intent, true, false);
                if (sprintSpec != null)
                {
                    _sprintTimer = 0f;
                    QueueOrStartAttack(sprintSpec);
                }
                return;
            }

            Move(intent.move, (Def != null ? Def.walkSpeed : 1.6f) * CombatTuning.WalkSpeedScale, dt);
            _moveSignal = intent.move;

            var spec = Attacks.FromIntent(intent, false, false);
            if (spec != null) QueueOrStartAttack(spec);
        }

        void Jump()
        {
            if (_jumpActive) return;
            // Fase de carga: el cuerpo sigue en el suelo mientras el clip flexiona las
            // piernas. El impulso sale en UpdateJump al acabar esa carga.
            _jumpActive = true;
            _jumpLaunched = false;
            _jumpTimer = 0f;
            _verticalVelocity = 0f;
            _airHeight = 0f;
            _sprintTimer = 0f;
            IsBlocking = false;
            IsCrouching = false;
            _moveVel = 0f;
            PlayBaseState("Jump", FadeLocomotion, true);
        }

        void UpdateJump(float dt)
        {
            if (!_jumpActive) return;
            _jumpTimer += dt;

            if (!_jumpLaunched && _jumpTimer >= CombatTuning.JumpAnticipation)
            {
                // Aqui es donde el clip extiende las piernas: el cuerpo despega ahora.
                _jumpLaunched = true;
                _verticalVelocity = JumpVelocity;
                _airHeight = 0.01f;
            }

            if (_jumpTimer >= CombatTuning.JumpTotal)
            {
                _jumpActive = false;
                if (!IsKO)
                {
                    SetPlayback(1f);
                    PlayBaseState(DesiredBaseState(), FadeLocomotion);
                }
            }
        }

        // Reproduccion por tramos: cada parte del clip ocupa SU tiempo real, asi el
        // aterrizaje del clip coincide con el aterrizaje de la parabola.
        static float JumpPlayback(float jumpTime)
        {
            const float D = CombatTuning.JumpStateDuration;
            if (jumpTime < CombatTuning.JumpAnticipation)
                return CombatTuning.JumpTakeoffPhase * D / Mathf.Max(0.02f, CombatTuning.JumpAnticipation);
            if (jumpTime < CombatTuning.JumpAnticipation + CombatTuning.JumpAirtime)
                return (CombatTuning.JumpLandPhase - CombatTuning.JumpTakeoffPhase) * D /
                       Mathf.Max(0.05f, CombatTuning.JumpAirtime);
            return (1f - CombatTuning.JumpLandPhase) * D / Mathf.Max(0.05f, CombatTuning.JumpLanding);
        }

        static bool IsDownReaction(string state) => state == "GroinHit" || state == "Stunned";

        // Levantarse tras una reaccion que acaba en el suelo. Entra por la mitad del clip
        // de GetUp (ya incorporandose) para que dure lo mismo que la ventana de castigo.
        void StartDownRecovery()
        {
            _hitState = string.Empty;
            _reactionExit = false;
            _downTimer = CombatTuning.DownRecoveryDuration;
            IsBlocking = false;
            IsCrouching = false;
            SetPlayback(CombatTuning.DownRecoverySpeed);
            if (Animator == null) return;
            _baseRequested = "GetUp";
            Animator.Play(Animator.StringToHash("GetUp"), CombatTuning.BaseLayer, CombatTuning.DownRecoveryStartPhase);
        }

        void StartSprint(float direction)
        {
            _sprintDirection = direction;
            _sprintTimer = CombatTuning.SprintDuration;
            _sprintCooldown = CombatTuning.SprintCooldown;
            IsBlocking = false;
            IsCrouching = false;
        }

        // Punto unico: o arranca YA, o deja la pulsacion esperando con su reloj.
        //
        // Antes precalculaba "lo que falta para el primer frame accionable + 0.18 s" con
        // tope 0.35 y ese numero se equivocaba: evaluaba `cancelable` ANTES de que el
        // ataque resolviera sus golpes, asi que una pulsacion a media patada se guardaba
        // para el final del clip. Ademas el tope se aplicaba a la suma, no a la espera, y
        // por eso entraban ordenes de hasta 0.35 s despues de pulsadas. Ahora no se predice
        // nada: la pulsacion espera y `ResolvePendingInput` decide frame a frame.
        void QueueOrStartAttack(AttackSpec spec, AttackButton? button = null, bool captureOnly = false)
        {
            if (spec == null) return;
            bool busy = captureOnly || _attack != null || _hurtTimer > 0f || _stunTimer > 0f ||
                        _blockStunTimer > 0f || _jumpActive || _downTimer > 0f ||
                        _recoveryLock > 0f || IsBlocking;
            if (!busy && StartAttack(spec)) return;

            _bufferedAttack = spec;
            _bufferedAge = 0f;
            _bufferedHolds = button.HasValue;
            _bufferedButton = button ?? AttackButton.LightPunch;
            _bufferedReleasedAt = -1f;
        }

        /// <summary>
        /// Arranca un ataque. Devuelve false si el luchador no puede en ESTE frame
        /// (hitstun, recuperacion, suelo, salto...). El valor de retorno importa para el
        /// combo pendiente: antes se descartaba el combo y luego el ataque se rechazaba
        /// en silencio, asi que la secuencia se perdia justo al terminar el golpe
        /// anterior (el candado de recuperacion de 0.10 s).
        /// </summary>
        public bool StartAttack(AttackSpec spec)
        {
            if (spec == null || _attack != null || _hurtTimer > 0f || !IsGrounded || IsBlocking ||
                _stunTimer > 0f || _blockStunTimer > 0f || _jumpActive || _downTimer > 0f ||
                _recoveryLock > 0f) return false;
            _attack = spec;
            _attackTimer = 0f;
            _nextHit = 0;
            _sprintTimer = 0f;
            IsBlocking = false;
            // Un ataque es full-body y se ejecuta de pie: si el jugador venia agachado, se
            // levanta. Dejarlo marcado como agachado mientras el clip esta de pie hacia que
            // la hurtbox y la animacion dijeran cosas distintas.
            IsCrouching = false;
            // Todos los ataques son full-body en Base: la pose la manda un solo clip.
            // La velocidad de reproduccion sale del frame data (AttackSpec.speed).
            SetPlayback(spec.speed);
            PlayBaseState(spec.animatorState, FadeAttack, true);
            RecordAttack(spec);
            Combat?.OnAttackStarted(this, spec);
            return true;
        }

        // ---------------- Traza de ataques (diagnostico) ----------------

        // Ultimos ataques lanzados, con el tiempo de juego. La usa la sonda de combate para
        // comprobar que una pulsacion produce exactamente el ataque que toca.
        readonly List<string> _attackTrace = new List<string>();
        public IReadOnlyList<string> AttackTrace => _attackTrace;
        public void ClearAttackTrace() => _attackTrace.Clear();

        public string DebugComboHistory()
        {
            string s = "";
            for (int i = 0; i < _comboHistoryCount; i++) s += _comboHistory[i];
            return s + $" pending={(_pendingCombo != null ? _pendingCombo.type.ToString() : "-")}";
        }

        void RecordAttack(AttackSpec spec)
        {
            _attackTrace.Add($"{Time.time:F3}|{spec.type}");
            if (_attackTrace.Count > 32) _attackTrace.RemoveAt(0);
        }

        // ---------------- Especial / super ----------------

        // Seleccion DETERMINISTA y ROTATORIA del especial.
        //
        // Antes elegia "el de menor alcance que aun cubre la distancia". Medido en combate:
        // la pelea ocurre casi siempre a 1,5-3 m, la guitarra cubre 2,6 m y por eso LA JEFA
        // PASABA TODO EL COMBATE CON EL PODER DE ALEX LLORA, que es justo lo que el jugador
        // espera ver rotar. El criterio era correcto para un luchador con un solo poder y
        // degenerado para quien tiene seis.
        //
        // Ahora recorre la lista disponible desde un cursor y toma el PRIMERO que cubre la
        // distancia, asi que los va turnando; y solo si ninguno llega usa el de mayor alcance.
        //
        // IMPORTANTE: estas funciones son PURAS. `SpecialRange` y `CanCastSpecial` las llaman
        // desde sus getters y la IA las consulta cada frame; si el cursor avanzara aqui, la
        // simple consulta se comeria el turno y el reparto dejaria de ser predecible. El
        // cursor solo avanza cuando el especial SE DE VERDAD (StartSpecial).
        public int SpecialCursor => _specialCursor;

        public void ResetSpecialCursor() => _specialCursor = 0;

        // Version PURA y con distancia explicita: la consulta la sonda del banco de pruebas
        // sin mover el cursor ni depender de donde este el rival de verdad.
        public SpecialDef SelectSpecialAt(float distance)
        {
            int i = SelectSpecialIndexAt(distance);
            return i >= 0 ? AvailableSpecial(i) : null;
        }

        public int SelectSpecialIndexAt(float distance)
        {
            if (Def == null) return -1;
            int count = AvailableSpecialCount;
            if (count <= 0) return -1;
            if (count == 1) return 0;
            for (int k = 0; k < count; k++)
            {
                int i = (_specialCursor + k) % count;
                var s = AvailableSpecial(i);
                if (s == null || !s.IsCombatSpecial) continue;
                if (s.range >= distance - 0.1f) return i;
            }
            // Nada alcanza: el de mayor alcance, para no quedarse con los brazos cruzados.
            int farthest = -1;
            float farthestRange = -1f;
            for (int i = 0; i < count; i++)
            {
                var s = AvailableSpecial(i);
                if (s == null || !s.IsCombatSpecial) continue;
                if (s.range > farthestRange) { farthestRange = s.range; farthest = i; }
            }
            return farthest;
        }

        int SelectSpecialIndex()
        {
            float dist = Opponent != null
                ? Mathf.Abs(Opponent.transform.position.x - transform.position.x)
                : 0f;
            return SelectSpecialIndexAt(dist);
        }

        SpecialDef SelectSpecial()
        {
            int i = SelectSpecialIndex();
            return i >= 0 ? AvailableSpecial(i) : null;
        }

        public void StartSpecial()
        {
            StartSpecial(-1);
        }

        // forcedIndex >= 0 salta la seleccion por distancia: lo usa el banco de pruebas
        // para verificar cada poder de la jefa sin depender de la posicion. El indice es de la
        // lista DISPONIBLE (para un personaje sin poderes absorbidos es identico al del JSON).
        public void StartSpecial(int forcedIndex)
        {
            if (!CanCastSpecial) return;
            int chosen = forcedIndex >= 0 && Def != null ? forcedIndex : SelectSpecialIndex();
            var special = chosen >= 0 ? AvailableSpecial(chosen) : null;
            if (special == null) return;
            // El turno avanza SOLO si el especial sale: si se rechaza, el siguiente intento
            // prueba con el mismo poder y no se come un turno por la nada.
            if (forcedIndex < 0)
            {
                int count = AvailableSpecialCount;
                if (count > 0) _specialCursor = (chosen + 1) % count;
            }
            _activeSpecial = special;
            _specialTimer = 0f;
            _specialReleased = false;
            _specialHit = false;
            _clubHitIndex = 0;
            _superMeter = 0f;
            _sprintTimer = 0f;
            IsBlocking = false;
            IsCrouching = false;
            _bufferedAttack = null;
            _bufferedAge = 0f;
            _bufferedHolds = false;
            _pendingCombo = null;
            _pendingComboAge = 0f;
            _moveSignal = 0f;
            SetPlayback(1f);
            PlayBaseState(special.animatorState, FadeAttack, true);
            SpawnPropForSpecial(special);
            // Con prop NO hay orbe de plasma: la carga es del propio instrumento.
            // En el slam tampoco: el especial es una carrera y un salto, un orbe flotando
            // entre las manos mientras corre se lee como otro personaje. La carga la cuenta
            // la propia animacion y el aura de super.
            if (_prop != null) _instrumentFx = KomSpecialFx.CreateInstrumentCharge(this, special, _prop);
            else if (!string.Equals(special.type, "slam", System.StringComparison.Ordinal))
                _chargeFx = KomSpecialFx.CreateCharge(this, special);
            if (_superAura != null)
            {
                // Con prop el aura baja: el contorno HDR se comia la guitarra/acordeon.
                _superAura.SetIntensityScale(string.IsNullOrEmpty(special.propResource) ? 1f : 0.22f);
                _superAura.SetCharged(true);
            }
            KomSpecialDiag.Log($"START|{CharacterId}|{special.type}|state={special.animatorState}|duration={special.duration:F2}|prop={special.propResource}");
            Combat?.OnSpecialStarted(this, special);
        }

        // El prop nace con el especial: la guitarra se cuelga de la mano (agarre
        // calibrado) y el acordeon se sostiene entre las palmas hasta el lanzamiento.
        void SpawnPropForSpecial(SpecialDef s)
        {
            DestroyProp();
            if (string.IsNullOrEmpty(s.propResource)) return;
            _prop = KomPropGrip.Spawn(s.propResource, null);
            if (_prop == null) return;
            if (string.Equals(s.type, "club", System.StringComparison.Ordinal))
            {
                KomPropGrip.AttachGuitar(_prop, _handLeft, _fingerLeft, _chest, transform);
                _clubFx = KomSpecialFx.CreateClubFx(this, s, _prop);
            }
            else if (string.Equals(s.type, "wave", System.StringComparison.Ordinal))
            {
                // El microfono va a la mano DERECHA: canta con el y la izquierda queda
                // libre para abrirse y lanzar la onda.
                KomPropGrip.AttachMic(_prop, _handRight, _fingerRight);
            }
            else
            {
                KomPropGrip.PlaceBetweenHands(_prop, RightPalmPosition, LeftPalmPosition,
                    transform.forward, s.propScale);
                KomSpecialDiag.Log($"PROP_SPAWN|{CharacterId}|R={Fmt(RightPalmPosition)}|L={Fmt(LeftPalmPosition)}|prop={Fmt(_prop.transform.position)}|scale={_prop.transform.lossyScale.x:F1}");
            }
        }

        void DestroyProp()
        {
            if (_clubFx != null) { KomFx.SafeDestroy(_clubFx.gameObject); _clubFx = null; }
            if (_prop != null) { KomFx.SafeDestroy(_prop); _prop = null; }
        }

        void UpdateSpecial(float dt)
        {
            if (_activeSpecial == null) return;
            var s = _activeSpecial;
            _specialTimer += dt;

            // La liberacion se dispara con el tiempo NORMALIZADO del Animator, no con
            // un timer paralelo. Un hitch de frame al iniciar el estado puede hacer que
            // el timer avance mas que la pose; anclar el release a la animacion evita
            // que el rayo/cadena salga en el frame equivocado.
            float progress = _specialTimer / Mathf.Max(0.01f, s.duration);
            // Reloj del ESTADO en segundos (no del clip). `AnimatorStateInfo.length` YA viene
            // escalada por la velocidad del estado: Two Hand Club Combo mide 5.43 s de clip
            // pero el estado lo reproduce a 1.70x y dura 3.20 s, que es justo el `duration`
            // declarado. Los hitTimes se declaran en esta misma unidad.
            float stateLength = Mathf.Max(0.01f, s.duration);
            if (Animator != null)
            {
                var st = Animator.GetCurrentAnimatorStateInfo(CombatTuning.BaseLayer);
                if (st.IsName(s.animatorState))
                {
                    // `normalizedTime` se congela con el hitstop: es el reloj correcto.
                    progress = Mathf.Clamp01(st.normalizedTime);
                    // `length` es clip/speed: con speed 0 (hitstop) Unity la devuelve
                    // INFINITA y el segundo golpe se disparaba en el mismo frame que el
                    // primero. Se conserva la ultima longitud valida (o `duration`).
                    if (st.length > 0.01f && !float.IsInfinity(st.length) && !float.IsNaN(st.length))
                        stateLength = st.length;
                }
            }
            float clipTime = progress * stateLength;
            float releaseAt = s.releaseTime / stateLength;

            // El acordeon se sostiene entre las manos hasta el lanzamiento.
            if (_prop != null && !_specialReleased &&
                string.Equals(s.type, "boomerang", System.StringComparison.Ordinal))
            {
                KomPropGrip.PlaceBetweenHands(_prop, RightPalmPosition, LeftPalmPosition,
                    transform.forward, s.propScale);
            }

            // El VFX de carga sigue el progreso REAL de la animacion.
            float chargeProgress = progress / Mathf.Max(0.0001f, releaseAt);
            if (_chargeFx != null) _chargeFx.SetProgress(chargeProgress);
            if (_instrumentFx != null) _instrumentFx.SetProgress(chargeProgress);

            if (!_specialReleased && progress >= releaseAt)
            {
                _specialReleased = true;
                ReleaseSpecial(s);
            }

            if (string.Equals(s.type, "chain", System.StringComparison.Ordinal) &&
                _specialReleased && !_specialHit &&
                progress >= (s.releaseTime + s.extendTime) / Mathf.Max(0.01f, s.duration))
            {
                _specialHit = true;
                ResolveChainImpact(s);
            }

            // La guitarra golpea en los tiempos REALES del clip (hitTimes del JSON):
            // el impacto cae en el frame en que el mastil llega abajo.
            if (string.Equals(s.type, "club", System.StringComparison.Ordinal))
            {
                while (_clubHitIndex < s.HitCount && clipTime >= ClubHitTime(s, _clubHitIndex))
                {
                    ResolveClubHit(s, _clubHitIndex, clipTime, stateLength, progress);
                    _clubHitIndex++;
                }
            }

            if (progress >= 0.999f || _specialTimer >= s.duration + 0.75f) EndSpecial();
        }

        static float ClubHitTime(SpecialDef s, int index)
        {
            if (s.hitTimes != null && index < s.hitTimes.Length) return s.hitTimes[index];
            return s.releaseTime;
        }

        void ReleaseSpecial(SpecialDef s)
        {
            if (_chargeFx != null)
            {
                _chargeFx.Release();
                _chargeFx = null;
            }
            if (_instrumentFx != null)
            {
                _instrumentFx.Release();
                _instrumentFx = null;
            }
            if (string.Equals(s.type, "beam", System.StringComparison.Ordinal))
            {
                Vector3 origin = HandMidPosition;
                Vector3 dir = new Vector3(_facing, 0f, 0f);
                _projectileFx = KomSpecialFx.SpawnProjectile(this, s, origin, dir);
                var st = Animator != null ? Animator.GetCurrentAnimatorStateInfo(CombatTuning.BaseLayer) : default;
                KomSpecialDiag.Log($"BEAM_RELEASE|{CharacterId}|origin={Fmt(origin)}|handMid={Fmt(HandMidPosition)}|dir={Fmt(dir)}|root={Fmt(transform.position)}|facing={_facing:F0}|t={_specialTimer:F3}|animNorm={st.normalizedTime:F3}|animLen={st.length:F2}|animSpeed={st.speed:F2}|animatorSpeed={(Animator != null ? Animator.speed : 0f):F2}");
                Combat?.OnSpecialReleased(this, s, origin);
            }
            else if (string.Equals(s.type, "wave", System.StringComparison.Ordinal))
            {
                // La onda sale de la palma IZQUIERDA, que es la que se abre en el frame 50
                // del clip. Si saliera del centro del cuerpo, el gesto no seria la causa.
                Vector3 origin = LeftPalmPosition;
                Vector3 dir = new Vector3(_facing, 0f, 0f);
                _waveFx = KomSpecialFx.SpawnSoundWave(this, s, origin, dir);
                KomSpecialDiag.Log($"WAVE_RELEASE|{CharacterId}|origin={Fmt(origin)}|palmL={Fmt(LeftPalmPosition)}|dir={Fmt(dir)}|range={s.range:F2}|speed={s.projectileSpeed:F2}|t={_specialTimer:F3}");
                Combat?.OnSpecialReleased(this, s, origin);
            }
            else if (string.Equals(s.type, "slam", System.StringComparison.Ordinal))
            {
                // El centro de la onda es donde pisa al aterrizar. Sale a ras de suelo y
                // crece: no es un proyectil, es un area.
                Vector3 center = transform.position;
                _slamFx = KomSpecialFx.SpawnGroundSlam(this, s, center);
                var slamSt = Animator != null ? Animator.GetCurrentAnimatorStateInfo(CombatTuning.BaseLayer) : default;
                KomSpecialDiag.Log($"SLAM_RELEASE|{CharacterId}|center={Fmt(center)}|root={Fmt(transform.position)}|range={s.range:F2}|t={_specialTimer:F3}|animNorm={slamSt.normalizedTime:F3}|animLen={slamSt.length:F2}|animSpeed={slamSt.speed:F2}|releaseAt={s.releaseTime / Mathf.Max(0.01f, s.duration):F3}");
                Combat?.OnSpecialReleased(this, s, center);
            }
            else if (string.Equals(s.type, "chain", System.StringComparison.Ordinal))
            {
                Vector3 anchor = RightPalmPosition;
                Vector3 target = ComputeChainTarget(anchor, s);
                _chainFx = KomSpecialFx.SpawnChain(this, s, _handRight, _fingerRight, anchor, target);
                KomSpecialDiag.Log($"CHAIN_RELEASE|{CharacterId}|anchor={Fmt(anchor)}|palm={Fmt(RightPalmPosition)}|target={Fmt(target)}|reach={Vector3.Distance(anchor, target):F3}");
                Combat?.OnSpecialReleased(this, s, anchor);
            }
            else if (string.Equals(s.type, "boomerang", System.StringComparison.Ordinal))
            {
                Vector3 origin = HandMidPosition;
                Vector3 target = ComputeBoomerangTarget(origin, s);
                var prop = _prop;
                _prop = null;
                _boomerangFx = KomSpecialFx.SpawnBoomerang(this, s, prop, origin, target);
                KomSpecialDiag.Log($"BOOMERANG_RELEASE|{CharacterId}|origin={Fmt(origin)}|target={Fmt(target)}|range={s.range:F2}|spin={s.spinDegrees:F0}");
                Combat?.OnSpecialReleased(this, s, origin);
            }
            else if (string.Equals(s.type, "club", System.StringComparison.Ordinal))
            {
                Vector3 body = _prop != null ? KomPropGrip.BodyCenter(_prop) : HandMidPosition;
                KomSpecialFx.MuzzleFlash(body, KomData.SpecialColor(Def, s), 0.9f);
                KomSpecialFx.AuraRing(body, KomData.SpecialColor(Def, s), 1.1f);
                var releaseGrip = _prop != null ? _prop.GetComponent<KomClubGrip>() : null;
                string releaseAim = releaseGrip != null ? $"|aim={Fmt(releaseGrip.Aim)}|palm={Fmt(LeftPalmPosition)}" : "";
                KomSpecialDiag.Log($"CLUB_RELEASE|{CharacterId}|body={Fmt(body)}|hits={s.HitCount}|first={ClubHitTime(s, 0):F2}{releaseAim}");
                Combat?.OnSpecialReleased(this, s, body);
            }
        }

        Vector3 ComputeBoomerangTarget(Vector3 origin, SpecialDef s)
        {
            if (Opponent == null) return origin + new Vector3(_facing, 0f, 0f) * s.range;
            var opp = Opponent;
            Vector3 aim = opp.transform.position + Vector3.up * s.hitboxCenterY;
            Vector3 flat = new Vector3(aim.x - origin.x, 0f, aim.z - origin.z);
            float dist = flat.magnitude;
            Vector3 dir = dist > 0.001f ? flat / dist : new Vector3(_facing, 0f, 0f);
            float reach = Mathf.Min(s.range, dist + opp.CurrentHurtboxRadius * 0.6f);
            return origin + dir * reach;
        }

        // Impacto de la guitarra en el punto REAL del cuerpo del prop.
        void ResolveClubHit(SpecialDef s, int index, float clipTime, float stateLength, float norm)
        {
            Vector3 point = _prop != null
                ? KomPropGrip.BodyEdge(_prop)
                : transform.position + Vector3.up * s.hitboxCenterY + new Vector3(_facing * s.range * 0.45f, 0f, 0f);
            bool hit = ResolveSpecialHit(point, s, false, s.hitDamageScale);
            if (hit && _clubFx != null) _clubFx.Impact(point, 1f);
            string oppInfo = Opponent != null ? $"opp={Fmt(Opponent.transform.position)}|ko={Opponent.IsKO}|dx={Mathf.Abs(point.x - Opponent.transform.position.x):F3}" : "opp=null";
            // Traza del agarre: sin esto no se puede separar "el golpe cae en el frame
            // equivocado" de "el prop apunta a otro lado".
            string gripInfo = "";
            if (_prop != null)
            {
                var grip = _prop.GetComponent<KomClubGrip>();
                if (grip != null)
                    gripInfo = $"|aim={Fmt(grip.Aim)}|palm={Fmt(LeftPalmPosition)}|chest={Fmt(_chest != null ? _chest.position : Vector3.zero)}";
            }
            var stHit = Animator != null ? Animator.GetCurrentAnimatorStateInfo(CombatTuning.BaseLayer) : default;
            KomSpecialDiag.Log($"CLUB_HIT|{CharacterId}|i={index}|point={Fmt(point)}|prop={_prop != null}|{oppInfo}{gripInfo}" +
                               $"|t={clipTime:F2}/{stateLength:F2}|norm={norm:F3}|stNorm={stHit.normalizedTime:F3}|stLen={stHit.length:F2}|stSpeed={stHit.speed:F2}|hit={hit}");
        }

        public void OnPropReturned(Vector3 at)
        {
            KomSpecialDiag.Log($"PROP_RETURN|{CharacterId}|at={Fmt(at)}");
        }

        Vector3 ComputeChainTarget(Vector3 anchor, SpecialDef s)
        {
            if (Opponent == null) return anchor + new Vector3(_facing, 0f, 0f) * s.range;
            var opp = Opponent;
            Vector3 aim = opp.transform.position + Vector3.up * s.hitboxCenterY;
            aim.z = Mathf.Lerp(anchor.z, opp.transform.position.z, 0.5f);
            Vector3 flat = new Vector3(aim.x - anchor.x, 0f, aim.z - anchor.z);
            float dist = flat.magnitude;
            Vector3 dir = dist > 0.001f ? flat / dist : new Vector3(_facing, 0f, 0f);
            float reach = Mathf.Min(s.range, dist + opp.CurrentHurtboxRadius * 0.35f);
            Vector3 target = anchor + dir * reach;
            target.y = Mathf.Clamp(aim.y, anchor.y - 0.4f, anchor.y + 0.55f);
            return target;
        }

        void ResolveChainImpact(SpecialDef s)
        {
            Vector3 tip = _chainFx != null ? _chainFx.CurrentTip : RightPalmPosition + new Vector3(_facing, 0f, 0f) * s.range;
            float anchorError = _chainFx != null ? _chainFx.AnchorError : -1f;
            float spacingError = _chainFx != null ? _chainFx.LinkSpacingError : -1f;
            int activeLinks = _chainFx != null ? _chainFx.ActiveLinks : -1;
            float reach = Vector3.Distance(RightPalmPosition, tip);
            // Gate numerico: la cadena nace en la palma, los eslabones van a paso
            // constante y la punta no excede el alcance declarado.
            bool anchorOk = anchorError >= 0f && anchorError < 0.001f;
            bool spacingOk = spacingError >= 0f && spacingError < 0.002f;
            bool reachOk = reach <= s.range + 0.25f;
            bool hit = ResolveSpecialHit(tip, s, false);
            if (hit)
            {
                if (_chainFx != null) _chainFx.PlayImpact();
                KomSpecialFx.Impact(tip, KomData.SpecialColor(Def), 0.85f);
            }
            KomSpecialDiag.Log($"CHAIN_IMPACT|{CharacterId}|tip={Fmt(tip)}|anchorErr={anchorError:F5}|links={activeLinks}|spacingErr={spacingError:F5}|reach={reach:F3}/{s.range:F2}|anchorOk={anchorOk}|spacingOk={spacingOk}|reachOk={reachOk}|hit={hit}");
        }

        void EndSpecial()
        {
            if (_activeSpecial == null) return;
            string type = _activeSpecial.type;
            _activeSpecial = null;
            _specialTimer = 0f;
            _specialReleased = false;
            _specialHit = false;
            _clubHitIndex = 0;
            if (_chargeFx != null) { KomFx.SafeDestroy(_chargeFx.gameObject); _chargeFx = null; }
            if (_instrumentFx != null) { KomFx.SafeDestroy(_instrumentFx.gameObject); _instrumentFx = null; }
            DestroyProp();
            if (_superAura != null)
            {
                _superAura.SetIntensityScale(1f);
                _superAura.SetCharged(SuperReady);
            }
            SetPlayback(1f);
            PlayBaseState(DesiredBaseState(), FadeAttackExit);
            KomSpecialDiag.Log($"END|{CharacterId}|{type}");
        }

        public void CancelSpecial(bool reset)
        {
            if (_chargeFx != null) { KomFx.SafeDestroy(_chargeFx.gameObject); _chargeFx = null; }
            if (_instrumentFx != null) { KomFx.SafeDestroy(_instrumentFx.gameObject); _instrumentFx = null; }
            if (_chainFx != null)
            {
                KomFx.SafeDestroy(_chainFx.gameObject);
                _chainFx = null;
            }
            if (reset && _waveFx != null)
            {
                KomFx.SafeDestroy(_waveFx.gameObject);
                _waveFx = null;
            }
            if (reset && _slamFx != null)
            {
                KomFx.SafeDestroy(_slamFx.gameObject);
                _slamFx = null;
            }
            if (reset && _projectileFx != null)
            {
                KomFx.SafeDestroy(_projectileFx.gameObject);
                _projectileFx = null;
            }
            if (reset && _boomerangFx != null)
            {
                KomFx.SafeDestroy(_boomerangFx.gameObject);
                _boomerangFx = null;
            }
            DestroyProp();
            if (_superAura != null) _superAura.SetIntensityScale(1f);
            if (_activeSpecial != null)
            {
                KomSpecialDiag.Log($"CANCEL|{CharacterId}|{_activeSpecial.type}|reset={reset}");
                _activeSpecial = null;
            }
            _specialTimer = 0f;
            _specialReleased = false;
            _specialHit = false;
            _clubHitIndex = 0;
        }

        public bool ResolveSpecialHit(Vector3 point, SpecialDef spec, bool projectile, float damageScale = 1f)
        {
            if (spec == null || Opponent == null || Opponent.IsKO) return false;
            var opp = Opponent;
            float horizontal = spec.hitboxWidth * 0.5f + opp.CurrentHurtboxRadius;
            if (Mathf.Abs(point.x - opp.transform.position.x) > horizontal) return false;
            float bottom = point.y - spec.hitboxHeight * 0.5f;
            float top = point.y + spec.hitboxHeight * 0.5f;
            float hurtBottom = opp.HurtboxBottom;
            float hurtTop = hurtBottom + opp.CurrentHurtboxHeight;
            if (top <= hurtBottom || bottom >= hurtTop) return false;
            if (opp.IsCrouching && spec.high) return false;

            bool blocked = opp.IsBlocking;
            float damage = spec.damage * (Def != null ? Def.damageScale : 1f) *
                           Mathf.Max(0.05f, damageScale) * (blocked ? 0.2f : 1f);
            var attack = spec.BuildAttackSpec();
            opp.ReceiveHit(this, attack, blocked, damage);
            Combat?.OnSpecialHit(this, opp, spec, blocked, point);
            AddSuper(damage * CombatTuning.SuperGainDealtPerDamage * (blocked ? 0.5f : 1f));
            KomSpecialDiag.Log($"HIT|{CharacterId}|{spec.type}|projectile={projectile}|point={Fmt(point)}|blocked={blocked}|damage={damage:F2}|oppHurt=[{opp.HurtboxBottom:F2},{opp.HurtboxBottom + opp.CurrentHurtboxHeight:F2}]");
            return true;
        }

        // Golpe de la onda de suelo: el frente es un radio que crece, asi que el impacto
        // depende de la DISTANCIA al centro, no de una caja orientada. Se usa el radio real
        // del frame; si el rival salta, la comprobacion vertical de ResolveSpecialHit lo
        // salva (una onda rasante no alcanza a quien esta en el aire).
        public bool ResolveSlamHit(SpecialDef spec, Vector3 center, float radius, float band)
        {
            if (spec == null || Opponent == null || Opponent.IsKO) return false;
            var opp = Opponent;
            Vector3 d = opp.transform.position - center;
            d.y = 0f;
            float dist = d.magnitude;
            float reach = band + opp.CurrentHurtboxRadius;
            if (dist > radius + reach) return false;
            if (dist < radius - reach) return false;
            Vector3 point = opp.transform.position + Vector3.up * spec.hitboxCenterY;
            return ResolveSpecialHit(point, spec, false, spec.hitDamageScale);
        }

        public void AddSuper(float amount)
        {
            if (amount <= 0f || IsKO) return;
            bool wasReady = SuperReady;
            _superMeter = Mathf.Clamp01(_superMeter + amount);
            if (!wasReady && SuperReady)
            {
                KomSpecialDiag.Log($"SUPER_READY|{CharacterId}");
                Combat?.OnSuperReady(this);
            }
        }

        public void FillSuperDebug() => AddSuper(CombatTuning.SuperFull);

        /// <summary>
        /// Vacía el medidor para una prueba. El medidor se conserva entre rondas por
        /// diseño; una sonda que arranca a mitad de combate heredaría el súper lleno y
        /// la IA pasaría la ventana lanzando especiales en vez de atacar.
        /// </summary>
        public void DevResetSuper()
        {
            _superMeter = 0f;
            if (_superAura != null) _superAura.SetCharged(false);
        }

        bool _devInvulnerable;

        /// <summary>
        /// Sondas de editor: absorbe el golpe (stun, medidor y guard damage incluidos) pero
        /// no puede llegar al K.O. Sirve para medir una ronda entera sin que el bando
        /// observador se muera a mitad.
        /// </summary>
        public void DevSetInvulnerable(bool on) => _devInvulnerable = on;

        /// <summary>
        /// Ignora el teclado sin apagar el control. Lo usa la captura del escaparate: los
        /// especiales exigen `ControlsEnabled`, pero una tecla suelta durante la grabacion
        /// arrancaria un ataque y arruinaria el clip.
        /// </summary>
        public bool DevIgnoreInput { get; set; }

        // ---------------- Recolectables ----------------

        // Curacion por concha. Devuelve lo realmente curado (0 si estaba al maximo), que
        // es lo que se rotula en pantalla.
        public float Heal(float amount)
        {
            if (amount <= 0f || IsKO) return 0f;
            float max = Def != null ? Def.maxHealth : 100f;
            float before = Health;
            Health = Mathf.Min(max, Health + amount);
            return Health - before;
        }

        // Recarga de estamina (la barra que gasta bloquear). Devuelve lo recuperado.
        public float RestoreGuard(float fraction)
        {
            if (fraction <= 0f || IsKO) return 0f;
            float before = _guardMeter;
            _guardMeter = Mathf.Clamp01(_guardMeter + fraction);
            // Recargar quita el castigo por bloqueo: se puede volver a levantar guardia.
            _guardRegenDelay = 0f;
            return _guardMeter - before;
        }

        static string Fmt(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

        void UpdateAttack(float dt)
        {
            float previous = _attackTimer;
            _attackTimer += dt;

            // Avance del ataque. La ventana y la velocidad se escalan juntas, de modo
            // que el desplazamiento TOTAL sigue siendo el declarado aunque el clip se
            // reproduzca mas rapido. Se integra el SOLAPE [previous, _attackTimer] con
            // la ventana, no el frame entero: si un hitch hace que el frame cruce el
            // final de la ventana, el tramo que cae dentro se cobra igual. Antes se
            // perdia ese tramo y Combo3 avanzaba 1.23 m en vez de 1.49.
            if (_attack.lunge > 0f)
            {
                float stepStart = previous;
                float stepEnd = Mathf.Min(_attackTimer, _attack.LungeWindow);
                if (stepEnd > stepStart)
                {
                    var pos = transform.position;
                    pos.x += _facing * _attack.lunge * _attack.speed * (stepEnd - stepStart);
                    transform.position = pos;
                }
            }

            // Ventanas de impacto: 1 para la mayoria, 4 para el combo QuadPunch.
            // Se compara el INTERVALO [previous, _attackTimer] contra la ventana y no
            // un instante suelto: con un hitch de frame el golpe ya no se pierde (era
            // un "mi ataque lo atravesó" intermitente e imposible de depurar).
            while (_nextHit < _attack.hitCount)
            {
                float windowStart = _attack.HitTimeAt(_nextHit);
                float windowEnd = windowStart + _attack.ActiveWindow;
                if (_attackTimer < windowStart) break;
                // Se consume el impacto ANTES de resolverlo: si un hook de VFX lanza
                // una excepcion, el mismo golpe no se vuelve a cobrar en el siguiente
                // frame (eso era el "muere de un golpe" del build sin shaders).
                _nextHit++;
                if (previous < windowEnd) ResolveHit(_attack);
            }

            if (_attackTimer >= _attack.Window)
            {
                _attack = null;
                _nextHit = 0;
                _cancelChain = 0;
                _recoveryLock = CombatTuning.AttackRecoveryLock;
                SetPlayback(1f);
                // El clip de ataque termina en guardia, no en Idle: sin este fundido
                // CADA golpe acababa con un salto de pose visible.
                PlayBaseState(IsCrouching ? "Crouch" : DesiredBaseState(), FadeAttackExit);
            }
        }

        // Ventana de golpe activa ahora mismo (para el depurador de hitboxes).
        bool IsHitWindowActive()
        {
            if (_attack == null) return false;
            for (int i = 0; i < _attack.hitCount; i++)
            {
                float start = _attack.HitTimeAt(i);
                if (_attackTimer >= start && _attackTimer <= start + _attack.ActiveWindow) return true;
            }
            return false;
        }

        bool ResolveHit(AttackSpec spec)
        {
            if (Opponent == null || Opponent.IsKO) return false;
            var opp = Opponent;
            float airLimit = spec.antiAir ? 1.6f : 0.4f;
            if (opp.AirHeight > airLimit) return false;
            float hitX = transform.position.x + _facing * spec.hitboxForward;
            float horizontal = spec.hitboxWidth * 0.5f + opp.CurrentHurtboxRadius;
            if (Mathf.Abs(hitX - opp.transform.position.x) > horizontal) return false;

            float hitBottom = transform.position.y + spec.hitboxCenterY - spec.hitboxHeight * 0.5f;
            float hitTop = transform.position.y + spec.hitboxCenterY + spec.hitboxHeight * 0.5f;
            float hurtBottom = opp.HurtboxBottom;
            float hurtTop = hurtBottom + opp.CurrentHurtboxHeight;
            if (hitTop <= hurtBottom || hitBottom >= hurtTop) return false;

            if (opp.IsCrouching && spec.high) return false;
            bool blocked = opp.IsBlocking;
            float damage = spec.damage * (Def != null ? Def.damageScale : 1f) * (blocked ? 0.2f : 1f);
            var point = new Vector3((transform.position.x + opp.transform.position.x) * 0.5f,
                transform.position.y + spec.hitboxCenterY, transform.position.z);
            opp.ReceiveHit(this, spec, blocked, damage);
            Combat?.OnHitLanded(this, opp, spec, blocked, point);
            AddSuper(damage * CombatTuning.SuperGainDealtPerDamage * (blocked ? 0.5f : 1f));
            return true;
        }

        public void ReceiveHit(Fighter attacker, AttackSpec spec, bool blocked, float damage)
        {
            if (IsKO) return;
            // Un golpe descarta el combo pendiente: no se bufferiza a traves del hitstun.
            _pendingCombo = null;
            Health = Mathf.Max(0f, Health - damage);
            // Sonda: el modo invulnerable registra el dano, el golpe, el stun y el medidor
            // (todo lo que la IA observa para decidir) pero no deja que la ronda se corte por
            // K.O. Sin esto el banco del jefe moria en el segundo golpe del guitarrazo y la
            // fase de estres del instrumento nunca se ejecutaba.
            if (_devInvulnerable && Health <= 0f) Health = Def != null ? Def.maxHealth : 100f;
            AddSuper(damage * (blocked ? CombatTuning.SuperGainBlockedPerDamage : CombatTuning.SuperGainTakenPerDamage));
            float dir = Mathf.Sign(transform.position.x - attacker.transform.position.x);
            if (dir == 0f) dir = -_facing;

            if (blocked)
            {
                _guardRegenDelay = CombatTuning.GuardRegenDelay;
                float guardDamage = spec.guardDamage * (attacker.Def != null ? attacker.Def.damageScale : 1f);
                _guardMeter = Mathf.Max(0f, _guardMeter - guardDamage);
                // Blockstun: bloquear deja de ser gratis. El empuje entra como
                // velocidad (no como teletransporte) y el defensor queda clavado el
                // tiempo justo para que el intercambio se lea.
                _knockVel = dir * spec.knockback * CombatTuning.KnockbackImpulse * CombatTuning.BlockPushScale;
                _blockStunTimer = Mathf.Max(_blockStunTimer,
                    spec.heavy ? CombatTuning.BlockStunHeavy : CombatTuning.BlockStunLight);
                _moveVel = 0f;
                if (_guardMeter <= 0f) BreakGuard(attacker, spec);
                else if (_shield != null) _shield.Flash(spec.heavy ? 1f : 0.62f);
            }
            else
            {
                // Un golpe limpio interrumpe el especial: la energia se consume y el
                // proyectil ya lanzado sigue su curso, pero la pose vuelve a la reaccion.
                if (_activeSpecial != null) CancelSpecial(false);

                string reaction = string.IsNullOrEmpty(spec.hitReaction)
                    ? (spec.heavy ? "HitHeavy" : "Hit")
                    : spec.hitReaction;
                // Un ataque multiple (QuadPunch, combos) golpea cada 0.10 s: reiniciar
                // el clip en cada impacto producia un tic visible. Si la reaccion ya
                // esta en curso se refresca el tiempo y NO se reinicia la pose.
                bool alreadyReacting = _hurtTimer > 0.02f && _hitState == reaction;

                _attack = null;
                _nextHit = 0;
                _cancelChain = 0;
                _bufferedAttack = null;
                _sprintTimer = 0f;
                _blockStunTimer = 0f;
                _hurtTimer = spec.hitStun > 0.01f
                    ? spec.hitStun
                    : (spec.heavy ? CombatTuning.HitHeavyDuration : CombatTuning.HitLightDuration);
                IsBlocking = false;
                IsCrouching = false;
                if (Animator != null) Animator.SetLayerWeight(CombatTuning.ReservedLayer, 0f);
                _knockVel = dir * spec.knockback * CombatTuning.KnockbackImpulse;
                _moveVel = 0f;
                _facing = attacker.transform.position.x >= transform.position.x ? 1f : -1f;
                ApplyFacing(true);
                // La reaccion se reproduce rapida: el clip de Mixamo es un flinch de
                // 1.3-3.5 s con cola muerta, y el hitstun es mucho mas corto. A 1x el
                // golpe se leia lento y el corte a mitad de clip saltaba a la vista.
                _hitState = reaction;
                _reactionExit = false;
                SetPlayback(ReactionSpeed(reaction));
                PlayBaseState(reaction, FadeHit, alreadyReacting == false);
            }

            if (Health <= 0f) TriggerKO();
        }

        static float ReactionSpeed(string state)
        {
            switch (state)
            {
                case "HitHeavy": return CombatTuning.HitHeavyReactionSpeed;
                case "GroinHit": return CombatTuning.GroinReactionSpeed;
                case "Stunned": return CombatTuning.StunnedReactionSpeed;
                default: return CombatTuning.HitReactionSpeed;
            }
        }

        void BreakGuard(Fighter attacker, AttackSpec spec)
        {
            _guardMeter = 0f;
            _stunTimer = CombatTuning.GuardBreakStun;
            _hurtTimer = 0f;
            _blockStunTimer = 0f;
            _attack = null;
            _nextHit = 0;
            _bufferedAttack = null;
            _sprintTimer = 0f;
            IsBlocking = false;
            IsCrouching = false;
            float dir = Mathf.Sign(transform.position.x - attacker.transform.position.x);
            if (dir == 0f) dir = -_facing;
            // Empuje como velocidad, igual que el resto de impactos.
            _knockVel = dir * (spec.knockback * 0.25f + CombatTuning.GuardBreakPushback) * CombatTuning.KnockbackImpulse;
            _moveVel = 0f;
            _hitState = "Stunned";
            _reactionExit = false;
            SetPlayback(CombatTuning.StunnedReactionSpeed);
            PlayBaseState("Stunned", FadeHit, true);
            // La guardia se rompe: el escudo estalla en vez de apagarse.
            if (_shield != null) _shield.Shatter();
            Combat?.OnGuardBreak(this);
        }

        /// <summary>
        /// Herramienta de desarrollo: KO inmediato sin pasar por el calculo de dano. La usa
        /// el menu de desarrollo para cerrar una ronda sin pelear.
        /// </summary>
        public void DevKill()
        {
            if (IsKO) return;
            Health = 0f;
            TriggerKO();
        }

        // Presentacion post-KO: el perdedor se levanta antes de resetear la ronda.
        // El Fighter esta KO, pero el Animator sigue reproduciendo el estado.
        public void PlayGetUp()
        {
            if (IsKO == false) return;
            _stunTimer = 0f;
            _hurtTimer = 0f;
            _attack = null;
            _bufferedAttack = null;
            _sprintTimer = 0f;
            IsBlocking = false;
            IsCrouching = false;
            if (Animator != null)
            {
                Animator.SetLayerWeight(CombatTuning.ReservedLayer, 0f);
                SetPlayback(1f);
                // Getting Up son 8.6 s: se entra al 23 % (ya tumbado) y el estado va a 4x,
                // de modo que en ~1.15 s esta de pie sin crear un clip derivado enorme.
                _baseRequested = "GetUp";
                Animator.Play(Animator.StringToHash("GetUp"), CombatTuning.BaseLayer, 0.23f);
            }
        }

        void TriggerKO()
        {
            if (IsKO) return;
            IsKO = true;
            ControlsEnabled = false;
            CancelSpecial(true);
            _attack = null;
            _nextHit = 0;
            _bufferedAttack = null;
            _sprintTimer = 0f;
            _stunTimer = 0f;
            _blockStunTimer = 0f;
            _jumpTimer = 0f;
            _jumpActive = false;
            _jumpLaunched = false;
            _downTimer = 0f;
            _recoveryLock = 0f;
            _cancelChain = 0;
            if (_shield != null) _shield.Reset();
            // Un KO a mitad de hitstop dejaria el Animator a velocidad 0 y el clip de
            // KO nunca se reproduciria: se limpia siempre.
            _freezeTimer = 0f;
            _knockVel = 0f;
            _moveVel = 0f;
            _hitState = string.Empty;
            SetPlayback(1f);
            IsBlocking = false;
            IsCrouching = false;
            if (Animator != null) Animator.SetLayerWeight(CombatTuning.ReservedLayer, 0f);
            if (_superAura != null) _superAura.SetCharged(false);
            PlayBaseState("KO", FadeKO, true);
            Combat?.OnFighterKO(this);
        }

        public void PlayVictory()
        {
            _victory = true;
            CancelSpecial(true);
            IsBlocking = false;
            IsCrouching = false;
            ControlsEnabled = false;
            _attack = null;
            _bufferedAttack = null;
            _sprintTimer = 0f;
            _stunTimer = 0f;
            _jumpTimer = 0f;
            _jumpActive = false;
            _jumpLaunched = false;
            _downTimer = 0f;
            _recoveryLock = 0f;
            _cancelChain = 0;
            if (_shield != null) _shield.Reset();
            if (Animator != null) Animator.SetLayerWeight(CombatTuning.ReservedLayer, 0f);
            // AAA: victoria full-body en Base con entrada suave.
            SetPlayback(1f);
            PlayBaseState("Victory", FadeLocomotion, true);
        }

        void UpdatePhysics(float dt)
        {
            if (_airHeight > 0f || _verticalVelocity != 0f)
            {
                _verticalVelocity -= Gravity * dt;
                _airHeight += _verticalVelocity * dt;
                if (_airHeight <= 0f)
                {
                    _airHeight = 0f;
                    _verticalVelocity = 0f;
                    // Durante el salto manda el tramo de aterrizaje del clip: aqui no se
                    // cambia de estado, lo hace UpdateJump al acabar la ventana.
                    if (IsKO == false && !_jumpActive) PlayBaseState(DesiredBaseState(), FadeLocomotion);
                }
            }

            var pos = transform.position;

            // Rampa de desplazamiento: se acerca al objetivo con aceleracion y frena
            // con deceleracion. Nadie pasa de 0 a tope en un frame.
            float rate = Mathf.Abs(_moveTarget) > Mathf.Abs(_moveVel)
                ? CombatTuning.MoveAccel
                : CombatTuning.MoveDecel;
            _moveVel = Mathf.MoveTowards(_moveVel, _moveTarget, rate * dt);
            _moveTarget = 0f;   // moverse se declara cada frame; si no, se frena
            pos.x += _moveVel * dt;

            // Empuje recibido: velocidad que decae. Sustituye al salto instantaneo,
            // que era el defecto mas visible de un golpe conectado.
            //
            // Se integra la exponencial EXACTA, no `v * dt`. Con Euler explicito un
            // frame largo (editor a pocos fps, hitch de carga) aplicaba todo el impulso
            // en un solo paso: medido en la sonda, un LightKick de 0.7 mandaba al rival
            // 1.5 m en vez de 0.38 y el Combo3 ya no lo alcanzaba. La integral
            // v0 * (1 - e^-k dt) / k da el mismo recorrido total a 30, 60 o 144 fps.
            if (Mathf.Abs(_knockVel) > 0.01f)
            {
                float decay = Mathf.Exp(-CombatTuning.KnockbackDecay * dt);
                pos.x += _knockVel * (1f - decay) / CombatTuning.KnockbackDecay;
                _knockVel *= decay;
                if (Mathf.Abs(_knockVel) < 0.05f) _knockVel = 0f;
            }

            pos.y = _airHeight;
            transform.position = pos;
            ResolveBounds();
        }

        void ResolveBounds()
        {
            float halfWidth = Combat != null ? Combat.ArenaHalfWidth : 6f;
            var pos = transform.position;
            pos.x = Mathf.Clamp(pos.x, -halfWidth + 0.7f, halfWidth - 0.7f);

            if (Opponent != null)
            {
                float dx = Opponent.transform.position.x - pos.x;
                if (Mathf.Abs(dx) < MinSeparation)
                {
                    float dir = dx > 0f ? 1f : -1f;
                    float correction = MinSeparation - Mathf.Abs(dx);
                    pos.x -= dir * correction;
                    pos.x = Mathf.Clamp(pos.x, -halfWidth + 0.7f, halfWidth - 0.7f);
                }
            }
            transform.position = pos;
        }

        // Declara la velocidad deseada. La integracion (rampa + posicion) la hace
        // UpdatePhysics, para que acelerar y frenar tengan el mismo tratamiento.
        void Move(float axis, float speed, float dt)
        {
            _moveTarget = Mathf.Abs(axis) < 0.01f ? 0f : axis * speed;
        }

        void UpdateFacing()
        {
            if (Opponent == null || _victory || IsKO) return;
            if (_attack != null || _hurtTimer > 0f || _stunTimer > 0f || _activeSpecial != null) return;
            float dir = Opponent.transform.position.x >= transform.position.x ? 1f : -1f;
            if (!Mathf.Approximately(dir, _facing))
            {
                _facing = dir;
                ApplyFacing();
            }
        }

        void ApplyFacing(bool force = false)
        {
            if (!force && Mathf.Approximately(_lastAppliedFacing, _facing)) return;
            _lastAppliedFacing = _facing;
            transform.rotation = KomVisual.FaceDirection(_facing, 10f);
        }

        void UpdateAura(float dt)
        {
            // Aura de super: el color SIEMPRE es el del personaje (Accent). Se enciende
            // al llenar la barra y se mantiene durante la ejecucion del especial.
            bool super = SuperReady || IsCastingSpecial;
            if (_superAura != null) _superAura.SetCharged(super);
        }

        // El escudo es el feedback de bloqueo: aparece solo cuando la guardia esta
        // realmente activa (ni en hitstun, ni atacando, ni en el aire, ni rota).
        void UpdateShield(float dt)
        {
            if (_shield == null) return;
            bool on = IsBlocking && _hurtTimer <= 0f && _stunTimer <= 0f && _blockStunTimer <= 0f &&
                      _attack == null && _activeSpecial == null && IsGrounded && !IsKO && !_victory;
            _shield.SetVisible(on, dt);
            _shield.SetGuard(_guardMeter);
        }

        void UpdateAnimationState()
        {
            if (Animator == null || _attack != null || _activeSpecial != null || _hurtTimer > 0f ||
                _stunTimer > 0f || _blockStunTimer > 0f || _downTimer > 0f || IsKO || _victory) return;
            if (_jumpActive || !IsGrounded)
            {
                // La reproduccion del salto se reparte por tramos para que el despegue
                // del clip caiga justo cuando la fisica despega al luchador.
                SetPlayback(JumpPlayback(_jumpTimer));
                PlayBaseState("Jump", FadeLocomotion);
                return;
            }
            if (IsCrouching)
            {
                SetPlayback(1f);
                PlayBaseState("Crouch", FadeLocomotion);
                return;
            }
            // Un solo estado y un solo clip: Idle, WalkGuardFwd/Back, RunFwd/Back o Block.
            // Andar con o sin L usa exactamente el mismo WalkGuard (guardia alta real).
            string desired = DesiredBaseState();
            // La velocidad de reproduccion acompana a la de desplazamiento: el clip de
            // andar/correr se acelera en la MISMA proporcion que el avance, de modo que
            // subir la velocidad no desliza los pies.
            SetPlayback(PlaybackFor(desired));

            var current = Animator.GetCurrentAnimatorStateInfo(CombatTuning.BaseLayer);
            float fade = 0f;
            if (_reactionExit)
            {
                // Salida de hitstun: el clip de reaccion se corta antes de su cola y sin
                // este fundido saltaba a la pose neutra de golpe.
                fade = FadeHitOut;
                _reactionExit = false;
                _hitState = string.Empty;
            }
            else
            {
                bool currentIdle = current.IsName("Idle");
                bool currentWalk = current.IsName("WalkGuardFwd") || current.IsName("WalkGuardBack");
                bool currentRun = current.IsName("RunFwd") || current.IsName("RunBack");
                bool currentCrouch = current.IsName("Crouch");
                bool desiredWalk = desired == "WalkGuardFwd" || desired == "WalkGuardBack";
                bool desiredRun = desired == "RunFwd" || desired == "RunBack";
                if ((currentIdle && desiredWalk) || (currentWalk && desired == "Idle"))
                    fade = CombatTuning.FadeIdleWalk;
                else if (currentRun != desiredRun && (currentRun || desiredRun))
                    fade = FadeRun;
                // Agacharse es un cambio de altura grande: sin fundido, levantarse de
                // golpe desde la pose baja se lee como un salto.
                else if (currentCrouch != (desired == "Crouch") && (currentCrouch || desired == "Crouch"))
                    fade = CombatTuning.FadeCrouch;
            }
            PlayBaseState(desired, fade);
        }

        // Velocidad de reproduccion de cada estado de locomocion. Es el MISMO factor
        // que se aplica al desplazamiento en ApplyIntent: por eso no hay patinaje.
        static float PlaybackFor(string state)
        {
            switch (state)
            {
                case "WalkGuardFwd":
                case "WalkGuardBack":
                    return CombatTuning.WalkSpeedScale;
                case "RunFwd":
                case "RunBack":
                    return CombatTuning.DashSpeedScale;
                case "Block":
                    // Guardia quieta: el clip avanza casi congelado. El feedback de
                    // bloqueo lo da el escudo, no la animacion.
                    return CombatTuning.BlockHoldSpeed;
                default:
                    return 1f;
            }
        }

        string DesiredBaseState()
        {
            if (_sprintTimer > 0f) return SignedMovement() >= 0f ? "RunFwd" : "RunBack";
            if (Mathf.Abs(_moveSignal) > 0.12f)
                return SignedMovement() >= 0f ? "WalkGuardFwd" : "WalkGuardBack";
            return IsBlocking ? "Block" : "Idle";
        }

        float SignedMovement()
        {
            if (Mathf.Abs(_moveSignal) < 0.01f) return 1f;
            return Mathf.Sign(_moveSignal) * _facing;
        }

        void EnforceReservedLayer()
        {
            // AAA: la capa reservada se mantiene a 0 permanente. Red de seguridad: si algo
            // la subiera, se volveria a mezclar una segunda pose encima de la Base.
            if (Animator == null || Animator.layerCount <= CombatTuning.ReservedLayer) return;
            if (Animator.GetLayerWeight(CombatTuning.ReservedLayer) != 0f)
                Animator.SetLayerWeight(CombatTuning.ReservedLayer, 0f);
        }

        void PlayBaseState(string state, float fade = 0f, bool restart = false)
        {
            if (Animator == null || string.IsNullOrEmpty(state)) return;
            // AAA: autoridad unica. Antes de cualquier cambio, la capa reservada queda a 0
            // para que jamas se cuele un frame de mezcla parcial.
            if (Animator.GetLayerWeight(CombatTuning.ReservedLayer) != 0f)
                Animator.SetLayerWeight(CombatTuning.ReservedLayer, 0f);
            int hash = Animator.StringToHash(state);
            var info = Animator.GetCurrentAnimatorStateInfo(CombatTuning.BaseLayer);
            if (restart == false && (info.shortNameHash == hash || _baseRequested == state)) return;
            _baseRequested = state;
            if (restart && fade <= 0.0005f)
            {
                // Corte seco: el golpe sale en el frame en que se pulsa.
                Animator.Play(hash, CombatTuning.BaseLayer, 0f);
                return;
            }
            // CrossFadeInFixedTime arranca el destino en el frame 0 Y funde: sirve
            // tanto para entrar a un ataque con un fundido corto como para cambiar
            // de estado sin salto de pose.
            Animator.CrossFadeInFixedTime(hash, Mathf.Max(0f, fade), CombatTuning.BaseLayer, 0f);
        }

        // Alcance real del ataque contra el rival actual. La tolerancia usa el radio de
        // la HURTBOX DEL DEFENSOR (es la que se cruza con la hitbox), no la del atacante.
        public float GetCurrentAttackRange() => _attack != null
            ? _attack.hitboxForward + _attack.hitboxWidth * 0.5f +
              (Opponent != null ? Opponent.CurrentHurtboxRadius : CurrentHurtboxRadius)
            : 0f;

        void DrawDebugBoxes()
        {
            if (CombatTuning.DebugHitboxes == false) return;
            var hurtColor = IsBlocking ? new Color(0.4f, 0.75f, 1f) : new Color(0.35f, 1f, 0.5f);
            DrawRect(transform.position.x, HurtboxBottom, CurrentHurtboxRadius * 2f, CurrentHurtboxHeight, hurtColor);
            if (IsHitWindowActive())
            {
                float cx = transform.position.x + _facing * _attack.hitboxForward;
                float bottom = transform.position.y + _attack.hitboxCenterY - _attack.hitboxHeight * 0.5f;
                DrawRect(cx, bottom, _attack.hitboxWidth, _attack.hitboxHeight, new Color(1f, 0.25f, 0.2f));
            }
        }

        void DrawRect(float centerX, float bottom, float width, float height, Color color)
        {
            float left = centerX - width * 0.5f;
            float right = centerX + width * 0.5f;
            float top = bottom + height;
            float z = transform.position.z - 0.6f;
            var a = new Vector3(left, bottom, z);
            var b = new Vector3(right, bottom, z);
            var c = new Vector3(right, top, z);
            var d = new Vector3(left, top, z);
            Debug.DrawLine(a, b, color);
            Debug.DrawLine(b, c, color);
            Debug.DrawLine(c, d, color);
            Debug.DrawLine(d, a, color);
        }

        void OnDrawGizmos()
        {
            if (CombatTuning.DebugHitboxes == false || Initialized == false) return;
            var hurtColor = IsBlocking ? new Color(0.4f, 0.75f, 1f, 0.8f) : new Color(0.35f, 1f, 0.5f, 0.8f);
            Gizmos.color = hurtColor;
            var center = new Vector3(transform.position.x, HurtboxBottom + CurrentHurtboxHeight * 0.5f,
                transform.position.z - 0.6f);
            Gizmos.DrawWireCube(center, new Vector3(CurrentHurtboxRadius * 2f, CurrentHurtboxHeight, 0.05f));
            if (IsHitWindowActive())
            {
                Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.9f);
                var hc = new Vector3(transform.position.x + _facing * _attack.hitboxForward,
                    transform.position.y + _attack.hitboxCenterY, transform.position.z - 0.6f);
                Gizmos.DrawWireCube(hc, new Vector3(_attack.hitboxWidth, _attack.hitboxHeight, 0.05f));
            }
        }
    }
}
