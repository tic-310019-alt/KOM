using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KOM.EditorTools
{
    // Banco de pruebas de los especiales en PLAY (solo editor).
    // Uso desde el puente:
    //   eval 'KOM.EditorTools.KomSpecialShot.Setup("juan_grabriel","peso_plomo")'
    //   (esperar a que cargue Combate)
    //   eval 'KOM.EditorTools.KomSpecialShot.Fire("juan","0.3,0.7,1.05,1.25,1.6")'
    // Congela la IA del rival para que la captura no dependa de que la golpee.
    public static class KomSpecialShot
    {
        [MenuItem("King of Music/Diagnostico/30 - Preparar combate", priority = 240)]
        public static void MenuSetup() => Setup("juan_grabriel", "peso_plomo");

        public static string Setup(string playerId, string opponentId)
        {
            return Setup(playerId, opponentId, 0);
        }

        // `encounter` avanza el torneo para probar cada escenario: 0 = La Madrugadora,
        // 1 = Bellas Artes, 2 = Zocalo, 3 = final.
        public static string Setup(string playerId, string opponentId, int encounter)
        {
            KomData.Reload();
            var session = GameSession.Ensure();
            session.ResetRun();
            session.BeginRun(playerId);
            for (int i = 0; i < encounter; i++) session.CompleteEncounter();
            if (!string.IsNullOrEmpty(opponentId))
                session.DebugSetOpponents(new[] { opponentId });
            SceneManager.LoadScene("Combat");
            return $"combat setup player={playerId} opponent={opponentId} encounter={encounter}";
        }

        // Elige al luchador del JUGADOR por id de sesion, no por "el ultimo que no es IA":
        // al cargar Combate dos veces conviven dos mundos y el orden de FindObjectsByType
        // no es estable, asi que se disparaba el especial del personaje equivocado.
        static void PickFighters(out Fighter player, out Fighter enemy)
        {
            var session = GameSession.Ensure();
            player = null;
            enemy = null;
            foreach (var f in UnityEngine.Object.FindObjectsByType<Fighter>())
            {
                if (f == null) continue;
                if (!f.IsAI && f.CharacterId == session.PlayerId) { player = f; continue; }
                if (f.IsAI) enemy = f;
            }
            if (player == null)
                foreach (var f in UnityEngine.Object.FindObjectsByType<Fighter>())
                    if (f != null && !f.IsAI) player = f;
            if (enemy == null)
                foreach (var f in UnityEngine.Object.FindObjectsByType<Fighter>())
                    if (f != null && f.IsAI) enemy = f;
        }

        public static string Fire(string prefix, string timesCsv)
        {
            PickFighters(out var player, out var enemy);
            if (player == null) return "NO PLAYER";
            if (enemy != null) enemy.ControlsEnabled = false;
            player.ControlsEnabled = true;
            player.FillSuperDebug();

            float[] times = Parse(timesCsv);
            var go = new GameObject("SpecialCapture");
            go.AddComponent<KomSpecialCapture>().ConfigureAtProgress(player, prefix, times);
            player.StartSpecial();
            return $"fired {prefix} milestones={times.Length}";
        }

        // Prueba de impacto limpia: el rival queda como saco (Fighter apagado, sin IA ni
        // animacion) a `distance` metros. Sin esto la IA devuelve el golpe y cancela el
        // especial antes de que llegue el segundo impacto.
        public static string FireClose(string prefix, string timesCsv, float distance)
        {
            PickFighters(out var player, out var enemy);
            if (player == null) return "NO PLAYER";
            if (enemy != null)
            {
                enemy.ControlsEnabled = false;
                enemy.enabled = false;
                var p = enemy.transform.position;
                p.x = player.transform.position.x + Mathf.Max(1.05f, distance);
                enemy.transform.position = p;
            }
            player.ControlsEnabled = true;
            player.FillSuperDebug();
            float[] times = Parse(timesCsv);
            if (times.Length > 0)
            {
                var go = new GameObject("SpecialCapture");
                go.AddComponent<KomSpecialCapture>().ConfigureAtProgress(player, prefix, times);
            }
            player.StartSpecial();
            return $"fired close {prefix} distance={distance}";
        }

        // Dispara sin capturar (para inspeccion manual del Game View).
        public static string FireOnly()
        {
            PickFighters(out var player, out _);
            if (player == null) return "NO PLAYER";
            player.ControlsEnabled = true;
            player.FillSuperDebug();
            player.StartSpecial();
            return "fired";
        }

        // Fuerza un especial concreto (indice del JSON) y coloca al rival a `distance`
        // metros para reproducir la seleccion por distancia de la jefa.
        public static string FireIndex(string prefix, string timesCsv, int index, float distance)
        {
            PickFighters(out var player, out var enemy);
            if (player == null) return "NO PLAYER";
            if (enemy != null)
            {
                enemy.ControlsEnabled = false;
                var p = enemy.transform.position;
                p.x = player.transform.position.x + Mathf.Max(1.1f, distance);
                enemy.transform.position = p;
            }
            player.ControlsEnabled = true;
            player.FillSuperDebug();
            float[] times = Parse(timesCsv);
            var go = new GameObject("SpecialCapture");
            go.AddComponent<KomSpecialCapture>().ConfigureAtProgress(player, prefix, times);
            player.StartSpecial(index);
            return $"fired index={index} distance={distance} milestones={times.Length}";
        }

        // Estado del combate en Play: quien pelea, donde esta y si el especial esta activo.
        // Sirve para diagnosticar un Setup que no tomo (sesion vieja, escena duplicada).
        public static string Probe()
        {
            var fighters = UnityEngine.Object.FindObjectsByType<Fighter>();
            var sb = new System.Text.StringBuilder();
            sb.Append("session player=").Append(GameSession.Ensure().PlayerId)
              .Append(" encounter=").Append(GameSession.Ensure().EncounterIndex)
              .Append(" fighters=").Append(fighters.Length);
            foreach (var f in fighters)
            {
                if (f == null) continue;
                var p = f.transform.position;
                sb.Append($"\n  {f.CharacterId} ai={f.IsAI} pos=({p.x:F2},{p.y:F2},{p.z:F2}) special={(f.ActiveSpecial != null ? f.ActiveSpecial.type : "-")} super={f.SuperFraction:F2}");
            }
            return sb.ToString();
        }

        static float[] Parse(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return new[] { 0.5f, 1f, 1.4f };
            var parts = csv.Split(',');
            var list = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out list[i]);
            return list;
        }
    }
}
