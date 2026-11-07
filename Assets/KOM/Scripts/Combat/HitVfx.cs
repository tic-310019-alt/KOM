using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KOM
{
    /// <summary>Identidad visual de un golpe. Cada estilo tiene su propia silueta.</summary>
    public enum HitStyle
    {
        /// <summary>Jab: pinchazo seco, mínimo, casi sin pantalla.</summary>
        Jab,
        /// <summary>Recto fuerte: impacto frontal con anillo y polvo.</summary>
        Straight,
        /// <summary>Patada rápida: medialuna corta.</summary>
        KickLight,
        /// <summary>Patada fuerte: medialuna ancha, anillo grande y polvo.</summary>
        KickHeavy,
        /// <summary>Barrido bajo: medialuna rasa + polvo de suelo.</summary>
        LowSweep,
        /// <summary>Uppercut: columna vertical, asciende.</summary>
        Rising,
        /// <summary>Patada voladora: líneas de velocidad y estela larga.</summary>
        FlyingKick,
        /// <summary>Multi-golpe (combos): impactos pequeños y rápidos.</summary>
        Rapid,
        /// <summary>Bloqueo: el golpe se desliza por el escudo, no entra.</summary>
        Guard
    }

    /// <summary>
    /// Impacto de un golpe, por capas y por ESTILO.
    ///
    /// La diferencia entre un golpe que se "ve" y uno que se "siente" no es el tamaño:
    /// es la cantidad de lecturas simultáneas y que cada ataque tenga su propia
    /// silueta. Un único billboard radial para todo hace que un jab y una patada
    /// voladora se lean igual, y entonces el jugador deja de distinguir lo que hizo.
    ///
    /// Cada estilo enciende solo las capas que le pertenecen (ver <see cref="Profile"/>):
    ///
    ///   · JAB        punto + chispa corta. Sin anillo. Nada de pantalla.
    ///   · RECTO      núcleo, estela, anillo, disco inclinado, polvo.
    ///   · PATADA     MEDIALUNA de barrido (arco) + chispas que caen.
    ///   · BARRIDO    medialuna rasa + onda de polvo en el suelo.
    ///   · UPPERCUT   COLUMNA vertical + anillo horizontal + brasas que suben.
    ///   · VOLADORA   LÍNEAS DE VELOCIDAD detrás del impacto + estela larga.
    ///   · RÁFAGA     micro-impacto sin anillo (7 seguidos no pueden ser 7 explosiones).
    ///   · BLOQUEO    domo + chispas tangentes: el golpe resbala.
    ///
    /// Nada de assets: mallas, materiales y partículas se construyen en runtime y
    /// mueren con el efecto.
    /// </summary>
    public sealed class HitVfx : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // Perfil de estilo
        // ------------------------------------------------------------------
        struct Profile
        {
            public float coreSize, corePower, coreIntensity;
            public float haloSize, haloIntensity;
            public float streakLen, streakWidth, streakIntensity;
            public float arcSize, arcWidth, arcIntensity, arcSpan;
            public int ringCount;
            public float ringSize, ringIntensity;
            public bool horizontalRing;     // anillo plano (uppercut)
            public float planeSize;         // disco inclinado; 0 = no
            public bool column;
            public float columnHeight;
            public int speedLines;
            public int sparkCount;
            public float sparkAngle, sparkSpeedMin, sparkSpeedMax;
            public bool sparksUp, sparksDown, sparksRing;
            public float domeSize;
            public bool dust, groundDust;
            public float lightIntensity, lightRange;
            // Cuánto de este golpe llega a la pantalla (0 = nada). Es lo que evita
            // que siete golpes por segundo laven el monitor.
            public float screenFlash, screenLines, screenRing;
            public float cameraScale;
        }

        static Profile Base()
        {
            var p = new Profile
            {
                coreSize = 0.20f, corePower = 7.5f, coreIntensity = 3.4f,
                haloSize = 0.60f, haloIntensity = 1.0f,
                streakLen = 0f, streakWidth = 0f, streakIntensity = 0f,
                arcSize = 0f, arcWidth = 0.10f, arcIntensity = 0f, arcSpan = 1.18f,
                ringCount = 1, ringSize = 0.50f, ringIntensity = 1.25f,
                horizontalRing = false,
                planeSize = 0f,
                column = false, columnHeight = 0f,
                speedLines = 0,
                sparkCount = 26, sparkAngle = 34f, sparkSpeedMin = 4.5f, sparkSpeedMax = 12f,
                sparksUp = false, sparksDown = false, sparksRing = false,
                domeSize = 0f,
                dust = true, groundDust = false,
                lightIntensity = 5.2f, lightRange = 3.0f,
                screenFlash = 0.45f, screenLines = 0.70f, screenRing = 0.70f,
                cameraScale = 1f
            };
            return p;
        }

        static Profile ProfileFor(HitStyle style)
        {
            var p = Base();
            switch (style)
            {
                case HitStyle.Jab:
                    p.coreSize = 0.15f; p.corePower = 9f; p.coreIntensity = 3.4f;
                    p.haloSize = 0.34f; p.haloIntensity = 0.65f;
                    p.streakLen = 0.55f; p.streakWidth = 0.16f; p.streakIntensity = 1.4f;
                    p.ringCount = 0;
                    p.sparkCount = 10; p.sparkAngle = 28f; p.sparkSpeedMin = 6f; p.sparkSpeedMax = 9f;
                    p.dust = false;
                    p.lightIntensity = 3.2f; p.lightRange = 2.4f;
                    p.screenFlash = 0.10f; p.screenLines = 0.22f; p.screenRing = 0.20f;
                    p.cameraScale = 0.55f;
                    break;

                case HitStyle.Straight:
                    // Golpe de referencia: cruz de impacto (nucleo + estela corta),
                    // un anillo fino y polvo. Es el unico estilo con "estrella".
                    p.streakLen = 0.95f; p.streakWidth = 0.24f; p.streakIntensity = 1.6f;
                    p.planeSize = 0.42f;
                    break;

                case HitStyle.KickLight:
                    // La MEDIALUNA es la protagonista: sin anillo y sin estela, para
                    // que el ojo lea "barrido" y no otra explosion redonda.
                    p.coreSize = 0.13f; p.corePower = 9f; p.coreIntensity = 3.0f;
                    p.haloSize = 0.34f; p.haloIntensity = 0.55f;
                    p.streakLen = 0.0f;
                    p.arcSize = 0.86f; p.arcWidth = 0.105f; p.arcIntensity = 1.9f; p.arcSpan = 1.16f;
                    p.ringCount = 0;
                    p.sparkCount = 16; p.sparkAngle = 44f; p.sparksDown = true;
                    p.sparkSpeedMin = 3f; p.sparkSpeedMax = 8f;
                    p.dust = false;
                    p.lightIntensity = 3.6f; p.lightRange = 2.6f;
                    p.screenFlash = 0.14f; p.screenLines = 0.30f; p.screenRing = 0.26f;
                    p.cameraScale = 0.75f;
                    break;

                case HitStyle.KickHeavy:
                    p.coreSize = 0.15f; p.corePower = 8f; p.coreIntensity = 3.6f;
                    p.haloSize = 0.46f; p.haloIntensity = 0.8f;
                    p.streakLen = 0f;
                    p.arcSize = 1.18f; p.arcWidth = 0.115f; p.arcIntensity = 2.4f; p.arcSpan = 1.22f;
                    p.ringCount = 0;
                    p.planeSize = 0f;
                    p.sparkCount = 34; p.sparkAngle = 46f; p.sparksDown = true;
                    p.sparkSpeedMin = 4f; p.sparkSpeedMax = 12f;
                    p.groundDust = true;
                    p.lightIntensity = 6f; p.lightRange = 3.2f;
                    p.screenFlash = 0.26f; p.screenLines = 0.52f; p.screenRing = 0.40f;
                    p.cameraScale = 1.05f;
                    break;

                case HitStyle.LowSweep:
                    p.coreSize = 0.13f; p.corePower = 9f; p.coreIntensity = 3.0f;
                    p.haloSize = 0.34f; p.haloIntensity = 0.55f;
                    p.streakLen = 0f;
                    p.arcSize = 0.95f; p.arcWidth = 0.115f; p.arcIntensity = 2.0f; p.arcSpan = 1.30f;
                    p.ringCount = 0;
                    p.sparkCount = 18; p.sparkAngle = 58f; p.sparksDown = true;
                    p.sparkSpeedMin = 3f; p.sparkSpeedMax = 9f;
                    p.dust = true; p.groundDust = true;
                    p.lightIntensity = 4.2f; p.lightRange = 2.8f;
                    p.screenFlash = 0.14f; p.screenLines = 0.30f; p.screenRing = 0.26f;
                    p.cameraScale = 0.8f;
                    break;

                case HitStyle.Rising:
                    p.coreSize = 0.22f; p.corePower = 7f; p.coreIntensity = 4.3f;
                    p.haloSize = 0.50f; p.haloIntensity = 1.0f;
                    p.streakLen = 0f;
                    p.column = true; p.columnHeight = 2.6f;
                    p.horizontalRing = true;
                    p.ringSize = 0.58f; p.ringIntensity = 1.6f;
                    p.sparkCount = 28; p.sparkAngle = 30f; p.sparksUp = true;
                    p.sparkSpeedMin = 3.5f; p.sparkSpeedMax = 9f;
                    p.lightIntensity = 6.2f; p.lightRange = 3.4f;
                    p.screenFlash = 0.28f; p.screenLines = 0.55f; p.screenRing = 0.45f;
                    p.cameraScale = 1.15f;
                    break;

                case HitStyle.FlyingKick:
                    p.coreSize = 0.26f; p.corePower = 6.5f; p.coreIntensity = 4.4f;
                    p.haloSize = 0.66f; p.haloIntensity = 1.1f;
                    p.streakLen = 1.55f; p.streakWidth = 0.34f; p.streakIntensity = 2.1f;
                    p.ringSize = 0.70f; p.ringIntensity = 1.8f;
                    p.planeSize = 0.52f;
                    p.speedLines = 6;
                    p.sparkCount = 40; p.sparkAngle = 26f;
                    p.sparkSpeedMin = 6f; p.sparkSpeedMax = 16f;
                    p.lightIntensity = 7f; p.lightRange = 3.6f;
                    p.screenFlash = 0.34f; p.screenLines = 0.70f; p.screenRing = 0.65f;
                    p.cameraScale = 1.3f;
                    break;

                case HitStyle.Rapid:
                    p.coreSize = 0.15f; p.corePower = 9f; p.coreIntensity = 3.1f;
                    p.haloSize = 0.32f; p.haloIntensity = 0.6f;
                    p.streakLen = 0.50f; p.streakWidth = 0.15f; p.streakIntensity = 1.25f;
                    p.ringCount = 0;
                    p.sparkCount = 8; p.sparkAngle = 30f;
                    p.sparkSpeedMin = 5f; p.sparkSpeedMax = 9f;
                    p.dust = false;
                    p.lightIntensity = 2.6f; p.lightRange = 2.2f;
                    p.screenFlash = 0.06f; p.screenLines = 0.16f; p.screenRing = 0.14f;
                    p.cameraScale = 0.45f;
                    break;

                case HitStyle.Guard:
                    p.coreSize = 0.16f; p.corePower = 9f; p.coreIntensity = 2.4f;
                    p.haloSize = 0.40f; p.haloIntensity = 0.55f;
                    p.streakLen = 0f;
                    p.ringSize = 0.60f; p.ringIntensity = 1.7f;
                    p.sparkCount = 22; p.sparkAngle = 74f;
                    p.sparkSpeedMin = 3f; p.sparkSpeedMax = 7.5f;
                    p.dust = false;
                    p.lightIntensity = 3f; p.lightRange = 2.6f;
                    // El bloqueo NO destella la pantalla: el golpe no entró.
                    p.screenFlash = 0f; p.screenLines = 0.14f; p.screenRing = 0.30f;
                    p.cameraScale = 0.7f;
                    break;
            }
            return p;
        }

        /// <summary>
        /// Pesos de pantalla y cámara de un estilo. Los consume HitFeedback para que
        /// un jab no destelle la pantalla y una voladora sí.
        /// </summary>
        public static void ScreenWeights(HitStyle style, out float flash, out float lines,
            out float ring, out float camera)
        {
            var p = ProfileFor(style);
            flash = p.screenFlash;
            lines = p.screenLines;
            ring = p.screenRing;
            camera = p.cameraScale;
        }

        // ------------------------------------------------------------------
        // Capa animable
        // ------------------------------------------------------------------
        sealed class Layer
        {
            public Transform t;
            public Material mat;
            public Vector2 size0, size1;
            public float life, fadePower, intensity;
            public bool spin;
            public float spinSpeed;
        }

        readonly List<Layer> _layers = new List<Layer>();
        readonly List<Material> _materials = new List<Material>();

        Light _light;
        float _lightPeak;
        float _t;
        float _life = 0.55f;
        float _scale = 1f;

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        /// <summary>
        /// Lanza un impacto. <paramref name="dir"/> es la dirección del golpe (del
        /// atacante al defensor). <paramref name="intensity"/> 0.2..1.6.
        /// </summary>
        public static HitVfx Play(Vector3 point, Vector3 dir, Color color, float intensity, HitStyle style)
        {
            var go = new GameObject("HitVfx_" + style);
            go.transform.position = point;
            var fx = go.AddComponent<HitVfx>();
            fx.Init(dir, color, intensity, style);
            return fx;
        }

        void Init(Vector3 dir, Color color, float intensity, HitStyle style)
        {
            // El shader tiene que estar incluido en el build. Si faltara, lanzar una
            // excepcion aqui aborta el golpe a mitad (dano repetido, KO y ronda colgada).
            // El VFX se degrada a "sin efecto"; el combate sigue.
            if (Shader.Find("KOM/ImpactGlow") == null)
            {
                Debug.LogWarning("[KOM] Falta el shader KOM/ImpactGlow: impacto sin VFX.");
                return;
            }

            var p = ProfileFor(style);
            float w = Mathf.Clamp(intensity, 0.15f, 1.7f);
            // Escala global suave: el estilo ya codifica el peso; esto solo separa un
            // jab flojo de un jab a plena potencia.
            _scale = Mathf.Lerp(0.88f, 1.14f, Mathf.Clamp01(w / 1.4f));

            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.right;
            dir.Normalize();

            bool blocked = style == HitStyle.Guard;
            Color baseColor = blocked
                ? Color.Lerp(color, new Color(0.55f, 0.86f, 1f), 0.75f)
                : color;
            Color hot = Color.Lerp(baseColor, Color.white, 0.82f);

            // --- núcleo y halo ------------------------------------------------
            AddGlow("HitCore", baseColor, hot, p.coreSize, p.corePower, p.coreIntensity * Mathf.Lerp(0.85f, 1.15f, w / 1.4f),
                new Vector2(1f, 1f), 1f, 0.11f, 2.4f);
            AddGlow("HitHalo", baseColor, hot, p.haloSize, 3.2f, p.haloIntensity,
                new Vector2(1f, 1f), 0.35f, 0.18f, 2.0f);

            // --- estela direccional -------------------------------------------
            if (p.streakLen > 0.01f)
            {
                var streak = AddGlow("HitStreak", baseColor, hot, 1f, 2.0f, p.streakIntensity,
                    new Vector2(0.82f, 2.05f), 0.8f, 0.15f, 2.2f);
                var rt = streak.t;
                rt.localScale = new Vector3(p.streakLen * _scale, p.streakWidth * _scale, 1f);
                streak.size0 = new Vector2(p.streakLen * _scale * 0.62f, p.streakWidth * _scale * 1.2f);
                streak.size1 = new Vector2(p.streakLen * _scale * 1.35f, p.streakWidth * _scale * 0.4f);
                var align = streak.t.gameObject.AddComponent<KomStreakBillboard>();
                align.Direction = dir;
            }

            // --- medialuna de patada -------------------------------------------
            if (p.arcSize > 0.01f)
            {
                var arc = AddShape("HitArc", baseColor, hot, p.arcSize, 2, p.arcWidth, p.arcIntensity,
                    new Vector2(1f, 1f), 0f, 0.20f, 1.6f, p.arcSpan);
                arc.size0 = new Vector2(p.arcSize * _scale * 0.55f, p.arcSize * _scale * 0.55f);
                arc.size1 = new Vector2(p.arcSize * _scale * 1.25f, p.arcSize * _scale * 1.25f);
                var align = arc.t.gameObject.AddComponent<KomStreakBillboard>();
                align.Direction = dir;
            }

            // --- anillos --------------------------------------------------------
            for (int i = 0; i < p.ringCount; i++)
            {
                var ring = AddShape("HitRing" + i, baseColor, hot, p.ringSize, 1, 0.075f,
                    p.ringIntensity * (i == 0 ? 1f : 0.6f), new Vector2(1f, 1f), 0f, 0.26f + i * 0.05f, 2.0f);
                ring.size0 = Vector2.one * p.ringSize * _scale * 0.32f;
                ring.size1 = Vector2.one * p.ringSize * _scale * 2.45f;
                if (p.horizontalRing)
                {
                    // Uppercut: el anillo se abre en el PLANO horizontal, como si el
                    // golpe empujara el aire hacia arriba.
                    ring.t.localRotation = Quaternion.Euler(90f, 0f, 0f);
                }
                else
                {
                    ring.t.gameObject.AddComponent<BillboardFx>();
                }
            }

            // --- domo del bloqueo: anillo de PERFIL en el plano del golpe ---------
            if (p.domeSize > 0.01f)
            {
                var dome = AddShape("HitDome", baseColor, hot, p.domeSize, 1, 0.11f,
                    p.ringIntensity * 0.85f, new Vector2(1f, 1f), 0f, 0.24f, 2.0f);
                dome.size0 = Vector2.one * p.domeSize * _scale * 0.35f;
                dome.size1 = Vector2.one * p.domeSize * _scale * 2.2f;
                // Perpendicular al golpe: de cara al atacante, no a la camara.
                dome.t.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }

            // --- disco inclinado (volumen 3D) -----------------------------------
            if (p.planeSize > 0.01f)
            {
                var plane = AddShape("HitRingPlane", baseColor, hot, p.planeSize, 1, 0.13f,
                    p.ringIntensity * 0.55f, new Vector2(1f, 1f), 0f, 0.22f, 1.8f);
                plane.size0 = Vector2.one * p.planeSize * _scale * 0.3f;
                plane.size1 = Vector2.one * p.planeSize * _scale * 2.1f;
                var cam = Camera.main;
                float yaw = dir.x >= 0f ? 58f : -58f;
                plane.t.rotation = (cam != null ? cam.transform.rotation : Quaternion.identity)
                    * Quaternion.Euler(0f, yaw, 0f);
            }

            // --- columna vertical (uppercut) ------------------------------------
            if (p.column)
            {
                var col = AddGlow("HitColumn", baseColor, hot, 1f, 2.4f, 2.3f,
                    new Vector2(0.32f, 3.0f), 0.55f, 0.24f, 1.8f);
                col.size0 = new Vector2(0.16f * _scale, p.columnHeight * 0.35f);
                col.size1 = new Vector2(0.42f * _scale, p.columnHeight * _scale);
                // La columna nace en el punto de impacto y sube: se desplaza con la escala.
                col.t.localPosition = new Vector3(0f, p.columnHeight * 0.30f, 0f);
            }

            // --- líneas de velocidad (patada voladora) --------------------------
            for (int i = 0; i < p.speedLines; i++)
            {
                float side = (i % 2 == 0 ? 1f : -1f);
                float spread = (i + 0.5f) / p.speedLines;
                float len = Mathf.Lerp(0.9f, 1.7f, spread) * _scale;
                var line = AddGlow("HitSpeed" + i, baseColor, hot, 1f, 2.6f, 1.5f,
                    new Vector2(0.9f, 2.6f), 0.6f, 0.16f + i * 0.012f, 2.4f);
                line.size0 = new Vector2(len * 0.5f, 0.05f * _scale);
                line.size1 = new Vector2(len * 1.5f, 0.02f * _scale);
                // Detrás del impacto, abriéndose en abanico.
                float back = Mathf.Lerp(0.15f, 0.75f, spread);
                var offset = new Vector3(-dir.x * back, side * (0.06f + spread * 0.16f), 0f);
                line.t.localPosition = offset * _scale;
                var align = line.t.gameObject.AddComponent<KomStreakBillboard>();
                align.Direction = dir;
            }

            // --- partículas -------------------------------------------------------
            SpawnParticles(dir, baseColor, p, blocked);
            if (p.groundDust) SpawnGroundDust(baseColor, blocked);

            // --- luz ---------------------------------------------------------------
            _lightPeak = p.lightIntensity * Mathf.Lerp(0.8f, 1.2f, w / 1.4f);
            _light = KomSpecialFx.CreateLight(transform, hot, p.lightRange, _lightPeak);
        }

        // ------------------------------------------------------------------
        // Builders
        // ------------------------------------------------------------------

        Layer AddGlow(string name, Color color, Color hot, float size, float power, float intensity,
            Vector2 ellipse, float coreMix, float life, float fadePower)
        {
            return AddShape(name, color, hot, size, 0, 0f, intensity, ellipse, coreMix, life, fadePower);
        }

        Layer AddShape(string name, Color color, Color hot, float size, int shape, float width,
            float intensity, Vector2 ellipse, float coreMix, float life, float fadePower,
            float arcSpan = 1.18f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            KomFx.SafeDestroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            float s = size * _scale;
            go.transform.localScale = new Vector3(s, s, 1f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.allowOcclusionWhenDynamic = false;

            var shader = Shader.Find("KOM/ImpactGlow");
            if (shader == null) return null;
            var mat = new Material(shader);
            mat.SetColor("_Color", color);
            mat.SetColor("_HotColor", hot);
            mat.SetFloat("_Intensity", intensity);
            mat.SetFloat("_Power", shape == 0 ? 3f : 1.9f);
            mat.SetVector("_Ellipse", new Vector4(ellipse.x, ellipse.y, 0f, 0f));
            mat.SetFloat("_Core", coreMix);
            mat.SetFloat("_Shape", shape);
            mat.SetFloat("_Radius", 0.86f);
            mat.SetFloat("_Width", Mathf.Max(0.02f, width));
            mat.SetFloat("_ArcSpan", arcSpan);
            mat.renderQueue = 3110;
            mr.sharedMaterial = mat;

            var layer = new Layer
            {
                t = go.transform,
                mat = mat,
                size0 = new Vector2(s, s),
                size1 = new Vector2(s, s),
                life = life,
                fadePower = fadePower,
                intensity = intensity
            };
            _layers.Add(layer);
            _materials.Add(mat);
            return layer;
        }

        void SpawnParticles(Vector3 dir, Color color, in Profile p, bool blocked)
        {
            if (p.sparkCount <= 0) return;

            var go = new GameObject("HitSparks");
            go.transform.SetParent(transform, false);
            // Dirección del cono: la del golpe; arriba en el uppercut, abajo en las
            // patadas y perpendicular en el bloqueo (las chispas resbalan).
            Vector3 axis = dir;
            if (p.sparksUp) axis = Vector3.up;
            else if (p.sparksDown) axis = (dir + Vector3.down * 0.85f).normalized;
            else if (blocked) axis = Vector3.up;
            go.transform.rotation = Quaternion.LookRotation(axis, Vector3.up);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var mat = KomSpecialFx.CreateParticleMaterial(KomSpecialFx.StreakTexture);
            mat.color = Color.white;
            _materials.Add(mat);

            var main = ps.main;
            main.duration = 0.16f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.13f, 0.34f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(p.sparkSpeedMin, p.sparkSpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.10f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white, Color.Lerp(color, Color.white, 0.5f));
            main.gravityModifier = p.sparksUp ? -0.05f : 0.42f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)p.sparkCount) });

            var shape = ps.shape;
            shape.enabled = true;
            if (p.sparksRing)
            {
                // Plano del escudo: las chispas resbalan por la superficie en vez de
                // atravesarla. El aro de emision da la sensacion de "burbuja".
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.10f;
                shape.radiusThickness = 0.9f;
                shape.rotation = new Vector3(90f, 0f, 0f);
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = p.sparkAngle;
                shape.radius = 0.05f;
                shape.radiusThickness = 0.4f;
            }

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.55f, 0.55f), new Keyframe(1f, 0f)));

            var colorOver = ps.colorOverLifetime;
            colorOver.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.35f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(0f, 1f) });
            colorOver.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = blocked ? 1.4f : 2.3f;
            renderer.velocityScale = 0.09f;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 4;
            ps.Play();

            // Polvo: solo golpes con peso. Gris sucio, nunca del color del ataque.
            if (p.dust) SpawnDust(color, Mathf.RoundToInt(p.sparkCount * 0.5f));
        }

        void SpawnDust(Color color, int count)
        {
            var go = new GameObject("HitDust");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var mat = KomSpecialFx.CreateParticleMaterial(KomFx.GlowTexture);
            mat.color = Color.white;
            _materials.Add(mat);

            var main = ps.main;
            main.duration = 0.22f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.42f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.46f);
            Color dust = Color.Lerp(color, new Color(0.62f, 0.62f, 0.62f), 0.8f);
            main.startColor = new ParticleSystem.MinMaxGradient(dust, Color.Lerp(dust, Color.white, 0.25f));
            main.gravityModifier = 0.12f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.35f)));

            var colorOver = ps.colorOverLifetime;
            colorOver.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(dust, 0f), new GradientColorKey(dust, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.16f), new GradientAlphaKey(0f, 1f) });
            colorOver.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 1;
            ps.Play();
        }

        // Onda de polvo en el SUELO: es lo que ancla la patada baja y la voladora al
        // piso. Sin esto el impacto flota a la altura de la cadera.
        void SpawnGroundDust(Color color, bool blocked)
        {
            var go = new GameObject("HitGroundDust");
            go.transform.position = new Vector3(transform.position.x, 0.05f, transform.position.z);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var mat = KomSpecialFx.CreateParticleMaterial(KomFx.GlowTexture);
            mat.color = Color.white;
            // NO se registra en _materials: este emisor vive fuera del efecto y se
            // lleva su material consigo. Compartirlo lo dejaba magenta al morir el
            // impacto (defecto medido en captura).

            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 4.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.38f);
            Color dust = Color.Lerp(color, new Color(0.66f, 0.66f, 0.66f), 0.85f);
            main.startColor = new ParticleSystem.MinMaxGradient(dust, Color.Lerp(dust, Color.white, 0.2f));
            main.gravityModifier = 0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 90;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(blocked ? 10 : 22)) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.18f;
            shape.radiusThickness = 0f;
            // Plano horizontal: la onda corre por el suelo, no sale en bola.
            shape.rotation = new Vector3(90f, 0f, 0f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.35f, 1f), new Keyframe(1f, 1.5f)));

            var colorOver = ps.colorOverLifetime;
            colorOver.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(dust, 0f), new GradientColorKey(dust, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.45f, 0.12f), new GradientAlphaKey(0f, 1f) });
            colorOver.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 1;
            ps.Play();
            Destroy(go, 1.4f);
            Destroy(mat, 1.5f);
        }

        // ------------------------------------------------------------------
        // Animación
        // ------------------------------------------------------------------

        void Update()
        {
            _t += Time.deltaTime;

            for (int i = 0; i < _layers.Count; i++)
            {
                var l = _layers[i];
                if (l.t == null || l.mat == null) continue;
                float k = Mathf.Clamp01(_t / l.life);
                float ease = 1f - Mathf.Pow(1f - k, 2.2f);
                var size = Vector2.LerpUnclamped(l.size0, l.size1, ease);
                l.t.localScale = new Vector3(size.x, size.y, 1f);
                l.mat.SetFloat(IntensityId, l.intensity * Mathf.Pow(1f - k, l.fadePower));
            }

            if (_light != null)
            {
                _light.intensity = _lightPeak * Mathf.Pow(1f - Mathf.Clamp01(_t / 0.11f), 2f);
            }

            if (_t >= _life) Destroy(gameObject);
        }

        void OnDestroy()
        {
            for (int i = 0; i < _materials.Count; i++)
                if (_materials[i] != null) Destroy(_materials[i]);
        }
    }

    /// <summary>
    /// Billboard con balanceo: mira a cámara y gira sobre su eje de vista para que su
    /// X local coincida con la dirección proyectada del golpe. Sin esto la estela es
    /// una raya horizontal que no dice nada de por dónde vino el puño.
    /// </summary>
    public sealed class KomStreakBillboard : MonoBehaviour
    {
        public Vector3 Direction = Vector3.right;

        Camera _camera;
        Vector3 _worldDir;

        void Start()
        {
            _worldDir = Direction.sqrMagnitude > 0.0001f ? Direction.normalized : Vector3.right;
        }

        void LateUpdate()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            var camT = _camera.transform;
            var fwd = transform.position - camT.position;
            if (fwd.sqrMagnitude < 0.0001f) return;

            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            var up = Vector3.Cross(fwd, right).normalized;
            float angle = Mathf.Atan2(Vector3.Dot(_worldDir, up), Vector3.Dot(_worldDir, right)) * Mathf.Rad2Deg;
            transform.rotation = camT.rotation * Quaternion.Euler(0f, 0f, angle);
        }
    }
}
