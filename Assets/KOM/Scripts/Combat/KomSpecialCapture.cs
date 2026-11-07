#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;

namespace KOM
{
    // Herramienta de diagnostico (solo editor): captura el Game View en tiempos de
    // JUEGO exactos. El hitstop cambia Time.timeScale; el CLI puede llegar tarde y
    // perder el impacto. Se crea por eval en Play:
    //   var cap = new GameObject("SpecialCapture").AddComponent<KOM.KomSpecialCapture>();
    //   cap.Configure(fighter, "juan", 0.65f, 1.18f, 1.45f);
    //
    // Modo PROGRESO (recomendado): las capturas se disparan cuando el Animator del
    // especial alcanza un tiempo NORMALIZADO. Inmune a hitstop, a la velocidad del
    // clip y a los tirones de frame.
    //   cap.ConfigureAtProgress(fighter, "juan", 0.10f, 0.30f, 0.49f, 0.55f, 0.75f);
    public sealed class KomSpecialCapture : MonoBehaviour
    {
        Fighter _fighter;
        string _prefix = "special";
        float[] _times = new float[0];
        float _start;

        public void Configure(Fighter fighter, string prefix, params float[] times)
        {
            _fighter = fighter;
            _prefix = string.IsNullOrEmpty(prefix) ? "special" : prefix;
            _times = times ?? new float[0];
            _start = Time.time;
            StartCoroutine(Run());
        }

        public void ConfigureAtProgress(Fighter fighter, string prefix, params float[] normalizedTimes)
        {
            _fighter = fighter;
            _prefix = string.IsNullOrEmpty(prefix) ? "special" : prefix;
            _times = normalizedTimes ?? new float[0];
            StartCoroutine(RunProgress());
        }

        IEnumerator Run()
        {
            Directory.CreateDirectory("Logs/play");
            for (int i = 0; i < _times.Length; i++)
            {
                float target = _start + _times[i];
                while (Time.time < target) yield return null;
                yield return new WaitForEndOfFrame();
                Shot(i, _times[i]);
            }
            Destroy(gameObject);
        }

        IEnumerator RunProgress()
        {
            Directory.CreateDirectory("Logs/play");
            var animator = _fighter != null ? _fighter.Animator : null;
            if (animator == null) { Destroy(gameObject); yield break; }
            // Esperar a que el especial arranque de verdad.
            float guard = Time.time + 2f;
            while (_fighter != null && !_fighter.IsCastingSpecial && Time.time < guard) yield return null;

            int next = 0;
            guard = Time.time + 12f;
            while (next < _times.Length && Time.time < guard)
            {
                if (_fighter == null) break;
                var st = animator.GetCurrentAnimatorStateInfo(CombatTuning.BaseLayer);
                // Solo se muestrea el estado del especial: Idle loopearia y su
                // normalizedTime crece sin limite (capturas en tiempos falsos).
                string expected = _fighter.ActiveSpecial != null ? _fighter.ActiveSpecial.animatorState : null;
                if (expected != null && !st.IsName(expected)) { yield return null; continue; }
                float progress = st.normalizedTime;
                if (progress >= _times[next])
                {
                    yield return new WaitForEndOfFrame();
                    Shot(next, progress);
                    next++;
                }
                yield return null;
            }
            Destroy(gameObject);
        }

        void Shot(int index, float stamp)
        {
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null) return;
            byte[] png = texture.EncodeToPNG();
            string path = $"Logs/play/{_prefix}_{index}_{stamp:F2}.png";
            File.WriteAllBytes(path, png);
            string pose = "";
            if (_fighter != null)
            {
                foreach (var tr in _fighter.GetComponentsInChildren<Transform>(true))
                    if (tr.name == "DEF-spine" || tr.name == "DEF-spine.006")
                        pose += tr.name + "=" + tr.position.ToString("F2") + " ";
                var st = _fighter.Animator.GetCurrentAnimatorStateInfo(CombatTuning.BaseLayer);
                pose += "norm=" + st.normalizedTime.ToString("F2");
            }
            Debug.Log($"[KOM][CAPTURE] {path} t={Time.time - _start:F2} casting={(_fighter != null && _fighter.IsCastingSpecial)} {pose}");
            Destroy(texture);
        }
    }
}
#endif
