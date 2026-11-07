using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KOM.EditorTools
{
    // Render LATERAL determinista (perfil puro, camara ortografica) sin entrar en Play.
    // Escribe un PNG por pose en Logs/anim_side/tiles y un manifiesto. El montaje con
    // etiquetas lo hace tools/anim_sheet.py (Pillow): Unity solo renderiza.
    // Motivo: en 3/4 los errores de mezcla y de estiramiento NO se ven. De lado se ve
    // exactamente lo que ve el jugador en combate.
    public static class KomAnimSheet
    {
        const string PrefabDir = "Assets/Resources/Characters";
        const string TileDir = "Logs/anim_side/tiles";

        const int TileW = 512;
        const int TileH = 640;

        static readonly float[] Phases = { 0.10f, 0.30f, 0.50f, 0.70f, 0.90f };
        // Locomocion: ciclo completo para ver el apoyo del pie y el cierre del loop.
        static readonly float[] LocomotionPhases = { 0.02f, 0.12f, 0.22f, 0.32f, 0.42f, 0.52f, 0.62f, 0.72f, 0.82f, 0.92f };
        // En el editor, PreviewScene no aísla bien Camera.Render con varias capturas.
        // El menú "todos" usa escena temporal Single (igual que batch) y restaura la activa.
        static bool _forceSingleScene;

        public static readonly string[] DefaultClips =
        {
            "Idle", "Run", "Boxing", "LightPunch", "HeavyPunch", "LightKick", "HeavyKick",
            "Jump", "Hit", "HitHeavy", "KO", "GetUp", "Crouch", "WalkGuard", "Block", "Stunned",
            "GroinHit", "Uppercut", "DropKick", "QuadPunch",
            "Combo1", "Combo2", "Combo3", "Victory"
        };

        // La hoja lateral de un personaje incluye su especial si lo declara. Asi la
        // animacion del poder pasa por el mismo gate visual que el resto del cuerpo.
        static string[] ClipsFor(CharacterDef def)
        {
            if (def == null || !def.HasSpecial) return DefaultClips;
            // TODOS los especiales de combate, no solo el principal: la jefa final declara
            // `extraSpecials` (beam + boomerang + club) y cada uno tiene su propio clip. Si
            // solo se revisa el principal, `Combo` (el golpe con la guitarra, que gira 172.8°
            // por diseño) se queda sin pasar por el gate visual.
            var list = new List<string>(DefaultClips);
            for (int i = 0; i < def.SpecialCount; i++)
            {
                var s = def.GetSpecial(i);
                if (s == null || string.IsNullOrEmpty(s.animatorState)) continue;
                if (!list.Contains(s.animatorState)) list.Add(s.animatorState);
            }
            return list.ToArray();
        }

        [MenuItem("King of Music/Preview/6 Hoja LATERAL de clips", priority = 55)]
        public static void MenuCaptureDefault()
        {
            CaptureInTemporaryScene("juan_grabriel");
        }

        // Hoja lateral de UN personaje: sirve para revisar un cambio de malla o de rig
        // sin tener que regenerar las hojas de los siete.
        public static void CaptureCharacter(string id)
        {
            CaptureInTemporaryScene(id);
        }

        [MenuItem("King of Music/Preview/7 Hoja LATERAL todos", priority = 56)]
        public static void MenuCaptureAll()
        {
            string previous = EditorSceneManager.GetActiveScene().path;
            _forceSingleScene = true;
            try
            {
                Directory.CreateDirectory(TileDir);
                foreach (var f in Directory.GetFiles(TileDir, "*.png")) File.Delete(f);
                var manifest = new StringBuilder();
                manifest.AppendLine("# KOM lateral tiles");
                manifest.AppendLine("mode=clips");
                foreach (var def in KomData.Characters.characters)
                {
                    if (def != null) CaptureClips(def.id, ClipsFor(def), manifest);
                }
                File.WriteAllText($"{TileDir}/manifest.txt", manifest.ToString());
                Debug.Log($"[KOM] Tiles laterales de todos en {TileDir}.");
            }
            finally
            {
                _forceSingleScene = false;
                if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
            }
        }

        static void CaptureInTemporaryScene(string id)
        {
            string previous = EditorSceneManager.GetActiveScene().path;
            _forceSingleScene = true;
            try
            {
                Directory.CreateDirectory(TileDir);
                foreach (var f in Directory.GetFiles(TileDir, "*.png")) File.Delete(f);
                var manifest = new StringBuilder();
                manifest.AppendLine("# KOM lateral tiles");
                manifest.AppendLine("mode=clips");
                var def = KomData.GetCharacter(id);
                CaptureClips(id, ClipsFor(def), manifest);
                File.WriteAllText($"{TileDir}/manifest.txt", manifest.ToString());
                Debug.Log($"[KOM] Tiles laterales en {TileDir}. Monta con: python tools/anim_sheet.py");
            }
            finally
            {
                _forceSingleScene = false;
                if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
            }
        }

        [MenuItem("King of Music/Preview/8 Hoja LATERAL todos en escena temporal", priority = 57)]
        public static void MenuCaptureAllSingle()
        {
            string previous = EditorSceneManager.GetActiveScene().path;
            _forceSingleScene = true;
            try
            {
                Directory.CreateDirectory(TileDir);
                foreach (var f in Directory.GetFiles(TileDir, "*.png")) File.Delete(f);
                var manifest = new StringBuilder();
                manifest.AppendLine("# KOM lateral tiles");
                manifest.AppendLine("mode=clips");
                foreach (var def in KomData.Characters.characters)
                {
                    if (def != null) CaptureClips(def.id, ClipsFor(def), manifest);
                }
                File.WriteAllText($"{TileDir}/manifest.txt", manifest.ToString());
                Debug.Log($"[KOM] Tiles laterales de todos (escena temporal) en {TileDir}.");
            }
            finally
            {
                _forceSingleScene = false;
                if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
            }
        }

        // Entrada batch:
        //  Unity.exe -batchmode -quit -executeMethod KOM.EditorTools.KomAnimSheet.CaptureBatch
        //  args opcionales: --kom-char=juan_grabriel  --kom-mode=clips|controller
        public static void CaptureBatch()
        {
            var args = Environment.GetCommandLineArgs();
            var ids = args.Where(a => a.StartsWith("--kom-char=")).Select(a => a.Substring("--kom-char=".Length)).ToArray();
            if (ids.Length == 0) ids = KomData.Characters.characters.Where(c => c != null).Select(c => c.id).ToArray();
            string mode = args.FirstOrDefault(a => a.StartsWith("--kom-mode="));
            mode = mode == null ? "clips" : mode.Substring("--kom-mode=".Length);

            Directory.CreateDirectory(TileDir);
            foreach (var f in Directory.GetFiles(TileDir, "*.png")) File.Delete(f);

            var manifest = new StringBuilder();
            manifest.AppendLine("# KOM lateral tiles");
            manifest.AppendLine($"mode={mode}");
            foreach (var id in ids)
            {
                if (mode == "controller") CaptureController(id, manifest);
                else CaptureClips(id, ClipsFor(KomData.GetCharacter(id)), manifest);
            }
            File.WriteAllText($"{TileDir}/manifest.txt", manifest.ToString());
            Debug.Log($"[KOM][SHEET] {manifest.Length} bytes de manifiesto, tiles en {TileDir}");
        }

        static void CaptureClips(string id, string[] clipNames, StringBuilder manifest)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{id}.prefab");
            if (prefab == null) { manifest.AppendLine($"{id}: FALTA PREFAB"); return; }

            var scene = OpenStageScene();
            GameObject go = null;
            Camera cam = null;
            RenderTexture rt = null;
            try
            {
                BuildStage(scene, out cam, out rt);
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.Euler(0f, 90f, 0f); // perfil puro: mira a +X
                var fighter = go.GetComponent<Fighter>();
                if (fighter != null) fighter.enabled = false;
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;

                // En batch no hay bucle de editor: sin esto el SkinnedMeshRenderer
                // reutiliza la ultima malla horneada y todas las capturas salen iguales.
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.forceMatrixRecalculationPerRender = true;
                    smr.updateWhenOffscreen = true;
                }

                // El foco debe ser un hueso DEF con peso/animacion real. El control `torso`
                // puede quedar sin curvas tras la poda y descuadraba la camara lateral.
                var transforms = go.GetComponentsInChildren<Transform>(true);
                var focus = transforms.FirstOrDefault(t => t.name.StartsWith("DEF-pelvis", StringComparison.Ordinal))
                    ?? transforms.FirstOrDefault(t => t.name.StartsWith("DEF-spine", StringComparison.Ordinal))
                    ?? transforms.FirstOrDefault(t => t.name == "torso");
                foreach (var name in clipNames)
                {
                    var clip = FindClip(id, name);
                    if (clip == null) { manifest.AppendLine($"{id}|{name}|MISSING"); continue; }
                    // La camara NO sigue la cadera: queda fija al origen, con el marcador
                    // de root visible. Asi cualquier deriva de la malla se ve de lado.
                    var phases = (name == "WalkGuard" || name == "Run") ? LocomotionPhases : Phases;
                    for (int c = 0; c < phases.Length; c++)
                    {
                        clip.SampleAnimation(go, phases[c] * clip.length);
                        string hip = "-";
                        if (focus != null) hip = ChainPosition(focus).ToString("F3");
                        Save(cam, rt, $"{id}__{name}__{c}.png");
                        manifest.AppendLine($"{id}|{name}|{c}|len={clip.length:F2}|hip={hip}");
                    }
                }
            }
            catch (Exception e)
            {
                manifest.AppendLine($"{id}: ERROR {e.Message}");
                Debug.LogError($"[KOM][SHEET] {id}: {e}");
            }
            finally
            {
                Teardown(scene, cam, rt, go);
            }
        }

        // Modo controller: usa el Animator REAL del prefab (blend tree + crossfades),
        // que es donde aparece la deformacion por mezcla. Avanza el Animator a mano.
        static void CaptureController(string id, StringBuilder manifest)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{id}.prefab");
            if (prefab == null) { manifest.AppendLine($"{id}: FALTA PREFAB"); return; }

            var scene = OpenStageScene();
            GameObject go = null;
            Camera cam = null;
            RenderTexture rt = null;
            try
            {
                BuildStage(scene, out cam, out rt);
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                var fighter = go.GetComponent<Fighter>();
                if (fighter != null) fighter.enabled = false;
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator == null) { manifest.AppendLine($"{id}: sin Animator"); return; }
                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.forceMatrixRecalculationPerRender = true;
                    smr.updateWhenOffscreen = true;
                }

                // 0) Block real (Center Block) y guard-walk real, ambos full-body en Base.
                animator.SetLayerWeight(0, 1f);
                if (animator.layerCount > 1) animator.SetLayerWeight(1, 0f);
                animator.Play(Animator.StringToHash("Block"), 0, 0f);
                Step(animator, 0.4f);
                Save(cam, rt, $"{id}__GUARD_BLOCK__0.png");
                manifest.AppendLine($"{id}|GUARD_BLOCK|0");
                Step(animator, 0.4f);
                Save(cam, rt, $"{id}__GUARD_BLOCK__1.png");
                manifest.AppendLine($"{id}|GUARD_BLOCK|1");

                animator.Play(Animator.StringToHash("WalkGuardFwd"), 0, 0f);
                Step(animator, 0.5f);
                Save(cam, rt, $"{id}__GUARD_WALK__0.png");
                manifest.AppendLine($"{id}|GUARD_WALK|0");
                Step(animator, 0.25f);
                Save(cam, rt, $"{id}__GUARD_WALK__1.png");
                manifest.AppendLine($"{id}|GUARD_WALK|1");

                // 1) Estados discretos de locomocion: un clip por estado, sin BlendTree.
                foreach (var state in new[] { "WalkGuardFwd", "WalkGuardBack", "RunFwd", "RunBack" })
                {
                    animator.Play(Animator.StringToHash(state), 0, 0f);
                    Step(animator, 0.55f);
                    Save(cam, rt, $"{id}__STATE_{state}__0.png");
                    manifest.AppendLine($"{id}|STATE_{state}|0");
                    Step(animator, 0.3f);
                    Save(cam, rt, $"{id}__STATE_{state}__1.png");
                    manifest.AppendLine($"{id}|STATE_{state}|1");
                }

                // 2) Crouch real: entrada y pose baja final.
                animator.Play(Animator.StringToHash("Crouch"), 0, 0f);
                Step(animator, 0.22f);
                Save(cam, rt, $"{id}__CROUCH_ENTRY__0.png");
                manifest.AppendLine($"{id}|CROUCH_ENTRY|0");
                Step(animator, 0.55f);
                Save(cam, rt, $"{id}__CROUCH_LOW__0.png");
                manifest.AppendLine($"{id}|CROUCH_LOW|0");

                // 3) Stunned (guard break) y GetUp.
                animator.Play(Animator.StringToHash("Stunned"), 0, 0f);
                Step(animator, 0.45f);
                Save(cam, rt, $"{id}__STUNNED__0.png");
                manifest.AppendLine($"{id}|STUNNED|0");
                animator.Play(Animator.StringToHash("GetUp"), 0, 0f);
                Step(animator, 0.45f);
                Save(cam, rt, $"{id}__GETUP__0.png");
                manifest.AppendLine($"{id}|GETUP|0");

                // Estados de ataque nuevos, capturados en su ventana de impacto.
                foreach (var (state, window) in new[] { ("LowKick", 0.45f), ("Uppercut", 0.72f), ("DropKick", 0.64f), ("QuadPunch", 0.26f) })
                {
                    animator.Play(Animator.StringToHash(state), 0, 0f);
                    Step(animator, window);
                    Save(cam, rt, $"{id}__STATE_{state}__0.png");
                    manifest.AppendLine($"{id}|STATE_{state}|0");
                }

                // 4) Transiciones reales en la Base full-body.
                var transitions = new[]
                {
                    (from: "Idle", to: "WalkGuardFwd"),
                    (from: "WalkGuardFwd", to: "Idle"),
                    (from: "WalkGuardFwd", to: "Block"),
                    (from: "Block", to: "WalkGuardFwd"),
                    (from: "WalkGuardFwd", to: "WalkGuardBack"),
                    (from: "WalkGuardBack", to: "RunBack"),
                    (from: "RunFwd", to: "WalkGuardFwd"),
                    (from: "Idle", to: "Crouch"),
                    (from: "WalkGuardFwd", to: "LightPunch"),
                    (from: "WalkGuardFwd", to: "HeavyKick"),
                    (from: "WalkGuardFwd", to: "Uppercut"),
                    (from: "WalkGuardFwd", to: "DropKick"),
                    (from: "WalkGuardFwd", to: "QuadPunch"),
                    (from: "WalkGuardFwd", to: "Hit"),
                    (from: "WalkGuardFwd", to: "Stunned"),
                    (from: "WalkGuardFwd", to: "KO"),
                    (from: "KO", to: "GetUp")
                };
                foreach (var tr in transitions)
                {
                    animator.Play(Animator.StringToHash(tr.from), 0, 0f);
                    Step(animator, 0.35f);
                    animator.CrossFadeInFixedTime(Animator.StringToHash(tr.to), 0.1f, 0, 0f);
                    Step(animator, 0.02f);
                    Save(cam, rt, $"{id}__FADE_{tr.from}_to_{tr.to}__0.png");
                    manifest.AppendLine($"{id}|FADE_{tr.from}_to_{tr.to}|0");
                    Step(animator, 0.03f);
                    Save(cam, rt, $"{id}__FADE_{tr.from}_to_{tr.to}__1.png");
                    manifest.AppendLine($"{id}|FADE_{tr.from}_to_{tr.to}|1");
                }
            }
            catch (Exception e)
            {
                manifest.AppendLine($"{id}: ERROR {e.Message}");
                Debug.LogError($"[KOM][SHEET] {id}: {e}");
            }
            finally
            {
                Teardown(scene, cam, rt, go);
            }
        }

        static void Step(Animator animator, float seconds)
        {
            const float dt = 1f / 60f;
            int steps = Mathf.Max(1, Mathf.RoundToInt(seconds / dt));
            for (int i = 0; i < steps; i++) animator.Update(dt);
        }

        // En batch no hay escena abierta guardada: hay que usar Single. En el editor
        // (menu) se usa una preview scene para no destruir la escena del usuario.
        static UnityEngine.SceneManagement.Scene OpenStageScene()
        {
            if (_forceSingleScene || Application.isBatchMode)
                return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            return EditorSceneManager.NewPreviewScene();
        }

        static void CloseStageScene(UnityEngine.SceneManagement.Scene scene)
        {
            if (_forceSingleScene || Application.isBatchMode)
                EditorSceneManager.CloseScene(scene, true);
            else
                EditorSceneManager.ClosePreviewScene(scene);
        }

        static void Save(Camera cam, RenderTexture rt, string file)
        {
            // Doble render: el primero actualiza matrices de mundo y skinning, el segundo
            // captura el estado definitivo. Sin esto la foto puede ir un paso por detras.
            cam.Render();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(TileW, TileH, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, TileW, TileH), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes($"{TileDir}/{file}", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static void Teardown(UnityEngine.SceneManagement.Scene scene, Camera cam, RenderTexture rt, GameObject go)
        {
            if (cam != null) cam.targetTexture = null;
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            CloseStageScene(scene);
        }

        static void BuildStage(UnityEngine.SceneManagement.Scene scene, out Camera cam, out RenderTexture rt)
        {
            var camGo = new GameObject("SideCamera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
            cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.10f);
            cam.orthographic = true;
            // Encuadre de CUERPO COMPLETO: el root (x=0) queda a la izquierda del centro
            // y el cuerpo entero (incluidos pies y brazos) siempre dentro del tile, con
            // ~1.7 m de margen a la derecha para que cualquier deriva sea visible.
            cam.orthographicSize = 1.55f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 40f;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            camGo.transform.position = new Vector3(0.50f, 1.30f, -6f);
            camGo.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            cam.tag = "MainCamera";

            rt = new RenderTexture(TileW, TileH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 4
            };
            rt.Create();
            cam.targetTexture = rt;

            var keyGo = new GameObject("Key", typeof(Light));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(keyGo, scene);
            var key = keyGo.GetComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.7f;
            key.color = new Color(1f, 0.95f, 0.88f);
            keyGo.transform.rotation = Quaternion.Euler(38f, -35f, 0f);

            var fillGo = new GameObject("Fill", typeof(Light));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fillGo, scene);
            var fill = fillGo.GetComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.7f;
            fill.color = new Color(0.55f, 0.7f, 1f);
            fillGo.transform.rotation = Quaternion.Euler(12f, 140f, 0f);

            var rimGo = new GameObject("Rim", typeof(Light));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rimGo, scene);
            var rim = rimGo.GetComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 1.0f;
            rim.color = new Color(1f, 0.75f, 0.45f);
            rimGo.transform.rotation = Quaternion.Euler(20f, 200f, 0f);

            var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMat.SetColor("_BaseColor", new Color(0.13f, 0.14f, 0.17f));
            floorMat.SetFloat("_Smoothness", 0.15f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(3f, 1f, 1.2f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            // Referencia metrica: linea horizontal cada 0.25 m detras del personaje.
            var gridMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            gridMat.SetColor("_BaseColor", new Color(0.22f, 0.55f, 0.72f));
            for (int i = 1; i <= 8; i++)
            {
                var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(line, scene);
                line.name = "Grid";
                line.transform.position = new Vector3(0f, i * 0.25f, 1.35f);
                line.transform.localScale = new Vector3(14f, 0.004f, 0.008f);
                line.GetComponent<Renderer>().sharedMaterial = gridMat;
                UnityEngine.Object.DestroyImmediate(line.GetComponent<Collider>());
            }

            // Marcador de ROOT (x=0): linea vertical magenta + disco en el suelo. Si la
            // malla se va del root, se ve inmediatamente contra esta referencia fija.
            var rootMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            rootMat.SetColor("_BaseColor", new Color(1f, 0.12f, 0.75f));
            var rootLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rootLine, scene);
            rootLine.name = "RootMarker";
            rootLine.transform.position = new Vector3(0f, 1.25f, 1.20f);
            rootLine.transform.localScale = new Vector3(0.018f, 2.5f, 0.018f);
            rootLine.GetComponent<Renderer>().sharedMaterial = rootMat;
            UnityEngine.Object.DestroyImmediate(rootLine.GetComponent<Collider>());
            var rootDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rootDisc, scene);
            rootDisc.name = "RootDisc";
            rootDisc.transform.position = new Vector3(0f, 0.012f, 0.05f);
            rootDisc.transform.localScale = new Vector3(0.30f, 0.01f, 0.30f);
            rootDisc.GetComponent<Renderer>().sharedMaterial = rootMat;
            UnityEngine.Object.DestroyImmediate(rootDisc.GetComponent<Collider>());
        }

        // Posicion en espacio de modelo desde los TRS locales (la cache de mundo del
        // editor puede ir un paso por detras justo despues de SampleAnimation).
        static Vector3 ChainPosition(Transform t)
        {
            var m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
            for (var p = t.parent; p != null; p = p.parent)
                m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
            return m.GetColumn(3);
        }

        static AnimationClip FindClip(string id, string name)
        {
            // La victoria no tiene clip propio: cada personaje celebra con una toma
            // distinta (SpecialWide / Boxing / MagicHeal). Sin este mapeo la hoja lateral
            // no la encuentra y el festejo se queda sin gate visual.
            if (name == "Victory") name = KomSetup.VictoryClipName(KomData.GetCharacter(id));
            // El clip que JUEGA es la copia in-place; la hoja debe mostrar ese.
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

        // =====================================================================================
        // Verificacion ULTRARROBUSTA del estado que JUEGA: 100 frames consecutivos del
        // Animator real (WalkGuardFwd), de perfil y en primer plano 3/4 de la cabeza.
        // El gate numerico equivalente esta en KomValidation (Logs/anim_head.txt).
        // =====================================================================================
        const string WalkDir = "Logs/walk100";

        [MenuItem("King of Music/Diagnostico/21 - WalkGuard 100 frames (juan)", priority = 231)]
        public static void MenuWalk100Juan()
        {
            CaptureWalk100(new[] { "juan_grabriel" });
        }

        [MenuItem("King of Music/Diagnostico/22 - WalkGuard 100 frames (todos)", priority = 232)]
        public static void MenuWalk100All()
        {
            CaptureWalk100(KomData.Characters.characters.Where(c => c != null).Select(c => c.id).ToArray());
        }

        static void CaptureWalk100(string[] ids)
        {
            string previous = EditorSceneManager.GetActiveScene().path;
            Directory.CreateDirectory(WalkDir);
            foreach (var f in Directory.GetFiles(WalkDir, "*.png")) File.Delete(f);
            var report = new StringBuilder();
            report.AppendLine("KOM WALKGUARD 100 FRAMES · Animator real · perfil + cabeza 3/4");
            _forceSingleScene = true;
            try
            {
                foreach (var id in ids) CaptureWalk100Char(id, report);
            }
            finally
            {
                _forceSingleScene = false;
                if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
            }
            File.WriteAllText($"{WalkDir}/report.txt", report.ToString());
            Debug.Log($"[KOM][WALK100] {WalkDir}/report.txt");
        }

        static void CaptureWalk100Char(string id, StringBuilder report)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{id}.prefab");
            if (prefab == null) { report.AppendLine($"{id}: FALTA prefab"); return; }
            var scene = OpenStageScene();
            GameObject go = null;
            Camera sideCam = null, headCam = null;
            RenderTexture sideRt = null, headRt = null;
            try
            {
                BuildStage(scene, out sideCam, out sideRt);
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                var fighter = go.GetComponent<Fighter>();
                if (fighter != null) fighter.enabled = false;
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator == null) { report.AppendLine($"{id}: sin Animator"); return; }
                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.forceMatrixRecalculationPerRender = true;
                    smr.updateWhenOffscreen = true;
                }

                var headGo = new GameObject("HeadCam", typeof(Camera));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(headGo, scene);
                headCam = headGo.GetComponent<Camera>();
                headCam.clearFlags = CameraClearFlags.SolidColor;
                headCam.backgroundColor = new Color(0.09f, 0.09f, 0.11f);
                headCam.fieldOfView = 20f;
                headCam.nearClipPlane = 0.05f;
                headCam.farClipPlane = 30f;
                headCam.allowHDR = false;
                headCam.allowMSAA = true;
                var eye = new Vector3(0f, 1.60f, 0f);
                headGo.transform.position = new Vector3(1.05f, 1.63f, 0.95f);
                headGo.transform.rotation = Quaternion.LookRotation((eye - headGo.transform.position).normalized, Vector3.up);
                headRt = new RenderTexture(320, 320, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                headRt.Create();
                headCam.targetTexture = headRt;

                animator.Play("WalkGuardFwd", 0, 0f);
                Step(animator, 0.02f);

                var head = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "DEF-spine.006");
                float prevX = 0f, prevY = 0f, prevZ = 0f;
                bool hasPrev = false;
                float maxJump = 0f;
                for (int i = 0; i < 100; i++)
                {
                    animator.Update(1f / 60f);
                    SaveTo(sideCam, sideRt, $"{WalkDir}/{id}_side_{i:000}.png", TileW, TileH);
                    SaveTo(headCam, headRt, $"{WalkDir}/{id}_head_{i:000}.png", 320, 320);
                    if (head != null)
                    {
                        var local = go.transform.InverseTransformPoint(head.position);
                        if (hasPrev)
                            maxJump = Mathf.Max(maxJump, Vector3.Distance(new Vector3(prevX, prevY, prevZ), local));
                        prevX = local.x; prevY = local.y; prevZ = local.z;
                        hasPrev = true;
                    }
                }
                report.AppendLine($"{id,-16} frames=100 maxHeadJump={maxJump:F4} m  [{(maxJump <= 0.05f ? "ok" : "FAIL")}]");
            }
            catch (Exception e)
            {
                report.AppendLine($"{id}: ERROR {e.Message}");
                Debug.LogError($"[KOM][WALK100] {id}: {e}");
            }
            finally
            {
                if (headCam != null) headCam.targetTexture = null;
                if (headRt != null) { headRt.Release(); UnityEngine.Object.DestroyImmediate(headRt); }
                Teardown(scene, sideCam, sideRt, go);
            }
        }

        static void SaveTo(Camera cam, RenderTexture rt, string path, int w, int h)
        {
            cam.Render();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
