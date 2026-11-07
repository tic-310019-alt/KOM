using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KOM.EditorTools
{
    // Los clips retargeteados de Mixamo NO son in-place: la cadera viaja dentro de la
    // pose (Walk 1.57 m, Run 2.27 m, guardia 0.33 m). Con applyRootMotion=false y el
    // movimiento por codigo eso deja la malla por delante del Fighter y da el tiron al
    // empezar a andar y al loopear. Este paso hornea copias IN-PLACE: mide la cadera en
    // mundo, y compensa esa traslacion horizontal en la raiz del armature con curvas
    // propias. No hay parche de runtime: el clip resultante ya nace centrado.
    public static class KomInPlaceBaker
    {
        const string ClipsDir = "Assets/KOM/Animation/Clips";
        const string OutDir = ClipsDir + "/InPlace";
        const int Fps = 30;
        // Tolerancia del verificador interno: 2 cm. El gate de KomValidation usa 6 cm.
        const float TravelVerify = 0.02f;

        // Estados que DEBEN repetirse. Sin loopTime el Animator clava el ultimo frame
        // y la locomocion se queda congelada.
        static readonly HashSet<string> LoopStates = new HashSet<string>
        {
            "Idle", "Run", "Boxing", "WalkGuard", "Block"
        };

        public sealed class Result
        {
            public Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>();
            public Dictionary<string, float> NaturalSpeeds = new Dictionary<string, float>();
            // Giro maximo del cuerpo por estado (grados, relativo al primer frame).
            public Dictionary<string, float> BodyTurn = new Dictionary<string, float>();
        }

        struct LocalTRS
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        public static Result Bake(GameObject model, string id, Dictionary<string, AnimationClip> sources,
            HashSet<string> firstRawStates)
        {
            var result = new Result();
            if (model == null || sources == null || sources.Count == 0)
            {
                if (sources != null) result.Clips = new Dictionary<string, AnimationClip>(sources);
                return result;
            }

            var animator = model.GetComponentInChildren<Animator>(true);
            var transforms = model.GetComponentsInChildren<Transform>(true);
            var pelvis = transforms.FirstOrDefault(t => t.name == "DEF-spine")
                ?? transforms.FirstOrDefault(t => t.name.StartsWith("DEF-pelvis", StringComparison.Ordinal))
                ?? transforms.FirstOrDefault(t => t.name == "torso");
            if (animator == null || pelvis == null)
            {
                Debug.LogError($"[KOM][INPLACE] {id}: falta Animator o hueso cadera; no se puede hornear.");
                result.Clips = new Dictionary<string, AnimationClip>(sources);
                return result;
            }

            var armature = pelvis;
            while (armature.parent != null && armature.parent != model.transform) armature = armature.parent;
            string armaturePath = AnimationUtility.CalculateTransformPath(armature, model.transform);

            var bind = new Dictionary<Transform, LocalTRS>();
            foreach (var t in transforms)
                bind[t] = new LocalTRS { position = t.localPosition, rotation = t.localRotation, scale = t.localScale };

            var rootTrs = new LocalTRS
            {
                position = model.transform.localPosition,
                rotation = model.transform.localRotation,
                scale = model.transform.localScale
            };

            var toes = transforms
                .Where(t => t.name == "DEF-toe.L" || t.name == "DEF-toe.R")
                .ToArray();
            var defaults = KomSetup.LoadBindDefaults(model);
            var relevant = KomSetup.BuildRelevantPaths(model);
            bool filter = relevant != null && relevant.Count >= 10;
            if (filter) relevant.Add(armaturePath);
            // La cadena de la cadera se copia EXACTA: con escala 100 en el armature,
            // una tolerancia de 0.0005 al simplificar se convierte en 5 cm de deriva.
            var exactPaths = new HashSet<string>();
            for (var t = pelvis; t != null; t = t.parent)
                exactPaths.Add(AnimationUtility.CalculateTransformPath(t, model.transform));

            animator.enabled = false;
            EnsureFolders();
            CleanOwnedAssets(id);
            try
            {
                // Se mide en espacio de modelo: el Fighter rota el prefab completo al
                // enfrentarse, y la compensacion debe viajar con esa rotacion.
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;

                foreach (var pair in sources)
                {
                    var source = pair.Value;
                    if (source == null) continue;

                    var trajectory = SampleTrajectory(model, transforms, bind, source, pelvis, armature,
                        out var armPositions, out var hipYaw);
                    float travel = HorizontalDistance(trajectory[0], trajectory[trajectory.Count - 1]);
                    if (source.length > 0.001f)
                        result.NaturalSpeeds[pair.Key] = travel / source.length;

                    bool preserveFacialAndScale = firstRawStates != null && firstRawStates.Contains(pair.Key);
                    var correction = new Vector3[trajectory.Count];
                    var baked = BuildClip(id, pair.Key, source, preserveFacialAndScale, filter, relevant,
                        defaults, armaturePath, armPositions, hipYaw, trajectory, correction, exactPaths);
                    if (baked == null)
                    {
                        result.Clips[pair.Key] = source;
                        continue;
                    }

                    var verify = SampleTrajectory(model, transforms, bind, baked, pelvis, armature,
                        out _, out _);
                    // Se mide la DERIVA NETA (principio -> final), no la excursion maxima: el
                    // cuerpo DEBE balancearse dentro de la pose. Exigir excursion cero obligaba
                    // a la raiz a recorrer ese balanceo al reves (15.7 cm en Idle, 2.4 m en
                    // Combo3) y eso es el tambaleo que se ve en Play.
                    float after = NetDrift(verify);
                    if (after > 0.005f)
                    {
                        // Segunda pasada: se corrige solo la deriva que queda, repartida en
                        // recta, para no volver a meter el balanceo en la raiz.
                        var rest = verify[verify.Count - 1] - verify[0];
                        rest.y = 0f;
                        for (int i = 0; i < verify.Count; i++)
                            correction[i] += rest * ((float)i / (verify.Count - 1));
                        baked = BuildClip(id, pair.Key, source, preserveFacialAndScale, filter, relevant,
                            defaults, armaturePath, armPositions, hipYaw, trajectory, correction, exactPaths);
                        verify = SampleTrajectory(model, transforms, bind, baked, pelvis, armature,
                            out _, out _);
                        after = NetDrift(verify);
                    }
                    if (after > TravelVerify)
                        Debug.LogError($"[KOM][INPLACE] {id}/{pair.Key}: queda {after:F3} m de deriva tras el horneado.");

                    // Giro del cuerpo: SOLO se mide y se reporta. Corregirlo rotando el
                    // nodo Armature descoloca la pelvis respecto de las piernas y saca los
                    // pies del suelo (se midieron 1.79 m de altura en Fireball). Si un clip
                    // gira demasiado, se cambia la toma de origen, no se contrarrota la pose.
                    float turn = 0f;
                    for (int i = 0; i < hipYaw.Length; i++) turn = Mathf.Max(turn, Mathf.Abs(hipYaw[i]));
                    result.BodyTurn[pair.Key] = turn;

                    // Cadencia real de locomocion: velocidad a la que el DEDO retrocede
                    // durante el apoyo en el clip ya in-place. Es la referencia correcta
                    // para el timeScale; el viaje de cadera incluye la fase de vuelo.
                    if (pair.Key == "WalkGuard" || pair.Key == "Run")
                    {
                        float contact = MeasureContactSpeed(model, transforms, bind, baked, toes);
                        if (contact > 0.1f) result.NaturalSpeeds[pair.Key] = contact;
                    }
                    result.Clips[pair.Key] = baked;
                }
            }
            finally
            {
                foreach (var t in transforms)
                {
                    if (t == null || !bind.TryGetValue(t, out var b)) continue;
                    t.localPosition = b.position;
                    t.localRotation = b.rotation;
                    t.localScale = b.scale;
                }
                model.transform.localPosition = rootTrs.position;
                model.transform.localRotation = rootTrs.rotation;
                model.transform.localScale = rootTrs.scale;
                animator.enabled = true;
            }
            return result;
        }

        static List<Vector3> SampleTrajectory(GameObject model, Transform[] transforms,
            Dictionary<Transform, LocalTRS> bind, AnimationClip clip, Transform pelvis, Transform armature,
            out Vector3[] armatureLocal, out float[] hipYaw)
        {
            int steps = Mathf.Clamp(Mathf.RoundToInt(clip.length * Fps), 2, 900);
            var points = new List<Vector3>(steps + 1);
            armatureLocal = new Vector3[steps + 1];
            hipYaw = new float[steps + 1];
            float prevYaw = 0f;
            float baseYaw = 0f;
            for (int i = 0; i <= steps; i++)
            {
                ResetBind(transforms, bind);
                float t = clip.length * i / steps;
                clip.SampleAnimation(model, Mathf.Min(t, clip.length - 0.00001f));
                points.Add(ChainPosition(pelvis));
                armatureLocal[i] = armature.localPosition;
                // Yaw del cuerpo = twist de la PELVIS sobre el eje Y (swing-twist exacto).
                // La linea de caderas se inclina en los hachazos y su proyeccion es ruido;
                // Procrustes sobre 9 puntos salta cuando quedan casi colineales (se midieron
                // saltos de 300 grados entre frames).
                float yaw = KomBodyYaw.TwistYaw(ChainRotation(pelvis));
                if (i == 0) { prevYaw = yaw; baseYaw = yaw; }
                // Desenrollar para que el giro sea continuo (cruce de +-180).
                while (yaw - prevYaw > 180f) yaw -= 360f;
                while (yaw - prevYaw < -180f) yaw += 360f;
                prevYaw = yaw;
                hipYaw[i] = yaw - baseYaw;
            }
            return points;
        }

        static Quaternion ChainRotation(Transform t)
        {
            var q = t.localRotation;
            for (var p = t.parent; p != null; p = p.parent)
                q = p.localRotation * q;
            return q;
        }

        static AnimationClip BuildClip(string id, string state, AnimationClip source, bool preserveFacialAndScale,
            bool filter, HashSet<string> relevant, Dictionary<string, KomSetup.BindDefault> defaults,
            string armaturePath, Vector3[] armatureLocal, float[] hipYaw,
            List<Vector3> trajectory, Vector3[] correction, HashSet<string> exactPaths)
        {
            int steps = armatureLocal.Length - 1;
            string suffix = !preserveFacialAndScale ? "Clean"
                : state == "WalkGuard" ? "RawWalk" : "FirstRaw";
            var clip = new AnimationClip
            {
                name = $"{id}_{state}_{suffix}",
                frameRate = source.frameRate > 0f ? source.frameRate : Fps,
                wrapMode = source.wrapMode
            };

            // Las copias RAW (primer commit y WalkGuard) se copian EXACTAS: todas las
            // curvas, sin filtro ni simplificacion. La cabeza se apoya en dos cadenas
            // paralelas (ORG y DEF) que comparten ancestros; simplificar una y no la
            // otra las desincroniza y estira la cara a lo largo del ciclo. El unico
            // cambio permitido es la posicion del armature (compensacion in-place).
            //
            // En las copias CLEAN la poda va por GRUPO (rotacion = 4 componentes,
            // posicion = 3). Descartar solo m_LocalRotation.w deja la rotacion
            // indefinida y el hueso salta a una orientacion arbitraria: era la causa de
            // las piernas destrozadas de los especiales.
            var groups = new Dictionary<string, List<(EditorCurveBinding binding, AnimationCurve curve)>>();
            var order = new List<string>();
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                bool isArmaturePosition = binding.path == armaturePath
                    && binding.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal);
                if (isArmaturePosition) continue; // se reconstruye con la compensacion

                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null || curve.length == 0) continue;

                if (preserveFacialAndScale)
                {
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                    continue;
                }

                if (binding.propertyName.StartsWith("m_LocalScale", StringComparison.Ordinal)) continue;
                if (KomSetup.IsFacialBonePath(binding.path)) continue;
                if (filter && !relevant.Contains(binding.path)) continue;

                string kind = binding.propertyName.StartsWith("m_LocalRotation", StringComparison.Ordinal) ? "rot"
                    : binding.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal) ? "pos"
                    : binding.propertyName;
                string key = binding.path + "|" + kind;
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<(EditorCurveBinding, AnimationCurve)>();
                    groups[key] = list;
                    order.Add(key);
                }
                list.Add((binding, curve));
            }

            foreach (var key in order)
            {
                var list = groups[key];
                bool dropAll = true;
                foreach (var item in list)
                {
                    if (!KomSetup.IsConstant(item.curve) ||
                        !KomSetup.IsBindPoseConstant(item.binding, item.curve, defaults))
                    {
                        dropAll = false;
                        break;
                    }
                }
                // NO se descarta el grupo constante en bind: con writeDefaultValues=false el
                // Animator no resetea lo que el clip no anima, asi que un hueso sin curvas se
                // queda en la pose del estado anterior. Se escribe plano con 2 keys.
                if (dropAll)
                {
                    foreach (var item in list)
                    {
                        var flat = new AnimationCurve(new Keyframe(0f, item.curve.keys[0].value),
                                                      new Keyframe(source.length, item.curve.keys[0].value));
                        AnimationUtility.SetEditorCurve(clip, item.binding, flat);
                        written.Add(item.binding.path);
                    }
                    continue;
                }

                foreach (var item in list)
                {
                    bool constant = KomSetup.IsConstant(item.curve);
                    bool exact = exactPaths != null && exactPaths.Contains(item.binding.path);
                    // Las posiciones nunca se simplifican: la tolerancia va en unidades de
                    // objeto y con el armature a escala 100 se traduce en centimetros de
                    // mundo (ver KomSetup.CreateOptimizedClip).
                    bool isPosition = item.binding.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal);
                    var optimized = constant
                        ? new AnimationCurve(new Keyframe(0f, item.curve.keys[0].value),
                                             new Keyframe(source.length, item.curve.keys[0].value))
                        : (exact || isPosition) ? item.curve : KomSetup.SimplifyCurve(item.curve, 0.0005f);
                    AnimationUtility.SetEditorCurve(clip, item.binding, optimized);
                    written.Add(item.binding.path);
                }
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            {
                var keys = AnimationUtility.GetObjectReferenceCurve(source, binding);
                if (keys != null) AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            }

            // Completa la pose: los huesos con peso que la toma no trae se escriben planos en
            // bind. Sin esto quedan sin animar y el Animator los deja en la pose del estado
            // anterior (writeDefaultValues=false) -> pelo/cara anclados y malla estirada.
            // Va ANTES de la compensacion del armature, que sobrescribe su posicion.
            KomSetup.CompletePose(clip, source.length, defaults, relevant, written);

            // WalkGuard se copia EXACTO (cuerpo + cabeza + facial + escala). No se injerta
            // nada de otro clip: mezclar la cabeza de Boxing con padres de WalkGuard
            // desincroniza las cadenas ORG/DEF y estira la cara a lo largo del ciclo.
            AddArmatureCompensation(clip, source, armaturePath, armatureLocal, trajectory, correction);

            var settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.startTime = 0f;
            settings.stopTime = source.length;
            // El clip in-place no cierra sus curvas locales (la cadera avanza y la raiz
            // compensa); mezclar el loop en el Animator reintroduciria el tiron.
            settings.loopBlend = false;
            // Sin esto la locomocion se queda clavada en el ultimo frame.
            settings.loopTime = LoopStates.Contains(state) || settings.loopTime;
            if (LoopStates.Contains(state)) clip.wrapMode = WrapMode.Loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            string path = $"{OutDir}/{clip.name}.anim";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // La cadera recorre delta(t) en mundo. Se anade a la raiz del armature un
        // desplazamiento local -delta(t) en el espacio de su padre: la malla deja de
        // salirse del Fighter, sin tocar ninguna rotacion ni la altura.
        static void AddArmatureCompensation(AnimationClip clip, AnimationClip source, string armaturePath,
            Vector3[] armatureLocal, List<Vector3> trajectory, Vector3[] correction)
        {
            int steps = armatureLocal.Length - 1;
            var xs = new AnimationCurve();
            var ys = new AnimationCurve();
            var zs = new AnimationCurve();
            var origin = trajectory[0];
            // DERIVA NETA, no trayectoria instantanea.
            //
            // Antes se restaba `trajectory[i] - origin`, es decir TODO el vaiven de la cadera
            // dentro de la pose. Eso obliga a la raiz a recorrer ese mismo vaiven en sentido
            // contrario: medido en Alex, la raiz se movia 15.7 cm durante el Idle (con saltos
            // de 3.6 cm entre frames) y 2.4 m durante Combo3. La malla no se deformaba: el
            // esqueleto entero SE ARRASTRABA, y en Play eso se ve como un tambaleo o como si
            // la malla se moviera "hacia direcciones distintas". En el inspector no se ve
            // porque solo se mira un fotograma.
            //
            // Lo correcto es quitar solo el avance acumulado (una recta del inicio al final):
            // asi el clip queda en el sitio y el cuerpo conserva su balanceo natural, que es
            // lo que hace que un idle o un golpe se lean bien.
            var drift = trajectory[steps] - origin;
            drift.y = 0f;
            for (int i = 0; i <= steps; i++)
            {
                float t = source.length * i / steps;
                var delta = drift * ((float)i / steps);
                var extra = correction != null && i < correction.Length ? correction[i] : Vector3.zero;
                var local = armatureLocal[i] - delta - extra;
                xs.AddKey(t, local.x);
                ys.AddKey(t, local.y);
                zs.AddKey(t, local.z);
            }
            SetLinear(xs);
            SetLinear(ys);
            SetLinear(zs);
            string path = string.IsNullOrEmpty(armaturePath) ? "" : armaturePath;
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.x"), xs);
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.y"), ys);
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.z"), zs);
        }

        static void SetLinear(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
        }

        static float MeasureContactSpeed(GameObject model, Transform[] transforms,
            Dictionary<Transform, LocalTRS> bind, AnimationClip clip, Transform[] toes)
        {
            if (toes == null || toes.Length == 0) return 0f;
            int steps = Mathf.Clamp(Mathf.RoundToInt(clip.length * 60f), 2, 900);
            int count = toes.Length;
            var z = new float[count][];
            var y = new float[count][];
            for (int f = 0; f < count; f++) { z[f] = new float[steps + 1]; y[f] = new float[steps + 1]; }
            for (int i = 0; i <= steps; i++)
            {
                ResetBind(transforms, bind);
                clip.SampleAnimation(model, Mathf.Min(clip.length * i / steps, clip.length - 0.00001f));
                for (int f = 0; f < count; f++)
                {
                    var p = ChainPosition(toes[f]);
                    z[f][i] = p.z;
                    y[f][i] = p.y;
                }
            }
            float dt = clip.length / steps;
            double sum = 0;
            int n = 0;
            for (int f = 0; f < count; f++)
            {
                float minY = y[f].Min();
                for (int i = 0; i < steps; i++)
                {
                    if (y[f][i] > minY + 0.03f || y[f][i + 1] > minY + 0.03f) continue;
                    sum += -(z[f][i + 1] - z[f][i]) / dt;
                    n++;
                }
            }
            return n > 0 ? (float)(sum / n) : 0f;
        }

        static void ResetBind(Transform[] transforms, Dictionary<Transform, LocalTRS> bind)
        {
            foreach (var t in transforms)
            {
                if (t == null || !bind.TryGetValue(t, out var b)) continue;
                t.localPosition = b.position;
                t.localRotation = b.rotation;
                t.localScale = b.scale;
            }
        }

        // Deriva NETA del clip: distancia horizontal entre el primer y el ultimo punto. Es lo
        // que hay que anular para que el clip quede en el sitio; el balanceo intermedio del
        // cuerpo es parte de la animacion y NO se toca.
        static float NetDrift(List<Vector3> points)
        {
            if (points == null || points.Count < 2) return 0f;
            return HorizontalDistance(points[0], points[points.Count - 1]);
        }

        static float MaxHorizontal(List<Vector3> points)
        {
            if (points == null || points.Count == 0) return 0f;
            var p0 = points[0];
            float max = 0f;
            for (int i = 1; i < points.Count; i++)
                max = Mathf.Max(max, HorizontalDistance(p0, points[i]));
            return max;
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x;
            float dz = b.z - a.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static Vector3 ChainPosition(Transform t)
        {
            var m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
            for (var p = t.parent; p != null; p = p.parent)
                m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
            return m.GetColumn(3);
        }

        static void CleanOwnedAssets(string id)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { OutDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path).StartsWith(id + "_", StringComparison.Ordinal))
                    AssetDatabase.DeleteAsset(path);
            }
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/KOM/Animation"))
                AssetDatabase.CreateFolder("Assets/KOM", "Animation");
            if (!AssetDatabase.IsValidFolder(ClipsDir))
                AssetDatabase.CreateFolder("Assets/KOM/Animation", "Clips");
            if (!AssetDatabase.IsValidFolder(OutDir))
                AssetDatabase.CreateFolder(ClipsDir, "InPlace");
        }
    }
}
