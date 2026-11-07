using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KOM
{
    /// <summary>
    /// Pulso de post-proceso por impacto.
    ///
    /// Es la capa que hace que el golpe "sacuda la optica" y no solo el encuadre:
    ///
    ///   - Aberracion cromatica: los bordes se separan en RGB durante 2-3 frames.
    ///     Es el recurso clasico de las peliculas de accion para una conmocion.
    ///   - Distorsion de lente: el espacio se abomba un instante.
    ///   - Vignette y exposicion: el contraste sube y el negro aprieta.
    ///   - Bloom: el nucleo del impacto florece sobre el escenario.
    ///
    /// Se implementa como un Volume de prioridad alta con `weight` animado: el perfil
    /// se escribe UNA vez y por frame solo cambia un float. Nada de crear y destruir
    /// overrides por golpe.
    /// </summary>
    public sealed class HitPostFx : MonoBehaviour
    {
        const string VolumeName = "KOM_ImpactVolume";

        Volume _volume;
        float _weight;
        float _peak;
        float _decay = 11f;

        public static HitPostFx Attach(Transform host, Camera camera)
        {
            if (camera == null) return null;
            var go = new GameObject(VolumeName);
            go.transform.SetParent(host, false);
            var fx = go.AddComponent<HitPostFx>();
            fx.Build(camera);
            return fx;
        }

        void Build(Camera camera)
        {
            // El look base ya lo pone VisualMood en un Volume global de prioridad 10.
            // Este va por encima y con weight 0, asi que en reposo no cambia NADA.
            if (Object.FindAnyObjectByType<Volume>() == null) VisualMood.Apply(camera);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;

            var chroma = profile.Add<ChromaticAberration>(true);
            chroma.intensity.Override(0.38f);

            var lens = profile.Add<LensDistortion>(true);
            lens.intensity.Override(-0.18f);
            lens.scale.Override(1f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.44f);
            vignette.smoothness.Override(0.52f);
            vignette.rounded.Override(false);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.55f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.66f);
            bloom.tint.Override(new Color(1f, 0.96f, 0.9f));

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.18f);
            color.contrast.Override(15f);
            color.saturation.Override(12f);

            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 40f;
            _volume.weight = 0f;
            _volume.sharedProfile = profile;
        }

        /// <summary>Dispara el pulso. <paramref name="amount"/> 0..1.</summary>
        public void Pulse(float amount)
        {
            if (_volume == null) return;
            float a = Mathf.Clamp01(amount);
            _weight = Mathf.Max(_weight, a);
            _peak = Mathf.Max(_peak, a);
            _volume.weight = _weight;
        }

        public void Clear()
        {
            _weight = 0f;
            _peak = 0f;
            if (_volume != null) _volume.weight = 0f;
        }

        /// <summary>Sonda para los gates de verificacion.</summary>
        public float DebugWeight => _volume != null ? _volume.weight : 0f;

        void Update()
        {
            if (_volume == null) return;
            if (_weight <= 0.001f) return;
            // Caida en tiempo real: el post-proceso pertenece a la camara, que no se
            // congela durante el hitstop.
            _weight = Mathf.MoveTowards(_weight, 0f, Time.unscaledDeltaTime * _decay * Mathf.Max(0.35f, _peak));
            _volume.weight = _weight;
        }

        void OnDestroy()
        {
            if (_volume != null && _volume.sharedProfile != null) Destroy(_volume.sharedProfile);
        }
    }
}
