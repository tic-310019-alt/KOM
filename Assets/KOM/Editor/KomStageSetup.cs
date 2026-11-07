using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KOM.EditorTools
{
    // Construye los prefabs de los ESCENARIOS REALES.
    //
    // El mundo va en BLANCO Y NEGRO: cada material del FBX se reescribe con `KOM/StageBW`,
    // que conserva la textura (la estructura: mosaico, letreros, veta) y la colapsa a
    // luminancia. Asi el escenario se lee grafico y son los personajes, sus VFX y el HUD
    // los que aportan el color.
    //
    // El prefab queda en el origen: el encuadre (giro, desplazamiento y escala) vive en
    // `stages.json` para poder mover la plataforma de pelea al centro sin reexportar.
    public static class KomStageSetup
    {
        const string FbxDir = "Assets/Art/Stages";
        const string PrefabDir = "Assets/Resources/Stages";
        const string MatDir = "Assets/Art/Stages/Materials";

        static readonly string[] Stages = { "la_mananera", "bellas_artes", "zocalo" };

        [MenuItem("King of Music/Setup/8 - Escenarios reales", priority = 16)]
        public static void SetupStagesMenu()
        {
            var report = SetupStages();
            Debug.Log(report);
            EditorUtility.DisplayDialog("King of Music", "Escenarios reales generados.", "OK");
        }

        public static string SetupStages()
        {
            EnsureFolder(PrefabDir);
            EnsureFolder(MatDir);
            var report = new System.Text.StringBuilder();
            foreach (var id in Stages)
            {
                string fbx = $"{FbxDir}/{id}.fbx";
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (source == null) { report.AppendLine("FALTA " + fbx); continue; }

                var go = Object.Instantiate(source);
                go.name = id;
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one;

                var texMap = LoadMaterialMap(id);
                int textured = 0, total = 0;
                var converted = new Dictionary<Material, Material>();
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var src = mats[i];
                        if (src == null) continue;
                        total++;
                        if (!converted.TryGetValue(src, out var bw))
                        {
                            bw = BuildBW(src, id, texMap, out bool hasTex);
                            converted[src] = bw;
                            if (hasTex) textured++;
                        }
                        mats[i] = bw;
                    }
                    r.sharedMaterials = mats;
                    // Las piezas grandes (paredes, techo) no proyectan: la luz es una sola
                    // direccional y proyectarian sombras falsas sobre la arena.
                    bool big = r.bounds.size.x > 8f || r.bounds.size.z > 8f;
                    r.shadowCastingMode = big ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    r.receiveShadows = true;
                }

                string path = $"{PrefabDir}/{id}.prefab";
                PrefabUtility.SaveAsPrefabAsset(go, path);
                Object.DestroyImmediate(go);
                report.AppendLine($"{id}: {total} materiales, {converted.Count} unicos, {textured} con textura -> {path}");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return report.ToString();
        }

        static Material BuildBW(Material src, string stageId, Dictionary<string, string> texMap, out bool hasTexture)
        {
            string dir = $"{MatDir}/{stageId}";
            EnsureFolder(dir);
            string path = $"{dir}/{Sanitize(src.name)}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("KOM/StageBW"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Shader.Find("KOM/StageBW");

            // La textura NO se toma del material importado del FBX: el exportador solo
            // se lleva la que esta enchufada al Base Color del Principled, y en estos
            // escenarios casi ninguna lo esta (medido: 1 de 112). El mapa que escribe
            // tools/blender/export_stage.py dice que imagen usa cada material.
            Texture baseMap = null;
            if (texMap != null && texMap.TryGetValue(src.name, out var fileName) && !string.IsNullOrEmpty(fileName))
                baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{FbxDir}/{stageId}/{fileName}");
            if (baseMap == null && src.HasProperty("_BaseMap")) baseMap = src.GetTexture("_BaseMap");
            if (baseMap == null && src.HasProperty("_MainTex")) baseMap = src.GetTexture("_MainTex");
            hasTexture = baseMap != null;
            if (baseMap != null) mat.SetTexture("_BaseMap", baseMap);

            Color tint = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.color;
            float lum = tint.r * 0.2126f + tint.g * 0.7152f + tint.b * 0.0722f;
            // El tinte se guarda en gris: la textura aporta la variacion, el tinte solo
            // corrige el valor base de la pieza.
            mat.SetColor("_BaseColor", new Color(lum, lum, lum, 1f));
            mat.SetFloat("_Contrast", 1.22f);
            mat.SetFloat("_BlackPoint", 0.05f);
            mat.SetFloat("_Ambient", 0.30f);
            mat.SetFloat("_Emission", 0f);
            // Recorte alfa: el emblema de la tarima es un plano con fondo transparente. Sin
            // esto el fondo se dibuja negro opaco y la plataforma se lee como un agujero.
            // Las texturas opacas traen alfa=1, asi que no se recortan.
            mat.SetFloat("_Cutoff", 0.5f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Dictionary<string, string> LoadMaterialMap(string stageId)
        {
            var map = new Dictionary<string, string>();
            string path = $"{FbxDir}/{stageId}/material_map.json";
            if (!File.Exists(path)) return map;
            var text = File.ReadAllText(path);
            // Formato plano { "Material": "archivo.png" | null }: se parsea a mano para no
            // depender de un serializador con tipos anidados.
            foreach (var line in text.Split((char)10))
            {
                int q1 = line.IndexOf('"');
                if (q1 < 0) continue;
                int q2 = line.IndexOf('"', q1 + 1);
                if (q2 < 0) continue;
                int colon = line.IndexOf(':', q2);
                if (colon < 0) continue;
                string key = line.Substring(q1 + 1, q2 - q1 - 1);
                string rest = line.Substring(colon + 1).Trim().TrimEnd(',');
                if (rest.StartsWith("null")) { map[key] = null; continue; }
                int q3 = rest.IndexOf('"');
                int q4 = rest.LastIndexOf('"');
                if (q3 >= 0 && q4 > q3) map[key] = rest.Substring(q3 + 1, q4 - q3 - 1);
            }
            return map;
        }

        static string Sanitize(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
