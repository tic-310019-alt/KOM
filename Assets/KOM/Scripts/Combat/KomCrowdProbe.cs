#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace KOM
{
    /// <summary>
    /// Sonda en Play del publico del Zocalo. Comprueba lo que el gate de editor no puede:
    /// que la malla exista en runtime, que el material tenga el atlas, que el renderer este
    /// visible desde la camara de combate y que la euforia responda al golpe fuerte real y
    /// al K.O. (valor de material incluido).
    ///
    /// Uso desde el puente, en Play y con el combate del Zocalo montado:
    ///   KOM.KomCrowdProbe.Spawn().Begin("Logs/crowd/play_report.txt")
    /// </summary>
    public sealed class KomCrowdProbe : MonoBehaviour
    {
        public static KomCrowdProbe Spawn()
        {
            var go = new GameObject("CrowdProbe");
            return go.AddComponent<KomCrowdProbe>();
        }

        public void Begin(string path)
        {
            StartCoroutine(Run(path));
        }

        IEnumerator Run(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("KOM PUBLICO · sonda en Play");
            bool ok = true;

            void Check(string label, bool condition, string detail)
            {
                sb.AppendLine($"[{(condition ? "OK" : "FAIL")}] {label}: {detail}");
                if (!condition) ok = false;
            }

            float waited = 0f;
            while (KomStageCrowd.Instance == null && waited < 20f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            var crowd = KomStageCrowd.Instance;
            Check("instancia", crowd != null, crowd != null ? $"activa tras {waited:F1} s" : "no se creo");
            if (crowd == null)
            {
                Finish(sb, path, false);
                yield break;
            }

            Check("escenario", crowd.name == "StageCrowd_zocalo" || crowd.name == "StageCrowd_zocalo_final",
                crowd.name);
            Check("asientos", crowd.SeatCount == 216, crowd.SeatCount.ToString());
            Check("avatares", crowd.AvatarCount >= 40 && crowd.AvatarCount % 2 == 0,
                crowd.AvatarCount.ToString());
            Check("fotogramas por ciclo", crowd.FrameCount == 2, crowd.FrameCount.ToString());
            Check("ritmo base", crowd.Fps > 0.1f && crowd.Fps <= 12f, crowd.Fps.ToString("F2"));
            Check("reparto de orientacion",
                crowd.FrontSeats + crowd.LateralSeats == crowd.SeatCount,
                $"frente={crowd.FrontSeats} laterales={crowd.LateralSeats} celebran={crowd.CheerSeats}");
            Check("grada mixta",
                crowd.LateralSeats >= 110 && crowd.CheerSeats >= 30,
                $"laterales={crowd.LateralSeats} celebran={crowd.CheerSeats}");
            Check("malla", crowd.MeshVertexCount == crowd.SeatCount * 4,
                $"{crowd.MeshVertexCount} vertices para {crowd.SeatCount} asientos");
            Check("renderer", crowd.CrowdRenderer != null && crowd.CrowdRenderer.enabled,
                crowd.CrowdRenderer != null ? "habilitado" : "sin renderer");
            var mat = crowd.CrowdMaterial;
            Check("shader", mat != null && mat.shader != null && mat.shader.name == "KOM/CrowdBW",
                mat != null && mat.shader != null ? mat.shader.name : "sin material");
            Check("atlas", mat != null && mat.GetTexture("_BaseMap") != null,
                mat != null ? (mat.GetTexture("_BaseMap") != null ? "asignado" : "vacio") : "sin material");
            var cam = Camera.main;
            Check("camara", cam != null, cam != null ? cam.name : "sin MainCamera");

            yield return new WaitForSeconds(0.6f);
            Check("visible", crowd.CrowdRenderer != null && crowd.CrowdRenderer.isVisible,
                crowd.CrowdRenderer != null ? "isVisible=" + crowd.CrowdRenderer.isVisible : "sin renderer");

            float e0 = crowd.Energy;
            KomStageCrowd.Hype(1f);
            yield return new WaitForSeconds(0.45f);
            float e1 = crowd.Energy;
            Check("euforia sube", e1 > 0.5f && e1 > e0 + 0.2f, $"{e0:F2} -> {e1:F2}");

            // REGRESION DEL PARPADEO. El reloj del ciclo se INTEGRA; su velocidad solo puede
            // ser fps * (1 + euforia * 0.35) ~ 5.7 fotogramas/s. Antes era _Time.y * fps, y
            // con _Time.y alto la fase se disparaba a cientos de fotogramas por segundo: la
            // grada alternaba las dos poses como ruido. Se mide con la euforia ARRIBA, que
            // es cuando el fallo se disparaba.
            float c0 = crowd.Cycle;
            float t0 = Time.realtimeSinceStartup;
            yield return new WaitForSeconds(0.5f);
            float c1 = crowd.Cycle;
            float rate = (c1 - c0) / Mathf.Max(0.0001f, Time.realtimeSinceStartup - t0);
            float maxRate = crowd.Fps * 1.5f;
            Check("reloj estable", rate > 0.5f && rate <= maxRate,
                $"{rate:F2} fotogramas/s (limite {maxRate:F1})");

            // Se espera a que la grada se enfrie de verdad. El combate sigue vivo detras y
            // la IA tambien levanta publico: con una espera fija el "antes" del golpe fuerte
            // salia a veces en 0.45 y la comprobacion fallaba por el ruido de fondo, no por
            // el gancho.
            float waited2 = 0f;
            while (crowd.Energy > 0.30f && waited2 < 10f)
            {
                waited2 += Time.deltaTime;
                yield return null;
            }
            float e2 = crowd.Energy;
            Check("euforia decae", e2 < e1 && e2 <= 0.30f, $"{e1:F2} -> {e2:F2} en {waited2:F1} s");

            // Gancho REAL del combate: un golpe fuerte tiene que levantar la grada.
            var controller = Object.FindAnyObjectByType<CombatController>();
            var fighters = Object.FindObjectsByType<Fighter>(FindObjectsInactive.Exclude);
            Fighter attacker = null, defender = null;
            foreach (var f in fighters)
            {
                if (f.IsAI) defender = f;
                else attacker = f;
            }
            if (controller != null && attacker != null && defender != null)
            {
                float before = crowd.Energy;
                controller.OnHitLanded(attacker, defender, Attacks.HeavyKick, false,
                    defender.transform.position + Vector3.up * 1.2f);
                yield return new WaitForSeconds(0.2f);
                Check("gancho golpe fuerte", crowd.Energy > before + 0.25f,
                    $"{before:F2} -> {crowd.Energy:F2}");
            }
            else
            {
                Check("gancho golpe fuerte", false, "no se encontraron controlador/luchadores");
            }

            KomStageCrowd.Knockout();
            yield return new WaitForSeconds(0.25f);
            Check("K.O.", crowd.Energy > 0.75f, crowd.Energy.ToString("F2"));
            float applied = mat != null ? mat.GetFloat("_CrowdEnergy") : -1f;
            Check("material sincronizado", Mathf.Abs(applied - crowd.Energy) < 0.06f,
                $"material={applied:F2} runtime={crowd.Energy:F2}");

            Finish(sb, path, ok);
        }

        void Finish(StringBuilder sb, string path, bool ok)
        {
            sb.AppendLine(ok ? "VERDICT: PASS" : "VERDICT: FAIL");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[KOM][CROWD] " + path + " · " + (ok ? "PASS" : "FAIL"));
        }
    }
}
#endif
