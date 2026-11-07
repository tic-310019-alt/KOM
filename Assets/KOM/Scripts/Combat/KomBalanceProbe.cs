#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace KOM
{
    /// <summary>
    /// Sonda de Play para dos defectos concretos:
    ///   1. Combo3 (J+J+J+J) debe avanzar con el clip (1.49 m medidos), no quedarse atras.
    ///   2. La IA no debe iniciar un golpe normal si el rival quieto esta fuera de su
    ///      alcance real (hitbox + media anchura + hurtbox del rival).
    ///
    /// Uso desde el puente:
    ///   KOM.KomBalanceProbe.Spawn().Begin("Logs/balance/report.txt")
    /// </summary>
    public sealed class KomBalanceProbe : MonoBehaviour
    {
        const string OutDir = "Logs/balance";

        public static KomBalanceProbe Spawn()
        {
            var go = new GameObject("BalanceProbe");
            return go.AddComponent<KomBalanceProbe>();
        }

        public void Begin(string path) => StartCoroutine(Run(path));

        IEnumerator Run(string path)
        {
            Directory.CreateDirectory(OutDir);
            var sb = new StringBuilder();
            sb.AppendLine("KOM BALANCE · avance de Combo3 y alcance de la IA (Play real)");

            Fighter player = null, enemy = null;
            foreach (var f in FindObjectsByType<Fighter>(FindObjectsInactive.Exclude))
            {
                if (f.IsAI) enemy = f; else player = f;
            }
            if (player == null || enemy == null)
            {
                sb.AppendLine("ERROR: no encuentro jugador y rival.");
                File.WriteAllText(path, sb.ToString());
                yield break;
            }

            var combat = player.Combat;
            if (combat != null) combat.PickupsEnabled = false;
            foreach (var p in FindObjectsByType<CombatPickup>(FindObjectsInactive.Exclude))
                Destroy(p.gameObject);

            // La ronda tiene que estar en marcha: el reloj, la IA y los controles del
            // Fighter se habilitan al terminar la presentacion.
            float wait = 0f;
            while (combat != null && !combat.RoundActive && wait < 8f)
            {
                yield return null;
                wait += Time.deltaTime;
            }
            sb.AppendLine($"perfil IA: {(GameSession.Ensure().CurrentNode != null ? GameSession.Ensure().CurrentNode.aiProfile : "?")}" +
                          $"  rondaActiva={(combat != null && combat.RoundActive)}");

            // ------------------------------------------------------------ Combo3
            player.ResetForRound(new Vector3(-2.6f, 0f, 0.2f), 1f);
            enemy.ResetForRound(new Vector3(2.6f, 0f, 0.2f), -1f);
            player.ControlsEnabled = false;   // los DebugPushComboToken no dependen de esto
            enemy.ControlsEnabled = false;
            yield return null;
            yield return null;

            // La referencia se toma ANTES de la secuencia: los tres ataques previos
            // (J, G, K) no tienen avance, asi que no ensucian la medida y no se pierde
            // el primer frame del combo por mirar el tipo de ataque desde fuera.
            float comboStartX = player.transform.position.x;
            float comboEndX = 0f;
            bool comboSeen = false;
            player.DebugRunSequence("3333", 0.12f);
            float t = 0f;
            while (t < 8f)
            {
                yield return null;
                t += Time.deltaTime;
                var atk = player.CurrentAttack;
                if (!comboSeen && atk != null && atk.type == AttackType.Combo3)
                    comboSeen = true;
                if (comboSeen && atk == null)
                {
                    comboEndX = player.transform.position.x;
                    break;
                }
            }
            float travel = comboEndX - comboStartX;
            bool travelOk = comboSeen && travel >= 1.3f && travel <= 1.7f;
            sb.AppendLine();
            sb.AppendLine($"COMBO3 J+J+J+J  visto={(comboSeen ? "si" : "no")}  avance={travel:F2} m  " +
                          $"{(travelOk ? "PASS" : "FAIL")}  (esperado 1.40-1.53 m medidos en el clip)");
            sb.AppendLine("   nota: el rival esta a 5.2 m para que el avance no lo limite la separacion minima.");

            // ------------------------------------------------ Combo3 a distancia de golpe
            // El avance tiene que MOVER el cuerpo sin sacarlo del alcance. Se repite a
            // 1.1 m y se mide el dano DEL COMBO (desde que entra hasta que termina),
            // separado del primer LightKick que forma parte de la secuencia.
            player.ResetForRound(new Vector3(-0.55f, 0f, 0.2f), 1f);
            enemy.ResetForRound(new Vector3(0.55f, 0f, 0.2f), -1f);
            player.ControlsEnabled = false;
            enemy.ControlsEnabled = false;
            player.DevResetSuper();
            enemy.DevResetSuper();
            yield return null;
            yield return null;
            sb.AppendLine($"   reset: p={player.transform.position.x:F2} e={enemy.transform.position.x:F2}" +
                          $" oppOk={ReferenceEquals(player.Opponent, enemy)}");

            float comboStartHealth = 0f, comboEndHealth = 0f;
            bool hitComboSeen = false;
            string comboPositions = "";
            player.DebugRunSequence("3333", 0.12f);
            t = 0f;
            float nextSample = 0f;
            while (t < 8f)
            {
                yield return null;
                t += Time.deltaTime;
                var atk = player.CurrentAttack;
                if (!hitComboSeen && atk != null && atk.type == AttackType.Combo3)
                {
                    hitComboSeen = true;
                    comboStartHealth = enemy.Health;
                }
                if (comboPositions.Length < 900 && t >= nextSample)
                {
                    // Muestreo desde el primer frame, no solo cuando el combo ya entro:
                    // asi se ve si el rival se mueve por una orden vieja o por knockback.
                    nextSample += 0.2f;
                    float px = player.transform.position.x;
                    float ex = enemy.transform.position.x;
                    comboPositions += $"{t:F2}:p={px:F2},e={ex:F2},d={Mathf.Abs(px - ex):F2},hp={enemy.Health:F0} ";
                }
                if (hitComboSeen && atk == null)
                {
                    comboEndHealth = enemy.Health;
                    break;
                }
            }
            float comboDamage = comboStartHealth - comboEndHealth;
            bool comboDamageOk = hitComboSeen && comboDamage >= 20f;
            sb.AppendLine();
            sb.AppendLine($"COMBO3 a 1.1 m  dano_del_combo={comboDamage:F1}  " +
                          $"{(comboDamageOk ? "PASS" : "FAIL")}  (esperado 20-28: 7 impactos de 4)");
            string comboAttacks = "";
            foreach (var a in player.AttackTrace) comboAttacks += a + " ";
            sb.AppendLine("   ataques: " + comboAttacks);
            if (!string.IsNullOrEmpty(comboPositions))
                sb.AppendLine("   posiciones: " + comboPositions);

            // ------------------------------------------------------------ IA
            player.ResetForRound(new Vector3(0f, 0f, 0.2f), 1f);
            enemy.ResetForRound(new Vector3(-2.4f, 0f, 0.2f), 1f);
            // El medidor de súper se conserva entre rondas por diseño. Si la sonda
            // arranca después de unos segundos de pelea, la IA puede tenerlo lleno y
            // pasar la ventana lanzando poderes: se vacía para medir SU ataque normal.
            player.DevResetSuper();
            enemy.DevResetSuper();
            player.ControlsEnabled = false;
            player.DevSetInvulnerable(true);
            enemy.ControlsEnabled = true;
            yield return null;
            yield return null;

            float maxHp = player.Def != null ? player.Def.maxHealth : 100f;
            AttackSpec previous = null;
            int starts = 0, outOfReach = 0, lunging = 0;
            bool damaged = false;
            float window = 12f, elapsed = 0f;
            while (elapsed < window)
            {
                yield return null;
                elapsed += Time.deltaTime;
                if (player.Health < maxHp - 0.01f) damaged = true;
                var atk = enemy.CurrentAttack;
                if (atk != null && !ReferenceEquals(atk, previous))
                {
                    starts++;
                    float distance = Mathf.Abs(enemy.transform.position.x - player.transform.position.x);
                    if (atk.lunge > 0.001f)
                    {
                        // DropKick y LowKick son ataques con avance declarado: el
                        // desplazamiento forma parte del golpe y no se mide como whiff.
                        lunging++;
                    }
                    else
                    {
                        float reach = atk.hitboxForward + atk.hitboxWidth * 0.5f +
                                      player.CurrentHurtboxRadius;
                        if (distance > reach + 0.02f)
                        {
                            outOfReach++;
                            sb.AppendLine($"   FUERA DE ALCANCE: {atk.type} d={distance:F2} m reach={reach:F2} m");
                        }
                    }
                }
                previous = atk;
                if (enemy.IsKO) break;
            }

            bool aiOk = starts > 0 && outOfReach == 0 && damaged;
            sb.AppendLine();
            sb.AppendLine($"IA vs jugador QUIETO  ventana={window:F0} s  ataques={starts}  " +
                          $"con_avance={lunging}  fuera_de_alcance={outOfReach}  " +
                          $"danio={(damaged ? "SI" : "NO")}  {(aiOk ? "PASS" : "FAIL")}");
            sb.AppendLine("   criterio: todo golpe SIN avance debe empezar dentro de su alcance real y el rival debe recibir dano.");

            int fails = (travelOk ? 0 : 1) + (comboDamageOk ? 0 : 1) + (aiOk ? 0 : 1);
            sb.AppendLine();
            sb.AppendLine(fails == 0 ? "VERDICT: PASS" : $"VERDICT: FAIL ({fails} comprobaciones)");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[KOM][BALANCE] {path}\n{sb}");

            player.DevSetInvulnerable(false);
            player.ControlsEnabled = true;
            Destroy(gameObject);
        }
    }
}
#endif
