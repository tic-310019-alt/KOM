using UnityEditor;
using UnityEngine;

namespace KOM.EditorTools
{
    /// <summary>
    /// Los VFX crean sus materiales con Shader.Find en runtime. En un build los shaders
    /// que no referencia ningun asset se eliminan y el Material lanza una excepcion a
    /// mitad del golpe. Estos materiales en Resources garantizan que los 10 shaders KOM
    /// entren al player aunque el Always Included se pierda en una migracion.
    /// </summary>
    public static class KomShaderIncludeSetup
    {
        static readonly string[] Shaders =
        {
            "KOM/AdditiveGlow", "KOM/AuraShell", "KOM/BlockShield", "KOM/ChainSilver",
            "KOM/EnergyOrb", "KOM/HitFlash", "KOM/ImpactGlow", "KOM/ImpactScreen",
            "KOM/PropBW", "KOM/StageBW",
        };

        [MenuItem("King of Music/Setup/12 - Incluir shaders KOM en build", priority = 247)]
        public static void MenuEnsure() => Debug.Log(Ensure());

        public static string Ensure()
        {
            const string folder = "Assets/Resources/KOMShaders";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/Resources", "KOMShaders");

            int created = 0, repaired = 0;
            foreach (string shaderName in Shaders)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogWarning("[KOM][SHADERS] no encontrado: " + shaderName);
                    continue;
                }
                string path = $"{folder}/{shaderName.Replace('/', '_')}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                    created++;
                }
                else if (mat.shader != shader)
                {
                    mat.shader = shader;
                    EditorUtility.SetDirty(mat);
                    repaired++;
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return $"[KOM][SHADERS] materiales de inclusion: {created} creados, {repaired} reparados.";
        }
    }
}
