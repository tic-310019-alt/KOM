using System;
using System.Collections.Generic;
using UnityEngine;

namespace KOM
{
    /// <summary>
    /// Publico de grada del Zocalo. Una sola malla de billboards animados (un draw call)
    /// construida desde un layout horneado y validado contra la geometria real de las
    /// bancadas.
    ///
    /// Los sprites vienen del sistema de publico mexicano (30 personajes x 4 fotogramas,
    /// alfa limpio). El layout vive en Resources/Crowd y llega del horno de editor
    /// (KomCrowdTools): cada asiento ya trae posicion, altura medida del escalon, personaje,
    /// espejo, tinte y fase. El runtime no sortea nada: lo que se ve es lo que se valido.
    ///
    /// ORIENTACION. El manifiesto declara, por personaje, si esta de frente, en 3/4 o de
    /// perfil y hacia que lado mira. Eso no es decorativo: un sprite de perfil puesto en la
    /// bancada que da la cara a la camara se lee como un maniqui girado y rompe la grada.
    /// El runtime comprueba la invariante al montar (bancada trasera = solo de frente, y el
    /// espejo de los laterales tiene que apuntar al centro de la arena) y se niega a montar
    /// el publico si el layout la incumple.
    ///
    /// ANIMACION. Cada personaje ocupa cuatro celdas en el atlas, pero se reproduce de dos
    /// en dos (`frameCount`): la pareja de aplauso o la de celebracion. Los cuatro
    /// fotogramas originales no forman un bucle (en la mitad de los personajes uno es
    /// "brazos arriba" y el resto "aplauso"), asi que emparejarlos por familias evita el
    /// aleteo de brazos que producia reproducirlos en orden.
    ///
    /// RELOJ. El fotograma se elige con un reloj INTEGRADO en el CPU (`_cycle`, en
    /// fotogramas) y no con `_Time.y * fps`. Multiplicar un reloj absoluto por un fps que
    /// cambia con la euforia hacia que la fase se disparase (cientos de fotogramas por
    /// segundo) y la grada parpadease; cuanto mas largo el asalto, peor. Aqui la euforia
    /// solo cambia la DERIVADA del reloj, nunca la fase ya acumulada.
    /// </summary>
    public sealed class KomStageCrowd : MonoBehaviour
    {
        public const int OrientationFront = 0;
        public const int OrientationQuarter = 1;
        public const int OrientationProfile = 2;

        /// <summary>Fotogramas por segundo si el manifiesto no lo declara.</summary>
        const float DefaultFps = 4.2f;
        /// <summary>Cuanto acelera la euforia al reloj (1 = sin cambio).</summary>
        const float EnergyRate = 0.35f;
        /// <summary>Por debajo de esto la pareja de celdas es un recorte fijo.</summary>
        public const float MinMotion = 0.010f;

        /// <summary>
        /// Tope de diferencia de estatura entre los dos fotogramas de una pareja (px de
        /// celda). Por encima de ~1.6 % el cuerpo se ve encoger y crecer en cada ciclo; es
        /// el "esta chica se hace pequena y luego grande" que se reporto. El horno no
        /// empareja poses que lo superen y el gate lo comprueba en el manifiesto.
        /// </summary>
        public const int MaxStatureDeltaPx = 4;

        [Serializable]
        public class Seat
        {
            public float x;
            public float y;
            public float z;
            public int row;
            public string bank;
            public int avatar;
            public float scale;
            public float phase;
            public float tint;
            public bool flip;
        }

        [Serializable]
        public class Layout
        {
            public string[] stageIds;
            public Seat[] seats;
        }

