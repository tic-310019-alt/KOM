using UnityEngine;

namespace KOM
{
    public sealed class CombatCameraRig : MonoBehaviour
    {
        // Altura de referencia del luchador para resolver el encuadre (m). Los personajes
        // medidos van de 1.71 a 1.86 m; 1.80 es la media y el numero con el que se define
        // "cuanto ocupa un luchador en pantalla".
        public const float RefFighterHeight = 1.80f;

        Camera _camera;
        Fighter _player;
        Fighter _enemy;
        float _baseDistance = 8f;
        float _baseHeight = 2.1f;
        // Altura a la que mira la camara. Por defecto 1.0 m (el pecho del luchador); un
        // escenario con un decorado alto detras (La Madrugadora) necesita mirar mas
        // arriba para que el letrero entre en cuadro sin alejar la camara y empequenecer
        // a los luchadores.
        float _lookHeight = 1.0f;

        // Encuadre resuelto por datos (ver StageDef.cameraFill). Cuando esta activo, la
        // distancia NO es un numero fijo: se despeja de la cobertura deseada.
        bool _solved;
        float _fov = 34f;
        float _fill = 0.46f;
        float _pitch = 12f;

        Vector3 _velocity;
        Vector3 _lookVelocity;
        Vector3 _basePosition;
        float _shakeAmount;
        float _shakeTimer;
        float _shakeDuration;
        float _shakeDir;
        float _shakeSeed;
        // Acercamiento por impacto (metros). Entra en el frame del golpe y vuelve solo.
        float _punch;

        // Balanceo (roll) sobre el eje de vista. Es la mitad del golpe que no se ve en
        // la traslacion: una camara que solo se desplaza se lee como un temblor de
        // terremoto; una que ademas gira se lee como un golpe.
        float _rollAmount;
        float _rollTimer;
        float _rollDuration;

        // Golpe de FOV: el encuadre se cierra ~2 grados en el frame del impacto y vuelve.
        // Es sutil pero es lo que hace que el mundo "entre" hacia el jugador.
        float _fovBase = 34f;
        float _fovKick;
        float _fovKickTimer;
        float _fovKickDuration;

        // PRESENTACION. Al empezar el asalto la camara arranca mas atras (se ve el
        // escenario) y entra hasta el encuadre de pelea. El factor lo fija el escenario
        // (`cameraIntroZoom`): alejarse de mas descubre los bordes cortados del decorado.
        // Medido con KomStageShot.CaptureIntro (rayos al vacio desde el borde del cuadro):
        // los tres escenarios cubren el cuadro hasta 3.4x, asi que el limite NO es la
        // geometria sino la legibilidad de los luchadores.
        float _introZoom = 1f;
        float _introT = 1f;
        float _introDuration = 1f;
        bool _introHold;

        // Fraccion del tiempo de intro que la camara se queda QUIETA en la panoramica.
        // Sin esta pausa el movimiento arranca en el primer frame y la panoramica no se
        // llega a leer: el ojo solo ve "la camara viene hacia mi".
        const float IntroHoldFraction = 0.18f;

        // Deriva lateral de la presentacion (m). La camara entra en diagonal hacia el
        // centro: eso es lo que la hace leer como panoramica y no como un simple zoom.
        // A 17 m de distancia son ~5 grados, muy por dentro del margen medido.
        const float IntroSway = 1.6f;

        public void Init(Camera camera, Fighter player, Fighter enemy, StageDef stage)
        {
            _camera = camera;
            _player = player;
            _enemy = enemy;
            _baseDistance = stage != null ? stage.cameraDistance : 8f;
            _baseHeight = stage != null ? stage.cameraHeight : 2.1f;
            _lookHeight = stage != null && stage.cameraLookY > 0.01f ? stage.cameraLookY : 1.0f;

            _solved = stage != null && stage.cameraFill > 0.01f;
            if (_solved)
            {
                _fov = stage.cameraFov > 1f ? stage.cameraFov : 34f;
                _fill = Mathf.Clamp(stage.cameraFill, 0.15f, 0.85f);
                _pitch = Mathf.Clamp(stage.cameraPitch, -10f, 45f);
                if (_camera != null) _camera.fieldOfView = _fov;
                _baseDistance = SolveDistance(4.2f);
                _baseHeight = HeightFor(_baseDistance);
            }
            if (_camera != null) _fovBase = _camera.fieldOfView;
            Snap();
        }

