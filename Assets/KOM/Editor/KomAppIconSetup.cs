using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace KOM.EditorTools
{
    /// <summary>
    /// Aplica el sello del juego como icono del ejecutable de Windows. El mismo PNG se
    /// usa para el icono del instalador y las imágenes del asistente.
    /// </summary>
    public static class KomAppIconSetup
    {
        const string IconPath = "Assets/Resources/UI/Logo/kom_icon.png";

        [MenuItem("King of Music/Setup/11 - Icono del juego", priority = 246)]
        public static void MenuApply() => Debug.Log(Apply());

        public static string Apply()
        {
            var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer == null) return $"[KOM][UI] falta {IconPath}";
            importer.textureType = TextureImporterType.Default;
            importer.maxTextureSize = 256;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null) return "[KOM][UI] no se pudo cargar el icono.";
            var sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Application);
            var icons = new Texture2D[sizes.Length];
            for (int i = 0; i < icons.Length; i++) icons[i] = icon;
            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, icons, IconKind.Application);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return "[KOM][UI] icono de Windows aplicado: " + IconPath;
        }
    }
}
