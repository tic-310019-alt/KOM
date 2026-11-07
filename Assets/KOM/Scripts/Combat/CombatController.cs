using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KOM
{
    public sealed class CombatController : MonoBehaviour
    {
        public float ArenaHalfWidth { get; private set; } = 6f;

        const float RoundDuration = 60f;
        const float SpawnDistance = 2.1f;

        GameSession _session;
        CharacterDef _playerDef;
        CharacterDef _enemyDef;
        StageDef _stage;
        TournamentNodeDef _node;
        Fighter _player;
        Fighter _enemy;
        CombatHud _hud;
        Camera _camera;
        CombatCameraRig _cameraRig;
        // Unico dueno del feel de los golpes (ver HitFeedback).
        HitFeedback _feedback;

        int _round = 1;
        int _playerWins;
        int _enemyWins;
        float _roundTime;
        bool _roundActive;
        bool _roundEnding;
        bool _matchOver;
        bool _paused;
        bool _controlsOpen;
        bool _resultOpen;
        // Hitstop POR LUCHADOR. Ya no se toca Time.timeScale: congelar el juego entero
        // apagaba tambien los VFX y el impacto se leia como un tiron de la aplicacion.
        int _chainHits;
        float _chainAge;

        // ---------------- Recolectables ----------------
        // El plan del asalto se decide al empezarlo (reparto equilibrado y sin sorpresas)
        // y el azar es determinista por nodo+ronda: dos partidas iguales dan lo mismo.
        readonly List<CombatPickup> _pickups = new List<CombatPickup>();
        PickupType[] _pickupPlan = Array.Empty<PickupType>();
        System.Random _pickupRng;
        float _pickupTimer;
        int _pickupCursor;

        public Fighter Player => _player;
        public Fighter Enemy => _enemy;
        /// <summary>La ronda esta en curso (lo usan las herramientas de verificacion).</summary>
        public bool RoundActive => _roundActive;
        public int Round => _round;
        public int PlayerRoundWins => _playerWins;
        public int EnemyRoundWins => _enemyWins;
        public bool RoundEnding => _roundEnding;
        public bool ResultOpen => _resultOpen;
        public bool MatchOver => _matchOver;
        /// <summary>La pausa normal (o el mapa consultable) tiene el combate congelado.</summary>
        public bool Paused => _paused;
        // Las sondas de verificacion lo apagan: un recolectable que cura a mitad de una
        // medicion de dano la invalida.
        public bool PickupsEnabled { get; set; } = true;
        bool _isFinalNode;
        bool _versus;

        Action _primaryAction;
        Action _secondaryAction;

        void Start()
        {
            _session = GameSession.Ensure();

            // Dos jugadores: no hay torneo, ni nodo, ni jefa. La pelea se monta con lo que
            // se eligio en la pantalla anterior.
            _versus = _session.VersusMode;
            if (_versus)
            {
                _playerDef = _session.VersusPlayer1Def;
                _enemyDef = _session.VersusPlayer2Def;
                _node = null;
                _stage = _session.VersusStage;
            }
            else
            {
                if (!_session.HasRun)
                {
                    ScreenFader.Load(KomScenes.CharacterSelect);
                    return;
                }
                if (_session.IsRunComplete)
                {
                    ScreenFader.Load(KomScenes.Ending);
                    return;
                }

                _playerDef = _session.PlayerDef;
                _enemyDef = _session.CurrentOpponentDef;
                _node = _session.CurrentNode;
                _stage = _node != null ? KomData.GetStage(_node.stageId) : null;
            }
            if (_playerDef == null || _enemyDef == null || _stage == null)
            {
                Debug.LogError("[KOM] Datos de combate incompletos. Volviendo a la seleccion.");
                ScreenFader.Load(KomScenes.CharacterSelect);
                return;
            }

            CombatTuning.DebugHitboxes = false;
            BuildWorld();
            if (_player == null || _enemy == null || _hud == null)
            {
                Debug.LogError("[KOM] No se pudo construir el combate; se vuelve a la seleccion.");
                ScreenFader.Load(KomScenes.CharacterSelect);
                return;
            }
            // Se cae el tema y entran la música del escenario y la grada, coordinadas.
            // La pelea final usa la cama "extensión".
            _isFinalNode = !_versus && _session.EncounterIndex >= _session.NodeCount - 1;
            KomAudio.Combat(_stage.id, _isFinalNode);

            // El primer combate del torneo es el tutorial: la camara se queda en el
            // encuadre panoramico ensenando el escenario y la pelea no arranca hasta que
            // el jugador cierra la referencia de controles. En versus no hay tutorial:
            // los dos jugadores ya conocen el modo y la leyenda del HUD lleva sus teclas.
            if (_versus)
            {
                if (_hud != null) _hud.SetLegendPersistent(true);
                BeginFirstFight();
            }
            else if (_session.EncounterIndex == 0)
            {
                _hud.SetLegendPersistent(true);
                if (_cameraRig != null) _cameraRig.HoldIntro(IntroZoom());
                _hud.ShowControls(true, KomLoc.T("CONTINUAR", "CONTINUE"), BeginFirstFight);
            }
            else
            {
                BeginFirstFight();
            }
            ScreenFader.FadeIn();
        }

        // `stageIdOverride` permite revisar un escenario concreto sin depender de la ruta
        // de la sesion: lo usan las capturas de verificacion (KomStageShot).
        public void BuildEditorPreview(string playerId = "juan_grabriel", string stageIdOverride = null)
        {
            _session = GameSession.Ensure();
            // Una preview de combate es SIEMPRE del torneo: si se viene de un preview
            // de versus, el modo local no debe contaminar la partida.
            if (_session.VersusMode) _session.ExitVersus();
            if (_session.IsRunComplete) _session.ResetRun();
            if (!_session.HasRun) _session.BeginRun(playerId);
            _playerDef = _session.PlayerDef;
            _enemyDef = _session.CurrentOpponentDef;
            _node = _session.CurrentNode;
            string stageId = string.IsNullOrEmpty(stageIdOverride)
                ? (_node != null ? _node.stageId : null)
                : stageIdOverride;
            _stage = stageId != null ? KomData.GetStage(stageId) : null;
            if (_playerDef == null || _enemyDef == null || _stage == null) return;
            BuildWorld();
            if (_cameraRig != null) _cameraRig.Snap();
        }

        void BuildWorld()
        {
            KomSpecialDiag.Reset();
            if (!string.IsNullOrEmpty(_stage.prefabResource))
            {
                var prefab = Resources.Load<GameObject>(_stage.prefabResource);
                if (prefab != null)
                {
                    var stageGo = Instantiate(prefab);
                    stageGo.transform.SetPositionAndRotation(
                        new Vector3(_stage.prefabOffsetX, _stage.prefabOffsetY, _stage.prefabOffsetZ),
                        Quaternion.Euler(_stage.prefabRotationX, _stage.prefabRotationY, 0f));
                    float s = _stage.prefabScale > 0.01f ? _stage.prefabScale : 1f;
                    stageGo.transform.localScale = Vector3.one * s;
                    StageBuilder.BuildRealStageLighting(stageGo.transform, _stage);
                    ArenaHalfWidth = _stage.arenaHalfWidth;
                }
                else
                {
                    ArenaHalfWidth = StageBuilder.Build(_stage).ArenaHalfWidth;
                }
            }
            else
            {
                ArenaHalfWidth = StageBuilder.Build(_stage).ArenaHalfWidth;
            }

            // Publico del escenario (hoy solo el Zocalo). Devuelve null si el escenario no
            // declara layout, asi que los demas escenarios no pagan nada.
            KomStageCrowd.Attach(_stage);

            var camGo = new GameObject("CombatCamera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            _camera = camGo.GetComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            var sky = KomData.ParseColor(_stage.skyColor, new Color(0.03f, 0.04f, 0.05f));
            _camera.backgroundColor = sky * 0.65f;
            // El FOV forma parte del encuadre: con el se resuelve la distancia que hace que
            // los luchadores ocupen la fraccion de pantalla pedida (StageDef.cameraFill).
            _camera.fieldOfView = _stage.cameraFov > 1f ? _stage.cameraFov : 34f;
            _camera.nearClipPlane = 0.1f;
            // El escenario se encoge al normalizarlo a escala humana: el plano lejano tiene
            // que seguir cubriendo el recinto, no cortarlo.
            _camera.farClipPlane = 140f;
            VisualMood.Apply(_camera);

            _player = SpawnFighter(_playerDef, new Vector3(-SpawnDistance, 0f, 0.2f), 1f, false, "rookie", 0);
            _enemy = SpawnFighter(_enemyDef, new Vector3(SpawnDistance, 0f, 0.2f), -1f, !_versus,
                _node != null ? _node.aiProfile : "rookie", _node != null ? _node.seed : 0);
            if (_player == null || _enemy == null)
            {
                Debug.LogError("[KOM] No se pudieron instanciar los luchadores.");
                return;
            }
            var p1Controls = new FighterControls(0, _versus);
            var p2Controls = _versus ? new FighterControls(1, true) : null;
            _player.Initialize(_playerDef, _enemy, false, "player", 0, this, p1Controls);
            _enemy.Initialize(_enemyDef, _player, !_versus, _node != null ? _node.aiProfile : "rookie",
                _node != null ? _node.seed : 0, this, p2Controls);
            // La jefa absorbe el poder de los rivales que el jugador ya derrotó. Se lo pasa
            // al spawn, antes de que la IA pueda decidir un especial.
            if (_enemyDef.isBoss) _enemy.AbsorbSpecials(_session.DefeatedOpponents);

            _cameraRig = camGo.AddComponent<CombatCameraRig>();
            _cameraRig.Init(_camera, _player, _enemy, _stage);

            var hudGo = new GameObject("CombatHud");
            _hud = hudGo.AddComponent<CombatHud>();
            _hud.Initialize(_playerDef, _enemyDef, _stage.displayName,
                _versus ? 1 : _session.EncounterIndex + 1,
                _versus ? 1 : _session.NodeCount,
                _versus, _session.VersusRoundsToWin);
            _hud.SetRound(_round, 0, 0);
            _hud.SetTimer(RoundDuration);
            _hud.SetHealth(1f, 1f, true);
            _hud.SetSuper(0f, 0f, true);

            // El feel se monta DESPUES del HUD: necesita la camara, el rig y el HUD ya
            // creados. La capa de pantalla y el volumen de impacto viven como hijos del
            // controlador, asi que mueren con el combate.
            _feedback = HitFeedback.Attach(transform, _cameraRig, _camera, _hud, _player);
        }

        Fighter SpawnFighter(CharacterDef def, Vector3 pos, float facing, bool ai, string profile, int seed)
        {
            var prefab = Resources.Load<GameObject>(def.prefabResource);
            if (prefab == null)
            {
                Debug.LogError($"[KOM] Falta Resources/{def.prefabResource}.prefab");
                return null;
            }
            var go = Instantiate(prefab, pos, KomVisual.FaceDirection(facing, 10f));
            var fighter = go.GetComponent<Fighter>();
            if (fighter == null)
            {
                Debug.LogError($"[KOM] El prefab {def.id} no tiene Fighter.");
                Destroy(go);
                return null;
            }
            fighter.enabled = true;
            return fighter;
        }

        /// <summary>Zoom de presentacion del escenario (ver StageDef.cameraIntroZoom).</summary>
        float IntroZoom() => _stage != null ? Mathf.Max(1f, _stage.cameraIntroZoom) : 1f;

        /// <summary>
        /// Arranca el combate: la camara entra desde el encuadre panoramico mientras suena
        /// el cartel de ronda, de modo que llega al encuadre de pelea al empezar.
        /// </summary>
        void BeginFirstFight()
        {
            if (_hud != null) _hud.ShowControls(false);
            if (_cameraRig != null) _cameraRig.PlayIntro(IntroZoom(), CombatTuning.CameraIntroTime);
            StartCoroutine(RoundIntroRoutine());
        }

        IEnumerator RoundIntroRoutine()
        {
            if (_hud == null) yield break;
            _roundActive = false;
            _player.ControlsEnabled = false;
            _enemy.ControlsEnabled = false;
            _playerWins = 0;
            _enemyWins = 0;
            _round = 1;

            _hud.SetRound(_round, _playerWins, _enemyWins);
            _hud.SetHealth(1f, 1f, true);
            _hud.SetSuper(0f, 0f, true);
            _hud.SetTimer(RoundDuration);
            if (_cameraRig != null) _cameraRig.Snap();
            yield return new WaitForSecondsRealtime(0.2f);
            yield return StartCoroutine(BeginRoundRoutine());
        }

        IEnumerator BeginRoundRoutine()
        {
            if (_hud == null) yield break;
            _roundEnding = false;
            _roundTime = RoundDuration;
            _player.ResetForRound(new Vector3(-SpawnDistance, 0f, 0.2f), 1f);
            _enemy.ResetForRound(new Vector3(SpawnDistance, 0f, 0.2f), -1f);
            // Los recolectables se sortean al empezar el asalto: plan equilibrado y
            // primera caida con margen para que la pelea arranque limpia.
            ResetPickups();
            KomCrowd.ResetRound();
            KomStageCrowd.RoundStart();
            _hud.SetHealth(1f, 1f, true);
            _hud.SetRound(_round, _playerWins, _enemyWins);
            _hud.SetSuper(_player.SuperFraction, _enemy.SuperFraction, true);
            _hud.SetTimer(RoundDuration);
            if (_cameraRig != null) _cameraRig.Snap();
            _hud.ShowBanner(KomLoc.T($"RONDA {_round}", $"ROUND {_round}"),
                KomLoc.T("PREPARADOS", "READY"), new Color(0.96f, 0.86f, 0.62f), 0.7f);
            // La grada se reparte: una SOLA canción de entrada por asalto. En la final
            // manda el himno del torneo; en la ronda 1 empuja el jugador y en la 2 el rival.
            KomCrowd.RoundIntro(_playerDef.id, _enemyDef.id, _round, _isFinalNode);
            // El cartel dura lo que la entrada de camara (CameraIntroTime): la panoramica
            // necesita tiempo para leerse y la camara debe llegar al encuadre de pelea
            // cuando el jugador recupera el control.
            yield return new WaitForSecondsRealtime(1.30f);
            if (_hud == null) yield break;
            _hud.ShowBanner(KomLoc.T("¡PELEA!", "FIGHT!"), "", new Color(0.98f, 0.78f, 0.35f), 0.45f);
            yield return new WaitForSecondsRealtime(0.45f);
            _player.ControlsEnabled = true;
            _enemy.ControlsEnabled = true;
            _roundActive = true;
        }

        void Update()
        {
            if (_hud == null || _player == null || _enemy == null) return;
            // Ventana del escalado de hitstop: fuera de un combo, el siguiente golpe
            // vuelve a congelar al 100 %.
            if (_chainAge < 1f) _chainAge += Time.deltaTime;
            if (InputReader.DebugTogglePressed())
            {
                CombatTuning.DebugHitboxes = CombatTuning.DebugHitboxes == false;
            }
            _hud.SetHealth(_player.Health / _playerDef.maxHealth, _enemy.Health / _enemyDef.maxHealth);
            // La estamina (guardia) ya no tiene barra: se sigue simulando en `Fighter` y
            // el aviso de guardia rota sale en el cartel.
            _hud.SetSuper(_player.SuperFraction, _enemy.SuperFraction);

            // La grada lee la pelea: súper cargado y alguien contra las cuerdas.
            if (_roundActive)
            {
                // La grada necesita saber cuánto lleva el asalto: en los primeros segundos
                // está sonando la canción de entrada y no puede pisarla.
                KomCrowd.Tick(Time.deltaTime);
                if (_player.SuperReady || _enemy.SuperReady)
                    KomCrowd.SuperReady(_player.SuperReady ? _playerDef.id : _enemyDef.id);
                const float LowFraction = 0.25f;
                if (_player.Health <= _playerDef.maxHealth * LowFraction) KomCrowd.LowHealth(true, _playerDef.id);
                if (_enemy.Health <= _enemyDef.maxHealth * LowFraction) KomCrowd.LowHealth(false, _enemyDef.id);
            }

            if (_resultOpen)
            {
                bool confirm = _versus ? VersusConfirmPressed() : InputReader.ConfirmPressed();
                bool cancel = _versus ? VersusCancelPressed() : InputReader.CancelPressed();
                if (confirm) _primaryAction?.Invoke();
                else if (cancel) _secondaryAction?.Invoke();
                return;
            }

            // Referencia de controles (F1 / R3): congela la pelea mientras se lee.
            if (_controlsOpen)
            {
                bool close = InputReader.ControlsPressed() || InputReader.PausePressed();
                close |= _versus ? VersusConfirmPressed() : InputReader.ConfirmPressed();
                if (close)
                {
                    _controlsOpen = false;
                    if (_hud != null) _hud.ShowControls(false);
                    if (_paused) ResumeCombat();
                }
                return;
            }
            if (!_paused && InputReader.ControlsPressed() && _roundActive && !_roundEnding && !_matchOver)
            {
                _controlsOpen = true;
                _paused = true;
                _player.SetPaused(true);
                _enemy.SetPaused(true);
                if (_hud != null) _hud.ShowControls(true, KomLoc.T("CERRAR  ·  F1", "CLOSE  ·  F1"));
                return;
            }

            if (_paused)
            {
                // El mapa consultable se cierra con ESC/Enter/Start y devuelve el control
                // a la MISMA ronda; no pasa por la pantalla de pausa otra vez.
                if (_hud != null && _hud.WorldMapVisible)
                {
                    if (InputReader.PausePressed() || InputReader.ConfirmPressed()) _hud.CloseWorldMap();
                    return;
                }
                if (InputReader.PausePressed()) ResumeCombat();
                return;
            }

            // El menu de desarrollo congela el combate mientras esta abierto: ni reloj ni
            // IA ni ataques. La pausa normal ya se resolvio arriba, asi que su cartel
            // sigue funcionando con el menu abierto.
            if (DevFrozen) return;

            if (!_roundActive || _roundEnding || _matchOver) return;
            // Debug explicito de la barra: F2 = P1, F3 = ambos. No hay relleno silencioso.
            if (InputReader.DebugFillSuperPressed()) _player.FillSuperDebug();
            if (InputReader.DebugFillBothSupersPressed()) { _player.FillSuperDebug(); _enemy.FillSuperDebug(); }
            if (InputReader.PausePressed())
            {
                PauseCombat();
                return;
            }

            _roundTime -= Time.deltaTime;
            _hud.SetTimer(_roundTime);
            UpdatePickups(Time.deltaTime);
            if (_roundTime <= 0f)
            {
                _roundTime = 0f;
                StartCoroutine(EndRoundRoutine(null));
            }
        }

        // En versus los menus del combate (resultado y referencia) los puede manejar
        // cualquiera de los dos con SU boton de golpe rapido. El Enter global no basta:
        // el mando de P2 no es `Gamepad.current`.
        bool VersusConfirmPressed()
        {
            if (!_versus) return false;
            return (_player != null && _player.Controls != null && _player.Controls.MenuConfirmPressed()) ||
                   (_enemy != null && _enemy.Controls != null && _enemy.Controls.MenuConfirmPressed());
        }

        bool VersusCancelPressed()
        {
            if (!_versus) return false;
            return (_player != null && _player.Controls != null && _player.Controls.MenuCancelPressed()) ||
                   (_enemy != null && _enemy.Controls != null && _enemy.Controls.MenuCancelPressed());
        }

        public void OnAttackStarted(Fighter fighter, AttackSpec spec)
        {
            if (spec == null || !_roundActive) return;
            if (spec.heavy && _cameraRig != null)
                _cameraRig.Shake(0.035f, 0.18f);
            // El aire del golpe: suena al arrancar la animacion, el impacto llega despues.
            KomAudio.Swing(spec);
        }

        public void OnHitLanded(Fighter attacker, Fighter defender, AttackSpec spec, bool blocked, Vector3 point)
        {
            // Un unico dueno del "feel": HitFeedback deriva de la MISMA intensidad el
            // destello del cuerpo, el VFX por capas, la capa de pantalla, la camara
            // (traslacion + balanceo + FOV), el pulso de post-proceso y la interfaz.
            if (_feedback != null) _feedback.Landed(attacker, defender, spec, blocked, point);
            KomAudio.Impact(spec, blocked);
            // La grada reacciona a los golpes que pesan, no a todos. El publico visual
            // sube con la misma regla (un jab no levanta a nadie de la silla).
            if (!blocked && (spec.heavy || spec.hitCount > 1))
            {
                KomCrowd.BigHit(false);
                KomStageCrowd.Hype(0.68f);
            }
            StartHitstop(attacker, defender, spec.hitstop, blocked);
        }

        public void OnSpecialStarted(Fighter fighter, SpecialDef special)
        {
            if (special == null) return;
            KomAudio.SpecialCharge();
            var color = KomData.SpecialColor(fighter != null ? fighter.Def : null, special);
            if (_hud != null && _roundActive)
            {
                _hud.ShowBanner(special.displayName.ToUpperInvariant(), special.subtitle,
                    color, 0.7f);
                _hud.FlashScreen(color, fighter == _player ? 0.07f : 0.05f, 0.22f);
            }
            if (_cameraRig != null) _cameraRig.Shake(0.09f, 0.3f);
        }

        public void OnSpecialReleased(Fighter fighter, SpecialDef special, Vector3 point)
        {
            KomAudio.SpecialRelease();
            var color = KomData.SpecialColor(fighter != null ? fighter.Def : null, special);
            if (_hud != null) _hud.FlashScreen(color, 0.08f, 0.18f);
            if (_cameraRig != null) _cameraRig.Shake(0.14f, 0.24f);
        }

        public void OnSpecialHit(Fighter attacker, Fighter defender, SpecialDef special, bool blocked, Vector3 point)
        {
            if (special == null) return;
            KomAudio.SpecialImpact(special, blocked);
            if (!blocked) KomCrowd.BigHit(true);
            KomStageCrowd.Hype(blocked ? 0.45f : 1f);
            if (_feedback != null) _feedback.SpecialLanded(attacker, defender, special, blocked, point);
            StartHitstop(attacker, defender, (blocked ? special.hitstop * 0.5f : special.hitstop) * 1.5f, blocked);
        }

        public void OnSuperReady(Fighter fighter)
        {
            if (!_roundActive || fighter == null || fighter.Def == null || !fighter.Def.HasSpecial) return;
            // El aviso sonoro es para los humanos: en versus el súper de P2 tambien
            // tiene que anunciarse, no solo el de P1.
            if (!fighter.IsAI) KomAudio.SuperReady();
            var special = fighter.SelectedSpecial;
            string name = special != null && !string.IsNullOrEmpty(special.displayName)
                ? special.displayName.ToUpperInvariant()
                : KomLoc.T("SÚPER", "SUPER");
            var color = KomData.SpecialColor(fighter.Def, special);
            if (_hud != null)
            {
                _hud.ShowBanner(KomLoc.T("SÚPER LISTO", "SUPER READY"), name, color, 0.5f);
                _hud.FlashScreen(color, 0.05f, 0.2f);
            }
            if (_cameraRig != null) _cameraRig.Shake(0.045f, 0.2f);
            KomStageCrowd.SuperReady();
        }

        public void OnSpecialDenied(Fighter fighter)
        {
            if (!_roundActive || _hud == null || fighter == null || fighter.IsAI) return;
            if (fighter.Def == null || !fighter.Def.HasSpecial)
            {
                _hud.ShowBanner(KomLoc.T("SÚPER", "SUPER"),
                    KomLoc.T("SIN PODER ESPECIAL", "NO SPECIAL POWER"), new Color(0.78f, 0.8f, 0.76f), 0.4f);
                return;
            }
            // El aviso dice lo que FALTA, no lo que hay: antes decia "BARRA AL 100%"
            // justo cuando la barra no estaba llena.
            _hud.ShowBanner(KomLoc.T("SÚPER", "SUPER"),
                fighter.SuperReady
                    ? KomLoc.T("ESPECIAL NO DISPONIBLE", "SPECIAL UNAVAILABLE")
                    : KomLoc.T("BARRA INCOMPLETA", "SUPER BAR NOT FULL"),
                new Color(0.78f, 0.8f, 0.76f), 0.3f);
        }

        // Hitstop: los DOS luchadores se congelan en el frame del impacto. El resto del
        // mundo (camara, particulas, interfaz) sigue a velocidad real, que es lo que
        // separa un golpe que "pesa" de una pausa tecnica.
        //
        // Escalado de cadena: los impactos encadenados congelan cada vez menos (hasta
        // el 55 %). Sin esto, un combo de 7 golpes se convierte en siete pausas.
        void StartHitstop(Fighter attacker, Fighter defender, float duration, bool blocked)
        {
            if (duration <= 0.0005f || _paused || _resultOpen) return;

            _chainHits = _chainAge < 0.9f ? _chainHits + 1 : 1;
            _chainAge = 0f;
            float scale = Mathf.Max(0.55f, 1f - (_chainHits - 1) * 0.12f);
            float frozen = duration * scale * (blocked ? 0.45f : 1f);

            if (attacker != null) attacker.Freeze(frozen);
            if (defender != null) defender.Freeze(frozen);
        }

        public void OnGuardBreak(Fighter defender)
        {
            KomAudio.GuardBreak();
            if (_hud != null)
            {
                _hud.ShowBanner(KomLoc.T("GUARDIA ROTA", "GUARD BROKEN"),
                    defender == _player
                        ? KomLoc.T("P1 queda expuesto", "P1 is exposed")
                        : (_versus
                            ? KomLoc.T("P2 queda expuesto", "P2 is exposed")
                            : KomLoc.T("El rival queda expuesto", "The rival is exposed")),
                    new Color(0.55f, 0.82f, 1f), 0.55f);
            }
            if (_feedback != null && defender != null)
                _feedback.GuardBroken(defender, defender.transform.position + Vector3.up * 1.15f);
        }

        public void OnFighterKO(Fighter loser)
        {
            if (!_roundActive || _roundEnding) return;
            KomAudio.Knockout();
            // Primero se cierra la ronda; el feedback visual no puede ser la unica
            // puerta de salida del K.O. (un shader ausente dejaba al rival tirado y
            // la pelea sin avanzar).
            StartCoroutine(EndRoundRoutine(loser));
            if (_feedback != null && loser != null)
                _feedback.KnockedOut(loser, loser.transform.position + Vector3.up * 1.15f);
        }

        IEnumerator EndRoundRoutine(Fighter knockedOut)
        {
            if (_roundEnding) yield break;
            _roundEnding = true;
            _roundActive = false;
            _player.ControlsEnabled = false;
            _enemy.ControlsEnabled = false;
            // El contador de combo y los pulsos de pantalla no sobreviven al cierre de
            // la ronda: el cartel de K.O. tiene que leerse limpio.
            if (_feedback != null) _feedback.Clear();
            if (_hud != null) _hud.HideCombo();

            Fighter roundWinner;
            if (knockedOut == null)
            {
                if (Mathf.Abs(_player.Health - _enemy.Health) < 0.5f)
                {
                    _hud.ShowBanner(KomLoc.T("EMPATE", "DRAW"), KomLoc.T("SE REPITE LA RONDA", "ROUND REPEATS"),
                        new Color(0.85f, 0.8f, 0.68f), 1.2f);
                    yield return new WaitForSecondsRealtime(2f);
                    _roundEnding = false;
                    StartCoroutine(BeginRoundRoutine());
                    yield break;
                }
                roundWinner = _player.Health > _enemy.Health ? _player : _enemy;
                knockedOut = roundWinner == _player ? _enemy : _player;
                _hud.ShowBanner(KomLoc.T("TIEMPO", "TIME"), "", new Color(0.9f, 0.78f, 0.5f), 1.1f);
            }
            else
            {
                roundWinner = knockedOut == _enemy ? _player : _enemy;
                _hud.ShowBanner("K.O.", "", new Color(0.98f, 0.42f, 0.32f), 1.1f);
            }

            if (roundWinner == _player) _playerWins++;
            else _enemyWins++;
            _hud.SetRound(_round, _playerWins, _enemyWins);
            if (_cameraRig != null) _cameraRig.Shake(0.18f, 0.5f);
            // K.O. de verdad: la grada entera se levanta; por tiempo solo se anima.
            if (knockedOut != null) KomStageCrowd.Knockout();
            else KomStageCrowd.Hype(0.5f);

            // El KO es "Falling Back Death" (2.6 s): se le da tiempo a leerse antes de
            // celebrar. Despues el perdedor se levanta con "Getting Up" y recien entonces
            // se resetea la ronda o se abre el resultado.
            yield return new WaitForSecondsRealtime(knockedOut != null ? 1.1f : 0.35f);
            if (roundWinner != null) roundWinner.PlayVictory();
            // El KO lo celebra la grada, no un sting de interfaz.
            if (knockedOut != null) KomAudio.AmbientPorra(0.86f, 0.15f, KomScenes.Combat);
            yield return new WaitForSecondsRealtime(knockedOut != null ? 1.35f : 1.85f);
            if (knockedOut != null)
            {
                knockedOut.PlayGetUp();
                yield return new WaitForSecondsRealtime(1.15f);
            }

            int needed = _versus ? _session.VersusRoundsToWin : 2;
            if (_playerWins >= needed || _enemyWins >= needed)
            {
                EndMatch(_playerWins >= needed);
            }
            else
            {
                _round++;
                yield return StartCoroutine(BeginRoundRoutine());
            }
        }

        void EndMatch(bool playerWon)
        {
            // Guarda de idempotencia: una corrutina de ronda viva puede llegar aqui despues
            // de que el menu de desarrollo haya cerrado el encuentro. Completar dos veces
            // avanzaria dos nodos del torneo y se comeria un combate.
            if (_matchOver) return;
            _matchOver = true;
            _roundActive = false;
            _resultOpen = true;
            _chainHits = 0;
            _chainAge = 0f;
            _player.ControlsEnabled = false;
            _enemy.ControlsEnabled = false;

            if (_versus)
            {
                var winner = playerWon ? _playerDef : _enemyDef;
                KomAudio.Coreo(winner.id, 0.95f, 0.2f, KomScenes.Combat);
                // Revancha inmediata o vuelta al selector de escenario: las dos salidas
                // que se usan en una partida de sofa.
                _primaryAction = () => ScreenFader.Load(KomScenes.Combat);
                _secondaryAction = () => ScreenFader.Load(KomScenes.Map);
                int winsNeeded = _session.VersusRoundsToWin;
                string matchEs = winsNeeded <= 1 ? "A UNA" : $"AL MEJOR DE {winsNeeded * 2 - 1}";
                string matchEn = winsNeeded <= 1 ? "SINGLE" : $"BEST OF {winsNeeded * 2 - 1}";
                string NL = System.Environment.NewLine;
                string scoreLine = winner.displayName + " se lleva el combate." + NL + NL +
                                   "Marcador " + _playerWins + " - " + _enemyWins + ".";
                string scoreLineEn = winner.displayName + " takes the match." + NL + NL +
                                     "Score " + _playerWins + " - " + _enemyWins + ".";
                _hud.ShowResult(playerWon, false, _playerDef, _enemyDef, _enemyDef.genre,
                    KomLoc.T("REVANCHA", "REMATCH"), KomLoc.T("ESCENARIO", "STAGE"),
                    _primaryAction, _secondaryAction,
                    KomLoc.T($"VERSUS · {matchEs}", $"VERSUS · {matchEn}"),
                    KomLoc.T(scoreLine, scoreLineEn));
                _hud.SetResultTitle(KomLoc.T($"GANA {winner.displayName.ToUpperInvariant()}",
                                             $"{winner.displayName.ToUpperInvariant()} WINS"));
                _hud.SetResultChrome(KomLoc.T("RESULTADO · VERSUS", "VERSUS RESULT"),
                    KomLoc.T("C / U  ·  REVANCHA        B / O  ·  ESCENARIO", "C / U  ·  REMATCH        B / O  ·  STAGE"));
                return;
            }

            if (playerWon)
            {
                _session.CompleteEncounter();
                bool final = _session.IsRunComplete;
                // En la final solo corea el nombre: la porra grande llega con el concierto.
                if (final) KomAudio.Coreo(_playerDef.id, 0.95f, 0.2f, KomScenes.Combat);
                else KomAudio.Victory(_playerDef.id);
                if (final)
                {
                    // El concierto es el primer golpe del epilogo; la pantalla de campeon
                    // viene despues. PlayOnce evita repetirlo si se vuelve a entrar.
                    _primaryAction = () =>
                    {
                        if (!StoryFlow.PlayOnce("concert", StoryFlow.Concert, KomScenes.Ending))
                            ScreenFader.Load(KomScenes.Ending);
                    };
                    _secondaryAction = GoToMenu;
                    _hud.ShowResult(true, true, _playerDef, _enemyDef, _enemyDef.genre,
                        KomLoc.T("VER EL CONCIERTO", "WATCH THE CONCERT"), KomLoc.T("MENÚ", "MENU"),
                        _primaryAction, _secondaryAction);
                }
                else if (_session.EncounterIndex == 2)
                {
                    // Tras el segundo combate el torneo ya se cobró dos géneros: el veto
                    // musical deja de ser una regla y se vuelve una noticia.
                    _primaryAction = () =>
                    {
                        if (!StoryFlow.PlayOnce("veto", StoryFlow.Veto, KomScenes.Map))
                            ScreenFader.Load(KomScenes.Map);
                    };
                    _secondaryAction = GoToMenu;
                    _hud.ShowResult(true, false, _playerDef, _enemyDef, _enemyDef.genre,
                        KomLoc.T("CONTINUAR", "CONTINUE"), KomLoc.T("MENÚ", "MENU"), _primaryAction, _secondaryAction);
                }
                else
                {
                    _primaryAction = () => ScreenFader.Load(KomScenes.Map);
                    _secondaryAction = GoToMenu;
                    _hud.ShowResult(true, false, _playerDef, _enemyDef, _enemyDef.genre,
                        KomLoc.T("CONTINUAR AL MAPA", "CONTINUE TO MAP"), KomLoc.T("MENÚ", "MENU"), _primaryAction, _secondaryAction);
                }
            }
            else
            {
                _primaryAction = () => ScreenFader.Load(KomScenes.Combat);
                _secondaryAction = GoToMenu;
                _hud.ShowResult(false, false, _playerDef, _enemyDef, _enemyDef.genre,
                    KomLoc.T("REINTENTAR", "RETRY"), KomLoc.T("MENÚ", "MENU"), _primaryAction, _secondaryAction);
            }
        }

        void PauseCombat()
        {
            if (!_roundActive || _roundEnding || _matchOver) return;
            _paused = true;
            _chainHits = 0;
            _chainAge = 0f;
            if (_feedback != null) _feedback.Clear();
            _player.SetPaused(true);
            _enemy.SetPaused(true);
            // En campaña la pausa permite consultar el mapa sin abandonar la pelea.
            if (_versus) _hud.ShowPause(true, ResumeCombat, GoToMenu);
            else _hud.ShowPause(true, ResumeCombat, GoToMenu, OpenWorldMap);
        }

        void OpenWorldMap()
        {
            if (_hud == null) return;
            _hud.ShowWorldMap(true, CloseWorldMapAndResume);
        }

        void CloseWorldMapAndResume()
        {
            if (_hud != null) _hud.ShowWorldMap(false);
            ResumeCombat();
        }

        void ResumeCombat()
        {
            if (!_paused) return;
            _paused = false;
            _player.SetPaused(false);
            _enemy.SetPaused(false);
            _hud.ShowPause(false);
        }

        void GoToMenu()
        {
            Time.timeScale = 1f;
            ScreenFader.Load(KomScenes.MainMenu);
        }

        // ---------------- Herramientas de desarrollo ----------------
        // Las usa el menu oculto (F9). No abren un camino paralelo: el instakill entra por
        // el mismo TriggerKO que un golpe letal y "ganar" cierra por EndMatch, asi que el
        // resto del flujo (veto, mapa, final) ve exactamente lo que veria jugando.

        public bool DevFrozen { get; private set; }

        /// <summary>Hay un combate vivo y aun se puede cerrar por la via de desarrollo.</summary>
        public bool DevActionsAvailable => !_matchOver && !_resultOpen && _hud != null
                                           && _player != null && _enemy != null;

        /// <summary>Quita la pausa normal y la congelacion del menu de desarrollo.</summary>
        public void DevForceResume()
        {
            if (_paused) ResumeCombat();
            SetDevFrozen(false);
        }

        /// <summary>Congela el combate mientras el menu de desarrollo esta abierto.</summary>
        public void SetDevFrozen(bool frozen)
        {
            if (DevFrozen == frozen) return;
            DevFrozen = frozen;
            // Si la pausa normal manda, no se toca a los luchadores: al cerrar el menu
            // se reanudarian por detras del cartel de pausa.
            if (_paused || _player == null || _enemy == null) return;
            _player.SetPaused(frozen);
            _enemy.SetPaused(frozen);
        }

        public void DevKillEnemy() => DevKill(_enemy);

        public void DevKillPlayer() => DevKill(_player);

        void DevKill(Fighter fighter)
        {
            if (!_roundActive || _roundEnding || _matchOver || _resultOpen || fighter == null || fighter.IsKO) return;
            DevForceResume();
            fighter.DevKill();
        }

        /// <summary>Cierra el encuentro como victoria sin pelear. Idempotente.</summary>
        public void DevWinEncounter()
        {
            if (!DevActionsAvailable) return;
            DevForceResume();
            _roundActive = false;
            _roundEnding = true;
            _playerWins = 2;
            _enemyWins = 0;
            _hud.SetRound(_round, _playerWins, _enemyWins);
            EndMatch(true);
        }

        // ---------------- Recolectables ----------------

        void ResetPickups()
        {
            for (int i = 0; i < _pickups.Count; i++)
                if (_pickups[i] != null) Destroy(_pickups[i].gameObject);
            _pickups.Clear();

            // Reparto del asalto: ALTERNO (nunca dos iguales seguidos) y con el tipo de
            // apertura sorteado. Con 3 caidas siempre entra al menos una de cada, asi que
            // ninguna pelea se queda sin vida o sin estamina.
            int total = CombatTuning.PickupPerRound;
            _pickupPlan = new PickupType[total];

            // Determinista por nodo + ronda: la misma pelea reparte lo mismo.
            int seed = (_node != null ? _node.seed : 7) * 131 + _round * 17;
            _pickupRng = new System.Random(seed);
            var first = _pickupRng.Next(2) == 0 ? PickupType.Concha : PickupType.Soda;
            for (int i = 0; i < total; i++)
            {
                _pickupPlan[i] = i % 2 == 0
                    ? first
                    : (first == PickupType.Concha ? PickupType.Soda : PickupType.Concha);
            }

            _pickupCursor = 0;
            _pickupTimer = PickupDelay(CombatTuning.PickupFirstDelayMin, CombatTuning.PickupFirstDelayMax);
        }

        float PickupDelay(float min, float max)
        {
            if (_pickupRng == null) return min;
            return Mathf.Lerp(min, max, (float)_pickupRng.NextDouble());
        }

        void UpdatePickups(float dt)
        {
            if (!PickupsEnabled) return;
            if (_pickupPlan == null || _pickupCursor >= _pickupPlan.Length) return;
            _pickupTimer -= dt;
            if (_pickupTimer > 0f) return;
            _pickupTimer = PickupDelay(CombatTuning.PickupIntervalMin, CombatTuning.PickupIntervalMax);
            if (_pickups.Count >= CombatTuning.PickupMaxAlive) return;
            SpawnPickup();
        }

        void SpawnPickup()
        {
            if (_pickupPlan == null || _pickupCursor >= _pickupPlan.Length) return;
            var type = _pickupPlan[_pickupCursor];
            _pickupCursor++;
            if (!TryFindPickupSpot(out var spot)) return;
            var pickup = CombatPickup.Create(type, spot, this);
            if (pickup != null) _pickups.Add(pickup);
        }

        // Un sitio justo: dentro de la LINEA DE COMBATE, lejos de los dos luchadores y de
        // otro recolectable. El carril se calcula con el MISMO limite que usa
        // Fighter.ResolveBounds, para que el recolectable nunca caiga donde el luchador no
        // puede plantar los pies. Si no hay hueco tras varios intentos, se elige el punto
        // del carril mas alejado de los dos: el centro era el peor sitio posible, porque es
        // justo donde estan ellos.
        bool TryFindPickupSpot(out Vector3 spot)
        {
            // Fighter.ResolveBounds recorta 0.7 m por lado: ese es el alcance real.
            float walkable = Mathf.Max(1.2f, ArenaHalfWidth - 0.7f);
            float half = Mathf.Clamp(
                Mathf.Min(ArenaHalfWidth - CombatTuning.PickupArenaMargin, CombatTuning.PickupLaneHalfWidth),
                1.1f, Mathf.Max(1.1f, walkable - 0.55f));

            float best = 0f;
            float bestGap = -1f;
            for (int attempt = 0; attempt < 18; attempt++)
            {
                float x = Mathf.Lerp(-half, half, (float)_pickupRng.NextDouble());
                float gap = 99f;
                if (_player != null) gap = Mathf.Min(gap, Mathf.Abs(_player.transform.position.x - x));
                if (_enemy != null) gap = Mathf.Min(gap, Mathf.Abs(_enemy.transform.position.x - x));
                if (gap > bestGap) { bestGap = gap; best = x; }
                if (gap < CombatTuning.PickupMinFighterDistance) continue;

                bool crowded = false;
                for (int i = 0; i < _pickups.Count; i++)
                {
                    var other = _pickups[i];
                    if (other != null && Mathf.Abs(other.transform.position.x - x) < 1.7f) { crowded = true; break; }
                }
                if (crowded) continue;
                spot = new Vector3(x, 0f, CombatPickup.LaneZ);
                return true;
            }
            spot = new Vector3(best, 0f, CombatPickup.LaneZ);
            return true;
        }

        // El recolectable disponible mas cercano. Lo consulta la IA para disputarlo.
        public CombatPickup NearestPickup(Vector3 from)
        {
            CombatPickup best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _pickups.Count; i++)
            {
                var pickup = _pickups[i];
                if (pickup == null || !pickup.Available) continue;
                float distance = Mathf.Abs(pickup.transform.position.x - from.x);
                if (distance < bestDist) { bestDist = distance; best = pickup; }
            }
            return best;
        }

        public void OnPickupCollected(Fighter fighter, PickupType type, CombatPickup pickup)
        {
            if (pickup != null) _pickups.Remove(pickup);
            if (fighter == null) return;

            KomAudio.Pickup();
            if (_cameraRig != null) _cameraRig.Shake(0.05f, 0.16f);

            if (type == PickupType.Concha)
            {
                float max = fighter.Def != null ? fighter.Def.maxHealth : 100f;
                float healed = fighter.Heal(max * CombatTuning.PickupConchaHealFraction);
                if (_hud != null)
                {
                    _hud.ShowBanner("CONCHA",
                        healed > 0.5f ? $"+{Mathf.RoundToInt(healed)} DE VIDA" : "VIDA AL MAXIMO",
                        new Color(0.96f, 0.92f, 0.84f), 0.55f);
                    _hud.FlashScreen(Color.white, fighter == _player ? 0.12f : 0.06f, 0.22f);
                }
            }
            else
            {
                // El refresco llena el SUPER: es la unica barra de recurso que queda y la
                // que el jugador puede leer, asi que el premio tiene que verse ahi. Sigue
                // recargando estamina de paso (bloquear cuesta y el refresco la devuelve),
                // pero eso ya no se anuncia porque no hay barra que lo muestre.
                float before = fighter.SuperFraction;
                fighter.AddSuper(CombatTuning.PickupSodaSuperFraction);
                fighter.RestoreGuard(CombatTuning.PickupSodaGuardFraction);
                float gained = fighter.SuperFraction - before;
                if (_hud != null)
                {
                    _hud.ShowBanner("REFRESCO",
                        gained > 0.01f ? $"+{Mathf.RoundToInt(gained * 100f)} % DE SÚPER" : "SÚPER AL MAXIMO",
                        new Color(0.86f, 0.92f, 0.96f), 0.55f);
                    _hud.FlashScreen(Color.white, fighter == _player ? 0.12f : 0.06f, 0.22f);
                }
            }
        }

        public void OnPickupExpired(CombatPickup pickup)
        {
            if (pickup != null) _pickups.Remove(pickup);
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
        }
    }
}
