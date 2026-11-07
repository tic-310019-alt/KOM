using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KOM.EditorTools
{
    // Capturas del ESCENARIO EN JUEGO: la camara de combate real, con el HUD, mas dos
    // encuadres de diagnostico (plano general y contraluz bajo).
    //
    // Ademas mide dos cosas que a ojo se escapan y que fueron justo los dos fallos de la
    // primera iteracion:
    //   - DONDE QUEDA EL SUELO bajo cada luchador (raycast): prueba que la plataforma de
    //     pelea esta a la altura de los pies, ni flotando ni enterrada;
    //   - la SATURACION de la imagen: prueba que el mundo va en blanco y negro y que el
    //     color solo lo ponen los personajes y los VFX.
    public static class KomStageShot
    {
        const string SceneDir = "Assets/Scenes/KOM";
        const string OutDir = "Logs/stages";
        const int W = 1600;
        const int H = 900;

        static readonly string[] DefaultStages = { "la_madrugadora", "bellas_artes", "zocalo" };

        struct Shot
        {
            public float MeanSat;
            public float ColorfulPct;
        }

        [MenuItem("King of Music/Diagnostico/40 - Capturas de escenario", priority = 240)]
        public static void MenuCaptureAll()
        {
            var report = Capture(DefaultStages);
            Debug.Log(report);
        }

        public static string Capture(string[] stageIds)
        {
            Directory.CreateDirectory(OutDir);
            // Los .json se cachean en KomData: sin recargar, un cambio de encuadre en
            // stages.json no se ve hasta reiniciar el editor.
            KomData.Reload();
            var report = new StringBuilder();
            report.AppendLine("KOM ESCENARIOS · camara de combate real + diagnosticos");
            report.AppendLine("sat = saturacion media de la imagen (0 = gris puro); color% = pixeles con saturacion > 0.18");
            foreach (var id in stageIds)
            {
                try { CaptureOne(id, report); }
                catch (Exception e)
                {
                    report.AppendLine($"{id}: ERROR {e.Message}");
                    Debug.LogError($"[KOM][STAGE] {id}: {e}");
                }
            }
            File.WriteAllText($"{OutDir}/report.txt", report.ToString());
            Debug.Log($"[KOM][STAGE] {OutDir}/report.txt");
            return report.ToString();
        }

        static void CaptureOne(string stageId, StringBuilder report)
        {
            var def = KomData.GetStage(stageId);
            if (def == null) { report.AppendLine($"{stageId}: no existe en stages.json"); return; }
            if (string.IsNullOrEmpty(def.prefabResource))
            {
                report.AppendLine($"{stageId}: sin prefabResource (escenario pendiente de exportar)");
                return;
            }

            EditorSceneManager.OpenScene($"{SceneDir}/Combat.unity", OpenSceneMode.Single);
            var controller = UnityEngine.Object.FindAnyObjectByType<CombatController>();
            if (controller == null) { report.AppendLine($"{stageId}: falta CombatController en la escena"); return; }

            controller.BuildEditorPreview("juan_grabriel", stageId);

            var cam = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (cam == null) { report.AppendLine($"{stageId}: sin camara"); return; }

            // El encuadre se resuelve a 16:9: se fija el aspecto ANTES de medir y se repite
            // el Snap para que la distancia no dependa del tamano de la GameView del editor.
            cam.ResetAspect();
            cam.aspect = W / (float)H;
            var rig = UnityEngine.Object.FindAnyObjectByType<CombatCameraRig>();
            if (rig != null) rig.Snap();

            // El HUD es ScreenSpaceOverlay: en Overlay no entra en el render de una camara.
            // Se pasa a ScreenSpaceCamera apuntando a la camara de combate.
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>())
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 0.9f;
            }

            PoseIdle();

            report.AppendLine($"--- {stageId} ({def.displayName}) ---");
            report.AppendLine($"  encuadre: giroY={def.prefabRotationY} giroX={def.prefabRotationX} " +
                              $"offset=({def.prefabOffsetX:F2},{def.prefabOffsetY:F2},{def.prefabOffsetZ:F2}) " +
                              $"escala={def.prefabScale:F2} luz={def.lightScale:F2}");

            var fighters = UnityEngine.Object.FindObjectsByType<Fighter>();
            var probes = new System.Collections.Generic.List<Vector3>();
            foreach (var f in fighters) probes.Add(f.transform.position);

            // Mapa del suelo: si el escenario tiene tarima, el mapa la delata. Es lo que
            // prueba que la plataforma de pelea (y no la del escenario) es la que queda a
            // la altura de los pies.
            var grid = new Vector3[9];
            int gi = 0;
            for (int iz = -1; iz <= 1; iz++)
                for (int ix = -1; ix <= 1; ix++)
                    grid[gi++] = new Vector3(ix * 3f, 0f, iz * 3f);
            foreach (var g in grid) probes.Add(g);

            var floors = FloorProbe(probes);

            for (int i = 0; i < fighters.Length; i++)
            {
                var f = fighters[i];
                var p = f.transform.position;
                float surface = floors[i];
                report.AppendLine($"  {f.name,-24} pos=({p.x:F2},{p.y:F2},{p.z:F2})  " +
                                  $"suelo={surface:F3}  hueco={(p.y - surface):F3}");
            }

            report.AppendLine("  mapa de suelo (x de -3 a 3, z de -3 a 3):");
            for (int iz = 0; iz < 3; iz++)
            {
                string row = "";
                for (int ix = 0; ix < 3; ix++) row += floors[fighters.Length + iz * 3 + ix].ToString("F2").PadLeft(7);
                report.AppendLine($"    z={(iz - 1) * 3,3} {row}");
            }

            // PERFIL EN Z: donde cae de verdad la plataforma de pelea. Sin esto se puede
            // dar por bueno un encuadre en el que los luchadores estan en el suelo de la
            // sala y la tarima se quedo 15 m mas atras.
            var zPoints = new System.Collections.Generic.List<Vector3>();
            var zValues = new System.Collections.Generic.List<float>();
            for (float z = -10f; z <= 44f; z += 2f) { zPoints.Add(new Vector3(0f, 0f, z)); zValues.Add(z); }
            var zProfile = FloorProbe(zPoints);
            var line = new StringBuilder();
            for (int i = 0; i < zProfile.Length; i++)
            {
                line.Append($"{zValues[i]:F0}:{zProfile[i]:F2}  ");
                if (i % 7 == 6) { report.AppendLine("  perfil Z " + line); line.Clear(); }
            }
            if (line.Length > 0) report.AppendLine("  perfil Z " + line);

            report.AppendLine("  superficies grandes y bajas (candidatas a plataforma):");
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>())
            {
                var b = r.bounds;
                if (b.max.y > 3f) continue;
                if (b.size.x * b.size.z < 25f) continue;
                if (r.name.StartsWith("GroundShadow") || r.name.StartsWith("GroundRing")) continue;
                report.AppendLine($"    {r.name,-30} x[{b.min.x:F1},{b.max.x:F1}] y[{b.min.y:F2},{b.max.y:F2}] z[{b.min.z:F1},{b.max.z:F1}]");
            }

            var main = Save(cam, $"{OutDir}/{stageId}_game.png", W, H);
            report.AppendLine($"  game.png  sat={main.MeanSat:F3}  color%={main.ColorfulPct:F1}");

            // Plano general: lee la composicion completa (plataforma, fondo, techo). La altura
            // se ata a la del recinto (no a un numero fijo) para que siga sirviendo cuando el
            // escenario se normaliza a escala humana: a 11 m fijos la camara entraba DENTRO de
            // la parrilla de luces.
            float ceiling = 11f;
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>())
                if (r.name.StartsWith("Piso_Marmol") || r.name == "bellas_artes") ceiling = Mathf.Max(ceiling, r.bounds.max.y);
            var over = TempCamera(new Vector3(0f, ceiling * 0.58f, -8.2f), new Vector3(0f, 1.3f, 2.2f), 45f);
            var overShot = Save(over, $"{OutDir}/{stageId}_over.png", W, H);
            report.AppendLine($"  over.png  sat={overShot.MeanSat:F3}  color%={overShot.ColorfulPct:F1}");
            UnityEngine.Object.DestroyImmediate(over.gameObject);

            // Contraluz bajo: es el encuadre que delata piernas cortadas, pies hundidos y
            // al personaje recortado contra el fondo.
            var hero = TempCamera(new Vector3(1.9f, 0.62f, -3.6f), new Vector3(0f, 1.05f, 0.4f), 48f);
            var heroShot = Save(hero, $"{OutDir}/{stageId}_hero.png", W, H);
            report.AppendLine($"  hero.png  sat={heroShot.MeanSat:F3}  color%={heroShot.ColorfulPct:F1}");
            UnityEngine.Object.DestroyImmediate(hero.gameObject);
        }

        // El escenario NO tiene colliders (el combate es cinematico, no fisico), asi que un
        // raycast normal no sirve, y los bounds tampoco: el suelo puede venir fundido en
        // una malla que tambien incluye paredes y techo. Se ponen MeshCollider temporales
        // sobre las mallas del decorado, se lanzan todos los rayos y se quitan.
        // Si un rayo no encuentra nada devuelve NaN (no 0: 0 seria un suelo inventado).
        static float[] FloorProbe(System.Collections.Generic.List<Vector3> points)
        {
            var added = new System.Collections.Generic.List<MeshCollider>();
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>())
            {
                // Los quads de sombra y anillo estan a 2-4 cm del suelo: si entran, el
                // "hueco" sale negativo y parece que los pies estan hundidos.
                if (r.name.StartsWith("GroundShadow") || r.name.StartsWith("GroundRing")) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mc = r.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                added.Add(mc);
            }
            Physics.SyncTransforms();

            var result = new float[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                result[i] = Physics.Raycast(new Vector3(p.x, 4f, p.z), Vector3.down, out var hit, 12f)
                    ? hit.point.y
                    : float.NaN;
            }

            foreach (var mc in added) UnityEngine.Object.DestroyImmediate(mc);
            return result;
        }

        // Atajo para el puente: `eval 'KOM.EditorTools.KomStageShot.CaptureIntroDefault()'`.
        public static string CaptureIntroDefault()
        {
            // zooms = null -> usa el cameraIntroZoom real de cada escenario.
            var report = CaptureIntro(DefaultStages, null);
            Debug.Log($"[KOM][STAGE] {OutDir}/intro_report.txt");
            return report;
        }

        // Panoramica de presentacion: captura el encuadre de INTRO a varios zoom para
        // decidir hasta donde se puede abrir sin descubrir el borde cortado del decorado.
        // Devuelve, por zoom, cuantos rayos del borde inferior/lateral se pierden en el
        // vacio (0 = el decorado cubre todo el cuadro).
        public static string CaptureIntro(string[] stageIds, float[] zooms)
        {
            Directory.CreateDirectory(OutDir);
            KomData.Reload();
            var report = new StringBuilder();
            report.AppendLine("KOM INTRO · encuadre panoramico por zoom (miss = rayos al vacio)");
            foreach (var id in stageIds)
            {
                var def = KomData.GetStage(id);
                if (def == null || string.IsNullOrEmpty(def.prefabResource)) { report.AppendLine($"{id}: sin prefab"); continue; }
                try
                {
                    EditorSceneManager.OpenScene($"{SceneDir}/Combat.unity", OpenSceneMode.Single);
                    var controller = UnityEngine.Object.FindAnyObjectByType<CombatController>();
                    if (controller == null) { report.AppendLine($"{id}: falta CombatController"); continue; }
                    controller.BuildEditorPreview("juan_grabriel", id);
                    var cam = UnityEngine.Object.FindAnyObjectByType<Camera>();
                    var rig = UnityEngine.Object.FindAnyObjectByType<CombatCameraRig>();
                    if (cam == null || rig == null) { report.AppendLine($"{id}: falta camara/rig"); continue; }
                    cam.ResetAspect();
                    cam.aspect = W / (float)H;
                    PoseIdle();
                    report.AppendLine($"--- {id} (actual cameraIntroZoom={def.cameraIntroZoom:F2}) ---");
                    var list = zooms ?? new[] { def.cameraIntroZoom };
                    foreach (var zoom in list)
                    {
                        // Se muestrean los dos extremos y el medio del movimiento: el borde
                        // cortado puede aparecer en cualquier punto del recorrido, no solo
                        // en el plano mas abierto.
                        foreach (var k in new[] { 0f, 0.5f, 1f })
                        {
                            rig.DebugSetIntro(zoom, k);
                            string file = $"{OutDir}/intro_{id}_z{zoom:F2}_k{k:F1}.png";
                            var shot = Save(cam, file, W, H);
                            int miss = BorderMisses(cam, 24);
                            report.AppendLine($"  zoom={zoom:F2} k={k:F1} dist={Vector3.Distance(cam.transform.position, new Vector3(0f, def.cameraLookY, 0.5f)):F2} " +
                                              $"camY={cam.transform.position.y:F2} miss={miss} sat={shot.MeanSat:F3}");
                        }
                    }
                }
                catch (Exception e)
                {
                    report.AppendLine($"{id}: ERROR {e.Message}");
                }
            }
            File.WriteAllText($"{OutDir}/intro_report.txt", report.ToString());
            return report.ToString();
        }

        // Rayos desde la camara hacia el borde INFERIOR y los laterales bajos del cuadro.
        // Si alguno no encuentra decorado, ahi se ve el fondo: es el borde cortado del
        // escenario. El borde superior no se prueba: en el Zocalo (plaza abierta) el cielo
        // arriba es correcto.
        static int BorderMisses(Camera cam, int samples)
        {
            var added = new System.Collections.Generic.List<MeshCollider>();
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>())
            {
                if (r.name.StartsWith("GroundShadow") || r.name.StartsWith("GroundRing")) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mc = r.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
                added.Add(mc);
            }
            Physics.SyncTransforms();

            int miss = 0;
            var points = new System.Collections.Generic.List<Vector2>();
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                points.Add(new Vector2(t, 0.02f));          // borde inferior
                if (t <= 0.6f)
                {
                    points.Add(new Vector2(0.005f, t));     // lateral izquierdo bajo
                    points.Add(new Vector2(0.995f, t));     // lateral derecho bajo
                }
            }
            foreach (var p in points)
            {
                var ray = cam.ViewportPointToRay(new Vector3(p.x, p.y, 0f));
                if (!Physics.Raycast(ray, out _, 200f, ~0, QueryTriggerInteraction.Ignore)) miss++;
            }

            foreach (var mc in added) UnityEngine.Object.DestroyImmediate(mc);
            return miss;
        }

        static void PoseIdle()
        {
            foreach (var animator in UnityEngine.Object.FindObjectsByType<Animator>())
            {
                if (animator.runtimeAnimatorController == null) continue;
                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Play("Idle", 0, 0f);
                for (int i = 0; i < 24; i++) animator.Update(1f / 60f);
            }
            // En batch el skinning no se refresca solo: hay que forzarlo por render.
            foreach (var smr in UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>())
            {
                smr.forceMatrixRecalculationPerRender = true;
                smr.updateWhenOffscreen = true;
            }
        }

        static Camera TempCamera(Vector3 pos, Vector3 look, float fov)
        {
            var go = new GameObject("ShotCam", typeof(Camera));
            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = 140f;
            cam.allowMSAA = true;
            VisualMood.Apply(cam);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
            return cam;
        }

        static Shot Save(Camera cam, string path, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 4
            };
            rt.Create();

            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();

            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = prevTarget;

            File.WriteAllBytes(path, tex.EncodeToPNG());

            // Muestreo 1 de cada 7 pixeles: suficiente para el veredicto y rapido.
            var px = tex.GetPixels32();
            double sum = 0;
            int colorful = 0, n = 0;
            for (int i = 0; i < px.Length; i += 7)
            {
                var c = px[i];
                int max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                int min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                float s = max <= 0 ? 0f : (max - min) / (float)max;
                sum += s;
                if (s > 0.18f) colorful++;
                n++;
            }

            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            if (n == 0) return new Shot();
            return new Shot { MeanSat = (float)(sum / n), ColorfulPct = colorful * 100f / n };
        }
    }
}
