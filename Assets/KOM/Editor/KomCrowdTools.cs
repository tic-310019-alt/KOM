using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KOM.EditorTools
{
    /// <summary>
    /// Publico de grada del Zocalo: peritaje, horno de asientos y gates.
    ///
    /// Nada se coloca a ojo. Las bancadas son parte de la malla del escenario, asi que sus
    /// alturas se miden con rayos sobre MeshColliders temporales. El horno reparte los
    /// asientos sobre los escalones medidos, comprueba asiento a asiento que el pie cae en
    /// el escalon, y guarda un layout determinista que el runtime solo construye.
    /// </summary>
    public static class KomCrowdTools
    {
        const string SceneDir = "Assets/Scenes/KOM";
        const string OutDir = "Logs/crowd";
        const string AtlasPngPath = "Assets/Resources/Crowd/crowd_atlas.png";
        const string AtlasJsonPath = "Assets/Resources/Crowd/crowd_atlas.json";
        const string MaterialPath = "Assets/Resources/Crowd/CrowdMat.mat";
        const string LayoutPath = "Assets/Resources/Crowd/crowd_zocalo.json";
        const string LayoutResource = "Crowd/crowd_zocalo";

        const float SeatSpacing = 0.68f;
        const float MaxSeatHeightError = 0.025f;

        [MenuItem("King of Music/Setup/Preparar publico del Zocalo", priority = 22)]
        public static void MenuPrepare()
        {
            Debug.Log(PrepareAll());
        }

        [MenuItem("King of Music/Diagnostico/50 - Sondear gradas del Zocalo", priority = 250)]
        public static void MenuProbeZocalo()
        {
            Debug.Log(ProbeZocalo());
        }

        [MenuItem("King of Music/Diagnostico/51 - Validar publico del Zocalo", priority = 251)]
        public static void MenuValidate()
        {
            var errors = new List<string>();
            int checks = 0;
            var report = new StringBuilder(ValidateCrowd(errors, ref checks, true));
            if (errors.Count > 0)
            {
                report.AppendLine($"VERDICT: FAIL ({errors.Count} errores)");
                foreach (var e in errors) report.AppendLine("  - " + e);
            }
            else
            {
                report.AppendLine($"VERDICT: PASS ({checks} checks)");
            }
            File.WriteAllText($"{OutDir}/report.txt", report.ToString());
            Debug.Log(report.ToString());
        }

        public static string PrepareAll()
        {
            Directory.CreateDirectory(OutDir);
            ConfigureAtlas();
            EnsureMaterial();
            var report = new StringBuilder();
            report.Append(BakeZocalo());
            var errors = new List<string>();
            int checks = 0;
            report.AppendLine(ValidateCrowd(errors, ref checks, true));
            if (errors.Count > 0)
            {
                report.AppendLine($"VERDICT: FAIL ({errors.Count} errores)");
                foreach (var e in errors) report.AppendLine("  - " + e);
            }
            else
            {
                report.AppendLine($"VERDICT: PASS ({checks} checks)");
            }
            File.WriteAllText($"{OutDir}/report.txt", report.ToString());
            return report.ToString();
        }

        // ---------------------------------------------------------------- assets

        static void ConfigureAtlas()
        {
            var importer = AssetImporter.GetAtPath(AtlasPngPath) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(AtlasPngPath);
                importer = AssetImporter.GetAtPath(AtlasPngPath) as TextureImporter;
            }
            if (importer == null)
            {
                Debug.LogError($"[KOM][CROWD] No se pudo importar {AtlasPngPath}");
                return;
            }
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            // 2048x3072 NO es potencia de dos: con el escalado NPOT por defecto Unity la
            // convierte a 2048x4096 y las UV del manifiesto (calculadas sobre 3072) dejan
            // de valer. La textura se queda tal cual.
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.crunchedCompression = false;
            var platform = importer.GetPlatformTextureSettings("DefaultTexturePlatform");
            platform.overridden = true;
            platform.maxTextureSize = 4096;
            platform.textureCompression = TextureImporterCompression.CompressedHQ;
            platform.crunchedCompression = false;
            importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();
        }

        static void EnsureMaterial()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPngPath);
            var shader = Shader.Find("KOM/CrowdBW");
            if (atlas == null || shader == null)
            {
                Debug.LogError("[KOM][CROWD] Falta el atlas o el shader KOM/CrowdBW.");
                return;
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "CrowdMat" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            else
            {
                mat.shader = shader;
            }
            mat.SetTexture("_BaseMap", atlas);
            mat.SetFloat("_Cutoff", 0.35f);
            mat.SetFloat("_CrowdLit", 0.85f);
            mat.SetFloat("_BlackPoint", 0.02f);
            mat.SetFloat("_Contrast", 1.0f);
            mat.SetFloat("_DepthDarken", 0.90f);
            // Dos fotogramas por ciclo: el atlas guarda parejas coherentes, no los cuatro
            // fotogramas originales. Ver tools/crowd/prepare_crowd_atlas.py.
            var manifest = LoadManifest();
            float fps = manifest != null && manifest.fps > 0.1f ? manifest.fps : 4.2f;
            mat.SetFloat("_CrowdFrames", 2f);
            mat.SetFloat("_CrowdFps", fps);
            mat.SetFloat("_CrowdCycle", 0f);
            mat.SetFloat("_CrowdTime", 0f);
            // Sin salto ni respiracion trasladada: la grada no se despega del escalon.
            // La euforia se expresa con balanceo y pulso de escala (ver KOMCrowdBW).
            mat.SetFloat("_CrowdSway", 0.006f);
            mat.SetFloat("_CrowdPulse", 0.008f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- horno

        class Tread
        {
            public float y;
            public float depthCenter;
            public float seatLo;
            public float seatHi;
        }

        static readonly string[] Banks = { "rear", "left", "right" };
        static readonly bool[] BankAlongX = { true, false, false };
        // signo del eje de profundidad: trasero +Z, izquierdo -X, derecho +X
        static readonly int[] BankDepthSign = { 1, -1, 1 };

        public static string BakeZocalo()
        {
            Directory.CreateDirectory(OutDir);
            var report = new StringBuilder();
            report.AppendLine("KOM PUBLICO · horno de asientos del Zocalo");

            string previousScene = EditorSceneManager.GetActiveScene().path;
            EditorSceneManager.OpenScene($"{SceneDir}/Combat.unity", OpenSceneMode.Single);
            var controller = UnityEngine.Object.FindAnyObjectByType<CombatController>();
            if (controller == null) throw new InvalidOperationException("Falta CombatController.");
            controller.BuildEditorPreview("juan_grabriel", "zocalo");
            var stageRoot = FindStageRoot();
            if (stageRoot == null) throw new InvalidOperationException("No se encontro el prefab zocalo(Clone).");

            var colliders = AddProbeColliders(stageRoot);
            Physics.SyncTransforms();
            var seats = new List<KomStageCrowd.Seat>();
            var bakeErrors = new List<string>();
            try
            {
                foreach (var bank in Banks)
                {
                    int bi = Array.IndexOf(Banks, bank);
                    bool alongX = BankAlongX[bi];
                    int sign = BankDepthSign[bi];
                    var treads = MeasureTreads(!alongX, sign);
                    report.AppendLine($"{bank}: {treads.Count} escalones medidos");
                    if (treads.Count != 3)
                    {
                        bakeErrors.Add($"{bank}: se esperaban 3 escalones y se midieron {treads.Count}.");
                        continue;
                    }
                    for (int row = 0; row < treads.Count; row++)
                    {
                        var tr = treads[row];
                        var rowSeats = BuildRow(bank, bi, row, tr, alongX);
                        seats.AddRange(rowSeats);
                        report.AppendLine($"  fila {row}: y={tr.y:F3} fondo={tr.depthCenter:F2} " +
                                          $"tramo=[{tr.seatLo:F2},{tr.seatHi:F2}] asientos={rowSeats.Count}");
                    }
                }
            }
            finally
            {
                RemoveProbeColliders(colliders);
            }

            if (bakeErrors.Count > 0)
            {
                foreach (var e in bakeErrors) report.AppendLine("ERROR: " + e);
                File.WriteAllText($"{OutDir}/bake.txt", report.ToString());
                if (!string.IsNullOrEmpty(previousScene))
                    EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
                throw new InvalidOperationException("Horno de publico fallido: " + string.Join(" | ", bakeErrors));
            }

            // La camara de pelea ya existe (BuildEditorPreview monta el rig y hace Snap).
            // Se mide el angulo aparente de cada asiento contra ESTA camara, no contra una
            // posicion inventada: es la que va a mirar la grada durante todo el combate.
            var fightCam = FindCombatCamera();
            if (fightCam == null) throw new InvalidOperationException("No hay camara de combate.");
            report.AppendLine($"camara de pelea: {fightCam.transform.position.ToString("F2")}");
            AssignAvatars(seats, fightCam.transform.position);
            var layout = new KomStageCrowd.Layout { stageIds = new[] { "zocalo", "zocalo_final" }, seats = seats.ToArray() };
            File.WriteAllText(LayoutPath, JsonUtility.ToJson(layout, true), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(LayoutPath);
            report.AppendLine($"layout: {seats.Count} asientos -> {LayoutPath}");
            report.AppendLine($"asientos: bancadas={string.Join(", ", CountByBank(seats))}");
            File.WriteAllText($"{OutDir}/bake.txt", report.ToString());

            // Restaura la escena de trabajo para no dejar el editor en Combat por un horno.
            if (!string.IsNullOrEmpty(previousScene))
                EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
            return report.ToString();
        }

        static List<string> CountByBank(List<KomStageCrowd.Seat> seats)
        {
            var result = new List<string>();
            foreach (var bank in Banks)
            {
                int n = 0;
                foreach (var s in seats) if (s.bank == bank) n++;
                result.Add($"{bank}:{n}");
            }
            return result;
        }

        static List<Tread> MeasureTreads(bool depthIsX, int sign)
        {
            // Muestreo fino del perfil perpendicular a la bancada en el eje central.
            float from = sign > 0 ? 8.4f : -11.4f;
            float to = sign > 0 ? 11.4f : -8.4f;
            var values = new List<Vector2>();
            for (float p = from; p <= to + 0.001f; p += 0.05f)
            {
                float x = depthIsX ? p : 0f;
                float z = depthIsX ? 0f : p;
                float y = RayFloor(x, z);
                if (!float.IsNaN(y)) values.Add(new Vector2(p, y));
            }

            var treads = new List<Tread>();
            int i = 0;
            while (i < values.Count)
            {
                int j = i;
                float sum = 0f;
                var run = new List<Vector2>();
                while (j < values.Count)
                {
                    if (run.Count == 0)
                    {
                        run.Add(values[j]);
                        sum = values[j].y;
                        j++;
                        continue;
                    }
                    float median = sum / run.Count;
                    if (Mathf.Abs(values[j].y - median) > 0.02f) break;
                    run.Add(values[j]);
                    sum += values[j].y;
                    j++;
                }
                float runLength = run.Count > 0 ? run[run.Count - 1].x - run[0].x : 0f;
                float yMedian = run.Count > 0 ? sum / run.Count : 0f;
                if (runLength >= 0.30f && yMedian > 0.02f)
                {
                    float depthCenter = (run[0].x + run[run.Count - 1].x) * 0.5f;
                    var tread = new Tread { y = yMedian, depthCenter = depthCenter };
                    MeasureSeatRun(tread, depthIsX);
                    treads.Add(tread);
                }
                i = Mathf.Max(j, i + 1);
            }
            treads.Sort((a, b) => a.y.CompareTo(b.y));
            return treads;
        }

        static void MeasureSeatRun(Tread tread, bool depthIsX)
        {
            // Recorrido del escalon a lo largo de la bancada: sus extremos reales (los
            // postes de las esquinas lo interrumpen) definen donde caben asientos.
            float bestLo = 0f, bestHi = 0f, bestLen = 0f;
            float runLo = 0f;
            bool open = false;
            for (float p = -8.4f; p <= 8.4f + 0.001f; p += 0.05f)
            {
                float x = depthIsX ? tread.depthCenter : p;
                float z = depthIsX ? p : tread.depthCenter;
                float y = RayFloor(x, z);
                bool ok = !float.IsNaN(y) && Mathf.Abs(y - tread.y) <= 0.025f;
                if (ok && !open) { open = true; runLo = p; }
                if (!ok && open)
                {
                    open = false;
                    float len = p - runLo;
                    if (len > bestLen) { bestLen = len; bestLo = runLo; bestHi = p; }
                }
            }
            if (open)
            {
                float p = 8.4f;
                float len = p - runLo;
                if (len > bestLen) { bestLen = len; bestLo = runLo; bestHi = p; }
            }
            tread.seatLo = bestLo + 0.30f;
            tread.seatHi = bestHi - 0.30f;
        }

        static List<KomStageCrowd.Seat> BuildRow(string bank, int bankIndex, int row, Tread tread, bool alongX)
        {
            var seats = new List<KomStageCrowd.Seat>();
            float length = tread.seatHi - tread.seatLo;
            if (length < 1f) return seats;
            int count = Mathf.Max(1, Mathf.FloorToInt(length / SeatSpacing) + 1);
            float step = count > 1 ? length / (count - 1) : 0f;
            for (int i = 0; i < count; i++)
            {
                float u = tread.seatLo + i * step;
                int salt = bankIndex * 1013 + row * 977 + i * 131;
                float jitterSeat = (Hash01(salt) - 0.5f) * 0.05f;
                float jitterDepth = (Hash01(salt ^ 0x5bd1e995) - 0.5f) * 0.04f;

                float seatCoord = u + jitterSeat;
                float depth = tread.depthCenter + jitterDepth;
                float x = alongX ? seatCoord : depth;
                float z = alongX ? depth : seatCoord;

                float yHit = RayFloor(x, z);
                if (float.IsNaN(yHit) || Mathf.Abs(yHit - tread.y) > MaxSeatHeightError)
                    continue;

                seats.Add(new KomStageCrowd.Seat
                {
                    x = Round(x),
                    y = Round(tread.y),
                    z = Round(z),
                    row = row,
                    bank = bank,
                    avatar = 0,
                    scale = 1f,
                    phase = Hash01(salt ^ 0x27d4eb2d),
                    tint = 1f,
                    flip = false,
                });
            }
            return seats;
        }

        // Umbrales de orientacion aparente (grados). Salen de medir la grada contra la
        // camara de pelea: la bancada trasera queda a 0-18 grados (de frente) y los flancos
        // de 25 a 46. La panoramica de apertura mira los flancos casi de perfil (hasta 88
        // grados en las esquinas delanteras), pero dura 2 s y esos asientos caen en el
        // borde del encuadre.
        const float FrontMaxYaw = 22f;
        const float QuarterMaxYaw = 50f;
        // Cuanto pesa la panoramica de apertura en el angulo que se pide al sprite.
        //
        // ESTABA A 0.30 Y ERA UN BUG VISIBLE: con ese peso las esquinas delanteras de los
        // flancos (las mas cercanas a la camara, x = +-9, z ~ -5) pasaban de 50 grados y
        // recibian arte de PERFIL. Durante el combate la camara las ve a 45, asi que la
        // grada mostraba un cuerpo de lado entre gente de frente: "ese tipo esta girado y
        // no va ahi". El sprite es un billboard y siempre mira a la camara; el angulo del
        // arte tiene que ser el de la PELEA, que es el 99 % del tiempo de pantalla.
        const float IntroWeight = 0f;
        const float IntroZoom = 2.3f;
        // Proporcion de asientos con la pareja de celebracion (brazos arriba).
        const float CheerShare = 0.22f;

        /// <summary>
        /// Reparte personajes por asiento. La regla no es "alternar la lista": cada asiento
        /// pide una orientacion segun el angulo al que lo ve la camara, y solo se le ofrece
        /// arte de esa familia. La bancada trasera, que es la que da la cara, no recibe
        /// NUNCA un sprite de 3/4 ni de perfil.
        /// </summary>
        static void AssignAvatars(List<KomStageCrowd.Seat> seats, Vector3 fightCam)
        {
            var manifest = LoadManifest();
            int avatarCount = manifest != null && manifest.avatars != null ? manifest.avatars.Length : 0;
            if (avatarCount == 0)
                throw new InvalidOperationException("No hay manifiesto de atlas para repartir.");

            var pools = new Dictionary<int, List<int>>
            {
                [KomStageCrowd.OrientationFront] = new List<int>(),
                [KomStageCrowd.OrientationQuarter] = new List<int>(),
                [KomStageCrowd.OrientationProfile] = new List<int>(),
            };
            for (int i = 0; i < avatarCount; i++)
            {
                var av = manifest.avatars[i];
                int key = pools.ContainsKey(av.orientation) ? av.orientation : KomStageCrowd.OrientationQuarter;
                pools[key].Add(i);
            }
            foreach (var list in pools.Values) Shuffle(list, 20261106);
            if (pools[KomStageCrowd.OrientationProfile].Count == 0)
                pools[KomStageCrowd.OrientationProfile] = pools[KomStageCrowd.OrientationQuarter];

            // Camara de la panoramica: misma logica que CombatCameraRig (alejarse en Z y
            // subir hasta mirar el pecho). Se conserva para el informe, no para repartir:
            // ver IntroWeight.
            var introCam = new Vector3(fightCam.x, fightCam.y * IntroZoom, fightCam.z * IntroZoom);
            var cursor = new Dictionary<int, int> { [0] = 0, [1] = 0, [2] = 0 };
            var yawHistogram = new int[7];
            int lastChar = -1;
            string lastRow = null;

            for (int i = 0; i < seats.Count; i++)
            {
                var s = seats[i];
                var pos = new Vector3(s.x, s.y, s.z);
                float yawFight = ApparentYaw(pos, fightCam);
                float yawIntro = ApparentYaw(pos, introCam);
                float yaw = Mathf.Lerp(yawFight, yawIntro, IntroWeight);
                yawHistogram[Mathf.Clamp(Mathf.FloorToInt(yaw / 15f), 0, 6)]++;

                int orientation = yaw < FrontMaxYaw ? KomStageCrowd.OrientationFront
                    : yaw < QuarterMaxYaw ? KomStageCrowd.OrientationQuarter
                    : KomStageCrowd.OrientationProfile;
                var pool = pools[orientation];
                if (pool.Count == 0) pool = pools[KomStageCrowd.OrientationQuarter];

                // Evita que dos asientos contiguos de la misma fila compartan personaje.
                string rowKey = s.bank + "/" + s.row;
                bool sameRow = rowKey == lastRow && lastChar >= 0;
                string lastCharId = sameRow ? manifest.avatars[lastChar].charId : null;
                int pick = -1;
                for (int attempt = 0; attempt < pool.Count; attempt++)
                {
                    int candidate = pool[(cursor[orientation] + attempt) % pool.Count];
                    if (!sameRow || manifest.avatars[candidate].charId != lastCharId)
                    {
                        pick = candidate;
                        cursor[orientation] = (cursor[orientation] + attempt + 1) % pool.Count;
                        break;
                    }
                }
                if (pick < 0)
                {
                    pick = pool[cursor[orientation]];
                    cursor[orientation] = (cursor[orientation] + 1) % pool.Count;
                }

                s.avatar = pick;
                lastChar = pick;
                lastRow = rowKey;

                // Variante: la mayoria aplaude; una parte celebra con los brazos arriba. Se
                // sortea con hash para que quede repartida y sea reproducible.
                bool cheer = Hash01((uint)i * 2246822519u + 374761393u) < CheerShare;
                if (cheer != (manifest.avatars[pick].variant == "cheer"))
                {
                    int twin = FindTwin(manifest.avatars, pick, cheer);
                    if (twin >= 0) { s.avatar = twin; pick = twin; lastChar = twin; }
                }

                var def = manifest.avatars[pick];
                bool frontal = def.orientation == KomStageCrowd.OrientationFront;
                int facing = def.facing == 0 ? 1 : def.facing;
                s.flip = frontal
                    // De frente el espejo es solo variedad: no cambia hacia donde mira.
                    ? Hash01((uint)i * 747796405u + 2891336453u) < 0.22f
                    // De lado, el sprite tiene que mirar al centro de la arena.
                    : (s.bank == "right") ^ (facing < 0);

                s.scale = 0.93f + Hash01((uint)i * 2654435761u) * 0.14f;
                s.tint = 0.80f + Hash01((uint)i * 40503u + 17u) * 0.22f;
                s.phase = Hash01((uint)i * 97u + 31u);
                s.tint = Mathf.Round(s.tint * 1000f) / 1000f;
                s.scale = Mathf.Round(s.scale * 1000f) / 1000f;
                s.phase = Mathf.Round(s.phase * 1000f) / 1000f;
            }

            var hist = new StringBuilder();
            hist.Append("  angulo aparente (grados, 15 por tramo): ");
            for (int b = 0; b < yawHistogram.Length; b++)
                hist.Append($"{b * 15}-{b * 15 + 15}:{yawHistogram[b]} ");
            Debug.Log("[KOM][CROWD] " + hist);
        }

        /// <summary>Hermano del mismo personaje en la otra variante (aplauso / celebracion).</summary>
        static int FindTwin(KomStageCrowd.AvatarDef[] avatars, int index, bool cheer)
        {
            if (string.IsNullOrEmpty(avatars[index].charId)) return -1;
            string want = cheer ? "cheer" : "clap";
            for (int i = 0; i < avatars.Length; i++)
                if (avatars[i].charId == avatars[index].charId && avatars[i].variant == want) return i;
            return -1;
        }

        /// <summary>
        /// Angulo entre "hacia donde mira el espectador" (el centro de la arena) y la
        /// direccion a la camara. 0 = lo vemos de frente, 90 = de perfil.
        /// </summary>
        static float ApparentYaw(Vector3 seatPos, Vector3 camPos)
        {
            var fwd = new Vector3(-seatPos.x, 0f, -seatPos.z);
            var toCam = camPos - seatPos;
            toCam.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f || toCam.sqrMagnitude < 0.0001f) return 0f;
            return Vector3.Angle(fwd, toCam);
        }

        static void Shuffle(List<int> list, int seed)
        {
            var rng = new System.Random(seed);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        static KomStageCrowd.AtlasManifest LoadManifest()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AtlasJsonPath);
            if (asset == null) return null;
            try { return JsonUtility.FromJson<KomStageCrowd.AtlasManifest>(asset.text); }
            catch { return null; }
        }

        static float Hash01(int x) => Hash01(unchecked((uint)x));

        static float Hash01(uint x)
        {
            unchecked
            {
                x ^= x >> 16;
                x *= 0x7feb352d;
                x ^= x >> 15;
                x *= 0x846ca68b;
                x ^= x >> 16;
                return (x & 0xFFFFFF) / 16777215f;
            }
        }

        static float Round(float v) => Mathf.Round(v * 1000f) / 1000f;

        // ---------------------------------------------------------------- gates

        public static string ValidateCrowd(List<string> errors, ref int checks, bool capture)
        {
            Directory.CreateDirectory(OutDir);
            var report = new StringBuilder();
            report.AppendLine("KOM PUBLICO · validacion del Zocalo");

            var stages = new List<StageDef>();
            foreach (var s in KomData.Stages.stages)
                if (s != null && !string.IsNullOrEmpty(s.crowdLayout)) stages.Add(s);
            Check(errors, ref checks, stages.Count > 0, "Ningun escenario declara crowdLayout.");
            foreach (var s in stages)
                Check(errors, ref checks, s.crowdLayout == LayoutResource,
                    $"{s.id}: crowdLayout debe ser '{LayoutResource}' y dice '{s.crowdLayout}'.");

            // Se leen del DISCO, no con Resources.Load: el horno acaba de escribir el layout
            // y Resources devuelve la copia que ya tenia en memoria, asi que el gate estaria
            // validando el layout viejo contra el atlas nuevo.
            var layoutAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutPath);
            var manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(AtlasJsonPath);
            var atlasTex = Resources.Load<Texture2D>("Crowd/crowd_atlas");
            var mat = Resources.Load<Material>("Crowd/CrowdMat");
            Check(errors, ref checks, layoutAsset != null, $"Falta Resources/{LayoutResource}.json");
            Check(errors, ref checks, manifestAsset != null, "Falta Resources/Crowd/crowd_atlas.json");
            Check(errors, ref checks, atlasTex != null, "Falta Resources/Crowd/crowd_atlas.png");
            Check(errors, ref checks, mat != null && mat.shader != null && mat.shader.name == "KOM/CrowdBW",
                "Falta Resources/Crowd/CrowdMat.mat con shader KOM/CrowdBW.");
            if (layoutAsset == null || manifestAsset == null || atlasTex == null || mat == null)
            {
                report.AppendLine("VERDICT: FAIL (faltan recursos)");
                return report.ToString();
            }

            KomStageCrowd.Layout layout = null;
            KomStageCrowd.AtlasManifest manifest = null;
            try
            {
                layout = JsonUtility.FromJson<KomStageCrowd.Layout>(layoutAsset.text);
                manifest = JsonUtility.FromJson<KomStageCrowd.AtlasManifest>(manifestAsset.text);
            }
            catch (Exception e)
            {
                errors.Add("JSON de publico invalido: " + e.Message);
                return report.ToString();
            }
            if (layout == null || layout.seats == null || layout.seats.Length == 0)
            {
                errors.Add("El layout de publico esta vacio.");
                return report.ToString();
            }
            if (manifest == null || manifest.avatars == null || manifest.avatars.Length == 0)
            {
                errors.Add("El manifiesto del atlas esta vacio.");
                return report.ToString();
            }

            Check(errors, ref checks, layout.stageIds != null && Array.IndexOf(layout.stageIds, "zocalo") >= 0,
                "El layout no declara el escenario zocalo.");
            Check(errors, ref checks, layout.stageIds != null && Array.IndexOf(layout.stageIds, "zocalo_final") >= 0,
                "El layout no declara el escenario zocalo_final.");
            Check(errors, ref checks, manifest.frameCount == 2,
                $"El atlas debe tener 2 fotogramas por ciclo (pareja aplauso/celebracion) y declara {manifest.frameCount}.");
            // 27 personajes x 2 variantes. Se exige el par completo por personaje y un
            // minimo de variedad, no un numero exacto: anadir o retirar una hoja no debe
            // romper el gate por aritmetica, pero un atlas a medias si tiene que romperlo.
            Check(errors, ref checks, manifest.avatars.Length >= 40 && manifest.avatars.Length % 2 == 0,
                $"El atlas debe traer dos variantes por personaje y trae {manifest.avatars.Length} avatares.");
            Check(errors, ref checks, manifest.fps > 0.1f && manifest.fps <= 12f,
                $"El manifiesto declara un ritmo base invalido ({manifest.fps} fps).");
            var charIds = new HashSet<string>();
            foreach (var av in manifest.avatars) if (!string.IsNullOrEmpty(av.charId)) charIds.Add(av.charId);
            int twins = 0;
            foreach (var id in charIds)
            {
                int claps = 0, cheers = 0;
                foreach (var av in manifest.avatars)
                {
                    if (av.charId != id) continue;
                    if (av.variant == "cheer") cheers++; else claps++;
                }
                if (claps == 1 && cheers == 1) twins++;
            }
            Check(errors, ref checks, twins == charIds.Count,
                $"Solo {twins} de {charIds.Count} personajes tienen sus dos variantes (aplauso y celebracion).");
            Check(errors, ref checks, atlasTex.width == manifest.atlasWidth && atlasTex.height == manifest.atlasHeight,
                $"El atlas importado ({atlasTex.width}x{atlasTex.height}) no coincide con el manifiesto " +
                $"({manifest.atlasWidth}x{manifest.atlasHeight}).");
            Check(errors, ref checks, atlasTex.width % 4 == 0 && atlasTex.height % 4 == 0,
                $"El atlas ({atlasTex.width}x{atlasTex.height}) debe tener dimensiones multiplo de 4 para BC7.");
            Check(errors, ref checks, mat.GetFloat("_CrowdFrames") == manifest.frameCount,
                $"El material pide {mat.GetFloat("_CrowdFrames")} fotogramas por ciclo y el atlas declara {manifest.frameCount}.");

            int[] perBank = new int[Banks.Length];
            var positions = new HashSet<string>();
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var s in layout.seats)
            {
                int bi = Array.IndexOf(Banks, s.bank);
                Check(errors, ref checks, bi >= 0, $"Asiento con bancada desconocida '{s.bank}'.");
                if (bi >= 0) perBank[bi]++;
                Check(errors, ref checks, s.row >= 0 && s.row < 3, $"{s.bank}: fila fuera de rango ({s.row}).");
                Check(errors, ref checks, s.avatar >= 0 && s.avatar < manifest.avatars.Length,
                    $"{s.bank}: personaje {s.avatar} fuera del atlas.");
                Check(errors, ref checks, s.scale >= 0.85f && s.scale <= 1.10f,
                    $"{s.bank}: escala {s.scale:F2} fuera de rango.");
                Check(errors, ref checks, s.tint >= 0.70f && s.tint <= 1.05f,
                    $"{s.bank}: tinte {s.tint:F2} fuera de rango.");
                Check(errors, ref checks, s.phase >= 0f && s.phase <= 1f,
                    $"{s.bank}: fase {s.phase:F2} fuera de rango.");
                // Distancia a la plataforma de pelea y huella del recinto.
                float outside = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
                Check(errors, ref checks, outside > 7.8f,
                    $"{s.bank}: asiento dentro de la arena (x={s.x:F2}, z={s.z:F2}).");
                Check(errors, ref checks, outside < 12.0f,
                    $"{s.bank}: asiento fuera del recinto (x={s.x:F2}, z={s.z:F2}).");
                string key = $"{s.x:F3}|{s.y:F3}|{s.z:F3}";
                Check(errors, ref checks, positions.Add(key), $"{s.bank}: asiento duplicado en {key}.");
                minX = Mathf.Min(minX, s.x); maxX = Mathf.Max(maxX, s.x);
                minZ = Mathf.Min(minZ, s.z); maxZ = Mathf.Max(maxZ, s.z);
            }
            for (int i = 0; i < perBank.Length; i++)
            {
                Check(errors, ref checks, perBank[i] >= 40, $"{Banks[i]}: solo {perBank[i]} asientos (minimo 40).");
                report.AppendLine($"  {Banks[i]}: {perBank[i]} asientos");
            }
            foreach (var av in manifest.avatars)
            {
                Check(errors, ref checks, av.worldHeight >= 1.5f && av.worldHeight <= 2.3f,
                    $"{av.name}: altura de mundo {av.worldHeight:F3} fuera de rango.");
                Check(errors, ref checks, av.cellU > 0f && av.cellV > 0f,
                    $"{av.name}: celda del atlas invalida.");
                Check(errors, ref checks, av.cycleScale >= 0.30f && av.cycleScale <= 1.01f,
                    $"{av.name}: ritmo de ciclo {av.cycleScale:F2} fuera de rango (0.30-1.00).");
                // Un avatar con las dos celdas iguales es un espectador de carton: es
                // justo el fallo que se reporto ("a mucha gente le falta animacion").
                Check(errors, ref checks, av.motion >= KomStageCrowd.MinMotion,
                    $"{av.name}: pareja sin movimiento ({av.motion:F3}); se veria congelado.");
                // Y una pareja que cambia de estatura hace que el cuerpo pulse en cada
                // ciclo ("se hace pequena y luego grande"). El horno ya no las empareja;
                // esto caza un manifiesto viejo o editado a mano.
                Check(errors, ref checks,
                    Mathf.Abs(av.stature0 - av.stature1) <= KomStageCrowd.MaxStatureDeltaPx,
                    $"{av.name}: la pareja cambia de estatura ({av.stature0} vs {av.stature1} px); " +
                    $"el cuerpo pulsaria al alternar los dos fotogramas.");
            }
            report.AppendLine($"  atlas: {manifest.avatars.Length} avatares (aplauso+celebracion) x " +
                              $"{manifest.frameCount} fotogramas ({atlasTex.width}x{atlasTex.height})");
            report.AppendLine($"  huella x[{minX:F2},{maxX:F2}] z[{minZ:F2},{maxZ:F2}] total={layout.seats.Length}");

            // Separacion dentro de cada fila (no entre filas: ahi el escalon ya separa).
            var rows = new Dictionary<string, List<KomStageCrowd.Seat>>();
            foreach (var s in layout.seats)
            {
                string key = s.bank + "/" + s.row;
                if (!rows.ContainsKey(key)) rows[key] = new List<KomStageCrowd.Seat>();
                rows[key].Add(s);
            }
            foreach (var kv in rows)
            {
                var list = kv.Value;
                float minGap = float.MaxValue;
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        float dx = list[i].x - list[j].x;
                        float dz = list[i].z - list[j].z;
                        minGap = Mathf.Min(minGap, Mathf.Sqrt(dx * dx + dz * dz));
                    }
                Check(errors, ref checks, minGap >= 0.45f,
                    $"{kv.Key}: separacion minima {minGap:F3} m (minimo 0.45).");
            }

            // Gate geometrico: cada pie sobre el escalon, medido de nuevo en escena.
            string previousScene = EditorSceneManager.GetActiveScene().path;
            try
            {
                EditorSceneManager.OpenScene($"{SceneDir}/Combat.unity", OpenSceneMode.Single);
                var controller = UnityEngine.Object.FindAnyObjectByType<CombatController>();
                if (controller == null) throw new InvalidOperationException("Falta CombatController.");
                controller.BuildEditorPreview("juan_grabriel", "zocalo");
                var stageRoot = FindStageRoot();
                var colliders = AddProbeColliders(stageRoot);
                Physics.SyncTransforms();
                int offGround = 0;
                float worst = 0f;
                foreach (var s in layout.seats)
                {
                    float y = RayFloor(s.x, s.z);
                    float err = float.IsNaN(y) ? 99f : Mathf.Abs(y - s.y);
                    if (err > MaxSeatHeightError) offGround++;
                    worst = Mathf.Max(worst, err);
                }
                Check(errors, ref checks, offGround == 0,
                    $"{offGround} asientos no apoyan en el escalon (peor error {worst:F3} m).");
                report.AppendLine($"  apoyo: peor error {worst:F3} m, fuera={offGround}");

                // Visibilidad: cuantos asientos caen dentro del frustum de pelea y del de intro.
                var cam = FindCombatCamera();
                var rig = UnityEngine.Object.FindAnyObjectByType<CombatCameraRig>();

                // Orientacion: se comprueba contra la camara REAL, ya montada y encuadrada.
                report.Append(CheckOrientation(layout, manifest, cam, errors, ref checks));

                int fightVisible = CountVisible(layout, cam);
                report.AppendLine($"  visibles en pelea: {fightVisible}");
                Check(errors, ref checks, fightVisible >= 40,
                    $"Solo {fightVisible} asientos visibles en el encuadre de pelea (minimo 40).");
                if (rig != null)
                {
                    rig.DebugSetIntro(2.3f, 0f);
                    int introVisible = CountVisible(layout, cam);
                    report.AppendLine($"  visibles en intro: {introVisible}");
                    Check(errors, ref checks, introVisible >= 100,
                        $"Solo {introVisible} asientos visibles en la panoramica (minimo 100).");
                    rig.Snap();
                }
                RemoveProbeColliders(colliders);

                if (capture)
                {
                    report.Append(CaptureCompare(controller, rig, cam, errors, ref checks));
                }
            }
            catch (Exception e)
            {
                errors.Add("Gate del publico fallo: " + e.Message);
                report.AppendLine("  ERROR: " + e.Message);
            }
            finally
            {
                if (!string.IsNullOrEmpty(previousScene))
                    EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
            }

            File.WriteAllText($"{OutDir}/report.txt", report.ToString());
            return report.ToString();
        }

        /// <summary>
        /// Gate de orientacion. Es el motivo de todo este horno: comprueba asiento a asiento,
        /// contra la camara real, que nadie lleva un sprite girado donde la camara lo ve de
        /// cara. No se fia del nombre del archivo (el paquete rotula `SideView` personajes
        /// que estan de frente); se fia del angulo.
        /// </summary>
        static string CheckOrientation(KomStageCrowd.Layout layout, KomStageCrowd.AtlasManifest manifest,
            Camera cam, List<string> errors, ref int checks)
        {
            var report = new StringBuilder();
            if (cam == null)
            {
                errors.Add("No hay camara para validar la orientacion del publico.");
                return report.ToString();
            }
            var camPos = cam.transform.position;
            var introPos = new Vector3(camPos.x, camPos.y * IntroZoom, camPos.z * IntroZoom);
            int rearLaterals = 0, flankFrontals = 0, badMirror = 0, badClass = 0;
            int fronts = 0, quarters = 0, profiles = 0, cheers = 0;
            float worstFrontYaw = 0f;
            var badExamples = new List<string>();
            foreach (var s in layout.seats)
            {
                var av = manifest.avatars[Mathf.Clamp(s.avatar, 0, manifest.avatars.Length - 1)];
                bool frontal = av.orientation == KomStageCrowd.OrientationFront;
                if (frontal) fronts++;
                else if (av.orientation == KomStageCrowd.OrientationQuarter) quarters++;
                else profiles++;
                if (av.variant == "cheer") cheers++;

                var pos = new Vector3(s.x, s.y, s.z);
                float yaw = Mathf.Lerp(ApparentYaw(pos, camPos), ApparentYaw(pos, introPos), IntroWeight);

                if (s.bank == "rear")
                {
                    if (!frontal) rearLaterals++;
                    worstFrontYaw = Mathf.Max(worstFrontYaw, yaw);
                }
                else
                {
                    if (frontal) flankFrontals++;
                    // Un lateral mal espejado mira hacia fuera de la arena.
                    int facing = av.facing == 0 ? 1 : av.facing;
                    bool wantFlip = (s.bank == "right") ^ (facing < 0);
                    if (!frontal && s.flip != wantFlip) badMirror++;
                }

                // Coherencia fina: donde la camara ve de frente no puede haber 3/4, y donde
                // ve de perfil no puede haber un cuerpo cuadrado.
                bool wrong = (yaw < FrontMaxYaw - 4f && !frontal) ||
                             (yaw > QuarterMaxYaw + 6f && av.orientation != KomStageCrowd.OrientationProfile);
                if (wrong)
                {
                    badClass++;
                    if (badExamples.Count < 5)
                        badExamples.Add($"{s.bank}/f{s.row} ({s.x:F1},{s.z:F1}) yaw={yaw:F1} " +
                                        $"arte={av.orientation} {av.name}");
                }
            }
            Check(errors, ref checks, rearLaterals == 0,
                $"{rearLaterals} asientos de la bancada frontal llevan arte de 3/4 o perfil " +
                $"(la camara los ve a {worstFrontYaw:F1} grados como mucho).");
            Check(errors, ref checks, badMirror == 0,
                $"{badMirror} sprites laterales miran hacia fuera de la arena.");
            Check(errors, ref checks, badClass == 0,
                $"{badClass} asientos llevan una orientacion que no toca por su angulo aparente: " +
                string.Join(" | ", badExamples));
            // El reparto de clases sale de la geometria, no de un cupo: la bancada trasera
            // se ve de cara (0-19 grados) y los flancos se abren de 18 a 56. Lo que se exige
            // es que las tres familias se usen y que el 3/4 domine los flancos.
            Check(errors, ref checks, flankFrontals < 100,
                $"{flankFrontals} asientos de flanco con arte de frente: los flancos se leen " +
                $"demasiado cuadrados.");
            Check(errors, ref checks, quarters >= 90,
                $"Solo {quarters} asientos con arte de 3/4: los flancos tienen que leerse girados.");
            // El sprite es un billboard: siempre mira a la camara, asi que el angulo del
            // ARTE tiene que ser el de la camara de pelea. Esa camara nunca pasa de 46
            // grados, luego no hay asiento donde un cuerpo de perfil sea correcto.
            Check(errors, ref checks, profiles == 0,
                $"{profiles} asientos con arte de perfil: la camara de pelea no pasa de 46 " +
                $"grados, asi que ahi un cuerpo de lado se lee como alguien girado.");
            Check(errors, ref checks, cheers >= 30 && cheers <= 80,
                $"{cheers} asientos con la pareja de celebracion (se esperan 30-80 de 216).");
            report.AppendLine($"  orientacion: frente={fronts} 3/4={quarters} perfil={profiles} " +
                              $"celebran={cheers} (frente, peor angulo {worstFrontYaw:F1} grados)");
            return report.ToString();
        }

        static int CountVisible(KomStageCrowd.Layout layout, Camera cam)
        {
            if (cam == null) return 0;
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            int n = 0;
            foreach (var s in layout.seats)
            {
                var b = new Bounds(new Vector3(s.x, s.y + 1f, s.z), new Vector3(0.8f, 2.2f, 0.4f));
                if (GeometryUtility.TestPlanesAABB(planes, b)) n++;
            }
            return n;
        }

        static string CaptureCompare(CombatController controller, CombatCameraRig rig, Camera cam,
            List<string> errors, ref int checks)
        {
            var report = new StringBuilder();
            cam.ResetAspect();
            cam.aspect = 1600f / 900f;
            // Vuelve al encuadre de pelea: la sonda de intro deja la camara abierta.
            if (rig != null) rig.DebugSetIntro(1f, 1f);
            PoseIdle();

            var crowd = KomStageCrowd.Instance;
            if (crowd == null || crowd.CrowdRenderer == null)
            {
                errors.Add("No hay instancia de publico para la captura.");
                report.AppendLine("  captura: no hay instancia de publico.");
                return report.ToString();
            }

            float fightPct = DiffPair(cam, crowd.CrowdRenderer, "fight", 1600, 900);
            report.AppendLine($"  pelea: pixeles cambiados {fightPct:F2}% (umbral 0.30%)");
            Check(errors, ref checks, fightPct >= 0.30f,
                $"El publico solo cambia el {fightPct:F2}% del encuadre de pelea (minimo 0.30%).");

            if (rig != null)
            {
                rig.DebugSetIntro(2.3f, 0f);
                float introPct = DiffPair(cam, crowd.CrowdRenderer, "intro", 1600, 900);
                report.AppendLine($"  intro: pixeles cambiados {introPct:F2}% (umbral 0.50%)");
                Check(errors, ref checks, introPct >= 0.50f,
                    $"El publico solo cambia el {introPct:F2}% de la panoramica (minimo 0.50%).");
                rig.Snap();
            }
            return report.ToString();
        }

        static float DiffPair(Camera cam, Renderer crowd, string tag, int w, int h)
        {
            var with = RenderToTexture(cam, w, h);
            crowd.enabled = false;
            var without = RenderToTexture(cam, w, h);
            crowd.enabled = true;

            SavePng(with, $"{OutDir}/{tag}_with.png");
            SavePng(without, $"{OutDir}/{tag}_without.png");
            int changed = 0;
            var a = with.GetPixels32();
            var b = without.GetPixels32();
            for (int i = 0; i < a.Length; i++)
            {
                int d = Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
                if (d > 24) changed++;
            }
            float pct = changed * 100f / a.Length;
            UnityEngine.Object.DestroyImmediate(with);
            UnityEngine.Object.DestroyImmediate(without);
            return pct;
        }

        static Texture2D RenderToTexture(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 4
            };
            rt.Create();
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = prev;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            return tex;
        }

        static void SavePng(Texture2D tex, string path)
        {
            File.WriteAllBytes(path, tex.EncodeToPNG());
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

        static void Check(List<string> errors, ref int checks, bool condition, string message)
        {
            checks++;
            if (!condition) errors.Add(message);
        }

        // ---------------------------------------------------------------- peritaje

        public static string ProbeZocalo()
        {
            Directory.CreateDirectory(OutDir);
            string previousScene = EditorSceneManager.GetActiveScene().path;
            EditorSceneManager.OpenScene($"{SceneDir}/Combat.unity", OpenSceneMode.Single);
            var controller = UnityEngine.Object.FindAnyObjectByType<CombatController>();
            if (controller == null) return "falta CombatController";
            controller.BuildEditorPreview("juan_grabriel", "zocalo");

            var report = new StringBuilder();
            report.AppendLine("KOM PUBLICO · peritaje de gradas del Zocalo");
            var stageRoot = FindStageRoot();
            if (stageRoot == null) return "no se encontro el prefab zocalo(Clone)";
            var bounds = new Bounds();
            bool first = true;
            foreach (var r in stageRoot.GetComponentsInChildren<MeshRenderer>())
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            report.AppendLine($"root={stageRoot.name} pos={stageRoot.transform.position} escala={stageRoot.transform.localScale.x:F2}");
            report.AppendLine($"bounds x[{bounds.min.x:F1},{bounds.max.x:F1}] y[{bounds.min.y:F2},{bounds.max.y:F1}] z[{bounds.min.z:F1},{bounds.max.z:F1}]");
            var cam = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (cam != null) report.AppendLine($"camara pelea pos={cam.transform.position} fov={cam.fieldOfView:F1}");

            var colliders = AddProbeColliders(stageRoot);
            Physics.SyncTransforms();
            report.AppendLine("\nMAPA DE ALTURAS (y del suelo; . = sin suelo; filas z de 30 a -20)");
            for (float z = 30f; z >= -20f; z -= 1f)
            {
                var row = new StringBuilder();
                row.Append($"z={z,4:F0} ");
                for (float x = -30f; x <= 30f; x += 1f)
                {
                    float y = RayFloor(x, z);
                    row.Append(float.IsNaN(y) ? '.' : HeightChar(y));
                }
                report.AppendLine(row.ToString());
            }
            foreach (var bank in Banks)
            {
                int bi = Array.IndexOf(Banks, bank);
                var treads = MeasureTreads(!BankAlongX[bi], BankDepthSign[bi]);
                report.AppendLine($"\n{bank}:");
                foreach (var t in treads)
                    report.AppendLine($"  y={t.y:F3} fondo={t.depthCenter:F2} tramo=[{t.seatLo:F2},{t.seatHi:F2}]");
            }
            RemoveProbeColliders(colliders);

            var topGo = new GameObject("CrowdTopCam", typeof(Camera));
            var topCam = topGo.GetComponent<Camera>();
            topCam.clearFlags = CameraClearFlags.SolidColor;
            topCam.backgroundColor = Color.black;
            topCam.orthographic = true;
            topCam.orthographicSize = 34f;
            topCam.transform.position = new Vector3(0f, 60f, 4f);
            topCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            topCam.nearClipPlane = 1f;
            topCam.farClipPlane = 120f;
            var topTex = RenderToTexture(topCam, 1200, 1200);
            SavePng(topTex, $"{OutDir}/zocalo_top.png");
            UnityEngine.Object.DestroyImmediate(topTex);
            UnityEngine.Object.DestroyImmediate(topGo);

            File.WriteAllText($"{OutDir}/probe.txt", report.ToString());
            Debug.Log($"[KOM][CROWD] {OutDir}/probe.txt");
            if (!string.IsNullOrEmpty(previousScene))
                EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
            return report.ToString();
        }

        static char HeightChar(float y)
        {
            if (y < -0.05f) return '-';
            if (y < 0.35f) return '0';
            if (y < 0.75f) return '1';
            if (y < 1.15f) return '2';
            if (y < 1.55f) return '3';
            if (y < 1.95f) return '4';
            if (y < 2.35f) return '5';
            if (y < 2.75f) return '6';
            if (y < 3.15f) return '7';
            if (y < 3.55f) return '8';
            return '9';
        }

        static GameObject FindStageRoot()
        {
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>())
                if (t.parent == null && t.name.StartsWith("zocalo")) return t.gameObject;
            return null;
        }

        /// <summary>Camara del combate (la del rig). Evita coger una camara de UI o de escena.</summary>
        static Camera FindCombatCamera()
        {
            var rig = UnityEngine.Object.FindAnyObjectByType<CombatCameraRig>();
            if (rig != null)
            {
                var onRig = rig.GetComponent<Camera>();
                if (onRig != null) return onRig;
            }
            return Camera.main != null ? Camera.main : UnityEngine.Object.FindAnyObjectByType<Camera>();
        }

        static float RayFloor(float x, float z)
        {
            return Physics.Raycast(new Vector3(x, 9f, z), Vector3.down, out var hit, 20f,
                ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : float.NaN;
        }

        static List<MeshCollider> AddProbeColliders(GameObject stageRoot)
        {
            var added = new List<MeshCollider>();
            foreach (var mf in stageRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                added.Add(mc);
            }
            return added;
        }

        static void RemoveProbeColliders(List<MeshCollider> colliders)
        {
            foreach (var mc in colliders) UnityEngine.Object.DestroyImmediate(mc);
        }
    }
}
