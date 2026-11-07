using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KOM.EditorTools
{
    // Prepara los props de los especiales: texturas PBR del .blend original, materiales
    // URP y prefabs en Resources. Los props NO se animan con el FBX: se anclan por codigo
    // a las manos (ver KomPropGrip), asi que el prefab solo tiene que ser rigido.
    public static class KomPropSetup
    {
        const string PropDir = "Assets/Art/Props";
        const string TexDir = PropDir + "/Textures";
        const string MatDir = PropDir + "/Materials";
        const string PrefabDir = "Assets/Resources/Props";
        const string ShotDir = "Logs/props";

        [MenuItem("King of Music/Setup/7 - Props de especiales", priority = 15)]
        public static void SetupPropsMenu()
        {
            SetupProps();
            EditorUtility.DisplayDialog("King of Music", "Props de especiales generados.", "OK");
        }

        public static void SetupProps()
        {
            EnsureFolder(MatDir);
            EnsureFolder(PrefabDir);
            ConfigureTextures();

            var guitar = BuildMaterial("guitarra",
                "Guitar_baseColor", "Guitar_normal", "Guitar_MetallicGloss", 1f, 0.30f);
            var caseMat = BuildMaterial("acordeon_case",
                "case_baseColor", "case_normal", "case_MetallicGloss", 1f, 0.42f);
            var keys = BuildMaterial("acordeon_teclas",
                "Material_001_baseColor", "Material_001_normal_scale0_norm", "Material_001_MetallicGloss", 1f, 0.50f);

            BuildPropPrefab("guitarra", new[] { guitar });
            BuildPropPrefab("acordeon", new[] { caseMat, keys });

            // Microfono (Jose Jose): cabeza de rejilla + yugo + conector, recortado del
            // pie del .blend por tools/blender/extract_mic.py. El pivote del FBX esta en
            // la base del conector, que es donde se empuna.
            var micGrid = BuildMaterial("microfono_rejilla",
                "microfono_baseColor", null, "microfono_MetallicGloss", 1f, 0.55f);
            var micBody = BuildPlainMaterial("microfono_cuerpo", new Color(0.10f, 0.10f, 0.11f), 0.85f, 0.35f);
            var micDetail = BuildPlainMaterial("microfono_detalle", new Color(0.55f, 0.55f, 0.58f), 0.95f, 0.60f);
            BuildPropPrefab("microfono", new[] { micBody, micDetail, micGrid });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[KOM] Props listos en " + PrefabDir);
        }

        // Material sin texturas: color plano + metallic/smoothness (piezas del prop que
        // en el .blend no tenian imagen asociada).
        static Material BuildPlainMaterial(string name, Color color, float metallic, float smoothness)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Shader.Find("Universal Render Pipeline/Lit");
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Surface", 0f);
            mat.SetFloat("_Cull", 2f);
            mat.renderQueue = -1;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void ConfigureTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                bool normal = path.Contains("_normal");
                bool data = normal || path.Contains("MetallicGloss") || path.Contains("metallicRoughness");
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !data;
                importer.maxTextureSize = 1024;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }

        static Material BuildMaterial(string name, string baseTex, string normalTex, string glossTex,
            float metallic, float smoothness)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Shader.Find("Universal Render Pipeline/Lit");
            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{baseTex}.png");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{normalTex}.png");
            var gloss = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{glossTex}.png");
            if (baseMap != null) mat.SetTexture("_BaseMap", baseMap);
            mat.SetColor("_BaseColor", Color.white);
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (gloss != null)
            {
                mat.SetTexture("_MetallicGlossMap", gloss);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                // Smoothness desde el alfa del mapa (URP: canal Metallic Alpha).
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
                mat.SetFloat("_Smoothness", 1f);
                mat.SetFloat("_Metallic", 1f);
            }
            else
            {
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Smoothness", smoothness);
            }
            mat.SetFloat("_Surface", 0f);
            mat.SetFloat("_Cull", 2f);
            mat.renderQueue = -1;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void BuildPropPrefab(string name, Material[] materials)
        {
            string fbxPath = $"{PropDir}/{name}.fbx";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (source == null) { Debug.LogError("[KOM] Falta " + fbxPath); return; }
            var go = UnityEngine.Object.Instantiate(source);
            go.name = name;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = materials[Mathf.Min(i, materials.Length - 1)];
                renderer.sharedMaterials = mats;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            string path = $"{PrefabDir}/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            Debug.Log("[KOM] Prefab de prop: " + path);
        }

        // La captura de props vive en KomPropLab (escena aditiva + trabajo asincrono):
        // Camera.Render sobre una preview scene no dibuja NADA (medido) y dentro de un
        // mismo eval el render va un frame por detras.

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
