#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace KOM
{
    // SONDA DE COMBATE (solo editor). Verifica el mapeo de botones y los combos EN JUEGO:
    // inyecta pulsaciones con tiempos reales, deja correr la animacion y comprueba
    //   1. que cada boton lance SU ataque (no otro),
    //   2. que una pulsacion produzca exactamente un ataque,
    //   3. que el ataque conecte y haga dano,
    //   4. que cada secuencia de 4 teclas lance su combo,
    //   5. que fuerte pegue mas que rapido.
    //
    // Se ejecuta desde el puente:
    //   KOM.KomCombatProbe.Spawn().Begin("Logs/combat/report.txt")
    public sealed class KomCombatProbe : MonoBehaviour
    {
        const string OutDir = "Logs/combat";

        static readonly (string label, int token, AttackType expected)[] Buttons =
        {
            ("G golpe rapido",  1, AttackType.LightPunch),
            ("H golpe fuerte",  2, AttackType.HeavyPunch),
            ("J patada rapida", 3, AttackType.LightKick),
            ("K patada fuerte", 4, AttackType.HeavyKick),
        };

        static readonly (string label, string tokens, AttackType expected)[] Combos =
        {
            ("G G G", "111", AttackType.Combo1),      // 1=G x3
            ("H H H", "222", AttackType.Combo2),      // 2=H x3
            ("J J J J", "3333", AttackType.Combo3),   // 3=J x4
        };

        public static KomCombatProbe Spawn()
        {
            var go = new GameObject("CombatProbe");
            return go.AddComponent<KomCombatProbe>();
        }

        public void Begin(string path)
        {
            StartCoroutine(Run(path));
        }

        IEnumerator Run(string path)
        {
            Directory.CreateDirectory(OutDir);
            var sb = new StringBuilder();
            sb.AppendLine("KOM COMBATE · mapeo de botones y combos (Play real)");

            Fighter player = null, enemy = null;
            foreach (var f in FindObjectsByType<Fighter>(FindObjectsInactive.Exclude))
            {
                if (f.IsAI) enemy = f; else player = f;
            }
            if (player == null || enemy == null)
            {
                sb.AppendLine("ERROR: no encuentro jugador y rival");
                File.WriteAllText(path, sb.ToString());
                yield break;
            }

            enemy.ControlsEnabled = false;
            player.ControlsEnabled = true;

            // Un recolectable que cure a mitad de una medicion de dano invalida la sonda.
            var combat = player.Combat;
            if (combat != null) combat.PickupsEnabled = false;
            foreach (var p in FindObjectsByType<CombatPickup>(FindObjectsInactive.Exclude))
                Destroy(p.gameObject);

            var results = new List<(string label, bool ok, string detail)>();
            var damageByAttack = new Dictionary<AttackType, float>();

            // --- botones ---
            foreach (var b in Buttons)
            {
                yield return RunSingle(player, enemy, b.token, b.expected, sb, results, damageByAttack);
            }

            // --- ataque extra: correr + K = patada voladora ---
            yield return RunDropKick(player, enemy, sb, results);

            // --- combos ---
            foreach (var c in Combos)
            {
                yield return RunCombo(player, enemy, c.tokens, c.expected, c.label, sb, results);
            }

            // --- contraste rapido vs fuerte ---
            sb.AppendLine();
            sb.AppendLine("--- contraste rapido/fuerte ---");
            if (damageByAttack.TryGetValue(AttackType.LightPunch, out var lp) &&
                damageByAttack.TryGetValue(AttackType.HeavyPunch, out var hp))
            {
                bool ok = hp > lp;
                sb.AppendLine($"punetazo: rapido={lp:F1} fuerte={hp:F1}  {(ok ? "PASS" : "FAIL")}");
                results.Add(("fuerte > rapido (punetazo)", ok, $"{lp:F1} / {hp:F1}"));
            }
            if (damageByAttack.TryGetValue(AttackType.LightKick, out var lk) &&
                damageByAttack.TryGetValue(AttackType.HeavyKick, out var hk))
            {
                bool ok = hk > lk;
                sb.AppendLine($"patada:   rapida={lk:F1} fuerte={hk:F1}  {(ok ? "PASS" : "FAIL")}");
                results.Add(("fuerte > rapido (patada)", ok, $"{lk:F1} / {hk:F1}"));
            }

            int fails = 0;
            sb.AppendLine();
            sb.AppendLine("--- resumen ---");
            foreach (var r in results)
            {
                if (!r.ok) fails++;
                sb.AppendLine($"{(r.ok ? "PASS" : "FAIL")}  {r.label}  {r.detail}");
            }
            sb.AppendLine();
            sb.AppendLine(fails == 0 ? "VERDICT: PASS" : $"VERDICT: FAIL ({fails} comprobaciones)");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[KOM][COMBAT] {path}");

            Destroy(gameObject);
        }

        IEnumerator RunSingle(Fighter player, Fighter enemy, int token, AttackType expected,
                              StringBuilder sb, List<(string, bool, string)> results,
                              Dictionary<AttackType, float> damageByAttack)
        {
            Place(player, enemy);
            yield return null;

            float maxHp = enemy.Def != null ? enemy.Def.maxHealth : 100f;
            player.DebugResetCombatState();
            enemy.DebugResetCombatState();
            yield return null;

            player.DebugPushComboToken(token);
            yield return null;

            var started = player.CurrentAttack;
            string startedName = started != null ? started.type.ToString() : "none";

            yield return WaitAttackEnd(player);
            yield return new WaitForSeconds(0.15f);

            float dealt = Mathf.Max(0f, maxHp - enemy.Health);

            int traceCount = player.AttackTrace.Count;
            bool typeOk = started != null && started.type == expected;
            bool single = traceCount == 1;
            bool hitOk = dealt > 0.01f;

            sb.AppendLine();
            sb.AppendLine($"{expected,-12} tecla={token}  estado={startedName}  dano={dealt:F1}  ataques_en_la_traza={traceCount}");
            sb.AppendLine($"   tipo={(typeOk ? "PASS" : "FAIL")}  un_solo_ataque={(single ? "PASS" : "FAIL")}  conecta={(hitOk ? "PASS" : "FAIL")}");
            results.Add(($"{expected} tipo", typeOk, startedName));
            results.Add(($"{expected} una pulsacion = un ataque", single, $"{traceCount}"));
            results.Add(($"{expected} conecta", hitOk, $"{dealt:F1}"));
            if (hitOk) damageByAttack[expected] = dealt;
        }

        IEnumerator RunDropKick(Fighter player, Fighter enemy, StringBuilder sb, List<(string, bool, string)> results)
        {
            Place(player, enemy);
            yield return null;

            float maxHp = enemy.Def != null ? enemy.Def.maxHealth : 100f;
            player.DebugResetCombatState();
            enemy.DebugResetCombatState();
            yield return null;

            // Corriendo + K (token 4) = patada voladora, sin consumir secuencia.
            player.DebugSprint(1f);
            yield return null;
            player.DebugPushComboToken(4);
            yield return null;

            var started = player.CurrentAttack;
            string startedName = started != null ? started.type.ToString() : "none";

            yield return WaitAttackEnd(player);
            yield return new WaitForSeconds(0.15f);

            float dealt = Mathf.Max(0f, maxHp - enemy.Health);
            bool typeOk = started != null && started.type == AttackType.DropKick;
            bool hitOk = dealt > 0.01f;

            sb.AppendLine();
            sb.AppendLine($"DropKick     correr + K  estado={startedName}  dano={dealt:F1}");
            sb.AppendLine($"   tipo={(typeOk ? "PASS" : "FAIL")}  conecta={(hitOk ? "PASS" : "FAIL")}");
            results.Add(("patada voladora (correr + K) tipo", typeOk, startedName));
            results.Add(("patada voladora conecta", hitOk, $"{dealt:F1}"));
        }

        IEnumerator RunCombo(Fighter player, Fighter enemy, string tokens, AttackType expected, string label,
                             StringBuilder sb, List<(string, bool, string)> results)
        {
            Place(player, enemy);
            yield return null;

            float maxHp = enemy.Def != null ? enemy.Def.maxHealth : 100f;
            player.DebugResetCombatState();
            enemy.DebugResetCombatState();
            yield return null;

            player.DebugRunSequence(tokens, 0.12f);

            // La secuencia son 4 pulsaciones; hay que esperar a que termine el combo.
            float waited = 0f;
            bool seen = false;
            while (waited < 6f)
            {
                yield return null;
                waited += Time.unscaledDeltaTime;
                foreach (var entry in player.AttackTrace)
                    if (entry.Contains(expected.ToString())) seen = true;
                if (seen && player.CurrentAttack == null) break;
            }
            yield return new WaitForSeconds(0.2f);

            float dealt = Mathf.Max(0f, maxHp - enemy.Health);
            bool ok = seen && dealt > 0f;

            sb.AppendLine();
            sb.AppendLine($"COMBO {label} -> {expected}  visto={(seen ? "si" : "no")}  dano={dealt:F1}");
            sb.AppendLine($"   secuencia={(seen ? "PASS" : "FAIL")}  conecta={(dealt > 0f ? "PASS" : "FAIL")}");
            string trace = "";
            foreach (var t in player.AttackTrace) trace += t + " ";
            sb.AppendLine($"   traza: {trace}");
            sb.AppendLine($"   historial: {player.DebugComboHistory()}");
            results.Add(($"combo {label}", ok, $"dano={dealt:F1}"));
        }

        static void Place(Fighter player, Fighter enemy)
        {
            player.transform.position = new Vector3(-0.55f, 0f, 0.2f);
            enemy.transform.position = new Vector3(0.55f, 0f, 0.2f);
        }

        static IEnumerator WaitAttackEnd(Fighter f)
        {
            int frames = 0;
            while (f.CurrentAttack != null && frames < 600)
            {
                frames++;
                yield return null;
            }
        }
    }
}
#endif
