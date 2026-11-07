using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KOM.EditorTools
{
    // Escenario de render para verificar props y poses de especiales. Usa una escena
    // ADITIVA real: una preview scene no se renderiza con Camera.Render (se midio:
    // todo sale del color de fondo). La escena se cierra siempre al terminar.
    public static class KomPropStage
    {
        public const string ShotDir = "Logs/props";
        public const int TileW = 640;
        public const int TileH = 720;

        public sealed class Stage
        {
            public Scene scene;
            public Camera camera;
            public RenderTexture rt;
            public Scene previous;
            public readonly List<GameObject> owned = new List<GameObject>();
        }

        public static Stage Begin(string prefix)
        {
            Directory.CreateDirectory(ShotDir);
            var stage = new Stage();
            stage.previous = EditorSceneManager.GetActiveScene();
            stage.scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(stage.scene);

            var camGo = Own(stage, new GameObject("StageCamera", typeof(Camera)));
            stage.camera = camGo.GetComponent<Camera>();
            stage.camera.clearFlags = CameraClearFlags.SolidColor;
            stage.camera.backgroundColor = new Color(0.055f, 0.062f, 0.075f);
            stage.camera.orthographic = true;
            stage.camera.orthographicSize = 1.55f;
            stage.camera.nearClipPlane = 0.05f;
            stage.camera.farClipPlane = 60f;
            stage.camera.allowHDR = false;
            stage.camera.allowMSAA = true;

            var key = Own(stage, new GameObject("Key", typeof(Light)));
            var kl = key.GetComponent<Light>();
            kl.type = LightType.Directional;
            kl.intensity = 1.8f;
            kl.color = new Color(1f, 0.94f, 0.86f);
            key.transform.rotation = Quaternion.Euler(42f, -38f, 0f);

            var fill = Own(stage, new GameObject("Fill", typeof(Light)));
            var fl = fill.GetComponent<Light>();
            fl.type = LightType.Directional;
            fl.intensity = 0.75f;
            fl.color = new Color(0.55f, 0.7f, 1f);
            fill.transform.rotation = Quaternion.Euler(14f, 142f, 0f);

            var rim = Own(stage, new GameObject("Rim", typeof(Light)));
            var rl = rim.GetComponent<Light>();
            rl.type = LightType.Directional;
            rl.intensity = 1.1f;
            rl.color = new Color(1f, 0.72f, 0.42f);
            rim.transform.rotation = Quaternion.Euler(18f, 198f, 0f);

            var floor = Own(stage, GameObject.CreatePrimitive(PrimitiveType.Plane));
            var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMat.SetColor("_BaseColor", new Color(0.11f, 0.12f, 0.15f));
            floorMat.SetFloat("_Smoothness", 0.18f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;
            floor.transform.position = new Vector3(0f, 0f, 0f);
            floor.transform.localScale = new Vector3(4f, 1f, 3f);

            stage.rt = new RenderTexture(TileW, TileH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 4
            };
            stage.rt.Create();
            stage.camera.targetTexture = stage.rt;
            return stage;
        }

        public static T Own<T>(Stage stage, T obj) where T : UnityEngine.Object
        {
            var go = obj as GameObject;
            if (go != null)
            {
                SceneManager.MoveGameObjectToScene(go, stage.scene);
                stage.owned.Add(go);
            }
            return obj;
        }

        public static GameObject Instantiate(Stage stage, GameObject prefab)
        {
            var go = UnityEngine.Object.Instantiate(prefab);
            go.name = prefab.name + "_stage";
            SceneManager.MoveGameObjectToScene(go, stage.scene);
            stage.owned.Add(go);
            // Sin esto el skinning se queda un frame atras: la foto sale con la pose
            // ANTERIOR al SampleAnimation (bug medido: se veia la pose de la fase previa).
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.forceMatrixRecalculationPerRender = true;
            return go;
        }

        public static void Frame(Stage stage, Vector3 lookAt, float distance, float yawDegrees, float pitchDegrees, float orthoSize)
        {
            stage.camera.orthographicSize = orthoSize;
            var rot = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
            stage.camera.transform.position = lookAt - rot * Vector3.forward * distance;
            stage.camera.transform.rotation = rot;
        }

        public static void Shot(Stage stage, string file)
        {
            stage.camera.Render();
            stage.camera.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = stage.rt;
            var tex = new Texture2D(TileW, TileH, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, TileW, TileH), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes($"{ShotDir}/{file}", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        public static void End(Stage stage)
        {
            if (stage == null) return;
            if (stage.camera != null) stage.camera.targetTexture = null;
            if (stage.rt != null) { stage.rt.Release(); UnityEngine.Object.DestroyImmediate(stage.rt); }
            foreach (var go in stage.owned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            if (stage.previous.IsValid() && stage.previous.isLoaded)
                EditorSceneManager.SetActiveScene(stage.previous);
            if (stage.scene.IsValid()) EditorSceneManager.CloseScene(stage.scene, true);
        }
    }
}
