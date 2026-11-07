using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KOM
{
    public struct StageLayout
    {
        public float ArenaHalfWidth;
    }

    public static class StageBuilder
    {
        static readonly Dictionary<string, Material> MatCache = new Dictionary<string, Material>();

        /// <summary>
        /// Material URP Lit cacheado. <paramref name="reflections"/> apaga el reflejo de
        /// entorno: sin skybox, URP cae en el cubemap por defecto (un cielo azul) y un
        /// suelo con brillo se tine de cian. En un juego en blanco y negro eso es un error
        /// de arte, no un detalle.
        /// </summary>
        public static Material GetMaterial(Color color, float smoothness = 0.35f, float metallic = 0f, bool reflections = true)
        {
            string key = "lit_" + ColorUtility.ToHtmlStringRGBA(color) + "_" + smoothness.ToString("0.00") + "_" + metallic.ToString("0.00")
                         + (reflections ? "" : "_nr");
            if (MatCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (!reflections) mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            MatCache[key] = mat;
            return mat;
        }

        public static Material GetEmissive(Color color, float intensity = 2f)
        {
            string key = "em_" + ColorUtility.ToHtmlStringRGBA(color) + "_" + intensity.ToString("0.0");
            if (MatCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var mat = GetMaterial(color * 0.25f, 0.3f, 0f);
            mat = new Material(mat);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * intensity);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            MatCache[key] = mat;
            return mat;
        }

        static GameObject Primitive(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 localScale, Material material, Quaternion? rotation = null, bool shadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            KomFx.SafeDestroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (rotation.HasValue) go.transform.localRotation = rotation.Value;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = shadows;
            return go;
        }

        static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, Quaternion? rot = null, bool shadows = true)
            => Primitive(PrimitiveType.Cube, parent, name, pos, size, mat, rot, shadows);

        static GameObject Cylinder(Transform parent, string name, Vector3 pos, float radius, float height, Material mat, bool shadows = true)
            => Primitive(PrimitiveType.Cylinder, parent, name, pos, new Vector3(radius * 2f, height * 0.5f, radius * 2f), mat, null, shadows);

        static GameObject Sphere(Transform parent, string name, Vector3 pos, float diameter, Material mat, bool shadows = false)
            => Primitive(PrimitiveType.Sphere, parent, name, pos, Vector3.one * diameter, mat, null, shadows);

        static void RingSegments(Transform parent, string name, Vector3 center, float radius, int segments, float thickness, Material mat, float depth = 0.08f)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = center;
            float segLength = 2f * Mathf.PI * radius / segments * 1.3f;
            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                Vector3 pos = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                var seg = Box(root, "Seg", pos, new Vector3(segLength, thickness, depth), mat, Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f), false);
                seg.layer = 0;
            }
        }

        static void WorldScreen(Transform parent, string name, Vector3 position, Vector2 size, string title, string subtitle, Color accent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.localScale = new Vector3(-0.011f, 0.011f, 0.011f);

            var bg = UIFactory.AddPanel(rt, "BG", new Color(0.012f, 0.02f, 0.024f, 0.98f), 18);
            UIFactory.Stretch(bg.rectTransform);
            var border = UIFactory.AddPanel(rt, "Border", new Color(accent.r, accent.g, accent.b, 0.95f), 18);
            UIFactory.Stretch(border.rectTransform);
            border.transform.SetAsFirstSibling();
            var inner = UIFactory.AddImage(rt, "Inner", UIFactory.WhiteSprite(), new Color(0.02f, 0.035f, 0.04f, 1f));
            UIFactory.Stretch(inner.rectTransform, 5f, 5f, 5f, 5f);
            inner.raycastTarget = false;

            var top = UIFactory.AddText(rt, "Top", KomLoc.T("TORNEO DEL BIENESTAR", "WELLBEING TOURNAMENT"), 21, new Color(accent.r, accent.g, accent.b), TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(top.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(size.x - 40f, 30f));
            var big = UIFactory.AddOutlinedText(rt, "Title", title, 54, new Color(0.97f, 0.93f, 0.83f), Color.black);
            UIFactory.Place(big.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(size.x - 40f, 80f));
            var small = UIFactory.AddText(rt, "Sub", subtitle, 22, new Color(0.7f, 0.76f, 0.74f), TextAnchor.MiddleCenter);
            UIFactory.Place(small.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(size.x - 40f, 32f));

            var barA = UIFactory.AddImage(rt, "AccentA", UIFactory.WhiteSprite(), accent);
            UIFactory.Place(barA.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(4f, size.y - 30f));
            var barB = UIFactory.AddImage(rt, "AccentB", UIFactory.WhiteSprite(), accent);
            UIFactory.Place(barB.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(4f, size.y - 30f));
        }

        static Light Spot(Transform parent, string name, Vector3 pos, Vector3 target, Color color, float intensity, float range, float angle, bool shadows)
        {
            var go = new GameObject(name, typeof(Light));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.LookAt(target);
            var light = go.GetComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * 0.55f;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = 0.75f;
            return light;
        }

        static Light Point(Transform parent, string name, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name, typeof(Light));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var light = go.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        static void BaseLighting(Transform root, StageDef def, Color accent, float frontFill = 1.2f)
        {
            // lightScale tambien vale para la arena procedural: era un campo que solo leia la
            // ruta de escenarios reales, asi que el Zocalo se quedaba con la receta fija y
            // salia casi negro.
            float gain = def != null && def.lightScale > 0.01f ? def.lightScale : 1f;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            var sky = KomData.ParseColor(def.skyColor, new Color(0.03f, 0.05f, 0.06f));
            RenderSettings.ambientLight = (sky * 1.2f + new Color(0.16f, 0.15f, 0.13f)) * gain;

            var sun = new GameObject("KeyDirectional", typeof(Light));
            sun.transform.SetParent(root, false);
            sun.transform.rotation = Quaternion.Euler(42f, -32f, 0f);
            var light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.intensity = 0.62f * gain;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.7f;

            Spot(root, "KeySpot", new Vector3(0f, 8.5f, -4.5f), new Vector3(0f, 0.8f, 0.5f), new Color(1f, 0.91f, 0.78f), 5.5f * gain, 30f, 56f, true);
            Spot(root, "RimAccent", new Vector3(-6.5f, 5.5f, 7.5f), new Vector3(0f, 1.1f, 0f), accent, 4.2f * gain, 26f, 50f, false);
            Spot(root, "RimCool", new Vector3(6.5f, 5.5f, 7.5f), new Vector3(0f, 1.1f, 0f), new Color(0.35f, 0.75f, 0.85f), 3.4f * gain, 26f, 50f, false);
            Point(root, "FrontFill", new Vector3(0f, 2.2f, -4.5f), new Color(0.7f, 0.82f, 0.82f), frontFill * gain, 14f);
        }

        // Iluminacion de un ESCENARIO REAL. El mundo va en blanco y negro (shader
        // KOM/StageBW) y solo usa la luz principal + ambiente; los focos existen para
        // separar a los LUCHADORES del fondo gris, que es lo unico que conserva color.
        public static void BuildRealStageLighting(Transform root, StageDef def)
        {
            float gain = def != null && def.lightScale > 0.01f ? def.lightScale : 1f;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            // Ambiente neutro: es lo que rellena las sombras del shader B/N.
            RenderSettings.ambientLight = new Color(0.30f, 0.30f, 0.30f) * gain;

            // Tono del mundo y charco de luz. Son GLOBALES del shader KOM/StageBW: valen
            // para todas las piezas del escenario de una vez, sin reescribir materiales.
            float worldGain = def != null && def.worldGain > 0.01f ? def.worldGain : 1f;
            float worldContrast = def != null && def.worldContrast > 0.01f ? def.worldContrast : 1f;
            Shader.SetGlobalFloat("_KOMWorldTone", 1f);
            Shader.SetGlobalFloat("_KOMWorldGain", worldGain);
            Shader.SetGlobalFloat("_KOMWorldContrast", worldContrast);

            float pool = def != null ? Mathf.Clamp01(def.poolStrength) : 0f;
            Shader.SetGlobalFloat("_KOMPoolEnable", pool > 0.001f ? 1f : 0f);
            Shader.SetGlobalFloat("_KOMPoolInner", def != null ? Mathf.Max(0.5f, def.poolInner) : 3.5f);
            Shader.SetGlobalFloat("_KOMPoolOuter", def != null ? Mathf.Max(1f, def.poolOuter) : 14f);
            // El charco nunca apaga del todo: un suelo negro absoluto pierde la textura y
            // el escenario se lee como un agujero.
            Shader.SetGlobalFloat("_KOMPoolFloor", Mathf.Lerp(1f, 0.34f, pool));
            Shader.SetGlobalFloat("_KOMPoolHeight", 1.4f);

            // Bruma de profundidad: lo lejano sube hacia un gris medio para separar el
            // decorado del suelo de pelea.
            float haze = def != null ? Mathf.Clamp01(def.hazeStrength) : 0f;
            Shader.SetGlobalFloat("_KOMHazeEnable", haze);
            Shader.SetGlobalFloat("_KOMHazeStart", def != null ? Mathf.Max(1f, def.hazeStart) : 14f);
            Shader.SetGlobalFloat("_KOMHazeEnd", def != null ? Mathf.Max(2f, def.hazeEnd) : 45f);
            Shader.SetGlobalFloat("_KOMHazeValue", def != null ? Mathf.Clamp01(def.hazeValue) : 0.42f);

            var key = new GameObject("StageKey", typeof(Light));
            key.transform.SetParent(root, false);
            key.transform.rotation = Quaternion.Euler(38f, -34f, 0f);
            var kl = key.GetComponent<Light>();
            kl.type = LightType.Directional;
            kl.color = Color.white;
            kl.intensity = 1.15f * gain;
            kl.shadows = LightShadows.Soft;
            kl.shadowStrength = 0.85f;

            // Los focos no tocan el mundo (el shader B/N solo mira la principal) pero si
            // a los personajes, que usan URP Lit: los recortan del fondo.
            Spot(root, "RimLeft", new Vector3(-5.5f, 6.5f, 5.5f), new Vector3(0f, 1.1f, 0f), Color.white, 6.0f * gain, 22f, 46f, false);
            Spot(root, "RimRight", new Vector3(5.5f, 6.5f, 5.5f), new Vector3(0f, 1.1f, 0f), new Color(0.85f, 0.85f, 0.85f), 5.0f * gain, 22f, 46f, false);
            Point(root, "FrontFill", new Vector3(0f, 2.4f, -4.2f), new Color(0.9f, 0.9f, 0.9f), 2.2f * gain, 16f);
        }

        static void GlowQuad(Transform parent, Vector3 localPosition, Vector2 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            KomFx.SafeDestroy(go.GetComponent<Collider>());
            go.name = "BackGlow";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            mat.mainTexture = KomFx.GlowTexture;
            mat.color = color;
            mat.renderQueue = 3000;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// Diorama de la seleccion: una caja negra de teatro, no una arena.
        ///
        /// La pantalla ya tiene tres columnas de interfaz, asi que el fondo no puede pedir
        /// atencion: un charco de luz en el suelo, un muro lejano que se pierde en la bruma
        /// y una cortina de cuchillas de luz detras del luchador. Todo en gris; el unico
        /// color de la pantalla es el personaje.
        /// </summary>
        public static void BuildSelectDiorama(Color accent, bool duo = false)
        {
            accent = UIFactory.Mono(accent);
            KomFx.ResetCache();
            MatCache.Clear();

            // Una preview del editor puede dejar el diorama anterior en la escena: se borra
            // antes de construir para no apilar dos suelos ni dos muros.
            foreach (var stale in UnityEngine.Object.FindObjectsByType<Transform>())
            {
                if (stale != null && stale.name == "SelectDiorama" && stale.parent == null)
                    KomFx.SafeDestroy(stale.gameObject);
            }

            var root = new GameObject("SelectDiorama").transform;
            RenderSettings.skybox = null;

            // Sin reflejos de entorno: el diorama no tiene skybox y URP usaria el cubemap
            // por defecto, que es azul. Un suelo pulido sobre ese cubemap se veia cian y
            // rompia la regla de blanco y negro de toda la pantalla.
            var dark = GetMaterial(new Color(0.022f, 0.024f, 0.026f), 0.35f, 0f, false);
            var darker = GetMaterial(new Color(0.006f, 0.0065f, 0.007f), 0.2f, 0f, false);
            var accentMat = GetEmissive(accent * 0.85f, 1.9f);
            var bladeMat = GetEmissive(new Color(0.72f, 0.72f, 0.72f), 1.5f);
            var lampMat = GetEmissive(new Color(1f, 0.95f, 0.88f), 2.6f);

            // Suelo: un disco grande y un charco de luz. El borde del disco no se ve porque
            // la camara lo corta; lo que se ve es el degradado del charco.
            KomFx.CreateFloorDisc(root, 13f, 0.22f, new Color(0.032f, 0.034f, 0.036f), false);
            FloorGlow(root, new Vector3(0f, 0.012f, 0.6f), 7.5f, new Color(1f, 1f, 1f, 0.17f));

            // Un solo anillo: marca donde se planta el luchador. Un segundo anillo grande
            // cruzaba por delante de la tira de tarjetas y competia con la interfaz.
            var inner = KomFx.CreateGroundRing(root, 0.98f, accent, 0.30f);
            inner.transform.position = new Vector3(inner.transform.position.x, 0.02f, inner.transform.position.z);

            // Muro lejano: un plano ancho, sin costuras. Los paneles en arco dejaban
            // franjas verticales que se leian como una valla.
            Box(root, "BackWall", new Vector3(0f, 6.5f, 11f), new Vector3(44f, 15f, 0.4f), darker, null, false);

            // Halo: un degradado radial grande detras del luchador. Es lo que separa la
            // silueta del muro sin llenar el fondo de geometria.
            GlowQuad(root, new Vector3(0f, 2.5f, 10.2f), new Vector2(9.5f, 7.5f), new Color(1f, 1f, 1f, 0.20f));

            // Techo y estructura: una viga con tres focos. Sin esto el fondo no tiene
            // escala y el personaje parece recortado sobre un degradado.
            Box(root, "Ceiling", new Vector3(0f, 8.4f, 2.2f), new Vector3(30f, 0.3f, 16f), dark, null, false);
            Box(root, "Truss", new Vector3(0f, 7.15f, 1.4f), new Vector3(16f, 0.16f, 0.32f), dark, null, false);
            for (int i = -1; i <= 1; i++)
            {
                Sphere(root, "Lamp" + (i + 2), new Vector3(i * 3.4f, 7.02f, 1.4f), 0.18f, lampMat);
                GlowQuad(root, new Vector3(i * 3.4f, 7.02f, 1.55f), new Vector2(1.5f, 1.5f), new Color(1f, 0.95f, 0.88f, 0.22f));
            }

            // Laterales: dos muros que cierran el encuadre y evitan que la vista se escape.
            for (int side = -1; side <= 1; side += 2)
            {
                Box(root, "SideWall", new Vector3(side * 8.2f, 4.2f, 3.4f), new Vector3(0.4f, 8.4f, 12f), darker, null, false);
                Box(root, "SideTrim", new Vector3(side * 7.98f, 4.2f, 1.2f), new Vector3(0.05f, 7.4f, 0.05f), accentMat, null, false);
            }

            // La seleccion comparte pantalla con la tira de tarjetas: el diorama se ilumina
            // por debajo de su receta normal para que el suelo no compita con la interfaz.
            // MEDIDO 2026-11-02: a 0.58 el luchador se leia casi en silueta; 1.05 lo saca
            // del fondo sin lavar los blancos de la ficha. El VS necesita que el rival de
            // negro tambien se lea, y comparte este diorama.
            BaseLighting(root, new StageDef { skyColor = "#0A0C0E", lightScale = 1.05f }, accent, duo ? 2.7f : 1.2f);

            // En el plano de dos, el relleno frontal sube: el luchador de la derecha
            // tiene la pose espejada, la luz clave le cae por detras y su ropa suele ser
            // negra. Un unico relleno central mas fuerte lo saca del fondo sin sumar
            // focos (el limite de luces adicionales de URP ya esta cubierto).

            // La seleccion es una caja negra: el ambiente de escena (pensado para la
            // arena) dejaba el muro gris y la pantalla sin contraste. El suelo y el muro
            // necesitan algo mas de relleno para que el personaje no flote en negro.
            RenderSettings.ambientLight = new Color(0.15f, 0.15f, 0.156f);

            // El rim de la arena es azul. En el combate no se ve porque el escenario usa
            // KOM/StageBW, pero el diorama es URP Lit y se teñiria de cian: se neutraliza.
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.name == "RimCool")
                {
                    light.color = new Color(0.86f, 0.87f, 0.88f);
                    light.intensity *= 0.6f;
                }
                else if (light.name == "FrontFill")
                {
                    // En el VS los dos luchadores estan a los lados: sin relleno, el que
                    // viste de negro se lee como una silueta.
                    light.color = new Color(0.88f, 0.89f, 0.90f);
                    light.intensity *= 2.4f;
                }
            }

            // NIEBLA PROHIBIDA en este proyecto (preferencia explicita del usuario). La
            // profundidad la dan el halo, el charco de luz y el degradado del suelo.
            RenderSettings.fog = false;
        }

        /// <summary>Charco de luz: el sprite de glow tumbado en el suelo.</summary>
        static void FloorGlow(Transform parent, Vector3 position, float size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            KomFx.SafeDestroy(go.GetComponent<Collider>());
            go.name = "FloorGlow";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            go.transform.localScale = new Vector3(size, size, 1f);
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.mainTexture = KomFx.GlowTexture;
            mat.color = color;
            mat.renderQueue = 3000;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public static StageLayout Build(StageDef def)
        {
            KomFx.ResetCache();
            MatCache.Clear();
            var root = new GameObject("Stage_" + def.id).transform;
            float halfWidth = def.arenaHalfWidth;
            // Arena procedural: tambien en blanco y negro.
            var accent = UIFactory.Mono(KomData.Accent(def));
            var floor = UIFactory.Mono(KomData.ParseColor(def.floorColor, new Color(0.1f, 0.09f, 0.08f)));
            var prop = UIFactory.Mono(KomData.ParseColor(def.propColor, new Color(0.08f, 0.1f, 0.11f)));
            var sky = UIFactory.Mono(KomData.ParseColor(def.skyColor, new Color(0.03f, 0.04f, 0.05f)));
            var darkMat = GetMaterial(prop, 0.35f, 0.15f);
            var floorMat = GetMaterial(floor, 0.55f, 0.18f);
            var accentMat = GetEmissive(accent, 2.1f);
            var coolMat = GetEmissive(UIFactory.Mono(new Color(0.35f, 0.8f, 0.85f)), 1.4f);

            BaseLighting(root, def, accent);
            Box(root, "Ground", new Vector3(0f, -0.25f, 8f), new Vector3(60f, 0.4f, 36f), GetMaterial(sky * 0.7f, 0.2f, 0f), null, false);
            Box(root, "BackWall", new Vector3(0f, 6f, 13.5f), new Vector3(48f, 16f, 0.6f), GetMaterial(sky * 0.8f, 0.2f, 0f), null, false);

            KomFx.CreateFloorDisc(root, halfWidth + 0.9f, 0.2f, floor);
            var ring = KomFx.CreateGroundRing(root, halfWidth + 0.55f, accent, 0.78f);
            ring.transform.position = new Vector3(ring.transform.position.x, 0.03f, ring.transform.position.z);
            KomFx.CreateGroundRing(root, 3.4f, new Color(accent.r, accent.g, accent.b, 0.35f), 0.35f);

            BuildTruss(root, darkMat, accentMat, halfWidth);
            ApplyTheme(def, root, halfWidth, accent, accentMat, coolMat, darkMat, prop);

            return new StageLayout { ArenaHalfWidth = halfWidth };
        }

        static void BuildTruss(Transform root, Material dark, Material accent, float halfWidth)
        {
            Box(root, "Truss", new Vector3(0f, 7.6f, 0.5f), new Vector3((halfWidth + 2f) * 2f, 0.3f, 0.6f), dark, null, false);
            for (int side = -1; side <= 1; side += 2)
            {
                Box(root, "TrussLeg", new Vector3(side * (halfWidth + 1f), 3.7f, 0.5f), new Vector3(0.32f, 7.4f, 0.32f), dark, null, false);
                Box(root, "TrussAccent", new Vector3(side * (halfWidth + 0.78f), 3.7f, 0.5f), new Vector3(0.05f, 6.4f, 0.05f), accent, null, false);
            }
            for (int i = -3; i <= 3; i++)
            {
                var lampMat = GetEmissive(new Color(1f, 0.83f, 0.55f), 2.2f);
                Sphere(root, "Lamp", new Vector3(i * (halfWidth / 3f), 7.22f, 0.5f), 0.22f, lampMat);
            }
        }

        static void ApplyTheme(StageDef def, Transform root, float halfWidth, Color accent, Material accentMat, Material coolMat, Material darkMat, Color prop)
        {
            switch (def.theme)
            {
                case "palace":
                    BuildPalace(root, halfWidth, accent, accentMat, darkMat);
                    break;
                case "plaza":
                    BuildPlaza(root, halfWidth, accent, accentMat, darkMat);
                    break;
                case "conference_final":
                    BuildConference(root, halfWidth, accent, accentMat, coolMat, darkMat, true, def.displayName);
                    break;
                default:
                    BuildConference(root, halfWidth, accent, accentMat, coolMat, darkMat, false, def.displayName);
                    break;
            }
        }

        static void BuildConference(Transform root, float halfWidth, Color accent, Material accentMat, Material coolMat, Material darkMat, bool final, string venue)
        {
            var curtain = GetMaterial(final ? new Color(0.09f, 0.03f, 0.035f) : new Color(0.02f, 0.08f, 0.09f), 0.2f, 0f);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    float x = side * (halfWidth + 1.6f + i * 1.4f);
                    Box(root, "Curtain", new Vector3(x, 4.2f, 9.5f - i * 0.5f), new Vector3(1.25f, 8.4f, 0.3f), curtain, null, false);
                    Box(root, "CurtainGlow", new Vector3(x - side * 0.2f, 4.2f, 9.32f - i * 0.5f), new Vector3(0.06f, 7.4f, 0.06f), accentMat, null, false);
                }
            }

            // La pantalla de la final anunciaba "La Madrugadora", que es la sede de la
            // ronda 1: el cartel del Zocalo contradecia al mapa. La sede sale del catalogo.
            WorldScreen(root, "ScreenL", new Vector3(-4.0f, 3.7f, 12.7f), new Vector2(520f, 300f),
                final ? KomLoc.T("FINAL", "FINAL") : KomLoc.T("FASE DE GRUPOS", "GROUP STAGE"),
                final ? venue : KomLoc.T("Torneo del Bienestar", "Wellbeing Tournament"), accent);
            WorldScreen(root, "ScreenR", new Vector3(4.0f, 3.7f, 12.7f), new Vector2(520f, 300f),
                final ? KomLoc.T("VETO", "VETO") : KomLoc.T("RUTA DE CAMPEONES", "CHAMPIONS' ROAD"),
                final ? KomLoc.T("Una sola voz", "A single voice") : venue, accent);

            for (int i = -2; i <= 2; i++)
            {
                float x = i * 2.6f;
                Box(root, "FloorBar", new Vector3(x, 0.04f, 6.8f), new Vector3(1.6f, 0.03f, 0.12f), accentMat, null, false);
            }
            Point(root, "ScreenGlowL", new Vector3(-6.4f, 4.4f, 10.5f), accent, 2.6f, 12f);
            Point(root, "ScreenGlowR", new Vector3(6.4f, 4.4f, 10.5f), new Color(0.4f, 0.8f, 0.85f), 2.2f, 12f);
        }

        static void BuildPalace(Transform root, float halfWidth, Color accent, Material accentMat, Material darkMat)
        {
            var marble = GetMaterial(new Color(0.32f, 0.29f, 0.24f), 0.7f, 0.08f);
            KomFx.CreateFloorDisc(root, 3.4f, 0.22f, new Color(0.42f, 0.37f, 0.29f));
            RingSegments(root, "PalaceHalo", new Vector3(0f, 5.1f, 12.6f), 3.1f, 64, 0.15f, accentMat);

            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    float x = side * (halfWidth + 0.8f + i * 2.4f);
                    float z = 7.5f - i * 0.6f;
                    Cylinder(root, "Column", new Vector3(x, 3.4f, z), 0.42f, 6.8f, marble);
                    Box(root, "Capital", new Vector3(x, 6.9f, z), new Vector3(1.3f, 0.3f, 1.3f), accentMat, null, false);
                    Box(root, "Base", new Vector3(x, 0.35f, z), new Vector3(1.2f, 0.7f, 1.2f), marble);
                }
                Box(root, "Arch", new Vector3(side * 8.5f, 6.2f, 10.5f), new Vector3(4.4f, 0.5f, 0.6f), marble, null, false);
            }

            Box(root, "ChandelierRod", new Vector3(0f, 8.9f, 4.2f), new Vector3(0.08f, 2.4f, 0.08f), darkMat, null, false);
            Sphere(root, "ChandelierCore", new Vector3(0f, 7.65f, 4.2f), 0.55f, accentMat);
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                Sphere(root, "Candle", new Vector3(Mathf.Cos(a) * 1.4f, 7.45f + Mathf.Sin(a * 2f) * 0.2f, 4.2f + Mathf.Sin(a) * 1.4f), 0.16f, GetEmissive(new Color(1f, 0.82f, 0.5f), 2.2f));
            }
            Point(root, "ChandelierLight", new Vector3(0f, 7.4f, 4.2f), new Color(1f, 0.8f, 0.52f), 3.5f, 16f);

            for (int i = -3; i <= 3; i++)
            {
                Box(root, "GoldRail", new Vector3(i * 2.4f, 0.04f, halfWidth - 0.3f), new Vector3(1.5f, 0.03f, 0.1f), accentMat, null, false);
            }
        }

        static void BuildPlaza(Transform root, float halfWidth, Color accent, Material accentMat, Material darkMat)
        {
            var stone = GetMaterial(new Color(0.17f, 0.17f, 0.18f), 0.35f, 0.05f);
            var building = GetMaterial(new Color(0.055f, 0.065f, 0.085f), 0.15f, 0f);
            var windowMat = GetEmissive(new Color(1f, 0.7f, 0.36f), 1.4f);

            for (int i = 0; i < 9; i++)
            {
                float x = (i - 4) * 2.2f;
                float h = 5f + ((i * 37) % 5) * 0.9f;
                Box(root, "CathedralBlock", new Vector3(x, h * 0.5f, 16f), new Vector3(2.0f, h, 0.8f), building, null, false);
                Box(root, "Window", new Vector3(x, h * 0.62f, 15.55f), new Vector3(0.22f, 0.5f, 0.05f), windowMat, null, false);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Box(root, "Tower", new Vector3(side * 11.5f, 5.5f, 15f), new Vector3(2.6f, 11f, 2.2f), building, null, false);
                Box(root, "TowerCap", new Vector3(side * 11.5f, 11.2f, 15f), new Vector3(3.2f, 1.2f, 2.8f), building, null, false);
            }

            Cylinder(root, "FlagPole", new Vector3(3.2f, 7.5f, 11.5f), 0.09f, 15f, GetMaterial(new Color(0.3f, 0.3f, 0.3f), 0.6f, 0.6f));
            var flagGreen = GetMaterial(new Color(0.05f, 0.4f, 0.22f), 0.2f, 0f);
            var flagWhite = GetMaterial(new Color(0.85f, 0.84f, 0.8f), 0.2f, 0f);
            var flagRed = GetMaterial(new Color(0.6f, 0.08f, 0.1f), 0.2f, 0f);
            float flagY = 13.1f;
            Box(root, "FlagG", new Vector3(3.2f + 1.1f, flagY, 11.5f), new Vector3(2.2f, 2.2f, 0.05f), flagGreen, null, false);
            Box(root, "FlagW", new Vector3(3.2f + 1.1f, flagY, 11.56f), new Vector3(0.75f, 2.2f, 0.05f), flagWhite, null, false);
            Box(root, "FlagR", new Vector3(3.2f + 3.25f, flagY, 11.5f), new Vector3(2.2f, 2.2f, 0.05f), flagRed, null, false);

            for (int row = 0; row < 2; row++)
            {
                float z = 9.5f + row * 0.6f;
                float y = 5.2f - row * 0.7f;
                for (int i = 0; i < 14; i++)
                {
                    float x = -halfWidth - 2f + i * ((halfWidth + 2f) * 2f / 13f);
                    Sphere(root, "StringLight", new Vector3(x, y + Mathf.Sin(i * 1.3f) * 0.15f, z), 0.1f, GetEmissive(new Color(1f, 0.78f, 0.46f), 2f));
                }
            }

            WorldScreen(root, "PlazaScreenL", new Vector3(-5.2f, 4.1f, 14.1f), new Vector2(480f, 280f),
                KomLoc.T("ZÓCALO", "ZÓCALO"), KomLoc.T("Plaza monumental", "Monumental plaza"), accent);
            WorldScreen(root, "PlazaScreenR", new Vector3(5.2f, 4.1f, 14.1f), new Vector2(480f, 280f),
                KomLoc.T("TORNEO", "TOURNAMENT"), KomLoc.T("Fase final", "Final stage"), accent);

            for (int i = -4; i <= 4; i++)
            {
                Box(root, "PlazaMark", new Vector3(i * 2.1f, 0.04f, halfWidth - 0.6f), new Vector3(1.4f, 0.03f, 0.1f), accentMat, null, false);
            }
        }
    }
}