        [Serializable]
        public class AvatarDef
        {
            public string name;
            public string charId;
            public int orientation;
            public int facing;
            public string variant;
            public int bodyPx;
            public int footPx;
            public float worldHeight;
            /// <summary>
            /// Ritmo del ciclo respecto a _CrowdFps. Las parejas que cambian mucho de pose
            /// (manos a la boca -> brazos abajo) van mas lentas para no leerse como un tic.
            /// </summary>
            public float cycleScale;
            /// <summary>
            /// Diferencia media entre las dos celdas que se alternan. Es la prueba de que el
            /// avatar se mueve: por debajo de <see cref="MinMotion"/> es un recorte fijo.
            /// </summary>
            public float motion;
            /// <summary>
            /// Altura del cuerpo (pies -> coronilla, en px de celda) de cada fotograma de la
            /// pareja. El paquete normaliza la CAJA, no el cuerpo: si las dos poses difieren,
            /// el personaje se encoje y crece en cada ciclo. El gate exige que no pasen de
            /// <see cref="MaxStatureDeltaPx"/>.
            /// </summary>
            public int stature0;
            public int stature1;
            public float u0;
            public float v0;
            public float cellU;
            public float cellV;
        }

        [Serializable]
        public class AtlasManifest
        {
            public int frameCount;
            public float fps;
            public int cellWidthPx;
            public int cellHeightPx;
            public int atlasWidth;
            public int atlasHeight;
            public AvatarDef[] avatars;
        }

        const string AtlasPath = "Crowd/crowd_atlas";
        const string MaterialPath = "Crowd/CrowdMat";
        const float IdleEnergy = 0.06f;

        public static KomStageCrowd Instance { get; private set; }

        public int SeatCount { get; private set; }
        public int AvatarCount { get; private set; }
        public int FrameCount { get; private set; }
        public int FrontSeats { get; private set; }
        public int LateralSeats { get; private set; }
        public int CheerSeats { get; private set; }
        public Material CrowdMaterial => _material;
        public MeshRenderer CrowdRenderer => _renderer;
        public float Energy => _energy;
        public int MeshVertexCount => _mesh != null ? _mesh.vertexCount : 0;

        Material _material;
        Mesh _mesh;
        MeshRenderer _renderer;

        float _energy;
        float _target = IdleEnergy;
        float _appliedEnergy = -1f;

        /// <summary>Fotogramas base por segundo (del manifiesto).</summary>
        float _fps = DefaultFps;
        /// <summary>Reloj del ciclo en FOTOGRAMAS acumulados. Se integra, no se calcula.</summary>
        float _cycle;
        /// <summary>Reloj de gesto en segundos (respiracion, balanceo, salto).</summary>
        float _clock;

        /// <summary>
        /// Monta la grada del escenario si su definicion declara un layout. Es idempotente:
        /// una segunda llamada (preview del editor, reconstruccion) reemplaza la anterior.
        /// </summary>
        public static KomStageCrowd Attach(StageDef stage)
        {
            if (Instance != null)
            {
                // En edicion `Destroy` no esta permitido y Unity escupe un error por cada
                // preview que se rehace (el horno y el gate montan el combate varias veces).
                Kill(Instance.gameObject);
                Instance = null;
            }
            if (stage == null || string.IsNullOrEmpty(stage.crowdLayout)) return null;

            var layoutAsset = LoadJson(stage.crowdLayout);
            var manifestAsset = LoadJson(AtlasPath);
            var baseMaterial = LoadMaterial();
            if (layoutAsset == null || manifestAsset == null || baseMaterial == null)
            {
                Debug.LogError($"[KOM][CROWD] Faltan recursos: layout '{stage.crowdLayout}', " +
                               $"atlas '{AtlasPath}' o material '{MaterialPath}'.");
                return null;
            }

            Layout layout;
            AtlasManifest manifest;
            try
            {
                layout = JsonUtility.FromJson<Layout>(layoutAsset.text);
                manifest = JsonUtility.FromJson<AtlasManifest>(manifestAsset.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"[KOM][CROWD] JSON invalido: {e.Message}");
                return null;
            }

            if (layout == null || layout.seats == null || layout.seats.Length == 0 ||
                manifest == null || manifest.avatars == null || manifest.avatars.Length == 0)
            {
                Debug.LogError("[KOM][CROWD] Layout o atlas vacio.");
                return null;
            }
            if (layout.stageIds != null && Array.IndexOf(layout.stageIds, stage.id) < 0)
            {
                Debug.LogError($"[KOM][CROWD] El layout '{stage.crowdLayout}' no declara el escenario '{stage.id}'.");
                return null;
            }
            if (manifest.frameCount < 1 || manifest.cellWidthPx <= 0 || manifest.cellHeightPx <= 0)
            {
                Debug.LogError("[KOM][CROWD] Manifiesto del atlas invalido.");
                return null;
            }

            var root = new GameObject("StageCrowd_" + stage.id);
            var crowd = root.AddComponent<KomStageCrowd>();
            if (!crowd.Build(layout, manifest, baseMaterial))
            {
                Kill(root);
                return null;
            }
            Instance = crowd;
            return crowd;
        }

