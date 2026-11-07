using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KOM.EditorTools
{
    // AUDITORIA DE ESCALA Y ENCUADRE.
    //
    // Responde con numeros a la pregunta "se ve gigante el escenario?". Mide, en la escena
    // de combate real:
    //   1. la altura real de los luchadores (mundo),
    //   2. la huella de la plataforma de pelea y su proporcion respecto al area jugable,
    //   3. la altura del decorado sobre la plataforma,
    //   4. cuanto ocupa un luchador en pantalla (por proyeccion Y por silueta de pixeles),
    //   5. el pitch de la camara y el contacto pies-suelo.
    //
    // La silueta se obtiene comparando el render con y sin luchadores: es independiente del
    // color del traje y del HUD, asi que mide lo que el jugador ve, no lo que la matematica
    // cree que deberia ver. Si la proyeccion y la silueta no coinciden, hay un error.
    public static class KomScaleAudit
    {
        const string ScenePath = "Assets/Scenes/KOM/Combat.unity";
        const string OutDir = "Logs/scale";
        const int W = 1600;
        const int H = 900;

        // ---- Referencias de diseno (metros / fracciones) -------------------------------
        // Un luchador humano real: 1.70-1.90 m. El juego usa ~1.85 m.
        public const float FighterHeightMin = 1.60f;
        public const float FighterHeightMax = 1.95f;
        // La superficie de pelea debe sobresalir del area jugable lo justo para no caerse,
        // no para que sobre espacio: por encima de 1.45x el suelo vacio domina el encuadre.
        public const float PlatformArenaRatioMin = 1.10f;
        public const float PlatformArenaRatioMax = 1.45f;
        // Cobertura vertical del luchador en pantalla (separacion neutra ~4.2 m).
        public const float FillMin = 0.38f;
        public const float FillMax = 0.54f;
        // Inclinacion de la camara respecto a la horizontal.
        public const float PitchMin = 7f;
        public const float PitchMax = 17f;
        // Fraccion de la altura de pantalla ocupada por el suelo (borde lejano de la
        // plataforma). Por encima de 0.55 el cuadro es un plano de suelo.
        public const float FloorShareMin = 0.30f;
        public const float FloorShareMax = 0.55f;
        // Contacto pies-suelo.
        public const float FloorGapTolerance = 0.03f;
        // Altura del recinto sobre la plataforma (un foro real: 8-25 m).
        public const float CeilingMin = 6f;
        public const float CeilingMax = 30f;
        // Diferencia tolerada entre la cobertura proyectada y la medida en pixeles.
        public const float FillAgreement = 0.08f;
        // Margen lateral minimo (fraccion del ancho) con los luchadores en los bordes.
        public const float SpreadMarginMin = 0.02f;

        public struct Metrics
        {
            public string Id;
            public bool Valid;

            public float FighterHeight;
            public float PlatformWidth;
            public float PlatformDepth;
            public float PlatformRise;
            public float PlatformY;
            public float PlatformFarZ;
            public float ArenaHalfWidth;
            public float ArenaSurfaceWidth;
            public float Ceiling;
            public float FloorGap;

            public float CamDistance;
            public float CamHeight;
            public float CamPitch;
            public float Fov;
            public float Fill;
            public Silhouette Visual;
            public Silhouette Spread;
            public float SpreadSeparation;
            public float HeadVpY;
            public float FeetVpY;
            public float FloorShare;
            public float Separation;
        }

        [MenuItem("King of Music/Diagnostico/41 - Auditoria de escala", priority = 241)]
        public static void MenuAudit()
        {
            var report = Audit(new[] { "la_madrugadora", "bellas_artes", "zocalo" }, true);
            Debug.Log(report);
            EditorUtility.DisplayDialog("King of Music", "Auditoria de escala en Logs/scale/report.txt", "OK");
        }

        public static string Audit(string[] stageIds, bool capture)
        {
            Directory.CreateDirectory(OutDir);
            KomData.Reload();

            var sb = new StringBuilder();
            sb.AppendLine("KOM ESCALA · auditoria de proporcion personaje/escenario y encuadre");
            sb.AppendLine("referencias: luchador 1.60-1.95 m | superficie/arena 1.10-1.45 | fill 0.38-0.54 | pitch 7-17 deg");
            sb.AppendLine();

            int fails = 0;
            foreach (var id in stageIds)
            {
                Metrics m;
                try { m = MeasureOne(id, capture, sb); }
                catch (Exception e)
                {
                    sb.AppendLine($"{id}: ERROR {e.Message}");
                    Debug.LogError($"[KOM][SCALE] {id}: {e}");
                    fails++;
                    continue;
                }
                if (!m.Valid) { fails++; continue; }
                fails += Report(sb, m);
                sb.AppendLine();
            }

            sb.AppendLine(fails == 0 ? "VERDICT: PASS" : $"VERDICT: FAIL ({fails} metricas fuera de rango)");
            File.WriteAllText($"{OutDir}/report.txt", sb.ToString());
            Debug.Log($"[KOM][SCALE] {OutDir}/report.txt");
            return sb.ToString();
        }

        static Metrics MeasureOne(string stageId, bool capture, StringBuilder sb)
        {
            var def = KomData.GetStage(stageId);
            var m = new Metrics { Id = stageId };
            if (def == null) { sb.AppendLine($"{stageId}: no existe en stages.json"); return m; }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var controller = UnityEngine.Object.FindAnyObjectByType<CombatController>();
            if (controller == null) { sb.AppendLine($"{stageId}: falta CombatController"); return m; }

            controller.BuildEditorPreview("juan_grabriel", stageId);
            var cam = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (cam == null) { sb.AppendLine($"{stageId}: sin camara"); return m; }

            PoseIdle();

            var fighters = UnityEngine.Object.FindObjectsByType<Fighter>();
            if (fighters.Length == 0) { sb.AppendLine($"{stageId}: sin luchadores"); return m; }

            // --- altura real del luchador: bounds de la malla animada sobre su origen ----
            float height = 0f;
            foreach (var f in fighters)
            {
                float h = 0f;
                foreach (var smr in f.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    h = Mathf.Max(h, smr.bounds.max.y - f.transform.position.y);
                height = Mathf.Max(height, h);
            }
            m.FighterHeight = height;
            m.ArenaHalfWidth = controller.ArenaHalfWidth;
            float refScale = def.arenaSurfaceRefScale > 0.01f ? def.arenaSurfaceRefScale : 1f;
            float worldScale = def.prefabScale > 0.01f ? def.prefabScale : 1f;
            m.ArenaSurfaceWidth = def.arenaSurfaceWidth * worldScale / refScale;

            // El encuadre se define a 16:9. Sin fijar el aspecto, la medida depende del tamano
            // de la GameView del editor y la proyeccion no coincide con el PNG renderizado.
            // Se vuelve a hacer Snap para que la distancia resuelta use este aspecto.
            cam.ResetAspect();
            cam.aspect = W / (float)H;
            var rig = UnityEngine.Object.FindAnyObjectByType<CombatCameraRig>();
            if (rig != null) rig.Snap();

            // --- plataforma de pelea ----------------------------------------------------
            var stageRoot = FindStageRoot(def);
            if (stageRoot == null) { sb.AppendLine($"{stageId}: no encuentro el prefab del escenario"); return m; }

            using (var probe = new StageProbe(stageRoot))
            {
                // La sonda arranca 1.5 m sobre los pies: por encima de ahi solo hay decorado
                // colgado (parrilla, focos) que no es suelo.
                float feetY = fighters[0].transform.position.y;
                var plateau = probe.FindPlateau(18f, 0.5f, feetY + 1.5f);
                if (!plateau.Found) { sb.AppendLine($"{stageId}: no encuentro plataforma"); return m; }

                m.PlatformWidth = plateau.MaxX - plateau.MinX;
                m.PlatformDepth = plateau.MaxZ - plateau.MinZ;
                m.PlatformRise = plateau.Rise;
                m.PlatformY = plateau.Height;
                m.PlatformFarZ = plateau.MaxZ;
                m.Ceiling = probe.Bounds.max.y - plateau.Height;

                // Contacto medido DONDE PISAN, no en el centro de la meseta: si la plataforma
                // se hubiera ido a otra cota, el hueco sale aqui.
                float gap = 0f;
                foreach (var f in fighters)
                {
                    float s = probe.SurfaceNear(f.transform.position.x, f.transform.position.z, f.transform.position.y);
                    if (!float.IsNaN(s)) gap = Mathf.Max(gap, Mathf.Abs(f.transform.position.y - s));
                }
                m.FloorGap = gap;
            }

            // --- encuadre ---------------------------------------------------------------
            var a = fighters[0].transform.position;
            var b = fighters.Length > 1 ? fighters[1].transform.position : a + Vector3.right * 4f;
            m.Separation = Mathf.Abs(a.x - b.x);

            float top = 0f, bottom = 1f;
            foreach (var f in fighters)
            {
                var feet = f.transform.position;
                var head = feet + Vector3.up * height;
                var vpFeet = cam.WorldToViewportPoint(feet);
                var vpHead = cam.WorldToViewportPoint(head);
                top = Mathf.Max(top, Mathf.Max(vpHead.y, vpFeet.y));
                bottom = Mathf.Min(bottom, Mathf.Min(vpHead.y, vpFeet.y));
            }
            m.Fill = top - bottom;
            m.HeadVpY = top;
            m.FeetVpY = bottom;

            var fwd = cam.transform.forward;
            m.CamPitch = -Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            m.CamHeight = cam.transform.position.y;
            m.Fov = cam.fieldOfView;
            var mid = (a + b) * 0.5f;
            m.CamDistance = Vector3.Dot(mid - cam.transform.position, fwd);

            // Reparto del cuadro: la altura en viewport del borde LEJANO de la plataforma es
            // la frontera suelo/decorado. Si sube demasiado, el encuadre es un plano de suelo
            // y el escenario se come la composicion.
            var farEdge = new Vector3(0f, m.PlatformY, m.PlatformFarZ);
            m.FloorShare = cam.WorldToViewportPoint(farEdge).y;

            if (capture)
            {
                m.Visual = CaptureAndMeasure(cam, stageId, fighters, "");

                // PEOR CASO: los dos en los bordes del area jugable. Es la prueba de que la
                // camara retrocede lo necesario y no recorta a nadie; el encuadre neutro no
                // lo detecta porque ahi siempre sobran margenes.
                var p0 = fighters[0].transform.position;
                var p1 = fighters.Length > 1 ? fighters[1].transform.position : p0 + Vector3.right * 2f;
                float hw = Mathf.Max(1f, m.ArenaHalfWidth * 0.98f);
                fighters[0].transform.position = new Vector3(-hw, p0.y, p0.z);
                if (fighters.Length > 1) fighters[1].transform.position = new Vector3(hw, p1.y, p1.z);
                if (rig != null) rig.Snap();
                m.Spread = CaptureAndMeasure(cam, stageId, fighters, "_spread");
                m.SpreadSeparation = hw * 2f;
                fighters[0].transform.position = p0;
                if (fighters.Length > 1) fighters[1].transform.position = p1;
                if (rig != null) rig.Snap();
            }

            m.Valid = true;
            return m;
        }

        static int Report(StringBuilder sb, Metrics m)
        {
            int fails = 0;
            float surface = m.ArenaSurfaceWidth > 0.01f ? m.ArenaSurfaceWidth : m.PlatformWidth;
            float ratio = m.ArenaHalfWidth > 0.01f ? surface / (m.ArenaHalfWidth * 2f) : 0f;

            sb.AppendLine($"--- {m.Id} ---");
            sb.AppendLine($"  luchador    alto={m.FighterHeight:F3} m          {Verdict(m.FighterHeight, FighterHeightMin, FighterHeightMax, ref fails)}");
            sb.AppendLine($"  plataforma  huella={m.PlatformWidth:F2} x {m.PlatformDepth:F2} m  alto={m.PlatformRise:F2} m  y={m.PlatformY:F3}");
            sb.AppendLine($"  arena       ancho jugable={m.ArenaHalfWidth * 2f:F2} m  superficie de pelea={surface:F2} m");
            sb.AppendLine($"  proporcion  superficie/arena={ratio:F2}x          {Verdict(ratio, PlatformArenaRatioMin, PlatformArenaRatioMax, ref fails)}");
            sb.AppendLine($"  recinto     techo sobre plataforma={m.Ceiling:F2} m {Verdict(m.Ceiling, CeilingMin, CeilingMax, ref fails)}");
            sb.AppendLine($"  contacto    |pies-suelo|={m.FloorGap:F3} m        {Verdict(m.FloorGap, -FloorGapTolerance, FloorGapTolerance, ref fails)}");
            // La cobertura que manda es la SILUETA (lo que se ve). La proyeccion usa la altura
            // de bind, asi que sirve de contraste, no de verdad absoluta.
            float fill = m.Visual.Fill > 0f ? m.Visual.Fill : m.Fill;
            sb.AppendLine($"  camara      fov={m.Fov:F1} dist={m.CamDistance:F2} m alto={m.CamHeight:F2} m pitch={m.CamPitch:F1} deg");
            sb.AppendLine($"  encuadre    separacion={m.Separation:F2} m  fill={fill:F3} ({fill * 100f:F0}% de la altura)  {Verdict(fill, FillMin, FillMax, ref fails)}");
            sb.AppendLine($"              suelo={m.FloorShare * 100f:F0}% del cuadro {Verdict(m.FloorShare, FloorShareMin, FloorShareMax, ref fails)}   " +
                          $"pitch {Verdict(m.CamPitch, PitchMin, PitchMax, ref fails)}");
            if (m.Visual.Fill > 0f)
            {
                float delta = Mathf.Abs(m.Visual.Fill - m.Fill);
                sb.AppendLine($"  proyeccion  fill={m.Fill:F3}  silueta pies={m.Visual.FeetY:F2} cabeza={m.Visual.HeadY:F2}  " +
                              $"delta={delta:F3} {Verdict(delta, 0f, FillAgreement, ref fails)}");
            }
            if (m.Spread.MaxX > 0f)
            {
                float left = m.Spread.MinX;
                float right = 1f - m.Spread.MaxX;
                float minMargin = Mathf.Min(left, right);
                sb.AppendLine($"  peor caso   separacion={m.SpreadSeparation:F1} m  margen izq={left * 100f:F1}% der={right * 100f:F1}%  " +
                              $"{Verdict(minMargin, SpreadMarginMin, 1f, ref fails)}");
            }
            return fails;
        }

        static string Verdict(float value, float min, float max, ref int fails)
        {
            bool ok = value >= min && value <= max;
            if (!ok) fails++;
            return ok ? "PASS" : $"FAIL (fuera de [{min:0.##},{max:0.##}])";
        }

        static GameObject FindStageRoot(StageDef def)
        {
            // Escenario real: la instancia del prefab. Escenario procedural (Zocalo): la
            // arena se construye en runtime con el nombre Stage_<id>.
            string realName = null;
            if (!string.IsNullOrEmpty(def.prefabResource))
            {
                var prefab = Resources.Load<GameObject>(def.prefabResource);
                if (prefab != null) realName = prefab.name;
            }
            string proceduralName = "Stage_" + def.id;
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
            {
                if (t.parent != null) continue;
                if (realName != null && (t.name == realName + "(Clone)" || t.name == realName)) return t.gameObject;
                if (t.name == proceduralName) return t.gameObject;
            }
            return null;
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
            foreach (var smr in UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>())
            {
                smr.forceMatrixRecalculationPerRender = true;
                smr.updateWhenOffscreen = true;
            }
        }

        // ---------------------------------------------------------------------------------
        // Medida VISUAL: se renderiza dos veces (sin y con luchadores) y se compara. La
        // silueta resultante no depende del color del traje ni del HUD, y da la altura real
        // en pixeles. La tercera pasada, con HUD, es la captura que se guarda.
        // ---------------------------------------------------------------------------------
        public struct Silhouette
        {
            public float Fill;
            public float FeetY;
            public float HeadY;
            public float MinX;
            public float MaxX;
        }

        static Silhouette CaptureAndMeasure(Camera cam, string stageId, Fighter[] fighters, string suffix)
        {
            var canvases = new List<Canvas>();
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>())
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 0.9f;
                canvases.Add(canvas);
            }

            // La silueta debe ser el CUERPO. Dos cosas la contaminan y hay que quitarlas:
            //   - la sombra proyectada (el muro esta a 2-3 m: la sombra trepa por el mural y
            //     dispara el borde superior medido);
            //   - el disco de sombra y el anillo, que son efectos hijos del luchador.
            var renderers = new List<Renderer>();
            var fx = new List<Renderer>();
            var shadowModes = new List<UnityEngine.Rendering.ShadowCastingMode>();
            foreach (var f in fighters)
            {
                foreach (var r in f.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name.StartsWith("GroundShadow") || r.name.StartsWith("GroundRing")) { fx.Add(r); continue; }
                    renderers.Add(r);
                    shadowModes.Add(r.shadowCastingMode);
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            foreach (var r in fx) r.enabled = false;

            SetCanvases(canvases, false);
            foreach (var r in renderers) r.enabled = false;
            var bg = RenderPixels(cam);

            foreach (var r in renderers) r.enabled = true;
            var fg = RenderPixels(cam);

            SetCanvases(canvases, true);
            SavePng(cam, $"{OutDir}/{stageId}{suffix}_scale.png");

            for (int i = 0; i < renderers.Count; i++) renderers[i].shadowCastingMode = shadowModes[i];
            foreach (var r in fx) r.enabled = true;

            return SilhouetteFill(bg, fg, stageId, suffix);
        }

        static void SetCanvases(List<Canvas> canvases, bool on)
        {
            foreach (var c in canvases) c.enabled = on;
        }

        static Silhouette SilhouetteFill(Color32[] bg, Color32[] fg, string stageId, string suffix)
        {
            int half = W / 2;
            // Se cuenta por fila: un punado de pixeles sueltos (dithering, AA) no puede
            // definir el borde de la silueta. Una fila cuenta si tiene >= 8 pixeles distintos.
            var leftCount = new int[H];
            var rightCount = new int[H];
            var mask = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                int row = y * W;
                for (int x = 0; x < W; x++)
                {
                    var p = fg[row + x];
                    var q = bg[row + x];
                    int d = Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b);
                    if (d < 14) { mask[row + x] = new Color32(0, 0, 0, 255); continue; }
                    mask[row + x] = new Color32(255, 255, 255, 255);
                    if (x < half) leftCount[y]++; else rightCount[y]++;
                }
            }

            const int MinRowPixels = 8;
            int leftMin = H, leftMax = -1, rightMin = H, rightMax = -1;
            var colCount = new int[W];
            for (int y = 0; y < H; y++)
            {
                if (leftCount[y] >= MinRowPixels) { if (y < leftMin) leftMin = y; if (y > leftMax) leftMax = y; }
                if (rightCount[y] >= MinRowPixels) { if (y < rightMin) rightMin = y; if (y > rightMax) rightMax = y; }
            }
            for (int y = 0; y < H; y++)
            {
                int row = y * W;
                for (int x = 0; x < W; x++)
                    if (mask[row + x].r > 0) colCount[x]++;
            }
            int minX = W, maxX = -1;
            for (int x = 0; x < W; x++)
            {
                if (colCount[x] < MinRowPixels) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
            }

            // La mascara se guarda: es la prueba visual de que la silueta medida es el cuerpo
            // y no una sombra o un reflejo.
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            tex.SetPixels32(mask);
            tex.Apply();
            File.WriteAllBytes($"{OutDir}/{stageId}{suffix}_mask.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            float l = leftMax >= 0 ? (leftMax - leftMin + 1f) / H : 0f;
            float r = rightMax >= 0 ? (rightMax - rightMin + 1f) / H : 0f;
            float fill = l <= 0f ? r : (r <= 0f ? l : (l + r) * 0.5f);

            // Cota inferior y superior de la silueta (0 = borde inferior de la imagen).
            float feet = 1f, head = 0f;
            if (leftMax >= 0) { feet = Mathf.Min(feet, leftMin / (float)H); head = Mathf.Max(head, leftMax / (float)H); }
            if (rightMax >= 0) { feet = Mathf.Min(feet, rightMin / (float)H); head = Mathf.Max(head, rightMax / (float)H); }

            return new Silhouette
            {
                Fill = fill,
                FeetY = feet,
                HeadY = head,
                MinX = maxX >= 0 ? minX / (float)W : 0f,
                MaxX = maxX >= 0 ? maxX / (float)W : 0f
            };
        }

        static Color32[] RenderPixels(Camera cam)
        {
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.antiAliasing = 1;
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = prev;
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            RenderTexture.ReleaseTemporary(rt);
            return px;
        }

        static void SavePng(Camera cam, string path)
        {
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
