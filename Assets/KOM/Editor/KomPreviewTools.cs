using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KOM.EditorTools
{
    // AAA verificacion lateral: la camara de combate SIEMPRE mira de lado (perfil puro).
    // En el angulo 3/4 actual los errores de mezcla no se ven; de lado se ve como en juego.
    public static class KomPreviewTools
    {
        const string SceneDir = "Assets/Scenes/KOM";

        // UNICA entrada valida para verificar animacion. Perfil puro, ortografico, con
        // estados reales del Animator. La vista 3/4 no detecta errores de mezcla.
        [MenuItem("King of Music/Preview/5 Animacion LATERAL (perfil puro)", priority = 54)]
        static void PreviewAnimSide()
        {
            KomAnimSheet.MenuCaptureDefault();
        }

        [MenuItem("King of Music/Preview/1 Menu", priority = 50)]
        static void PreviewMenu()
        {
            Open<MainMenuController>("MainMenu", c => c.BuildEditorPreview());
        }

        // El panel de ajustes se abre con AJUSTES en el juego; la preview necesita una
        // entrada propia para poder revisarlo (filas, descripciones, fila deshabilitada).
        [MenuItem("King of Music/Preview/1b Menu AJUSTES", priority = 50)]
        static void PreviewMenuSettings()
        {
            Open<MainMenuController>("MainMenu", c => c.BuildEditorPreviewSettings());
        }

        [MenuItem("King of Music/Preview/1c Menu CONTROLES", priority = 50)]
        static void PreviewMenuControls()
        {
            Open<MainMenuController>("MainMenu", c => c.BuildEditorPreviewControls());
        }

        [MenuItem("King of Music/Preview/2 Seleccion", priority = 51)]
        static void PreviewSelect()
        {
            Open<CharacterSelectController>("CharacterSelect", c => c.BuildEditorPreview());
        }

        [MenuItem("King of Music/Preview/2b Seleccion REVELACION", priority = 56)]
        static void PreviewSelectReveal()
        {
            Open<CharacterSelectController>("CharacterSelect", c => c.BuildRevealPreview());
        }

        [MenuItem("King of Music/Preview/2c Seleccion VERSUS", priority = 57)]
        static void PreviewSelectVersus()
        {
            Open<CharacterSelectController>("CharacterSelect", c => c.BuildVersusPreview());
        }

        [MenuItem("King of Music/Preview/2d Seleccion DOS JUGADORES", priority = 58)]
        static void PreviewSelectVersusLocal()
        {
            Open<CharacterSelectController>("CharacterSelect", c => c.BuildVersusSelectPreview());
        }

        [MenuItem("King of Music/Preview/3 Mapa", priority = 52)]
        static void PreviewMap()
        {
            Open<MapController>("Map", c => c.BuildEditorPreview("juan_grabriel"));
        }

        [MenuItem("King of Music/Preview/3b Mapa ESCENARIO versus", priority = 59)]
        static void PreviewMapVersus()
        {
            Open<MapController>("Map", c => c.BuildVersusEditorPreview());
        }

        [MenuItem("King of Music/Preview/6 Final", priority = 55)]
        static void PreviewEnding()
        {
            Open<EndingController>("Ending", c => c.BuildEditorPreview("juan_grabriel"));
        }

        [MenuItem("King of Music/Preview/4 Combate (Madrugadora)", priority = 53)]
        static void PreviewCombat()
        {
            Open<CombatController>("Combat", c => c.BuildEditorPreview("juan_grabriel"));
        }

        static void Open<T>(string sceneName, Action<T> build) where T : Component
        {
            string sourcePath = $"{SceneDir}/{sceneName}.unity";

            // La preview se construye sobre una COPIA de la escena (`_Preview_<nombre>`).
            // La escena real no se abre nunca, asi que es IMPOSIBLE que quede guardada con
            // el diorama, el canvas y los rigs dentro: ese era el origen de los personajes
            // y los escenarios duplicados al entrar en Play.
            string tempPath = $"{SceneDir}/_Preview_{sceneName}.unity";
            var active = EditorSceneManager.GetActiveScene();
            if (active.path != tempPath)
            {
                if (System.IO.File.Exists(tempPath)) AssetDatabase.DeleteAsset(tempPath);
                if (!AssetDatabase.CopyAsset(sourcePath, tempPath))
                {
                    Debug.LogError($"[KOM] No se pudo copiar {sourcePath} a {tempPath}");
                    return;
                }
                EditorSceneManager.OpenScene(tempPath, OpenSceneMode.Single);
            }

            var controller = UnityEngine.Object.FindAnyObjectByType<T>();
            if (controller == null)
            {
                Debug.LogError($"[KOM] No se encontro {typeof(T).Name} en {sourcePath}");
                return;
            }

            // La copia puede traer la preview anterior: se deja solo el bootstrap.
            CleanScene(controller.gameObject);
            build(controller);
            PrepareForCameraCapture();
            Selection.activeGameObject = controller.gameObject;

            // Se guarda la COPIA: la preview queda limpia (sin cambios pendientes) y Unity
            // no pregunta "guardar?" al pasar a otra pantalla.
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), tempPath);
            Debug.Log($"[KOM] Preview de {sceneName} construida en {tempPath}. Captura la Game View (sin Play).");
        }

        /// <summary>
        /// Repara una escena que se guardo con una preview dentro: deja solo el bootstrap.
        /// Es la red de seguridad si alguien pulsa Ctrl+S mientras mira una preview.
        /// </summary>
        [MenuItem("King of Music/Preview/0 Limpiar escena", priority = 49)]
        static void CleanActiveScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                Debug.LogWarning("[KOM] La escena activa no esta guardada en disco.");
                return;
            }

            var roots = scene.GetRootGameObjects();
            GameObject keep = null;
            foreach (var root in roots)
            {
                if (IsBootstrap(root)) { keep = root; break; }
            }
            if (keep == null)
            {
                Debug.LogWarning("[KOM] No hay bootstrap de KOM en la escena activa.");
                return;
            }

            int removed = 0;
            foreach (var root in roots)
            {
                if (root == keep) continue;
                UnityEngine.Object.DestroyImmediate(root);
                removed++;
            }
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[KOM] Escena limpia: {removed} objetos fuera, solo queda {keep.name}.");
        }

        static bool IsBootstrap(GameObject go)
        {
            return go.GetComponent<MainMenuController>() != null
                || go.GetComponent<CharacterSelectController>() != null
                || go.GetComponent<MapController>() != null
                || go.GetComponent<CombatController>() != null
                || go.GetComponent<EndingController>() != null
                || go.GetComponent<StoryController>() != null;
        }

        /// <summary>Deja en la escena abierta solo el objeto indicado.</summary>
        public static void CleanScene(GameObject keep)
        {
            if (keep == null) return;
            foreach (var root in keep.scene.GetRootGameObjects())
            {
                if (root == keep) continue;
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void PrepareForCameraCapture()
        {
            var camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (camera == null) return;
            camera.cullingMask = ~0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>())
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 0.9f;
            }
        }
    }
}