        /// <summary>
        /// Carga un JSON de Resources. **En el editor se lee del disco a proposito**:
        /// `Resources.Load` devuelve la copia que Unity tenia en memoria, asi que despues de
        /// hornear (el horno escribe el layout con File.WriteAllText) puede entregar el
        /// layout/manifiesto ANTERIOR. El sintoma es desconcertante: el publico se aborta
        /// con "layout incoherente" aunque en disco todo cuadre, y solo pasa cuando una
        /// reconstruccion de contenido pilla la importacion a medias.
        /// </summary>
        static TextAsset LoadJson(string resourcesPath)
        {
#if UNITY_EDITOR
            var direct = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Resources/" + resourcesPath + ".json");
            if (direct != null) return direct;
#endif
            return Resources.Load<TextAsset>(resourcesPath);
        }

        static Material LoadMaterial()
        {
#if UNITY_EDITOR
            var direct = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Resources/" + MaterialPath + ".mat");
            if (direct != null) return direct;
#endif
            return Resources.Load<Material>(MaterialPath);
        }

        /// <summary>Destruye respetando el modo: en edicion hace falta DestroyImmediate.</summary>
        static void Kill(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        bool Build(Layout layout, AtlasManifest manifest, Material baseMaterial)
        {
            if (!ValidateOrientation(layout, manifest)) return false;
            if (!ValidateMotion(layout, manifest)) return false;

            int count = layout.seats.Length;
            var verts = new Vector3[count * 4];
            var uvs = new Vector2[count * 4];
            var uv2 = new Vector4[count * 4];
            var uv3 = new Vector4[count * 4];
            var uv4 = new Vector4[count * 4];
            var colors = new Color[count * 4];
            var tris = new List<int>(count * 6);
            float aspect = manifest.cellWidthPx / (float)manifest.cellHeightPx;

            int v = 0;
            var boundsMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var boundsMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            for (int i = 0; i < count; i++)
            {
                var s = layout.seats[i];
                var avatar = manifest.avatars[Mathf.Clamp(s.avatar, 0, manifest.avatars.Length - 1)];

                float height = Mathf.Max(0.5f, avatar.worldHeight * (s.scale > 0f ? s.scale : 1f));
                float width = height * Mathf.Max(0.1f, aspect);
                float halfW = width * 0.5f;
                // El borde inferior del sprite es transparente: se baja el quad para que
                // los pies (footPx) apoyen exactamente en el escalon medido.
                float footOffset = height * avatar.footPx / manifest.cellHeightPx;

                float depth = Mathf.Clamp01(s.row / 2f);
                float tint = s.tint > 0f ? s.tint : 1f;
                var anchor = new Vector3(s.x, s.y - footOffset, s.z);
                boundsMin = Vector3.Min(boundsMin, anchor);
                boundsMax = Vector3.Max(boundsMax, anchor);

                Vector2[] corners =
                {
                    new Vector2(-halfW, 0f),
                    new Vector2(halfW, 0f),
                    new Vector2(-halfW, height),
                    new Vector2(halfW, height),
                };
                Vector2[] cornerUv = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };

                for (int k = 0; k < 4; k++)
                {
                    verts[v] = anchor;
                    uvs[v] = cornerUv[k];
                    uv2[v] = new Vector4(corners[k].x, corners[k].y, s.flip ? 1f : 0f, s.phase);
                    uv3[v] = new Vector4(avatar.u0, avatar.v0, avatar.cellU, avatar.cellV);
                    // TEXCOORD3: ritmo del ciclo de este personaje (1 = el del material).
                    uv4[v] = new Vector4(avatar.cycleScale > 0.01f ? avatar.cycleScale : 1f, 0f, 0f, 0f);
                    colors[v] = new Color(tint, tint, tint, depth);
                    v++;
                }

                tris.Add(v - 4);
                tris.Add(v - 3);
                tris.Add(v - 2);
                tris.Add(v - 2);
                tris.Add(v - 3);
                tris.Add(v - 1);

                if (avatar.orientation == OrientationFront) FrontSeats++;
                else LateralSeats++;
                if (avatar.variant == "cheer") CheerSeats++;
            }

            if (v == 0)
            {
                Debug.LogError("[KOM][CROWD] Ningun asiento valido.");
                return false;
            }

            _mesh = new Mesh { name = "CrowdMesh" };
            _mesh.SetVertices(verts, 0, v);
            _mesh.SetUVs(0, uvs, 0, v);
            _mesh.SetUVs(1, uv2, 0, v);
            _mesh.SetUVs(2, uv3, 0, v);
            _mesh.SetUVs(3, uv4, 0, v);
            _mesh.SetColors(colors, 0, v);
            _mesh.SetTriangles(tris, 0, true);
            // Los vertices son anclas de pie: se expanden los bounds a mano para que el
            // culling no recorte la grada al mirar de lado.
            var center = (boundsMin + boundsMax) * 0.5f;
            var size = boundsMax - boundsMin;
            _mesh.bounds = new Bounds(center + Vector3.up * 1.2f,
                new Vector3(size.x + 3f, size.y + 4f, size.z + 3f));

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _material = new Material(baseMaterial) { name = "CrowdMat_Runtime" };
            _material.SetFloat("_CrowdEnergy", _energy);
            // El numero de fotogramas del ciclo sale del manifiesto, no del material: asi
            // el atlas y el shader no pueden desincronizarse. Lo mismo con el ritmo base.
            _fps = manifest.fps > 0.1f ? manifest.fps : DefaultFps;
            _material.SetFloat("_CrowdFrames", manifest.frameCount);
            _material.SetFloat("_CrowdFps", _fps);
            _material.SetFloat("_CrowdCycle", 0f);
            _material.SetFloat("_CrowdTime", 0f);
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            SeatCount = v / 4;
            AvatarCount = manifest.avatars.Length;
            FrameCount = manifest.frameCount;
            return SeatCount > 0;
        }

