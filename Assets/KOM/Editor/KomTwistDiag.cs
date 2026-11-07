using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KOM.EditorTools
{
    // Yaw del cuerpo por PROCUSTES 2D: ajusta el giro que mejor lleva la pose actual a
    // la pose de referencia usando 9 huesos proyectados al plano XZ. La linea de caderas
    // NO sirve: cuando la pelvis se inclina (hachazo, patada) su proyeccion salta y el
    // angulo medido es ruido (se midio 0.224 de componente vertical).
    public static class KomBodyYaw
    {
        public static readonly string[] Probes =
        {
            "DEF-foot.L", "DEF-foot.R", "DEF-toe.L", "DEF-toe.R",
            "DEF-thigh.L", "DEF-thigh.R", "DEF-upper_arm.L", "DEF-upper_arm.R",
            "DEF-spine.006"
        };

        public static Transform[] FindProbes(Transform[] all, out int found)
        {
            var list = new List<Transform>();
            foreach (var name in Probes)
            {
                Transform hit = null;
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].name == name) { hit = all[i]; break; }
                if (hit != null) list.Add(hit);
            }
            found = list.Count;
            return list.ToArray();
        }

        public static Vector3[] Sample(Transform[] probes)
        {
            var pts = new Vector3[probes.Length];
            for (int i = 0; i < probes.Length; i++)
            {
                var p = probes[i].position;
                pts[i] = new Vector3(p.x, 0f, p.z);
            }
            return pts;
        }

        // Igual pero encadenando TRS locales: no depende de Physics.SyncTransforms y
        // por tanto es seguro dentro del horneado (donde no corre el player loop).
        public static Vector3[] Sample(Transform[] probes, Transform root)
        {
            var pts = new Vector3[probes.Length];
            for (int i = 0; i < probes.Length; i++)
            {
                var p = KomPropLab.ChainPosition(probes[i], root);
                pts[i] = new Vector3(p.x, 0f, p.z);
            }
            return pts;
        }

        // Yaw EXACTO de un quaternion: descomposicion swing-twist sobre el eje Y. Es
        // estable aunque la pelvis se incline (el hachazo la inclina 40 grados) y no
        // depende de que los puntos de sondeo esten bien repartidos.
        public static float TwistYaw(Quaternion q)
        {
            Vector3 axis = new Vector3(q.x, q.y, q.z);
            float proj = Vector3.Dot(axis, Vector3.up);
            var twist = new Quaternion(0f, proj, 0f, q.w);
            float mag = twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w;
            if (mag < 1e-8f) return 0f;
            twist = new Quaternion(twist.x / Mathf.Sqrt(mag), twist.y / Mathf.Sqrt(mag),
                twist.z / Mathf.Sqrt(mag), twist.w / Mathf.Sqrt(mag));
            return 2f * Mathf.Atan2(twist.y, twist.w) * Mathf.Rad2Deg;
        }

        // Angulo (grados) que hay que girar la referencia para caer en la pose actual.
        public static float Yaw(Vector3[] reference, Vector3[] current)
        {
            if (reference == null || current == null || reference.Length < 3) return 0f;
            float cross = 0f, dot = 0f;
            int n = Mathf.Min(reference.Length, current.Length);
            for (int i = 0; i < n; i++)
            {
                cross += reference[i].z * current[i].x - reference[i].x * current[i].z;
                dot += reference[i].x * current[i].x + reference[i].z * current[i].z;
            }
            return Mathf.Atan2(cross, dot) * Mathf.Rad2Deg;
        }
    }

    // Diagnostico de GIRO del cuerpo en un clip. Cada fase se muestrea en una instancia
    // LIMPIA: reutilizarla hace que los huesos sin curva (el clip horneado filtra huesos)
    // arrastren la pose anterior y el yaw medido derive (se midio 91 grados de deriva falsa).
    public static class KomTwistDiag
    {
        static GameObject Spawn(string id)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{id}.prefab");
            if (prefab == null) return null;
            var go = UnityEngine.Object.Instantiate(prefab);
            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.enabled = false;
            return go;
        }

        static Vector3[] SampleAt(string id, AnimationClip clip, float time, out int found)
        {
            var go = Spawn(id);
            var all = go.GetComponentsInChildren<Transform>(true);
            var probes = KomBodyYaw.FindProbes(all, out found);
            clip.SampleAnimation(go, time);
            Physics.SyncTransforms();
            var pts = KomBodyYaw.Sample(probes);
            UnityEngine.Object.DestroyImmediate(go);
            return pts;
        }

        public static string Measure(string id, string clipName, int steps = 20)
        {
            var probe = Spawn(id);
            if (probe == null) return "ERROR: falta prefab " + id;
            UnityEngine.Object.DestroyImmediate(probe);
            var clip = KomPropLab.FindClip(id, clipName);
            if (clip == null) return "ERROR: falta clip " + clipName;

            var lines = new List<string>();
            lines.Add($"TWIST|{id}|{clipName}|len={clip.length:F3}");
            lines.Add("t_norm\tyaw");
            float maxYaw = 0f;
            Vector3[] reference = null;
            int found = 0;
            for (int i = 0; i <= steps; i++)
            {
                float n = i / (float)steps;
                float t = Mathf.Min(n * clip.length, clip.length - 0.0001f);
                var sample = SampleAt(id, clip, t, out found);
                if (i == 0) reference = sample;
                float yaw = KomBodyYaw.Yaw(reference, sample);
                maxYaw = Mathf.Max(maxYaw, Mathf.Abs(yaw));
                lines.Add($"{n:F3}\t{yaw:F1}");
            }
            lines.Add($"MAX|yaw={maxYaw:F1}|probes={found}");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/twist.txt", string.Join("\n", lines) + "\n");
            return $"MAX yaw={maxYaw:F1} (probes={found}) clip={clipName} id={id}";
        }
    }
}
