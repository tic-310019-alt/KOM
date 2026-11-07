using UnityEngine;
#if UNITY_EDITOR
using System.IO;
#endif

namespace KOM
{
    // Evidencia numerica de los especiales en Play: anclaje de mano, origen del
    // proyectil, punta de la cadena y estado del hit. En build solo va al log.
    public static class KomSpecialDiag
    {
        const string Path = "Logs/special_math.txt";

        public static void Reset()
        {
#if UNITY_EDITOR
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Path,
                "KOM SPECIAL MATH · anclajes y colisiones (Play)\n" +
                "formato: EVENTO|caster|tipo|campos...\n");
#endif
        }

        public static void Log(string line)
        {
            Debug.Log("[KOM][SPECIAL] " + line);
#if UNITY_EDITOR
            Directory.CreateDirectory("Logs");
            File.AppendAllText(Path, line + "\n");
#endif
        }
    }
}