        /// <summary>
        /// La regla que el usuario pidio con todas las letras: los que dan la cara a la
        /// camara tienen que estar de frente, y los laterales solo van en los flancos.
        /// Se comprueba aqui, en runtime, ademas de en el horno, porque un layout viejo o
        /// editado a mano no puede colarse.
        /// </summary>
        bool ValidateOrientation(Layout layout, AtlasManifest manifest)
        {
            int badRear = 0, badMirror = 0;
            foreach (var s in layout.seats)
            {
                if (s.avatar < 0 || s.avatar >= manifest.avatars.Length) continue;
                var av = manifest.avatars[s.avatar];
                bool frontal = av.orientation == OrientationFront;
                if (s.bank == "rear" && !frontal) badRear++;
                if (!frontal)
                {
                    // `facing` dice hacia donde mira el arte (+1 derecha de pantalla). El
                    // flanco derecho se ve desde el otro lado, asi que su sprite va espejado.
                    int facing = av.facing == 0 ? 1 : av.facing;
                    bool wantFlip = (s.bank == "right") ^ (facing < 0);
                    if (s.flip != wantFlip) badMirror++;
                }
            }
            if (badRear > 0 || badMirror > 0)
            {
                Debug.LogError($"[KOM][CROWD] Layout incoherente: {badRear} asientos de la " +
                               $"bancada frontal con arte lateral y {badMirror} laterales " +
                               $"espejados al lado contrario. Se aborta el publico. " +
                               $"Si acabas de cambiar el atlas, vuelve a hornear " +
                               $"(King of Music/Setup/Preparar publico del Zocalo).");
                return false;
            }
            // Los asientos de flanco con arte de frente son legitimos (las esquinas traseras
            // de cada flanco se ven casi de cara): no se avisa. El horno si los cuenta.
            return true;
        }

