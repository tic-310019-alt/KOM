using UnityEngine;

namespace KOM
{
    public struct FighterIntent
    {
        public float move;
        public bool jump;
        public bool crouch;
        public bool block;
        public bool lightPunch;
        public bool heavyPunch;
        public bool lightKick;
        public bool heavyKick;
        public bool dashLeft;
        public bool dashRight;
        public bool special;
    }

    public interface IFighterBrain
    {
        // Se llama SIEMPRE, aunque el luchador no pueda actuar. Los temporizadores de
        // decision tienen que correr en tiempo real: si solo avanzan cuando el cerebro
        // piensa, la IA paga la espera dos veces (una durante su propio ataque y otra
        // despues) y se queda mirando al rival.
        void Tick(float deltaTime);
        FighterIntent Think(Fighter self, Fighter opponent, float deltaTime);
    }

    public sealed class PlayerFighterBrain : IFighterBrain
    {
        readonly FighterControls _controls;

        public PlayerFighterBrain(FighterControls controls)
        {
            _controls = controls ?? FighterControls.First();
        }

        public void Tick(float deltaTime)
        {
            // El doble toque se sondea en TODOS los frames, no solo cuando el luchador
            // puede actuar: si se leyera dentro de Think, el primer toque durante un
            // ataque o un hitstun se perderia y el segundo no encadenaria nada.
            _controls.PollDash();
        }

        public FighterIntent Think(Fighter self, Fighter opponent, float deltaTime)
        {
            var intent = new FighterIntent
            {
                move = _controls.MoveAxis(),
                jump = _controls.JumpPressed(),
                crouch = _controls.CrouchHeld(),
                block = _controls.BlockHeld(),
                // Los cuatro botones de ataque NO se leen aqui: el jugador los procesa en
                // Fighter.UpdatePlayerAttackInput, que ademas resuelve los combos. Leerlos
                // en dos sitios era lo que hacia que un toque disparara dos ataques.
                special = _controls.SpecialPressed()
            };
            // Correr: doble toque en teclado, o gatillo derecho + direccion en mando.
            // El doble toque manda la direccion del toque, no la del eje: si el jugador
            // pulsa A dos veces, corre a la izquierda aunque el eje aun no se haya movido.
            if (_controls.DashLeftPressed()) intent.dashLeft = true;
            else if (_controls.DashRightPressed()) intent.dashRight = true;
            else if (_controls.SprintHeld() &&
                (_controls.SprintPressed() || _controls.MoveLeftPressed() || _controls.MoveRightPressed()) &&
                Mathf.Abs(intent.move) > 0.4f)
            {
                if (intent.move < 0f) intent.dashLeft = true;
                else intent.dashRight = true;
            }
            return intent;
        }
    }

    public sealed class AiProfile
    {
        public string id;
        public float reaction;
        public float aggression;
        public float blockChance;
        public float dodgeChance;
        public float attackCooldown;
        public float preferredRange;
        public float moveBias;

        public static AiProfile Get(string id)
        {
            switch (id)
            {
                case "rookie":
                    return new AiProfile { id = id, reaction = 0.3f, aggression = 0.34f, blockChance = 0.08f, dodgeChance = 0.04f, attackCooldown = 1.5f, preferredRange = 1.55f, moveBias = 0.82f };
                case "contender":
                    return new AiProfile { id = id, reaction = 0.21f, aggression = 0.5f, blockChance = 0.18f, dodgeChance = 0.1f, attackCooldown = 1.1f, preferredRange = 1.5f, moveBias = 0.95f };
                case "veteran":
                    return new AiProfile { id = id, reaction = 0.15f, aggression = 0.68f, blockChance = 0.3f, dodgeChance = 0.16f, attackCooldown = 0.82f, preferredRange = 1.42f, moveBias = 1.0f };
                case "boss":
                    // Reajustado tras arreglar el rodillo defensivo (ver Think). Antes estos
                    // numeros se relanzaban por frame y la jefa bloqueaba casi siempre; ahora
                    // significan lo que dicen, asi que se sube la agresividad real: castiga al
                    // terminar el bloqueo, ataca mas seguido y solo levanta la guardia ante un
                    // tercio de los golpes que recibe.
                    return new AiProfile { id = id, reaction = 0.1f, aggression = 0.92f, blockChance = 0.34f, dodgeChance = 0.22f, attackCooldown = 0.5f, preferredRange = 1.35f, moveBias = 1.1f };
                default:
                    return Get("rookie");
            }
        }
    }

