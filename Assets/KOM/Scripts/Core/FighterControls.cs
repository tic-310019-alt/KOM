using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace KOM
{
    /// <summary>
    /// Mandos de un luchador humano. Dos personas pueden pelear en la misma maquina:
    /// cada una lee su mitad del teclado y su propio mando, sin pisarse.
    ///
    /// Reparto de teclado (pensado para jugar de lado a lado en un solo teclado):
    ///   · JUGADOR 1 — mano izquierda: A/D, W, S y Shift izquierdo.
    ///                 individual: G/H/J/K + U.
    ///                 versus:     C/V/B/N + M (la fila inferior, mas comoda al lado de A/D).
    ///   · JUGADOR 2 — mano derecha: flechas y Shift derecho.
    ///                 mano izquierda: U/I/O/P y Y para el especial.
    ///                 (el teclado numerico tambien vale: 1/2/4/5 y 0)
    ///
    /// Mandos: con dos o mas conectados, el primero es de J1 y el segundo de J2. Con uno
    /// solo, en individual es de J1 y en versus es del invitado (J2), que es el reparto
    /// que espera quien pone una laptop y un mando sobre la mesa. La asignacion se
    /// resuelve por frame y conserva el dispositivo por id, asi que conectar o
    /// desconectar un mando a mitad de partida no rompe los controles: el que se queda
    /// sin mando sigue en el teclado.
    /// </summary>
    public sealed class FighterControls
    {
        readonly int _player;          // 0 = J1 · 1 = J2
        readonly bool _versus;

        const float DoubleTapWindow = 0.28f;
        float _lastLeftTap = -10f;
        float _lastRightTap = -10f;
        int _dashFrame = -1;
        bool _dashLeft;
        bool _dashRight;

        int _padFrame = -1;
        Gamepad _pad;

        // Asignacion estable por dispositivo. Es static a proposito: el mando del jugador
        // no debe cambiar al pasar de la seleccion al mapa o al combate.
        static bool _assignmentInit;
        static bool _assignmentVersus;
        static readonly int[] _slotDeviceId = { -1, -1 };

        public FighterControls(int player, bool versus)
        {
            _player = player <= 0 ? 0 : 1;
            _versus = versus;
        }

        /// <summary>Luchador individual (o J1 en una partida de un solo jugador).</summary>
        public static FighterControls First() => new FighterControls(0, false);
        /// <summary>Invitado de la misma maquina.</summary>
        public static FighterControls Second() => new FighterControls(1, true);

        public bool HasPad => Pad != null;
        public string DeviceLabel => HasPad ? "MANDO" : "TECLADO";

        static Keyboard K => Keyboard.current;

        Gamepad Pad
        {
            get
            {
                if (_padFrame != Time.frameCount)
                {
                    _padFrame = Time.frameCount;
                    _pad = ResolvePad(_player, _versus);
                }
                return _pad;
            }
        }

        static void EnsureAssignmentMode(bool versus)
        {
            if (_assignmentInit && _assignmentVersus == versus) return;
            _assignmentInit = true;
            _assignmentVersus = versus;
            _slotDeviceId[0] = -1;
            _slotDeviceId[1] = -1;
        }

        static void PruneDisconnected()
        {
            var pads = Gamepad.all;
            for (int slot = 0; slot < 2; slot++)
            {
                int id = _slotDeviceId[slot];
                if (id < 0) continue;
                bool alive = false;
                for (int i = 0; i < pads.Count; i++)
                {
                    if (pads[i].deviceId == id) { alive = true; break; }
                }
                if (!alive) _slotDeviceId[slot] = -1;
            }
        }

        static Gamepad PadById(int id)
        {
            if (id < 0) return null;
            var pads = Gamepad.all;
            for (int i = 0; i < pads.Count; i++)
                if (pads[i].deviceId == id) return pads[i];
            return null;
        }

        static bool IsClaimed(Gamepad pad)
        {
            return pad != null && (_slotDeviceId[0] == pad.deviceId || _slotDeviceId[1] == pad.deviceId);
        }

        /// <summary>
        /// Mando del jugador para este frame. `null` significa que esa persona juega en
        /// teclado. No lanza: cualquier combinacion de 0, 1 o N mandos es valida.
        /// </summary>
        public static Gamepad ResolvePad(int player, bool versus)
        {
            EnsureAssignmentMode(versus);
            PruneDisconnected();

            var pads = Gamepad.all;
            if (pads.Count == 0) return null;

            if (!versus)
            {
                if (player != 0) return null;
                var assigned = PadById(_slotDeviceId[0]);
                if (assigned != null) return assigned;
                _slotDeviceId[0] = pads[0].deviceId;
                return pads[0];
            }

            // En versus cada jugador conserva su mando mientras siga conectado.
            var own = PadById(_slotDeviceId[player]);
            if (own != null) return own;

            if (pads.Count == 1)
            {
                // Un unico mando: es del invitado. Si ya pertenece al otro jugador se
                // respeta (acaba de desconectarse el suyo y no se roba el que queda).
                if (IsClaimed(pads[0])) return null;
                _slotDeviceId[1] = pads[0].deviceId;
                return player == 1 ? pads[0] : null;
            }

            // Dos o mas: J1 prefiere el primero y J2 el segundo, sin depender del orden
            // en que los cerebros consulten en el frame.
            var preferred = pads[Mathf.Clamp(player, 0, pads.Count - 1)];
            if (!IsClaimed(preferred))
            {
                _slotDeviceId[player] = preferred.deviceId;
                return preferred;
            }
            for (int i = 0; i < pads.Count; i++)
            {
                if (IsClaimed(pads[i])) continue;
                _slotDeviceId[player] = pads[i].deviceId;
                return pads[i];
            }
            return null;
        }

        // ------------------------------------------------------------------ movimiento

        public float MoveAxis()
        {
            float value = 0f;
            var k = K;
            if (k != null)
            {
                if (_player == 0)
                {
                    if (k.dKey.isPressed) value += 1f;
                    if (k.aKey.isPressed) value -= 1f;
                }
                else
                {
                    if (k.rightArrowKey.isPressed) value += 1f;
                    if (k.leftArrowKey.isPressed) value -= 1f;
                }
            }
            var pad = Pad;
            if (pad != null)
            {
                float stick = pad.leftStick.ReadValue().x;
                if (Mathf.Abs(stick) > 0.25f) value += stick;
                if (pad.dpad.right.isPressed) value += 1f;
                if (pad.dpad.left.isPressed) value -= 1f;
            }
            return Mathf.Clamp(value, -1f, 1f);
        }

        public bool MoveLeftPressed()
        {
            var k = K;
            if (k != null && (_player == 0 ? k.aKey.wasPressedThisFrame : k.leftArrowKey.wasPressedThisFrame)) return true;
            var pad = Pad;
            return pad != null && pad.dpad.left.wasPressedThisFrame;
        }

        public bool MoveRightPressed()
        {
            var k = K;
            if (k != null && (_player == 0 ? k.dKey.wasPressedThisFrame : k.rightArrowKey.wasPressedThisFrame)) return true;
            var pad = Pad;
            return pad != null && pad.dpad.right.wasPressedThisFrame;
        }

        public bool JumpPressed()
        {
            var k = K;
            if (k != null && (_player == 0 ? k.wKey.wasPressedThisFrame : k.upArrowKey.wasPressedThisFrame)) return true;
            var pad = Pad;
            return pad != null && pad.buttonSouth.wasPressedThisFrame;
        }

        public bool CrouchHeld()
        {
            var k = K;
            if (k != null && (_player == 0 ? k.sKey.isPressed : k.downArrowKey.isPressed)) return true;
            var pad = Pad;
            if (pad == null) return false;
            return pad.dpad.down.isPressed || pad.leftStick.ReadValue().y < -0.55f;
        }

        public bool BlockHeld()
        {
            var k = K;
            if (k != null)
            {
                if (_player == 0 && (k.leftShiftKey.isPressed || k.lKey.isPressed)) return true;
                if (_player == 1 && k.rightShiftKey.isPressed) return true;
            }
            var pad = Pad;
            if (pad == null) return false;
            return pad.leftShoulder.isPressed || pad.leftTrigger.ReadValue() > 0.4f;
        }

        // ------------------------------------------------------------------ ataques

        static bool Pressed(KeyControl key) => key != null && key.wasPressedThisFrame;
        static bool Held(KeyControl key) => key != null && key.isPressed;

        // Estado SOSTENIDO del boton, no el flanco. Lo usa el buffer: una pulsacion que
        // todavia no puede salir sigue viva mientras el dedo siga apretando (modelo de
        // Smash Ultimate). Sin esto el jugador que aprieta un poco pronto no recibe nada
        // y solo le queda la sensacion de que el juego se comio la tecla.
        public bool AttackHeld(AttackButton button)
        {
            var k = K;
            if (k != null)
            {
                if (_player == 0)
                {
                    switch (button)
                    {
                        case AttackButton.LightPunch: if (Held(_versus ? k.cKey : k.gKey)) return true; break;
                        case AttackButton.HeavyPunch: if (Held(_versus ? k.vKey : k.hKey)) return true; break;
                        case AttackButton.LightKick: if (Held(_versus ? k.bKey : k.jKey)) return true; break;
                        default: if (Held(_versus ? k.nKey : k.kKey)) return true; break;
                    }
                }
                else
                {
                    switch (button)
                    {
                        case AttackButton.LightPunch: if (Held(k.uKey) || Held(k.numpad1Key)) return true; break;
                        case AttackButton.HeavyPunch: if (Held(k.iKey) || Held(k.numpad2Key)) return true; break;
                        case AttackButton.LightKick: if (Held(k.oKey) || Held(k.numpad4Key)) return true; break;
                        default: if (Held(k.pKey) || Held(k.numpad5Key)) return true; break;
                    }
                }
            }
            var pad = Pad;
            if (pad == null) return false;
            switch (button)
            {
                case AttackButton.LightPunch: return pad.buttonWest.isPressed;
                case AttackButton.HeavyPunch: return pad.buttonNorth.isPressed;
                case AttackButton.LightKick: return pad.buttonEast.isPressed;
                default: return pad.rightShoulder.isPressed;
            }
        }

        public bool LightPunchPressed()
        {
            var k = K;
            if (k != null)
            {
                if (_player == 0 && Pressed(_versus ? k.cKey : k.gKey)) return true;
                if (_player == 1 && (Pressed(k.uKey) || Pressed(k.numpad1Key))) return true;
            }
            var pad = Pad;
            return pad != null && pad.buttonWest.wasPressedThisFrame;
        }

        public bool HeavyPunchPressed()
        {
            var k = K;
            if (k != null)
            {
                if (_player == 0 && Pressed(_versus ? k.vKey : k.hKey)) return true;
                if (_player == 1 && (Pressed(k.iKey) || Pressed(k.numpad2Key))) return true;
            }
            var pad = Pad;
            return pad != null && pad.buttonNorth.wasPressedThisFrame;
        }

        public bool LightKickPressed()
        {
            var k = K;
            if (k != null)
            {
                if (_player == 0 && Pressed(_versus ? k.bKey : k.jKey)) return true;
                if (_player == 1 && (Pressed(k.oKey) || Pressed(k.numpad4Key))) return true;
            }
            var pad = Pad;
            return pad != null && pad.buttonEast.wasPressedThisFrame;
        }

        public bool HeavyKickPressed()
        {
            var k = K;
            if (k != null)
            {
                if (_player == 0 && Pressed(_versus ? k.nKey : k.kKey)) return true;
                if (_player == 1 && (Pressed(k.pKey) || Pressed(k.numpad5Key))) return true;
            }
            var pad = Pad;
            return pad != null && pad.rightShoulder.wasPressedThisFrame;
        }

        public bool SpecialPressed()
        {
            var k = K;
            if (k != null)
            {
                // Campana: U (la tecla de siempre; T fue un cambio descartado).
                // Versus: M, porque U es el golpe rapido de J2.
                if (_player == 0 && Pressed(_versus ? k.mKey : k.uKey)) return true;
                if (_player == 1 && (Pressed(k.yKey) || Pressed(k.numpad0Key))) return true;
            }
            var pad = Pad;
            if (pad == null) return false;
            return pad.selectButton.wasPressedThisFrame || pad.rightStickButton.wasPressedThisFrame;
        }

        // ------------------------------------------------------------------ menus

        public bool MenuUpPressed()
        {
            var k = K;
            if (k != null && (_player == 0 ? k.wKey.wasPressedThisFrame : k.upArrowKey.wasPressedThisFrame)) return true;
            var pad = Pad;
            if (pad == null) return false;
            return pad.dpad.up.wasPressedThisFrame || pad.leftStick.ReadValue().y > 0.55f;
        }

        public bool MenuDownPressed()
        {
            var k = K;
            if (k != null && (_player == 0 ? k.sKey.wasPressedThisFrame : k.downArrowKey.wasPressedThisFrame)) return true;
            var pad = Pad;
            if (pad == null) return false;
            return pad.dpad.down.wasPressedThisFrame || pad.leftStick.ReadValue().y < -0.55f;
        }

        /// <summary>
        /// Confirmar en menus (seleccion, mapa, resultado). Es el mismo boton que el golpe
        /// rapido, como en un juego de pelea: la mano ya esta ahi. En teclado J1 tambien
        /// acepta Enter/Espacio y J2 el Enter del teclado numerico.
        /// </summary>
        public bool MenuConfirmPressed()
        {
            var k = K;
            if (k != null)
            {
                if (_player == 0)
                {
                    if (Pressed(_versus ? k.cKey : k.gKey) || Pressed(k.enterKey) || Pressed(k.spaceKey)) return true;
                }
                else if (Pressed(k.uKey) || Pressed(k.numpad1Key) || Pressed(k.numpadEnterKey)) return true;
            }
            var pad = Pad;
            return pad != null && pad.buttonSouth.wasPressedThisFrame;
        }

        /// <summary>
        /// Cambiar / deshacer en menus. Es la patada rapida. Escape solo para J1 (salir
        /// de la pantalla); J2 usa O para no cerrar el modo sin querer con el mando.
        /// </summary>
        public bool MenuCancelPressed()
        {
            var k = K;
            if (k != null)
            {
                if (_player == 0)
                {
                    if (Pressed(_versus ? k.bKey : k.jKey) || Pressed(k.escapeKey) || Pressed(k.backspaceKey)) return true;
                }
                else if (Pressed(k.oKey) || Pressed(k.numpad4Key)) return true;
            }
            var pad = Pad;
            return pad != null && pad.buttonEast.wasPressedThisFrame;
        }

        /// <summary>
        /// Sondea el doble toque una vez por frame. Se llama siempre, también cuando el
        /// luchador no puede actuar: si solo se leyera al decidir, el primer toque de un
        /// doble toque durante un ataque se perdería.
        /// </summary>
        public void PollDash()
        {
            int frame = Time.frameCount;
            if (_dashFrame == frame) return;
            _dashFrame = frame;
            _dashLeft = false;
            _dashRight = false;

            float now = Time.unscaledTime;
            if (TapLeft())
            {
                if (now - _lastLeftTap <= DoubleTapWindow) { _dashLeft = true; _lastLeftTap = -10f; }
                else _lastLeftTap = now;
            }
            if (TapRight())
            {
                if (now - _lastRightTap <= DoubleTapWindow) { _dashRight = true; _lastRightTap = -10f; }
                else _lastRightTap = now;
            }
        }

        bool TapLeft()
        {
            var k = K;
            if (k != null && (_player == 0 ? k.aKey.wasPressedThisFrame : k.leftArrowKey.wasPressedThisFrame)) return true;
            var pad = Pad;
            return pad != null && pad.dpad.left.wasPressedThisFrame;
        }

        bool TapRight()
        {
            var k = K;
            if (k != null && (_player == 0 ? k.dKey.wasPressedThisFrame : k.rightArrowKey.wasPressedThisFrame)) return true;
            var pad = Pad;
            return pad != null && pad.dpad.right.wasPressedThisFrame;
        }

        public bool DashLeftPressed()
        {
            PollDash();
            return _dashLeft;
        }

        public bool DashRightPressed()
        {
            PollDash();
            return _dashRight;
        }

        public bool SprintHeld() => Pad != null && Pad.rightTrigger.ReadValue() > 0.4f;

        public bool SprintPressed() => Pad != null && Pad.rightTrigger.wasPressedThisFrame;
    }
}
