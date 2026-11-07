using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KOM.EditorTools
{
    public static class KomSetup
    {
        const string CharacterArtDir = "Assets/Art/Characters";
        // FBX de la primera version (iteracion 1). Sus clips RAW son la referencia que
        // el usuario confirma que no deformaba la cara; se usan para los estados comunes.
        const string FirstCommitDir = "Assets/Art/Characters/FirstCommit";
        const string CharacterPrefabDir = "Assets/Resources/Characters";
        const string ControllerDir = "Assets/KOM/Animation/Controllers";
        const string MaterialDir = "Assets/Art/Characters/Materials";
        const string SceneDir = "Assets/Scenes/KOM";
        const string PreviewScenePath = SceneDir + "/_PreviewVersus.unity";

        static readonly Dictionary<string, string> TakeMap = new Dictionary<string, string>
        {
            { "Idle", "Idle" },
            { "Idle2", "Idle2" },
            { "Running", "Run" },
            { "Jumping", "Jump" },
            { "Boxing", "Boxing" },
            { "Singing", "Singing" },
            { "Goalkeeper Catch", "GroundSlam" },
            // Combos por secuencia (G+G+G, H+H+H, J+J+J+J). Los FBX se llaman
            // COMBO1QUADPUNCH / Uppercut JabCOMBO2 / COMBO3CUIDADONOTIENEINPLACE.
            { "COMBO1QUADPUNCH", "Combo1" },
            { "Uppercut JabCOMBO2", "Combo2" },
            { "COMBO3CUIDADONOTIENEINPLACE", "Combo3" },
            { "Punching2 (2)", "LightPunch" },
            { "Cross Punch (1)", "HeavyPunch" },
            { "Uppercut Jab", "Uppercut" },
            { "Kicking (1)", "LightKick" },
            { "Roundhouse Kick", "HeavyKick" },
            { "Drop Kick", "DropKick" },
            { "Head Hit", "Hit" },
            { "Receive Punch To The Face (1)", "HitHeavy" },
            { "Stunned", "Stunned" },
            { "Falling Back Death", "KO" },
            { "Getting Up", "GetUp" },
            { "agacharse", "Crouch" },
            { "camianrcongaurdia", "WalkGuard" },
            { "Center Block (1)", "Block" },
            { "Casting Spell", "Special" },
            { "Wide Arm Spell Casting", "SpecialWide" },
            { "Two Hand Club Combo", "Combo" },
            { "Kick To The Groin", "GroinHit" },
            { "Quad Punch", "QuadPunch" },
            { "Upward Thrust", "Thrust" },
            { "Double Dagger Stab", "DaggerStab" },
            { "Stabbing", "Stab" },
            { "Fireball", "Fireball" },
            { "Magic Heal", "MagicHeal" },
            { "Standing 2H Magic Attack 01", "SpecialBeam" },
            { "Dancing Twerk", "Twerk" }
        };

        // La locucion NO tiene Walk normal: andar (con o sin bloqueo) usa solo
        // camianrcongaurdia. Los estados de direccion son discretos (Fwd/Back), sin BlendTree.
        static readonly HashSet<string> LoopStates = new HashSet<string>
        {
            "Idle", "Idle2", "Run", "Boxing", "WalkGuard", "Block"
        };

        public static readonly string[] RequiredBaseStates =
        {
            "Idle", "WalkGuardFwd", "WalkGuardBack", "RunFwd", "RunBack", "Block", "Crouch",
            "Jump", "Hit", "HitHeavy", "GroinHit", "Stunned", "KO", "GetUp", "Victory",
            "LightPunch", "HeavyPunch", "LightKick", "HeavyKick",
            "Uppercut", "DropKick", "QuadPunch"
        };

        public static readonly string[] RequiredClips =
        {
            "Idle", "Idle2", "Run", "Jump", "Boxing",
            "LightPunch", "HeavyPunch", "Uppercut", "LightKick", "HeavyKick",
            "DropKick", "Hit", "HitHeavy", "Stunned", "KO", "GetUp",
            "Special", "SpecialWide", "Combo", "GroinHit", "QuadPunch",
            "Thrust", "DaggerStab", "Stab", "Fireball", "MagicHeal", "Twerk",
            "Crouch", "WalkGuard", "Block"
        };

        [MenuItem("King of Music/Setup/1 - Configurar importacion", priority = 10)]
        public static void ConfigureImportsMenu()
        {
            ConfigureImports();
            EditorUtility.DisplayDialog("King of Music", "Importacion configurada.\nSiguiente: construir prefabs y escenas.", "OK");
        }

        [MenuItem("King of Music/Setup/2 - Construir prefabs y escenas", priority = 11)]
        public static void BuildContentMenu()
        {
            BuildContent();
            EditorUtility.DisplayDialog("King of Music", "Prefabs, controllers y escenas generados.", "OK");
        }

        [MenuItem("King of Music/Setup/Construir prefabs y escenas", priority = 111)]
        public static void BuildContentAgent()
        {
            BuildContent();
        }

        [MenuItem("King of Music/Setup/3 - Validar (VERDICT)", priority = 12)]
        public static void ValidateMenu()
        {
            KomValidation.RunValidationMenu();
        }

        // Iteracion rapida de animacion: rehace SOLO el controller de cada personaje
        // usando los clips del FBX (sub-assets binarios). Evita reimportar los modelos.
        [MenuItem("King of Music/Setup/5 - Rehacer controllers", priority = 13)]
        public static void RefreshCrouchAndControllers()
        {
            RequireEditMode();
            AssetDatabase.Refresh();
            foreach (var def in KomData.Characters.characters)
            {
                if (def == null) continue;
                RefreshCharacter(def);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[KOM] Controllers regenerados desde los clips del FBX.");
        }

        // Re-hornea UN personaje: iterar animacion sin pagar los 7 (~13 min).
        // Reconstruye el PREFAB desde el modelo (mallas incluidas) y luego rehornea.
        // RefreshCharacterById NO sirve para esto: reusa el prefab existente y solo
        // resiembra los huesos, asi que un cambio en la MALLA (p. ej. acortar brazos en
        // el FBX del primer commit) no se aplica nunca.
        public static string RebuildCharacterById(string id)
        {
            RequireEditMode();
            AssetDatabase.Refresh();
            var def = KomData.GetCharacter(id);
            if (def == null) return "ERROR: personaje desconocido " + id;
            BuildCharacter(def);
            RefreshCharacter(def);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return "rebuilt " + id;
        }

        public static string RefreshCharacterById(string id)
        {
            RequireEditMode();
            AssetDatabase.Refresh();
            var def = KomData.GetCharacter(id);
            if (def == null) return "ERROR: personaje desconocido " + id;
            RefreshCharacter(def);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return "rebaked " + id;
        }

        static void RefreshCharacter(CharacterDef def)
        {
            // Walk/WalkAlt eliminados del juego: borrar sus copias clean antiguas.
            AssetDatabase.DeleteAsset($"Assets/KOM/Animation/Clips/{def.id}_Walk.anim");
            AssetDatabase.DeleteAsset($"Assets/KOM/Animation/Clips/{def.id}_WalkAlt.anim");
            string prefabPath = $"{CharacterPrefabDir}/{def.id}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { Debug.LogError($"[KOM] Falta {prefabPath}"); return; }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterModelPath(def.id));
            var raw = LoadFbxClips(def.id);
            if (model == null || raw.Count == 0) { Debug.LogError($"[KOM] {def.id}: el FBX no expone clips."); return; }
            var clips = BuildCleanClips(def, raw, model);
            ApplyFirstRawClips(clips, def.id);
            var go = UnityEngine.Object.Instantiate(prefab);
            try
            {
                // El bind del prefab puede estar posado por una corrida anterior: el
                // horneado captura el bind DEL PREFAB y lo vuelve a guardar, asi que
                // cualquier desvio se hereda y se agrava. Se resiembra desde el FBX
                // (fuente de verdad) antes de hornear.
                int reset = ResetBindFromModel(go, model);
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator == null) animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = null;
                var baked = KomInPlaceBaker.Bake(go, def.id, clips, RawDerivedStates(def.id));
                var controller = CreateFighterController(def, baked.Clips, baked.NaturalSpeeds);
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                int deviated = CountBindDeviations(go, model);
                if (deviated > 0)
                    Debug.LogError($"[KOM] {def.id}: el prefab quedo con {deviated} huesos fuera del bind del FBX tras hornear.");
                else
                    Debug.Log($"[KOM] {def.id}: bind resembrado ({reset} huesos) y verificado.");
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // Copia el TRS local del FBX a la instancia del prefab, hueso por hueso (por
        // ruta, no por nombre: hay nombres repetidos entre cadenas ORG y DEF).
        internal static int ResetBindFromModel(GameObject instance, GameObject modelAsset)
        {
            if (instance == null || modelAsset == null) return 0;
            var source = new Dictionary<string, Transform>();
            foreach (var t in modelAsset.GetComponentsInChildren<Transform>(true))
                source[AnimationUtility.CalculateTransformPath(t, modelAsset.transform)] = t;

            int count = 0;
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(t, instance.transform);
                if (!source.TryGetValue(path, out var s) || s == null) continue;
                t.localPosition = s.localPosition;
                t.localRotation = s.localRotation;
                t.localScale = s.localScale;
                count++;
            }
            return count;
        }

        internal static int CountBindDeviations(GameObject instance, GameObject modelAsset)
        {
            if (instance == null || modelAsset == null) return 0;
            var source = new Dictionary<string, Transform>();
            foreach (var t in modelAsset.GetComponentsInChildren<Transform>(true))
                source[AnimationUtility.CalculateTransformPath(t, modelAsset.transform)] = t;

            int bad = 0;
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(t, instance.transform);
                if (!source.TryGetValue(path, out var s) || s == null) continue;
                if (Quaternion.Angle(t.localRotation, s.localRotation) > 0.5f) bad++;
                else if (Vector3.Distance(t.localPosition, s.localPosition) > 0.002f) bad++;
            }
            return bad;
        }

        // Los clips binarios del FBX son la fuente unica. Las copias .anim se generan
        // COMPACTAS: sin curvas de escala, sin curvas constantes iguales al bind pose y
        // con keyframes redundantes eliminados. Antes se copiaban 32 clips completos por
        // personaje (~3 GB) y el primer Instantiate tardaba 10 s (el freeze reportado).
        static Dictionary<string, AnimationClip> LoadFbxClips(string id)
        {
            string fbxPath = $"{CharacterArtDir}/{id}.fbx";
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>())
            {
                if (clip == null || clip.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                clips[clip.name] = clip;
            }
            return clips;
        }

        // Clips RAW del FBX de la primera version, indexados por nombre de estado.
        // El primer commit no tenia la deformacion de cabeza; su retarget es la referencia.
        static Dictionary<string, AnimationClip> LoadFirstRawClips(string id)
        {
            var clips = new Dictionary<string, AnimationClip>();
            string fbxPath = $"{FirstCommitDir}/{id}.fbx";
            if (!File.Exists(fbxPath)) return clips;
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>())
            {
                if (clip == null || clip.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                string state = CleanTakeName(clip.name);
                clips[state] = clip;
            }
            return clips;
        }

        // Sustituye los estados comunes (los que existian en la primera version) por sus
        // clips RAW originales. Los estados nuevos (Crouch, WalkGuard, Block, GroinHit,
        // QuadPunch) no existen alli y conservan el pipeline actual. La guardia quieta
        // de la primera version era Boxing: se usa para Block.
        internal static readonly string[] FirstRawStates =
        {
            "Idle", "Run", "Jump", "Boxing",
            "LightPunch", "HeavyPunch", "Uppercut", "LightKick", "HeavyKick", "DropKick",
            "Hit", "HitHeavy", "Stunned", "KO", "GetUp"
        };

        // Estados cuyo material de origen conserva facial y escala del primer commit.
        // WalkGuard NO esta aqui: usa el clip clean de camianrcongaurdia (cara congelada).
        //
        // La toma de la CELEBRACION se suma solo si de verdad viene del FBX del primer
        // commit (ver VictoryClipName): es la unica forma de que el festejo conserve la
        // cara y la escala originales. Si se conservara la del retarget actual, se
        // hornean las curvas faciales deformadas que el pipeline congela a proposito.
        internal static HashSet<string> RawDerivedStates(string id = null)
        {
            var set = new HashSet<string>(FirstRawStates) { "Block", "WalkGuard" };
            if (!string.IsNullOrEmpty(id))
            {
                string victory = VictoryClipName(KomData.GetCharacter(id));
                if (FirstCommitHas(id, victory)) set.Add(victory);
            }
            return set;
        }

        // Tomas que existen en el FBX del primer commit, por personaje. Se cachea porque
        // cargar el FBX entero para leer los nombres no es gratis.
        static readonly Dictionary<string, HashSet<string>> FirstTakes =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        internal static bool FirstCommitHas(string id, string take)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(take)) return false;
            if (!FirstTakes.TryGetValue(id, out var takes))
            {
                takes = new HashSet<string>(StringComparer.Ordinal);
                string path = $"{FirstCommitDir}/{id}.fbx";
                if (File.Exists(path))
                {
                    foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                    {
                        if (clip == null) continue;
                        if (clip.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                        takes.Add(CleanTakeName(clip.name));
                    }
                }
                FirstTakes[id] = takes;
            }
            return takes.Contains(take);
        }

        static void ApplyFirstRawClips(Dictionary<string, AnimationClip> clips, string id)
        {
            var first = LoadFirstRawClips(id);
            if (first.Count == 0)
            {
                Debug.LogWarning($"[KOM] {id}: no hay FBX del primer commit; se usan los clips actuales.");
                return;
            }
            foreach (var state in FirstRawStates)
                if (first.TryGetValue(state, out var clip) && clip != null) clips[state] = clip;
            if (first.TryGetValue("Boxing", out var guard) && guard != null) clips["Block"] = guard;

            // La celebracion tambien se sustituye por su toma original. `Victory` no es un
            // estado con clip propio: cada personaje celebra con una toma distinta, y si
            // esa toma existe en el primer commit se usa TAL CUAL (cuerpo, cara y escala).
            var victoryTake = VictoryClipName(KomData.GetCharacter(id));
            if (first.TryGetValue(victoryTake, out var victoryClip) && victoryClip != null)
                clips[victoryTake] = victoryClip;

            // WalkGuard ya viene con su cuerpo, piernas, guardia y facial del FBX actual.
            // No se compone ni se injerta nada: cualquier mezcla de cabeza entre clips
            // desincroniza las cadenas ORG/DEF y estira la cara (bug recurrente).
        }

        [MenuItem("King of Music/Setup/6 - Configurar FBX del primer commit", priority = 14)]
        public static void ConfigureFirstCommitMenu()
        {
            ConfigureFirstCommitModels();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void ConfigureFirstCommitModels()
        {
            if (!Directory.Exists(FirstCommitDir)) return;
            int configured = 0;
            foreach (var file in Directory.GetFiles(FirstCommitDir, "*.fbx", SearchOption.TopDirectoryOnly))
            {
                string path = file.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;
                // Ajustes de la primera version: compresion por reduccion de keys y
                // conservando curvas de escala (las que mantienen la cara en su sitio).
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.importAnimation = true;
                importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
                importer.resampleCurves = true;
                importer.removeConstantScaleCurves = false;
                importer.importBlendShapes = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.addCollider = false;
                var firstClips = importer.defaultClipAnimations;
                if (firstClips != null && firstClips.Length > 0)
                {
                    foreach (var firstClip in firstClips)
                    {
                        string clean = CleanTakeName(firstClip.takeName);
                        firstClip.name = clean;
                        firstClip.loopTime = LoopStates.Contains(clean);
                    }
                    importer.clipAnimations = firstClips;
                }
                importer.SaveAndReimport();
                configured++;
            }
            Debug.Log($"[KOM] FBX del primer commit configurados: {configured}");
        }

        internal struct BindDefault
        {
            public Vector3 position;
            public Quaternion rotation;
        }

        internal static Dictionary<string, BindDefault> LoadBindDefaults(GameObject model)
        {
            var defaults = new Dictionary<string, BindDefault>();
            if (model == null) return defaults;
            var animator = model.GetComponentInChildren<Animator>(true);
            var root = animator != null ? animator.transform : model.transform;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                string path = AnimationUtility.CalculateTransformPath(t, root);
                if (string.IsNullOrEmpty(path)) continue;
                defaults[path] = new BindDefault { position = t.localPosition, rotation = t.localRotation };
            }
            return defaults;
        }

        // Solo importan los huesos con peso en la malla y sus ancestros. Los controles
        // Rigify sin peso no afectan al skinning y sus curvas solo engordan el clip.
        internal static HashSet<string> BuildRelevantPaths(GameObject model)
        {
            var paths = new HashSet<string>();
            if (model == null) return paths;
            var animator = model.GetComponentInChildren<Animator>(true);
            var root = animator != null ? animator.transform : model.transform;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null || smr.bones == null || smr.bones.Length == 0) continue;
                var used = new bool[smr.bones.Length];
                foreach (var w in mesh.boneWeights)
                {
                    AddBoneUse(used, w.weight0, w.boneIndex0);
                    AddBoneUse(used, w.weight1, w.boneIndex1);
                    AddBoneUse(used, w.weight2, w.boneIndex2);
                    AddBoneUse(used, w.weight3, w.boneIndex3);
                }
                for (int i = 0; i < smr.bones.Length; i++)
                {
                    if (!used[i] || smr.bones[i] == null) continue;
                    for (var t = smr.bones[i]; t != null && t != root; t = t.parent)
                        paths.Add(AnimationUtility.CalculateTransformPath(t, root));
                }
            }
            // La cadena de control (torso/root/rig) se conserva SIEMPRE: la camara de
            // verificacion y la jerarquia la usan, aunque no tenga pesos directos.
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                string n = t.name;
                if (n == "torso" || n == "root" || n == "rig")
                    paths.Add(AnimationUtility.CalculateTransformPath(t, root));
            }
            return paths;
        }

        static void AddBoneUse(bool[] used, float weight, int index)
        {
            if (weight > 0.001f && index >= 0 && index < used.Length) used[index] = true;
        }

        // Recortes por estado, en frames del clip de origen (1-based). El especial de Jose
        // corta la interpretacion en el frame 50: ahi el brazo izquierdo termina de abrirse
        // y es el instante en que lanza la onda. Lo que viene despues en la toma son mas
        // gestos de escenario que alargan el ataque sin aportar nada.
        internal static readonly Dictionary<string, int> StateTrimFrames = new Dictionary<string, int>
        {
            { "Singing", 50 }
        };

        // Tomas de origen alternativas por ESTADO (mecanismo disponible, sin usar).
        // El guitarrazo de Alex Llora DEBE usar "Two Hand Club Combo": es su animacion
        // disenada, un golpe a dos manos con el instrumento empunado por el mando como si
        // fuera una espada. Su giro de cuerpo es parte del gesto, no un defecto: se acepta
        // y se verifica que los pies sigan al cuerpo (KomSpecialAudit.Gate). Solo se
        // rellenaria si un clip futuro tuviera que venir de otra toma del mismo FBX.
        internal static readonly Dictionary<string, string> StateSourceTake = new Dictionary<string, string>();

        static Dictionary<string, AnimationClip> BuildCleanClips(CharacterDef def, Dictionary<string, AnimationClip> raw,
            GameObject model)
        {
            foreach (var pair in StateSourceTake)
            {
                if (raw.TryGetValue(pair.Value, out var source) && source != null)
                    raw[pair.Key] = source;
            }
            EnsureFolder("Assets/KOM/Animation/Clips");
            var defaults = LoadBindDefaults(model);
            var relevant = BuildRelevantPaths(model);
            var needed = new HashSet<string>
            {
                "Idle", "Run", "Boxing", "Jump", "Hit", "HitHeavy",
                "GroinHit", "Stunned", "KO", "GetUp", "Crouch", "Block",
                "LightPunch", "HeavyPunch", "LightKick", "HeavyKick", "Uppercut", "DropKick", "QuadPunch"
            };
            foreach (var spec in Attacks.All) needed.Add(spec.animatorState);
            // Especiales por personaje: se hornea un clip por CADA especial declarado
            // (la jefa final acumula varios poderes y necesita todos los estados).
            if (def != null)
            {
                for (int i = 0; i < def.SpecialCount; i++)
                {
                    var sp = def.GetSpecial(i);
                    if (sp != null && sp.IsCombatSpecial && !string.IsNullOrEmpty(sp.animatorState))
                        needed.Add(sp.animatorState);
                }
            }
            var victory = VictoryClip(def, raw);
            if (victory != null) needed.Add(victory.name);

            var clean = new Dictionary<string, AnimationClip>(needed.Count);
            foreach (var name in needed)
            {
                if (!raw.TryGetValue(name, out var source) || source == null) continue;
                var clip = CreateOptimizedClip(def.id, name, source, defaults, relevant);
                if (clip != null) clean[name] = clip;
            }
            // WalkGuard = clip RAW real camianrcongaurdia del FBX actual, SIN injertos ni
            // composicion. Su subrig facial ya es rigido (las distancias cara-craneo no
            // cambian en 100 fases) y sus cadenas ORG/DEF se mueven coherentes.
            // KomInPlaceBaker lo hornea in-place conservando facial y escala exactos.
            if (raw.TryGetValue("WalkGuard", out var guardRaw) && guardRaw != null)
                clean["WalkGuard"] = guardRaw;

            // SIN FBX DEL PRIMER COMMIT (Natanael, cuyo rig se rehizo): ApplyFirstRawClips
            // no puede sustituir los estados raw y se quedaban como copia clean podada
            // (70 de 279 huesos, 0 curvas faciales, 0 de escala) mientras los otros seis
            // usan la toma EXACTA. Con writeDefaultValues=false el Animator no resetea esos
            // huesos, y su malla es la unica del plantel que lleva el pelo pesado al subrig
            // facial (613 verts en DEF-spine.006 frente a 1162 de Alex): el pelo se queda en
            // la pose del estado anterior y la cabeza se ve deformada. Se copian EXACTOS del
            // FBX actual, igual que WalkGuard.
            if (!File.Exists($"{FirstCommitDir}/{def.id}.fbx"))
            {
                foreach (var st in FirstRawStates)
                    if (raw.TryGetValue(st, out var take) && take != null) clean[st] = take;
                if (raw.TryGetValue("Boxing", out var box) && box != null) clean["Block"] = box;
            }
            return clean;
        }

        // Completa la pose del clip: todo hueso con peso (y sus ancestros) recibe las 4
        // curvas de rotacion y las 3 de posicion, aunque la toma de origen no las traiga.
        // Motivo: los estados llevan writeDefaultValues=false, asi que el Animator NO
        // resetea lo que el clip no anima. Un hueso sin curvas se queda con la pose del
        // estado anterior y la malla se estira entre dos poses. Ese es el bug que dejo la
        // cabeza de Natanael y los tres combos deformados: clips de 71 de 279 huesos con
        // 0 curvas faciales. Un estado tiene que ser la unica autoridad de la pose.
        internal static void CompletePose(AnimationClip clip, float length, Dictionary<string, BindDefault> defaults,
            HashSet<string> relevant, HashSet<string> written)
        {
            if (clip == null || defaults == null || relevant == null) return;
            // CORTAFUEGOS: se leen las rutas que el clip YA tiene escritas directamente del clip.
            // No basta con confiar en `written` (una lista que construye quien llama): si llega
            // vacia o incompleta, esto escribia constantes ENCIMA de las curvas animadas y dejaba
            // el clip entero congelado. Una ruta con curvas no se toca nunca.
            var existing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
                if (!string.IsNullOrEmpty(b.path)) existing.Add(b.path);
            int filled = 0;
            foreach (var path in relevant)
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (existing.Contains(path)) continue;
                if (written != null && written.Contains(path)) continue;
                if (!defaults.TryGetValue(path, out var d)) continue;
                WriteConstant(clip, path, "m_LocalPosition", d.position, length);
                WriteConstant(clip, path, "m_LocalRotation", d.rotation, length);
                existing.Add(path);
                written?.Add(path);
                filled++;
            }
            if (filled > 0) clip.EnsureQuaternionContinuity();
        }

        static void WriteConstant(AnimationClip clip, string path, string prop, Vector3 v, float length)
        {
            // Curva de 2 keys con el MISMO valor: es plana con cualquier tangente, asi que no
            // se tocan los modos de tangente (SetKeyLeft/RightTangentMode son de las llamadas
            // mas caras del editor y dominaban el tiempo de rehorneado).
            for (int i = 0; i < 3; i++)
            {
                float value = i == 0 ? v.x : i == 1 ? v.y : v.z;
                var b = EditorCurveBinding.FloatCurve(path, typeof(Transform), prop + (i == 0 ? ".x" : i == 1 ? ".y" : ".z"));
                AnimationUtility.SetEditorCurve(clip, b,
                    new AnimationCurve(new Keyframe(0f, value), new Keyframe(length, value)));
            }
        }

        static void WriteConstant(AnimationClip clip, string path, string prop, Quaternion q, float length)
        {
            float[] v = { q.x, q.y, q.z, q.w };
            string[] s = { ".x", ".y", ".z", ".w" };
            for (int i = 0; i < 4; i++)
            {
                var b = EditorCurveBinding.FloatCurve(path, typeof(Transform), prop + s[i]);
                AnimationUtility.SetEditorCurve(clip, b,
                    new AnimationCurve(new Keyframe(0f, v[i]), new Keyframe(length, v[i])));
            }
        }

        // El subrig facial se congela ENTERO en bind al hornear los clips: la cara no
        // usa expresiones y el retarget de Mixamo la deforma. La cabeza sigue girando
        // por ORG-spine.006. El resto del cuerpo se compacta con el filtro de relevancia.
        static AnimationClip CreateOptimizedClip(string id, string state, AnimationClip source,
            Dictionary<string, BindDefault> defaults, HashSet<string> relevant)
        {
            bool useRelevantFilter = relevant != null && relevant.Count > 0;
            if (useRelevantFilter)
            {
                int matched = 0;
                foreach (var b in AnimationUtility.GetCurveBindings(source))
                    if (relevant.Contains(b.path)) matched++;
                // Si los paths no coinciden con el modelo, no filtrar (evita T-pose).
                useRelevantFilter = matched >= 10;
            }
            string path = $"Assets/KOM/Animation/Clips/{id}_{state}.anim";
            AssetDatabase.DeleteAsset(path);
            bool loop = LoopStates.Contains(source.name);
            var clip = new AnimationClip
            {
                name = $"{id}_{state}",
                frameRate = source.frameRate,
                wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever
            };

            // Las curvas de un cuaternion (x,y,z,w) son UNA sola rotacion: no se puede
            // descartar un componente y dejar los otros. Si falta w, Unity combina los
            // x/y/z animados con el w que tenga el hueso en ese momento y el hueso gira a
            // cualquier sitio (era la causa de las piernas destrozadas en los especiales).
            // Igual con la posicion (x,y,z). Por eso se agrupan y se decide por GRUPO.
            var groups = new Dictionary<string, CurveGroup>();
            var order = new List<string>();
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                // Las curvas de escala se eliminan SIEMPRE: son las que deforman la malla.
                if (binding.propertyName.StartsWith("m_LocalScale", StringComparison.Ordinal)) continue;
                if (IsFacialBonePath(binding.path)) continue;
                if (useRelevantFilter && !relevant.Contains(binding.path)) continue;
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null || curve.length == 0) continue;

                string kind = CurveKind(binding.propertyName);
                string key = binding.path + "|" + kind;
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new CurveGroup { path = binding.path, kind = kind };
                    groups[key] = group;
                    order.Add(key);
                }
                group.items.Add((binding, curve));
            }

            foreach (var key in order)
            {
                var group = groups[key];
                bool allConstantAtBind = group.items.Count > 0;
                foreach (var item in group.items)
                {
                    bool constant = IsConstant(item.curve);
                    if (!constant || !IsBindPoseConstant(item.binding, item.curve, defaults))
                    {
                        allConstantAtBind = false;
                        break;
                    }
                }
                // Solo se descarta el grupo completo si TODOS sus componentes son
                // constantes e iguales al bind. Un grupo parcial se escribe entero.
                // OJO: NO se descartan los constantes en bind. Con writeDefaultValues=false
                // el Animator no resetea lo que el clip no anima: un hueso sin curvas se queda
                // en la pose del estado anterior y la malla se estira entre dos poses. Ese fue
                // el bug de los combos y el de Natanael (clip parcial -> pelo/cara anclados).
                if (allConstantAtBind)
                {
                    foreach (var item in group.items)
                    {
                        var flat = new AnimationCurve(new Keyframe(0f, item.curve.keys[0].value),
                                                     new Keyframe(source.length, item.curve.keys[0].value));
                        AnimationUtility.SetEditorCurve(clip, item.binding, flat);
                        written.Add(item.binding.path);
                    }
                    continue;
                }

                foreach (var item in group.items)
                {
                    // LAS POSICIONES NO SE SIMPLIFICAN. La tolerancia es en unidades de
                    // OBJETO y este rig tiene el armature a escala 100: 0.0005 unidades son
                    // 5 cm de mundo. Medido en `torso` de Combo1, la curva del FBX tiene 53
                    // keys y el simplificador la dejaba en 2 con rango 0 en X y Z: el tren
                    // superior dejaba de balancearse y se estiraba contra la pelvis. Eso es
                    // la deformacion "gelatinosa" que se ve solo al reproducir.
                    // Las posiciones tienen pocas keys (maximo ~50) y no compensa tocarlas.
                    bool isPosition = group.kind == "pos";
                    var optimized = IsConstant(item.curve)
                        ? new AnimationCurve(new Keyframe(0f, item.curve.keys[0].value),
                                             new Keyframe(source.length, item.curve.keys[0].value))
                        : isPosition ? item.curve : SimplifyCurve(item.curve, 0.0005f);
                    AnimationUtility.SetEditorCurve(clip, item.binding, optimized);
                    written.Add(item.binding.path);
                }
            }
            // Completa la pose: el subrig facial se salta arriba (se congela) y algunos
            // huesos no traen curvas. Sin esto quedan sin animar y el Animator los deja en
            // la pose del estado anterior (writeDefaultValues=false) -> malla estirada.
            CompletePose(clip, source.length, defaults, relevant, written);
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            {
                var keys = AnimationUtility.GetObjectReferenceCurve(source, binding);
                if (keys != null) AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            }

            var settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.startTime = 0f;
            settings.stopTime = source.length;
            settings.loopTime = loop;
            settings.loopBlend = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // Agrupa componentes de una misma magnitud: una rotacion son 4 curvas y una
        // posicion son 3; descartar una sola deja la magnitud indefinida.
        sealed class CurveGroup
        {
            public string path;
            public string kind;
            public readonly List<(EditorCurveBinding binding, AnimationCurve curve)> items =
                new List<(EditorCurveBinding, AnimationCurve)>();
        }

        static string CurveKind(string propertyName)
        {
            if (propertyName.StartsWith("m_LocalRotation", StringComparison.Ordinal)) return "rot";
            if (propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal)) return "pos";
            if (propertyName.StartsWith("m_LocalScale", StringComparison.Ordinal)) return "scale";
            return propertyName;
        }

        // Todo lo que cuelga de ORG-face es subrig facial: frente, sienes, orejas,
        // mandibula, labios, ojos, cejas... Si se anima, deforma craneo y cara. Se congela.
        // Los prefijos cubren rigs donde el path no expone ORG-face.
        static readonly string[] FacialBonePrefixes =
        {
            "DEF-jaw", "DEF-cheek", "DEF-chin", "DEF-lip", "DEF-nose", "DEF-eye",
            "DEF-brow", "DEF-tongue", "DEF-teeth", "DEF-beard", "DEF-mustache", "DEF-eyelid",
            "DEF-forehead", "DEF-temple", "DEF-ear", "DEF-nostril", "DEF-pupil", "DEF-eyelash"
        };

        public static bool IsFacialBonePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.IndexOf("/ORG-face/", StringComparison.Ordinal) >= 0
                || path.EndsWith("/ORG-face", StringComparison.Ordinal)) return true;
            int slash = path.LastIndexOf('/');
            string bone = slash >= 0 ? path.Substring(slash + 1) : path;
            for (int i = 0; i < FacialBonePrefixes.Length; i++)
                if (bone.StartsWith(FacialBonePrefixes[i], StringComparison.Ordinal)) return true;
            return false;
        }

        internal static bool IsConstant(AnimationCurve curve)
        {
            var keys = curve.keys;
            if (keys.Length == 0) return true;
            float v = keys[0].value;
            for (int i = 1; i < keys.Length; i++)
                if (Mathf.Abs(keys[i].value - v) > 0.000001f) return false;
            return true;
        }

        internal static bool IsBindPoseConstant(EditorCurveBinding binding, AnimationCurve curve, Dictionary<string, BindDefault> defaults)
        {
            if (!defaults.TryGetValue(binding.path, out var def)) return false;
            float v = curve.keys[0].value;
            switch (binding.propertyName)
            {
                case "m_LocalPosition.x": return Mathf.Abs(v - def.position.x) < 0.0001f;
                case "m_LocalPosition.y": return Mathf.Abs(v - def.position.y) < 0.0001f;
                case "m_LocalPosition.z": return Mathf.Abs(v - def.position.z) < 0.0001f;
                case "m_LocalRotation.x": return Mathf.Abs(v - def.rotation.x) < 0.0001f;
                case "m_LocalRotation.y": return Mathf.Abs(v - def.rotation.y) < 0.0001f;
                case "m_LocalRotation.z": return Mathf.Abs(v - def.rotation.z) < 0.0001f;
                case "m_LocalRotation.w": return Mathf.Abs(v - def.rotation.w) < 0.0001f;
                default: return false;
            }
        }

        // Elimina keys que se pueden reconstruir linealmente dentro de la tolerancia.
        internal static AnimationCurve SimplifyCurve(AnimationCurve curve, float tolerance)
        {
            var keys = curve.keys;
            if (keys.Length <= 2) return curve;
            var kept = new List<Keyframe> { keys[0] };
            int anchor = 0;
            for (int i = 2; i < keys.Length; i++)
            {
                bool linear = true;
                for (int j = anchor + 1; j < i; j++)
                {
                    float t = Mathf.InverseLerp(keys[anchor].time, keys[i].time, keys[j].time);
                    if (Mathf.Abs(keys[j].value - Mathf.Lerp(keys[anchor].value, keys[i].value, t)) > tolerance)
                    {
                        linear = false;
                        break;
                    }
                }
                if (linear) continue;
                kept.Add(keys[i - 1]);
                anchor = i - 1;
            }
            kept.Add(keys[keys.Length - 1]);
            var result = new AnimationCurve(kept.ToArray());
            // Tangentes LINEALES: con keys reducidas, SmoothTangents produce overshoot
            // entre keys (huesos "flotando" y quaternions no unitarios => malla deforme).
            for (int i = 0; i < result.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(result, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(result, i, AnimationUtility.TangentMode.Linear);
            }
            return result;
        }

        [MenuItem("King of Music/Setup/Construir todo", priority = 1)]
        public static void BuildAll()
        {
            ConfigureImports();
            BuildContent();
            KomValidation.RunValidation();
            Debug.Log("[KOM] SETUP COMPLETO");
        }

        public static void ConfigureImports()
        {
            RequireEditMode();
            EnsureFolder(CharacterPrefabDir);
            EnsureFolder(ControllerDir);
            EnsureFolder(MaterialDir);
            EnsureFolder(SceneDir);

            ConfigureUiTextures();
            ConfigureModels();
            ConfigureFirstCommitModels();
            ConfigureCharacterTextures();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[KOM] Importacion configurada.");
        }

        static void ConfigureUiTextures()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/UI" });
            int configured = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                bool isMap = path.Contains("/Map/");
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = isMap ? 2048 : 1024;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
                configured++;
            }
            Debug.Log($"[KOM] Texturas UI configuradas: {configured}");
        }

        static void ConfigureCharacterTextures()
        {
            if (!Directory.Exists(CharacterArtDir)) return;
            int configured = 0;
            foreach (var fbm in Directory.GetDirectories(CharacterArtDir, "*.fbm", SearchOption.TopDirectoryOnly))
            {
                foreach (var file in Directory.GetFiles(fbm, "*.png", SearchOption.TopDirectoryOnly))
                {
                    string path = file.Replace('\\', '/');
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null) continue;
                    string lower = path.ToLowerInvariant();
                    bool normal = lower.Contains("normal");
                    bool data = normal || lower.Contains("metallic") || lower.Contains("roughness");
                    importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    importer.maxTextureSize = 512;
                    importer.mipmapEnabled = true;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.sRGBTexture = !data;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.SaveAndReimport();
                    configured++;
                }
            }
            Debug.Log($"[KOM] Texturas de personajes configuradas: {configured}");
        }

        static void ConfigureModels()
        {
            var fbxPaths = Directory.GetFiles(CharacterArtDir, "*.fbx", SearchOption.TopDirectoryOnly)
                .Select(p => p.Replace('\\', '/'))
                .OrderBy(p => p)
                .ToArray();
            int skipped = 0;
            int index = 0;
            foreach (var path in fbxPaths)
            {
                index++;
                EditorUtility.DisplayProgressBar("King of Music · importacion",
                    $"{Path.GetFileName(path)}  ({index}/{fbxPaths.Length})", index / (float)fbxPaths.Length);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    Debug.LogError($"[KOM] No se pudo leer el importer de {path}");
                    continue;
                }

                // Si el FBX ya esta configurado, NO se reimporta: reimportar los 7 modelos
                // con compresion off cuesta ~10 minutos y "Construir todo" lo hacia siempre.
                if (IsModelConfigured(importer) && ClipsMatch(importer, path))
                {
                    skipped++;
                    continue;
                }

                importer.importAnimation = true;
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.importCameras = false;
                importer.importLights = false;
                importer.importBlendShapes = false;
                importer.importVisibility = false;
                importer.addCollider = false;
                importer.optimizeMeshPolygons = true;
                importer.optimizeMeshVertices = true;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                // COMPRESION: KeyframeReduction, NO Off.
                //
                // El FBX de Blender trae las rotaciones autoradas en EULER. Con
                // `animationCompression = Off`, Unity guarda las curvas tal cual
                // (`localEulerAnglesRaw.x/y/z`) y NO genera `m_LocalRotation`. Toda la
                // tuberia (`CreateOptimizedClip`, `BuildClip`, `CompletePose`) trabaja con
                // `m_LocalRotation`, asi que los clips `_Clean` salian con **0 curvas de
                // rotacion en el cuerpo** (medido: 88 faciales y 106 rutas de cuerpo sin
                // rotacion). Con writeDefaultValues=false esos huesos heredan la pose del
                // estado anterior y el personaje se convierte en una masa deforme.
                //
                // Con KeyframeReduction Unity resamplea a cuaterniones (medido: 1116 curvas
                // de rotacion, 0 Euler) y la tuberia funciona. Es lo que usaba la primera
                // version del juego.
                importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
                importer.resampleCurves = true;
                importer.removeConstantScaleCurves = true;
                // PESOS DE SKINNING: el FBX trae hasta 12 influencias por vertice (medido en
                // Blender con tools/blender). Con el tope por defecto de Unity (4) el resto se
                // tira y se RENORMALIZA: la malla se deforma al articular y no se parece a lo
                // que se ve en Blender. Se nota poco en Idle y muchisimo en los combos, que son
                // las poses mas extremas. Hay que subirlo ANTES de extraer la malla a asset.
                importer.skinWeights = ModelImporterSkinWeights.Custom;
                importer.maxBonesPerVertex = 32;

                var clips = importer.defaultClipAnimations;
                if (clips != null && clips.Length > 0)
                {
                    foreach (var clip in clips)
                    {
                        string clean = CleanTakeName(clip.takeName);
                        clip.name = clean;
                        clip.loopTime = LoopStates.Contains(clean);
                        clip.loopPose = clip.loopTime;
                        if (StateTrimFrames.TryGetValue(clean, out int lastFrame))
                        {
                            clip.firstFrame = 1f;
                            clip.lastFrame = lastFrame;
                            Debug.Log($"[KOM] {Path.GetFileName(path)}: '{clean}' recortado a frames 1-{lastFrame}");
                        }
                    }
                    importer.clipAnimations = clips;
                }
                else
                {
                    Debug.LogWarning($"[KOM] {Path.GetFileName(path)} no expone clips en defaultClipAnimations.");
                }
                importer.SaveAndReimport();
                var importedClips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__"))
                    .Select(c => c.name)
                    .OrderBy(n => n);
                Debug.Log($"[KOM] {Path.GetFileName(path)} clips: {string.Join(", ", importedClips)}");
            }
            EditorUtility.ClearProgressBar();
            if (skipped > 0) Debug.Log($"[KOM] {skipped} FBX ya estaban configurados: no se reimportaron.");
        }

        // Comprueba los ajustes que de verdad afectan a la animacion/skinning.
        static bool IsModelConfigured(ModelImporter importer)
        {
            return importer.importAnimation
                && importer.animationType == ModelImporterAnimationType.Generic
                && !importer.importCameras
                && !importer.importLights
                && !importer.importBlendShapes
                && !importer.addCollider
                && importer.meshCompression == ModelImporterMeshCompression.Off
                // La comprobacion de compresion era IMPOSIBLE: aqui estaba a la vez
                // `== Off` y `== KeyframeReduction`, asi que IsModelConfigured devolvía siempre
                // false y "Construir todo" reimportaba los 7 FBX (~110 MB cada uno) en cada
                // pasada. El valor real es el que pone ConfigureModels: KeyframeReduction.
                && importer.animationCompression == ModelImporterAnimationCompression.KeyframeReduction
                && importer.resampleCurves
                && importer.removeConstantScaleCurves
                // Si el tope de influencias no esta subido, el FBX se considera NO configurado
                // y se reimporta: si no, un proyecto viejo se queda con la malla deformada.
                && importer.maxBonesPerVertex >= 32;
        }

        // Comprueba que las tomas ya esten renombradas, recortadas y con el loop correcto.
        static bool ClipsMatch(ModelImporter importer, string path)
        {
            // Ojo con la fuente: `defaultClipAnimations` devuelve los takes RAW ("Mixamo|Standing
            // 2H Magic Attack 01"), asi que compararlos contra CleanTakeName hacia esta
            // comprobacion IMPOSIBLE y el FBX se reimportaba SIEMPRE, incluso bien configurado.
            // La lista que de verdad guarda el importer es `clipAnimations`.
            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return false;
            foreach (var clip in clips)
            {
                string clean = CleanTakeName(clip.takeName);
                if (clip.name != clean) return false;
                if (clip.loopTime != LoopStates.Contains(clean)) return false;
                // El recorte de frames tambien forma parte del estado configurado: si no se
                // comprobara, un take recién declarado en StateTrimFrames se saltaria el
                // reimportado y el clip seguiria entero.
                if (StateTrimFrames.TryGetValue(clean, out int lastFrame) &&
                    (!Mathf.Approximately(clip.firstFrame, 1f) ||
                     !Mathf.Approximately(clip.lastFrame, lastFrame)))
                    return false;
            }
            var imported = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Count(c => !c.name.StartsWith("__preview__"));
            return imported == clips.Length;
        }

        // El mesh/rig bueno es el del primer commit (la version que no deformaba
        // cabezas). El FBX actual se usa solo como fuente de los clips nuevos.
        static string CharacterModelPath(string id)
        {
            string first = $"{FirstCommitDir}/{id}.fbx";
            return File.Exists(first) ? first : $"{CharacterArtDir}/{id}.fbx";
        }

        internal static string CleanTakeName(string takeName)
        {
            if (string.IsNullOrEmpty(takeName)) return "Unnamed";
            int idx = takeName.LastIndexOf('|');
            string clean = idx >= 0 ? takeName.Substring(idx + 1) : takeName;
            clean = clean.Trim();
            return TakeMap.TryGetValue(clean, out var mapped) ? mapped : clean;
        }

        [MenuItem("King of Music/Setup/Abrir escena Main Menu", priority = 40)]
        public static void OpenMainMenu()
        {
            EditorSceneManager.OpenScene(SceneDir + "/" + KomScenes.MainMenu + ".unity");
        }

        // --- Introduccion narrativa ---------------------------------------------------
        // Las cuatro ilustraciones se importan como Sprite Single, sin mipmaps y con
        // compresion de alta calidad: se ven a pantalla completa y el dibujo a linea
        // castiga cualquier artefacto de bloque.
        const string StoryImageDir = "Assets/Resources/UI/Story";

        [MenuItem("King of Music/Setup/Configurar imagenes de la historia", priority = 15)]
        public static void ConfigureStoryImages()
        {
            RequireEditMode();
            if (!Directory.Exists(StoryImageDir))
            {
                Debug.LogError($"[KOM] Falta la carpeta {StoryImageDir}");
                return;
            }

            var files = Directory.GetFiles(StoryImageDir, "*.png", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f)
                .ToArray();
            foreach (var file in files)
            {
                string path = file.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    importer = AssetImporter.GetAtPath(path) as TextureImporter;
                }
                if (importer == null)
                {
                    Debug.LogWarning($"[KOM] No se pudo importar {path}");
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.maxTextureSize = 2048;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = false;
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteGenerateFallbackPhysicsShape = false;
                importer.SetTextureSettings(settings);

                importer.SaveAndReimport();
                Debug.Log($"[KOM] Imagen de historia lista: {path}");
            }
            AssetDatabase.Refresh();
        }

        // Crea SOLO la escena de la historia y actualiza el build. Evita correr el setup
        // completo (que rehornea los siete personajes) cuando solo cambia esta pantalla.
        [MenuItem("King of Music/Setup/Crear escena Historia", priority = 112)]
        public static void BuildStorySceneOnly()
        {
            RequireEditMode();
            EnsureFolder(SceneDir);

            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty) EditorSceneManager.SaveScene(active);

            CreateScene(KomScenes.Story, typeof(StoryController));
            ConfigureBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            OpenMainMenu();
            Debug.Log("[KOM] Escena Story creada y build settings actualizados.");
        }

        // --- Audio ---------------------------------------------------------------------
        // Una sola autoridad de importacion: `KomAssetImport`, que ademas fija
        // `normalize = false` (obligatorio: la nivelacion por RMS la trae el WAV). Este
        // menu se conserva por compatibilidad y delega; tener dos rutas que configuran lo
        // mismo es como se acaba con la mitad de los clips sin normalizar.
        [MenuItem("King of Music/Setup/Configurar audio", priority = 16)]
        public static void ConfigureAudio()
        {
            RequireEditMode();
            int count = KomAssetImport.ApplyAudio();
            AssetDatabase.Refresh();
            Debug.Log($"[KOM] Audio configurado: {count} clips.");
        }

        public static void BuildContent()
        {
            RequireEditMode();
            EnsureFolder(CharacterPrefabDir);
            EnsureFolder(ControllerDir);
            EnsureFolder(MaterialDir);
            EnsureFolder(SceneDir);
            AssetDatabase.Refresh();

            var defs = KomData.Characters.characters.Where(d => d != null).ToArray();
            for (int i = 0; i < defs.Length; i++)
            {
                EditorUtility.DisplayProgressBar("King of Music · contenido",
                    $"Personaje {i + 1}/{defs.Length}: {defs[i].displayName}", (i + 0.05f) / defs.Length);
                BuildCharacter(defs[i]);
            }
            EditorUtility.DisplayProgressBar("King of Music · contenido", "Escenas y build settings", 0.95f);

            // La locomocion usa el WalkGuard real (camianrcongaurdia), no un compuesto.
            RefreshCrouchAndControllers();

            BuildScenes();
            BuildPreviewScene();
            ConfigureBuildSettings();
            ConfigurePlayerSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();
            Debug.Log("[KOM] Prefabs y escenas construidos.");
        }


        static void BuildCharacter(CharacterDef def)
        {
            string fbxPath = $"{CharacterArtDir}/{def.id}.fbx";
            if (!File.Exists(fbxPath))
            {
                Debug.LogError($"[KOM] Falta el FBX {fbxPath}");
                return;
            }

            // Prefab/mesh desde el modelo bueno del primer commit; los clips nuevos
            // siguen viniendo del FBX actual.
            string modelPath = CharacterModelPath(def.id);
            var sourceMaterials = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().ToList();
            var converted = new List<Material>();
            var sourceToConverted = new Dictionary<Material, Material>();
            for (int i = 0; i < sourceMaterials.Count; i++)
            {
                var src = sourceMaterials[i];
                if (src == null || src.shader == null) continue;
                if (src.name.StartsWith("__preview__")) continue;
                string matPath = $"{MaterialDir}/{def.id}_{Sanitize(src.name)}_{i}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                CopyToUrp(src, mat);
                EditorUtility.SetDirty(mat);
                converted.Add(mat);
                sourceToConverted[src] = mat;
            }
            if (converted.Count == 0)
            {
                var fallbackPath = $"{MaterialDir}/{def.id}_Fallback.mat";
                var fallback = AssetDatabase.LoadAssetAtPath<Material>(fallbackPath);
                if (fallback == null)
                {
                    fallback = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    AssetDatabase.CreateAsset(fallback, fallbackPath);
                }
                fallback.color = new Color(0.6f, 0.6f, 0.6f);
                converted.Add(fallback);
                Debug.LogWarning($"[KOM] {def.id}: sin materiales importados, se usa fallback.");
            }

            var clipAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .Where(c => c.name.StartsWith("__preview__") == false)
                .OrderBy(c => c.name)
                .ToArray();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError($"[KOM] No se pudo cargar el modelo {modelPath}");
                return;
            }
            // Fuente unica: los clips binarios del FBX. Se generan copias COMPACTAS
            // (sin escala ni curvas muertas) para que el controller no cargue GB de texto.
            var clips = BuildCleanClips(def, LoadFbxClips(def.id), model);
            ApplyFirstRawClips(clips, def.id);

            var go = UnityEngine.Object.Instantiate(model);
            go.name = def.id;
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;

            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = go.AddComponent<Animator>();

            var baked = KomInPlaceBaker.Bake(go, def.id, clips, RawDerivedStates(def.id));
            var controller = CreateFighterController(def, baked.Clips, baked.NaturalSpeeds);

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src != null && sourceToConverted.TryGetValue(src, out var convertedMat))
                        mats[i] = convertedMat;
                    else if (mats.Length == 1 && converted.Count > 0)
                        mats[i] = converted[0];
                }
                renderer.sharedMaterials = mats;
            }

            // Mallas independientes: el prefab deja de referenciar el FBX (100 MB de
            // animaciones crudas). El FBX solo se usa en build-time para generar clips.
            int extractedMeshes = ExtractMeshesToAssets(def.id, go);

            var fighter = go.GetComponent<Fighter>();
            if (fighter == null) fighter = go.AddComponent<Fighter>();
            fighter.SetCharacterId(def.id);
            fighter.enabled = true;

            string prefabPath = $"{CharacterPrefabDir}/{def.id}.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            UnityEngine.Object.DestroyImmediate(go);
            Debug.Log($"[KOM] Prefab creado: {prefabPath} ({clipAssets.Length} clips, {converted.Count} materiales, {extractedMeshes} mallas, {controller.layers.Length} capas)");
        }

        static int ExtractMeshesToAssets(string id, GameObject go)
        {
            string dir = $"{CharacterPrefabDir}/Meshes";
            EnsureFolder(dir);
            int count = 0;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                string path = $"{dir}/{id}_skinned{count}.asset";
                AssetDatabase.DeleteAsset(path);
                var copy = UnityEngine.Object.Instantiate(smr.sharedMesh);
                copy.name = $"{id}_skinned{count}";
                AssetDatabase.CreateAsset(copy, path);
                smr.sharedMesh = copy;
                count++;
            }
            int staticIndex = 0;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                string path = $"{dir}/{id}_static{staticIndex}.asset";
                AssetDatabase.DeleteAsset(path);
                var copy = UnityEngine.Object.Instantiate(mf.sharedMesh);
                copy.name = $"{id}_static{staticIndex}";
                AssetDatabase.CreateAsset(copy, path);
                mf.sharedMesh = copy;
                staticIndex++;
            }
            return count + staticIndex;
        }

        // Reextrae las mallas de un personaje desde su FBX CONSERVANDO todos los pesos, y
        // parchea el prefab. No rehornea clips ni controller: sirve para arreglar una malla
        // que se extrajo con el tope de 4 influencias por vertice (la malla se deformaba al
        // articular aunque el esqueleto estuviera perfecto).
        [MenuItem("King of Music/Setup/6 - Reextraer mallas con todos los pesos", priority = 14)]
        public static void RefreshSkinnedMeshesMenu()
        {
            foreach (var def in KomData.Characters.characters)
                if (def != null) RefreshSkinnedMeshes(def.id);
            AssetDatabase.SaveAssets();
            Debug.Log("[KOM] Mallas reextraidas con todos los pesos.");
        }

        public static void RefreshSkinnedMeshes(string id)
        {
            string fbx = $"{CharacterArtDir}/{id}.fbx";
            string prefabPath = $"{CharacterPrefabDir}/{id}.prefab";
            if (!File.Exists(fbx) || !File.Exists(prefabPath)) return;

            var fbxMeshes = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>()
                .OrderBy(m => m.name, StringComparer.Ordinal).ToArray();
            if (fbxMeshes.Length == 0) { Debug.LogError($"[KOM] {id}: el FBX no expone mallas."); return; }

            string dir = $"{CharacterPrefabDir}/Meshes";
            EnsureFolder(dir);

            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var smrs = contents.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs.Length == 0) { Debug.LogError($"[KOM] {id}: el prefab no tiene SMR."); return; }
                for (int i = 0; i < smrs.Length && i < fbxMeshes.Length; i++)
                {
                    string path = $"{dir}/{id}_skinned{i}.asset";
                    AssetDatabase.DeleteAsset(path);
                    var copy = UnityEngine.Object.Instantiate(fbxMeshes[i]);
                    copy.name = $"{id}_skinned{i}";
                    AssetDatabase.CreateAsset(copy, path);
                    smrs[i].sharedMesh = copy;
                    var bpv = copy.GetBonesPerVertex();
                    int max = 0;
                    for (int v = 0; v < bpv.Length; v++) if (bpv[v] > max) max = bpv[v];
                    Debug.Log($"[KOM] {id}: malla {i} reextraida con maxInfluencias={max} verts={bpv.Length}");
                }
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static AnimatorController CreateFighterController(CharacterDef def, Dictionary<string, AnimationClip> clips,
            Dictionary<string, float> naturalSpeeds)
        {
            EnsureFolder(ControllerDir);
            string path = $"{ControllerDir}/{def.id}.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            // ---------------- Capa 0: Base. Manda la pose completa, siempre.
            // Locomocion DISCRETA: un estado por direccion y un solo clip a peso 1.
            // No hay BlendTree, no hay parametro MoveSpeed y no existe Walk normal.
            var sm = controller.layers[0].stateMachine;
            while (sm.states.Length > 0) sm.RemoveState(sm.states[0].state);
            var idle = AddState(sm, "Idle", GetClip(clips, "Idle"), 1f);

            var walk = GetClip(clips, "WalkGuard");
            float walkNatural = NaturalSpeed(naturalSpeeds, "WalkGuard", CombatTuning.NaturalWalkSpeed);
            float walkScale = Mathf.Clamp(def.walkSpeed / walkNatural, 0.25f, 4.5f);
            AddState(sm, "WalkGuardFwd", walk, walkScale);
            AddState(sm, "WalkGuardBack", walk, -walkScale);

            var run = GetClip(clips, "Run");
            float runNatural = NaturalSpeed(naturalSpeeds, "Run", CombatTuning.NaturalRunSpeed);
            float runScale = Mathf.Clamp(def.dashSpeed / runNatural, 0.35f, 4.5f);
            AddState(sm, "RunFwd", run, runScale);
            AddState(sm, "RunBack", run, -runScale);

            AddState(sm, "Block", GetClip(clips, "Block"), 1f);
            // Agacharse es una accion, no una transicion: el clip `agacharse` dura
            // 0.567 s y a 1x tardaba el triple que un golpe rapido. Se comprime a
            // CrouchStateDuration y la pose final se mantiene.
            AddState(sm, "Crouch", GetClip(clips, "Crouch"),
                StateSpeed(clips, "Crouch", CombatTuning.CrouchStateDuration));
            AddState(sm, "Jump", GetClip(clips, "Jump"), StateSpeed(clips, "Jump", CombatTuning.JumpStateDuration));
            // Reacciones: el estado dura el hitstun, no el clip completo. La cola larga
            // de Mixamo se corta al volver a Locomotion (sin clips derivados gigantes).
            AddState(sm, "Hit", GetClip(clips, "Hit"), 1f);
            AddState(sm, "HitHeavy", GetClip(clips, "HitHeavy"), 1f);
            AddState(sm, "GroinHit", GetClip(clips, "GroinHit"), 1f);
            AddState(sm, "Stunned", GetClip(clips, "Stunned"), 1f);
            AddState(sm, "KO", GetClip(clips, "KO"), 1f);
            // Getting Up: se arranca al 23 % (tumbado) y a 4x se llega de pie en ~1.15 s.
            AddState(sm, "GetUp", GetClip(clips, "GetUp"), 4f);
            AddState(sm, "Victory", VictoryClip(def, clips), 1f);
            var attackStates = new HashSet<string>();
            foreach (var spec in Attacks.All)
            {
                // LowKick reutiliza el estado LightKick: no crear dos estados con el mismo nombre.
                if (!attackStates.Add(spec.animatorState)) continue;
                AddBaseAttackState(sm, clips, spec);
            }

            // Especial full-body en Base (misma autoridad unica, sin mascaras ni capa 1).
            // La velocidad del estado alinea la duracion del clip con la duracion jugable
            // del JSON, de modo que releaseTime/normalizedTime coinciden con la mano real.
            if (def != null)
            {
                for (int i = 0; i < def.SpecialCount; i++)
                {
                    var sp = def.GetSpecial(i);
                    if (sp == null || !sp.IsCombatSpecial || string.IsNullOrEmpty(sp.animatorState)) continue;
                    if (clips.TryGetValue(sp.animatorState, out var specialClip) && specialClip != null)
                    {
                        float speed = StateSpeed(specialClip, sp.duration);
                        AddState(sm, sp.animatorState, specialClip, speed);
                    }
                    else
                    {
                        Debug.LogWarning($"[KOM] {def.id}: especial '{sp.animatorState}' sin clip en el FBX.");
                    }
                }
            }
            sm.defaultState = idle;

            // ---------------- Capa 1: reservada para poderes. Peso 0 permanente.
            // Se deja VACIA a proposito: los clips de especiales/proyectiles siguen en el
            // FBX, pero no se referencian aqui. Referenciarlos obligaba a cargar ~10 clips
            // extra por personaje en el primer Instantiate y no aportaban nada en juego.
            controller.AddLayer("ReservedSpecials");
            var reservedSm = controller.layers[1].stateMachine;
            var reservedEmpty = reservedSm.AddState("Empty");
            reservedEmpty.writeDefaultValues = false;
            reservedSm.defaultState = reservedEmpty;

            var layers = controller.layers;
            layers[0].defaultWeight = 1f;
            layers[0].avatarMask = null;
            layers[1].avatarMask = null;
            layers[1].defaultWeight = 0f;
            controller.layers = layers;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        // Victoria con caracter: cada personaje celebra con un clip distinto.
        // Asi los clips de combo/sombra/magia tambien se ven en partida.
        //
        // "Dancing Twerk" queda FUERA: el clip esta mal retargeteado (la cadera se
        // descoloca y la malla se estira) y era la animacion de victoria por defecto.
        //
        // REGLA (medida, 2026-11-02): la victoria NO puede ser la animacion de un especial
        // que lleva instrumento. Flavia celebraba con "Combo" = `Two Hand Club Combo`, la
        // animacion del guitarrazo de Alex Llora. Como `Victory` es un estado suelto y no el
        // especial, `SpawnPropForSpecial` no corre: la jefa **mima una guitarra invisible**
        // justo al terminar la ronda o al noquear. El jugador lo lee como "siempre lanza el
        // guitarrazo sin razon" y como "a veces no aparece la guitarra". Los dos sintomas,
        // una sola causa. `KomValidation` lo bloquea.
        internal static string VictoryClipName(CharacterDef def)
        {
            string preferred = "SpecialWide";
            if (def != null)
            {
                switch (def.id)
                {
                    case "alex_llora":
                    case "roman_ayala":
                        preferred = "Boxing";
                        break;
                    case "jose_jose":
                    case "peso_plomo":
                        preferred = "MagicHeal";
                        break;
                    case "natanael_chano":
                        preferred = "SpecialWide";
                        break;
                }
            }
            return SafeVictoryTake(def, preferred);
        }

        // La celebracion SOLO es sana si el clip viene del FBX del primer commit: el
        // retarget nuevo deforma la cabeza en los estados que no existian en la primera
        // version (docs/ANIM_PIPELINE_DEFECTS.md). Si la toma preferida no esta alli,
        // SpecialWide si esta en los seis, asi que se cae a ella en vez de celebrar con
        // un clip deformado.
        static string SafeVictoryTake(CharacterDef def, string preferred)
        {
            if (def == null) return preferred;
            return FirstCommitHas(def.id, preferred) ? preferred : "SpecialWide";
        }

        static AnimationClip VictoryClip(CharacterDef def, Dictionary<string, AnimationClip> clips)
        {
            string preferred = VictoryClipName(def);
            foreach (var name in new[] { preferred, "SpecialWide", "Boxing", "MagicHeal" })
            {
                if (clips.TryGetValue(name, out var clip) && clip != null) return clip;
            }
            return null;
        }

        static void AddAttackState(AnimatorStateMachine sm, Dictionary<string, AnimationClip> clips, AttackSpec spec)
        {
            AddState(sm, spec.animatorState, GetClip(clips, spec.animatorState), StateSpeed(clips, spec.animatorState, spec.duration));
        }

        static void AddBaseAttackState(AnimatorStateMachine sm, Dictionary<string, AnimationClip> clips, AttackSpec spec)
        {
            // AAA: ataque full-body separado en Base. Sin mezclar con locomocion.
            // animSpeed fijo permite cortar la cola muerta del clip sin crear copias.
            float speed = spec.animSpeed > 0.01f
                ? spec.animSpeed
                : StateSpeed(clips, spec.animatorState, spec.duration);
            AddState(sm, spec.animatorState, GetClip(clips, spec.animatorState), speed);
        }

        static AnimatorState AddState(AnimatorStateMachine sm, string name, AnimationClip clip, float speed)
        {
            var state = sm.AddState(name);
            // AAA: writeDefaultValues=false evita que el estado arrastre valores por defecto
            // que se mezclan con la pose. Cada estado separado manda solo.
            state.motion = clip;
            // Los estados de locomocion hacia atras usan velocidad negativa (clip invertido).
            float magnitude = Mathf.Clamp(Mathf.Abs(speed), 0.2f, 4.5f);
            state.speed = speed < 0f ? -magnitude : magnitude;
            state.writeDefaultValues = false;
            return state;
        }

        static AnimationClip GetClip(Dictionary<string, AnimationClip> clips, string name)
        {
            if (clips.TryGetValue(name, out var clip)) return clip;
            Debug.LogWarning($"[KOM] Falta el clip '{name}' en el FBX.");
            return null;
        }

        static float NaturalSpeed(Dictionary<string, float> speeds, string state, float fallback)
        {
            if (speeds != null && speeds.TryGetValue(state, out float value) && value > 0.1f)
                return value;
            return fallback;
        }

        static float StateSpeed(Dictionary<string, AnimationClip> clips, string name, float duration)
        {
            clips.TryGetValue(name, out var clip);
            return StateSpeed(clip, duration);
        }

        static float StateSpeed(AnimationClip clip, float duration)
        {
            if (clip != null && duration > 0.001f) return clip.length / duration;
            return 1f;
        }

        static void CopyToUrp(Material src, Material dst)
        {
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            if (urp != null) dst.shader = urp;

            Texture main = GetTexture(src, "_MainTex") ?? GetTexture(src, "_BaseMap");
            Texture normal = GetTexture(src, "_BumpMap");
            Texture metal = GetTexture(src, "_MetallicGlossMap");
            Color tint = GetColor(src, "_Color", GetColor(src, "_BaseColor", Color.white));
            float smooth = GetFloat(src, "_Glossiness", GetFloat(src, "_Smoothness", 0.35f));

            if (main != null) dst.SetTexture("_BaseMap", main);
            if (dst.HasProperty("_BaseColor")) dst.SetColor("_BaseColor", tint);
            else dst.color = tint;
            dst.SetFloat("_Smoothness", Mathf.Clamp01(smooth));
            dst.SetFloat("_Metallic", GetFloat(src, "_Metallic", 0f));
            if (normal != null)
            {
                dst.SetTexture("_BumpMap", normal);
                dst.EnableKeyword("_NORMALMAP");
            }
            if (metal != null)
            {
                dst.SetTexture("_MetallicGlossMap", metal);
                dst.EnableKeyword("_METALLICSPECGLOSSMAP");
                dst.SetFloat("_Smoothness", 1f);
            }
            dst.SetFloat("_Surface", 0f);
            dst.renderQueue = -1;
            EditorUtility.SetDirty(dst);
        }

        static Texture GetTexture(Material mat, string prop)
        {
            return mat.HasProperty(prop) ? mat.GetTexture(prop) : null;
        }

        static Color GetColor(Material mat, string prop, Color fallback)
        {
            return mat.HasProperty(prop) ? mat.GetColor(prop) : fallback;
        }

        static float GetFloat(Material mat, string prop, float fallback)
        {
            return mat.HasProperty(prop) ? mat.GetFloat(prop) : fallback;
        }

        static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }

        static void BuildScenes()
        {
            CreateScene(KomScenes.MainMenu, typeof(MainMenuController));
            CreateScene(KomScenes.Story, typeof(StoryController));
            CreateScene(KomScenes.CharacterSelect, typeof(CharacterSelectController));
            CreateScene(KomScenes.Map, typeof(MapController));
            CreateScene(KomScenes.Combat, typeof(CombatController));
            CreateScene(KomScenes.Ending, typeof(EndingController));
            Debug.Log("[KOM] Escenas creadas.");
        }

        static void CreateScene(string name, Type controllerType)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject(name + "Bootstrap");
            go.AddComponent(controllerType);
            string path = $"{SceneDir}/{name}.unity";
            EditorSceneManager.SaveScene(scene, path);
        }

        static void BuildPreviewScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var floorMat = EnsureAssetMaterial("Assets/KOM/PreviewFloor.mat", new Color(0.1f, 0.11f, 0.12f), 0.4f, 0.1f);
            var backdropMat = EnsureAssetMaterial("Assets/KOM/PreviewBackdrop.mat", new Color(0.025f, 0.035f, 0.04f), 0.2f, 0f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(2f, 1f, 2f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Backdrop";
            wall.transform.position = new Vector3(0f, 4f, 7f);
            wall.transform.localScale = new Vector3(26f, 8f, 0.4f);
            wall.GetComponent<MeshRenderer>().sharedMaterial = backdropMat;

            var camGo = new GameObject("PreviewCamera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.015f, 0.025f, 0.03f);
            cam.fieldOfView = 33f;
            camGo.transform.position = new Vector3(0f, 1.25f, -5.5f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.92f, 0.25f) - camGo.transform.position);

            var lightGo = new GameObject("Directional", typeof(Light));
            lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.92f, 0.8f);

            var rimGo = new GameObject("Rim", typeof(Light));
            rimGo.transform.position = new Vector3(-3f, 4.5f, 2.5f);
            rimGo.transform.LookAt(new Vector3(0f, 1f, 0f));
            var rim = rimGo.GetComponent<Light>();
            rim.type = LightType.Spot;
            rim.color = new Color(0.97f, 0.7f, 0.3f);
            rim.intensity = 8f;
            rim.range = 18f;
            rim.spotAngle = 55f;

            var rim2Go = new GameObject("Rim2", typeof(Light));
            rim2Go.transform.position = new Vector3(3f, 4.5f, 2.5f);
            rim2Go.transform.LookAt(new Vector3(0f, 1f, 0f));
            var rim2 = rim2Go.GetComponent<Light>();
            rim2.type = LightType.Spot;
            rim2.color = new Color(0.3f, 0.8f, 0.85f);
            rim2.intensity = 7f;
            rim2.range = 18f;
            rim2.spotAngle = 55f;

            SpawnPreview("juan_grabriel", new Vector3(-1.85f, 0f, 0.25f), 1f);
            SpawnPreview("alex_llora", new Vector3(1.85f, 0f, 0.25f), -1f);

            EditorSceneManager.SaveScene(scene, PreviewScenePath);
            Debug.Log($"[KOM] Preview creada: {PreviewScenePath}");
        }

        static void SpawnPreview(string id, Vector3 position, float dir)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterPrefabDir}/{id}.prefab");
            if (prefab == null)
            {
                Debug.LogWarning($"[KOM] Preview: falta {id}.prefab");
                return;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = position;
            go.transform.rotation = KomVisual.PreviewRotation(dir);
            var fighter = go.GetComponent<Fighter>();
            if (fighter != null) fighter.enabled = false;
            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.enabled = false;
        }

        static Material EnsureAssetMaterial(string path, Color color, float smoothness, float metallic)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else mat.color = color;
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void ConfigureBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>();
            foreach (var name in KomScenes.All)
                scenes.Add(new EditorBuildSettingsScene($"{SceneDir}/{name}.unity", true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[KOM] Build settings actualizados.");
        }

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.productName = "King of Music";
            PlayerSettings.companyName = "Universidad";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
        }

        public static void ConfigurePlayerSettingsOnly()
        {
            ConfigurePlayerSettings();
            AssetDatabase.SaveAssets();
        }

        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Sal del modo Play antes de ejecutar el setup.");
        }
    }
}