        // Distancia que hace que un luchador de RefFighterHeight ocupe `_fill` de la altura
        // de pantalla. La proyeccion vertical se acorta con el pitch (el eje del luchador
        // deja de ser perpendicular al plano imagen), de ahi el coseno.
        //
        // Regla dura: los DOS luchadores tienen que caber. Si se separan, la camara retrocede
        // lo que haga falta (hasta un tope de seguridad); el encuadre pierde cobertura antes
        // que recortar a alguien.
        float SolveDistance(float separation)
        {
            float halfFov = _fov * 0.5f * Mathf.Deg2Rad;
            float apparent = RefFighterHeight * Mathf.Cos(_pitch * Mathf.Deg2Rad);
            float d0 = (apparent / _fill) / (2f * Mathf.Tan(halfFov));

            float aspect = _camera != null && _camera.aspect > 0.01f ? _camera.aspect : 16f / 9f;
            float halfH = Mathf.Atan(Mathf.Tan(halfFov) * aspect);
            // 0.90 m de aire a cada lado: 0.5 m de cuerpo (un brazo extendido ya ocupa eso) y
            // 0.4 m de margen real. Con 0.45 el peor caso de Bellas Artes dejaba 1% de aire.
            float need = (separation * 0.5f + 0.9f) / Mathf.Tan(halfH);
            return Mathf.Clamp(Mathf.Max(d0, need), d0 - 0.4f, MaxDistance);
        }

        const float MaxDistance = 13f;

        float HeightFor(float distance)
        {
            return _solved
                ? _lookHeight + distance * Mathf.Tan(_pitch * Mathf.Deg2Rad)
                : _baseHeight;
        }

        public void Snap()
        {
            if (_player == null || _enemy == null) return;
            var mid = (_player.transform.position.x + _enemy.transform.position.x) * 0.5f;
            float separation = Mathf.Abs(_player.transform.position.x - _enemy.transform.position.x);
            float distance = (_solved ? SolveDistance(separation) : LegacyDistance(separation)) * IntroZoom();
            float height = HeightFor(distance);
            _basePosition = new Vector3(mid * 0.55f + IntroSwayOffset(), height, -distance);
            transform.position = _basePosition;
            transform.rotation = Quaternion.LookRotation(new Vector3(mid * 0.42f, _lookHeight, 0.5f) - _basePosition);
            _velocity = Vector3.zero;
            _punch = 0f;
            _shakeTimer = 0f;
            _shakeAmount = 0f;
            _rollTimer = 0f;
            _rollAmount = 0f;
            _fovKick = 0f;
            _fovKickTimer = 0f;
            if (_camera != null) _camera.fieldOfView = _fovBase;
        }

        float LegacyDistance(float separation)
        {
            return Mathf.Clamp(_baseDistance - 0.15f + separation * 0.33f, _baseDistance - 0.3f, _baseDistance + 1.35f);
        }

        /// <summary>
        /// Presentacion: arranca en `zoom` x la distancia de pelea y entra en `duration`.
        /// El encuadre panoramico depende del escenario (ver StageDef.cameraIntroZoom).
        /// </summary>
        public void PlayIntro(float zoom, float duration)
        {
            _introZoom = Mathf.Max(1f, zoom);
            _introDuration = Mathf.Max(0.05f, duration);
            _introT = 0f;
            _introHold = false;
            Snap();
        }

        /// <summary>Se queda en el encuadre panoramico hasta que se llame a ReleaseIntro.</summary>
        public void HoldIntro(float zoom)
        {
            _introZoom = Mathf.Max(1f, zoom);
            _introT = 0f;
            _introHold = true;
            Snap();
        }

        /// <summary>Entra desde el encuadre panoramico actual hasta el de pelea.</summary>
        public void ReleaseIntro(float duration)
        {
            _introDuration = Mathf.Max(0.05f, duration);
            _introT = 0f;
            _introHold = false;
        }

