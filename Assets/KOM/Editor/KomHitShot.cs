using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace KOM.EditorTools
{
    // Banco de pruebas del feel de los golpes (solo editor).
    //   eval 'KOM.EditorTools.KomHitShot.Setup("juan_grabriel","peso_plomo")'
    //   eval 'KOM.EditorTools.KomHitShot.Punch("HeavyPunch","punch")'
    //   eval 'KOM.EditorTools.KomHitShot.Punch("Combo1","combo1",0.9)'
    public static class KomHitShot
    {
        [MenuItem("King of Music/Diagnostico/31 - Golpe con captura", priority = 241)]
        public static void MenuPunch() => Punch("HeavyPunch", "punch");

        public static string Setup(string playerId, string opponentId)
            => KomSpecialShot.Setup(playerId, opponentId);

        /// <summary>
        /// Coloca al rival a distancia de golpe, lanza el ataque pedido y captura una
        /// rafaga de frames desde el impacto. `attack` es el nombre del estado
        /// (HeavyPunch, Combo1, DropKick...).
        /// </summary>
        public static string Punch(string attack, string prefix, float delay = 0.016f)
        {
            var fighters = UnityEngine.Object.FindObjectsByType<Fighter>();
            Fighter player = null;
            Fighter enemy = null;
            foreach (var f in fighters)
            {
                if (f == null) continue;
                if (f.IsAI) enemy = f; else player = f;
            }
            if (player == null) return "NO PLAYER";

            var spec = Resolve(attack);
            if (spec == null) return "NO SPEC " + attack;

            if (enemy != null)
            {
                enemy.ControlsEnabled = false;
                // A 1.0 m del pecho: dentro de la caja de todos los golpes medidos.
                var p = enemy.transform.position;
                p.x = player.transform.position.x + (player.Facing >= 0f ? 1.0f : -1.0f);
                enemy.transform.position = p;
            }

            player.ControlsEnabled = true;
            var go = new GameObject("HitCapture");
            go.AddComponent<KomHitCapture>().Configure(player, enemy, spec, prefix, delay);
            return $"punch {attack} prefix={prefix} enemy={(enemy != null ? enemy.CharacterId : "-")}";
        }

        static AttackSpec Resolve(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var spec in Attacks.All)
            {
                if (spec == null) continue;
                if (string.Equals(spec.animatorState, name, StringComparison.OrdinalIgnoreCase)) return spec;
                if (string.Equals(spec.type.ToString(), name, StringComparison.OrdinalIgnoreCase)) return spec;
            }
            return null;
        }
    }
}