    public sealed class AiFighterBrain : IFighterBrain
    {
        readonly AiProfile _profile;
        readonly System.Random _rng;
        float _decisionTimer;
        float _attackTimer;
        float _blockTimer;
        float _crouchTimer;
        float _jumpTimer;
        float _move;
        bool _lastAttackLight;
        // Decision defensiva ya tomada para el ataque entrante (ver Think): se tira UNA vez
        // por ataque, no por frame.
        AttackSpec _reactedTo;
        int _defensive;            // 0 nada · 1 bloqueo · 2 agacharse · 3 retroceder
        bool _counterReady;        // bloqueo/evadi con exito: toca castigar al terminar

        public AiFighterBrain(string profileId, int seed)
        {
            _profile = AiProfile.Get(profileId);
            _rng = new System.Random(seed);
        }

        public void Tick(float deltaTime)
        {
            _decisionTimer -= deltaTime;
            _attackTimer -= deltaTime;
            _blockTimer -= deltaTime;
            _crouchTimer -= deltaTime;
            _jumpTimer -= deltaTime;
        }

        public FighterIntent Think(Fighter self, Fighter opponent, float deltaTime)
        {
            var intent = new FighterIntent();

            if (self.IsKO || opponent == null || !self.ControlsEnabled) return intent;
            // Rival noqueado: ya no hay nada que decidir. Sin esto, en el frame del K.O. la IA
            // todavia tenia control y medidor lleno, y disparaba un especial contra el cuerpo
            // que cae (el "guitarrazo sin razon" que se reporto).
            if (opponent.IsKO) return intent;
            if (!self.IsGrounded) return intent;

            float dx = opponent.transform.position.x - self.transform.position.x;
            float distance = Mathf.Abs(dx);
            float toward = Mathf.Sign(dx);

            if (_blockTimer > 0f)
            {
                intent.block = true;
                _move = 0f;
                intent.move = 0f;
                return intent;
            }
            if (_crouchTimer > 0f)
            {
                intent.crouch = true;
                intent.move = 0f;
                return intent;
            }

            if (!opponent.IsGrounded && _attackTimer <= 0f &&
                CanReach(Attacks.Uppercut, opponent, distance) &&
                NextFloat() < 0.55f * _profile.aggression)
            {
                // Antiaereo: agacharse y uppercut al rival que salta. Solo se lanza si el
                // uppercut LLEGA: antes bastaba con estar a 1.75 m y el puno se quedaba
                // 0.4 m corto, que es el "golpea al aire" reportado.
                _attackTimer = _profile.attackCooldown;
                _lastAttackLight = false;
                intent.crouch = true;
                intent.heavyPunch = true;
                return intent;
            }

            // El rival ya no está atacando: la decisión defensiva de ESE ataque se borra para
            // que el siguiente vuelva a tirarse, y se aprovecha para castigar.
            if (!opponent.IsAttacking)
            {
                _reactedTo = null;
                if (_counterReady)
                {
                    _counterReady = false;
                    if (_attackTimer <= 0f && distance < _profile.preferredRange + 0.7f)
                    {
                        // Castigo: quien acaba de bloquear un golpe tiene la ventana para
                        // responder. Sin esto la IA bloquea y se queda mirando, que es justo
                        // lo que se le reprochaba a la jefa. Si ningun ataque llega, en vez
                        // de tirar el golpe al aire se cierra la distancia.
                        if (ChooseAttack(ref intent, distance, opponent))
                        {
                            _attackTimer = _profile.attackCooldown * 0.6f;
                            return intent;
                        }
                        _move = toward;
                        intent.move = _move;
                        return intent;
                    }
                }
            }

            if (opponent.IsAttacking && distance < opponent.GetCurrentAttackRange() + 0.35f)
            {
                var incoming = opponent.CurrentAttack;
                if (incoming != null && !ReferenceEquals(incoming, _reactedTo))
                {
                    // LA DECISION DEFENSIVA SE TIRA UNA VEZ POR ATAQUE ENTRANTE.
                    //
                    // Estaba dentro del bloque de cada frame, asi que con un ataque de ~30
                    // frames el rodillo se relanzaba 30 veces: blockChance 0.45 daba un
                    // 1 - 0.55^30 = 99,99 % de bloqueos, y hasta el `rookie` con 0.08
                    // bloqueaba el 91 %. Por eso la jefa parecia pasiva: no es que eligiera
                    // la guardia a menudo, es que la elegida QUEDABA FIJA por el temporizador
                    // que ella misma activaba. Los numeros del perfil vuelven a significar
                    // lo que dicen, para todos los rivales.
                    _reactedTo = incoming;
                    float roll = NextFloat();
                    if (roll < _profile.blockChance) _defensive = 1;
                    else if (opponent.CurrentAttackHigh && roll < _profile.blockChance + _profile.dodgeChance) _defensive = 2;
                    else if (roll < _profile.blockChance + _profile.dodgeChance + 0.16f) _defensive = 3;
                    else _defensive = 0;   // ni bloquea ni se cubre: sigue atacando
                }

                switch (_defensive)
                {
                    case 1:
                        _blockTimer = 0.28f + NextFloat() * 0.18f;
                        _defensive = 0;
                        _counterReady = true;
                        intent.block = true;
                        intent.move = 0f;
                        return intent;
                    case 2:
                        _crouchTimer = 0.34f;
                        _defensive = 0;
                        _counterReady = true;
                        intent.crouch = true;
                        // A veces responde con barrido desde la agachada, solo si el
                        // barrido alcanza de verdad (su hitbox es baja y corta).
                        if (CanReach(Attacks.LowKick, opponent, distance) && NextFloat() < 0.35f)
                            intent.lightKick = true;
                        return intent;
                    case 3:
                        _move = -toward;
                        intent.move = _move;
                        return intent;
                }
            }

            if (_decisionTimer > 0f)
            {
                intent.move = _move;
                return intent;
            }

            _decisionTimer = _profile.reaction * (0.8f + NextFloat() * 0.4f);
            _move = 0f;

            // Recolectable: la IA lo disputa si esta mas cerca que el rival. Sin esto el
            // jugador tendria el buffet para el solo y la mecanica no seria una pelea.
            var pickup = self.Combat != null ? self.Combat.NearestPickup(self.transform.position) : null;
            if (pickup != null && !opponent.IsAttacking)
            {
                float pdx = pickup.transform.position.x - self.transform.position.x;
                float pdist = Mathf.Abs(pdx);
                if (pdist > 0.55f && pdist < 4.2f && pdist < distance + 0.8f)
                {
                    _move = Mathf.Sign(pdx);
                    intent.move = _move;
                    return intent;
                }
            }

            // El especial se decide en la ventana de decision (no durante un bloqueo
            // reactivo) y solo si el rival esta dentro del alcance real del poder.
            if (self.SuperReady && self.CanCastSpecial && distance <= self.SpecialRange + 0.15f &&
                NextFloat() < 0.5f + _profile.aggression * 0.35f)
            {
                _attackTimer = _profile.attackCooldown * 1.25f;
                intent.special = true;
                return intent;
            }

            if (distance > _profile.preferredRange + 0.22f)
            {
                _move = toward;
                if (_jumpTimer <= 0f && distance > 2.6f && NextFloat() < 0.06f)
                {
                    intent.jump = true;
                    _jumpTimer = 2.5f;
                }
                if (distance > 2.2f && NextFloat() < 0.14f + _profile.aggression * 0.12f)
                {
                    if (toward > 0f) intent.dashRight = true; else intent.dashLeft = true;
                    // A veces encadena la patada en carrera al sprint.
                    if (NextFloat() < 0.28f) intent.heavyKick = true;
                }
            }
            else if (distance < _profile.preferredRange - 0.35f)
            {
                _move = _profile.aggression < 0.5f ? -toward : toward * 0.2f;
            }
            else
            {
                if (_attackTimer <= 0f && NextFloat() < _profile.aggression)
                {
                    // El golpe se elige por ALCANCE, no por costumbre: el perfil decia
                    // "preferredRange 1.55" y luego elegia un punetazo de 1.40 m, asi que
                    // contra un jugador quieto la IA tiraba manos al aire. Si ningun
                    // ataque llega, se acerca antes de volver a intentarlo.
                    if (ChooseAttack(ref intent, distance, opponent))
                        _attackTimer = _profile.attackCooldown * (0.75f + NextFloat() * 0.5f);
                    else
                        _move = toward;
                }
                else if (NextFloat() < 0.18f)
                {
                    _move = -toward;
                }
            }

            intent.move = _move;
            return intent;
        }

