using UnityEngine;

namespace KOM
{
    /// <summary>
    /// Capa de impacto en espacio de pantalla.
    ///
    /// Un solo quad hijo de la camara dibuja (ver KOM/ImpactScreen) el flash blanco,
    /// las lineas de foco radiales y la onda de choque que se abre desde el punto
    /// exacto del golpe, proyectado a uv de pantalla.
    ///
    /// Por que un quad y no post-proceso: el post-proceso no sabe DONDE golpeo. Un
    /// golpe en el hombro izquierdo tiene que reventar en el hombro izquierdo; si el
    /// efecto nace en el centro de la pantalla, el ojo lo lee como un flash de daño
    /// generico y no como un impacto. Ademas el quad pasa por el bloom del pipeline,
    /// asi que el nucleo florece sobre el escenario sin costo extra.
    ///
    /// Es deliberadamente BARATO: cero texturas, cero particulas, un draw call. Se
    /// puede emitir en cada golpe de un combo de 7 sin que el frame se resienta.
    /// </summary>
    public sealed class ImpactScreenFx : MonoBehaviour
    {
        const float FlashDuration = 0.13f;
        const float LinesDuration = 0.21f;
        const float RingDuration = 0.34f;
        const float Overscan = 1.30f;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int HotId = Shader.PropertyToID("_HotColor");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int LinesTId = Shader.PropertyToID("_LinesT");
        static readonly int LinesAId = Shader.PropertyToID("_LinesA");
        static readonly int RingTId = Shader.PropertyToID("_RingT");
        static readonly int RingAId = Shader.PropertyToID("_RingA");
        static readonly int CenterId = Shader.PropertyToID("_Center");
        static readonly int AspectId = Shader.PropertyToID("_Aspect");
        static readonly int OverscanId = Shader.PropertyToID("_Overscan");
        static readonly int SpokesId = Shader.PropertyToID("_Spokes");
        static readonly int SeedId = Shader.PropertyToID("_Seed");

        Camera _camera;
        Transform _quad;
        Material _mat;

        float _age = 999f;
        float _flash;
        float _lines;
        float _ring;
        Vector2 _center = new Vector2(0.5f, 0.5f);
        Color _color = Color.white;
        Color _hot = Color.white;
        float _spokes = 26f;

        /// <summary>
        /// Devuelve la capa de pantalla de esta camara, creandola si hace falta. El
        /// objeto vive como hijo de la camara, asi que muere con ella.
        /// </summary>
        public static ImpactScreenFx Ensure(Camera camera)
        {
            if (camera == null) return null;
            var existing = camera.GetComponentInChildren<ImpactScreenFx>(true);
            if (existing != null) return existing;

            var shader = Shader.Find("KOM/ImpactScreen");
            if (shader == null)
            {
                Debug.LogWarning("[KOM] Falta el shader KOM/ImpactScreen: los golpes no tendran flash de pantalla.");
                return null;
            }

            var go = new GameObject("ImpactScreen");
            go.transform.SetParent(camera.transform, false);
            var fx = go.AddComponent<ImpactScreenFx>();
            fx.Build(camera, shader);
            return fx;
        }

        void Build(Camera camera, Shader shader)
        {
            _camera = camera;
            _mat = new Material(shader);
            _mat.name = "ImpactScreen";
            _mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _mat.SetFloat(OverscanId, Overscan);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            KomFx.SafeDestroy(quad.GetComponent<Collider>());
            quad.name = "ImpactScreenQuad";
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.35f);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.allowOcclusionWhenDynamic = false;
            _quad = quad.transform;
            _mat.SetFloat(FlashId, 0f);
            _mat.SetFloat(LinesAId, 0f);
            _mat.SetFloat(RingAId, 0f);
        }

