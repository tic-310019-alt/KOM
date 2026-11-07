using UnityEngine;

namespace KOM
{
    public static class KomFx
    {
        static Texture2D _ringTex;
        static Texture2D _softRingTex;
        static Texture2D _glowTex;
        static Material _shadowMat;
        static Material _particleMat;

        public static void SafeDestroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }

        public static Texture2D GlowTexture => _glowTex != null ? _glowTex : _glowTex = MakeRadialTexture(128, 2.4f);

        public static Texture2D RingTexture => _ringTex != null ? _ringTex : _ringTex = MakeRingTexture(256, 0.78f, 1f, 0.05f);
        // Anillo ANCHO y difuso: para ondas de choque. El fino se lee como un aro
        // duro de plastico; este se lee como una onda de aire.
        public static Texture2D SoftRingTexture => _softRingTex != null ? _softRingTex : _softRingTex = MakeRingTexture(256, 0.35f, 1f, 0.42f);

        public static void ResetCache()
        {
            _ringTex = null;
            _softRingTex = null;
            _glowTex = null;
            _shadowMat = null;
            _particleMat = null;
        }

        static Texture2D MakeRadialTexture(int size, float power)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float ny = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(d, power)));
                }
            }
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.Apply();
            return tex;
        }

        static Texture2D MakeRingTexture(int size, float inner, float outer, float feather)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float ny = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float a = Mathf.Clamp01(Mathf.Min((r - inner) / feather, (outer - r) / feather));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.Apply();
            return tex;
        }

        // URP Particles/Unlit nace opaco: sin esto las particulas con textura radial
        // se ven como cuadrados opacos. Alpha blend suave + ZWrite off.
        public static void ConfigureParticleMaterial(Material mat)
        {
            if (mat == null) return;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", 1f);
            if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", 10f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_ALPHAMODULATE_ON");
            mat.renderQueue = 3000;
        }

        static Material MakeSpriteMaterial(Texture texture, Color color)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            var mat = new Material(shader);
            mat.mainTexture = texture;
            mat.color = color;
            mat.renderQueue = 3000;
            mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return mat;
        }

        // Anillo de impacto: crece y se apaga en ~0.2 s. Es lo que convierte el
        // contacto en un golpe legible; sin el, dos mallas se cruzan y no pasa nada.
        public static void ImpactRing(Vector3 position, Color color, float scale = 1f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            SafeDestroy(go.GetComponent<Collider>());
            go.name = "ImpactRing";
            go.transform.position = position;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = MakeSpriteMaterial(RingTexture, color);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = 4;
            var ring = go.AddComponent<KomImpactRing>();
            ring.Init(mr.sharedMaterial, color, Mathf.Max(0.15f, scale));
        }

        public static GameObject CreateGroundShadow(Transform parent, float radius = 1.1f, float alpha = 0.55f)
        {
            if (_shadowMat == null) _shadowMat = MakeSpriteMaterial(GlowTexture, new Color(0f, 0f, 0f, alpha));
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            SafeDestroy(go.GetComponent<Collider>());
            go.name = "GroundShadow";
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            go.transform.localScale = new Vector3(radius * 2.2f, radius * 2.2f, 1f);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _shadowMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        public static GameObject CreateGroundRing(Transform parent, float radius, Color color, float alpha = 0.7f)
        {
            var mat = MakeSpriteMaterial(RingTexture, new Color(color.r, color.g, color.b, alpha));
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            SafeDestroy(go.GetComponent<Collider>());
            go.name = "GroundRing";
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            go.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 1;
            return go;
        }

        public static void HitBurst(Vector3 position, Color color)
        {
            var go = new GameObject("HitBurst");
            go.SetActive(false);
            go.transform.position = position;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.32f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
            main.startColor = color;
            main.gravityModifier = 0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (_particleMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                _particleMat = new Material(shader);
                _particleMat.mainTexture = GlowTexture;
                ConfigureParticleMaterial(_particleMat);
            }
            var mat = new Material(_particleMat);
            mat.color = Color.white;
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            go.SetActive(true);
            ps.Play();
            Object.Destroy(go, 1.2f);
            Object.Destroy(mat, 1.3f);
        }

        public static GameObject CreateFloorDisc(Transform parent, float radius, float height, Color color, bool reflections = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            SafeDestroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, -height * 0.5f, 0f);
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = StageBuilder.GetMaterial(color, 0.55f, 0.18f, reflections);
            return go;
        }
    }

    // Anillo de impacto autogestionado: se orienta a camara, crece y se apaga.
    // Vive 0.2 s y se destruye con su material (no deja basura entre golpes).
    public sealed class KomImpactRing : MonoBehaviour
    {
        const float Life = 0.20f;

        static Camera _camera;

        Material _mat;
        Color _color = Color.white;
        Transform _t;
        float _age;
        float _scale = 1f;

        public void Init(Material mat, Color color, float scale)
        {
            _mat = mat;
            _color = color;
            _scale = Mathf.Max(0.15f, scale);
            _t = transform;
            if (_mat != null) _mat.hideFlags = HideFlags.None;
            _t.localScale = new Vector3(0.32f * _scale, 0.32f * _scale, 1f);
        }

        void Update()
        {
            if (_t == null)
            {
                Destroy(gameObject);
                return;
            }
            _age += Time.deltaTime;
            float k = Mathf.Clamp01(_age / Life);
            float s = Mathf.Lerp(0.32f, 1.35f, k) * _scale;
            _t.localScale = new Vector3(s, s, 1f);

            if (_camera == null) _camera = Camera.main;
            if (_camera != null) _t.rotation = _camera.transform.rotation;

            if (_mat != null)
            {
                var c = _color;
                c.a = _color.a * (1f - k) * (1f - k);
                _mat.color = c;
            }
            if (k >= 1f)
            {
                if (_mat != null) Destroy(_mat);
                Destroy(gameObject);
            }
        }
    }
}
