using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KOM.EditorTools
{
    // Gate NUMERICO de deformacion. Mide lo unico que puede "romper" la malla de un
    // esqueleto rigido: que cambie la longitud entre hueso y hueso (escala o posicion
    // local animada) o que un hueso de deformacion salga de escala 1.
    // Si la longitud se conserva, la malla NO puede desgarrarse: solo puede haber
    // colapso normal de codo/rodilla (candy-wrapper), que se arregla con pesos, no con mezcla.
    // Escribe Logs/anim_skeleton.txt
    public static class KomAnimDiag
    {
        const string PrefabDir = "Assets/Resources/Characters";
        const string ReportPath = "Logs/anim_skeleton.txt";

        // Umbral duro: 1% de cambio de longitud ya es deformacion visible.
        public const float BoneLenFail = 0.01f;
        // Salto maximo de la raiz del armature entre frames consecutivos. 60 mm/frame ya es
        // un temblor visible; las animaciones sanas dan 0 y las que caen al suelo ~25 mm.
        public const float RootJumpFail = 0.06f;
        
        static readonly string[] Probe =
        {
            "Idle", "Run", "Boxing", "LightPunch", "HeavyPunch", "LightKick", "HeavyKick",
            "Jump", "Hit", "HitHeavy", "KO", "GetUp", "Crouch", "WalkGuard", "Block", "Stunned",
            "GroinHit", "Uppercut", "DropKick", "QuadPunch",
            // Los tres combos por secuencia quedaban FUERA de este gate, que es el unico que
            // mide deformacion real de la malla. Un clip que deforma y no se mide es un clip
            // que se "arregla" a ciegas: entran aqui para que no vuelva a pasar.
            "Combo1", "Combo2", "Combo3",
            // El festejo tambien se mide. Estaba fuera del gate y por eso su deformacion
            // sobrevivio a todas las pasadas anteriores.
            "Victory"
        };

        [MenuItem("King of Music/Diagnostico/4 - Gate de deformacion", priority = 210)]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("KOM GATE DE DEFORMACION · longitud de hueso vs bind pose");
            sb.AppendLine("Unity " + Application.unityVersion + " · " + DateTime.Now.ToString("u"));
            sb.AppendLine($"FAIL si |len/bind-1| > {BoneLenFail:P0} o |scale-1| > 0.01 en huesos DEF con longitud > 1 cm");
            sb.AppendLine();

            int totalFails = 0;
            foreach (var def in KomData.Characters.characters)
            {
                if (def == null) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{def.id}.prefab");
                if (prefab == null) { sb.AppendLine($"{def.id}: falta prefab"); continue; }

                var scene = EditorSceneManager.NewPreviewScene();
                GameObject go = null;
                try
                {
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    var animator = go.GetComponentInChildren<Animator>(true);
                    if (animator != null) animator.enabled = false;
                    if (smr == null) { sb.AppendLine($"{def.id}: sin SMR"); continue; }

                    var bones = smr.bones.Where(b => b != null).ToArray();

                    // Referencia = bind pose real de la malla. Rebind deja el esqueleto
                    // exactamente en la pose con la que se skino. Se mide en world space:
                    // si la longitud padre-hijo se conserva, la malla no puede desgarrarse.
                    if (animator != null) animator.Rebind();

                    var pairs = new List<(Transform child, Transform parent, float bindLen, float restLen)>();
                    foreach (var b in bones)
                    {
                        if (b.parent == null) continue;
                        // Solo la cadena DEF (la que skinea la malla). Los huesos de control
                        // MCH/ORG se mueven libremente sin deformar nada.
                        if (!b.name.StartsWith("DEF-", StringComparison.Ordinal)) continue;
                        if (!b.parent.name.StartsWith("DEF-", StringComparison.Ordinal)) continue;
                        float bl = Vector3.Distance(ChainPosition(b), ChainPosition(b.parent));
                        if (bl < 0.01f) continue;
                        pairs.Add((b, b.parent, bl, bl));
                    }

                    sb.AppendLine($"=== {def.id} · bones={bones.Length} pares utiles={pairs.Count} verts={smr.sharedMesh.vertexCount} ===");
                    sb.AppendLine("  estado/clip            peorLenDev  hueso                     peorScaleDev  [veredicto]");

                    int fails = 0;
                    foreach (var name in Probe)
                    {
                        var clip = FindClip(def.id, name);
                        if (clip == null) continue;
                        var worst = (dev: 0f, bone: "-", scale: 0f);
                        foreach (float t in new[] { 0.10f, 0.30f, 0.50f, 0.70f, 0.90f })
                        {
                            clip.SampleAnimation(go, t * clip.length);
                            var m = Measure(pairs);
                            if (m.dev > worst.dev) worst = (m.dev, m.bone, Mathf.Max(worst.scale, m.scale));
                            else worst.scale = Mathf.Max(worst.scale, m.scale);
                        }
                        bool fail = worst.dev > BoneLenFail || worst.scale > 0.01f;
                        if (fail) fails++;
                        sb.AppendLine($"  {name,-22} {worst.dev,10:P2}  {worst.bone,-26} {worst.scale,11:P2}  [{(fail ? "FAIL" : "ok")}]");

                        // Gate DENSO: 60 muestras/segundo. Las 5 fases pueden caer en keys
                        // validas y ocultar overshoot entre keys (huesos flotando, quaternions
                        // no unitarios y malla deformada solo al reproducir).
                        int denseSteps = Mathf.Clamp(Mathf.RoundToInt(clip.length * 60f), 2, 600);
                        float denseDev = 0f, denseScale = 0f, worstNorm = 0f;
                        string denseBone = "-";
                        for (int s = 0; s <= denseSteps; s++)
                        {
                            clip.SampleAnimation(go, clip.length * s / denseSteps);
                            var dm = Measure(pairs);
                            if (dm.dev > denseDev) { denseDev = dm.dev; denseBone = dm.bone; }
                            if (dm.scale > denseScale) denseScale = dm.scale;
                            foreach (var b in bones)
                            {
                                if (b == null) continue;
                                var q = b.localRotation;
                                float norm = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                                float normDev = Mathf.Abs(norm - 1f);
                                if (normDev > worstNorm) { worstNorm = normDev; denseBone = b.name; }
                            }
                        }
                        bool denseFail = denseDev > BoneLenFail || denseScale > 0.01f || worstNorm > 0.01f;
                        if (denseFail) fails++;
                        sb.AppendLine($"  DENSE {name,-16} {denseDev,10:P2}  {denseBone,-26} {denseScale,11:P2}  qnorm {worstNorm:P2}  [{(denseFail ? "FAIL" : "ok")}]");
                    }

                    // Mezclas emuladas exactamente como el Animator: lerp de TRS local al 50%.
                    var mixes = new[]
                    {
                        ("Idle", "WalkGuard"), ("WalkGuard", "Run"), ("WalkGuard", "Boxing"),
                        ("WalkGuard", "LightPunch"), ("WalkGuard", "HeavyKick"), ("Idle", "Boxing"),
                        ("Boxing", "LightPunch"), ("Hit", "WalkGuard"), ("KO", "GetUp"),
                        ("Block", "WalkGuard"), ("Idle", "Crouch"),
                        ("Crouch", "LightKick"), ("Crouch", "Uppercut"),
                        ("Hit", "GroinHit")
                    };
                    foreach (var (a, b) in mixes)
                    {
                        var ca = FindClip(def.id, a);
                        var cb = FindClip(def.id, b);
                        if (ca == null || cb == null) continue;
                        var pa = SamplePose(go, ca, ca.length * 0.35f);
                        var pb = SamplePose(go, cb, cb.length * 0.35f);
                        BlendPose(go, pa, pb, 0.5f);
                        var m = Measure(pairs);
                        bool fail = m.dev > BoneLenFail || m.scale > 0.01f;
                        if (fail) fails++;
                        sb.AppendLine($"  MEZCLA {a}+{b,-14} {m.dev,10:P2}  {m.bone,-26} {m.scale,11:P2}  [{(fail ? "FAIL" : "ok")}]");
                    }

                    // GATE DE INTEGRIDAD DE CURVAS. Cierra las tres regresiones que costaron
                    // dias y que la medida de longitud de hueso NO detecta (una traslacion de
                    // 5 cm del tren superior no cambia ninguna arista):
                    //   1. Clip _Clean sin rotaciones (fuente en Euler): 88 en vez de 194.
                    //   2. Curva de posicion aplanada por SimplifyCurve: el torso se queda
                    //      clavado mientras la pelvis se mueve -> estiramiento "gelatinoso".
                    //   3. Raiz que salta entre frames (compensacion in-place mal calculada).
                    fails += CurveIntegrity(def.id, go, sb);

                    sb.AppendLine($"  => {def.id}: {(fails == 0 ? "PASS" : fails + " FALLOS")}");
                    totalFails += fails;
                }
                catch (Exception e)
                {
                    sb.AppendLine($"{def.id}: ERROR {e}");
                    totalFails++;
                }
                finally
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                    EditorSceneManager.ClosePreviewScene(scene);
                }
                sb.AppendLine();
            }

            sb.AppendLine($"VERDICT: {(totalFails == 0 ? "PASS" : "FAIL (" + totalFails + " fallos)")}");
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, sb.ToString());
            Debug.Log($"[KOM][DIAG] {ReportPath} · {sb.ToString().Split('\n').Last(l => l.StartsWith("VERDICT"))}");
        }

        [MenuItem("King of Music/Diagnostico/7 - Deriva de raiz", priority = 213)]
        public static void RootDrift()
        {
            var sb = new StringBuilder();
            sb.AppendLine("KOM ROOT DRIFT · desplazamiento de la raiz dentro del clip (debe ser 0 en un juego con movimiento por codigo)");
            sb.AppendLine("drift = distancia horizontal entre la raiz en t=0 y en t=fin");
            sb.AppendLine();
            foreach (var def in KomData.Characters.characters)
            {
                if (def == null) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{def.id}.prefab");
                if (prefab == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    var animator = go.GetComponentInChildren<Animator>(true);
                    if (animator != null) animator.enabled = false;
                    var root = go.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(t => t.name == "root" || t.name == "Root");
                    if (root == null) { sb.AppendLine($"{def.id}: sin hueso 'root'"); continue; }
                    sb.AppendLine($"=== {def.id} · root='{root.name}' ===");
                    sb.AppendLine("  clip               len   dx     dy     dz    |drift|  vuelve?");
                    foreach (var clip in AllClips(def.id))
                    {
                        clip.SampleAnimation(go, 0f);
                        var p0 = root.position;
                        clip.SampleAnimation(go, Mathf.Max(0f, clip.length - 0.001f));
                        var p1 = root.position;
                        var d = p1 - p0;
                        bool loops = Mathf.Abs(d.x) < 0.01f && Mathf.Abs(d.z) < 0.01f;
                        sb.AppendLine($"  {clip.name,-18} {clip.length,4:F2} {d.x,7:F3} {d.y,7:F3} {d.z,7:F3}  {new Vector2(d.x, d.z).magnitude,7:F3}  {(loops ? "si" : "NO")}");
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
                sb.AppendLine();
            }
            File.WriteAllText("Logs/anim_rootdrift.txt", sb.ToString());
            Debug.Log("[KOM][DIAG] Logs/anim_rootdrift.txt");
        }

        [MenuItem("King of Music/Diagnostico/8 - Trayectoria de cadera", priority = 214)]
        public static void HipTravel()
        {
            var sb = new StringBuilder();
            sb.AppendLine("KOM HIP TRAVEL · recorrido horizontal de 'torso' (cabeza de la cadena DEF) dentro del clip");
            sb.AppendLine("Un clip in-place debe tener recorrido ~0. Si recorre, la malla se sale del collider y da tirones al loopear.");
            sb.AppendLine();
            var def = KomData.Characters.characters.FirstOrDefault(c => c != null);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{def.id}.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;
                var torso = go.GetComponentsInChildren<Transform>(true).First(t => t.name == "torso");
                var pelvis = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "DEF-pelvis");
                sb.AppendLine($"personaje={def.id} torso='{torso.name}' pelvis='{(pelvis != null ? pelvis.name : "-")}'");
                foreach (var clip in AllClips(def.id))
                {
                    float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                    var samples = new List<float>();
                    const int N = 12;
                    for (int i = 0; i < N; i++)
                    {
                        clip.SampleAnimation(go, clip.length * i / (N - 1f));
                        var p = torso.position;
                        minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                        minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                        minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                        samples.Add(p.x);
                    }
                    sb.AppendLine($"  {clip.name,-18} X[{minX,6:F2},{maxX,6:F2}] recorridoX={maxX - minX,5:F2}  Z recorrido={maxZ - minZ,5:F2}  Y[{minY:F2},{maxY:F2}]");
                    if (clip.name.Contains("WalkGuard") || clip.name.Contains("Run"))
                        sb.AppendLine("      X: " + string.Join(" ", samples.Select(v => v.ToString("F2"))));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            File.WriteAllText("Logs/anim_hip.txt", sb.ToString());
            Debug.Log("[KOM][DIAG] Logs/anim_hip.txt");
        }

        [MenuItem("King of Music/Diagnostico/9 - Quien lleva el desplazamiento", priority = 215)]
        public static void WhoTravels()
        {
            var sb = new StringBuilder();
            var def = KomData.Characters.characters.FirstOrDefault(c => c != null);
            foreach (var name in new[] { "WalkGuard", "Run", "Jump", "KO", "GetUp", "Idle" })
            {
                var clip = FindClip(def.id, name);
                if (clip == null) continue;
                sb.AppendLine($"=== {def.id} / {clip.name} ===");
                var rows = new List<(string path, float dx, float dy, float dz)>();
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!b.propertyName.StartsWith("m_LocalPosition")) continue;
                    var c = AnimationUtility.GetEditorCurve(clip, b);
                    if (c == null || c.keys.Length == 0) continue;
                    float lo = c.keys.Min(k => k.value), hi = c.keys.Max(k => k.value);
                    if (hi - lo < 0.01f) continue;
                    int idx = rows.FindIndex(r => r.path == b.path);
                    var cur = idx >= 0 ? rows[idx] : (path: b.path, dx: 0f, dy: 0f, dz: 0f);
                    if (b.propertyName.EndsWith(".x")) cur.dx = hi - lo;
                    else if (b.propertyName.EndsWith(".y")) cur.dy = hi - lo;
                    else cur.dz = hi - lo;
                    if (idx >= 0) rows[idx] = cur; else rows.Add(cur);
                }
                foreach (var r in rows.OrderByDescending(r => Mathf.Max(r.dx, r.dy, r.dz)).Take(5))
                    sb.AppendLine($"  {r.path,-42} dx={r.dx:F3} dy={r.dy:F3} dz={r.dz:F3}");
                sb.AppendLine();
            }
            File.WriteAllText("Logs/anim_who.txt", sb.ToString());
            Debug.Log("[KOM][DIAG] Logs/anim_who.txt");
        }

        [MenuItem("King of Music/Diagnostico/10 - Traza WalkGuard", priority = 216)]
        public static void TraceWalk()
        {
            var sb = new StringBuilder();
            var def = KomData.Characters.characters.FirstOrDefault(c => c != null);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{def.id}.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;
                var all = go.GetComponentsInChildren<Transform>(true);
                var rig = all.First(t => t.name.StartsWith("rig."));
                var root = all.First(t => t.name == "root");
                var torso = all.First(t => t.name == "torso");
                foreach (var src in new[] { "fbx", "clean" })
                {
                    var clip = src == "fbx"
                        ? AssetDatabase.LoadAllAssetsAtPath($"Assets/Art/Characters/{def.id}.fbx").OfType<AnimationClip>().FirstOrDefault(c => c.name == "WalkGuard")
                        : AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/KOM/Animation/Clips/InPlace/{def.id}_WalkGuard_RawWalk.anim");
                    if (clip == null) { sb.AppendLine($"=== {src}: no existe ==="); continue; }
                    sb.AppendLine($"=== {src} · {clip.name} len={clip.length:F2} ===");
                    var chain = new List<Transform>();
                    for (var t = torso; t != null; t = t.parent) chain.Insert(0, t);
                    clip.SampleAnimation(go, 0f);
                    Physics.SyncTransforms();
                    var a = chain.Select(t => t.position).ToArray();
                    var al = chain.Select(t => t.localPosition).ToArray();
                    clip.SampleAnimation(go, clip.length);
                    Physics.SyncTransforms();
                    var b = chain.Select(t => t.position).ToArray();
                    var bl = chain.Select(t => t.localPosition).ToArray();
                    for (int i = 0; i < chain.Count; i++)
                        sb.AppendLine($"  {chain[i].name,-24} world {a[i].ToString("F3")} -> {b[i].ToString("F3")}   local {al[i].ToString("F3")} -> {bl[i].ToString("F3")}");
                    sb.AppendLine();
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            File.WriteAllText("Logs/anim_trace.txt", sb.ToString());
            Debug.Log("[KOM][DIAG] Logs/anim_trace.txt");
        }

        // Perfil del coste real de instanciar un personaje (lo que hace CharacterSelect y
        // Combat al aparecer un luchador). Separa carga de prefab, Instantiate, Rebind y
        // la PRIMERA evaluacion del Animator, que es donde suele estar el tiron.
        [MenuItem("King of Music/Diagnostico/11 - Perfil de instanciacion", priority = 217)]
        public static void ProfileInstantiation()
        {
            var sb = new StringBuilder();
            sb.AppendLine("KOM PERFIL · instanciacion de personajes (ms)");
            sb.AppendLine("load=Resources.Load  inst=Instantiate  rebind=Animator.Rebind  play1=primer Update  play2=Update cacheado");
            sb.AppendLine();
            var sw = new Stopwatch();
            foreach (var def in KomData.Characters.characters)
            {
                if (def == null) continue;
                sw.Restart();
                var prefab = Resources.Load<GameObject>(def.prefabResource);
                sw.Stop(); long load = sw.ElapsedMilliseconds;
                if (prefab == null) { sb.AppendLine($"{def.id}: FALTA {def.prefabResource}"); continue; }

                sw.Restart();
                var go = UnityEngine.Object.Instantiate(prefab);
                sw.Stop(); long inst = sw.ElapsedMilliseconds;

                var animator = go.GetComponentInChildren<Animator>(true);
                long rebind = -1, play1 = -1, play2 = -1;
                if (animator != null)
                {
                    animator.applyRootMotion = false;
                    sw.Restart(); animator.Rebind(); sw.Stop(); rebind = sw.ElapsedMilliseconds;
                    sw.Restart();
                    animator.Play(Animator.StringToHash("Idle"), 0, 0.3f);
                    animator.Update(1f / 60f);
                    sw.Stop(); play1 = sw.ElapsedMilliseconds;
                    sw.Restart();
                    animator.Play(Animator.StringToHash("Idle"), 0, 0.5f);
                    animator.Update(1f / 60f);
                    sw.Stop(); play2 = sw.ElapsedMilliseconds;
                }

                sw.Restart();
                UnityEngine.Object.DestroyImmediate(go);
                sw.Stop(); long destroy = sw.ElapsedMilliseconds;

                sw.Restart();
                var go2 = UnityEngine.Object.Instantiate(prefab);
                sw.Stop(); long inst2 = sw.ElapsedMilliseconds;
                UnityEngine.Object.DestroyImmediate(go2);

                sb.AppendLine($"{def.id,-16} load={load,5} inst={inst,5} inst2={inst2,5} rebind={rebind,5} play1={play1,5} play2={play2,5} destroy={destroy,4}");
            }
            File.WriteAllText("Logs/anim_perf.txt", sb.ToString());
            UnityEngine.Debug.Log("[KOM][PERF] Logs/anim_perf.txt");
        }

        static IEnumerable<AnimationClip> AllClips(string id)
        {
            return AssetDatabase.LoadAllAssetsAtPath($"Assets/Art/Characters/{id}.fbx")
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .OrderBy(c => c.name);
        }

        // Posicion en espacio de modelo desde los TRS locales: no depende de la cache
        // de matrices de mundo del editor (que puede ir un paso por detras tras SampleAnimation).
        static Vector3 ChainPosition(Transform t)
        {
            var m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
            for (var p = t.parent; p != null; p = p.parent)
                m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
            return m.GetColumn(3);
        }

        static (float dev, string bone, float scale) Measure(List<(Transform child, Transform parent, float bindLen, float restLen)> pairs)
        {
            float dev = 0f, scale = 0f;
            string bone = "-";
            foreach (var p in pairs)
            {
                float len = Vector3.Distance(ChainPosition(p.child), ChainPosition(p.parent));
                float d = Mathf.Abs(len / p.bindLen - 1f);
                if (d > dev) { dev = d; bone = p.child.name; }
                var s = p.child.localScale;
                scale = Mathf.Max(scale, Mathf.Abs(s.x - 1f), Mathf.Abs(s.y - 1f), Mathf.Abs(s.z - 1f));
            }
            return (dev, bone, scale);
        }

        struct TRS { public Vector3 pos; public Quaternion rot; public Vector3 scale; }

        static Dictionary<Transform, TRS> SamplePose(GameObject go, AnimationClip clip, float time)
        {
            clip.SampleAnimation(go, time);
            var dict = new Dictionary<Transform, TRS>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                dict[t] = new TRS { pos = t.localPosition, rot = t.localRotation, scale = t.localScale };
            return dict;
        }

        static void BlendPose(GameObject go, Dictionary<Transform, TRS> a, Dictionary<Transform, TRS> b, float w)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (!a.TryGetValue(t, out var pa) || !b.TryGetValue(t, out var pb)) continue;
                t.localPosition = Vector3.Lerp(pa.pos, pb.pos, w);
                t.localRotation = Quaternion.Slerp(pa.rot, pb.rot, w);
                t.localScale = Vector3.Lerp(pa.scale, pb.scale, w);
            }
        }

        static Vector3 BindOrigin(Matrix4x4 bindPose) => bindPose.inverse.MultiplyPoint3x4(Vector3.zero);

        // Audita el desplazamiento HORIZONTAL que el clip mete dentro de la pose (no es
        // root motion de Unity: applyRootMotion=false). Identifica que hueso lo lleva
        // restaurando su bind pose en el frame medio y midiendo cuanto vuelve la cadera.
        [MenuItem("King of Music/Diagnostico/14 - Auditoria de traslacion", priority = 220)]
        public static void TravelAudit()
        {
            var sb = new StringBuilder();
            var def = KomData.Characters.characters.FirstOrDefault(c => c != null);
            if (def == null) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{def.id}.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;
                var all = go.GetComponentsInChildren<Transform>(true);
                var torso = all.First(t => t.name == "torso");
                var hip = all.FirstOrDefault(t => t.name.StartsWith("DEF-pelvis"))
                    ?? all.FirstOrDefault(t => t.name == "DEF-spine") ?? torso;
                var rootBone = all.FirstOrDefault(t => t.name == "root");
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                sb.AppendLine($"prefab={def.id}");
                sb.AppendLine($"  animator='{Path(animator.transform, go.transform)}' smrRootBone='{Path(smr.rootBone, go.transform)}'");
                sb.AppendLine($"  hip='{Path(hip, go.transform)}' torso='{Path(torso, go.transform)}' root='{Path(rootBone, go.transform)}'");

                var bind = new Dictionary<Transform, TRS>();
                if (animator != null) animator.Rebind();
                foreach (var t in all)
                    bind[t] = new TRS { pos = t.localPosition, rot = t.localRotation, scale = t.localScale };

                var clips = new List<(string tag, AnimationClip clip)>();
                var controller = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                if (controller != null)
                {
                    foreach (var cs in controller.layers[0].stateMachine.states)
                    {
                        var motion = cs.state.motion;
                        if (motion is UnityEditor.Animations.BlendTree bt)
                        {
                            foreach (var ch in bt.children)
                                if (ch.motion is AnimationClip cc) clips.Add(($"state:{cs.state.name}@{ch.threshold:0}", cc));
                        }
                        else if (motion is AnimationClip c2) clips.Add(($"state:{cs.state.name}", c2));
                    }
                }
                foreach (var src in new[] { "current", "first" })
                {
                    string path = src == "current"
                        ? $"Assets/Art/Characters/{def.id}.fbx"
                        : $"Assets/Art/Characters/FirstCommit/{def.id}.fbx";
                    foreach (var name in new[] { "Idle", "WalkGuard", "Run", "Boxing" })
                    {
                        var c = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                            .FirstOrDefault(x => x.name == name);
                        if (c != null) clips.Add(($"{src}:{name}", c));
                    }
                }

                var seen = new HashSet<AnimationClip>();
                foreach (var (tag, clip) in clips)
                {
                    if (clip == null || !seen.Add(clip)) continue;
                    sb.AppendLine();
                    sb.AppendLine($"=== {tag} · {clip.name} · {clip.length:F2}s · bindings={AnimationUtility.GetCurveBindings(clip).Length} ===");
                    var pelvis = all.FirstOrDefault(t => t.name == "DEF-spine") ?? torso;
                    var chain = new List<Transform>();
                    for (var t = hip; t != null && t != go.transform; t = t.parent)
                        chain.Insert(0, t);
                    var probeMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    var probeW = new List<Vector3>();
                    var torsoW = new List<Vector3>();
                    var pelvisW = new List<Vector3>();
                    var meshW = new List<Vector3>();
                    const int N = 20;
                    for (int i = 0; i <= N; i++)
                    {
                        if (animator != null) animator.Rebind();
                        clip.SampleAnimation(go, Mathf.Min(clip.length * i / N, clip.length - 0.0001f));
                        torsoW.Add(ChainPosition(torso));
                        pelvisW.Add(ChainPosition(pelvis));
                        probeW.Add(ChainPosition(hip));
                        if (smr != null)
                        {
                            smr.forceMatrixRecalculationPerRender = true;
                            smr.BakeMesh(probeMesh, true);
                            var verts = probeMesh.vertices;
                            Vector3 sum = Vector3.zero;
                            for (int v = 0; v < verts.Length; v++) sum += verts[v];
                            var c = verts.Length > 0 ? sum / verts.Length : Vector3.zero;
                            meshW.Add(smr.transform.TransformPoint(c));
                        }
                    }
                    UnityEngine.Object.DestroyImmediate(probeMesh);
                    Describe(sb, "torso", torsoW);
                    Describe(sb, "DEF-spine", pelvisW);
                    Describe(sb, "DEF-pelvis.L", probeW);
                    Describe(sb, "MALLA centroid", meshW);

                    sb.AppendLine($"  hasRootCurves={clip.hasRootCurves} hasGenericRootTransform={clip.hasGenericRootTransform} hasMotionCurves={clip.hasMotionCurves}");
                    if (animator != null) animator.Rebind();
                    clip.SampleAnimation(go, 0f);
                    var trs0 = chain.Select(t => (p: t.localPosition, r: t.localRotation, s: t.localScale)).ToArray();
                    if (animator != null) animator.Rebind();
                    clip.SampleAnimation(go, clip.length * 0.5f);
                    sb.AppendLine("  huesos cuyo TRS local cambia entre t=0 y t=0.5 (cadena completa):");
                    for (int i = 0; i < chain.Count; i++)
                    {
                        var t = chain[i];
                        var p = t.localPosition; var r = t.localRotation; var s = t.localScale;
                        float dp = (p - trs0[i].p).magnitude;
                        float dr = Quaternion.Angle(r, trs0[i].r);
                        float ds = (s - trs0[i].s).magnitude;
                        if (dp < 0.001f && dr < 0.5f && ds < 0.001f) continue;
                        sb.AppendLine($"    {Path(t, go.transform),-52} dp={dp:F3} dr={dr:F1}° ds={ds:F3}  p0={trs0[i].p.ToString("F3")} p1={p.ToString("F3")} r1={r.eulerAngles.ToString("F0")}");
                    }
                    sb.AppendLine("  curvas con rango > 0.02 en la cadena cadera->raiz:");
                    var chainPaths = new HashSet<string>(chain.Select(t => Path(t, go.transform)));
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (!chainPaths.Contains(b.path)) continue;
                        if (!b.propertyName.StartsWith("m_Local")) continue;
                        var c = AnimationUtility.GetEditorCurve(clip, b);
                        if (c == null || c.keys.Length == 0) continue;
                        float range = c.keys.Max(k => k.value) - c.keys.Min(k => k.value);
                        if (range < 0.02f) continue;
                        sb.AppendLine($"    {b.path,-52} {b.propertyName,-22} rango={range:F3} [{c.keys.Min(k => k.value):F3},{c.keys.Max(k => k.value):F3}]");
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            File.WriteAllText("Logs/anim_travel_audit.txt", sb.ToString());
            Debug.Log("[KOM][DIAG] Logs/anim_travel_audit.txt");
        }

        // Gate de deslizamiento: en un clip in-place, el pie plantado retrocede a la
        // cadencia natural. Si el Fighter avanza a moveSpeed y el Animator reproduce a
        // moveSpeed/natural, el pie queda CLAVADO en el suelo (slip ~0).
        [MenuItem("King of Music/Diagnostico/15 - Deslizamiento de pies", priority = 221)]
        public static void FootSlide()
        {
            var sb = new StringBuilder();
            sb.AppendLine("KOM FOOT SLIDE · velocidad horizontal del pie en contacto (m/s). Debe ser ~0.");
            sb.AppendLine("walk=andar normal · guard=caminar con guardia · run=sprint");
            sb.AppendLine();
            int fails = 0;
            foreach (var def in KomData.Characters.characters)
            {
                if (def == null) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{def.id}.prefab");
                var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                    $"Assets/KOM/Animation/Controllers/{def.id}.controller");
                if (prefab == null || controller == null) { sb.AppendLine($"{def.id}: falta prefab/controller"); fails++; continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    var animator = go.GetComponentInChildren<Animator>(true);
                    if (animator != null) animator.enabled = false;
                    var transforms = go.GetComponentsInChildren<Transform>(true);
                    var bind = transforms.ToDictionary(t => t, t => (p: t.localPosition, r: t.localRotation, s: t.localScale));
                    // El punto de contacto es el DEDO, no el tobillo: el tobillo se mueve
                    // durante el apoyo por la rotacion del pie y falseaba el gate.
                    var feet = new[]
                    {
                        transforms.FirstOrDefault(t => t.name == "DEF-toe.L") ?? transforms.FirstOrDefault(t => t.name == "DEF-foot.L"),
                        transforms.FirstOrDefault(t => t.name == "DEF-toe.R") ?? transforms.FirstOrDefault(t => t.name == "DEF-foot.R")
                    };
                    if (feet[0] == null || feet[1] == null) { sb.AppendLine($"{def.id}: faltan dedos/pies DEF"); fails++; continue; }

                    var sm = controller.layers[0].stateMachine;
                    var states = sm.states.Select(x => x.state).ToArray();
                    var walkFwd = states.FirstOrDefault(x => x.name == "WalkGuardFwd");
                    var walkBack = states.FirstOrDefault(x => x.name == "WalkGuardBack");
                    var runFwd = states.FirstOrDefault(x => x.name == "RunFwd");
                    var runBack = states.FirstOrDefault(x => x.name == "RunBack");
                    var walkClip = walkFwd != null ? walkFwd.motion as AnimationClip : null;
                    var runClip = runFwd != null ? runFwd.motion as AnimationClip : null;
                    // Velocidad CON SIGNO: hacia atras el Animator invierte el clip
                    // (timeScale negativo) y la formula de deslizamiento debe invertirse
                    // con el. Usar abs() daba un sesgo artificial de -1.1 m/s.
                    float walkFwdTs = walkFwd != null ? walkFwd.speed : 0f;
                    float walkBackTs = walkBack != null ? walkBack.speed : 0f;
                    float runFwdTs = runFwd != null ? runFwd.speed : 0f;
                    float runBackTs = runBack != null ? runBack.speed : 0f;
                    sb.AppendLine($"=== {def.id} · walkSpeed={def.walkSpeed:F2} dashSpeed={def.dashSpeed:F2} ===");
                    MeasureSlip(sb, go, transforms, bind, feet, "walk+", walkClip, walkFwdTs, def.walkSpeed, ref fails);
                    MeasureSlip(sb, go, transforms, bind, feet, "walk-", walkClip, walkBackTs, -def.walkSpeed, ref fails);
                    MeasureSlip(sb, go, transforms, bind, feet, "run+", runClip, runFwdTs, def.dashSpeed, ref fails);
                    MeasureSlip(sb, go, transforms, bind, feet, "run-", runClip, runBackTs, -def.dashSpeed, ref fails);
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
                sb.AppendLine();
            }
            sb.AppendLine($"VERDICT: {(fails == 0 ? "PASS" : "FAIL (" + fails + ")")}");
            File.WriteAllText("Logs/anim_footslide.txt", sb.ToString());
            Debug.Log($"[KOM][DIAG] Logs/anim_footslide.txt · {sb.ToString().Split('\n').Last(l => l.StartsWith("VERDICT"))}");
        }

        static void MeasureSlip(StringBuilder sb, GameObject go, Transform[] transforms,
            Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)> bind, Transform[] feet,
            string label, AnimationClip clip, float timeScale, float moveSpeed, ref int fails)
        {
            if (clip == null || Mathf.Abs(timeScale) < 0.001f) { sb.AppendLine($"  {label,-6} SIN CLIP/timeScale"); fails++; return; }
            foreach (var t in transforms)
            {
                if (t == null || !bind.TryGetValue(t, out var b)) continue;
                t.localPosition = b.p; t.localRotation = b.r; t.localScale = b.s;
            }
            int steps = Mathf.Clamp(Mathf.RoundToInt(clip.length * 60f), 2, 600);
            var z = new float[2][];
            var y = new float[2][];
            for (int f = 0; f < 2; f++) { z[f] = new float[steps + 1]; y[f] = new float[steps + 1]; }
            for (int i = 0; i <= steps; i++)
            {
                clip.SampleAnimation(go, Mathf.Min(clip.length * i / steps, clip.length - 0.00001f));
                for (int f = 0; f < 2; f++)
                {
                    var p = ChainPosition(feet[f]);
                    z[f][i] = p.z;
                    y[f][i] = p.y;
                }
            }
            double absSum = 0, signedSum = 0, contactSpeedSum = 0;
            float max = 0;
            int n = 0;
            float dtClip = clip.length / steps;
            float[] minYs = { y[0].Min(), y[1].Min() };
            for (int i = 0; i < steps; i++)
            {
                for (int f = 0; f < 2; f++)
                {
                    if (y[f][i] > minYs[f] + 0.03f || y[f][i + 1] > minYs[f] + 0.03f) continue;
                    float footSpeed = (z[f][i + 1] - z[f][i]) / dtClip;
                    float slip = footSpeed * timeScale + moveSpeed;
                    signedSum += slip;
                    absSum += Mathf.Abs(slip);
                    contactSpeedSum += -footSpeed;
                    max = Mathf.Max(max, Mathf.Abs(slip));
                    n++;
                }
            }
            float bias = n > 0 ? (float)(signedSum / n) : 0f;
            float variation = n > 0 ? (float)(absSum / n) : 0f;
            float contactSpeed = n > 0 ? (float)(contactSpeedSum / n) : 0f;
            // El sesgo es el deslizamiento SISTEMATICO (lo que hacia patinar al
            // personaje). La variacion es la rodada natural del pie de Mixamo y no
            // se puede quitar con un timeScale: se mide y se reporta.
            bool fail = n < 4 || Mathf.Abs(bias) > 0.30f || variation > 1.20f;
            if (fail) fails++;
            sb.AppendLine($"  {label,-6} clip={clip.name,-38} ts={timeScale:F3} move={moveSpeed:F2} muestras={n,3} vContacto={contactSpeed:F2} sesgo={bias:F3} variacion={variation:F3} max={max:F3} [{(fail ? "FAIL" : "ok")}]");
        }

        static void Describe(StringBuilder sb, string label, List<Vector3> pts)
        {
            if (pts == null || pts.Count == 0) return;
            float minX = pts.Min(p => p.x), maxX = pts.Max(p => p.x);
            float minZ = pts.Min(p => p.z), maxZ = pts.Max(p => p.z);
            float minY = pts.Min(p => p.y), maxY = pts.Max(p => p.y);
            sb.AppendLine($"  {label,-16} X[{minX:F3},{maxX:F3}] rango={maxX - minX:F3}  Z[{minZ:F3},{maxZ:F3}] rango={maxZ - minZ:F3}  Y[{minY:F3},{maxY:F3}]");
            sb.AppendLine($"    X: {string.Join(" ", pts.Select(p => p.x.ToString("F2")))}");
            sb.AppendLine($"    Z: {string.Join(" ", pts.Select(p => p.z.ToString("F2")))}");
        }

        static string Path(Transform t, Transform root)
        {
            if (t == null) return "-";
            return AnimationUtility.CalculateTransformPath(t, root);
        }

        // GATE DE INTEGRIDAD DE CURVAS (ver AGENTS.md 16-quater y 16-quinquies).
        //
        // Mide lo que el gate de longitud de hueso NO puede ver:
        //   A) rutas de ROTACION de un clip _Clean contra su toma del FBX. Si la fuente se
        //      importa en Euler (`animationCompression = Off`), el clip sale con ~88 rutas en
        //      vez de ~194 y los huesos de cuerpo heredan la pose anterior.
        //   B) curvas de POSICION que la fuente mueve y el clip horneado deja planas. Con el
        //      armature a escala 100, la tolerancia de simplificacion (0.0005) son 5 cm de
        //      mundo: el torso se queda clavado y la malla se estira.
        //   C) SALTO de la raiz del armature entre frames consecutivos: si la compensacion
        //      in-place cancela la trayectoria instantanea, la raiz recorre el vaiven de la
        //      cadera y el personaje tiembla.
        static int CurveIntegrity(string id, GameObject go, StringBuilder sb)
        {
            int fails = 0;
            var fbx = AllClips(id).ToArray();
            if (fbx.Length == 0) { sb.AppendLine("  CURVAS: sin clips en el FBX"); return 1; }

            // --- C) salto de la raiz, con el clip ya cargado
            var animator = go.GetComponentInChildren<Animator>(true);
            var pelvis = go.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "DEF-spine");
            Transform armature = pelvis;
            while (armature != null && armature.parent != null && armature.parent != go.transform)
                armature = armature.parent;
            if (armature != null && animator != null)
            {
                bool wasEnabled = animator.enabled;
                animator.enabled = false;
                var inPlace = AssetDatabase.FindAssets("t:AnimationClip",
                    new[] { "Assets/KOM/Animation/Clips/InPlace" });
                foreach (var guid in inPlace)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string file = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (!file.StartsWith(id + "_", StringComparison.Ordinal)) continue;
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (clip == null) continue;
                    int steps = Mathf.Clamp(Mathf.RoundToInt(clip.length * 60f), 2, 900);
                    Vector3 prev = Vector3.zero;
                    float worst = 0f;
                    for (int i = 0; i <= steps; i++)
                    {
                        clip.SampleAnimation(go, clip.length * i / steps);
                        var p = armature.localPosition;
                        if (i > 0)
                        {
                            float d = new Vector2(p.x - prev.x, p.z - prev.z).magnitude;
                            if (d > worst) worst = d;
                        }
                        prev = p;
                    }
                    bool bad = worst > RootJumpFail;
                    if (bad) fails++;
                    if (bad)
                        sb.AppendLine($"  RAIZ {file,-40} salto {worst * 1000f,6:F1} mm/frame  [FAIL]");
                }
                animator.enabled = wasEnabled;
            }

            // --- A y B) contra la toma del FBX, clip por clip
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip",
                new[] { "Assets/KOM/Animation/Clips/InPlace" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!file.StartsWith(id + "_", StringComparison.Ordinal)) continue;
                if (!file.EndsWith("_Clean", StringComparison.Ordinal)) continue;
                string state = file.Substring(id.Length + 1, file.Length - id.Length - 1 - "_Clean".Length);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                var source = fbx.FirstOrDefault(c => c.name == state);
                if (clip == null || source == null) continue;

                // A) rutas de rotacion DEL CUERPO. Las faciales se excluyen a proposito: la
                // copia _Clean congela el subrig facial (regla 15), asi que la fuente tiene
                // ~173 rutas faciales que el clip no debe tener. Lo que importa es que el
                // cuerpo conserve TODAS las suyas (~106).
                var srcRot = new HashSet<string>();
                foreach (var b in AnimationUtility.GetCurveBindings(source))
                {
                    if (!b.propertyName.StartsWith("m_LocalRotation", StringComparison.Ordinal)) continue;
                    if (KomSetup.IsFacialBonePath(b.path)) continue;
                    srcRot.Add(b.path);
                }
                var dstRot = new HashSet<string>();
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!b.propertyName.StartsWith("m_LocalRotation", StringComparison.Ordinal)) continue;
                    if (KomSetup.IsFacialBonePath(b.path)) continue;
                    dstRot.Add(b.path);
                }

                if (srcRot.Count > 0 && dstRot.Count < srcRot.Count * 0.95f)
                {
                    fails++;
                    sb.AppendLine($"  ROTACION {file,-38} cuerpo {dstRot.Count}/{srcRot.Count} rutas  [FAIL] " +
                                  "(fuente en Euler: animationCompression debe ser KeyframeReduction)");
                }

                // B) posiciones aplanadas
                int flattened = 0;
                string worstPos = "-";
                foreach (var b in AnimationUtility.GetCurveBindings(source))
                {
                    if (!b.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal)) continue;
                    var srcCurve = AnimationUtility.GetEditorCurve(source, b);
                    if (srcCurve == null || srcCurve.length < 2) continue;
                    float smn = srcCurve.keys[0].value, smx = smn;
                    foreach (var k in srcCurve.keys) { if (k.value < smn) smn = k.value; if (k.value > smx) smx = k.value; }
                    if (smx - smn <= 1e-5f) continue;      // la fuente tampoco la mueve
                    var dstCurve = AnimationUtility.GetEditorCurve(clip, b);
                    if (dstCurve == null) continue;
                    float dmn = dstCurve.keys[0].value, dmx = dmn;
                    foreach (var k in dstCurve.keys) { if (k.value < dmn) dmn = k.value; if (k.value > dmx) dmx = k.value; }
                    // Si la fuente recorre S y el clip recorre menos del 50 %, se aplano.
                    if ((dmx - dmn) < (smx - smn) * 0.5f)
                    {
                        flattened++;
                        if (worstPos == "-") worstPos = b.path;
                    }
                }
                if (flattened > 0)
                {
                    fails++;
                    sb.AppendLine($"  POSICION {file,-38} {flattened} curvas aplanadas  [FAIL]  ({worstPos})");
                }
            }
            return fails;
        }

        static AnimationClip FindClip(string id, string name)
        {
            // La victoria no tiene clip propio: cada personaje celebra con una toma distinta
            // (SpecialWide / Boxing / MagicHeal). Sin este mapeo el festejo no se medía.
            if (name == "Victory") name = KomSetup.VictoryClipName(KomData.GetCharacter(id));
            foreach (var suffix in new[] { "_RawWalk", "_FirstRaw", "_Clean" })
            {
                var inPlace = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    $"Assets/KOM/Animation/Clips/InPlace/{id}_{name}{suffix}.anim");
                if (inPlace != null) return inPlace;
            }
            var clean = AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/KOM/Animation/Clips/{id}_{name}.anim");
            if (clean != null) return clean;
            return AssetDatabase.LoadAllAssetsAtPath($"Assets/Art/Characters/{id}.fbx")
                .OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
        }
    }
}
