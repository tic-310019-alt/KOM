#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace KOM
{
    // SONDA DE SENSACION (solo editor). `KomCombatProbe` comprueba que los botones
    // lanzan SU ataque; esto comprueba que el combate se SIENTE bien, con numeros:
    //
    //   1. la ventana real de cada ataque coincide con AttackSpec.Window,
    //   2. el hitstun del rival coincide con AttackSpec.hitStun,
    //   3. el hitstop congela a los DOS (Animator a 0) y los suelta,
    //   4. una orden pulsada durante la recuperacion NO se pierde,
    //   5. el empuje se reparte en varios frames (no es un teletransporte),
    //   6. el frame advantage declarado (hitstun - ventana) es el que se mide.
    //
    //   KOM.KomFeelProbe.Spawn().Begin("Logs/combat/feel.txt")
    public sealed class KomFeelProbe : MonoBehaviour
    {
        struct Row
        {
            public string label;
            public bool ok;
            public string detail;
        }

        public static KomFeelProbe Spawn()
        {
            var go = new GameObject("FeelProbe");
            return go.AddComponent<KomFeelProbe>();
        }

        public void Begin(string path)
        {
            StartCoroutine(Run(path));
        }

        IEnumerator Run(string path)
        {
            Directory.CreateDirectory("Logs/combat");
            var sb = new StringBuilder();
            var rows = new List<Row>();
            sb.AppendLine("KOM SENSACION · frame data real medido en Play");
            sb.AppendLine();

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

            // --- 1. Ventanas reales de ataque (sin contacto: nada de hitstop) ---------
            sb.AppendLine("--- ventana real del ataque (spec.Window) ---");
            foreach (var spec in Attacks.All)
            {
                yield return MeasureWindow(player, enemy, spec, rows, sb);
            }
            sb.AppendLine();

            // --- 2/3/5. Hitstun, hitstop y empuje -----------------------------------
            sb.AppendLine("--- impacto: hitstun, hitstop y empuje ---");
            yield return MeasureImpact(player, enemy, Attacks.LightPunch, rows, sb);
            yield return MeasureImpact(player, enemy, Attacks.HeavyPunch, rows, sb);
            yield return MeasureImpact(player, enemy, Attacks.HeavyKick, rows, sb);
            sb.AppendLine();

            // --- 4. Buffer que sobrevive al ataque ----------------------------------
            sb.AppendLine("--- buffer de entrada ---");
            yield return MeasureBuffer(player, enemy, rows, sb);
            sb.AppendLine();

            // --- 6. Frame advantage declarado ---------------------------------------
            // Se mide contra el ULTIMO golpe del ataque, no contra su inicio: en un
            // combo de 7 impactos la ventaja depende de donde cae el ultimo.
            sb.AppendLine("--- frame advantage declarado (ultimo golpe + hitstun - ventana) ---");
            foreach (var spec in Attacks.All)
            {
                float lastHit = spec.HitTimeAt(spec.hitCount - 1);
                float adv = lastHit + spec.hitStun - spec.Window;
                sb.AppendLine($"{spec.type,-12} ultimo={lastHit:F3}  ventana={spec.Window:F3}  " +
                              $"hitstun={spec.hitStun:F3}  adv={adv * 60f:+0.0;-0.0;0.0} frames");
            }
            sb.AppendLine();

            int fails = 0;
            sb.AppendLine("--- resumen ---");
            foreach (var r in rows)
            {
                if (!r.ok) fails++;
                sb.AppendLine($"{(r.ok ? "PASS" : "FAIL")}  {r.label}  {r.detail}");
            }
            sb.AppendLine();
            sb.AppendLine(fails == 0 ? "VERDICT: PASS" : $"VERDICT: FAIL ({fails} comprobaciones)");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[KOM][FEEL] {path}");
            Destroy(gameObject);
        }

        static void Place(Fighter a, Fighter b, float distance)
        {
            a.transform.position = new Vector3(-distance * 0.5f, 0f, 0.2f);
            b.transform.position = new Vector3(distance * 0.5f, 0f, 0.2f);
        }

        IEnumerator MeasureWindow(Fighter p, Fighter e, AttackSpec spec, List<Row> rows, StringBuilder sb)
        {
            // Lejos: el ataque no conecta, asi que el hitstop no alarga la medida.
            Place(p, e, 9f);
            yield return null;
            p.DebugResetCombatState();
            e.DebugResetCombatState();
            p.ControlsEnabled = true;
            yield return null;

            p.StartAttack(spec);
            float t0 = Time.time;
            float guard = 0f;
            while (p.IsAttacking && guard < 8f)
            {
                guard += Time.deltaTime;
                yield return null;
            }
            float real = Time.time - t0;
            float err = Mathf.Abs(real - spec.Window);
            bool ok = err <= 0.07f;
            rows.Add(new Row { label = $"ventana {spec.type}", ok = ok, detail = $"real={real:F3} spec={spec.Window:F3} err={err:F3}" });
            sb.AppendLine($"{spec.type,-12} real={real:F3}  spec={spec.Window:F3}  err={err:F3}  {(ok ? "PASS" : "FAIL")}");
        }

        IEnumerator MeasureImpact(Fighter p, Fighter e, AttackSpec spec, List<Row> rows, StringBuilder sb)
        {
            Place(p, e, 1.15f);
            yield return null;
            p.DebugResetCombatState();
            e.DebugResetCombatState();
            p.ControlsEnabled = true;
            e.ControlsEnabled = false;
            yield return null;

            float hpBefore = e.Health;
            p.StartAttack(spec);

            // Esperar al impacto.
            float guard = 0f;
            while (e.Health >= hpBefore && guard < 3f)
            {
                guard += Time.deltaTime;
                yield return null;
            }
            bool hit = e.Health < hpBefore;
            float t0 = Time.time;
            float frozenTime = 0f;
            bool sawFrozen = false;
            bool sawAnimatorZero = false;
            bool sawAttackerFrozen = false;
            float startX = e.transform.position.x;
            float prevX = startX;
            float maxStep = 0f;
            int movedFrames = 0;

            while (e.IsHit && guard < 10f)
            {
                guard += Time.deltaTime;
                if (e.IsFrozen)
                {
                    frozenTime += Time.deltaTime;
                    sawFrozen = true;
                    if (e.Animator != null && e.Animator.speed <= 0.0001f) sawAnimatorZero = true;
                    if (p.IsFrozen) sawAttackerFrozen = true;
                }
                float x = e.transform.position.x;
                float step = Mathf.Abs(x - prevX);
                if (step > 0.0001f) movedFrames++;
                if (step > maxStep) maxStep = step;
                prevX = x;
                yield return null;
            }

            float real = Time.time - t0;
            float active = real - frozenTime;
            float totalPush = Mathf.Abs(e.transform.position.x - startX);

            bool hitOk = hit;
            bool stunOk = hit && Mathf.Abs(active - spec.hitStun) <= 0.09f;
            bool freezeOk = sawFrozen && sawAnimatorZero && sawAttackerFrozen;
            bool releaseOk = !e.IsFrozen && e.Animator != null && e.Animator.speed > 0.5f;
            // El empuje tiene que repartirse: si todo cae en un frame es un teletransporte.
            bool pushOk = totalPush > 0.03f && movedFrames >= 2 && maxStep < totalPush * 0.85f;

            rows.Add(new Row { label = $"{spec.type} conecta", ok = hitOk, detail = hit ? "si" : "NO" });
            rows.Add(new Row { label = $"{spec.type} hitstun", ok = stunOk, detail = $"activo={active:F3} spec={spec.hitStun:F3}" });
            rows.Add(new Row { label = $"{spec.type} hitstop congela ambos", ok = freezeOk, detail = $"frozen={sawFrozen} anim0={sawAnimatorZero} atacante={sawAttackerFrozen}" });
            rows.Add(new Row { label = $"{spec.type} suelta tras el hitstop", ok = releaseOk, detail = $"frozen={e.IsFrozen} speed={(e.Animator != null ? e.Animator.speed : -1f):F2}" });
            rows.Add(new Row { label = $"{spec.type} empuje progresivo", ok = pushOk, detail = $"total={totalPush:F3} frames={movedFrames} maxStep={maxStep:F3}" });

            sb.AppendLine($"{spec.type,-12} hit={hit}  hitstun_real={active:F3}/{spec.hitStun:F3}  " +
                          $"freeze={sawFrozen}/{sawAnimatorZero}/{sawAttackerFrozen}  " +
                          $"empuje={totalPush:F3} en {movedFrames} frames  " +
                          $"{(hitOk && stunOk && freezeOk && releaseOk && pushOk ? "PASS" : "FAIL")}");
        }

        IEnumerator MeasureBuffer(Fighter p, Fighter e, List<Row> rows, StringBuilder sb)
        {
            Place(p, e, 9f);
            yield return null;
            p.DebugResetCombatState();
            e.DebugResetCombatState();
            p.ControlsEnabled = true;
            yield return null;

            // Se lanza un golpe fuerte y, a mitad de su recuperacion, se pulsa otro.
            // Antes el buffer caducaba a los 0.18 s y la orden se perdia.
            p.StartAttack(Attacks.HeavyPunch);
            yield return new WaitForSeconds(0.30f);
            p.HandleAttackPress(AttackButton.LightPunch);

            float guard = 0f;
            while (guard < 2.5f)
            {
                guard += Time.deltaTime;
                yield return null;
            }

            bool found = false;
            foreach (var entry in p.AttackTrace)
                if (entry.Contains("LightPunch")) found = true;

            rows.Add(new Row { label = "buffer sobrevive a la recuperacion", ok = found, detail = found ? "LightPunch ejecutado" : "orden perdida" });
            sb.AppendLine($"pulsacion a 0.30 s de un HeavyPunch -> {(found ? "PASS (se ejecuta)" : "FAIL (se pierde)")}");
            sb.AppendLine($"traza: {string.Join("  ", p.AttackTrace)}");

            // ------------------------------------------------------------------
            // La otra cara del buffer: lo que llega DEMASIADO pronto se descarta.
            // Antes se guardaba y salia casi un segundo despues, que es la cola que
            // el jugador percibe como "se me quedo pegada la tecla".
            // ------------------------------------------------------------------
            yield return ResetForBuffer(p, e);
            p.StartAttack(Attacks.HeavyPunch);          // 0.731 s de ventana
            yield return new WaitForSeconds(0.10f);
            p.HandleAttackPress(AttackButton.LightPunch);
            yield return new WaitForSeconds(1.20f);

            bool earlyLeaked = false;
            foreach (var entry in p.AttackTrace)
                if (entry.Contains("LightPunch")) earlyLeaked = true;

            rows.Add(new Row
            {
                label = "pulsacion demasiado pronto se descarta",
                ok = !earlyLeaked,
                detail = earlyLeaked ? "salio tarde (cola)" : "descartada"
            });
            sb.AppendLine($"pulsacion a 0.10 s de un HeavyPunch -> {(earlyLeaked ? "FAIL (sale tarde)" : "PASS (se descarta)")}");
            sb.AppendLine($"traza: {string.Join("  ", p.AttackTrace)}");

            // ------------------------------------------------------------------
            // El ultimo input gana sobre un combo ya reconocido: G G G deja el combo
            // pendiente y una H inmediatamente despues lo tira. Es el caso que el
            // jugador describe como "quiero golpear con otra y no me deja".
            // ------------------------------------------------------------------
            yield return ResetForBuffer(p, e);
            p.StartAttack(Attacks.LightPunch);
            yield return new WaitForSeconds(0.02f);
            p.HandleAttackPress(AttackButton.LightPunch);
            yield return new WaitForSeconds(0.08f);
            p.HandleAttackPress(AttackButton.LightPunch);
            yield return new WaitForSeconds(0.08f);
            p.HandleAttackPress(AttackButton.LightPunch);   // G G G -> Combo1 pendiente
            yield return new WaitForSeconds(0.02f);
            p.HandleAttackPress(AttackButton.HeavyPunch);   // cambio de idea
            yield return new WaitForSeconds(1.20f);

            bool comboLeaked = false, heavyCame = false;
            foreach (var entry in p.AttackTrace)
            {
                if (entry.Contains("Combo1")) comboLeaked = true;
                if (entry.Contains("HeavyPunch")) heavyCame = true;
            }

            rows.Add(new Row
            {
                label = "ultima tecla gana al combo pendiente",
                ok = heavyCame && !comboLeaked,
                detail = heavyCame ? (comboLeaked ? "salieron los dos" : "solo HeavyPunch") : "no salio nada"
            });
            sb.AppendLine($"G G G + H -> heavy={(heavyCame ? "si" : "no")} combo={(comboLeaked ? "si" : "no")} " +
                          $"{(heavyCame && !comboLeaked ? "PASS" : "FAIL")}");
            sb.AppendLine($"traza: {string.Join("  ", p.AttackTrace)}");

            // ------------------------------------------------------------------
            // Machacar cinco veces el MISMO boton no apila ordenes: un combo y nada mas.
            // ------------------------------------------------------------------
            yield return ResetForBuffer(p, e);
            p.DebugRunSequence("11111", 0.10f);
            yield return new WaitForSeconds(2.60f);

            int combos = 0, total = 0;
            foreach (var entry in p.AttackTrace)
            {
                total++;
                if (entry.Contains("Combo1")) combos++;
            }

            bool mashOk = combos == 1 && total <= 3;
            rows.Add(new Row
            {
                label = "mash de 5 no encola",
                ok = mashOk,
                detail = $"ataques={total} combos={combos}"
            });
            sb.AppendLine($"G x5 -> ataques={total} combos={combos} {(mashOk ? "PASS" : "FAIL")}");
            sb.AppendLine($"traza: {string.Join("  ", p.AttackTrace)}");
        }

        // Estado limpio y traza vacia para la siguiente medicion del buffer.
        static IEnumerator ResetForBuffer(Fighter p, Fighter e)
        {
            p.DebugResetCombatState();
            e.DebugResetCombatState();
            p.ControlsEnabled = true;
            yield return null;
            p.ClearAttackTrace();
            yield return null;
        }
    }
}
#endif