        /// <summary>
        /// Dispara la capa de pantalla. `worldPoint` es el punto real del impacto: se
        /// proyecta a uv para que las lineas nazcan donde golpeo.
        /// `intensity` 0..1.5. Los tres multiplicadores vienen del ESTILO del ataque:
        /// un jab casi no mancha la pantalla; una voladora si. Sin ellos, siete golpes
        /// por segundo convierten la pelea en un estroboscopio.
        /// </summary>
        public void Play(Vector3 worldPoint, Color color, float intensity, bool blocked,
            float flashScale, float linesScale, float ringScale)
        {
            if (_mat == null) return;

            float w = Mathf.Clamp(intensity, 0.05f, 1.6f);
            _color = blocked ? Color.Lerp(color, new Color(0.55f, 0.86f, 1f), 0.75f) : color;
            _hot = Color.Lerp(_color, Color.white, 0.78f);
            // El flash es LOCAL (ver KOM/ImpactScreen): no hay levantamiento global de
            // blanco. Se multiplica por el peso del estilo.
            _flash = Mathf.Max(_flash, (blocked ? 0.55f : 0.85f) * w * Mathf.Clamp01(flashScale));
            _lines = Mathf.Max(_lines, (blocked ? 0.45f : 1.05f) * w * Mathf.Clamp01(linesScale));
            _ring = Mathf.Max(_ring, (blocked ? 0.55f : 0.95f) * w * Mathf.Clamp01(ringScale));
            _spokes = blocked ? 12f : Mathf.Lerp(16f, 26f, Mathf.Clamp01(w - 0.4f));

            var cam = _camera;
            if (cam != null)
            {
                var vp = cam.WorldToViewportPoint(worldPoint);
                // Detras de la camara: se usa el centro para no dibujar un abanico
                // invertido en el borde.
                var target = vp.z > 0f
                    ? new Vector2(Mathf.Clamp01(vp.x), Mathf.Clamp01(vp.y))
                    : new Vector2(0.5f, 0.5f);
                // Si ya hay un pulso vivo, se mezcla: dos golpes seguidos no deben
                // teletransportar el centro de un lado al otro de la pantalla.
                _center = _age < 0.12f ? Vector2.Lerp(_center, target, 0.55f) : target;
            }

            // Un golpe nuevo reinicia los tiempos solo si el anterior ya paso su pico;
            // encadenar reinicios a 8 Hz convertiria el flash en un estroboscopio.
            if (_age > 0.10f) _age = 0f;
            Apply();
        }

        /// <summary>Apagado inmediato (fin de ronda, pausa, KO).</summary>
        public void Clear()
        {
            _age = 999f;
            _flash = _lines = _ring = 0f;
            Apply();
        }

        // Sondas para los gates de verificacion: lo que se esta dibujando AHORA.
        public float DebugFlash => _mat != null ? _mat.GetFloat(FlashId) : 0f;
        public float DebugLines => _mat != null ? _mat.GetFloat(LinesAId) : 0f;
        public float DebugRing => _mat != null ? _mat.GetFloat(RingAId) : 0f;
        public Vector2 DebugCenter => _center;

        void LateUpdate()
        {
            if (_mat == null || _quad == null || _camera == null) return;

            // El quad cubre exactamente la pantalla a la distancia a la que esta. Se
            // recalcula cada frame porque el FOV hace kick en cada impacto.
            float d = Mathf.Max(0.1f, _quad.localPosition.z);
            float h = 2f * d * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * Overscan;
            float w = h * Mathf.Max(0.2f, _camera.aspect);
            _quad.localScale = new Vector3(w, h, 1f);
            _mat.SetFloat(AspectId, Mathf.Max(0.2f, _camera.aspect));

            if (_age < 999f)
            {
                _age += Time.unscaledDeltaTime;
                if (_age > RingDuration + 0.05f)
                {
                    _age = 999f;
                    _flash = _lines = _ring = 0f;
                }
                Apply();
            }
        }

        void Apply()
        {
            if (_mat == null) return;

            // Flash: caida exponencial. Es el unico que llega a "quemar" el frame.
            float fk = Mathf.Clamp01(_age / FlashDuration);
            float flash = _flash * (1f - fk) * (1f - fk);

            float lk = Mathf.Clamp01(_age / LinesDuration);
            float lines = _lines * Mathf.Pow(1f - lk, 1.7f);

            float rk = Mathf.Clamp01(_age / RingDuration);
            float ring = _ring * Mathf.Pow(1f - rk, 1.35f);

            _mat.SetColor(ColorId, _color);
            _mat.SetColor(HotId, _hot);
            _mat.SetFloat(FlashId, flash);
            _mat.SetFloat(LinesAId, lines);
            _mat.SetFloat(LinesTId, lk);
            _mat.SetFloat(RingAId, ring);
            _mat.SetFloat(RingTId, rk);
            _mat.SetFloat(SpokesId, _spokes);
            _mat.SetVector(CenterId, new Vector4(_center.x, _center.y, 0f, 0f));
            _mat.SetFloat(SeedId, Random.value * 10f);
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
