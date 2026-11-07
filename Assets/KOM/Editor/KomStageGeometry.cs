using System.Collections.Generic;
using UnityEngine;

namespace KOM.EditorTools
{
    // Sonda geometrica de un escenario. El decorado real NO tiene colliders (el combate es
    // cinematico), asi que para medirlo se le ponen MeshCollider temporales a sus mallas, se
    // lanzan los rayos y se quitan. Todo lo que sigue se apoya en esto: la escala de un
    // escenario no se decide a ojo, se mide.
    public sealed class StageProbe : System.IDisposable
    {
        readonly List<MeshCollider> _added = new List<MeshCollider>();
        readonly List<Collider> _disabled = new List<Collider>();
        readonly Bounds _bounds;

        public Bounds Bounds => _bounds;

        public StageProbe(GameObject root)
        {
            // Los escenarios procedurales se arman con primitivas: `Destroy` del collider no
            // funciona en modo edicion y el CapsuleCollider del disco sobrevive. Se apagan
            // TODOS los colliders existentes y se mide contra las mallas reales.
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (!c.enabled) continue;
                c.enabled = false;
                _disabled.Add(c);
            }

            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            bool first = true;
            var b = new Bounds();
            foreach (var r in renderers)
            {
                // El disco de sombra y el anillo son hijos del luchador, no del decorado:
                // si entraran, el "suelo" medido seria el del efecto.
                if (r.name.StartsWith("GroundShadow") || r.name.StartsWith("GroundRing")) continue;
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);

                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mc = r.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
                _added.Add(mc);
            }
            _bounds = b;
            Physics.SyncTransforms();
        }

        public bool Cast(Vector3 origin, Vector3 dir, out RaycastHit hit, float dist = 400f)
        {
            return Physics.Raycast(origin, dir, out hit, dist, ~0, QueryTriggerInteraction.Ignore);
        }

        // Altura de la primera superficie solida bajo (x,z). NaN si no hay nada: un 0 seria
        // un suelo inventado.
        public float SurfaceAt(float x, float z, float fromY = 200f)
        {
            return Cast(new Vector3(x, fromY, z), Vector3.down, out var hit, fromY + 200f)
                ? hit.point.y
                : float.NaN;
        }

        // Superficie de suelo CERCA de una cota dada (los pies del luchador). Se usa para el
        // contacto: un rayo desde 200 m impacta antes en la parrilla de luces o en un foco,
        // que no es el suelo.
        public float SurfaceNear(float x, float z, float y, float dist = 3f)
        {
            return Cast(new Vector3(x, y + 0.8f, z), Vector3.down, out var hit, dist)
                ? hit.point.y
                : float.NaN;
        }

        public struct Plateau
        {
            public bool Found;
            public float FloorY;        // suelo de la sala (la superficie grande mas baja)
            public float Height;        // cota de la plataforma
            public float Rise;          // Height - FloorY
            public Vector3 Center;      // centro de la huella (x, Height, z)
            public float MinX, MaxX, MinZ, MaxZ;
            public float Area;          // m2 de la huella
            public int Cells;
        }

        // Encuentra la PLATAFORMA de pelea: la meseta continua que contiene el origen.
        // No se busca por nombre de material ni por bounds (el decorado es una sola malla
        // que incluye techo y paredes): se busca por geometria, que es lo unico que no miente.
        // `fromY` acota la sonda por encima de los pies: sin el, el primer impacto es la
        // parrilla de luces del techo.
        public Plateau FindPlateau(float extent = 16f, float step = 0.5f, float fromY = 3.5f)
        {
            int n = Mathf.RoundToInt(extent * 2f / step) + 1;
            var h = new float[n * n];
            for (int iz = 0; iz < n; iz++)
            {
                float z = -extent + iz * step;
                for (int ix = 0; ix < n; ix++)
                {
                    float x = -extent + ix * step;
                    h[iz * n + ix] = SurfaceAt(x, z, fromY);
                }
            }

            // Suelo de la sala: la cota baja mas frecuente (bucket de 5 cm) con presencia
            // suficiente. Se toma la MAS BAJA de las que superan el umbral, no la moda
            // global, porque en Bellas Artes el mosaico ocupa mas superficie que el suelo.
            var counts = new Dictionary<int, int>();
            int valid = 0;
            for (int i = 0; i < h.Length; i++)
            {
                if (float.IsNaN(h[i])) continue;
                int key = Mathf.RoundToInt(h[i] * 20f);
                counts.TryGetValue(key, out var c);
                counts[key] = c + 1;
                valid++;
            }
            int minCells = Mathf.Max(8, Mathf.RoundToInt(valid * 0.06f));
            int floorKey = int.MaxValue;
            foreach (var kv in counts)
                if (kv.Value >= minCells && kv.Key < floorKey) floorKey = kv.Key;
            if (floorKey == int.MaxValue) return default;
            float floorY = floorKey / 20f;

            // Superficie que pisan los luchadores: la del origen. La malla puede tener un
            // hueco justo en el centro (el medallon), asi que se toma la mediana de una
            // ventana pequena en vez de un unico rayo.
            float anchorY = AnchorHeight(h, n, n / 2, n / 2, 3);
            if (float.IsNaN(anchorY)) return new Plateau { Found = false, FloorY = floorY };

            // Meseta = celdas a la MISMA cota que el punto de apoyo. La tolerancia tiene que
            // ser MAS FINA que el escalon mas bajo que separa la plataforma del suelo de la
            // sala: el disco del Zocalo esta solo 5 cm sobre el suelo, y con 20 cm de margen
            // ambas superficies se fundian en una sola huella de 36 m.
            var mask = new bool[n * n];
            for (int i = 0; i < h.Length; i++)
                mask[i] = !float.IsNaN(h[i]) && Mathf.Abs(h[i] - anchorY) <= 0.03f;

            var seen = new bool[n * n];
            var best = new List<int>();
            var stack = new Stack<int>();
            // La plataforma de pelea es la meseta que CONTIENE el origen: es donde pisan los
            // luchadores. Elegir la meseta mas grande a secas confunde una galeria elevada o
            // un techo con la tarima (paso en Bellas Artes al encoger el escenario).
            // El origen puede caer en un hueco de la malla (el medallon central): se busca la
            // celda enmascarada mas cercana, no la exacta.
            int anchor = (n / 2) * n + (n / 2);
            if (!mask[anchor]) anchor = NearestMasked(mask, n, n / 2, n / 2, 12);
            var anchorComp = new List<int>();
            for (int start = 0; start < mask.Length; start++)
            {
                if (!mask[start] || seen[start]) continue;
                var comp = new List<int>();
                stack.Clear();
                stack.Push(start);
                seen[start] = true;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    comp.Add(i);
                    int ix = i % n, iz = i / n;
                    if (ix > 0) Push(mask, seen, stack, i - 1);
                    if (ix < n - 1) Push(mask, seen, stack, i + 1);
                    if (iz > 0) Push(mask, seen, stack, i - n);
                    if (iz < n - 1) Push(mask, seen, stack, i + n);
                }
                if (comp.Contains(anchor)) anchorComp = comp;
                if (comp.Count > best.Count) best = comp;
            }
            if (anchorComp.Count >= 16) best = anchorComp;
            if (best.Count == 0) return new Plateau { Found = false, FloorY = floorY };

            // Altura robusta: mediana de la componente (evita que un borde inclinado la mueva).
            var ys = new List<float>(best.Count);
            foreach (var i in best) ys.Add(h[i]);
            ys.Sort();
            float height = ys[ys.Count / 2];

            float sx = 0f, sz = 0f, minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var i in best)
            {
                float x = -extent + (i % n) * step;
                float z = -extent + (i / n) * step;
                sx += x; sz += z;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }

            return new Plateau
            {
                Found = true,
                FloorY = floorY,
                Height = height,
                Rise = height - floorY,
                Center = new Vector3(sx / best.Count, height, sz / best.Count),
                // Las celdas se muestrean en su CENTRO: la huella real llega media celda mas
                // alla por cada lado. Sin esta correccion el ancho sale ~0.5 m corto.
                MinX = minX - step * 0.5f, MaxX = maxX + step * 0.5f,
                MinZ = minZ - step * 0.5f, MaxZ = maxZ + step * 0.5f,
                Area = best.Count * step * step,
                Cells = best.Count
            };
        }

        static void Push(bool[] mask, bool[] seen, Stack<int> stack, int i)
        {
            if (!mask[i] || seen[i]) return;
            seen[i] = true;
            stack.Push(i);
        }

        static int NearestMasked(bool[] mask, int n, int cx, int cz, int radius)
        {
            for (int r = 1; r <= radius; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= n || z >= n) continue;
                        int i = z * n + x;
                        if (mask[i]) return i;
                    }
                }
            }
            return -1;
        }

        // Mediana de las alturas validas en una ventana cuadrada. Robusta a huecos y a un
        // bisel en el borde del medallon.
        static float AnchorHeight(float[] h, int n, int cx, int cz, int radius)
        {
            var vals = new List<float>();
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= n || z >= n) continue;
                    float v = h[z * n + x];
                    if (!float.IsNaN(v)) vals.Add(v);
                }
            }
            if (vals.Count == 0) return float.NaN;
            vals.Sort();
            return vals[vals.Count / 2];
        }

        public void Dispose()
        {
            foreach (var mc in _added)
                if (mc != null) Object.DestroyImmediate(mc);
            _added.Clear();
            foreach (var c in _disabled)
                if (c != null) c.enabled = true;
            _disabled.Clear();
        }
    }
}
