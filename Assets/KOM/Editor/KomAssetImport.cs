using UnityEditor;
using UnityEngine;

namespace KOM.EditorTools
{
    /// <summary>
    /// Ajustes de importacion de los assets de audio y de las tarjetas.
    ///
    /// Existe porque estos dos grupos tienen una regla que el importador por defecto
    /// rompe en silencio:
    ///
    ///  · AUDIO — los WAV se preparan con `tools/prepare_audio.py`, que ya deja cada
    ///    capa a su nivel de mezcla (musica -20 dBFS RMS, grada -24, voces -18). El
    ///    importador de Unity normaliza el PICO por defecto, y como cada pista tiene
    ///    un factor de cresta distinto eso deshace justo la nivelacion: una porra
    ///    quedaria 4 dB por encima de otra. Por eso `normalize = false` es
    ///    obligatorio, no una preferencia.
    ///  · TARJETAS — son ilustraciones a color de 1086x1448. Se importan como sprite
    ///    sin mipmaps y con compresion de calidad alta: son la pieza que el jugador
    ///    mira mas tiempo en la seleccion.
    ///
    /// Menu: King of Music / Assets / Ajustar audio y tarjetas.
    /// </summary>
    public static class KomAssetImport
    {
        const string AudioRoot = "Assets/Resources/Audio";
        const string CardRoot = "Assets/Resources/UI/Cards";

        [MenuItem("King of Music/Assets/Ajustar audio y tarjetas")]
        public static void Apply()
        {
            int audio = ApplyAudio();
            int cards = ApplyCards();
            AssetDatabase.Refresh();
            Debug.Log($"[KOM] Import ajustado: {audio} audios, {cards} tarjetas.");
        }

        public static int ApplyAudio()
        {
            int changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) continue;

                bool streaming = IsBed(path);
                var settings = importer.defaultSampleSettings;
                settings.loadType = streaming ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = streaming ? 0.7f : 0.85f;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                importer.defaultSampleSettings = settings;
                importer.forceToMono = false;
                importer.loadInBackground = streaming;

                // `normalize` y `preloadAudioData` ya no son propiedades publicas del
                // AudioImporter en Unity 6 (viven en el serializado del importador, con
                // prefijo m_). Se escriben por SerializedObject: es la via estable entre
                // versiones, y el nombre real se comprueba en el propio serializado.
                var so = new SerializedObject(importer);
                SetBool(so, "m_Normalize", false);          // la nivelacion la trae el WAV
                SetBool(so, "m_PreloadAudioData", false);
                SetBool(so, "m_DefaultSettings.preloadAudioData", false);
                so.ApplyModifiedPropertiesWithoutUndo();

                importer.SaveAndReimport();
                changed++;
            }
            return changed;
        }

        static void SetBool(SerializedObject so, string name, bool value)
        {
            var prop = so.FindProperty(name);
            if (prop != null && prop.propertyType == SerializedPropertyType.Boolean) prop.boolValue = value;
        }

        // Camas y musica: pistas largas que se reproducen en streaming. Las voces
        // (porras y coreos) son cortas y se quedan comprimidas en memoria para que
        // el disparo no dependa de una lectura de disco a mitad de golpe.
        static bool IsBed(string path)
        {
            return path.Contains("/Music/") || path.Contains("/Ambience/");
        }

        public static int ApplyCards()
        {
            int changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { CardRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.sRGBTexture = true;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
                changed++;
            }
            return changed;
        }
    }
}