        float IntroZoom()
        {
            if (_introZoom <= 1.0001f) return 1f;
            return Mathf.Lerp(_introZoom, 1f, IntroProgress());
        }

        // 0 = panoramica, 1 = encuadre de pelea. Curva smoothstep precedida de una pausa:
        // la camara se queda quieta en el plano general y DESPUES entra.
        float IntroProgress()
        {
            if (_introZoom <= 1.0001f) return 1f;
            if (_introHold) return 0f;
            float t = Mathf.Clamp01(_introT);
            t = Mathf.Clamp01((t - IntroHoldFraction) / (1f - IntroHoldFraction));
            return t * t * (3f - 2f * t);
        }

        // Deriva lateral que se apaga con la intro. Solo existe si hay panoramica.
        float IntroSwayOffset()
        {
            if (_introZoom <= 1.0001f) return 0f;
            return IntroSway * (1f - IntroProgress());
        }

        public void Shake(float strength, float duration) => Shake(strength, duration, 0f);

        // `dir` sesga el temblor hacia el lado del golpe: un impacto de derecha a
        // izquierda mueve la camara al contrario. Sin esto todos los golpes se
        // sentian iguales.
        public void Shake(float strength, float duration, float dir)
        {
            _shakeAmount = Mathf.Max(_shakeAmount, strength);
            _shakeDuration = Mathf.Max(_shakeDuration, duration);
            _shakeTimer = Mathf.Max(_shakeTimer, duration);
            _shakeDir = Mathf.Clamp(dir, -1f, 1f);
            // Semilla nueva por impacto: dos golpes seguidos no repiten la misma curva.
            _shakeSeed = Random.value * 100f;
        }

        // Acercamiento de impacto. Se suma sobre el encuadre resuelto, no lo recalcula:
        // el encuadre sigue siendo el que pide el escenario.
        public void Punch(float meters)
        {
            _punch = Mathf.Max(_punch, meters);
        }

        /// <summary>
        /// Sacudida completa de impacto: traslacion, balanceo sobre el eje de vista y
        /// golpe de FOV. Es la version que usan los golpes; <see cref="Shake(float,float,float)"/>
        /// se queda para los avisos suaves (super listo, guardia rota de fondo).
        /// </summary>
        public void ShakeEx(float strength, float duration, float dir, float rollDegrees, float fovKick)
        {
            Shake(strength, duration, dir);
            if (rollDegrees > 0.01f)
            {
                _rollAmount = Mathf.Max(_rollAmount, rollDegrees);
                _rollDuration = Mathf.Max(_rollDuration, duration);
                _rollTimer = Mathf.Max(_rollTimer, duration);
            }
            if (fovKick > 0.01f)
            {
                _fovKick = Mathf.Max(_fovKick, fovKick);
                _fovKickDuration = Mathf.Max(_fovKickDuration, duration);
                _fovKickTimer = Mathf.Max(_fovKickTimer, duration);
            }
        }

        /// <summary>
        /// Fija la presentacion en un punto concreto (0 = panoramica, 1 = encuadre de pelea).
        /// Solo para los bancos de prueba: en edicion no corre LateUpdate y la intro no avanza.
        /// </summary>
        public void DebugSetIntro(float zoom, float progress)
        {
            _introZoom = Mathf.Max(1f, zoom);
            _introDuration = 1f;
            _introHold = false;
            // Se deshace la pausa inicial para que `progress` sea el avance real de la curva.
            float eased = Mathf.Clamp01(progress);
            _introT = IntroHoldFraction + eased * (1f - IntroHoldFraction);
            Snap();
        }

        /// <summary>Sonda de estado de la presentacion (Play).</summary>
        public string DebugState()
        {
            return $"intro zoom={_introZoom:F2} t={_introT:F3} dur={_introDuration:F2} hold={_introHold} " +
                   $"progress={IntroProgress():F3} cam={transform.position.ToString("F2")} " +
                   $"fov={(_camera != null ? _camera.fieldOfView : 0f):F1} solved={_solved}";
        }

