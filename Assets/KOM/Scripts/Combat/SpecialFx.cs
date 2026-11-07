using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KOM
{
    // VFX procedurales de los especiales. Todo nace y muere con el efecto: no hay
    // assets nuevos ni cambios de material del personaje. La cadena ancla su primer
    // punto EXACTAMENTE en la palma (DEF-hand.R -> DEF-f_index.01.R) cada frame.
    //
    // Calidad: los materiales son shaders propios (KOM/EnergyOrb, KOM/AuraShell,
    // KOM/ChainSilver) para que el efecto se lea con la luz del escenario, que es
    // oscura. Nada de billboards gigantes planos: el volumen se construye con
    // esferas anidadas, anillos 3D que giran y arcos electricos reales.
    public static class KomSpecialFx
    {
        static Mesh _linkMesh;
        static Mesh _ringMesh;
        static Mesh _sphereMesh;
        static Texture2D _ribbonTex;
        static Texture2D _streakTex;
        static Texture2D _noteTex;

        // ---------------- API publica ----------------

        public static SpecialChargeFx CreateCharge(Fighter owner, SpecialDef special)
        {
            var go = new GameObject("SpecialCharge");
            var fx = go.AddComponent<SpecialChargeFx>();
            fx.Init(owner, special);
            return fx;
        }

        public static SpecialProjectileFx SpawnProjectile(Fighter owner, SpecialDef special, Vector3 origin, Vector3 dir)
        {
            var go = new GameObject("SpecialProjectile");
            var fx = go.AddComponent<SpecialProjectileFx>();
            fx.Init(owner, special, origin, dir);
            return fx;
        }

        // Onda de golpe en el suelo (Natanael): aros planos que se abren desde el punto
        // de impacto y danan cuando el frente alcanza al rival.
        public static GroundSlamFx SpawnGroundSlam(Fighter owner, SpecialDef special, Vector3 center)
        {
            var go = new GameObject("SpecialGroundSlam");
            var fx = go.AddComponent<GroundSlamFx>();
            fx.Init(owner, special, center);
            return fx;
        }

        // Onda de sonido (Jose Jose): frente de choque ancho que viaja recto, con notas
        // y anillos que se abren. No es una bola: es un frente plano con volumen.
        public static SoundWaveFx SpawnSoundWave(Fighter owner, SpecialDef special, Vector3 origin, Vector3 dir)
        {
            var go = new GameObject("SpecialSoundWave");
            var fx = go.AddComponent<SoundWaveFx>();
            fx.Init(owner, special, origin, dir);
            return fx;
        }

        public static ChainWhipFx SpawnChain(Fighter owner, SpecialDef special, Transform hand, Transform finger,
            Vector3 anchor, Vector3 target)
        {
            var go = new GameObject("SpecialChain");
            var fx = go.AddComponent<ChainWhipFx>();
            fx.Init(owner, special, hand, finger, anchor, target);
            return fx;
        }

        // Acordeon que vuela y vuelve. Toma posesion del prop (el Fighter lo suelta).
        public static BoomerangFx SpawnBoomerang(Fighter owner, SpecialDef special, GameObject prop,
            Vector3 origin, Vector3 target)
        {
            var go = new GameObject("SpecialBoomerang");
            var fx = go.AddComponent<BoomerangFx>();
            fx.Init(owner, special, prop, origin, target);
            return fx;
        }

        // Carga del instrumento: sin orbe. El prop se enciende, giran anillos a su
        // alrededor y las notas convergen hacia el.
        public static InstrumentChargeFx CreateInstrumentCharge(Fighter owner, SpecialDef special, GameObject prop)
        {
            var go = new GameObject("InstrumentCharge");
            var fx = go.AddComponent<InstrumentChargeFx>();
            fx.Init(owner, special, prop);
            return fx;
        }

        // Estela y arcos de la guitarra. Vive en su propia raiz (las cintas se construyen
        // en coordenadas de mundo y no pueden colgar de un prop que se mueve).
        public static ClubFx CreateClubFx(Fighter owner, SpecialDef special, GameObject prop)
        {
            var go = new GameObject("SpecialClub");
            var fx = go.AddComponent<ClubFx>();
            fx.Init(owner, special, prop);
            return fx;
        }

        public static SuperAuraFx CreateSuperAura(Transform parent, Color color)
        {
            var go = new GameObject("SuperAura");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<SuperAuraFx>();
            fx.Init(color);
            return fx;
        }

        public static void Impact(Vector3 point, Color color, float scale = 1f)
        {
            Impact(point, color, scale, false);
        }

        // physical = golpe de arma: sin orbe de plasma (se leia como bola de fuego),
        // con fogonazo seco, anillos, onda de suelo y polvo.
        public static void Impact(Vector3 point, Color color, float scale, bool physical)
        {
            var go = new GameObject("SpecialImpact");
            go.transform.position = point;
            var fx = go.AddComponent<SpecialImpactFx>();
            fx.Init(color, scale, physical);
        }

        public static void MuzzleFlash(Vector3 point, Color color, float scale = 0.7f)
        {
            var go = new GameObject("SpecialMuzzle");
            go.transform.position = point;
            var fx = go.AddComponent<SpecialFlashFx>();
            fx.Init(color, scale, 0.22f);
        }

        public static void AuraRing(Vector3 point, Color color, float scale = 1.4f)
        {
            var go = new GameObject("AuraRing");
            go.transform.position = point;
            var fx = go.AddComponent<SpecialRingFx>();
            fx.Init(color, scale, 0.55f);
        }

        // Onda de suelo: anillo plano que se abre y se apaga. Es lo que ancla el golpe
        // al piso (sin esto el impacto "flota").
        public static void GroundShock(Vector3 point, Color color, float scale = 1f)
        {
            var go = new GameObject("GroundShock");
            go.transform.position = new Vector3(point.x, 0.045f, point.z);
            var fx = go.AddComponent<SpecialRingFx>();
            fx.Init(color, 1.25f * scale, 0.6f, true);
        }

        // Polvo del golpe: particulas suaves con gravedad que aterrizan el impacto.
        public static void DustBurst(Vector3 point, Color tint, int count, float scale)
        {
            var go = new GameObject("DustBurst");
            go.transform.position = point;
            var fx = go.AddComponent<DustBurstFx>();
            fx.Init(tint, Mathf.Clamp(count, 4, 40), Mathf.Max(0.3f, scale));
        }

        // Ráfaga de notas musicales (identidad sonora del acordeon y de la guitarra).
        public static void NoteBurst(Vector3 point, Color color, int count)
        {
            var go = new GameObject("NoteBurst");
            go.transform.position = point;
            var fx = go.AddComponent<NoteBurstFx>();
            fx.Init(color, Mathf.Clamp(count, 4, 40));
        }

        // ---------------- materiales ----------------

        public static Material CreateOrbMaterial(Color color, Color core, float intensity = 2.2f)
        {
            var shader = Shader.Find("KOM/EnergyOrb");
            if (shader == null) return CreateAdditiveMaterial(color, GlowTexture, intensity);
            var mat = new Material(shader);
            mat.SetColor("_Color", color);
            mat.SetColor("_CoreColor", core);
            mat.SetFloat("_Intensity", intensity);
            mat.SetFloat("_RimPower", 2.0f);
            mat.SetFloat("_NoiseScale", 7.5f);
            mat.SetFloat("_Speed", 1.7f);
            mat.SetFloat("_Swirl", 3f);
            mat.SetFloat("_Alpha", 1f);
            mat.SetFloat("_Bands", 9f);
            mat.renderQueue = 3120;
            return mat;
        }

        public static Material CreateAuraMaterial(Color color, Color hot, float intensity = 1.2f)
        {
            var shader = Shader.Find("KOM/AuraShell");
            if (shader == null) return null;
            var mat = new Material(shader);
            mat.SetColor("_Color", color);
            mat.SetColor("_HotColor", hot);
            mat.SetFloat("_Intensity", intensity);
            // _Expand = 0: el aura es un RIM sobre la silueta EXACTA. Cualquier valor > 0
            // dibuja una copia de la malla empujada a lo largo de las normales y el personaje
            // se ve hinchado y parpadeante ("la malla se deforma", "la cabeza se redimensiona").
            // El halo lo pone el bloom, no una cascara mas grande.
            mat.SetFloat("_Expand", 0f);
            mat.SetFloat("_NoiseScale", 6.0f);
            mat.SetFloat("_Speed", 2.0f);
            mat.SetFloat("_RimPower", 2.35f);
            mat.SetFloat("_BodyHeight", 1.8f);
            mat.renderQueue = 3130;
            return mat;
        }

        public static Material CreateChainSilver(Color glowColor)
        {
            var shader = Shader.Find("KOM/ChainSilver");
            if (shader == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit == null) lit = Shader.Find("Standard");
                var fallback = new Material(lit);
                if (fallback.HasProperty("_BaseColor")) fallback.SetColor("_BaseColor", new Color(0.7f, 0.74f, 0.8f));
                if (fallback.HasProperty("_Metallic")) fallback.SetFloat("_Metallic", 0.9f);
                if (fallback.HasProperty("_Smoothness")) fallback.SetFloat("_Smoothness", 0.9f);
                return fallback;
            }
            var mat = new Material(shader);
            mat.SetColor("_Color", new Color(0.47f, 0.52f, 0.60f));
            mat.SetColor("_ShadeColor", new Color(0.07f, 0.09f, 0.13f));
            mat.SetColor("_SpecColor2", new Color(1f, 1f, 1f));
            mat.SetFloat("_SpecIntensity", 2.3f);
            mat.SetFloat("_Shininess", 46f);
            mat.SetColor("_RimColor", new Color(0.55f, 0.72f, 1f));
            mat.SetFloat("_RimPower", 4f);
            mat.SetFloat("_RimIntensity", 0.15f);
            mat.SetColor("_GlowColor", glowColor);
            mat.SetFloat("_Glow", 0.05f);
            mat.SetVector("_LightDir", new Vector4(0.35f, 0.82f, 0.45f, 0f));
            return mat;
        }

        public static Material CreateAdditiveMaterial(Color color, Texture texture, float intensity = 1.6f)
        {
            var shader = Shader.Find("KOM/AdditiveGlow");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_MainTex")) mat.mainTexture = texture != null ? texture : Texture2D.whiteTexture;
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            else mat.color = color;
            if (mat.HasProperty("_Intensity")) mat.SetFloat("_Intensity", intensity);
            mat.renderQueue = 3100;
            return mat;
        }

        public static Material CreateParticleMaterial()
        {
            return CreateParticleMaterial(GlowTexture);
        }

        public static Material CreateParticleMaterial(Texture texture)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture != null ? texture : GlowTexture);
            else mat.mainTexture = texture != null ? texture : GlowTexture;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            else mat.color = Color.white;
            KomFx.ConfigureParticleMaterial(mat);
            return mat;
        }

        // ---------------- geometria ----------------

        public static Texture2D GlowTexture => KomFx.GlowTexture;
        public static Texture2D RingTexture => KomFx.RingTexture;

        // Perfil suave a lo ancho: es lo que hace que una cinta (trail) se lea como
        // estela de energia y no como una linea dura.
        public static Texture2D RibbonTexture
        {
            get
            {
                if (_ribbonTex != null) return _ribbonTex;
                const int w = 8;
                const int h = 64;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                for (int y = 0; y < h; y++)
                {
                    float v = (y + 0.5f) / h;
                    float edge = 1f - Mathf.Abs(v * 2f - 1f);
                    float a = Mathf.Pow(Mathf.Clamp01(edge), 1.35f);
                    for (int x = 0; x < w; x++)
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                tex.Apply();
                _ribbonTex = tex;
                return tex;
            }
        }

        // Nota musical procedural (corchea): cabeza + tallo + bandera. Es la firma
        // visual del acordeon (nortena) y de la guitarra (rock), sin assets externos.
        public static Texture2D NoteTexture
        {
            get
            {
                if (_noteTex != null) return _noteTex;
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                var flag = new[]
                {
                    new Vector2(0.495f, 0.865f), new Vector2(0.560f, 0.845f),
                    new Vector2(0.640f, 0.760f), new Vector2(0.700f, 0.640f),
                    new Vector2(0.735f, 0.545f)
                };
                for (int y = 0; y < size; y++)
                {
                    float v = (y + 0.5f) / size;
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + 0.5f) / size;
                        // Cabeza: elipse inclinada.
                        float dx = u - 0.315f;
                        float dy = v - 0.245f;
                        float c = Mathf.Cos(-0.42f);
                        float s = Mathf.Sin(-0.42f);
                        float ex = (dx * c - dy * s) / 0.185f;
                        float ey = (dx * s + dy * c) / 0.130f;
                        float d = Mathf.Sqrt(ex * ex + ey * ey) - 1f;
                        // Tallo.
                        float stemX = Mathf.Abs(u - 0.500f) - 0.024f;
                        float stemY = Mathf.Max(0.205f - v, v - 0.875f);
                        float stem = Mathf.Max(stemX, stemY);
                        d = Mathf.Min(d, stem);
                        // Bandera: distancia a la polilinea, con grosor.
                        for (int i = 0; i < flag.Length - 1; i++)
                        {
                            Vector2 a = flag[i];
                            Vector2 b = flag[i + 1];
                            Vector2 p = new Vector2(u, v);
                            Vector2 ab = b - a;
                            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                            float dist = Vector2.Distance(p, a + ab * t) - 0.030f;
                            d = Mathf.Min(d, dist);
                        }
                        float alpha = Mathf.Clamp01(0.5f - d * 34f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                tex.Apply();
                _noteTex = tex;
                return tex;
            }
        }

        // Chispa alargada (para particulas con Stretch): se lee como esquirla, no
        // como bolita.
        public static Texture2D StreakTexture
        {
            get
            {
                if (_streakTex != null) return _streakTex;
                const int w = 64;
                const int h = 16;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                for (int y = 0; y < h; y++)
                {
                    float v = (y + 0.5f) / h;
                    float edge = Mathf.Pow(1f - Mathf.Abs(v * 2f - 1f), 1.4f);
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x + 0.5f) / w;
                        float tail = Mathf.Pow(1f - Mathf.Abs(u * 2f - 1f), 1.2f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, edge * tail));
                    }
                }
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                tex.Apply();
                _streakTex = tex;
                return tex;
            }
        }

        // Eslabon ovalado construido en unidades de "paso de cadena" (pitch = 1).
        // El eje largo va en Z para que LookRotation(tangente) lo alinee con la cadena.
        public static Mesh LinkMesh
        {
            get
            {
                if (_linkMesh != null) return _linkMesh;
                _linkMesh = BuildOvalLink(0.60f, 0.375f, 0.115f, 22, 10);
                _linkMesh.name = "KOM_ChainLink";
                _linkMesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return _linkMesh;
            }
        }

        // Anillo fino de radio 1 (se escala). Para orbitas de energia 3D.
        public static Mesh RingMesh
        {
            get
            {
                if (_ringMesh != null) return _ringMesh;
                _ringMesh = BuildTorus(1f, 0.052f, 64, 10);
                _ringMesh.name = "KOM_EnergyRing";
                _ringMesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return _ringMesh;
            }
        }

        public static Mesh SphereMesh
        {
            get
            {
                if (_sphereMesh != null) return _sphereMesh;
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _sphereMesh = go.GetComponent<MeshFilter>().sharedMesh;
                KomFx.SafeDestroy(go);
                return _sphereMesh;
            }
        }

        static Mesh BuildOvalLink(float a, float b, float tube, int majorSegments, int minorSegments)
        {
            int count = majorSegments * minorSegments;
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            var triangles = new int[majorSegments * minorSegments * 6];
            for (int i = 0; i < majorSegments; i++)
            {
                float u = i / (float)majorSegments * Mathf.PI * 2f;
                var center = new Vector3(0f, b * Mathf.Sin(u), a * Mathf.Cos(u));
                // Normal de la elipse en el plano YZ.
                var radial = new Vector3(0f, Mathf.Sin(u) / Mathf.Max(0.0001f, b), Mathf.Cos(u) / Mathf.Max(0.0001f, a));
                radial.Normalize();
                for (int j = 0; j < minorSegments; j++)
                {
                    float v = j / (float)minorSegments * Mathf.PI * 2f;
                    var normal = radial * Mathf.Cos(v) + Vector3.right * Mathf.Sin(v);
                    int index = i * minorSegments + j;
                    vertices[index] = center + normal * tube;
                    normals[index] = normal;
                    uvs[index] = new Vector2(i / (float)majorSegments, j / (float)minorSegments);
                }
            }
            int t = 0;
            for (int i = 0; i < majorSegments; i++)
            {
                int i1 = (i + 1) % majorSegments;
                for (int j = 0; j < minorSegments; j++)
                {
                    int j1 = (j + 1) % minorSegments;
                    int a0 = i * minorSegments + j;
                    int b0 = i1 * minorSegments + j;
                    int c0 = i1 * minorSegments + j1;
                    int d0 = i * minorSegments + j1;
                    triangles[t++] = a0; triangles[t++] = b0; triangles[t++] = c0;
                    triangles[t++] = a0; triangles[t++] = c0; triangles[t++] = d0;
                }
            }
            var mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh BuildTorus(float major, float minor, int majorSegments, int minorSegments)
        {
            int count = majorSegments * minorSegments;
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            var triangles = new int[majorSegments * minorSegments * 6];
            for (int i = 0; i < majorSegments; i++)
            {
                float u = i / (float)majorSegments * Mathf.PI * 2f;
                for (int j = 0; j < minorSegments; j++)
                {
                    float v = j / (float)minorSegments * Mathf.PI * 2f;
                    var normal = new Vector3(Mathf.Cos(u) * Mathf.Cos(v), Mathf.Sin(u) * Mathf.Cos(v), Mathf.Sin(v));
                    int index = i * minorSegments + j;
                    vertices[index] = new Vector3(Mathf.Cos(u) * major, Mathf.Sin(u) * major, 0f) + normal * minor;
                    normals[index] = normal;
                    uvs[index] = new Vector2(i / (float)majorSegments, j / (float)minorSegments);
                }
            }
            int t = 0;
            for (int i = 0; i < majorSegments; i++)
            {
                int i1 = (i + 1) % majorSegments;
                for (int j = 0; j < minorSegments; j++)
                {
                    int j1 = (j + 1) % minorSegments;
                    int a = i * minorSegments + j;
                    int b = i1 * minorSegments + j;
                    int c = i1 * minorSegments + j1;
                    int d = i * minorSegments + j1;
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = d;
                }
            }
            var mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------- builders ----------------

        public static GameObject CreateGlowQuad(Transform parent, string name, Color color, float width, float height,
            Texture texture = null, float intensity = 1.6f, bool billboard = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            KomFx.SafeDestroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = new Vector3(width, height, 1f);
            var mat = CreateAdditiveMaterial(color, texture != null ? texture : GlowTexture, intensity);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (billboard) go.AddComponent<BillboardFx>();
            return go;
        }

        public static GameObject CreateCore(Transform parent, string name, Color color, float diameter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * diameter;
            go.AddComponent<MeshFilter>().sharedMesh = SphereMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = StageBuilder.GetEmissive(Color.Lerp(color, Color.white, 0.55f), 4.5f);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        // Orbe de plasma: cascara procedural animada. El color ya es HDR.
        public static GameObject CreateOrb(Transform parent, string name, Color color, Color core, float diameter,
            float intensity = 2.2f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * diameter;
            go.AddComponent<MeshFilter>().sharedMesh = SphereMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var mat = CreateOrbMaterial(color, core, intensity);
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 3;
            return go;
        }

        public static GameObject CreateRing3D(Transform parent, string name, Color color, float radius,
            float thickness, float intensity = 1.4f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = new Vector3(radius, radius, thickness);
            go.AddComponent<MeshFilter>().sharedMesh = RingMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateAdditiveMaterial(color, Texture2D.whiteTexture, intensity);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 2;
            return go;
        }

        public static GameObject CreateRingQuad(Transform parent, string name, Color color, float diameter, float intensity,
            bool soft = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            KomFx.SafeDestroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = new Vector3(diameter, diameter, 1f);
            var mat = CreateAdditiveMaterial(color, soft ? KomFx.SoftRingTexture : RingTexture, intensity);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        public static Light CreateLight(Transform parent, Color color, float range, float intensity)
        {
            var go = new GameObject("SpecialLight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            return light;
        }

        // Arco electrico procedural entre dos puntos (espacio local del padre).
        public static KomBoltFx CreateBolt(Transform parent, string name, Vector3 from, Vector3 to,
            Color color, float width, float jitter, int seed, float life = 0.06f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var bolt = go.AddComponent<KomBoltFx>();
            bolt.Init(from, to, color, width, jitter, seed, life);
            return bolt;
        }

        // Cinta de estela. Vive en la raiz (no es hija del proyectil: el mesh se
        // construye en coordenadas de mundo) y se destruye sola al terminar.
        public static KomRibbon CreateRibbon(Transform follow, string name, Color color, float width,
            float lifetime, float intensity)
        {
            var go = new GameObject(name);
            // El mesh se construye en coordenadas de MUNDO: el transform tiene que
            // quedarse en el origen o la cinta se dibuja desplazada el doble.
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var ribbon = go.AddComponent<KomRibbon>();
            var mat = CreateAdditiveMaterial(Color.white, RibbonTexture, intensity);
            ribbon.Init(follow, width, lifetime, color, mat);
            return ribbon;
        }

        // El modulo Velocity exige que TODAS sus curvas usen el mismo modo. Se fijan
        // las seis a constante y se reinicia el sistema para validarlo (constant vs
        // random lanzaba "Particle Velocity curves must all be in the same mode").
        public static void SetVelocity(ParticleSystem ps, Vector3 linear, float radial,
            float orbitalX, float orbitalY, float orbitalZ)
        {
            if (ps == null) return;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(linear.x);
            velocity.y = new ParticleSystem.MinMaxCurve(linear.y);
            velocity.z = new ParticleSystem.MinMaxCurve(linear.z);
            velocity.radial = new ParticleSystem.MinMaxCurve(radial);
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(orbitalX);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(orbitalY);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(orbitalZ);
            ps.Play();
        }

        public static ParticleSystem CreateParticles(Transform parent, string name, Color color, bool localSpace,
            float rate, float lifetimeMin, float lifetimeMax, float sizeMin, float sizeMax, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.55f));
            main.gravityModifier = -0.02f;
            main.simulationSpace = localSpace ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            main.maxParticles = 320;
            main.playOnAwake = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.18f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));
            var colorOver = ps.colorOverLifetime;
            colorOver.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.4f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            colorOver.color = new ParticleSystem.MinMaxGradient(gradient);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 2;
            ps.Play();
            return ps;
        }

        // Chispas alargadas que salen disparadas: el "debris" de energia.
        public static ParticleSystem CreateStreaks(Transform parent, string name, Color color, float rate,
            float lifetimeMin, float lifetimeMax, float sizeMin, float sizeMax, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.6f));
            main.gravityModifier = 0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 260;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 0.5f), new Keyframe(1f, 0f)));
            var colorOver = ps.colorOverLifetime;
            colorOver.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.5f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
            colorOver.color = new ParticleSystem.MinMaxGradient(gradient);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1.7f;
            renderer.velocityScale = 0.08f;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 2;
            ps.Play();
            return ps;
        }
    }

    public sealed class BillboardFx : MonoBehaviour
    {
        Camera _camera;

        void LateUpdate()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera != null) transform.rotation = _camera.transform.rotation;
        }
    }

    // Cinta procedural que mira a camara. Sustituye al TrailRenderer: controla el
    // ancho real en metros, el perfil suave a lo ancho y el desvanecido por edad.
    // El TrailRenderer se leia como una linea de 1 px; esto se lee como estela.
    public sealed class KomRibbon : MonoBehaviour
    {
        public Transform Follow;
        public float Lifetime = 0.35f;
        public float Width = 0.55f;
        public float TailWidth = 0.05f;
        public float MinDistance = 0.025f;
        public Material Material;
        public Gradient Gradient;
        public int MaxSamples = 48;

        readonly List<Vector3> _points = new List<Vector3>(64);
        readonly List<float> _stamps = new List<float>(64);
        readonly List<Vector3> _vertices = new List<Vector3>(128);
        readonly List<Color> _colors = new List<Color>(128);
        readonly List<Vector2> _uvs = new List<Vector2>(128);
        readonly List<int> _triangles = new List<int>(256);
        Mesh _mesh;
        MeshRenderer _renderer;
        bool _stopped;
        float _stopTime;

        public void Init(Transform follow, float width, float lifetime, Color color, Material material)
        {
            Follow = follow;
            Width = width;
            Lifetime = Mathf.Max(0.05f, lifetime);
            Material = material;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.Lerp(color, Color.white, 0.7f), 0f), new GradientColorKey(color, 0.5f), new GradientColorKey(color * 0.5f, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.45f, 0.45f), new GradientAlphaKey(0f, 1f) });
            Gradient = g;
            Build();
        }

        void Build()
        {
            _mesh = new Mesh { name = "KomRibbon" };
            _mesh.MarkDynamic();
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = Material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.sortingOrder = 2;
        }

        // Deja de muestrear: la cola se consume y el objeto se destruye solo.
        public void Stop()
        {
            if (_stopped) return;
            _stopped = true;
            _stopTime = Time.time;
        }

        void LateUpdate()
        {
            float now = Time.time;
            if (!_stopped)
            {
                Vector3 p = Follow != null ? Follow.position : transform.position;
                if (_points.Count == 0 || Vector3.Distance(_points[_points.Count - 1], p) >= MinDistance)
                {
                    _points.Add(p);
                    _stamps.Add(now);
                    if (_points.Count > MaxSamples)
                    {
                        _points.RemoveAt(0);
                        _stamps.RemoveAt(0);
                    }
                }
            }
            while (_stamps.Count > 0 && now - _stamps[0] > Lifetime)
            {
                _points.RemoveAt(0);
                _stamps.RemoveAt(0);
            }
            if (_stopped && _points.Count == 0)
            {
                Destroy(gameObject);
                return;
            }
            Rebuild(now);
        }

        void Rebuild(float now)
        {
            if (_mesh == null) return;
            int n = _points.Count;
            if (n < 2)
            {
                _mesh.Clear();
                return;
            }
            var cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : _points[0] + Vector3.forward * 10f;

            _vertices.Clear();
            _colors.Clear();
            _uvs.Clear();
            _triangles.Clear();

            for (int i = 0; i < n; i++)
            {
                Vector3 prev = _points[Mathf.Max(0, i - 1)];
                Vector3 next = _points[Mathf.Min(n - 1, i + 1)];
                Vector3 tangent = next - prev;
                if (tangent.sqrMagnitude < 0.000001f) tangent = Vector3.forward;
                tangent.Normalize();
                Vector3 view = camPos - _points[i];
                if (view.sqrMagnitude < 0.0001f) view = Vector3.forward;
                view.Normalize();
                Vector3 side = Vector3.Cross(tangent, view);
                if (side.sqrMagnitude < 0.000001f) side = Vector3.Cross(tangent, Vector3.up);
                if (side.sqrMagnitude < 0.000001f) side = Vector3.right;
                side.Normalize();

                float t = i / (n - 1f);                 // 0 = cola, 1 = cabeza
                float age = Mathf.Clamp01((now - _stamps[i]) / Lifetime);
                float w = Mathf.Lerp(TailWidth, Width, Mathf.Pow(t, 0.65f)) * (1f - age * 0.55f);
                Vector3 v0 = _points[i] + side * (w * 0.5f);
                Vector3 v1 = _points[i] - side * (w * 0.5f);
                _vertices.Add(v0);
                _vertices.Add(v1);
                // age 0 = cabeza (brillante), age 1 = cola (apagada).
                Color c = Gradient.Evaluate(age);
                _colors.Add(c);
                _colors.Add(c);
                _uvs.Add(new Vector2(t, 0f));
                _uvs.Add(new Vector2(t, 1f));
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 2;
                _triangles.Add(a); _triangles.Add(a + 1); _triangles.Add(a + 2);
                _triangles.Add(a + 2); _triangles.Add(a + 1); _triangles.Add(a + 3);
            }
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            if (Material != null) Destroy(Material);
            if (_mesh != null) Destroy(_mesh);
        }
    }

    // Arco electrico: LineRenderer regenerado cada `life` segundos con desplazamiento
    // perpendicular. Sin Random global: usa su propia semilla para ser determinista.
    public sealed class KomBoltFx : MonoBehaviour
    {
        LineRenderer _line;
        Material _material;
        Vector3 _a;
        Vector3 _b;
        float _width;
        float _jitter;
        float _life;
        float _timer;
        int _seed;
        int _segments = 9;
        Vector3[] _points;
        float _fade = 1f;

        public void Init(Vector3 from, Vector3 to, Color color, float width, float jitter, int seed, float life)
        {
            _a = from;
            _b = to;
            _width = width;
            _jitter = jitter;
            _seed = seed;
            _life = Mathf.Max(0.02f, life);
            _points = new Vector3[_segments];
            _line = gameObject.AddComponent<LineRenderer>();
            _material = KomSpecialFx.CreateAdditiveMaterial(color, Texture2D.whiteTexture, 2.4f);
            _line.material = _material;
            _line.useWorldSpace = false;
            _line.positionCount = _segments;
            _line.widthMultiplier = _width;
            _line.numCapVertices = 2;
            _line.numCornerVertices = 2;
            _line.alignment = LineAlignment.View;
            _line.textureMode = LineTextureMode.Stretch;
            _line.startColor = color;
            _line.endColor = color * 0.35f;
            _line.shadowCastingMode = ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.sortingOrder = 4;
            Rebuild();
        }

        public void SetFade(float fade)
        {
            _fade = Mathf.Clamp01(fade);
            if (_line == null) return;
            var c = _line.startColor;
            _line.widthMultiplier = _width * (0.4f + 0.6f * _fade);
            _line.startColor = new Color(c.r, c.g, c.b, _fade);
            var e = _line.endColor;
            _line.endColor = new Color(e.r, e.g, e.b, _fade * 0.35f);
        }

        // Cambia el extremo sin reconstruir: el jitter se regenera en el siguiente
        // ciclo para no gastar CPU cada frame.
        public void SetEnd(Vector3 to)
        {
            _b = to;
        }

        public void SetStart(Vector3 from)
        {
            _a = from;
        }

        void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= _life)
            {
                _timer = 0f;
                Rebuild();
            }
        }

        void Rebuild()
        {
            if (_line == null || _points == null) return;
            var rng = new System.Random(_seed + Mathf.RoundToInt(Time.time * 997f));
            Vector3 dir = _b - _a;
            float len = dir.magnitude;
            if (len < 0.0001f) dir = Vector3.up * 0.001f;
            Vector3 axis = dir.normalized;
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 side2 = Vector3.Cross(axis, side);
            for (int i = 0; i < _segments; i++)
            {
                float t = i / (_segments - 1f);
                // Los extremos quedan clavados; el jitter crece hacia el centro.
                float envelope = Mathf.Sin(t * Mathf.PI);
                float j1 = (float)(rng.NextDouble() * 2.0 - 1.0);
                float j2 = (float)(rng.NextDouble() * 2.0 - 1.0);
                Vector3 p = Vector3.Lerp(_a, _b, t) + (side * j1 + side2 * j2) * _jitter * envelope;
                if (i == 0) p = _a;
                else if (i == _segments - 1) p = _b;
                _points[i] = p;
            }
            _line.SetPositions(_points);
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }

    public sealed class SpecialFlashFx : MonoBehaviour
    {
        float _duration;
        float _t;
        float _scale;
        Transform _quad;
        Material _material;
        Light _light;

        public void Init(Color color, float scale, float duration)
        {
            _duration = Mathf.Max(0.05f, duration);
            _scale = scale;
            var go = KomSpecialFx.CreateGlowQuad(transform, "Flash", Color.Lerp(color, Color.white, 0.5f),
                scale, scale, KomSpecialFx.GlowTexture, 3.2f);
            _quad = go.transform;
            _material = go.GetComponent<MeshRenderer>().sharedMaterial;
            _light = KomSpecialFx.CreateLight(transform, color, 3.5f * scale, 3.2f);
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _duration);
            if (_quad != null)
                _quad.localScale = new Vector3(_scale * (0.5f + k * 1.7f), _scale * (0.5f + k * 1.7f), 1f);
            if (_material != null)
            {
                float fade = (1f - k) * (1f - k);
                _material.SetColor("_Color", new Color(fade, fade, fade, 1f));
            }
            if (_light != null) _light.intensity = 3.2f * (1f - k);
            if (_t >= _duration) Destroy(gameObject);
        }

        void OnDestroy() => KomFx.SafeDestroy(_material);
    }

    public sealed class SpecialRingFx : MonoBehaviour
    {
        float _duration;
        float _t;
        float _scale;
        Transform _ring;
        Material _material;
        float _startDelay;

        public void Init(Color color, float scale, float duration, bool flat = false)
        {
            _duration = Mathf.Max(0.1f, duration);
            _scale = scale;
            var go = KomSpecialFx.CreateRingQuad(transform, "Ring", color, scale, 0.55f, true);
            if (flat) go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            else go.AddComponent<BillboardFx>();
            _ring = go.transform;
            _material = go.GetComponent<MeshRenderer>().sharedMaterial;
            _ring.localScale = Vector3.one * 0.001f;
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _duration);
            // Curva de expansion con desaceleracion: el anillo frena al final.
            float ease = 1f - Mathf.Pow(1f - k, 2.6f);
            if (_ring != null)
            {
                float s = _scale * (0.2f + ease * 1.45f);
                _ring.localScale = new Vector3(s, s, 1f);
            }
            if (_material != null)
            {
                float fade = Mathf.Pow(1f - k, 1.6f);
                var c = _material.GetColor("_Color");
                _material.SetColor("_Color", new Color(c.r, c.g, c.b, fade));
            }
            if (_t >= _duration) Destroy(gameObject);
        }

        void OnDestroy() => KomFx.SafeDestroy(_material);
    }

    // Rafaga de notas musicales: una sola emision con velocidades al azar (semilla fija
    // por posicion para no romper el determinismo del combate) y muerte limpia.
    public sealed class NoteBurstFx : MonoBehaviour
    {
        ParticleSystem _ps;
        Material _mat;
        float _life;

        public void Init(Color color, int count)
        {
            var go = new GameObject("Notes");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.7f, 0.7f);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.6f));
            main.gravityModifier = 0.18f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 64;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = _ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.16f;
            var velocity = _ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            // TODAS las curvas en el mismo modo (dos constantes). Mezclar constante y
            // rango hace que Unity registre "Particle Velocity curves must all be in the
            // same mode" CADA FRAME, y con Error Pause activo congela el editor entero.
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.radial = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
            var colorOver = _ps.colorOverLifetime;
            colorOver.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.35f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
            colorOver.color = new ParticleSystem.MinMaxGradient(gradient);
            var size = _ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0.75f)));
            _mat = KomSpecialFx.CreateParticleMaterial(KomSpecialFx.NoteTexture);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 3;
            _ps.Play();
            _life = 1.6f;
        }

        void Update()
        {
            _life -= Time.deltaTime;
            if (_life <= 0f) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }

    // Polvo de impacto: emision unica, gravedad alta y desvanecido suave.
    public sealed class DustBurstFx : MonoBehaviour
    {
        ParticleSystem _ps;
        Material _mat;
        float _life;

        public void Init(Color tint, int count, float scale)
        {
            var go = new GameObject("Dust");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 4.2f) ;
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f * scale, 0.45f * scale);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.62f, 0.55f, 0.46f, 0.55f), new Color(0.40f, 0.35f, 0.30f, 0.35f));
            main.gravityModifier = 0.55f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 64;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = _ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f * scale;
            var size = _ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.2f)));
            var colorOver = _ps.colorOverLifetime;
            colorOver.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(tint, 0.4f), new GradientColorKey(tint, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.12f), new GradientAlphaKey(0f, 1f) });
            colorOver.color = new ParticleSystem.MinMaxGradient(gradient);
            _mat = KomSpecialFx.CreateParticleMaterial(KomSpecialFx.GlowTexture);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 1;
            _ps.Play();
            _life = 1.5f;
        }

        void Update()
        {
            _life -= Time.deltaTime;
            if (_life <= 0f) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