        // Alcance REAL de un ataque contra este rival: el centro de la hitbox esta a
        // `hitboxForward` del atacante y su media anchura + el radio del rival son la
        // tolerancia. Es la misma cuenta que hace Fighter.ResolveHit, asi que la IA no
        // puede decidir con una tabla optimista y fallar por 15 cm.
        static bool CanReach(AttackSpec spec, Fighter opponent, float distance)
        {
            if (spec == null) return false;
            float radius = opponent != null ? opponent.CurrentHurtboxRadius : CombatTuning.DefaultHurtboxRadius;
            float reach = spec.hitboxForward + spec.hitboxWidth * 0.5f + radius;
            return distance <= reach - 0.02f;
        }

        // Elige un ataque que LLEGUE a la distancia actual y devuelve false si ninguno
        // llega. Los pesos conservan el caracter de cada perfil (rapido/fuerte) dentro
        // del subconjunto alcanzable; a distancia larga pesan mas las patadas y a corta
        // los punos. Contra un rival agachado se descartan los golpes altos, que pasan
        // por encima de la hurtbox.
        bool ChooseAttack(ref FighterIntent intent, float distance, Fighter opponent)
        {
            bool crouching = opponent != null && opponent.IsCrouching;
            float wLightPunch = !crouching && CanReach(Attacks.LightPunch, opponent, distance) ? 0.32f : 0f;
            float wHeavyPunch = !crouching && CanReach(Attacks.HeavyPunch, opponent, distance) ? 0.24f : 0f;
            float wLightKick = CanReach(Attacks.LightKick, opponent, distance) ? 0.24f : 0f;
            float wHeavyKick = CanReach(Attacks.HeavyKick, opponent, distance) ? 0.20f : 0f;

            // G -> H: si el ultimo golpe fue rapido, a veces encadena el fuerte (solo si
            // el fuerte llega; si no, cae en el reparto normal).
            if (_lastAttackLight && wHeavyPunch > 0f && NextFloat() < 0.5f)
            {
                intent.heavyPunch = true;
                _lastAttackLight = false;
                return true;
            }

            if (distance > 1.72f)
            {
                // A esta distancia el puno ya no llega: el peso se va a las piernas.
                wHeavyPunch *= 0.4f;
                wLightKick *= 0.8f;
                wHeavyKick *= 2.2f;
            }

            float total = wLightPunch + wHeavyPunch + wLightKick + wHeavyKick;
            if (total <= 0f) return false;

            float roll = NextFloat() * total;
            if ((roll -= wLightPunch) < 0f) intent.lightPunch = true;
            else if ((roll -= wHeavyPunch) < 0f) intent.heavyPunch = true;
            else if ((roll -= wLightKick) < 0f) intent.lightKick = true;
            else intent.heavyKick = true;

            _lastAttackLight = intent.lightPunch;
            return true;
        }

        float NextFloat() => (float)_rng.NextDouble();
    }
}