        void LateUpdate()
        {
            if (_player == null || _enemy == null || _camera == null) return;

            var p = _player.transform.position;
            var e = _enemy.transform.position;
            float mid = (p.x + e.x) * 0.5f;
            float separation = Mathf.Abs(p.x - e.x);
            if (!_introHold) _introT += Time.unscaledDeltaTime / _introDuration;
            float distance = (_solved ? SolveDistance(separation) : LegacyDistance(separation)) * IntroZoom();
            float height = HeightFor(distance) + Mathf.Clamp01(separation / 8f) * (_solved ? 0.06f : 0.12f);

            var desired = new Vector3(mid * 0.55f + IntroSwayOffset(), height, -distance);
            // Seguimiento mas tenso: con 0.32 s la camara flotaba detras de la accion y
            // el combate se sentia pastoso. 0.16 s la mantiene pegada sin marear.
            _basePosition = Vector3.SmoothDamp(_basePosition, desired, ref _velocity,
                CombatTuning.CameraFollowTime, Mathf.Infinity, Time.unscaledDeltaTime);
            var lookTarget = new Vector3(mid * 0.42f, _lookHeight, 0.5f);
            var pos = _basePosition;

            if (_shakeTimer > 0f)
            {
                _shakeTimer -= Time.unscaledDeltaTime;
                float k = _shakeDuration > 0f ? Mathf.Clamp01(_shakeTimer / _shakeDuration) : 0f;
                k *= k;   // decae rapido: el golpe se siente, no se arrastra
                // Senos desfasados en vez de ruido blanco por frame: el ruido se ve
                // como vibracion de la imagen, esto se ve como un golpe.
                float t = Time.unscaledTime;
                float sx = Mathf.Sin(t * 61f + _shakeSeed) * _shakeAmount * k;
                float sy = Mathf.Sin(t * 47f + _shakeSeed * 1.7f) * _shakeAmount * k * 0.75f;
                pos += new Vector3(sx + _shakeDir * _shakeAmount * k * 0.7f, sy, 0f);
                if (_shakeTimer <= 0f) _shakeAmount = 0f;
            }

            if (_punch > 0.0005f)
            {
                var dir = lookTarget - pos;
                float len = dir.magnitude;
                if (len > 0.001f) pos += dir / len * _punch;
                _punch = Mathf.MoveTowards(_punch, 0f, Time.unscaledDeltaTime * 0.85f);
            }

            transform.position = pos;
            var desiredRot = Quaternion.LookRotation(lookTarget - pos);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot,
                1f - Mathf.Exp(-CombatTuning.CameraRotateSpeed * Time.unscaledDeltaTime));

            // Balanceo de impacto. Se aplica DESPUES de mirar al objetivo porque
            // LookRotation devuelve roll 0 y borraria el efecto. Amplitud maxima ~4.5
            // grados: mas que eso marea y ademas descubre el encuadre.
            if (_rollTimer > 0f)
            {
                _rollTimer -= Time.unscaledDeltaTime;
                float rk = _rollDuration > 0f ? Mathf.Clamp01(_rollTimer / _rollDuration) : 0f;
                rk *= rk;
                float t = Time.unscaledTime;
                float roll = Mathf.Sin(t * 38f + _shakeSeed) * _rollAmount * rk;
                // Un segundo armonico mas lento evita el aspecto de "vibracion de motor".
                roll += Mathf.Sin(t * 17f + _shakeSeed * 2.3f) * _rollAmount * rk * 0.35f;
                transform.rotation *= Quaternion.Euler(0f, 0f, roll);
                if (_rollTimer <= 0f) _rollAmount = 0f;
            }

            // Golpe de FOV: cierra el encuadre y vuelve. La curva es cuadratica para
            // que el pico caiga en el PRIMER frame del impacto, que es cuando el ojo
            // esta mirando el punto de contacto.
            if (_fovKickTimer > 0f)
            {
                _fovKickTimer -= Time.unscaledDeltaTime;
                float fk = _fovKickDuration > 0f ? Mathf.Clamp01(_fovKickTimer / _fovKickDuration) : 0f;
                _camera.fieldOfView = _fovBase - _fovKick * fk * fk;
                if (_fovKickTimer <= 0f)
                {
                    _fovKick = 0f;
                    _camera.fieldOfView = _fovBase;
                }
            }
        }
    }
}
