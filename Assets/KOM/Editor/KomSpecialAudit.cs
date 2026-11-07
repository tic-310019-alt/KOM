using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KOM.EditorTools
{
    // Auditoria de los especiales con prop. Mide, para el clip RAW del FBX y para el clip
    // horneado que usa el Animator, la misma bateria de magnitudes en cada fase:
    //
    //   pelvisYaw  giro del cuerpo (swing-twist de la pelvis sobre Y, grados)
    //   footLYaw   giro del pie izquierdo sobre Y, en el mismo sistema
    //   footRYaw   idem derecho
    //   hipX/hipZ  desplazamiento horizontal de la cadera respecto al root del modelo
    //   footY      altura del punto mas bajo de los dos pies (0 = planta en el suelo)
    //   armEuler   rotacion local del nodo Armature (lo que escribe AddArmatureYaw)
    //
    // El objetivo es separar "el clip de origen ya gira" de "el horneado introduce el giro".
    public static class KomSpecialAudit
    {
        const string FbxDir = "Assets/Art/Characters";
        const string InPlaceDir = "Assets/KOM/Animation/Clips/InPlace";

        public static readonly string[] DefaultStates = { "Combo", "Fireball", "SpecialBeam", "MagicHeal" };

        public static string Audit(string id, int steps = 24)
        {
            return AuditStates(id, steps, DefaultStates);
        }

        public static string AuditStates(string id, int steps, string[] states)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            if (prefab == null) return "ERROR: falta prefab " + id;

            var raw = LoadRawClips(id);
            var lines = new List<string>();
            lines.Add($"# AUDIT {id}  (pasos={steps})");
            lines.Add("clip\tt\tyawPelvis\tyawFootL\tyawFootR\thipX\thipZ\tfootY\tarmEulerX\tarmEulerY\tarmEulerZ\tarmPosY");

            foreach (var state in states)
            {
                if (!raw.TryGetValue(state, out var rawClip) || rawClip == null) continue;
                var baked = FindBaked(id, state);

                Measure(prefab, id, rawClip, steps, lines, "RAW");
                if (baked != null) Measure(prefab, id, baked, steps, lines, "BAKED");
                lines.Add("");
            }

            Directory.CreateDirectory("Logs");
            File.WriteAllText($"Logs/special_audit_{id}.txt", string.Join("\n", lines) + "\n");
            return Summarize(id, lines);
        }

        static AnimationClip FindBaked(string id, string state)
        {
            string[] suffixes = { "Clean", "FirstRaw", "RawWalk" };
            foreach (var s in suffixes)
            {
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{InPlaceDir}/{id}_{state}_{s}.anim");
                if (c != null) return c;
            }
            return null;
        }

        // Barrido de TODAS las tomas del FBX. Sirve para elegir una base que no gire el
        // cuerpo: el criterio de seleccion de los especiales es datos, no intuicion.
        public static string ScanTakes(string id, int steps = 16)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            if (prefab == null) return "ERROR: falta prefab " + id;
            var raw = LoadRawClips(id);
            var rows = new List<string> { "take\tlen\tyawPelvis\tfootL\tfootR\thipTravel\tfootYmin\tfootYmax" };
            foreach (var kv in raw.OrderBy(k => k.Key))
            {
                var clip = kv.Value;
                if (clip == null || clip.length < 0.05f) continue;
                float maxP = 0f, maxL = 0f, maxR = 0f, travel = 0f, minY = float.MaxValue, maxY = float.MinValue;
                int n = Mathf.Max(2, steps);
                Vector3[] origin = null;
                float baseP = float.NaN, baseL = float.NaN, baseR = float.NaN;
                float prevP = 0f, prevL = 0f, prevR = 0f;
                for (int i = 0; i <= n; i++)
                {
                    var go = UnityEngine.Object.Instantiate(prefab);
                    try
                    {
                        var animator = go.GetComponentInChildren<Animator>(true);
                        if (animator != null) animator.enabled = false;
                        var all = go.GetComponentsInChildren<Transform>(true);
                        clip.SampleAnimation(go, Mathf.Min(clip.length * i / n, clip.length - 0.0001f));
                        var pelvis = Find(all, "DEF-spine") ?? Find(all, "DEF-pelvis");
                        var footL = Find(all, "DEF-foot.L");
                        var footR = Find(all, "DEF-foot.R");
                        if (pelvis == null) continue;
                        var hip = ChainPos(pelvis);
                        if (origin == null) origin = new[] { hip };
                        travel = Mathf.Max(travel, new Vector2(hip.x - origin[0].x, hip.z - origin[0].z).magnitude);

                        // Giro RELATIVO al primer frame, desenrollado: es la magnitud que
                        // se ve como "el personaje gira sobre si mismo".
                        float yp = KomBodyYaw.TwistYaw(ChainRot(pelvis));
                        float yl = footL != null ? KomBodyYaw.TwistYaw(ChainRot(footL)) : 0f;
                        float yr = footR != null ? KomBodyYaw.TwistYaw(ChainRot(footR)) : 0f;
                        if (float.IsNaN(baseP)) { baseP = yp; baseL = yl; baseR = yr; prevP = yp; prevL = yl; prevR = yr; }
                        yp = Unwrap(yp, ref prevP); yl = Unwrap(yl, ref prevL); yr = Unwrap(yr, ref prevR);
                        maxP = Mathf.Max(maxP, Mathf.Abs(yp - baseP));
                        maxL = Mathf.Max(maxL, Mathf.Abs(yl - baseL));
                        maxR = Mathf.Max(maxR, Mathf.Abs(yr - baseR));

                        float y = float.MaxValue;
                        if (footL != null) y = Mathf.Min(y, ChainPos(footL).y);
                        if (footR != null) y = Mathf.Min(y, ChainPos(footR).y);
                        if (y < float.MaxValue) { minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(go); }
                }
                rows.Add($"{kv.Key}\t{clip.length:F2}\t{maxP:F1}\t{maxL:F1}\t{maxR:F1}\t{travel:F3}\t{minY:F3}\t{maxY:F3}");
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText($"Logs/takes_{id}.txt", string.Join("\n", rows) + "\n");
            return string.Join("\n", rows);
        }

        // Volcado puntual: compara RAW y BAKED en el mismo t y lista que huesos tienen
        // curvas en cada clip. Sirve para separar "curva corrupta" de "curva ausente".
        public static string DumpFrame(string id, string state, float tNorm)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            if (prefab == null) return "ERROR: falta prefab " + id;
            var raw = LoadRawClips(id);
            if (!raw.TryGetValue(state, out var rawClip) || rawClip == null) return "ERROR: falta toma " + state;
            var baked = FindBaked(id, state);
            var lines = new List<string>();

            string[] bones = { "DEF-spine", "DEF-thigh.L", "DEF-shin.L", "DEF-foot.L", "DEF-toe.L",
                               "DEF-thigh.R", "DEF-shin.R", "DEF-foot.R", "DEF-toe.R" };

            foreach (var pair in new[] { ("RAW", rawClip), ("BAKED", baked) })
            {
                if (pair.Item2 == null) continue;
                var clip = pair.Item2;
                lines.Add($"--- {pair.Item1} {clip.name} len={clip.length:F3} curves={AnimationUtility.GetCurveBindings(clip).Length}");
                var go = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    var animator = go.GetComponentInChildren<Animator>(true);
                    if (animator != null) animator.enabled = false;
                    clip.SampleAnimation(go, Mathf.Clamp01(tNorm) * clip.length);
                    var all = go.GetComponentsInChildren<Transform>(true);
                    foreach (var b in bones)
                    {
                        var tr = Find(all, b);
                        if (tr == null) { lines.Add($"  {b}\tMISSING"); continue; }
                        var p = ChainPos(tr);
                        var e = tr.localRotation.eulerAngles;
                        lines.Add($"  {b}\tpos=({p.x:F3},{p.y:F3},{p.z:F3})\tlocalEuler=({e.x:F1},{e.y:F1},{e.z:F1})");
                    }
                    lines.Add("  --- cadena de DEF-thigh.L (de raiz a hoja):");
                    var thigh = Find(all, "DEF-thigh.L");
                    if (thigh != null)
                    {
                        var chain = new List<Transform>();
                        for (var t = thigh; t != null; t = t.parent) chain.Add(t);
                        chain.Reverse();
                        for (int ci = Mathf.Max(0, chain.Count - 7); ci < chain.Count; ci++)
                        {
                            var t = chain[ci];
                            var lp = t.localPosition;
                            var le = t.localRotation.eulerAngles;
                            lines.Add($"    {t.name}\tlp=({lp.x:F4},{lp.y:F4},{lp.z:F4})\tlr=({le.x:F1},{le.y:F1},{le.z:F1})");
                        }
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }

            // Huesos con curvas de rotacion en cada clip, para ver que se pierde.
            var rawPaths = new HashSet<string>(AnimationUtility.GetCurveBindings(rawClip)
                .Where(b => b.propertyName.StartsWith("m_LocalRotation")).Select(b => b.path));
            if (baked != null)
            {
                var bakedPaths = new HashSet<string>(AnimationUtility.GetCurveBindings(baked)
                    .Where(b => b.propertyName.StartsWith("m_LocalRotation")).Select(b => b.path));
                var missing = rawPaths.Where(p => !bakedPaths.Contains(p)).OrderBy(p => p).ToArray();
                lines.Add($"--- rotacion: raw={rawPaths.Count} baked={bakedPaths.Count} faltan={missing.Length}");
                foreach (var m in missing) lines.Add("  falta " + m);
            }

            Directory.CreateDirectory("Logs");
            File.WriteAllText($"Logs/dump_{id}_{state}.txt", string.Join("\n", lines) + "\n");
            return string.Join("\n", lines);
        }

        // Compara las rutas que produce el filtro de relevancia con las rutas reales de
        // los bindings del clip. Si no coinciden, el filtro borra huesos y la malla se
        // deforma (fue la causa de las piernas rotas de los especiales).
        public static string Paths(string id, string state)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            if (prefab == null) return "ERROR: falta prefab " + id;
            var anim = prefab.GetComponentInChildren<Animator>(true);
            var rel = KomSetup.BuildRelevantPaths(prefab);
            var raw = LoadRawClips(id);
            if (!raw.TryGetValue(state, out var clip) || clip == null) return "ERROR: falta toma " + state;
            var bp = AnimationUtility.GetCurveBindings(clip).Select(b => b.path).Distinct().OrderBy(p => p).ToArray();
            int hit = bp.Count(p => rel.Contains(p));
            var lines = new List<string>
            {
                $"animator={anim.transform.name}  relevant={rel.Count}  bindingPaths={bp.Length}  interseccion={hit}",
                "--- relevant (6):"
            };
            lines.AddRange(rel.OrderBy(x => x).Take(6));
            lines.Add("--- bindings (6):");
            lines.AddRange(bp.Take(6));
            var dropped = bp.Where(p => !rel.Contains(p)).ToArray();
            lines.Add($"--- DESCARTADOS por el filtro: {dropped.Length}");
            lines.AddRange(dropped);
            Directory.CreateDirectory("Logs");
            File.WriteAllText($"Logs/paths_{id}_{state}.txt", string.Join("\n", lines) + "\n");
            return string.Join("\n", lines);
        }

        // Compara el bind guardado en el PREFAB con el bind del FBX original. El horneado
        // captura el bind del prefab, no del FBX: si el prefab quedo posado por una corrida
        // anterior, la corrupcion se hereda y se agrava en cada rehorneado.
        public static string BindDiff(string id)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Art/Characters/{id}.fbx");
            if (prefab == null || model == null) return "ERROR: faltan assets de " + id;

            var pgo = UnityEngine.Object.Instantiate(prefab);
            var mgo = UnityEngine.Object.Instantiate(model);
            var lines = new List<string> { $"# BINDDIFF {id}" };
            try
            {
                var pa = pgo.GetComponentsInChildren<Transform>(true);
                var ma = mgo.GetComponentsInChildren<Transform>(true);
                int bad = 0;
                foreach (var pm in pa)
                {
                    var mm = Find(ma, pm.name);
                    if (mm == null) continue;
                    float dq = Quaternion.Angle(pm.localRotation, mm.localRotation);
                    float dp = Vector3.Distance(pm.localPosition, mm.localPosition);
                    if (dq > 0.5f || dp > 0.002f)
                    {
                        bad++;
                        if (bad <= 40)
                            lines.Add($"{pm.name}\tdRot={dq:F1}deg\tdPos={dp:F4}m\tprefabEuler={pm.localRotation.eulerAngles} fbxEuler={mm.localRotation.eulerAngles}");
                    }
                }
                lines.Add($"TOTAL huesos desviados: {bad}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(pgo);
                UnityEngine.Object.DestroyImmediate(mgo);
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText($"Logs/binddiff_{id}.txt", string.Join("\n", lines) + "\n");
            return string.Join("\n", lines);
        }

        // Valores crudos de las curvas de un hueso en RAW y BAKED. Definitivo para saber
        // si una curva falta, se simplifico mal o cambio de valor.
        public static string CurveDump(string id, string state, string boneName)
        {
            var raw = LoadRawClips(id);
            if (!raw.TryGetValue(state, out var rawClip) || rawClip == null) return "ERROR: falta toma " + state;
            var baked = FindBaked(id, state);
            var lines = new List<string>();
            foreach (var pair in new[] { ("RAW", rawClip), ("BAKED", baked) })
            {
                if (pair.Item2 == null) continue;
                lines.Add($"--- {pair.Item1} {pair.Item2.name}");
                foreach (var b in AnimationUtility.GetCurveBindings(pair.Item2))
                {
                    if (!b.path.EndsWith("/" + boneName, StringComparison.Ordinal) && b.path != boneName) continue;
                    var c = AnimationUtility.GetEditorCurve(pair.Item2, b);
                    if (c == null) continue;
                    string first = c.length > 0 ? $"{c.keys[0].time:F3}:{c.keys[0].value:F5}" : "vacia";
                    string last = c.length > 0 ? $"{c.keys[c.length - 1].time:F3}:{c.keys[c.length - 1].value:F5}" : "";
                    lines.Add($"  {b.propertyName}\tkeys={c.length}\tfirst={first}\tlast={last}");
                }
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText($"Logs/curve_{id}_{state}_{boneName}.txt", string.Join("\n", lines) + "\n");
            return string.Join("\n", lines);
        }

        // Recorrido de la mano que sostiene el prop en cada toma. Para elegir la base de
        // un especial con instrumento: importa el arco de la MANO, no solo el giro.
        public static string HandArc(string id, string[] states, int steps = 24)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            if (prefab == null) return "ERROR: falta prefab " + id;
            var raw = LoadRawClips(id);
            var lines = new List<string> { "take\tlen\tturn\tarcL\tspanL\tarcR\tspanR\tdownL\tlowestL" };
            foreach (var state in states)
            {
                if (!raw.TryGetValue(state, out var clip) || clip == null) continue;
                int n = Mathf.Max(2, steps);
                var ptsL = new List<Vector3>();
                var ptsR = new List<Vector3>();
                float maxTurn = 0f, prev = 0f, baseYaw = float.NaN, lowest = 0f;
                for (int i = 0; i <= n; i++)
                {
                    var go = UnityEngine.Object.Instantiate(prefab);
                    try
                    {
                        var animator = go.GetComponentInChildren<Animator>(true);
                        if (animator != null) animator.enabled = false;
                        var all = go.GetComponentsInChildren<Transform>(true);
                        clip.SampleAnimation(go, Mathf.Min(clip.length * i / n, clip.length - 0.0001f));
                        var pelvis = Find(all, "DEF-spine") ?? Find(all, "DEF-pelvis");
                        var hl = Find(all, "DEF-hand.L");
                        var hr = Find(all, "DEF-hand.R");
                        if (pelvis != null)
                        {
                            float y = KomBodyYaw.TwistYaw(ChainRot(pelvis));
                            if (float.IsNaN(baseYaw)) { baseYaw = y; prev = y; }
                            y = Unwrap(y, ref prev);
                            maxTurn = Mathf.Max(maxTurn, Mathf.Abs(y - baseYaw));
                        }
                        if (hl != null) ptsL.Add(ChainPos(hl));
                        if (hr != null) ptsR.Add(ChainPos(hr));
                        if (hl != null) lowest = Mathf.Min(lowest, ChainPos(hl).y);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(go); }
                }
                lines.Add(string.Join("\t", new[]
                {
                    state, clip.length.ToString("F2"), maxTurn.ToString("F1"),
                    Arc(ptsL).ToString("F3"), Span(ptsL).ToString("F3"),
                    Arc(ptsR).ToString("F3"), Span(ptsR).ToString("F3"),
                    (Arc(ptsL) / Mathf.Max(0.01f, clip.length)).ToString("F2"),
                    lowest.ToString("F3")
                }));
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText($"Logs/handarc_{id}.txt", string.Join("\n", lines) + "\n");
            return string.Join("\n", lines);
        }

        static float Arc(List<Vector3> pts)
        {
            float d = 0f;
            for (int i = 1; i < pts.Count; i++) d += Vector3.Distance(pts[i - 1], pts[i]);
            return d;
        }

        static float Span(List<Vector3> pts)
        {
            float best = 0f;
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                    best = Mathf.Max(best, Vector3.Distance(pts[i], pts[j]));
            return best;
        }

        // Perfil de altura/alcance de las manos a lo largo del clip. Sirve para clavar el
        // frame de impacto de un especial con prop (el pico de la mano que lo lleva).
        public static string HandProfile(string id, string state, int steps = 24)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            if (prefab == null) return "ERROR: falta prefab " + id;
            var raw = LoadRawClips(id);
            if (!raw.TryGetValue(state, out var clip) || clip == null) return "ERROR: falta toma " + state;
            var lines = new List<string> { "t_norm\tt\tmLx\tmLy\tmLz\tmRx\tmRy\tmRz" };
            for (int i = 0; i <= steps; i++)
            {
                float norm = i / (float)steps;
                var go = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    var animator = go.GetComponentInChildren<Animator>(true);
                    if (animator != null) animator.enabled = false;
                    var all = go.GetComponentsInChildren<Transform>(true);
                    clip.SampleAnimation(go, Mathf.Min(norm * clip.length, clip.length - 0.0001f));
                    var hl = Find(all, "DEF-hand.L");
                    var hr = Find(all, "DEF-hand.R");
                    var pl = hl != null ? ChainPos(hl) : Vector3.zero;
                    var pr = hr != null ? ChainPos(hr) : Vector3.zero;
                    lines.Add($"{norm:F3}\t{norm * clip.length:F3}\t{pl.x:F3}\t{pl.y:F3}\t{pl.z:F3}\t{pr.x:F3}\t{pr.y:F3}\t{pr.z:F3}");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText($"Logs/handprofile_{id}_{state}.txt", string.Join("\n", lines) + "\n");
            return string.Join("\n", lines);
        }

        // Punto de golpe REAL del prop (BodyEdge) a lo largo del clip, en espacio del
        // modelo. Es la unica medida que dice en que frame el instrumento esta delante del
        // rival; el render depende de la camara del banco y no sirve para cronometrar.
        public static string PropEdgeProfile(string id, string state, string propName, int steps = 24)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            var propPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Props/{propName}.prefab");
            if (prefab == null || propPrefab == null) return "ERROR: faltan assets";
            var clip = FindBaked(id, state);
            if (clip == null) return "ERROR: falta clip " + state;

            var lines = new List<string> { "t_norm\tt\tedgeX\tedgeY\tedgeZ\thandX\thandY\thandZ" };
            for (int i = 0; i <= steps; i++)
            {
                float norm = i / (float)steps;
                var go = UnityEngine.Object.Instantiate(prefab);
                var prop = UnityEngine.Object.Instantiate(propPrefab);
                try
                {
                    var animator = go.GetComponentInChildren<Animator>(true);
                    if (animator != null) animator.enabled = false;
                    var fighter = go.GetComponent<Fighter>();
                    if (fighter != null) fighter.enabled = false;
                    var all = go.GetComponentsInChildren<Transform>(true);
                    clip.SampleAnimation(go, Mathf.Min(norm * clip.length, clip.length - 0.0001f));
                    var hand = Find(all, "DEF-hand.L");
                    var finger = Find(all, "DEF-f_index.01.L");
                    if (hand == null) return "ERROR: falta DEF-hand.L";
                    var chest = Find(all, "DEF-spine.003") ?? Find(all, "DEF-spine.002") ?? Find(all, "DEF-spine");
                    // Con pecho y raiz el prop pasa por el MISMO solver que en runtime
                    // (KomClubGrip), asi la medicion y el juego no pueden divergir.
                    KomPropGrip.AttachGuitar(prop, hand, finger, chest, go.transform);
                    var edge = KomPropGrip.BodyEdge(prop);
                    var hp = ChainPos(hand);
                    var root = go.transform.position;
                    lines.Add($"{norm:F3}\t{norm * clip.length:F3}\t{edge.x - root.x:F3}\t{edge.y:F3}\t{edge.z - root.z:F3}\t{hp.x - root.x:F3}\t{hp.y:F3}\t{hp.z:F3}");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(prop);
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText($"Logs/edge_{id}_{state}.txt", string.Join("\n", lines) + "\n");
            return string.Join("\n", lines);
        }

        // GATE de los especiales con prop. La invariante es que el clip HORNEADO
        // reproduzca la pose de la toma de origen: si el horneado se come curvas (el caso
        // de m_LocalRotation.w) la malla se deforma aunque el clip "exista". Se compara
        // frame a frame contra la toma original y ademas se exige in-place y sin giro
        // excesivo. Fue justo lo que se colo a produccion: pies a 1.79 m, cuerpo girado
        // 173 grados y piernas desgarradas, con todos los gates en verde.
        public static string Gate(int steps = 24)
        {
            var lines = new List<string> { "# GATE especiales (horneado vs toma de origen)" };
            var warn = new List<string>();
            bool ok = true;
            foreach (var def in KomData.Characters.characters)
            {
                if (def == null || !def.HasSpecial) continue;
                var raw = LoadRawClips(def.id);
                for (int i = 0; i < def.SpecialCount; i++)
                {
                    var sp = def.GetSpecial(i);
                    if (sp == null || !sp.IsCombatSpecial || string.IsNullOrEmpty(sp.animatorState)) continue;
                    string state = sp.animatorState;
                    var baked = FindBaked(def.id, state);
                    if (baked == null) { lines.Add($"FAIL {def.id}/{state}: falta el clip horneado"); ok = false; continue; }

                    string source = KomSetup.StateSourceTake.TryGetValue(state, out var alt) ? alt : state;
                    if (!raw.TryGetValue(source, out var rawClip) || rawClip == null)
                    {
                        lines.Add($"FAIL {def.id}/{state}: falta la toma de origen '{source}'");
                        ok = false;
                        continue;
                    }

                    var b = MeasureClip(def.id, baked, steps);
                    var r = MeasureClip(def.id, rawClip, steps);
                    float dPelvis = Mathf.Abs(Mathf.DeltaAngle(b.pelvisYaw, r.pelvisYaw));
                    float dFoot = Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(b.footLYaw, r.footLYaw)),
                                            Mathf.Abs(Mathf.DeltaAngle(b.footRYaw, r.footRYaw)));
                    float dHeight = Mathf.Max(Mathf.Abs(b.minFootY - r.minFootY), Mathf.Abs(b.maxFootY - r.maxFootY));

                    var problems = new List<string>();
                    if (dPelvis > 8f) problems.Add($"pose girada {dPelvis:F1}deg vs origen");
                    if (dFoot > 20f) problems.Add($"pies desviados {dFoot:F1}deg vs origen");
                    if (dHeight > 0.12f) problems.Add($"altura de pies {dHeight:F3}m vs origen");
                    if (b.hipTravel > 0.12f) problems.Add($"cadera viaja {b.hipTravel:F3}m");
                    // Un especial no puede darse la vuelta. 150 grados es el umbral de
                    // "se gira de espaldas" (el bug original: 173.5). Entre 80 y 150 se
                    // avisa: es un pivote grande, revisar el diseno.
                    // Un especial PUEDE girar (Two Hand Club Combo gira 173 grados por
                    // diseno: es un combo giratorio). Lo que no puede es TERMINAR mirando a
                    // otro lado, porque el Fighter no reorienta al personaje al acabar.
                    if (b.netTurn > 20f)
                    {
                        // Los clips de INTERPRETACION giran al personaje por diseno (el
                        // cantante se dirige al publico). Se reporta como aviso; el resto
                        // de especiales no pueden terminar de espaldas.
                        if (TurnTolerantStates.Contains(state))
                            warn.Add($"{def.id}/{state} acaba girado {b.netTurn:F1}deg (interpretacion)");
                        else
                            problems.Add($"acaba girado {b.netTurn:F1}deg");
                    }
                    if (b.turn > 80f) warn.Add($"{def.id}/{state} gira {b.turn:F1}deg (por diseno)");

                    if (problems.Count == 0)
                        lines.Add($"ok   {def.id}/{state} <- {source}: dPelvis={dPelvis:F1} dPie={dFoot:F1} dAltura={dHeight:F3} cadera={b.hipTravel:F3} giroMax={b.turn:F1} giroFinal={b.netTurn:F1}");
                    else { lines.Add($"FAIL {def.id}/{state} <- {source}: " + string.Join(" | ", problems)); ok = false; }
                }
            }
            foreach (var w in warn) lines.Add("warn " + w);
            lines.Add(ok ? "VERDICT: PASS" : "VERDICT: FAIL");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/special_gate.txt", string.Join("\n", lines) + "\n");
            return string.Join("\n", lines);
        }

        // Estados cuyo clip gira al personaje a proposito (interpretacion en escena).
        static readonly HashSet<string> TurnTolerantStates = new HashSet<string> { "Singing" };

        struct ClipMetrics
        {
            public float minFootY, maxFootY, turn, netTurn, hipTravel;
            public float pelvisYaw, footLYaw, footRYaw;
        }

        // Metricas agregadas de un clip en la instancia real del prefab.
        static ClipMetrics MeasureClip(string id, AnimationClip clip, int steps)
        {
            var m = new ClipMetrics { minFootY = float.MaxValue, maxFootY = float.MinValue };
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            if (prefab == null) return m;
            int n = Mathf.Max(2, steps);
            float prevP = 0f, baseP = float.NaN;
            Vector3? origin = null;
            for (int i = 0; i <= n; i++)
            {
                var go = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    var animator = go.GetComponentInChildren<Animator>(true);
                    if (animator != null) animator.enabled = false;
                    var all = go.GetComponentsInChildren<Transform>(true);
                    clip.SampleAnimation(go, Mathf.Min(clip.length * i / n, clip.length - 0.0001f));
                    var pelvis = Find(all, "DEF-spine") ?? Find(all, "DEF-pelvis");
                    var fl = Find(all, "DEF-foot.L");
                    var fr = Find(all, "DEF-foot.R");
                    if (pelvis == null) continue;
                    var hip = ChainPos(pelvis);
                    if (origin == null) origin = hip;
                    m.hipTravel = Mathf.Max(m.hipTravel,
                        new Vector2(hip.x - origin.Value.x, hip.z - origin.Value.z).magnitude);

                    float yp = KomBodyYaw.TwistYaw(ChainRot(pelvis));
                    if (float.IsNaN(baseP)) { baseP = yp; prevP = yp; }
                    yp = Unwrap(yp, ref prevP);
                    m.turn = Mathf.Max(m.turn, Mathf.Abs(yp - baseP));
                    m.pelvisYaw = baseP;
                    m.netTurn = Mathf.Abs(yp - baseP);
                    if (fl != null) m.footLYaw = KomBodyYaw.TwistYaw(ChainRot(fl));
                    if (fr != null) m.footRYaw = KomBodyYaw.TwistYaw(ChainRot(fr));

                    float y = float.MaxValue;
                    if (fl != null) y = Mathf.Min(y, ChainPos(fl).y);
                    if (fr != null) y = Mathf.Min(y, ChainPos(fr).y);
                    if (y < float.MaxValue)
                    {
                        m.minFootY = Mathf.Min(m.minFootY, y);
                        m.maxFootY = Mathf.Max(m.maxFootY, y);
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            if (m.minFootY == float.MaxValue) { m.minFootY = 0f; m.maxFootY = 0f; }
            return m;
        }

        // Perfil geometrico del prop a lo largo de su eje largo: ancho y alto por tramo.
        // Sirve para saber donde esta el CUERPO (ancho) y donde el MANGO (estrecho), que es
        // el punto por el que se empuna la guitarra como si fuera una espada.
        public static string PropShape(string propName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Props/{propName}.prefab");
            if (prefab == null) return "ERROR: falta prop " + propName;
            var go = UnityEngine.Object.Instantiate(prefab);
            var lines = new List<string> { $"# SHAPE {propName}" };
            try
            {
                var mf = go.GetComponentInChildren<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) return "ERROR: sin mesh";
                var mesh = mf.sharedMesh;
                var verts = mesh.vertices;
                var b = mesh.bounds;
                lines.Add($"verts={verts.Length} boundsC=({b.center.x:F4},{b.center.y:F4},{b.center.z:F4}) size=({b.size.x:F4},{b.size.y:F4},{b.size.z:F4})");
                int bins = 24;
                float y0 = b.min.y, y1 = b.max.y;
                var width = new float[bins];
                var count = new int[bins];
                foreach (var v in verts)
                {
                    int k = Mathf.Clamp((int)((v.y - y0) / Mathf.Max(1e-6f, y1 - y0) * bins), 0, bins - 1);
                    width[k] = Mathf.Max(width[k], Mathf.Abs(v.x - b.center.x));
                    count[k]++;
                }
                lines.Add("bin\tyNorm\twidthX\tverts");
                for (int i = 0; i < bins; i++)
                    lines.Add($"{i}\t{(i + 0.5f) / bins:F3}\t{width[i] * 2f:F5}\t{count[i]}");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            Directory.CreateDirectory("Logs");
            File.WriteAllText($"Logs/shape_{propName}.txt", string.Join("\n", lines) + "\n");
            return string.Join("\n", lines);
        }

        // Informe de agarre del prop a lo largo del clip, con el agarre ACTUAL:
        //   edge    punto de golpe (BodyEdge) en espacio del modelo
        //   grip    punto de la palma
        //   aim     direccion pecho -> palma (hacia donde debe salir el instrumento)
        //   body    eje del cuerpo del prop (su -Y) en espacio del modelo
        //   ang     angulo body vs aim: 0 = el instrumento sale alineado con los brazos,
        //           que es como se empuna un arma a dos manos.
        public static string GripReport(string id, string state, int steps = 24)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            var propPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Props/guitarra.prefab");
            if (prefab == null || propPrefab == null) return "ERROR: faltan assets";
            var clip = FindBaked(id, state);
            if (clip == null) return "ERROR: falta clip " + state;

            var lines = new List<string> { "t_norm\tt\tedgeX\tedgeY\tedgeZ\tchestX\tchestY\tchestZ\taimX\taimY\taimZ\treach\tangDeg\tgripErr\tfwdDeg\tbodyX\tbodyY\tbodyZ" };
            for (int i = 0; i <= steps; i++)
            {
                float norm = i / (float)steps;
                var go = UnityEngine.Object.Instantiate(prefab);
                var prop = UnityEngine.Object.Instantiate(propPrefab);
                try
                {
                    var animator = go.GetComponentInChildren<Animator>(true);
                    if (animator != null) animator.enabled = false;
                    var fighter = go.GetComponent<Fighter>();
                    if (fighter != null) fighter.enabled = false;
                    var all = go.GetComponentsInChildren<Transform>(true);
                    clip.SampleAnimation(go, Mathf.Min(norm * clip.length, clip.length - 0.0001f));
                    var handL = Find(all, "DEF-hand.L");
                    var fingerL = Find(all, "DEF-f_index.01.L");
                    var handR = Find(all, "DEF-hand.R");
                    var chest = Find(all, "DEF-spine.003") ?? Find(all, "DEF-spine.002") ?? Find(all, "DEF-spine");
                    if (handL == null || chest == null) return "ERROR: faltan huesos";
                    KomPropGrip.AttachGuitar(prop, handL, fingerL, chest, go.transform);

                    var grip = KomPropGrip.PalmLocal(handL, fingerL);
                    var gripWorld = handL.TransformPoint(grip);
                    var chestWorld = chest.position;
                    var aim = (gripWorld - chestWorld).normalized;

                    // Eje del cuerpo del prop: su -Y local llevado a espacio del modelo.
                    var bodyDir = (prop.transform.rotation * Vector3.down).normalized;
                    float ang = Vector3.Angle(bodyDir, aim);
                    // Alineacion con el FRENTE del modelo: el rival esta en +Z local
                    // (medido en Play: localDir=(-0.17,0,0.98)). Es el angulo que decide si
                    // el golpe conecta, no el de los brazos.
                    float fwdAng = Vector3.Angle(bodyDir, go.transform.forward);
                    float gripErr = handR != null ? Vector3.Distance(handR.position, gripWorld) : 0f;
                    var edge = KomPropGrip.BodyEdge(prop);
                    var root = go.transform.position;

                    // Alcance real del arma: cuanto avanza el borde del cuerpo por delante
                    // del pecho, medido SOBRE la direccion en la que apuntan los brazos. Es
                    // el unico criterio valido cuando el clip gira al personaje, porque un
                    // "adelante" fijo en espacio del modelo deja de significar nada.
                    float reach = Vector3.Dot(edge - chestWorld, aim);
                    lines.Add($"{norm:F3}\t{norm * clip.length:F3}\t{edge.x - root.x:F3}\t{edge.y:F3}\t{edge.z - root.z:F3}\t" +
                              $"{chestWorld.x - root.x:F3}\t{chestWorld.y:F3}\t{chestWorld.z - root.z:F3}\t" +
                              $"{aim.x:F3}\t{aim.y:F3}\t{aim.z:F3}\t{reach:F3}\t{ang:F1}\t{gripErr:F3}\t{fwdAng:F1}\t{bodyDir.x:F3}\t{bodyDir.y:F3}\t{bodyDir.z:F3}");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(prop);
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText($"Logs/gripreport_{id}_{state}.txt", string.Join("\n", lines) + "\n");
            return string.Join("\n", lines);
        }

        public static string AuditAll()
        {
            var ids = KomData.Characters.characters.Where(c => c != null).Select(c => c.id).ToArray();
            var summary = new List<string>();
            foreach (var id in ids) summary.Add(Audit(id));
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/special_audit_all.txt", string.Join("\n", summary) + "\n");
            return string.Join("\n", summary);
        }

        static string Summarize(string id, List<string> lines)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{id}]");
            foreach (var group in lines.Where(l => l.StartsWith("RAW\t") || l.StartsWith("BAKED\t") || l.StartsWith("clip\t")))
                sb.AppendLine("  " + group);
            foreach (var block in GroupByClip(lines))
            {
                sb.AppendLine($"  {block.key}: pelvisYaw max={block.maxPelvis:F1}  footL max={block.maxFootL:F1}  " +
                              $"footR max={block.maxFootR:F1}  hipTravel={block.hipTravel:F3}m  footYmin={block.minFootY:F3}m  " +
                              $"armYaw={block.armYaw:F1}");
            }
            return sb.ToString();
        }

        sealed class Block
        {
            public string key;
            public float maxPelvis, maxFootL, maxFootR, hipTravel, minFootY, armYaw;
        }

        static List<Block> GroupByClip(List<string> lines)
        {
            var blocks = new List<Block>();
            Block cur = null;
            foreach (var line in lines)
            {
                if (line.StartsWith("RAW\t") || line.StartsWith("BAKED\t"))
                {
                    var parts = line.Split('\t');
                    if (cur == null || cur.key != parts[0] + " " + parts[1])
                    {
                        cur = new Block { key = parts[0] + " " + parts[1], minFootY = float.MaxValue };
                        blocks.Add(cur);
                    }
                    if (parts.Length < 12) continue;
                    float P(int i) => float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
                    cur.maxPelvis = Mathf.Max(cur.maxPelvis, Mathf.Abs(P(2)));
                    cur.maxFootL = Mathf.Max(cur.maxFootL, Mathf.Abs(P(3)));
                    cur.maxFootR = Mathf.Max(cur.maxFootR, Mathf.Abs(P(4)));
                    cur.hipTravel = Mathf.Max(cur.hipTravel, Mathf.Sqrt(P(5) * P(5) + P(6) * P(6)));
                    cur.minFootY = Mathf.Min(cur.minFootY, P(7));
                    cur.armYaw = Mathf.Max(cur.armYaw, Mathf.Abs(P(9)));
                }
            }
            return blocks;
        }

        static Dictionary<string, AnimationClip> LoadRawClips(string id)
        {
            var map = new Dictionary<string, AnimationClip>();
            string fbx = $"{FbxDir}/{id}.fbx";
            if (!File.Exists(fbx)) return map;
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>())
            {
                if (clip == null || clip.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                map[KomSetup.CleanTakeName(clip.name)] = clip;
            }
            return map;
        }

        static void Measure(GameObject prefab, string id, AnimationClip clip, int steps, List<string> lines, string tag)
        {
            int n = Mathf.Max(2, steps);
            var state = clip.name;
            for (int i = 0; i <= n; i++)
            {
                float t = Mathf.Min(clip.length * i / n, clip.length - 0.0001f);
                var go = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    var animator = go.GetComponentInChildren<Animator>(true);
                    if (animator != null) animator.enabled = false;
                    var all = go.GetComponentsInChildren<Transform>(true);
                    clip.SampleAnimation(go, t);

                    var pelvis = Find(all, "DEF-spine") ?? Find(all, "DEF-pelvis");
                    var footL = Find(all, "DEF-foot.L");
                    var footR = Find(all, "DEF-foot.R");
                    var toeL = Find(all, "DEF-toe.L");
                    var toeR = Find(all, "DEF-toe.R");
                    if (pelvis == null) { lines.Add($"{tag}\t{state}\tMISSING_PELVIS"); return; }

                    var armature = pelvis;
                    while (armature.parent != null && armature.parent != go.transform) armature = armature.parent;

                    var qP = ChainRot(pelvis);
                    float yawP = KomBodyYaw.TwistYaw(qP);
                    float yawL = footL != null ? KomBodyYaw.TwistYaw(ChainRot(footL)) : 0f;
                    float yawR = footR != null ? KomBodyYaw.TwistYaw(ChainRot(footR)) : 0f;

                    var hip = ChainPos(pelvis);
                    float footY = float.MaxValue;
                    if (footL != null) footY = Mathf.Min(footY, ChainPos(footL).y);
                    if (footR != null) footY = Mathf.Min(footY, ChainPos(footR).y);
                    if (toeL != null) footY = Mathf.Min(footY, ChainPos(toeL).y);
                    if (toeR != null) footY = Mathf.Min(footY, ChainPos(toeR).y);
                    if (footY == float.MaxValue) footY = 0f;

                    var ae = armature.localRotation.eulerAngles;
                    float ay = Mathf.DeltaAngle(0f, ae.y);

                    lines.Add(string.Join("\t", new[]
                    {
                        tag, state,
                        F(yawP), F(yawL), F(yawR),
                        F(hip.x), F(hip.z), F(footY),
                        F(ae.x), F(ay), F(ae.z), F(armature.localPosition.y)
                    }));
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
        }

        static string F(float v) => v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

        // Desenrolla un angulo para que la variacion sea continua al cruzar +-180.
        static float Unwrap(float value, ref float previous)
        {
            float v = value;
            while (v - previous > 180f) v -= 360f;
            while (v - previous < -180f) v += 360f;
            previous = v;
            return v;
        }

        static Transform Find(Transform[] all, string name)
        {
            for (int i = 0; i < all.Length; i++) if (all[i] != null && all[i].name == name) return all[i];
            return null;
        }

        static Quaternion ChainRot(Transform t)
        {
            var q = t.localRotation;
            for (var p = t.parent; p != null; p = p.parent) q = p.localRotation * q;
            return q;
        }

        static Vector3 ChainPos(Transform t)
        {
            var m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
            for (var p = t.parent; p != null; p = p.parent)
                m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
            return m.GetColumn(3);
        }
    }
}