        /// <summary>
        /// Ningun asiento puede llevar una pareja de celdas identicas. El horno ya lo
        /// impide (nunca publica dos veces el mismo fotograma), pero un atlas viejo con la
        /// grada llena de recortes fijos es exactamente el fallo que se reporto, asi que se
        /// comprueba tambien aqui: si el layout apunta a un avatar congelado, no se monta.
        /// </summary>
        bool ValidateMotion(Layout layout, AtlasManifest manifest)
        {
            int frozen = 0;
            string example = null;
            foreach (var s in layout.seats)
            {
                if (s.avatar < 0 || s.avatar >= manifest.avatars.Length) continue;
                var av = manifest.avatars[s.avatar];
                if (av.motion >= MinMotion) continue;
                frozen++;
                example ??= $"{s.bank}/f{s.row} {av.name} (motion {av.motion:F3})";
            }
            if (frozen == 0) return true;
            Debug.LogError($"[KOM][CROWD] {frozen} asientos con un avatar sin animacion " +
                           $"(p.ej. {example}). Vuelve a hornear el atlas y el layout " +
                           $"(python tools/crowd/prepare_crowd_atlas.py + menu de preparacion).");
            return false;
        }

        void Update()
        {
            if (_material == null) return;
            // Un tiron de frame (carga, pausa del editor) no puede teletransportar el ciclo:
            // se acota el paso. Con el reloj integrado, un dt enorme solo sumaria fotogramas
            // de mas; acotarlo mantiene la grada legible.
            float dt = Mathf.Min(Time.deltaTime, 0.1f);

            _target = Mathf.MoveTowards(_target, IdleEnergy, dt * 0.30f);
            float rate = _target > _energy ? 3.2f : 0.55f;
            _energy = Mathf.MoveTowards(_energy, _target, dt * rate);

            // Reloj del ciclo: se acumula. La euforia cambia la velocidad, no la fase.
            _clock += dt;
            _cycle += dt * _fps * (1f + _energy * EnergyRate);
            _material.SetFloat("_CrowdCycle", _cycle);
            _material.SetFloat("_CrowdTime", _clock);

            if (Mathf.Abs(_energy - _appliedEnergy) > 0.002f)
            {
                _appliedEnergy = _energy;
                _material.SetFloat("_CrowdEnergy", _energy);
            }
        }

        /// <summary>Fotogramas acumulados del ciclo (para sondas y gates).</summary>
        public float Cycle => _cycle;
        /// <summary>Fotogramas por segundo base del manifiesto.</summary>
        public float Fps => _fps;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_mesh != null) Kill(_mesh);
            if (_material != null) Kill(_material);
        }

        // --- Reacciones del combate. Sin instancia no hacen nada (escenarios sin publico). ---

        /// <summary>Arranque del asalto: la grada despierta con la entrada de camara.</summary>
        public static void RoundStart() => Hype(0.55f);

        /// <summary>Golpe que pesa o especial: sube la euforia (no salta en cada jab).</summary>
        public static void Hype(float amount)
        {
            if (Instance == null) return;
            Instance._target = Mathf.Clamp01(Mathf.Max(Instance._target, amount));
        }

        /// <summary>K.O.: la grada entera se levanta.</summary>
        public static void Knockout()
        {
            if (Instance == null) return;
            Instance._target = 1f;
            Instance._energy = Mathf.Max(Instance._energy, 0.55f);
        }

        /// <summary>Súper cargado: la grada empuja antes del golpe grande.</summary>
        public static void SuperReady() => Hype(0.62f);
    }
}
