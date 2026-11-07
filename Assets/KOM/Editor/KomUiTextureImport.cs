using UnityEditor;
using UnityEngine;

namespace KOM.EditorTools
{
    /// <summary>
    /// Las ilustraciones, cards, mapa y portada se importaban con maxTextureSize 1024;
    /// en el build se veian blandas al escalarlas a 1920. Se suben a 2048 y se reimportan
    /// una sola vez. Menu para poder repetirlo tras cambiar un asset de UI.
    /// </summary>
    public static class KomUiTextureImport
    {
        [MenuItem("King of Music/Setup/10 - Texturas UI a 2048", priority = 245)]
        public static void MenuFix() => Debug.Log(Fix());

        public static string Fix()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/UI" });
            int changed = 0;
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                if (importer.maxTextureSize >= 2048) continue;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
                changed++;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return $"[KOM][UI] texturas UI a 2048: {changed} de {guids.Length} reimportadas.";
        }
    }
}
