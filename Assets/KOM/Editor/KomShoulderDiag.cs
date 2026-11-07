using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KOM.EditorTools
{
    // Diagnostico: bind vs clip puro vs Animator + primeros planos del hombro.
    // Usa escena Single (no PreviewScene): en este Unity el render URP de una PreviewScene
    // sale en negro al hacer Camera.Render. Restaura la escena activa al terminar.
    public static class KomShoulderDiag
    {
        const string OutDir = "Logs/diag";
        const int Size = 700;

        static readonly string[] Watch =
        {
            "DEF-spine.003", "ORG-shoulder.L", "DEF-shoulder.L", "DEF-upper_arm.L", "DEF-upper_arm.L.001",
            "DEF-forearm.L", "DEF-forearm.L.001", "DEF-hand.L",
            "ORG-shoulder.R", "DEF-shoulder.R", "DEF-upper_arm.R", "DEF-upper_arm.R.001",
            "DEF-forearm.R", "DEF-forearm.R.001", "DEF-hand.R"
        };

        [MenuItem("King of Music/Diagnostico/20 - Cara/Pelo: bind vs clip vs Animator", priority = 230)]
        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            string previous = EditorSceneManager.GetActiveScene().path;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                foreach (var id in new[] { "juan_grabriel", "jose_jose", "alex_llora", "roman_ayala", "natanael_chano", "peso_plomo", "flavia" })
                    Diagnose(id, scene);
            }
            finally
            {
                if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
            }
            Debug.Log($"[KOM][SHOULDER] Capturas y reportes en {OutDir}");
        }

        static void Diagnose(string id, Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            if (prefab == null) return;
            GameObject go = null;
            GameObject camGo = null, keyGo = null, fillGo = null, rimGo = null;
            RenderTexture rt = null;
            var sb = new StringBuilder();
            try
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;
                var fighter = go.GetComponent<Fighter>();
                if (fighter != null) fighter.enabled = false;
                var animator = go.GetComponentInChildren<Animator>(true);
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr == null) return;
                animator.enabled = false;
                animator.Rebind();
                foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    s.forceMatrixRecalculationPerRender = true;
                    s.updateWhenOffscreen = true;
                }

                sb.AppendLine($"=== {id} ===");
                var watch = new List<Transform>();
                foreach (var n in Watch)
                {
                    var t = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n);
                    if (t != null) watch.Add(t);
                }
                var bindRot = new Dictionary<Transform, Quaternion>();
                var bindWorldRot = new Dictionary<Transform, Quaternion>();
                var bindPos = new Dictionary<Transform, Vector3>();
                foreach (var t in watch) { bindRot[t] = t.localRotation; bindWorldRot[t] = t.rotation; bindPos[t] = t.localPosition; }
                var shoulderBind = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "DEF-shoulder.L");
                var shoulderBindR = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "DEF-shoulder.R");
                var faceRoot = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "ORG-face");
                var faceBones = faceRoot != null ? faceRoot.GetComponentsInChildren<Transform>(true).ToArray() : new Transform[0];
                var faceBind = new Dictionary<Transform, (Vector3 pos, Quaternion rot, Vector3 scale)>();
                foreach (var t in faceBones) faceBind[t] = (t.localPosition, t.localRotation, t.localScale);
                sb.AppendLine($"  facialRig root={(faceRoot != null ? faceRoot.name : "SIN ORG-face")} bones={faceBones.Length}");

                camGo = new GameObject("DiagCam", typeof(Camera));
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.10f, 0.10f, 0.12f);
                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 30f;
                cam.allowHDR = false;
                cam.allowMSAA = true;
                keyGo = Light(scene, "Key", 2.1f, new Color(1f, 0.95f, 0.88f), Quaternion.Euler(35f, 150f, 0f));
                fillGo = Light(scene, "Fill", 0.8f, new Color(0.55f, 0.7f, 1f), Quaternion.Euler(10f, -40f, 0f));
                rimGo = Light(scene, "Rim", 1.1f, new Color(1f, 0.8f, 0.5f), Quaternion.Euler(20f, -10f, 0f));
                rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                rt.Create();
                cam.targetTexture = rt;

                var eye = new Vector3(0f, 1.30f, 0f);
                Action<string, Vector3> shot = (name, pos) =>
                {
                    camGo.transform.position = pos;
                    camGo.transform.rotation = Quaternion.LookRotation((eye - pos).normalized, Vector3.up);
                    cam.Render(); cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes($"{OutDir}/{id}_{name}.png", tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                };
                Action<string> trio = tag =>
                {
                    shot(tag + "_34", new Vector3(1.15f, 1.38f, -1.55f));
                    shot(tag + "_front", new Vector3(0f, 1.38f, -1.9f));
                    shot(tag + "_side", new Vector3(1.9f, 1.38f, 0f));
                };
                // Primeros planos de CARA (el personaje mira a +Z).
                Action<string> headTrio = tag =>
                {
                    Vector3 headEye = new Vector3(0f, 1.60f, 0f);
                    void headShot(string n, Vector3 p)
                    {
                        camGo.transform.position = p;
                        camGo.transform.rotation = Quaternion.LookRotation((headEye - p).normalized, Vector3.up);
                        cam.fieldOfView = 20f;
                        cam.Render(); cam.Render();
                        var prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                        tex.Apply();
                        RenderTexture.active = prev;
                        File.WriteAllBytes($"{OutDir}/{id}_{n}.png", tex.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(tex);
                    }
                    headShot(tag + "_headfront", new Vector3(0f, 1.60f, 1.35f));
                    headShot(tag + "_head34", new Vector3(0.95f, 1.62f, 1.05f));
                    headShot(tag + "_headside", new Vector3(1.35f, 1.60f, 0.05f));
                    cam.fieldOfView = 30f;
                };
                // Primeros planos relativos a la CABEZA REAL: las poses de especial
                // inclinan/bajan la cabeza y el encuadre fijo la dejaba fuera.
                Action<string, Vector3> headTrioAt = (tag, eye) =>
                {
                    void headShotAt(string n, Vector3 p)
                    {
                        camGo.transform.position = p;
                        camGo.transform.rotation = Quaternion.LookRotation((eye - p).normalized, Vector3.up);
                        cam.fieldOfView = 20f;
                        cam.Render(); cam.Render();
                        var prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                        tex.Apply();
                        RenderTexture.active = prev;
                        File.WriteAllBytes($"{OutDir}/{id}_{n}.png", tex.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(tex);
                    }
                    Vector3 fwd = go.transform.forward;
                    Vector3 right = go.transform.right;
                    headShotAt(tag + "_headfront", eye + fwd * 0.95f);
                    headShotAt(tag + "_head34", eye + fwd * 0.8f + right * 0.55f);
                    headShotAt(tag + "_headside", eye + right * 0.95f);
                    cam.fieldOfView = 30f;
                };
                Action<string> dump = tag =>
                {
                    sb.AppendLine($"  --- {tag}");
                    foreach (var t in watch)
                    {
                        var dq = Quaternion.Inverse(bindRot[t]) * t.localRotation;
                        float twist = TwistY(dq);
                        float swing = Swing(dq);
                        sb.AppendLine($"    {t.name,-20} dLocal={Quaternion.Angle(bindRot[t], t.localRotation),6:F1} twist={twist,6:F1} swing={swing,6:F1} dWorld={Quaternion.Angle(bindWorldRot[t], t.rotation),6:F1} dPos={(t.localPosition - bindPos[t]).magnitude:F5}");
                    }
                };

                trio("bind");

                var idleClean = AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/KOM/Animation/Clips/{id}_Idle.anim");
                var rawIdle = AssetDatabase.LoadAllAssetsAtPath($"Assets/Art/Characters/{id}.fbx").OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name == "Idle" || c.name.EndsWith("|Idle") || c.name.EndsWith("Idle"));
                if (idleClean != null)
                {
                    // Vertices del hombro/espalda alta que mas se desplazan al pasar de bind a Idle.
                    var mesh = smr.sharedMesh;
                    var verts = mesh.vertices;
                    var bw = mesh.boneWeights;
                    var bones = smr.bones;
                    var bindWorld = new Vector3[verts.Length];
                    for (int v = 0; v < verts.Length; v++) bindWorld[v] = smr.transform.TransformPoint(verts[v]);
                    var bmin = bindWorld[0]; var bmax = bindWorld[0];
                    for (int v = 1; v < bindWorld.Length; v++) { bmin = Vector3.Min(bmin, bindWorld[v]); bmax = Vector3.Max(bmax, bindWorld[v]); }
                    sb.AppendLine($"  mesh verts={verts.Length} smr={smr.name} lossy={smr.transform.lossyScale.ToString("F3")} bindBounds=[{bmin.ToString("F2")} .. {bmax.ToString("F2")}]");

                    foreach (float tt in new[] { 0.8f, 2.0f })
                    {
                        idleClean.SampleAnimation(go, tt);
                        Physics.SyncTransforms();
                        var tag = $"idleclip_{tt:0.0}";
                        trio(tag);
                        dump(tag);

                        var rows = new List<(float dist, int v, Vector3 bindW, Vector3 nowW, string bone, string weights)>();
                        var baked = new Mesh();
                        smr.BakeMesh(baked, false);
                        var bakedVerts = baked.vertices;
                        float bakeScale = Mathf.Max(0.0001f, smr.transform.lossyScale.x);
                        for (int v = 0; v < verts.Length; v++)
                        {
                            var b = bindWorld[v];
                            if (b.y < 1.20f || b.y > 1.55f) continue;
                            if (Mathf.Abs(b.x) < 0.09f || Mathf.Abs(b.x) > 0.36f) continue;
                            if (b.z < -0.28f || b.z > 0.20f) continue;
                            var now = smr.transform.TransformPoint(bakedVerts[v] / bakeScale);
                            float d = Vector3.Distance(b, now);
                            if (d < 0.02f) continue;
                            int best = -1; float bestW = 0f;
                            Acc(bw[v].weight0, bw[v].boneIndex0, ref best, ref bestW);
                            Acc(bw[v].weight1, bw[v].boneIndex1, ref best, ref bestW);
                            Acc(bw[v].weight2, bw[v].boneIndex2, ref best, ref bestW);
                            Acc(bw[v].weight3, bw[v].boneIndex3, ref best, ref bestW);
                            string bn = best >= 0 && best < bones.Length && bones[best] != null ? bones[best].name : "?";
                            if (bn.Contains("temple") || bn.Contains("ear") || bn.Contains("forehead") || bn.Contains("spine.006") || bn.Contains("head") || bn.Contains("neck")) continue;
                            string ws = FmtW(bw[v], bones);
                            rows.Add((d, v, b, now, bn, ws));
                        }
                        UnityEngine.Object.DestroyImmediate(baked);
                        sb.AppendLine($"  --- TOP desplazamientos hombro {tag} (bind -> deformado)");
                        foreach (var r in rows.OrderByDescending(x => x.dist).Take(18))
                            sb.AppendLine($"    d={r.dist:F3} v={r.v} bind={r.bindW.ToString("F3")} now={r.nowW.ToString("F3")} dominante={r.bone} pesos=[{r.weights}]");

                        var byBone = new Dictionary<string, (float max, float sum, int n)>();
                        foreach (var r in rows)
                        {
                            byBone.TryGetValue(r.bone, out var acc);
                            acc.max = Mathf.Max(acc.max, r.dist); acc.sum += r.dist; acc.n++;
                            byBone[r.bone] = acc;
                        }
                        sb.AppendLine($"  --- desplazamiento medio por hueso dominante {tag}");
                        foreach (var kv in byBone.OrderByDescending(k => k.Value.max).Take(10))
                            sb.AppendLine($"    {kv.Key,-22} n={kv.Value.n,4} avg={kv.Value.sum / kv.Value.n:F3} max={kv.Value.max:F3}");
                    }

                    // Referencia BUENA real: Idle_FirstRaw (copia exacta del primer commit).
                    var idleFirstRaw = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                        $"Assets/KOM/Animation/Clips/InPlace/{id}_Idle_FirstRaw.anim");
                    if (idleFirstRaw != null)
                    {
                        animator.Rebind();
                        idleFirstRaw.SampleAnimation(go, idleFirstRaw.length * 0.33f);
                        Physics.SyncTransforms();
                        trio("idleraw");
                        headTrio("idleraw");
                        dump("idleraw");
                    }

                    // Gate visual del clip de caminar en guardia: es el estado que el
                    // usuario ve al andar. Debe conservar la cara/craneo sin inflar.
                    var walkGuard = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                        $"Assets/KOM/Animation/Clips/InPlace/{id}_WalkGuard_RawWalk.anim");
                    if (walkGuard != null)
                    {
                        animator.Rebind();
                        walkGuard.SampleAnimation(go, walkGuard.length * 0.5f);
                        Physics.SyncTransforms();
                        trio("walkguard");
                        headTrio("walkguard");
                        dump("walkguard");
                    }
                    var rawWalkGuard = AssetDatabase.LoadAllAssetsAtPath($"Assets/Art/Characters/{id}.fbx")
                        .OfType<AnimationClip>().FirstOrDefault(c => c.name == "WalkGuard" || c.name.EndsWith("|WalkGuard"));
                    if (rawWalkGuard != null)
                    {
                        animator.Rebind();
                        rawWalkGuard.SampleAnimation(go, rawWalkGuard.length * 0.5f);
                        Physics.SyncTransforms();
                        trio("walkeraw");
                        headTrio("walkeraw");
                        dump("walkeraw");
                    }
                    var blockRaw = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                        $"Assets/KOM/Animation/Clips/InPlace/{id}_Block_FirstRaw.anim");
                    if (blockRaw != null)
                    {
                        animator.Rebind();
                        blockRaw.SampleAnimation(go, blockRaw.length * 0.5f);
                        Physics.SyncTransforms();
                        headTrio("blockraw");
                    }

                    // Prueba de causalidad: limitar la clavicula (ORG-shoulder) y compensar
                    // el brazo para conservar EXACTAMENTE la orientacion mundial del upper arm.
                    foreach (float factor in new[] { 0f, 0.3f })
                    {
                        idleClean.SampleAnimation(go, 0.8f);
                        Physics.SyncTransforms();
                        var targets = new Dictionary<Transform, Quaternion>();
                        foreach (var side in new[] { "L", "R" })
                        {
                            var org = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "ORG-shoulder." + side);
                            var upper = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "DEF-upper_arm." + side);
                            if (org == null || upper == null) continue;
                            targets[upper] = upper.rotation;
                            org.localRotation = Quaternion.Slerp(bindRot[org], org.localRotation, factor);
                        }
                        Physics.SyncTransforms();
                        foreach (var kv in targets)
                            kv.Key.localRotation = Quaternion.Inverse(kv.Key.parent.rotation) * kv.Value;
                        Physics.SyncTransforms();
                        string tag = factor <= 0.001f ? "idleclip_0.8_clav0" : $"idleclip_0.8_clav{factor:0.0}";
                        trio(tag);
                        dump(tag);
                    }

                    // Prueba: quitar SOLO el twist de la clavicula (conserva el swing) y
                    // compensar el upper arm para conservar la orientacion del brazo.
                    {
                        idleClean.SampleAnimation(go, 0.8f);
                        Physics.SyncTransforms();
                        var targets = new Dictionary<Transform, Quaternion>();
                        foreach (var side in new[] { "L", "R" })
                        {
                            var org = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "ORG-shoulder." + side);
                            var upper = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "DEF-upper_arm." + side);
                            if (org == null || upper == null) continue;
                            targets[upper] = upper.rotation;
                            var dq = Quaternion.Inverse(bindRot[org]) * org.localRotation;
                            var twist = TwistQuatY(dq);
                            var swing = dq * Quaternion.Inverse(twist);
                            org.localRotation = bindRot[org] * swing;
                        }
                        Physics.SyncTransforms();
                        foreach (var kv in targets)
                            kv.Key.localRotation = Quaternion.Inverse(kv.Key.parent.rotation) * kv.Value;
                        Physics.SyncTransforms();
                        trio("idleclip_0.8_clavNoTwist");
                        dump("idleclip_0.8_clavNoTwist");
                    }

                    // Prueba: congelar TODO el subrig facial (ORG-face y descendientes) en bind.
                    // La cabeza debe seguir girando por DEF-spine.006; la cara queda rigida.
                    {
                        idleClean.SampleAnimation(go, 0.8f);
                        Physics.SyncTransforms();
                        headTrio("idleclip_0.8");
                        foreach (var t in faceBones)
                        {
                            t.localPosition = faceBind[t].pos;
                            t.localRotation = faceBind[t].rot;
                            t.localScale = faceBind[t].scale;
                        }
                        Physics.SyncTransforms();
                        headTrio("idleclip_0.8_facefrozen");
                    }
                }

                if (rawIdle != null && idleClean != null)
                {
                    rawIdle.SampleAnimation(go, Mathf.Min(0.8f, rawIdle.length - 0.01f));
                    Physics.SyncTransforms();
                    trio("idleraw_0.8");
                    dump("idleraw_0.8");
                }

                // Cara/pelo en el frame de liberacion del especial que JUEGA.
                var specialDef = KomData.GetCharacter(id);
                if (specialDef != null && specialDef.HasSpecial)
                {
                    string state = specialDef.special.animatorState;
                    var specialClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                        $"Assets/KOM/Animation/Clips/InPlace/{id}_{state}_Clean.anim");
                    if (specialClip != null)
                    {
                        float t = Mathf.Clamp01(specialDef.special.releaseTime /
                            Mathf.Max(0.01f, specialDef.special.duration)) * specialClip.length;
                        specialClip.SampleAnimation(go, t);
                        Physics.SyncTransforms();
                        var headBone = go.GetComponentsInChildren<Transform>(true)
                            .FirstOrDefault(x => x.name == "DEF-spine.006");
                        Vector3 specialEye = headBone != null ? headBone.position : new Vector3(0f, 1.5f, 0f);
                        headTrioAt($"special_{state}", specialEye);
                        dump($"special_{state}");
                    }
                }

                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.SetLayerWeight(0, 1f);
                if (animator.layerCount > 1) animator.SetLayerWeight(1, 0f);
                animator.Play("Idle", 0, 0f);
                for (int i = 0; i < 90; i++) animator.Update(1f / 60f);
                trio("idlecontroller");
                dump("idlecontroller");

                AuditWeights(sb, smr, shoulderBind, "DEF-shoulder.L");
                AuditWeights(sb, smr, shoulderBindR, "DEF-shoulder.R");
            }
            catch (Exception e)
            {
                sb.AppendLine("ERROR " + e);
            }
            finally
            {
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (keyGo != null) UnityEngine.Object.DestroyImmediate(keyGo);
                if (fillGo != null) UnityEngine.Object.DestroyImmediate(fillGo);
                if (rimGo != null) UnityEngine.Object.DestroyImmediate(rimGo);
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            File.WriteAllText($"{OutDir}/{id}_shoulder_report.txt", sb.ToString());
        }

        static void AuditWeights(StringBuilder sb, SkinnedMeshRenderer smr, Transform shoulder, string label)
        {
            if (shoulder == null) return;
            var shoulderWorld = shoulder.position;
            sb.AppendLine($"  --- pesos cerca de {label} ({shoulderWorld.ToString("F3")}, r=0.14)");
            var mesh = smr.sharedMesh;
            var weights = mesh.boneWeights;
            var verts = mesh.vertices;
            var bones = smr.bones;
            var counts = new Dictionary<string, int>();
            for (int v = 0; v < verts.Length; v++)
            {
                var wp = smr.transform.TransformPoint(verts[v]);
                if ((wp - shoulderWorld).magnitude > 0.14f) continue;
                var w = weights[v];
                Add(counts, bones, w.weight0, w.boneIndex0);
                Add(counts, bones, w.weight1, w.boneIndex1);
                Add(counts, bones, w.weight2, w.boneIndex2);
                Add(counts, bones, w.weight3, w.boneIndex3);
            }
            foreach (var kv in counts.OrderByDescending(k => k.Value).Take(10))
                sb.AppendLine($"    {kv.Key,-22} verts={kv.Value}");
        }

        static void Acc(float w, int index, ref int best, ref float bestW)
        {
            if (w > bestW && index >= 0) { bestW = w; best = index; }
        }

        static string FmtW(BoneWeight w, Transform[] bones)
        {
            var parts = new List<string>();
            void One(float ww, int bi)
            {
                if (ww < 0.01f || bi < 0 || bi >= bones.Length) return;
                parts.Add($"{(bones[bi] != null ? bones[bi].name : "?")}:{ww:F2}");
            }
            One(w.weight0, w.boneIndex0);
            One(w.weight1, w.boneIndex1);
            One(w.weight2, w.boneIndex2);
            One(w.weight3, w.boneIndex3);
            return string.Join(", ", parts);
        }

        static float TwistY(Quaternion q)
        {
            float mag = Mathf.Sqrt(q.y * q.y + q.w * q.w);
            if (mag < 1e-6f) return 180f;
            return Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(q.y, q.w) * Mathf.Rad2Deg);
        }

        static Quaternion TwistQuatY(Quaternion q)
        {
            float mag = Mathf.Sqrt(q.y * q.y + q.w * q.w);
            if (mag < 1e-6f) return Quaternion.identity;
            return new Quaternion(0f, q.y / mag, 0f, q.w / mag);
        }

        static float Swing(Quaternion q)
        {
            var swing = q * Quaternion.Inverse(TwistQuatY(q));
            return Quaternion.Angle(Quaternion.identity, swing);
        }

        static GameObject Light(Scene scene, string name, float intensity, Color color, Quaternion rot)
        {
            var go = new GameObject(name, typeof(Light));
            SceneManager.MoveGameObjectToScene(go, scene);
            var l = go.GetComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = intensity;
            l.color = color;
            go.transform.rotation = rot;
            return go;
        }

        static void Add(Dictionary<string, int> counts, Transform[] bones, float weight, int index)
        {
            if (weight < 0.05f || index < 0 || index >= bones.Length) return;
            var n = bones[index] != null ? bones[index].name : "?";
            counts.TryGetValue(n, out int c);
            counts[n] = c + 1;
        }
    }
}
