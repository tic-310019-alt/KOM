using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KOM
{
    public static class VisualMood
    {
        /// <summary>
        /// Aplica el look del proyecto a una camara. <paramref name="filmGrain"/> se apaga
        /// en las pantallas de ficha (seleccion): alli el grano no aporta textura de
        /// pelicula, solo ensucia el retrato y el fondo.
        /// </summary>
        public static void Apply(Camera camera, bool filmGrain = true)
        {
            if (camera == null) return;
            camera.allowHDR = true;
            var data = camera.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = true;
                data.renderShadows = true;
                // El AA lo dicta el pipeline: con MSAA activo, FXAA solo añade desenfoque.
                data.antialiasing = KomSettings.PreferredAntialiasing();
            }

            // El look base vive en un Volume global. El combate anade ADEMAS un Volume
            // de impacto (HitPostFx) con prioridad mas alta y weight 0: hay que
            // ignorarlo aqui o el look base se aplicaria sobre el pulso de golpe.
            var volumes = Object.FindObjectsByType<Volume>();
            for (int i = 0; i < volumes.Length; i++)
            {
                var v = volumes[i];
                if (v == null) continue;
                if (v.gameObject.name == "KOM_ImpactVolume") continue;
                SetFilmGrain(v.sharedProfile, filmGrain);
                return;
            }
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.55f);
            bloom.threshold.Override(1.02f);
            bloom.scatter.Override(0.62f);
            bloom.tint.Override(new Color(1f, 0.94f, 0.86f));

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.24f);
            vignette.smoothness.Override(0.5f);
            vignette.rounded.Override(false);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.08f);
            color.contrast.Override(11f);
            color.saturation.Override(9f);
            color.colorFilter.Override(new Color(1f, 0.98f, 0.95f));

            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.ACES);

            if (filmGrain)
            {
                var grain = profile.Add<FilmGrain>(true);
                grain.type.Override(FilmGrainLookup.Thin1);
                grain.intensity.Override(0.11f);
                grain.response.Override(0.75f);
            }

            var go = new GameObject("KOM_GlobalVolume");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
        }

        static void SetFilmGrain(VolumeProfile profile, bool enabled)
        {
            if (profile == null) return;
            if (!profile.TryGet<FilmGrain>(out var grain))
            {
                if (!enabled) return;
                grain = profile.Add<FilmGrain>(true);
                grain.type.Override(FilmGrainLookup.Thin1);
            }
            grain.active = enabled;
            grain.intensity.Override(enabled ? 0.11f : 0f);
        }
    }
}
