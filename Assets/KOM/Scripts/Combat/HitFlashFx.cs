using System.Collections.Generic;
using UnityEngine;

namespace KOM
{
    /// <summary>
    /// Destello de impacto sobre el CUERPO del luchador.
    ///
    /// Se construye una vez por personaje (no por golpe): copia cada
    /// SkinnedMeshRenderer y lo dibuja con KOM/HitFlash en aditivo. El coste en reposo
    /// es cero porque las cascaras se desactivan cuando la intensidad llega a 0.
    ///
    /// Es la pieza que hace que el golpe "entre" en el personaje. Sin ella, por mucho
    /// que tiemble la camara, el que recibe sigue siendo una malla intacta y el
    /// cerebro lee "atravesado", no "golpeado".
    ///
    /// Se usa la MISMA tecnica que el aura de super y el escudo: cascara sobre copia
    /// del SMR, jamas cambiar los materiales del personaje (los comparte el prefab y
    /// el cambio se propagaria a todas las instancias y al selector).
    /// </summary>
    public sealed class HitFlashFx : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int RimId = Shader.PropertyToID("_Rim");

        readonly List<SkinnedMeshRenderer> _shells = new List<SkinnedMeshRenderer>();

        Material _mat;
        Fighter _owner;
        float _intensity;
        float _peak;
        float _rim;
        float _rimTarget;
        // Curva de caida en segundos: un flash corto (0.09 s) se lee como impacto;
        // uno largo (0.3 s) se lee como que el personaje se ha vuelto de mármol.
        float _decay = 12f;

        public static HitFlashFx Attach(Fighter owner)
        {
            if (owner == null) return null;
            var go = new GameObject("HitFlash");
            go.transform.SetParent(owner.transform, false);
            var fx = go.AddComponent<HitFlashFx>();
            fx.Build(owner);
            return fx;
        }

        void Build(Fighter owner)
        {
            _owner = owner;
            var shader = Shader.Find("KOM/HitFlash");
            if (shader == null)
            {
                Debug.LogWarning("[KOM] Falta el shader KOM/HitFlash: los golpes no destellaran.");
                return;
            }

            _mat = new Material(shader);
            _mat.name = "HitFlash_" + owner.CharacterId;
            _mat.SetColor(ColorId, new Color(1f, 0.985f, 0.95f));
            _mat.SetFloat(IntensityId, 0f);
            _mat.SetFloat(RimId, 0.2f);
            _mat.renderQueue = 3145;

            var renderers = owner.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers == null) return;

            for (int i = 0; i < renderers.Length; i++)
            {
                var src = renderers[i];
                if (src == null || src.sharedMesh == null) continue;
                string n = src.gameObject.name;
                if (n.EndsWith("_AuraShell", System.StringComparison.Ordinal)) continue;
                if (n.EndsWith("_BlockShell", System.StringComparison.Ordinal)) continue;
                if (n.EndsWith("_HitShell", System.StringComparison.Ordinal)) continue;

                var go = new GameObject(n + "_HitShell");
                go.transform.SetParent(src.transform.parent, false);
                go.transform.localPosition = src.transform.localPosition;
                go.transform.localRotation = src.transform.localRotation;
                go.transform.localScale = src.transform.localScale;

                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = src.sharedMesh;
                smr.bones = src.bones;
                smr.rootBone = src.rootBone;
                smr.localBounds = src.localBounds;
                smr.updateWhenOffscreen = true;
                smr.forceMatrixRecalculationPerRender = true;
                smr.skinnedMotionVectors = false;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.receiveShadows = false;
                smr.allowOcclusionWhenDynamic = false;

                int subMeshes = Mathf.Max(1, src.sharedMesh.subMeshCount);
                var mats = new Material[subMeshes];
                for (int m = 0; m < subMeshes; m++) mats[m] = _mat;
                smr.sharedMaterials = mats;

                int shapes = src.sharedMesh.blendShapeCount;
                for (int b = 0; b < shapes; b++) smr.SetBlendShapeWeight(b, src.GetBlendShapeWeight(b));

                smr.enabled = false;
                _shells.Add(smr);
            }
        }

        /// <summary>
        /// Enciende el destello. <paramref name="strength"/> 0..1.6 y
        /// <paramref name="rim"/> 0 = silueta quemada, 1 = solo contorno (bloqueo).
        /// </summary>
        public void Flash(float strength, float rim, float decay = 12f)
        {
            if (_mat == null) return;
            float s = Mathf.Clamp(strength, 0f, 1.8f);
            _intensity = Mathf.Max(_intensity, s);
            _peak = Mathf.Max(_peak, s);
            _rim = rim;
            _rimTarget = rim;
            _decay = Mathf.Max(4f, decay);
            // Se escribe YA, no en el LateUpdate siguiente: el frame del impacto es
            // el unico que importa y un frame de retraso deja la silueta intacta
            // justo cuando la camara tiembla.
            _mat.SetFloat(IntensityId, _intensity);
            _mat.SetFloat(RimId, _rim);
            Show(true);
        }

        public void Clear()
        {
            _intensity = 0f;
            _peak = 0f;
            if (_mat != null) _mat.SetFloat(IntensityId, 0f);
            Show(false);
        }

        // Sondas para los gates de verificacion.
        public float DebugIntensity => _mat != null ? _mat.GetFloat(IntensityId) : 0f;
        public int DebugShellCount => _shells.Count;
        public bool DebugVisible
        {
            get
            {
                for (int i = 0; i < _shells.Count; i++)
                    if (_shells[i] != null && _shells[i].enabled) return true;
                return false;
            }
        }

        void Show(bool on)
        {
            for (int i = 0; i < _shells.Count; i++)
                if (_shells[i] != null && _shells[i].enabled != on) _shells[i].enabled = on;
        }

        void LateUpdate()
        {
            if (_mat == null) return;
            if (_intensity <= 0.002f)
            {
                if (_shells.Count > 0 && _shells[0] != null && _shells[0].enabled) Show(false);
                return;
            }

            // Caida en tiempo REAL (no deltaTime escalado): el flash pertenece al
            // mundo de la camara, que no se congela durante el hitstop.
            _intensity = Mathf.MoveTowards(_intensity, 0f, Time.unscaledDeltaTime * _decay * _peak);
            _rim = Mathf.MoveTowards(_rim, _rimTarget, Time.unscaledDeltaTime * 4f);
            _mat.SetFloat(IntensityId, _intensity);
            _mat.SetFloat(RimId, _rim);
        }

        void OnDestroy()
        {
            for (int i = 0; i < _shells.Count; i++)
                if (_shells[i] != null) Destroy(_shells[i].gameObject);
            if (_mat != null) Destroy(_mat);
        }
    }
}
