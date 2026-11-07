using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KOM.EditorTools
{
    // LIMPIEZA DE ANIMACIONES.
    //
    // El proyecto acumula copias .anim que ya no referencia ningun rig. Antes de borrar
    // nada hay que saber QUE se usa, y eso no se decide por nombre: se pregunta a los
    // assets. Un clip cuenta como usado si lo referencia:
    //   1. un AnimatorController (estados, sub-maquinas, BlendTrees),
    //   2. un AnimatorOverrideController,
    //   3. cualquier otro asset del proyecto (prefab, escena, ScriptableObject).
    //
    // El borrado va SIEMPRE precedido de una copia en Backups/<sello>/ con la misma ruta
    // relativa, asi que se puede restaurar arrastrando la carpeta de vuelta a Assets.
    public static class KomAnimCleanup
    {
        const string ClipRoot = "Assets/KOM/Animation/Clips";
        const string BackupRoot = "Backups";

        public struct ClipInfo
        {
            public string Path;
            public long Bytes;
            public bool Used;
        }

        [MenuItem("King of Music/Diagnostico/43 - Inventario de animaciones", priority = 243)]
        public static void MenuReport()
        {
            Debug.Log(Report());
        }

        public static string Report()
        {
            var all = Scan();
            var sb = new StringBuilder();
            long totalBytes = 0, orphanBytes = 0;
            int used = 0, orphans = 0;
            foreach (var c in all)
            {
                totalBytes += c.Bytes;
                if (c.Used) used++;
                else { orphans++; orphanBytes += c.Bytes; }
            }

            sb.AppendLine("KOM ANIMACIONES · inventario de uso");
            sb.AppendLine($"clips .anim: {all.Count}  usados: {used}  huerfanos: {orphans}");
            sb.AppendLine($"peso total: {Mb(totalBytes)}  peso huerfano: {Mb(orphanBytes)}");
            sb.AppendLine();

            var byChar = new Dictionary<string, List<ClipInfo>>();
            foreach (var c in all)
            {
                string key = CharacterOf(c.Path);
                if (!byChar.TryGetValue(key, out var list)) { list = new List<ClipInfo>(); byChar[key] = list; }
                list.Add(c);
            }

            foreach (var kv in byChar)
            {
                int u = 0, o = 0;
                long ob = 0;
                foreach (var c in kv.Value) { if (c.Used) u++; else { o++; ob += c.Bytes; } }
                sb.AppendLine($"{kv.Key,-18} usados={u,3}  huerfanos={o,3}  ({Mb(ob)})");
                foreach (var c in kv.Value)
                    if (!c.Used) sb.AppendLine($"   - {Path.GetFileName(c.Path)}  {Mb(c.Bytes)}");
            }

            sb.AppendLine();
            sb.AppendLine("--- usados ---");
            foreach (var c in all)
                if (c.Used) sb.AppendLine($"   {c.Path}");

            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/anim_cleanup.txt", sb.ToString());
            Debug.Log($"[KOM][ANIM] Logs/anim_cleanup.txt");
            return sb.ToString();
        }

        // Copia los huerfanos a Backups/<stamp>/ y DESPUES los borra del proyecto.
        // Devuelve el resumen; si algo falla al copiar, no borra ese archivo.
        public static string BackupAndDelete(string stamp)
        {
            var all = Scan();
            var orphans = new List<ClipInfo>();
            foreach (var c in all) if (!c.Used) orphans.Add(c);

            string backupDir = Path.Combine(BackupRoot, stamp);
            Directory.CreateDirectory(backupDir);

            int copied = 0, deleted = 0, failed = 0;
            long freed = 0;
            var manifest = new StringBuilder();
            manifest.AppendLine($"# Respaldo de animaciones no usadas · {stamp}");
            manifest.AppendLine($"# Restaurar: copiar el contenido de esta carpeta sobre la raiz del proyecto.");
            manifest.AppendLine();

            foreach (var c in orphans)
            {
                string rel = c.Path.Replace('/', Path.DirectorySeparatorChar);
                string dst = Path.Combine(backupDir, rel);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(c.Path, dst, true);
                    string meta = c.Path + ".meta";
                    if (File.Exists(meta)) File.Copy(meta, dst + ".meta", true);
                    copied++;
                    manifest.AppendLine($"{c.Path}\t{c.Bytes}");
                }
                catch (System.Exception e)
                {
                    failed++;
                    Debug.LogError($"[KOM][ANIM] no pude respaldar {c.Path}: {e.Message}");
                    continue;
                }

                if (AssetDatabase.DeleteAsset(c.Path))
                {
                    deleted++;
                    freed += c.Bytes;
                }
                else
                {
                    failed++;
                    Debug.LogError($"[KOM][ANIM] no pude borrar {c.Path}");
                }
            }

            File.WriteAllText(Path.Combine(backupDir, "manifest.txt"), manifest.ToString());
            AssetDatabase.Refresh();

            var sb = new StringBuilder();
            sb.AppendLine($"respaldo en {backupDir}: copiados={copied} borrados={deleted} fallos={failed} liberado={Mb(freed)}");
            return sb.ToString();
        }

        static List<ClipInfo> Scan()
        {
            var usedPaths = CollectUsedPaths();
            var result = new List<ClipInfo>();
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".anim")) continue; // los clips dentro de un FBX no se tocan
                var fi = new FileInfo(path);
                result.Add(new ClipInfo { Path = path, Bytes = fi.Exists ? fi.Length : 0, Used = usedPaths.Contains(path) });
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return result;
        }

        // Todo lo que puede "poseer" un clip. Se recorre con AssetDatabase.GetDependencies
        // para no depender de como este guardada la referencia (estado, override, prefab...).
        static HashSet<string> CollectUsedPaths()
        {
            var used = new HashSet<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (c == null) continue;
                foreach (var layer in c.layers)
                    CollectStateMachine(layer.stateMachine, used);
            }

            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorOverrideController"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var dep in AssetDatabase.GetDependencies(path, true))
                    if (dep.EndsWith(".anim")) used.Add(dep);
            }

            // Prefabs, escenas y ScriptableObjects: cualquier referencia directa.
            foreach (var filter in new[] { "t:Prefab", "t:Scene", "t:ScriptableObject" })
            {
                foreach (var guid in AssetDatabase.FindAssets(filter))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path) || Directory.Exists(path)) continue;
                    foreach (var dep in AssetDatabase.GetDependencies(path, true))
                        if (dep.EndsWith(".anim")) used.Add(dep);
                }
            }

            return used;
        }

        static void CollectStateMachine(AnimatorStateMachine sm, HashSet<string> used)
        {
            if (sm == null) return;
            foreach (var st in sm.states)
                CollectMotion(st.state.motion, used);
            foreach (var sub in sm.stateMachines)
                CollectStateMachine(sub.stateMachine, used);
        }

        static void CollectMotion(Motion motion, HashSet<string> used)
        {
            if (motion == null) return;
            if (motion is AnimationClip clip)
            {
                string p = AssetDatabase.GetAssetPath(clip);
                if (!string.IsNullOrEmpty(p) && p.EndsWith(".anim")) used.Add(p);
                return;
            }
            if (motion is BlendTree tree)
                foreach (var child in tree.children)
                    CollectMotion(child.motion, used);
        }

        static string CharacterOf(string path)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            int i = file.IndexOf('_');
            return i > 0 ? file.Substring(0, i) : "(otros)";
        }

        static string Mb(long bytes) => bytes >= 1024L * 1024L
            ? $"{bytes / (1024f * 1024f):F1} MB"
            : $"{bytes / 1024f:F0} KB";
    }
}
