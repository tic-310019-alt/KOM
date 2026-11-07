using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KOM.EditorTools
{
    // PERITAJE DE UN ESTADO DE ANIMACION. No hornea nada y no escribe ningun asset.
    //
    // Nace de un caso real: se dieron por buenas dos causas equivocadas de una deformacion
    // porque nadie la midio. Cada medida responde a UNA pregunta y dice en que hueso falla:
    //
    //  A) FIDELIDAD DEL HORNEADO (Run): posicion de cada hueso RELATIVA A LA PELVIS en la
    //     toma del FBX y en el clip horneado, a la misma fase. Relativa para que la
    //     compensacion in-place (que traslada el armature entero) no contamine la medida.
    //     Si difieren, el horneado cambio la pose.
    //
    //  B) HERENCIA DE POSE FACIAL (RunLeak): rigidez del subrig facial respecto de la cabeza
    //     al entrar en un estado desde un ataque RAW. Es la hipotesis documentada, medida
    //     donde de verdad ocurre (en el juego un combo nunca se muestra en aislamiento).
    //
    //  C) CUATERNIONES NO UNITARIAS (RunNorm): |q| muestreado a 60 fps. La simplificacion
    //     deja keys separadas; si |q| se va de 1 entre keys, Unity escala la matriz y la
    //     malla se deforma. Solo se ve ENTRE keys.
    //
    //  D) IMAGENES (RunShots): el estado por el ANIMATOR REAL, con el mismo Play/CrossFade
    //     que usa Fighter. Es la unica ruta que reproduce lo que ve el jugador.
    //
    // NO se mide "longitud de arista contra bind": el skinning estira a proposito en las
    // articulaciones, asi que esa metrica marca FAIL en todos los clips, incluidos los
    // buenos, y no discrimina nada. Se quito para que no vuelva a inducir a error.
    public static class KomComboDiag
    {
        const string PrefabDir = "Assets/Resources/Characters";
        const string InPlaceDir = "Assets/KOM/Animation/Clips/InPlace";
        const string ReportPath = "Logs/combo_diag.txt";

        static readonly string[] States = { "Combo1", "Combo2", "Combo3", "QuadPunch", "LightPunch" };

        // El horneado aplica SimplifyCurve(0.0005) a TODOS los clips _Clean, y eso ya deja
        // 5-8 cm de error en la mano (medido tambien en el control bueno). Un umbral de 1 cm
        // marcaba FAIL hasta QuadPunch y no discriminaba nada. 10 cm si es una divergencia.
        const float PoseTol = 0.10f;
        // 1 % de estiramiento de arista se ve; 0.2 % es ruido numerico de BakeMesh.
        const float EdgeTol = 0.01f;

        [MenuItem("King of Music/Diagnostico/21 - Peritaje de combos: pose", priority = 231)]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("KOM PERITAJE · fidelidad del clip horneado respecto de la toma del FBX");
            sb.AppendLine("Unity " + Application.unityVersion + " · " + DateTime.Now.ToString("u"));
            sb.AppendLine("Medida RELATIVA A LA PELVIS (la compensacion in-place traslada el armature entero y no cuenta).");
            sb.AppendLine($"FAIL si un hueso con peso se desvia > {PoseTol * 100:F0} cm del RAW a la misma fase.");
            sb.AppendLine("Referencia: LightPunch (_FirstRaw, copia exacta) debe dar ~0.000 m; QuadPunch ~0.055 m.");
            sb.AppendLine();

            int fails = 0;
            foreach (var def in KomData.Characters.characters.Where(c => c != null))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{def.id}.prefab");
                if (prefab == null) continue;

                var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                GameObject go = null;
                try
                {
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    var animator = go.GetComponentInChildren<Animator>(true);
                    var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if (smr == null) continue;
                    if (animator != null) animator.enabled = false;

                    var bindTrs = new Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)>();
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        bindTrs[t] = (t.localPosition, t.localRotation, t.localScale);

                    var weighted = WeightedBones(smr);
                    var bones = weighted.Where(b => b != null).ToArray();
                    var pelvis = bones.FirstOrDefault(b => b.name == "DEF-spine")
                                 ?? bones.FirstOrDefault(b => b.name.StartsWith("DEF-pelvis", StringComparison.Ordinal))
                                 ?? bones.FirstOrDefault();

                    sb.AppendLine($"=== {def.id} · huesos con peso={bones.Length} pelvis={pelvis?.name} ===");
                    sb.AppendLine("  estado        maxDesvio(pelvis)   peor hueso          maxRot   hueso rot");

                    foreach (var state in States)
                    {
                        var raw = FindRaw(def.id, state);
                        var baked = FindBaked(def.id, state);
                        if (raw == null || baked == null) { sb.AppendLine($"  {state,-12} (falta {(raw == null ? "RAW" : "HORNEADO")})"); continue; }

                        int steps = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(raw.length, baked.length) * 60f), 2, 600);
                        float worst = 0f, worstRot = 0f;
                        string worstBone = "-", worstRotBone = "-";

                        for (int s = 0; s <= steps; s++)
                        {
                            float u = (float)s / steps;

                            ResetBind(bindTrs);
                            raw.SampleAnimation(go, u * raw.length);
                            var rawPos = Relative(bones, pelvis);
                            var rawRot = bones.Select(b => b.localRotation).ToArray();

                            ResetBind(bindTrs);
                            baked.SampleAnimation(go, u * baked.length);
                            var bkPos = Relative(bones, pelvis);
                            var bkRot = bones.Select(b => b.localRotation).ToArray();

                            for (int i = 0; i < bones.Length; i++)
                            {
                                float d = Vector3.Distance(rawPos[i], bkPos[i]);
                                if (d > worst) { worst = d; worstBone = bones[i].name; }
                                float a = Quaternion.Angle(rawRot[i], bkRot[i]);
                                if (a > worstRot) { worstRot = a; worstRotBone = bones[i].name; }
                            }
                        }

                        bool fail = worst > PoseTol;
                        if (fail) fails++;
                        sb.AppendLine($"  {state,-12} {worst,10:F3} m       {worstBone,-20} {worstRot,6:F1}°  {worstRotBone,-18} [{(fail ? "FAIL" : "ok")}]");
                    }
                    sb.AppendLine();
                }
                catch (Exception e) { sb.AppendLine($"{def.id}: ERROR {e}"); fails++; }
                finally
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            sb.AppendLine($"VERDICT: {(fails == 0 ? "PASS" : "FAIL (" + fails + " estados)")}");
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, sb.ToString());
            Debug.Log($"[KOM][COMBO-DIAG] {ReportPath}");
        }

        // Medida D: REPRODUCCION VISUAL del sintoma. Renderiza el mismo estado dos veces:
        // (a) partiendo del bind y (b) partiendo de la pose que deja el estado anterior.
        // Con writeDefaultValues=false lo que el clip no anima se HEREDA; si las dos
        // imagenes no coinciden, el estado no es autoridad unica de la pose y la malla se
        // estira. Es la prueba que se puede mirar sin creerse ningun informe.
        [MenuItem("King of Music/Diagnostico/23 - Peritaje de combos: imagenes", priority = 233)]
        public static void RunShots()
        {
            const string outDir = "Logs/combo_shots";
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);

            string id = "alex_llora";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{id}.prefab");
            if (prefab == null) { Debug.LogError("[KOM][COMBO-SHOT] falta el prefab"); return; }

            // Escena REAL en modo Single: la camara de una PreviewScene no renderiza desde
            // el contexto de eval (todas las capturas salian negras). Se restaura la escena
            // que tuviera abierta el usuario al terminar.
            string previousScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            GameObject go = null;
            Camera cam = null;
            RenderTexture rt = null;
            try
            {
                BuildCamera(scene, out cam, out rt);
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                var fighter = go.GetComponent<Fighter>();
                if (fighter != null) fighter.enabled = false;
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator == null) { Debug.LogError("[KOM][COMBO-SHOT] sin Animator"); return; }
                // ANIMATOR REAL: es la unica ruta que no se habia probado. El juego entra en
                // el combo por CrossFadeInFixedTime desde un ataque, no por SampleAnimation.
                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.SetLayerWeight(0, 1f);
                if (animator.layerCount > 1) animator.SetLayerWeight(1, 0f);
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.forceMatrixRecalculationPerRender = true;
                    smr.updateWhenOffscreen = true;
                }

                var log = new StringBuilder();
                log.AppendLine("escenario|estado");

                // 1) Estado aislado, como lo deja el juego al arrancar un ataque.
                Play(animator, "LightPunch", 0.90f);
                Fade(animator, "Combo1", 0.10f, 0.45f);
                Save(cam, rt, $"{outDir}/{id}_Anim_Combo1_tras_LightPunch.png");
                log.AppendLine($"animator|Combo1 tras LightPunch");

                Play(animator, "LightPunch", 0.90f);
                Fade(animator, "Combo2", 0.10f, 0.45f);
                Save(cam, rt, $"{outDir}/{id}_Anim_Combo2_tras_LightPunch.png");
                log.AppendLine($"animator|Combo2 tras LightPunch");

                Play(animator, "LightPunch", 0.90f);
                Fade(animator, "Combo3", 0.10f, 0.45f);
                Save(cam, rt, $"{outDir}/{id}_Anim_Combo3_tras_LightPunch.png");
                log.AppendLine($"animator|Combo3 tras LightPunch");

                // Control: un estado que el usuario NO reporta deforme.
                Play(animator, "Idle", 0.50f);
                Fade(animator, "QuadPunch", 0.10f, 0.45f);
                Save(cam, rt, $"{outDir}/{id}_Anim_QuadPunch_tras_Idle.png");
                log.AppendLine($"animator|QuadPunch tras Idle");

                File.WriteAllText($"{outDir}/index.txt", log.ToString());
                Debug.Log($"[KOM][COMBO-SHOT] {outDir}");
            }
            finally
            {
                if (rt != null) { cam.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                if (!string.IsNullOrEmpty(previousScene))
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(previousScene);
            }
        }

        static void Play(Animator animator, string state, float seconds)
        {
            animator.Play(Animator.StringToHash(state), 0, 0f);
            Step(animator, seconds);
        }

        static void Fade(Animator animator, string state, float fade, float seconds)
        {
            animator.CrossFadeInFixedTime(Animator.StringToHash(state), fade, 0, 0f);
            Step(animator, seconds);
        }

        static void Step(Animator animator, float seconds)
        {
            const float dt = 1f / 60f;
            int steps = Mathf.Max(1, Mathf.RoundToInt(seconds / dt));
            for (int i = 0; i < steps; i++) animator.Update(dt);
        }

        static void BuildCamera(UnityEngine.SceneManagement.Scene scene, out Camera cam, out RenderTexture rt)
        {
            var camGo = new GameObject("SideCamera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
            cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.10f);
            cam.orthographic = true;
            cam.orthographicSize = 1.55f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 40f;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            camGo.transform.position = new Vector3(0.50f, 1.30f, -6f);
            camGo.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            camGo.tag = "MainCamera";

            rt = new RenderTexture(512, 640, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            cam.targetTexture = rt;

            var keyGo = new GameObject("Key", typeof(Light));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(keyGo, scene);
            var key = keyGo.GetComponent<Light>();
            key.type = LightType.Directional; key.intensity = 1.7f;
            key.color = new Color(1f, 0.95f, 0.88f);
            keyGo.transform.rotation = Quaternion.Euler(38f, -35f, 0f);

            var fillGo = new GameObject("Fill", typeof(Light));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fillGo, scene);
            var fill = fillGo.GetComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = 0.7f;
            fill.color = new Color(0.55f, 0.7f, 1f);
            fillGo.transform.rotation = Quaternion.Euler(12f, 140f, 0f);

            var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMat.SetColor("_BaseColor", new Color(0.13f, 0.14f, 0.17f));
            floorMat.SetFloat("_Smoothness", 0.15f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(3f, 1f, 1.2f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
        }

        static void Save(Camera cam, RenderTexture rt, string path)
        {
            // Doble render: el primero actualiza matrices de mundo y skinning, el segundo
            // captura el estado definitivo. Con uno solo la imagen sale vacia.
            cam.Render();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        // Medida E: HERENCIA DE POSE (la hipotesis documentada, medida donde de verdad ocurre).
        //
        // En el juego un combo NO se muestra en aislamiento: entra por crossfade desde un
        // ataque, y esos ataques son copias RAW que SI animan el subrig facial completo. Si
        // el clip del combo no escribe esos huesos, con writeDefaultValues=false se quedan
        // en la pose que dejo el ataque anterior y la cara/pelo se deforman.
        //
        // Se mide la RIGIDEZ del craneo: distancia de cada hueso facial a DEF-spine.006
        // (cabeza), comparada con la del bind. Un subrig facial sano es rigido (mm); uno
        // contaminado por otra pose cambia de forma (cm).
        [MenuItem("King of Music/Diagnostico/24 - Peritaje de combos: herencia de pose", priority = 234)]
        public static void RunLeak()
        {
            var sb = new StringBuilder();
            sb.AppendLine("KOM PERITAJE · herencia de pose facial al entrar en un estado CLEAN");
            sb.AppendLine("Unity " + Application.unityVersion + " · " + DateTime.Now.ToString("u"));
            sb.AppendLine("Se mide la RIGIDEZ del subrig facial respecto de DEF-spine.006 vs el bind.");
            sb.AppendLine("FAIL si un hueso facial se aleja > 5 mm de su posicion relativa en bind.");
            sb.AppendLine();

            const float tol = 0.005f;
            int fails = 0;
            foreach (var def in KomData.Characters.characters.Where(c => c != null))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{def.id}.prefab");
                if (prefab == null) continue;
                var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                GameObject go = null;
                try
                {
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    var animator = go.GetComponentInChildren<Animator>(true);
                    var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if (smr == null) continue;
                    if (animator != null) animator.enabled = false;

                    var bindTrs = new Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)>();
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        bindTrs[t] = (t.localPosition, t.localRotation, t.localScale);

                    var all = go.GetComponentsInChildren<Transform>(true);
                    var head = all.FirstOrDefault(t => t.name == "DEF-spine.006") ?? all.FirstOrDefault(t => t.name == "DEF-spine.005");
                    if (head == null) { sb.AppendLine($"{def.id}: sin hueso de cabeza"); continue; }
                    // Subrig facial = todo lo que cuelga de ORG-face.
                    var faceRoot = all.FirstOrDefault(t => t.name == "ORG-face");
                    var facial = new List<Transform>();
                    if (faceRoot != null)
                        foreach (var t in faceRoot.GetComponentsInChildren<Transform>(true))
                            if (t != faceRoot) facial.Add(t);
                    if (facial.Count == 0) { sb.AppendLine($"{def.id}: sin subrig facial"); continue; }

                    // Referencia en el ESPACIO LOCAL DE LA CABEZA, no solo restando posiciones:
                    // si la cabeza gira, restar posiciones mueve todos los huesos faciales y
                    // daba 77 mm de "deformacion" en clips perfectos. Hay que deshacer la
                    // rotacion de la cabeza para medir la FORMA del subrig facial.
                    ResetBind(bindTrs);
                    var refOff = facial.Select(t => HeadLocal(head, ChainPosition(t))).ToArray();

                    sb.AppendLine($"=== {def.id} · huesos faciales={facial.Count} cabeza={head.name} ===");
                    sb.AppendLine("  escenario                                  maxDesvio   peor hueso");

                    var cases = new (string label, string prev, float prevPhase, string state, float phase)[]
                    {
                        ("Combo1 SOLO (desde bind)",            null,        0f,    "Combo1", 0.50f),
                        ("LightPunch RAW -> Combo1",            "LightPunch",0.90f, "Combo1", 0.50f),
                        ("HeavyKick RAW -> Combo1",             "HeavyKick", 0.60f, "Combo1", 0.50f),
                        ("Idle RAW -> Combo2",                  "Idle",      0.50f, "Combo2", 0.50f),
                        ("LightPunch RAW -> Combo3",            "LightPunch",0.90f, "Combo3", 0.50f),
                        ("LightPunch RAW -> QuadPunch",         "LightPunch",0.90f, "QuadPunch",0.50f),
                        ("LightPunch RAW -> Crouch",            "LightPunch",0.90f, "Crouch", 0.50f),
                        ("LightPunch RAW -> Idle (FirstRaw)",   "LightPunch",0.90f, "Idle",   0.50f),
                    };

                    foreach (var c in cases)
                    {
                        ResetBind(bindTrs);
                        if (!string.IsNullOrEmpty(c.prev))
                        {
                            var pv = FindBaked(def.id, c.prev);
                            if (pv != null) pv.SampleAnimation(go, c.prevPhase * pv.length);
                        }
                        var clip = FindBaked(def.id, c.state);
                        if (clip == null) continue;
                        clip.SampleAnimation(go, c.phase * clip.length);

                        float worst = 0f; string worstBone = "-";
                        for (int i = 0; i < facial.Count; i++)
                        {
                            float d = Vector3.Distance(HeadLocal(head, ChainPosition(facial[i])), refOff[i]);
                            if (d > worst) { worst = d; worstBone = facial[i].name; }
                        }
                        bool fail = worst > tol;
                        if (fail) fails++;
                        sb.AppendLine($"  {c.label,-42} {worst * 1000,7:F1} mm   {worstBone,-20} [{(fail ? "FAIL" : "ok")}]");
                    }
                    sb.AppendLine();
                }
                catch (Exception e) { sb.AppendLine($"{def.id}: ERROR {e.Message}"); fails++; }
                finally
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            sb.AppendLine($"VERDICT: {(fails == 0 ? "PASS" : "FAIL (" + fails + " casos)")}");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/combo_leak.txt", sb.ToString());
            Debug.Log("[KOM][COMBO-LEAK] Logs/combo_leak.txt");
        }

        // Medida F: CUATERNIONES NO UNITARIAS ENTRE KEYS.
        // La simplificacion deja keys separadas y tangentes LINEALES por componente. Al
        // interpolar los 4 componentes por separado el cuaternion deja de ser unitario
        // (|q| < 1) y Unity construye la matriz de rotacion con ese |q|: el hueso se ESCALA
        // y la malla se deforma. Solo se ve ENTRE keys, asi que hay que muestrear denso.
        [MenuItem("King of Music/Diagnostico/25 - Peritaje de combos: norma de quaternion", priority = 235)]
        public static void RunNorm()
        {
            var sb = new StringBuilder();
            sb.AppendLine("KOM PERITAJE · |q| de los huesos muestreado a 60 fps (deformacion entre keys)");
            sb.AppendLine("Unity " + Application.unityVersion + " · " + DateTime.Now.ToString("u"));
            sb.AppendLine("FAIL si |q|-1 > 1 %: la matriz de rotacion se escala y la malla se deforma.");
            sb.AppendLine();

            var states = new[] { "Combo1", "Combo2", "Combo3", "QuadPunch", "Crouch", "Idle", "LightPunch", "HeavyKick" };
            int fails = 0;
            foreach (var def in KomData.Characters.characters.Where(c => c != null))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{def.id}.prefab");
                if (prefab == null) continue;
                var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                GameObject go = null;
                try
                {
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    var animator = go.GetComponentInChildren<Animator>(true);
                    var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if (smr == null) continue;
                    if (animator != null) animator.enabled = false;
                    var bones = smr.bones.Where(b => b != null).Distinct().ToArray();

                    var bindTrs = new Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)>();
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        bindTrs[t] = (t.localPosition, t.localRotation, t.localScale);

                    sb.AppendLine($"=== {def.id} · huesos={bones.Length} ===");
                    sb.AppendLine("  estado        peor |q|-1   hueso");

                    foreach (var state in states)
                    {
                        var clip = FindBaked(def.id, state);
                        if (clip == null) continue;
                        int steps = Mathf.Clamp(Mathf.RoundToInt(clip.length * 60f), 2, 600);
                        float worstNorm = 0f;
                        string normBone = "-";

                        for (int s = 0; s <= steps; s++)
                        {
                            ResetBind(bindTrs);
                            clip.SampleAnimation(go, clip.length * s / steps);
                            for (int i = 0; i < bones.Length; i++)
                            {
                                var q = bones[i].localRotation;
                                float d = Mathf.Abs(Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w) - 1f);
                                if (d > worstNorm) { worstNorm = d; normBone = bones[i].name; }
                            }
                        }
                        bool fail = worstNorm > 0.01f;
                        if (fail) fails++;
                        sb.AppendLine($"  {state,-12} {worstNorm,10:P3}   {normBone,-24} [{(fail ? "FAIL" : "ok")}]");
                    }
                    sb.AppendLine();
                }
                catch (Exception e) { sb.AppendLine($"{def.id}: ERROR {e.Message}"); fails++; }
                finally
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            sb.AppendLine($"VERDICT: {(fails == 0 ? "PASS" : "FAIL (" + fails + " estados)")}");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/combo_norm.txt", sb.ToString());
            Debug.Log("[KOM][COMBO-NORM] Logs/combo_norm.txt");
        }

        // Medida G: FORMA DE LA POSE contra Blender.
        // Vuelca la distancia de la cadera a una lista fija de huesos. Esas distancias son
        // invariantes a rotacion, traslacion y espejo, asi que se comparan tal cual con
        // tools/blender/dump_action_shape.py. Medir RAW y HORNEADO por separado dice si el
        // que rompe es la IMPORTACION del FBX o el horneado.
        [MenuItem("King of Music/Diagnostico/26 - Peritaje de combos: forma vs Blender", priority = 236)]
        public static void RunShape()
        {
            var bones = new[]
            {
                "DEF-spine", "DEF-spine.003", "DEF-spine.006",
                "DEF-shoulder.L", "DEF-shoulder.R", "DEF-upper_arm.L", "DEF-upper_arm.R",
                "DEF-forearm.L", "DEF-forearm.R", "DEF-hand.L", "DEF-hand.R",
                "DEF-thigh.L", "DEF-thigh.R", "DEF-shin.L", "DEF-shin.R",
                "DEF-foot.L", "DEF-foot.R", "DEF-toe.L", "DEF-toe.R",
            };
            var phases = new[] { 0.10f, 0.30f, 0.50f, 0.70f, 0.90f };
            var sb = new StringBuilder();

            foreach (var def in KomData.Characters.characters.Where(c => c != null))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{def.id}.prefab");
                if (prefab == null) continue;
                var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                GameObject go = null;
                try
                {
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    var animator = go.GetComponentInChildren<Animator>(true);
                    if (animator != null) animator.enabled = false;
                    var all = go.GetComponentsInChildren<Transform>(true)
                        .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                    if (!all.ContainsKey("DEF-spine")) continue;
                    var hip = all["DEF-spine"];
                    var list = bones.Where(b => all.ContainsKey(b)).ToArray();

                    var bindTrs = new Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)>();
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        bindTrs[t] = (t.localPosition, t.localRotation, t.localScale);

                    foreach (var state in new[] { "Combo1", "Combo2", "Combo3", "QuadPunch" })
                    {
                        var raw = FindRaw(def.id, state);
                        var baked = FindBaked(def.id, state);
                        if (raw == null || baked == null) continue;

                        foreach (var kind in new[] { "RAW", "BAKED" })
                        {
                            var clip = kind == "RAW" ? raw : baked;
                            sb.AppendLine($"### {def.id}|{state}|{kind}");
                            var pairs = new List<(string a, string b)>();
                            for (int i = 0; i < list.Length; i++)
                                for (int j = i + 1; j < list.Length; j++)
                                    pairs.Add((list[i], list[j]));
                            sb.AppendLine("phase\t" + string.Join("\t", pairs.Select(x => x.a + "|" + x.b)));
                            foreach (var p in phases)
                            {
                                ResetBind(bindTrs);
                                clip.SampleAnimation(go, p * clip.length);
                                var pts = list.ToDictionary(n => n, n => ChainPosition(all[n]));
                                sb.AppendLine($"{p:F2}\t" + string.Join("\t",
                                    pairs.Select(x => Vector3.Distance(pts[x.a], pts[x.b]).ToString("F4"))));
                            }
                        }
                        sb.AppendLine();
                    }
                }
                catch (Exception e) { sb.AppendLine($"{def.id}: ERROR {e.Message}"); }
                finally
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/shape_unity.txt", sb.ToString());
            Debug.Log("[KOM][COMBO-SHAPE] Logs/shape_unity.txt");
        }

        // Medida H: QUE HUESO cambia entre RAW y HORNEADO.
        // Vuelca, para cada hueso, la diferencia de posicion y rotacion LOCAL entre la toma
        // del FBX y el clip horneado en la misma fase, ordenada por magnitud. Nombra el
        // culpable en vez de discutir teorias.
        [MenuItem("King of Music/Diagnostico/27 - Peritaje de combos: hueso culpable", priority = 237)]
        public static void RunDiff()
        {
            var sb = new StringBuilder();
            sb.AppendLine("KOM PERITAJE · diferencia LOCAL RAW vs HORNEADO (por hueso)");
            sb.AppendLine("Unity " + Application.unityVersion + " · " + DateTime.Now.ToString("u"));
            sb.AppendLine();

            foreach (var def in KomData.Characters.characters.Where(c => c != null))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{def.id}.prefab");
                if (prefab == null) continue;
                var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                GameObject go = null;
                try
                {
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    var animator = go.GetComponentInChildren<Animator>(true);
                    if (animator != null) animator.enabled = false;
                    var trs = go.GetComponentsInChildren<Transform>(true);
                    var bindTrs = new Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)>();
                    foreach (var t in trs) bindTrs[t] = (t.localPosition, t.localRotation, t.localScale);

                    foreach (var state in new[] { "Combo1", "Combo2", "Combo3" })
                    {
                        var raw = FindRaw(def.id, state);
                        var baked = FindBaked(def.id, state);
                        if (raw == null || baked == null) continue;

                        const float phase = 0.50f;
                        ResetBind(bindTrs);
                        raw.SampleAnimation(go, phase * raw.length);
                        var rawPos = trs.Select(t => t.localPosition).ToArray();
                        var rawRot = trs.Select(t => t.localRotation).ToArray();

                        ResetBind(bindTrs);
                        baked.SampleAnimation(go, phase * baked.length);
                        var bkPos = trs.Select(t => t.localPosition).ToArray();
                        var bkRot = trs.Select(t => t.localRotation).ToArray();

                        var rows = new List<(float dpos, float drot, string path)>();
                        for (int i = 0; i < trs.Length; i++)
                        {
                            float dp = Vector3.Distance(rawPos[i], bkPos[i]);
                            float dr = Quaternion.Angle(rawRot[i], bkRot[i]);
                            if (dp > 0.0005f || dr > 0.5f)
                                rows.Add((dp, dr, AnimationUtility.CalculateTransformPath(trs[i], go.transform)));
                        }
                        rows.Sort((a, b) => (b.dpos * 1000f + b.drot).CompareTo(a.dpos * 1000f + a.drot));

                        sb.AppendLine($"=== {def.id} | {state} | fase {phase:F2} | huesos distintos={rows.Count} ===");
                        foreach (var r in rows.Take(18))
                            sb.AppendLine($"   dPos={r.dpos * 1000,8:F1} mm   dRot={r.drot,6:F1}°   {r.path}");
                        sb.AppendLine();
                    }
                }
                catch (Exception e) { sb.AppendLine($"{def.id}: ERROR {e.Message}"); }
                finally
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/combo_diff.txt", sb.ToString());
            Debug.Log("[KOM][COMBO-DIFF] Logs/combo_diff.txt");
        }

        // Medida I: PRIMER PLANO de cuello/hombros por estado. La deformacion del cuello y la
        // union cabeza-torso no se ve en un plano de cuerpo entero: hay que acercarse.
        [MenuItem("King of Music/Diagnostico/28 - Peritaje de combos: primer plano", priority = 238)]
        public static void RunCloseup()
        {
            const string outDir = "Logs/combo_closeup";
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);

            string id = "alex_llora";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{id}.prefab");
            if (prefab == null) return;

            string previousScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            GameObject go = null;
            Camera cam = null;
            RenderTexture rt = null;
            try
            {
                BuildCamera(scene, out cam, out rt);
                // Encuadre 3/4 de cuello y hombros, el mismo angulo que el render de Blender
                // (--view tq), para poder superponerlos y comparar de verdad.
                cam.orthographicSize = 0.62f;
                var target = new Vector3(0f, 1.42f, 0f);
                var eye = new Vector3(2.6f, 1.42f, -2.6f);
                cam.transform.position = eye;
                cam.transform.rotation = Quaternion.LookRotation((target - eye).normalized);
                cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(rt);
                rt = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                rt.Create();
                cam.targetTexture = rt;

                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                var fighter = go.GetComponent<Fighter>();
                if (fighter != null) fighter.enabled = false;
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.forceMatrixRecalculationPerRender = true;
                    smr.updateWhenOffscreen = true;
                }
                var bindTrs = new Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)>();
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    bindTrs[t] = (t.localPosition, t.localRotation, t.localScale);

                foreach (var state in new[] { "Idle", "Combo1", "Combo2", "Combo3", "QuadPunch" })
                {
                    var clip = FindBaked(id, state);
                    if (clip == null) continue;
                    ResetBind(bindTrs);
                    clip.SampleAnimation(go, 0.5f * clip.length);
                    Save(cam, rt, $"{outDir}/{id}_{state}.png");
                }
                Debug.Log($"[KOM][COMBO-CLOSEUP] {outDir}");
            }
            finally
            {
                if (rt != null) { cam.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                if (!string.IsNullOrEmpty(previousScene))
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(previousScene);
            }
        }

        // Medida J: BARRIDO de todas las fases de los 3 combos, cuerpo entero, 3/4.
        [MenuItem("King of Music/Diagnostico/29 - Peritaje de combos: barrido de fases", priority = 239)]
        public static void RunSweep()
        {
            const string outDir = "Logs/combo_sweep";
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);

            string id = "alex_llora";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{id}.prefab");
            if (prefab == null) return;
            string previousScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            GameObject go = null;
            Camera cam = null;
            RenderTexture rt = null;
            try
            {
                BuildCamera(scene, out cam, out rt);
                var target = new Vector3(0f, 1.00f, 0f);
                var eye = new Vector3(2.9f, 1.55f, -2.9f);
                cam.orthographicSize = 1.15f;
                cam.transform.position = eye;
                cam.transform.rotation = Quaternion.LookRotation((target - eye).normalized);

                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                var fighter = go.GetComponent<Fighter>();
                if (fighter != null) fighter.enabled = false;
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.forceMatrixRecalculationPerRender = true;
                    smr.updateWhenOffscreen = true;
                }
                var bindTrs = new Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)>();
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    bindTrs[t] = (t.localPosition, t.localRotation, t.localScale);

                foreach (var state in new[] { "Combo1", "Combo2", "Combo3" })
                {
                    var clip = FindBaked(id, state);
                    if (clip == null) continue;
                    for (int i = 0; i < 8; i++)
                    {
                        float ph = (i + 0.5f) / 8f;
                        ResetBind(bindTrs);
                        clip.SampleAnimation(go, ph * clip.length);
                        Save(cam, rt, $"{outDir}/{id}_{state}_p{i}.png");
                    }
                }
                Debug.Log($"[KOM][COMBO-SWEEP] {outDir}");
            }
            finally
            {
                if (rt != null) { cam.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                if (!string.IsNullOrEmpty(previousScene))
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(previousScene);
            }
        }

        // Medida K: ESCALA del clip RAW contra la escala del PREFAB.
        // El inspector del FBX reproduce el clip CRUDO (que SI escribe escala). El juego
        // reproduce la copia `_Clean`, que NO escribe escala: los huesos se quedan con la
        // escala del prefab. Si esos dos valores no coinciden, el inspector se ve bien y el
        // juego se ve deformado, en TODAS las animaciones y en TODOS los personajes.
        [MenuItem("King of Music/Diagnostico/30 - Peritaje: escala clip vs prefab", priority = 240)]
        public static void RunScaleCompare()
        {
            var sb = new StringBuilder();
            sb.AppendLine("KOM PERITAJE · escala del clip CRUDO vs escala del PREFAB");
            sb.AppendLine("Unity " + Application.unityVersion + " · " + DateTime.Now.ToString("u"));
            sb.AppendLine("FAIL si alguna ruta difiere > 0.001: la copia _Clean no escribe escala.");
            sb.AppendLine();

            int fails = 0;
            foreach (var def in KomData.Characters.characters.Where(c => c != null))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{def.id}.prefab");
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Art/Characters/{def.id}.fbx");
                if (prefab == null || model == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    var byPath = new Dictionary<string, Transform>();
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        byPath[AnimationUtility.CalculateTransformPath(t, go.transform)] = t;

                    var rawClips = AssetDatabase.LoadAllAssetsAtPath($"Assets/Art/Characters/{def.id}.fbx")
                        .OfType<AnimationClip>()
                        .Where(c => c != null && !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                        .ToArray();

                    sb.AppendLine($"=== {def.id} · clips crudos={rawClips.Length} ===");
                    float globalWorst = 0f;
                    string globalBone = "-";
                    foreach (var clip in rawClips)
                    {
                        var scaleByPath = new Dictionary<string, Vector3>();
                        foreach (var b in AnimationUtility.GetCurveBindings(clip))
                        {
                            if (!b.propertyName.StartsWith("m_LocalScale", StringComparison.Ordinal)) continue;
                            var c = AnimationUtility.GetEditorCurve(clip, b);
                            if (c == null || c.length == 0) continue;
                            if (!scaleByPath.TryGetValue(b.path, out var v)) v = Vector3.one;
                            if (b.propertyName.EndsWith(".x", StringComparison.Ordinal)) v.x = c.keys[0].value;
                            else if (b.propertyName.EndsWith(".y", StringComparison.Ordinal)) v.y = c.keys[0].value;
                            else if (b.propertyName.EndsWith(".z", StringComparison.Ordinal)) v.z = c.keys[0].value;
                            scaleByPath[b.path] = v;
                        }
                        float worst = 0f; string worstBone = "-";
                        foreach (var kv in scaleByPath)
                        {
                            if (!byPath.TryGetValue(kv.Key, out var t)) continue;
                            var p = t.localScale;
                            float m = Mathf.Max(Mathf.Abs(kv.Value.x - p.x),
                                       Mathf.Max(Mathf.Abs(kv.Value.y - p.y), Mathf.Abs(kv.Value.z - p.z)));
                            if (m > worst) { worst = m; worstBone = kv.Key; }
                        }
                        if (worst > globalWorst) { globalWorst = worst; globalBone = clip.name + " / " + worstBone; }
                    }
                    bool fail = globalWorst > 0.001f;
                    if (fail) fails++;
                    sb.AppendLine($"  peor desvio de escala={globalWorst:F5}  {globalBone}  [{(fail ? "FAIL" : "ok")}]");
                    sb.AppendLine();
                }
                catch (Exception e) { sb.AppendLine($"{def.id}: ERROR {e.Message}"); }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }

            sb.AppendLine($"VERDICT: {(fails == 0 ? "PASS" : "FAIL (" + fails + " personajes)")}");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/combo_scale.txt", sb.ToString());
            Debug.Log("[KOM][SCALE-CMP] Logs/combo_scale.txt");
        }

        // Medida L: FOTO DEL PERSONAJE VIVO EN PLAY. Es la unica forma de ver exactamente
        // lo que ve el jugador: mismo Animator, mismo Fighter, mismo material y misma camara
        // que el juego, sin muestrear clips a mano.
        [MenuItem("King of Music/Diagnostico/31 - Foto del personaje vivo en Play", priority = 241)]
        public static void RunLiveShot()
        {
            var fighters = UnityEngine.Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None);
            if (fighters == null || fighters.Length == 0)
            {
                Debug.LogError("[KOM][LIVE] no hay ningun Fighter en Play");
                return;
            }
            const string outDir = "Logs/live";
            Directory.CreateDirectory(outDir);
            foreach (var f in fighters)
            {
                var root = f.transform;
                var camGo = new GameObject("KOMDiagCam");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.07f, 0.08f, 0.10f);
                cam.orthographic = true;
                cam.orthographicSize = 1.15f;
                var target = root.position + new Vector3(0f, 1.0f, 0f);
                var eye = target + new Vector3(2.9f, 0.55f, -2.9f);
                camGo.transform.position = eye;
                camGo.transform.rotation = Quaternion.LookRotation((target - eye).normalized);
                var rt = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                cam.targetTexture = rt;
                Save(cam, rt, $"{outDir}/{f.CharacterId}.png");
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(camGo);
                Debug.Log($"[KOM][LIVE] {f.CharacterId} -> {outDir}/{f.CharacterId}.png");
            }
        }

        // Medida M: EL AURA INFLADA. KOM/AuraShell no es un halo: empuja cada vertice a lo
        // largo de su normal (_Expand 0.03 x flick hasta 1.7 = 5 cm) sobre una COPIA de la
        // malla. Con la super llena el personaje se ve hinchado y parpadeante, y eso es
        // exactamente "la malla se deforma" / "la cabeza se redimensiona".
        // Renderiza el MISMO fotograma con y sin cascara para poder compararlo.
        [MenuItem("King of Music/Diagnostico/32 - Aura inflada: con y sin cascara", priority = 242)]
        public static void RunAuraTest()
        {
            const string outDir = "Logs/aura_test";
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);

            string id = "alex_llora";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{id}.prefab");
            if (prefab == null) return;
            var clip = FindBaked(id, "Combo1");
            if (clip == null) return;

            string previousScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            GameObject go = null;
            Camera cam = null;
            RenderTexture rt = null;
            try
            {
                BuildCamera(scene, out cam, out rt);
                var target = new Vector3(0f, 1.05f, 0f);
                var eye = new Vector3(2.2f, 1.45f, -2.2f);
                cam.orthographicSize = 1.05f;
                cam.transform.position = eye;
                cam.transform.rotation = Quaternion.LookRotation((target - eye).normalized);

                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                var fighter = go.GetComponent<Fighter>();
                if (fighter != null) fighter.enabled = false;
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;
                var src = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    s.forceMatrixRecalculationPerRender = true;
                    s.updateWhenOffscreen = true;
                }
                var bindTrs = new Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)>();
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    bindTrs[t] = (t.localPosition, t.localRotation, t.localScale);

                // Mismo fotograma que el inspector del usuario: frame 28 de 65.
                float phase = 28f / 65f;
                ResetBind(bindTrs);
                clip.SampleAnimation(go, phase * clip.length);
                Save(cam, rt, $"{outDir}/{id}_sin_cascara.png");

                // Cascara igual que la construye SpecialFxRuntime.BuildShells.
                var shellGo = new GameObject(src.name + "_AuraShell");
                shellGo.transform.SetParent(src.transform.parent, false);
                shellGo.transform.localPosition = src.transform.localPosition;
                shellGo.transform.localRotation = src.transform.localRotation;
                shellGo.transform.localScale = src.transform.localScale;
                var shell = shellGo.AddComponent<SkinnedMeshRenderer>();
                shell.sharedMesh = src.sharedMesh;
                shell.bones = src.bones;
                shell.rootBone = src.rootBone;
                shell.localBounds = src.localBounds;
                shell.updateWhenOffscreen = true;
                shell.forceMatrixRecalculationPerRender = true;
                var mat = KomSpecialFx.CreateAuraMaterial(new Color(0.84f, 0.26f, 0.18f), new Color(1f, 0.6f, 0.2f), 0.9f);
                int sub = Mathf.Max(1, src.sharedMesh.subMeshCount);
                var mats = new Material[sub];
                for (int m = 0; m < sub; m++) mats[m] = mat;
                shell.sharedMaterials = mats;
                ResetBind(bindTrs);
                clip.SampleAnimation(go, phase * clip.length);
                Save(cam, rt, $"{outDir}/{id}_con_cascara.png");
                Debug.Log($"[KOM][AURA-TEST] {outDir}");
            }
            finally
            {
                if (rt != null) { cam.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                if (!string.IsNullOrEmpty(previousScene))
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(previousScene);
            }
        }

        // Medida N: EL AISLAMIENTO DEFINITIVO. En la MISMA escena, con las MISMAS luces y la
        // MISMA camara, se renderiza el mismo fotograma dos veces: una con el MODELO del FBX
        // (lo que muestra el inspector) y otra con el PREFAB del juego. Si las dos imagenes
        // no son identicas, la diferencia esta en la malla, el material o el prefab — no en
        // la escena, ni en la luz, ni en la camara.
        [MenuItem("King of Music/Diagnostico/33 - Aislar prefab vs FBX en la misma escena", priority = 243)]
        public static void RunIsolate()
        {
            const string outDir = "Logs/isolate";
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);

            string id = "alex_llora";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{id}.prefab");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Art/Characters/{id}.fbx");
            var clip = FindBaked(id, "Combo1");
            if (prefab == null || model == null || clip == null) { Debug.LogError("[KOM][ISOLATE] faltan assets"); return; }

            string previousScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            GameObject go = null;
            Camera cam = null;
            RenderTexture rt = null;
            try
            {
                BuildCamera(scene, out cam, out rt);
                var target = new Vector3(0f, 1.05f, 0f);
                var eye = new Vector3(2.2f, 1.45f, -2.2f);
                cam.orthographicSize = 1.05f;
                cam.transform.position = eye;
                cam.transform.rotation = Quaternion.LookRotation((target - eye).normalized);

                float phase = 28f / 65f;
                foreach (var pair in new[] { ("fbx", model), ("prefab", prefab) })
                {
                    if (go != null) { UnityEngine.Object.DestroyImmediate(go); go = null; }
                    go = (GameObject)PrefabUtility.InstantiatePrefab(pair.Item2, scene);
                    go.transform.position = Vector3.zero;
                    go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                    var fighter = go.GetComponent<Fighter>();
                    if (fighter != null) fighter.enabled = false;
                    var animator = go.GetComponentInChildren<Animator>(true);
                    if (animator != null) animator.enabled = false;
                    foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        s.forceMatrixRecalculationPerRender = true;
                        s.updateWhenOffscreen = true;
                    }
                    var bindTrs = new Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)>();
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        bindTrs[t] = (t.localPosition, t.localRotation, t.localScale);
                    ResetBind(bindTrs);
                    clip.SampleAnimation(go, phase * clip.length);
                    Save(cam, rt, $"{outDir}/{id}_{pair.Item1}.png");
                }
                Debug.Log($"[KOM][ISOLATE] {outDir}");
            }
            finally
            {
                if (rt != null) { cam.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                if (!string.IsNullOrEmpty(previousScene))
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(previousScene);
            }
        }

        // Medida O: FOTO CON LA CAMARA DEL JUEGO. Fuerza el estado y el fotograma exactos en
        // el Fighter vivo y renderiza con la camara real del combate. Es la unica imagen que
        // coincide con lo que ve el jugador, y se puede comparar con la del inspector.
        [MenuItem("King of Music/Diagnostico/34 - Foto con la camara del juego", priority = 244)]
        public static void RunGameCameraShot()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[KOM][GAME-SHOT] hay que estar en Play");
                return;
            }
            var fighters = UnityEngine.Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None);
            if (fighters == null || fighters.Length == 0) { Debug.LogError("[KOM][GAME-SHOT] sin fighters"); return; }

            Camera cam = null;
            foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (c.name == "CombatCamera") { cam = c; break; }
            }
            if (cam == null) cam = Camera.main;
            if (cam == null) { Debug.LogError("[KOM][GAME-SHOT] sin camara"); return; }

            var f = fighters[0];
            foreach (var x in fighters) if (x.CharacterId == "alex_llora") f = x;
            var animator = f.GetComponentInChildren<Animator>(true);
            animator.Play(Animator.StringToHash("Combo1"), 0, 28f / 65f);
            animator.Update(0f);
            animator.Update(0f);

            const string outDir = "Logs/game_shot";
            Directory.CreateDirectory(outDir);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            var previous = cam.targetTexture;
            cam.targetTexture = rt;
            Save(cam, rt, $"{outDir}/{f.CharacterId}_combo1_f28.png");
            cam.targetTexture = previous;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            Debug.Log($"[KOM][GAME-SHOT] {outDir}/{f.CharacterId}_combo1_f28.png");
        }

        // Medida P: UN SOLO personaje, camara del juego, fotograma exacto. Limpia los fighters
        // que haya, instancia el prefab una vez y renderiza con la camara del combate.
        [MenuItem("King of Music/Diagnostico/35 - Un personaje limpio con la camara del juego", priority = 245)]
        public static void RunSingleGameShot()
        {
            if (!Application.isPlaying) { Debug.LogError("[KOM][SINGLE] hay que estar en Play"); return; }
            // DestroyImmediate: Destroy se difiere al final del frame y las copias viejas
            // seguian renderizando en la foto (salieron tres personajes superpuestos).
            foreach (var x in UnityEngine.Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(x.gameObject);
            foreach (var x in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (x.name != "CombatCamera" && x.name != "DiagCam") UnityEngine.Object.DestroyImmediate(x.gameObject);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/alex_llora.prefab");
            var clip = FindBaked("alex_llora", "Combo1");
            var cam = Camera.main;
            if (prefab == null || clip == null || cam == null) { Debug.LogError("[KOM][SINGLE] faltan piezas"); return; }

            var go = (GameObject)UnityEngine.Object.Instantiate(prefab);
            go.transform.position = new Vector3(0f, 0f, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 150f, 0f);
            var fighter = go.GetComponent<Fighter>();
            if (fighter != null) fighter.enabled = false;
            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.enabled = false;
            foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                s.forceMatrixRecalculationPerRender = true;
                s.updateWhenOffscreen = true;
            }
            clip.SampleAnimation(go, (28f / 65f) * clip.length);

            const string outDir = "Logs/game_shot";
            Directory.CreateDirectory(outDir);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            var previous = cam.targetTexture;
            cam.targetTexture = rt;
            Save(cam, rt, $"{outDir}/single_combo1_f28.png");
            cam.targetTexture = previous;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            Debug.Log("[KOM][SINGLE] Logs/game_shot/single_combo1_f28.png");
        }

        static Vector3[] Relative(Transform[] bones, Transform pelvis)
        {
            var o = ChainPosition(pelvis);
            var res = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length; i++) res[i] = ChainPosition(bones[i]) - o;
            return res;
        }

        static HashSet<Transform> WeightedBones(SkinnedMeshRenderer smr)
        {
            var set = new HashSet<Transform>();
            var mesh = smr.sharedMesh;
            if (mesh == null || smr.bones == null) return set;
            var used = new bool[smr.bones.Length];
            foreach (var w in mesh.boneWeights)
            {
                Mark(used, w.weight0, w.boneIndex0);
                Mark(used, w.weight1, w.boneIndex1);
                Mark(used, w.weight2, w.boneIndex2);
                Mark(used, w.weight3, w.boneIndex3);
            }
            for (int i = 0; i < smr.bones.Length; i++)
                if (used[i] && smr.bones[i] != null) set.Add(smr.bones[i]);
            return set;
        }

        static void Mark(bool[] used, float w, int i)
        {
            if (w > 0.001f && i >= 0 && i < used.Length) used[i] = true;
        }

        static void ResetBind(Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)> bind)
        {
            foreach (var kv in bind)
            {
                if (kv.Key == null) continue;
                kv.Key.localPosition = kv.Value.p;
                kv.Key.localRotation = kv.Value.r;
                kv.Key.localScale = kv.Value.s;
            }
        }

        static AnimationClip FindBaked(string id, string state)
        {
            foreach (var suffix in new[] { "_Clean", "_FirstRaw", "_RawWalk" })
            {
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{InPlaceDir}/{id}_{state}{suffix}.anim");
                if (c != null) return c;
            }
            return null;
        }

        // Las tomas del FBX llevan el nombre del estado (el setup las renombra al importar).
        static AnimationClip FindRaw(string id, string state)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath($"Assets/Art/Characters/{id}.fbx")
                .OfType<AnimationClip>()
                .Where(c => c != null && !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                .ToArray();
            return all.FirstOrDefault(c => c.name == state);
        }

        // Posicion en el espacio local de la cabeza: deshace su traslacion Y su rotacion.
        static Vector3 HeadLocal(Transform head, Vector3 worldPos)
        {
            var m = ChainMatrix(head);
            return m.inverse.MultiplyPoint3x4(worldPos);
        }

        static Matrix4x4 ChainMatrix(Transform t)
        {
            var m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
            for (var p = t.parent; p != null; p = p.parent)
                m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
            return m;
        }

        static Vector3 ChainPosition(Transform t)
        {
            var m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
            for (var p = t.parent; p != null; p = p.parent)
                m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
            return m.GetColumn(3);
        }
    }
}
