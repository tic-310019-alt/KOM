using UnityEngine;
using UnityEngine.InputSystem;

namespace KOM
{
    public static class InputReader
    {
        public static bool AnyDevice => Keyboard.current != null || Gamepad.current != null;

        public static float MoveAxis()
        {
            float value = 0f;
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) value += 1f;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) value -= 1f;
            }
            var g = Gamepad.current;
            if (g != null)
            {
                float stick = g.leftStick.ReadValue().x;
                if (Mathf.Abs(stick) > 0.25f) value += stick;
                if (g.dpad.right.isPressed) value += 1f;
                if (g.dpad.left.isPressed) value -= 1f;
            }
            return Mathf.Clamp(value, -1f, 1f);
        }

        public static bool MoveLeftPressed()
        {
            var k = Keyboard.current;
            if (k != null && (k.aKey.wasPressedThisFrame || k.leftArrowKey.wasPressedThisFrame)) return true;
            var g = Gamepad.current;
            return g != null && g.dpad.left.wasPressedThisFrame;
        }

        public static bool MoveRightPressed()
        {
            var k = Keyboard.current;
            if (k != null && (k.dKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame)) return true;
            var g = Gamepad.current;
            return g != null && g.dpad.right.wasPressedThisFrame;
        }

        public static bool MoveLeftHeld => MoveAxis() < -0.4f;
        public static bool MoveRightHeld => MoveAxis() > 0.4f;

        // Navegacion VERTICAL de menus. Se mantiene aparte de MoveAxis para no tocar la
        // locomocion del combate, que solo mira el eje horizontal.
        public static bool MoveUpPressed()
        {
            var k = Keyboard.current;
            if (k != null && (k.wKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame)) return true;
            var g = Gamepad.current;
            if (g == null) return false;
            return g.dpad.up.wasPressedThisFrame || g.leftStick.ReadValue().y > 0.55f;
        }

        public static bool MoveDownPressed()
        {
            var k = Keyboard.current;
            if (k != null && (k.sKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame)) return true;
            var g = Gamepad.current;
            if (g == null) return false;
            return g.dpad.down.wasPressedThisFrame || g.leftStick.ReadValue().y < -0.55f;
        }

        public static bool ConfirmPressed()
        {
            var k = Keyboard.current;
            if (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame ||
                              k.spaceKey.wasPressedThisFrame)) return true;
            var g = Gamepad.current;
            return g != null && (g.buttonSouth.wasPressedThisFrame || g.startButton.wasPressedThisFrame);
        }

        public static bool CancelPressed()
        {
            var k = Keyboard.current;
            if (k != null && k.escapeKey.wasPressedThisFrame) return true;
            var g = Gamepad.current;
            return g != null && g.buttonEast.wasPressedThisFrame;
        }

        public static bool JumpPressed()
        {
            var k = Keyboard.current;
            if (k != null && (k.wKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame)) return true;
            var g = Gamepad.current;
            return g != null && g.buttonSouth.wasPressedThisFrame;
        }

        public static bool CrouchHeld()
        {
            var k = Keyboard.current;
            if (k != null && (k.sKey.isPressed || k.downArrowKey.isPressed)) return true;
            var g = Gamepad.current;
            if (g == null) return false;
            return g.dpad.down.isPressed || g.leftStick.ReadValue().y < -0.55f;
        }

        public static bool BlockHeld()
        {
            var k = Keyboard.current;
            if (k != null && (k.lKey.isPressed || k.leftShiftKey.isPressed)) return true;
            var g = Gamepad.current;
            if (g == null) return false;
            return g.leftShoulder.isPressed || g.leftTrigger.ReadValue() > 0.4f;
        }

        // Correr: DOBLE TOQUE de A/D (o flecha izquierda/derecha). Cada dirección lleva su
        // propio cronómetro, así que tocar A y luego D no encadena una carrera, y mantener
        // la tecla no cuenta como segundo toque. El mando conserva el gatillo derecho.
        //
        // Se sondea una vez por frame y se cachea: `Think` puede llamarse más de una vez
        // (pruebas, dos luchadores con cerebro de jugador) y sin caché el segundo toque se
        // consumiría dos veces.
        const float DoubleTapWindow = 0.28f;

        static float _lastLeftTap = -10f;
        static float _lastRightTap = -10f;
        static int _dashFrame = -1;
        static bool _dashLeft;
        static bool _dashRight;

        /// <summary>
        /// Sondea el doble toque una vez por frame. Se llama desde el cerebro del jugador
        /// SIEMPRE (también cuando no puede actuar): si solo se leyera al decidir, el
        /// primer toque de un doble toque durante un ataque se perdería.
        /// </summary>
        public static void PollDash()
        {
            int frame = Time.frameCount;
            if (_dashFrame == frame) return;
            _dashFrame = frame;
            _dashLeft = false;
            _dashRight = false;

            // Tiempo NO escalado: el hitstop congela a los luchadores, no al jugador.
            float now = Time.unscaledTime;
            if (TapLeft())
            {
                // Un doble toque consume el primero: un tercer toque no encadena dos carreras.
                if (now - _lastLeftTap <= DoubleTapWindow) { _dashLeft = true; _lastLeftTap = -10f; }
                else _lastLeftTap = now;
            }
            if (TapRight())
            {
                if (now - _lastRightTap <= DoubleTapWindow) { _dashRight = true; _lastRightTap = -10f; }
                else _lastRightTap = now;
            }
        }

        static bool TapLeft()
        {
            var k = Keyboard.current;
            if (k != null && (k.aKey.wasPressedThisFrame || k.leftArrowKey.wasPressedThisFrame)) return true;
            var g = Gamepad.current;
            return g != null && g.dpad.left.wasPressedThisFrame;
        }

        static bool TapRight()
        {
            var k = Keyboard.current;
            if (k != null && (k.dKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame)) return true;
            var g = Gamepad.current;
            return g != null && g.dpad.right.wasPressedThisFrame;
        }

        public static bool DashLeftPressed()
        {
            PollDash();
            return _dashLeft;
        }

        public static bool DashRightPressed()
        {
            PollDash();
            return _dashRight;
        }

        // Mando: gatillo derecho + dirección. En teclado ya NO hay sprint por modificador:
        // correr es el doble toque de arriba.
        public static bool SprintHeld()
        {
            var g = Gamepad.current;
            return g != null && g.rightTrigger.ReadValue() > 0.4f;
        }

        public static bool SprintPressed()
        {
            var g = Gamepad.current;
            return g != null && g.rightTrigger.wasPressedThisFrame;
        }

        public static bool LightPunchPressed()
        {
            var k = Keyboard.current;
            if (k != null && k.gKey.wasPressedThisFrame) return true;
            var g = Gamepad.current;
            return g != null && g.buttonWest.wasPressedThisFrame;
        }

        public static bool HeavyPunchPressed()
        {
            var k = Keyboard.current;
            if (k != null && k.hKey.wasPressedThisFrame) return true;
            var g = Gamepad.current;
            return g != null && g.buttonNorth.wasPressedThisFrame;
        }

        public static bool LightKickPressed()
        {
            var k = Keyboard.current;
            if (k != null && k.jKey.wasPressedThisFrame) return true;
            var g = Gamepad.current;
            return g != null && g.buttonEast.wasPressedThisFrame;
        }

        public static bool HeavyKickPressed()
        {
            var k = Keyboard.current;
            if (k != null && k.kKey.wasPressedThisFrame) return true;
            var g = Gamepad.current;
            return g != null && g.rightShoulder.wasPressedThisFrame;
        }

        // Especial: U en teclado (la tecla historica del modo campana), SELECT en mando.
        // Solo se dispara con la barra llena.
        public static bool SpecialPressed()
        {
            var k = Keyboard.current;
            if (k != null && k.uKey.wasPressedThisFrame) return true;
            var g = Gamepad.current;
            return g != null && (g.selectButton.wasPressedThisFrame || g.rightStickButton.wasPressedThisFrame);
        }

        // Debug explicito para probar la carga sin esperar al combate (F2 = P1, F3 = ambos).
        public static bool DebugFillSuperPressed()
        {
            var k = Keyboard.current;
            return k != null && k.f2Key.wasPressedThisFrame;
        }

        public static bool DebugFillBothSupersPressed()
        {
            var k = Keyboard.current;
            return k != null && k.f3Key.wasPressedThisFrame;
        }

        // Debug de hitboxes: F4. Antes era F1, la MISMA tecla que abre la referencia de
        // controles, asi que pedir la ayuda encendia tambien el dibujo de cajas.
        public static bool DebugTogglePressed()
        {
            var k = Keyboard.current;
            return k != null && k.f4Key.wasPressedThisFrame;
        }

        public static bool PausePressed()
        {
            var k = Keyboard.current;
            if (k != null && k.escapeKey.wasPressedThisFrame) return true;
            // Cualquier mando puede pausar: en versus el de P2 no es Gamepad.current.
            var pads = Gamepad.all;
            for (int i = 0; i < pads.Count; i++)
                if (pads[i].startButton.wasPressedThisFrame) return true;
            return false;
        }

        // Referencia de controles a mano en cualquier momento del combate (F1 / Tab / R3).
        // SELECT queda libre para el especial del jugador.
        public static bool ControlsPressed()
        {
            var k = Keyboard.current;
            if (k != null && (k.f1Key.wasPressedThisFrame || k.tabKey.wasPressedThisFrame)) return true;
            var pads = Gamepad.all;
            for (int i = 0; i < pads.Count; i++)
                if (pads[i].rightStickButton.wasPressedThisFrame) return true;
            return false;
        }
    }
}
