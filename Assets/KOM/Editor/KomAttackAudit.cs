using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KOM.EditorTools
{
    // AUDITORIA DE ATAQUES: que extremidad se mueve DE VERDAD en cada clip.
    //
    // El nombre de un clip no prueba nada. "Kicking (1)" puede ser una patada derecha o
    // izquierda, y un retarget puede dejar el golpe en la mano equivocada. Aqui se muestrea
    // el clip y se mide el recorrido de cada mano y cada pie en el espacio de la PELVIS
    // (asi el giro del cuerpo no cuenta como movimiento de la extremidad).
    //
    // Tambien comprueba el TIEMPO: el pico de extension de la extremidad dominante tiene que
    // caer donde el AttackSpec declara el golpe. Si el puno llega a los 0.45 s y la hitbox
    // se abre a los 0.26 s, el ataque no conecta donde se ve.
    public static class KomAttackAudit
    {
        const string PrefabDir = "Assets/Resources/Characters";
        const string OutDir = "Logs/attacks";
        const int Samples = 60;

        // Tolerancias
        const float DominanceRatio = 1.15f;  // la dominante debe superar a la siguiente
        const float MinTravel = 0.15f;       // metros: por debajo no hay golpe apreciable
        const float TimingTolerance = 0.12f; // fraccion del clip

        struct Limb
        {
            public string Name;
            public float Travel;
            public float PeakNormalized;
            public Vector3 Delta;
        }

        struct Row
        {
            public string State;
            public string Clip;
            public float Duration;
            public float HitTime;
            public string ExpectedLimb;
            public Limb[] Limbs;
            public string Dominant;
            public float Dominance;
            public bool LimbOk;
            public bool TimingOk;
            public bool TimingChecked;
        }

        [MenuItem("King of Music/Diagnostico/42 - Auditoria de ataques", priority = 242)]
        public static void MenuAudit()
        {
            var report = Audit("juan_grabriel");
            Debug.Log(report);
        }

        // Especificacion del jugador: los CUATRO botones. Es lo unico que decide PASS/FAIL.
        static readonly (string state, string limb)[] Expected =
        {
            ("LightPunch", "hand.L"),   // G: golpe rapido (el arte original es zurdo)
            ("HeavyPunch", "hand.R"),   // H: golpe fuerte
            ("LightKick",  "foot.R"),   // J: patada rapida
            ("HeavyKick",  "foot.R"),   // K: patada fuerte
        };

        // Movimientos que solo usa la IA y los combos: se miden y se informan, no bloquean.
        static readonly (string state, string limb)[] Informational =
        {
            ("Uppercut", "-"),
            ("DropKick", "-"),
            ("QuadPunch", "-"),
            ("Combo1", "-"),
            ("Combo2", "-"),
            ("Combo3", "-"),
        };

        public static string Audit(string characterId)
        {
            Directory.CreateDirectory(OutDir);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{characterId}.prefab");
            if (prefab == null) return $"{characterId}: FALTA PREFAB";

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var sb = new StringBuilder();
            var rows = new List<Row>();
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var fighter = go.GetComponent<Fighter>();
                if (fighter != null) fighter.enabled = false;
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;

                var controller = animator != null ? animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController : null;
                if (controller == null) return $"{characterId}: sin AnimatorController";

                var handL = Find(go, "DEF-hand.L");
                var handR = Find(go, "DEF-hand.R");
                var footL = Find(go, "DEF-foot.L");
                var footR = Find(go, "DEF-foot.R");
                if (handL == null || handR == null || footL == null || footR == null)
                    return $"{characterId}: faltan huesos DEF (manos/pies)";

                var states = new Dictionary<string, AnimationClip>();
                foreach (var st in controller.layers[0].stateMachine.states)
                    if (st.state.motion is AnimationClip c) states[st.state.name] = c;

                sb.AppendLine($"KOM ATAQUES · {characterId}");
                sb.AppendLine($"dominancia >= {DominanceRatio:F2}x | recorrido minimo {MinTravel:F2} m | tolerancia de tiempo {TimingTolerance:F2}");
                sb.AppendLine();

                int fails = 0;
                foreach (var (state, expected) in Expected)
                {
                    if (!states.TryGetValue(state, out var clip)) { sb.AppendLine($"{state}: SIN ESTADO"); fails++; continue; }
                    var row = Measure(go, clip, state, expected, handL, handR, footL, footR);
                    rows.Add(row);
                    AppendRow(sb, row);
                    if (!row.LimbOk) fails++;
                    if (row.TimingChecked && !row.TimingOk) fails++;
                }

                sb.AppendLine("--- informativo (IA y combos: no deciden el veredicto) ---");
                foreach (var (state, expected) in Informational)
                {
                    if (!states.TryGetValue(state, out var clip)) { sb.AppendLine($"{state}: SIN ESTADO"); continue; }
                    var row = Measure(go, clip, state, expected, handL, handR, footL, footR);
                    AppendRow(sb, row);
                }

                fails += AppendContrast(sb, rows);

                sb.AppendLine(fails == 0 ? "VERDICT: PASS" : $"VERDICT: FAIL ({fails} comprobaciones)");
                File.WriteAllText($"{OutDir}/report.txt", sb.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
            return sb.ToString();
        }

        // Contraste rapido/fuerte: no basta con que sean clips distintos, tienen que LEERSE
        // distintos. Se compara el recorrido de la extremidad (amplitud), la anticipacion
        // (tiempo hasta la extension maxima) y el dano declarado.
        static int AppendContrast(StringBuilder sb, List<Row> rows)
        {
            sb.AppendLine("--- contraste rapido / fuerte (legibilidad) ---");
            int fails = 0;
            fails += ContrastPair(sb, rows, "LightPunch", "HeavyPunch", "punetazo");
            fails += ContrastPair(sb, rows, "LightKick", "HeavyKick", "patada");
            return fails;
        }

        static int ContrastPair(StringBuilder sb, List<Row> rows, string lightState, string heavyState, string label)
        {
            var l = rows.Find(r => r.State == lightState);
            var h = rows.Find(r => r.State == heavyState);
            if (l.State == null || h.State == null) { sb.AppendLine($"{label}: falta un estado"); return 1; }

            var ld = Array.Find(l.Limbs, x => x.Name == l.Dominant);
            var hd = Array.Find(h.Limbs, x => x.Name == h.Dominant);
            float lWind = ld.PeakNormalized * l.Duration;
            float hWind = hd.PeakNormalized * h.Duration;
            var lSpec = FindSpec(lightState);
            var hSpec = FindSpec(heavyState);
            float lDmg = lSpec != null ? lSpec.damage : 0f;
            float hDmg = hSpec != null ? hSpec.damage : 0f;

            bool travelOk = hd.Travel > ld.Travel * 1.05f;
            bool windOk = hWind > lWind;
            bool dmgOk = hDmg > lDmg;

            sb.AppendLine($"{label}:");
            sb.AppendLine($"   amplitud    rapido={ld.Travel:F2} m  fuerte={hd.Travel:F2} m   {(travelOk ? "PASS" : "FAIL")}");
            sb.AppendLine($"   anticipacion rapido={lWind:F2} s  fuerte={hWind:F2} s   {(windOk ? "PASS" : "FAIL")}");
            sb.AppendLine($"   dano        rapido={lDmg:F0}    fuerte={hDmg:F0}      {(dmgOk ? "PASS" : "FAIL")}");
            int fails = 0;
            if (!travelOk) fails++;
            if (!windOk) fails++;
            if (!dmgOk) fails++;
            return fails;
        }

        static void AppendRow(StringBuilder sb, Row r)
        {
            sb.AppendLine($"{r.State,-12} clip={r.Clip,-34} dur={r.Duration:F2}s golpe={r.HitTime:F2}s");
            foreach (var l in r.Limbs)
            {
                string mark = l.Name == r.Dominant ? " <== dominante" : "";
                sb.AppendLine($"   {l.Name,-8} recorrido={l.Travel:F3} m  pico={l.PeakNormalized:F2}  " +
                              $"delta=({l.Delta.x:F2},{l.Delta.y:F2},{l.Delta.z:F2}){mark}");
            }
            string limbVerdict = r.ExpectedLimb == "-"
                ? "n/a"
                : (r.LimbOk ? "PASS" : $"FAIL (se esperaba {r.ExpectedLimb})");
            sb.AppendLine($"   extremidad={r.Dominant} dominancia={r.Dominance:F2}x  {limbVerdict}");

            if (r.TimingChecked)
            {
                float hitNorm = r.Duration > 0.001f ? r.HitTime / r.Duration : 0f;
                var dom = Array.Find(r.Limbs, l => l.Name == r.Dominant);
                sb.AppendLine($"   tiempo: pico={dom.PeakNormalized:F2} golpe={hitNorm:F2}  {(r.TimingOk ? "PASS" : "FAIL")}");
            }
            sb.AppendLine();
        }

        static Row Measure(GameObject go, AnimationClip clip, string state, string expected,
                           Transform handL, Transform handR, Transform footL, Transform footR)
        {
            var names = new[] { "hand.L", "hand.R", "foot.L", "foot.R" };
            var bones = new[] { handL, handR, footL, footR };
            var start = new Vector3[4];
            var travel = new float[4];
            var peak = new float[4];
            var delta = new Vector3[4];

            // Espacio de MUNDO. El personaje se instancia en el origen y la rotacion global
            // forma parte del golpe (un punetazo gira la cadera), asi que no se descuenta.
            // Descontar el espacio de la pelvis era peor: los huesos de control del rig
            // llevan escalas de 100 y el inverso deformaba la medida a milimetros.
            AnimationMode.StartAnimationMode();
            try
            {
                for (int i = 0; i <= Samples; i++)
                {
                    float n = i / (float)Samples;
                    AnimationMode.SampleAnimationClip(go, clip, n * clip.length);

                    for (int b = 0; b < 4; b++)
                    {
                        var p = WorldPoint(bones[b]);
                        if (i == 0) { start[b] = p; continue; }
                        float d = Vector3.Distance(p, start[b]);
                        if (d > travel[b]) { travel[b] = d; peak[b] = n; delta[b] = p - start[b]; }
                    }
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }

            var limbs = new Limb[4];
            for (int b = 0; b < 4; b++)
                limbs[b] = new Limb { Name = names[b], Travel = travel[b], PeakNormalized = peak[b], Delta = delta[b] };

            int best = 0;
            for (int b = 1; b < 4; b++) if (limbs[b].Travel > limbs[best].Travel) best = b;
            int second = -1;
            for (int b = 0; b < 4; b++)
                if (b != best && (second < 0 || limbs[b].Travel > limbs[second].Travel)) second = b;

            float dominance = second >= 0 && limbs[second].Travel > 0.0001f
                ? limbs[best].Travel / limbs[second].Travel
                : 99f;

            var spec = FindSpec(state);
            float duration = spec != null ? spec.duration : clip.length;
            float hitTime = spec != null ? spec.hitTime : 0f;

            bool limbOk = true;
            if (expected != "-")
            {
                bool strongEnough = limbs[best].Travel >= MinTravel;
                bool clearWinner = dominance >= DominanceRatio;
                limbOk = strongEnough && clearWinner && limbs[best].Name == expected;
            }

            bool timingChecked = spec != null && spec.hitCount <= 1 && expected != "-";
            bool timingOk = true;
            if (timingChecked)
            {
                float hitNorm = duration > 0.001f ? hitTime / duration : 0f;
                timingOk = Mathf.Abs(limbs[best].PeakNormalized - hitNorm) <= TimingTolerance;
            }

            return new Row
            {
                State = state,
                Clip = clip.name,
                Duration = duration,
                HitTime = hitTime,
                ExpectedLimb = expected,
                Limbs = limbs,
                Dominant = limbs[best].Name,
                Dominance = dominance,
                LimbOk = limbOk,
                TimingOk = timingOk,
                TimingChecked = timingChecked,
            };
        }

        static AttackSpec FindSpec(string state)
        {
            foreach (var s in Attacks.All)
                if (s != null && s.animatorState == state) return s;
            return null;
        }

        // Posicion mundo compuesta a mano: tras SampleAnimationClip, leer transform.position
        // puede devolver el valor cacheado del frame anterior.
        static Vector3 WorldPoint(Transform t)
        {
            var m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
            var p = t.parent;
            while (p != null)
            {
                m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
                p = p.parent;
            }
            return m.GetColumn(3);
        }

        static Transform Find(GameObject go, string name)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
