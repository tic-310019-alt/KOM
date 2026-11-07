using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KOM.EditorTools
{
    // Banco de pruebas de PROPS y poses de especiales (solo editor).
    // Mide las manos REALES del clip (no estima) para calibrar agarres y tiempos
    // de impacto. Todas las posiciones se calculan encadenando TRS locales: tras
    // SampleAnimation, transform.position puede venir cacheado un frame.
    public static class KomPropLab
    {
        public const string PropDir = "Assets/Art/Props";
        public const string ReportPath = "Logs/prop_lab.txt";

        // ---------------------------------------------------------------- utilidades

        public static AnimationClip FindClip(string id, string name)
        {
            string[] candidates =
            {
                $"Assets/KOM/Animation/Clips/InPlace/{id}_{name}_Clean.anim",
                $"Assets/KOM/Animation/Clips/InPlace/{id}_{name}_FirstRaw.anim",
                $"Assets/KOM/Animation/Clips/{id}_{name}.anim",
                $"Assets/Art/Characters/{id}.fbx",
                $"Assets/Art/Characters/FirstCommit/{id}.fbx"
            };
            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    var clip = asset as AnimationClip;
                    if (clip != null && clip.name == name) return clip;
                }
            }
            return null;
        }

        static GameObject LoadModel(string id)
        {
            string prefabPath = $"Assets/Resources/Characters/{id}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return null;
            var go = UnityEngine.Object.Instantiate(prefab);
            go.name = id + "_lab";
            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.enabled = false;
            var fighter = go.GetComponent<Fighter>();
            if (fighter != null) fighter.enabled = false;
            return go;
        }

        static Transform FindBone(GameObject go, string name)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // Posicion en espacio del modelo encadenando TRS locales (evita el cacheo).
        public static Vector3 ChainPosition(Transform t, Transform root)
        {
            var m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
            for (var p = t.parent; p != null && p != root.parent; p = p.parent)
                m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
            return m.GetColumn(3);
        }

        public static Quaternion ChainRotation(Transform t, Transform root)
        {
            var q = t.localRotation;
            for (var p = t.parent; p != null && p != root.parent; p = p.parent)
                q = p.localRotation * q;
            return q;
        }

        static string Fmt(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";
        static string FmtQ(Quaternion q) => $"({q.x:F3},{q.y:F3},{q.z:F3},{q.w:F3})";

        static float[] ParsePhases(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return new[] { 0f, 0.25f, 0.5f, 0.75f, 1f };
            var parts = csv.Split(',');
            var list = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out list[i]);
            return list;
        }

        // ---------------------------------------------------------------- medicion

        // Tabla por fase: manos, separacion, direccion de la mano, altura. Sirve para
        // elegir el frame de liberacion (mano mas adelantada) y los impactos (mano mas baja).
        public static string Measure(string id, string clipName, int steps = 24)
        {
            var go = LoadModel(id);
            if (go == null) return "ERROR: falta el prefab de " + id;
            var clip = FindClip(id, clipName);
            if (clip == null) { UnityEngine.Object.DestroyImmediate(go); return "ERROR: falta el clip " + clipName; }
            var handR = FindBone(go, "DEF-hand.R");
            var handL = FindBone(go, "DEF-hand.L");
            var indexR = FindBone(go, "DEF-f_index.01.R");
            var indexL = FindBone(go, "DEF-f_index.01.L");
            var spine = FindBone(go, "DEF-spine.003") ?? FindBone(go, "DEF-spine");
            var root = go.transform;
            var lines = new List<string>();
            lines.Add($"MEASURE|{id}|{clipName}|len={clip.length:F3}|fps={clip.frameRate:F0}|steps={steps}");
            lines.Add("t_norm\tt_sec\thandR\thandL\tmid\tsep\thandDirR\tspineY\theightR\theightL");

            float bestForward = -999f, bestForwardT = 0f;
            float lowest = 999f, lowestT = 0f;
            float closestHands = 999f, closestT = 0f;

            for (int i = 0; i <= steps; i++)
            {
                float n = i / (float)steps;
                clip.SampleAnimation(go, Mathf.Min(n * clip.length, clip.length - 0.0001f));
                var pr = ChainPosition(handR, root);
                var pl = ChainPosition(handL, root);
                var mid = (pr + pl) * 0.5f;
                var dir = indexR != null ? ChainPosition(indexR, root) - pr : Vector3.zero;
                var sy = spine != null ? ChainPosition(spine, root).y : 0f;
                float sep = Vector3.Distance(pr, pl);
                lines.Add($"{n:F3}\t{n * clip.length:F3}\t{Fmt(pr)}\t{Fmt(pl)}\t{Fmt(mid)}\t{sep:F3}\t{Fmt(dir.normalized)}\t{sy:F3}\t{pr.y:F3}\t{pl.y:F3}");
                if (mid.x > bestForward) { bestForward = mid.x; bestForwardT = n; }
                if (mid.y < lowest) { lowest = mid.y; lowestT = n; }
                if (sep < closestHands) { closestHands = sep; closestT = n; }
            }
            lines.Add($"BEST_FORWARD|t={bestForwardT:F3}|x={bestForward:F3}");
            lines.Add($"LOWEST_HANDS|t={lowestT:F3}|y={lowest:F3}");
            lines.Add($"CLOSEST_HANDS|t={closestT:F3}|sep={closestHands:F3}");

            // Perfil de altura de la mano derecha (para localizar los impactos del combo).
            var profile = new List<string>();
            for (int i = 0; i <= steps; i++)
            {
                float n = i / (float)steps;
                clip.SampleAnimation(go, Mathf.Min(n * clip.length, clip.length - 0.0001f));
                profile.Add($"{n:F2}:{ChainPosition(handR, root).y:F2}");
            }
            lines.Add("HAND_R_Y_PROFILE|" + string.Join(" ", profile));

            UnityEngine.Object.DestroyImmediate(go);
            var text = string.Join("\n", lines);
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, text + "\n");
            return text;
        }

        // ---------------------------------------------------------------- props

        // Ejes de la mano en un frame concreto: sirve para decidir que eje local de la
        // mano coincide con el mango del arma y calibrar el offset del prop.
        public static string GripProbe(string id, string clipName, float tNorm)
        {
            var go = LoadModel(id);
            if (go == null) return "ERROR: falta el prefab de " + id;
            var clip = FindClip(id, clipName);
            if (clip == null) { UnityEngine.Object.DestroyImmediate(go); return "ERROR: falta el clip"; }
            var handR = FindBone(go, "DEF-hand.R");
            var handL = FindBone(go, "DEF-hand.L");
            var indexR = FindBone(go, "DEF-f_index.01.R");
            var root = go.transform;
            clip.SampleAnimation(go, Mathf.Clamp01(tNorm) * (clip.length - 0.0001f));

            var pr = ChainPosition(handR, root);
            var pl = ChainPosition(handL, root);
            var qr = ChainRotation(handR, root);
            var hilt = (pl - pr).normalized;
            var lines = new List<string>();
            lines.Add($"GRIP_PROBE|{id}|{clipName}|t={tNorm:F3}|len={clip.length:F3}");
            lines.Add($"  handR={Fmt(pr)} handL={Fmt(pl)} sep={Vector3.Distance(pr, pl):F3}");
            lines.Add($"  hilt(handR->handL)={Fmt(hilt)}");
            lines.Add($"  handRot={FmtQ(qr)} euler={qr.eulerAngles.ToString("F2")}");
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward, Vector3.left, Vector3.down, Vector3.back };
            var names = new[] { "+X", "+Y", "+Z", "-X", "-Y", "-Z" };
            for (int i = 0; i < axes.Length; i++)
            {
                var w = qr * axes[i];
                lines.Add($"  {names[i]} world={Fmt(w)} angleToHilt={Vector3.Angle(w, hilt):F1} angleToDown={Vector3.Angle(w, Vector3.down):F1} angleToFwd={Vector3.Angle(w, Vector3.forward):F1}");
            }
            if (indexR != null)
            {
                var fingers = (ChainPosition(indexR, root) - pr).normalized;
                lines.Add($"  fingers={Fmt(fingers)} angleToHilt={Vector3.Angle(fingers, hilt):F1}");
            }
            // Direccion del "filo": perpendicular al mango y al antebrazo, para el roll.
            lines.Add($"  palmToChest={Fmt((ChainPosition(root, root) - pr).normalized)}");
            UnityEngine.Object.DestroyImmediate(go);
            var text = string.Join("\n", lines);
            Directory.CreateDirectory("Logs");
            File.AppendAllText("Logs/prop_lab.txt", "\n" + text + "\n");
            return text;
        }

        // ---------------------------------------------------------------- captura

        // El render del editor usa el estado del INICIO del frame: los cambios hechos
        // dentro de un mismo eval no llegan a Camera.Render (medido: 3 capturas
        // identicas en bind pose). Por eso la captura corre como un TRABAJO asincrono:
        // un paso por frame (muestrear / renderizar) y el estado se publica en JobStatus.
        public static string JobStatus { get; private set; } = "idle";
        static readonly Queue<Action> _steps = new Queue<Action>();
        static bool _running;
        static KomPropStage.Stage _stage;

        static void Enqueue(Action step) => _steps.Enqueue(step);

        static void Pump()
        {
            if (_steps.Count == 0)
            {
                _running = false;
                EditorApplication.update -= Pump;
                JobStatus = "done";
                return;
            }
            var step = _steps.Dequeue();
            try { step(); }
            catch (Exception e)
            {
                JobStatus = "error: " + e.Message;
                _steps.Clear();
                _running = false;
                EditorApplication.update -= Pump;
                if (_stage != null) { KomPropStage.End(_stage); _stage = null; }
            }
        }

        static void StartJob(string label)
        {
            JobStatus = label;
            if (_running) return;
            _running = true;
            EditorApplication.update += Pump;
        }

        static void CloseStage()
        {
            if (_stage == null) return;
            KomPropStage.End(_stage);
            _stage = null;
        }

        public static string StartCapturePose(string id, string clipName, string phasesCsv, string prefix)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            if (prefab == null) return "ERROR: falta el prefab " + id;
            var clip = FindClip(id, clipName);
            if (clip == null) return "ERROR: falta el clip " + clipName;
            var phases = ParsePhases(phasesCsv);
            StartJob("pose:" + prefix);
            GameObject go = null;
            Enqueue(() =>
            {
                _stage = KomPropStage.Begin(prefix);
                go = KomPropStage.Instantiate(_stage, prefab);
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;
                var fighter = go.GetComponent<Fighter>();
                if (fighter != null) fighter.enabled = false;
            });
            for (int i = 0; i < phases.Length; i++)
            {
                float phase = phases[i];
                int index = i;
                Enqueue(() =>
                {
                    clip.SampleAnimation(go, Mathf.Clamp01(phase) * (clip.length - 0.0001f));
                    Physics.SyncTransforms();
                });
                Enqueue(() =>
                {
                    KomPropStage.Frame(_stage, new Vector3(0f, 1.0f, 0f), 8f, 90f, 0f, 1.90f);
                    KomPropStage.Shot(_stage, $"{prefix}_{index}_side.png");
                    KomPropStage.Frame(_stage, new Vector3(0f, 1.0f, 0f), 8f, 145f, 8f, 1.90f);
                    KomPropStage.Shot(_stage, $"{prefix}_{index}_tq.png");
                    KomPropStage.Frame(_stage, new Vector3(0f, 1.0f, 0f), 8f, 180f, 4f, 1.90f);
                    KomPropStage.Shot(_stage, $"{prefix}_{index}_front.png");
                });
            }
            Enqueue(CloseStage);
            return "job started";
        }

        // Captura del clip con el prop anclado a la mano derecha (offset dado).
        public static string StartCaptureGrip(string id, string clipName, string phasesCsv, string prefix,
            string propName, string eulerCsv, string offsetCsv, string handName = "DEF-hand.R")
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            if (prefab == null) return "ERROR: falta el prefab " + id;
            var propPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Props/{propName}.prefab");
            if (propPrefab == null) return "ERROR: falta el prop " + propName;
            var clip = FindClip(id, clipName);
            if (clip == null) return "ERROR: falta el clip " + clipName;
            var phases = ParsePhases(phasesCsv);
            var euler = ParsePhases(eulerCsv);
            var offset = ParsePhases(offsetCsv);
            var localRot = Quaternion.Euler(euler[0], euler[1], euler[2]);
            var localPos = new Vector3(offset[0], offset[1], offset[2]);

            StartJob("grip:" + prefix);
            GameObject go = null, prop = null;
            Transform hand = null, handL = null;
            var diag = new List<string>();
            Enqueue(() =>
            {
                _stage = KomPropStage.Begin(prefix);
                go = KomPropStage.Instantiate(_stage, prefab);
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;
                var fighter = go.GetComponent<Fighter>();
                if (fighter != null) fighter.enabled = false;
                hand = FindBone(go, handName);
                handL = FindBone(go, handName == "DEF-hand.R" ? "DEF-hand.L" : "DEF-hand.R");
                prop = KomPropStage.Instantiate(_stage, propPrefab);
                prop.transform.SetParent(hand, false);
                prop.transform.localPosition = localPos;
                prop.transform.localRotation = localRot;
                var baseScale = prop.transform.localScale;
                float handScale = hand.lossyScale.x;
                prop.transform.localScale = baseScale * (handScale > 0.0001f ? 1f / handScale : 1f);
            });
            for (int i = 0; i < phases.Length; i++)
            {
                float phase = phases[i];
                int index = i;
                Enqueue(() =>
                {
                    clip.SampleAnimation(go, Mathf.Clamp01(phase) * (clip.length - 0.0001f));
                    Physics.SyncTransforms();
                });
                Enqueue(() =>
                {
                    var pr = ChainPosition(hand, go.transform);
                    var pl = ChainPosition(handL, go.transform);
                    var pivot = prop.transform.position;
                    var bodyAxis = prop.transform.rotation * Vector3.down;
                    diag.Add($"t={phase:F2} handR={Fmt(pr)} handL={Fmt(pl)} propScale={prop.transform.lossyScale.x:F3} " +
                             $"gripErr={Vector3.Distance(pivot, (pr + pl) * 0.5f):F3} bodyDir={Fmt(bodyAxis)} " +
                             $"hilt={Fmt((pl - pr).normalized)} angle={Vector3.Angle(bodyAxis, (pl - pr).normalized):F1}");
                    KomPropStage.Frame(_stage, new Vector3(0f, 1.0f, 0f), 8f, 90f, 0f, 1.90f);
                    KomPropStage.Shot(_stage, $"{prefix}_{index}_side.png");
                    KomPropStage.Frame(_stage, new Vector3(0f, 1.0f, 0f), 8f, 145f, 8f, 1.90f);
                    KomPropStage.Shot(_stage, $"{prefix}_{index}_tq.png");
                    KomPropStage.Frame(_stage, new Vector3(0f, 1.0f, 0f), 8f, 180f, 4f, 1.90f);
                    KomPropStage.Shot(_stage, $"{prefix}_{index}_front.png");
                });
            }
            Enqueue(() =>
            {
                Directory.CreateDirectory("Logs");
                File.AppendAllText("Logs/prop_lab.txt", "\nCAPTURE_GRIP " + prefix + "\n" + string.Join("\n", diag) + "\n");
            });
            Enqueue(CloseStage);
            return "job started";
        }

        // Captura del CLUB con el agarre REAL: KomPropGrip.AttachGuitar + KomClubGrip, el
        // mismo camino que en Play. Sirve para juzgar la orientacion de la guitarra sin
        // abrir el juego. `view` elige el encuadre: side / tq (3/4 de combate) / front.
        public static string StartCaptureClub(string id, string clipName, string phasesCsv, string prefix)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            if (prefab == null) return "ERROR: falta el prefab " + id;
            var propPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Props/guitarra.prefab");
            if (propPrefab == null) return "ERROR: falta el prop guitarra";
            var clip = FindClip(id, clipName);
            if (clip == null) return "ERROR: falta el clip " + clipName;
            var phases = ParsePhases(phasesCsv);

            StartJob("club:" + prefix);
            GameObject go = null, prop = null;
            Transform hand = null, finger = null, chest = null;
            var diag = new List<string>();
            Enqueue(() =>
            {
                _stage = KomPropStage.Begin(prefix);
                go = KomPropStage.Instantiate(_stage, prefab);
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;
                var fighter = go.GetComponent<Fighter>();
                if (fighter != null) fighter.enabled = false;
                hand = FindBone(go, "DEF-hand.L");
                finger = FindBone(go, "DEF-f_index.01.L");
                chest = FindBone(go, "DEF-spine.003") ?? FindBone(go, "DEF-spine.002") ?? FindBone(go, "DEF-spine");
                prop = KomPropStage.Instantiate(_stage, propPrefab);
                // El prop se cuelga UNA vez: queda hijo de la mano, asi que la animacion ya
                // lo arrastra. Reanclar por fase recompensaba la escala una y otra vez.
                KomPropGrip.AttachGuitar(prop, hand, finger, chest, go.transform);
            });
            for (int i = 0; i < phases.Length; i++)
            {
                float phase = phases[i];
                int index = i;
                Enqueue(() =>
                {
                    clip.SampleAnimation(go, Mathf.Clamp01(phase) * (clip.length - 0.0001f));
                    Physics.SyncTransforms();
                    // Snap = resolver sin suavizar: cada foto es una pose independiente.
                    var grip0 = prop.GetComponent<KomClubGrip>();
                    if (grip0 != null) grip0.Snap();
                });
                Enqueue(() =>
                {
                    var grip = prop.GetComponent<KomClubGrip>();
                    Vector3 aim = grip != null ? grip.Aim : Vector3.forward;
                    var chestW = chest.position;
                    var edge = KomPropGrip.BodyEdge(prop);
                    float reach = Vector3.Dot(edge - chestW, aim);
                    diag.Add($"t={phase:F3} aim=({aim.x:F2},{aim.y:F2},{aim.z:F2}) edgeY={edge.y:F2} reach={reach:F2} " +
                             $"handY={hand.position.y:F2}");
                    KomPropStage.Frame(_stage, new Vector3(0f, 1.0f, 0f), 9f, 90f, 0f, 2.05f);
                    KomPropStage.Shot(_stage, $"{prefix}_{index}_side.png");
                    KomPropStage.Frame(_stage, new Vector3(0f, 1.0f, 0f), 9f, 145f, 10f, 2.05f);
                    KomPropStage.Shot(_stage, $"{prefix}_{index}_tq.png");
                    KomPropStage.Frame(_stage, new Vector3(0f, 1.0f, 0f), 9f, 180f, 4f, 2.05f);
                    KomPropStage.Shot(_stage, $"{prefix}_{index}_front.png");
                });
            }
            Enqueue(() =>
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/club_grip.txt", string.Join("\n", diag) + "\n");
            });
            Enqueue(CloseStage);
            return "job started";
        }

        public static string JobReport()
        {
            var text = File.Exists("Logs/prop_lab.txt") ? File.ReadAllText("Logs/prop_lab.txt") : "";
            int idx = text.LastIndexOf("CAPTURE_GRIP", StringComparison.Ordinal);
            return JobStatus + "\n" + (idx >= 0 ? text.Substring(idx) : "");
        }

        // Diagnostico del agarre de espada: cadena de transformaciones completa, para
        // separar "el solver orienta mal" de "el prop esta escalado/colocado mal".
        public static string ClubGripDiag(string id, string clipName, float tNorm)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            var propPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Props/guitarra.prefab");
            if (prefab == null || propPrefab == null) return "ERROR: faltan assets";
            var clip = FindClip(id, clipName);
            if (clip == null) return "ERROR: falta el clip";
            var go = UnityEngine.Object.Instantiate(prefab);
            var prop = UnityEngine.Object.Instantiate(propPrefab);
            var lines = new List<string>();
            try
            {
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;
                var fighter = go.GetComponent<Fighter>();
                if (fighter != null) fighter.enabled = false;
                clip.SampleAnimation(go, Mathf.Clamp01(tNorm) * (clip.length - 0.0001f));
                var hand = FindBone(go, "DEF-hand.L");
                var finger = FindBone(go, "DEF-f_index.01.L");
                var chest = FindBone(go, "DEF-spine.003") ?? FindBone(go, "DEF-spine.002") ?? FindBone(go, "DEF-spine");
                lines.Add($"prefabScale={prop.transform.localScale} renderers={prop.GetComponentsInChildren<Renderer>(true).Length}");
                foreach (var r in prop.GetComponentsInChildren<Renderer>(true))
                    lines.Add($"  R {r.name} type={r.GetType().Name} childOfRoot={r.transform == prop.transform} localScale={r.transform.localScale} lbC={r.localBounds.center} lbS={r.localBounds.size} lossy={r.transform.lossyScale}");
                lines.Add($"hand={Fmt(hand.position)} handLossy={hand.lossyScale}");
                KomPropGrip.AttachGuitar(prop, hand, finger, chest, go.transform);
                var grip = prop.GetComponent<KomClubGrip>();
                lines.Add($"propLossy={prop.transform.lossyScale} pos={Fmt(prop.transform.position)} euler={prop.transform.rotation.eulerAngles.ToString("F1")}");
                lines.Add($"grip aim={Fmt(grip.Aim)} face={Fmt(grip.Face)}");
                var palm = KomPropGrip.PalmWorld(hand, finger);
                var edge = KomPropGrip.BodyEdge(prop);
                var center = KomPropGrip.BodyCenter(prop);
                lines.Add($"palm={Fmt(palm)} edge={Fmt(edge)} center={Fmt(center)} chest={Fmt(chest.position)}");
                lines.Add($"dist palm->edge={Vector3.Distance(palm, edge):F3} palm->center={Vector3.Distance(palm, center):F3}");
                var r0 = prop.GetComponentInChildren<Renderer>();
                var b = r0.localBounds;
                lines.Add($"gripAnchor(0.82)={Fmt(KomPropGrip.GripAnchor(prop, KomPropGrip.GuitarGripNorm))} bounds min.y={b.min.y:F5} max.y={b.max.y:F5}");
                lines.Add($"local -Y in world={Fmt(prop.transform.rotation * Vector3.down)}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prop);
                UnityEngine.Object.DestroyImmediate(go);
            }
            return string.Join("\n", lines);
        }
        // Calcula el offset de rotacion que alinea el cuerpo de la guitarra (-Y local)
        // con el eje del mango (mano derecha -> mano izquierda) en el frame dado.
        public static string CalibrateGuitar(string id, string clipName, float tNorm, string handName = "DEF-hand.R")
        {
            var go = LoadModel(id);
            if (go == null) return "ERROR: falta el prefab";
            var clip = FindClip(id, clipName);
            if (clip == null) { UnityEngine.Object.DestroyImmediate(go); return "ERROR: falta el clip"; }
            var handR = FindBone(go, handName);
            var handL = FindBone(go, handName == "DEF-hand.R" ? "DEF-hand.L" : "DEF-hand.R");
            clip.SampleAnimation(go, Mathf.Clamp01(tNorm) * (clip.length - 0.0001f));
            var pr = ChainPosition(handR, go.transform);
            var pl = ChainPosition(handL, go.transform);
            var qr = ChainRotation(handR, go.transform);
            var hilt = handName == "DEF-hand.R" ? (pl - pr).normalized : (pr - pl).normalized;
            var local = Quaternion.Inverse(qr) * hilt;
            var q = Quaternion.FromToRotation(Vector3.down, local);
            var e = q.eulerAngles;
            var text = $"CALIBRATE|{id}|{clipName}|t={tNorm:F3}\n" +
                       $"  hiltWorld={Fmt(hilt)} localInHand={Fmt(local)}\n" +
                       $"  euler=({e.x:F2},{e.y:F2},{e.z:F2}) quat={FmtQ(q)}\n" +
                       $"  check={Fmt(qr * q * Vector3.down)} vs hilt={Fmt(hilt)} err={Vector3.Angle(qr * q * Vector3.down, hilt):F2}deg";
            UnityEngine.Object.DestroyImmediate(go);
            Directory.CreateDirectory("Logs");
            File.AppendAllText("Logs/prop_lab.txt", "\n" + text + "\n");
            return text;
        }

        // Calibra el agarre apuntando el CUERPO del prop a una direccion concreta del
        // modelo (no al eje del mango): es lo que hace que el guitarrazo golpee hacia el
        // rival en el frame de impacto. aim se da en espacio del modelo (+Z = al frente).
        public static string CalibrateGuitarAim(string id, string clipName, float tNorm, float ax, float ay, float az)
        {
            var go = LoadModel(id);
            if (go == null) return "ERROR: falta el prefab";
            var clip = FindClip(id, clipName);
            if (clip == null) { UnityEngine.Object.DestroyImmediate(go); return "ERROR: falta el clip"; }
            var hand = FindBone(go, "DEF-hand.L");
            clip.SampleAnimation(go, Mathf.Clamp01(tNorm) * (clip.length - 0.0001f));
            var qr = ChainRotation(hand, go.transform);
            var aim = new Vector3(ax, ay, az).normalized;
            var local = Quaternion.Inverse(qr) * aim;
            var q = Quaternion.FromToRotation(Vector3.down, local);
            var e = q.eulerAngles;
            var text = $"CALIBRATE_AIM|{id}|{clipName}|t={tNorm:F3}|aim={Fmt(aim)}" +
                       $" euler=({e.x:F2},{e.y:F2},{e.z:F2}) check={Fmt(qr * q * Vector3.down)} err={Vector3.Angle(qr * q * Vector3.down, aim):F2}deg";
            UnityEngine.Object.DestroyImmediate(go);
            Directory.CreateDirectory("Logs");
            File.AppendAllText("Logs/prop_lab.txt", text + "\n");
            return text;
        }

        public static GameObject LoadProp(string resourceName)
        {
            string path = $"Assets/Art/Props/{resourceName}.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;
            return prefab;
        }

        // Informe de ejes del prop: bounds, centro, eje largo y materiales.
        public static string PropInfo(string resourceName)
        {
            var prefab = LoadProp(resourceName);
            if (prefab == null) return "ERROR: falta " + resourceName;
            var go = UnityEngine.Object.Instantiate(prefab);
            var lines = new List<string>();
            lines.Add($"PROP|{resourceName}|root={go.name}|scale={go.transform.localScale.ToString("F4")}");
            var b = new Bounds();
            bool first = true;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var bb = mf.sharedMesh.bounds;
                var world = mf.transform.TransformPoint(bb.center);
                lines.Add($"  MESH {mf.name} verts={mf.sharedMesh.vertexCount} center={Fmt(bb.center)} size={Fmt(bb.size)} worldCenter={Fmt(world)}");
                if (first) { b = bb; first = false; } else b.Encapsulate(bb);
            }
            lines.Add($"  BOUNDS center={Fmt(b.center)} size={Fmt(b.size)}");
            lines.Add($"  AXIS largest={(b.size.x >= b.size.y && b.size.x >= b.size.z ? "X" : (b.size.y >= b.size.z ? "Y" : "Z"))}");
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var names = new List<string>();
                foreach (var m in r.sharedMaterials)
                    names.Add(m != null ? $"{m.name}[{m.shader.name}]" : "null");
                lines.Add($"  R {r.name} mats={string.Join(" ; ", names)}");
            }
            UnityEngine.Object.DestroyImmediate(go);
            return string.Join("\n", lines);
        }
    }
}
