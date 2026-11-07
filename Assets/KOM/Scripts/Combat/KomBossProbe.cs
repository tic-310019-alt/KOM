#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace KOM
{
    /// <summary>
    /// SONDA DEL JEFE FINAL (solo editor, en Play real). Mide las tres cosas que se
    /// rompieron y que a ojo no se distinguen:
    ///
    ///   1. **Poderes absorbidos.** La jefa tiene que usar el poder de los rivales DERROTADOS
    ///      de esta partida, no una tabla fija. Se registra el catalogo declarado, el filtro
    ///      que aplica `AbsorbSpecials` y, sobre la pelea observada, CUALES USO DE VERDAD.
    ///      Con la seleccion antigua ("el de menor alcance que cubre") solo usaba la guitarra:
    ///      la pelea ocurre a 1,5-3 m y la guitarra cubre 2,6 m, asi que ganaba siempre.
    ///
    ///   2. **El instrumento.** Cada vez que lanza un poder con prop se comprueba FRAME A
    ///      FRAME si el GameObject existe, si tiene renderers activos y si esta pegado a la
    ///      mano. Las tres fallas son distintas y sin esta medicion se confunden:
    ///        · `prop=null`      -> no se instancio (Resources no lo encontro o el special
    ///                             no declara propResource);
    ///        · `0 renderers`    -> se instancio vacio (prefab sin malla);
    ///        · `lejos de la mano` -> se instancio pero el anclaje fallo (hueso nulo, escala
    ///                             mal compensada) y se ve lejos o dentro del cuerpo.
    ///
    ///   3. **Agresividad de la IA.** Fraccion del tiempo con la guardia alzada, ataques
    ///      iniciados y especiales lanzados en la ventana observada. El defecto que se
    ///      corrigio era que la decision defensiva se relanzaba CADA FRAME mientras el rival
    ///      atacaba, asi que `blockChance` no era una probabilidad por ataque sino por frame.
    ///
    /// Uso desde el puente (en Play, con la jefa en pista):
    ///   KOM.KomBossProbe.Spawn().Begin("Logs/boss/report.txt", 40f)
    /// </summary>
    public sealed class KomBossProbe : MonoBehaviour
    {
        public static KomBossProbe Spawn()
        {
            var go = new GameObject("BossProbe");
            return go.AddComponent<KomBossProbe>();
        }

        public void Begin(string path, float windowSeconds = 40f)
        {
            StartCoroutine(Run(path, windowSeconds));
        }

        class PropRecord
        {
            public string type;
            public bool everSeen;
            public int maxRenderers;
            public float minHandDistance = float.MaxValue;
            public float maxBoundsVolume;
            public int framesCasting;
            public string worst;
        }

        // La fase de estres necesita registrar en las mismas listas que la ventana observada.
        readonly List<PropRecord> _propRecords = new List<PropRecord>();
        readonly Dictionary<string, int> _usedTypes = new Dictionary<string, int>();
        Fighter _boss, _player;
        bool _lastCastOk;
        bool _lastCastEntered;

        /// <summary>
        /// Lanza un especial concreto y lo muestrea frame a frame hasta que termina. Deja el
        /// registro en `_propRecords` si el poder usa instrumento y el resultado en
        /// `_lastCastOk`.
        /// </summary>
        IEnumerator CastOnce(Fighter boss, Fighter player, int index, StringBuilder sb)
        {
            _lastCastOk = false;
            var spec = boss.AvailableSpecial(index);
            if (spec == null) yield break;

            // Esperar la VENTANA REAL de lanzamiento: que haya terminado cualquier especial en
            // curso (el de la propia IA cuenta) y que el medidor este lleno. Salirse al primer
            // frame de cast ajeno era el defecto de esta sonda: reportaba "no se pudo lanzar"
            // cuando en realidad habia que esperar.
            float waited = 0f;
            while (waited < 10f)
            {
                boss.DevSetInvulnerable(true);
                player.DevSetInvulnerable(true);
                boss.FillSuperDebug();
                if (!boss.IsCastingSpecial && boss.CanCastSpecial) break;
                waited += Time.deltaTime;
                yield return null;
            }
            if (!boss.CanCastSpecial)
            {
                sb.AppendLine($"      (no se pudo lanzar '{spec.type}': CanCastSpecial=false, " +
                              $"casting={boss.IsCastingSpecial} ctrl={boss.ControlsEnabled} super={boss.SuperReady})");
                yield break;
            }

            PropRecord rec = null;
            if (!string.IsNullOrEmpty(spec.propResource))
            {
                rec = new PropRecord { type = spec.type };
                _propRecords.Add(rec);
            }
            int before = _usedTypes.TryGetValue(spec.type, out var c) ? c : 0;
            _usedTypes[spec.type] = before + 1;

            boss.StartSpecial(index);
            if (!boss.IsCastingSpecial || boss.ActiveSpecial != spec)
            {
                sb.AppendLine($"      {spec.type}: NO arranco (StartSpecial no tomo el poder)");
                yield break;
            }
            // El estado tiene que existir y entrarse: si el Animator no lo tiene, el poder se
            // ve como "no hace nada" y es el mismo sintoma que una seleccion rota.
            var anim = boss.GetComponentInChildren<Animator>();
            bool entered = anim != null && anim.GetCurrentAnimatorStateInfo(CombatTuning.BaseLayer)
                .IsName(spec.animatorState);
            float t = 0f;
            while (boss.IsCastingSpecial && t < 8f)
            {
                t += Time.deltaTime;
                if (anim != null && !entered)
                    entered = anim.GetCurrentAnimatorStateInfo(CombatTuning.BaseLayer).IsName(spec.animatorState);
                if (rec != null)
                {
                    rec.framesCasting++;
                    var prop = boss.ActiveProp;
                    if (prop != null)
                    {
                        rec.everSeen = true;
                        int enabled = 0;
                        Renderer first = null;
                        foreach (var r in prop.GetComponentsInChildren<Renderer>(true))
                        {
                            if (r.enabled && r.gameObject.activeInHierarchy)
                            {
                                enabled++;
                                if (first == null) first = r;
                            }
                        }
                        if (enabled > rec.maxRenderers) rec.maxRenderers = enabled;
                        if (first != null)
                        {
                            var b = first.bounds.size;
                            float vol = b.x * b.y * b.z;
                            if (vol > rec.maxBoundsVolume) rec.maxBoundsVolume = vol;
                        }
                        float dHand = Vector3.Distance(prop.transform.position, boss.LeftPalmPosition);
                        if (dHand < rec.minHandDistance) rec.minHandDistance = dHand;
                    }
                }
                yield return null;
            }
            string propInfo = rec == null ? "sin instrumento" :
                $"prop visto={(rec.everSeen ? "SI" : "NO")} renderers={rec.maxRenderers} " +
                $"mano={(rec.minHandDistance < 9999f ? rec.minHandDistance.ToString("F3") : "-")} m";
            sb.AppendLine($"      {spec.type,-10} estado={spec.animatorState,-12} entro={(entered ? "SI" : "NO")} " +
                          $"duracion={t:F2}s frames={rec?.framesCasting ?? 0} {propInfo}");
            _lastCastOk = entered && (rec == null || (rec.everSeen && rec.maxRenderers > 0 && rec.minHandDistance <= 0.6f));
            _lastCastEntered = entered;
            yield return new WaitForSecondsRealtime(0.35f);
        }

        /// <summary>
        /// Prueba del K.O.: noquea al jugador con el medidor de la jefa lleno y vigila 1,5 s.
        /// El defecto que se reporto ("lanza el guitarrazo al eliminarme, sin razon") era un
        /// especial que ARRANCABA en el frame del K.O., antes de que CombatController le
        /// quitara el control. Se cuenta cada inicio, no cada frame de cast.
        /// </summary>
        public void BeginKoCheck(string path)
        {
            StartCoroutine(KoCheck(path));
        }

        IEnumerator KoCheck(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("KOM JEFE FINAL · prueba del K.O. (no debe lanzar especiales contra un rival tumbado)");
            int fails = 0;

            Fighter boss = null, player = null;
            foreach (var f in FindObjectsByType<Fighter>(FindObjectsInactive.Exclude))
            {
                if (f.IsAI && f.Def != null && f.Def.isBoss) boss = f;
                else if (!f.IsAI) player = f;
            }
            if (boss == null || player == null)
            {
                sb.AppendLine("  FALLA  no hay jefa y jugador en escena.");
            }
            else
            {
                boss.DevSetInvulnerable(true);
                player.DevSetInvulnerable(true);
                boss.FillSuperDebug();

                // Dejar que llegue a una distancia de la que pueda lanzar, y confirmar que
                // CON EL RIVAL VIVO si puede: si no puede, la prueba no demuestra nada.
                float warm = 0f;
                bool couldWhileAlive = false;
                while (warm < 6f && !couldWhileAlive)
                {
                    boss.FillSuperDebug();
                    couldWhileAlive = boss.CanCastSpecial;
                    warm += Time.deltaTime;
                    yield return null;
                }
                sb.AppendLine($"  con el rival vivo: puedeLanzar={couldWhileAlive} (esperado True) super={boss.SuperReady}");
                if (!couldWhileAlive)
                {
                    sb.AppendLine("  AVISO  no se dio la ventana de lanzamiento con el rival vivo; se repite tras 2 s.");
                    yield return new WaitForSecondsRealtime(2f);
                    boss.FillSuperDebug();
                    couldWhileAlive = boss.CanCastSpecial;
                    sb.AppendLine($"  reintento: puedeLanzar={couldWhileAlive}");
                }

                player.DevSetInvulnerable(false);
                player.DevKill();
                bool koNow = player.IsKO;
                int started = 0;
                bool wasCasting = boss.IsCastingSpecial;
                float t = 0f;
                while (t < 1.5f)
                {
                    t += Time.deltaTime;
                    bool now = boss.IsCastingSpecial;
                    if (now && !wasCasting) started++;
                    wasCasting = now;
                    yield return null;
                }
                sb.AppendLine($"  tras el K.O. (rivalKO={koNow}): especiales INICIADOS en 1,5 s = {started}");
                if (started > 0)
                {
                    fails++;
                    sb.AppendLine("  FALLA  la jefa lanzo un especial contra un rival ya noqueado.");
                }
                else sb.AppendLine("  ok     ningun especial contra el rival tumbado");

                // ------- celebracion: el estado Victory nunca puede llevar instrumento.
                // El defecto que vio el usuario ("siempre lanza el guitarrazo al eliminarme")
                // tenia DOS causas distintas y esta linea comprueba la segunda: la jefa
                // celebraba con `Combo`, la animacion de su propio guitarrazo. Como la
                // victoria no pasa por StartSpecial, el prop nunca se instancia y Flavia
                // mimiaba una guitarra invisible.
                float vt = 0f;
                string winState = "-";
                bool propOnWin = false;
                while (vt < 6f)
                {
                    vt += Time.deltaTime;
                    var st = boss.Animator != null ? boss.Animator.GetCurrentAnimatorStateInfo(0) : default;
                    if (st.IsName("Victory"))
                    {
                        winState = "Victory";
                        propOnWin = boss.ActiveProp != null;
                        break;
                    }
                    yield return null;
                }
                sb.AppendLine($"  celebracion: estado={winState} instrumento={((winState == "Victory") ? (propOnWin ? "SI" : "no") : "-")}");
                if (winState != "Victory")
                    sb.AppendLine("  AVISO  la jefa no entro en 'Victory' en 6 s; no se pudo comprobar la celebracion.");
                else if (propOnWin)
                {
                    fails++;
                    sb.AppendLine("  FALLA  la celebracion usa un clip con instrumento: en la victoria el prop no se instancia y el luchador mima un objeto invisible.");
                }
                else sb.AppendLine("  ok     celebracion limpia (sin instrumento)");
            }

            sb.AppendLine("");
            sb.AppendLine(fails == 0 ? "VERDICT: PASS" : $"VERDICT: FAIL ({fails} problemas)");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Logs");
            File.WriteAllText(path, sb.ToString());
            Debug.Log(sb.ToString());
        }

        IEnumerator Run(string path, float windowSeconds)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Logs");
            var sb = new StringBuilder();
            int fails = 0;
            void Fail(string m) { fails++; sb.AppendLine("  FALLA  " + m); }
            void Ok(string m) => sb.AppendLine("  ok     " + m);

            sb.AppendLine("KOM JEFE FINAL · sonda en Play real");

            Fighter boss = null, player = null;
            foreach (var f in FindObjectsByType<Fighter>(FindObjectsInactive.Exclude))
            {
                if (f.IsAI && f.Def != null && f.Def.isBoss) boss = f;
                else if (!f.IsAI) player = f;
            }
            if (boss == null || player == null)
            {
                sb.AppendLine("  FALLA  no hay jefa y jugador en la escena (entrar al ultimo encuentro).");
                sb.AppendLine("");
                sb.AppendLine(fails == 0 ? "VERDICT: PASS" : "VERDICT: FAIL");
                File.WriteAllText(path, sb.ToString());
                Debug.Log($"[KOM][JEFE] {path}");
                yield break;
            }

            // ---------------- 1. que poderes tiene y cuales deberia tener
            var session = GameSession.Ensure();
            var defeated = session.DefeatedOpponents;
            string defeatedTypes = "";
            foreach (var id in defeated)
            {
                var d = KomData.GetCharacter(id);
                defeatedTypes += $"{id}:{(d?.special != null ? d.special.type : "?")} ";
            }
            sb.AppendLine($"  jugador={session.PlayerId} encuentro={session.EncounterIndex} derrotados=[{defeatedTypes.Trim()}]");

            int declared = boss.Def.SpecialCount;
            int available = boss.AvailableSpecialCount;
            string declaredList = "", availableList = "";
            for (int i = 0; i < declared; i++)
            {
                var s = boss.Def.GetSpecial(i);
                declaredList += (s != null ? s.type : "?") + ",";
            }
            for (int i = 0; i < available; i++)
            {
                var s = boss.AvailableSpecial(i);
                availableList += (s != null ? s.type : "?") + ",";
            }
            sb.AppendLine($"  declarados en JSON ({declared}): {declaredList}");
            sb.AppendLine($"  disponibles ahora  ({available}): {availableList}");

            if (declared < 6) Fail($"la jefa declara {declared} poderes en characters.json; se esperaban 6 (beam + 5 absorbibles).");
            // Un derrotado cuyo poder sea del MISMO tipo que el de la jefa no le suma nada
            // (vencer a Juan Grabriel no le da un segundo rayo). Se cuenta por tipo distinto.
            var expectedTypes = new HashSet<string>();
            if (boss.Def.special != null && boss.Def.special.IsCombatSpecial) expectedTypes.Add(boss.Def.special.type);
            foreach (var id in defeated)
            {
                var d = KomData.GetCharacter(id);
                if (d != null && d.special != null && d.special.IsCombatSpecial) expectedTypes.Add(d.special.type);
            }
            if (available != expectedTypes.Count)
                Fail($"poderes disponibles = {available}, se esperaban {expectedTypes.Count} ({string.Join("/", expectedTypes)}).");
            else Ok($"{available} poderes disponibles = el propio + los derrotados con tipo nuevo ({string.Join("/", expectedTypes)})");

            // Cada estado declarado tiene que EXISTIR en su controller: si falta, la jefa no
            // puede lanzar ese poder y el sintoma es idéntico al de "siempre usa lo mismo".
            var anim = boss.GetComponentInChildren<Animator>();
            var missingStates = new List<string>();
            for (int i = 0; i < available; i++)
            {
                var s = boss.AvailableSpecial(i);
                if (s == null || string.IsNullOrEmpty(s.animatorState)) continue;
                if (anim == null || !anim.HasState(CombatTuning.BaseLayer, Animator.StringToHash(s.animatorState)))
                    missingStates.Add($"{s.type}->{s.animatorState}");
            }
            if (missingStates.Count > 0)
                Fail($"estados del Animator que faltan: {string.Join(", ", missingStates)}");
            else Ok("los " + available + " estados de especial existen en el AnimatorController");

            // ---------------- 2. observacion de la pelea
            var combat = boss.Combat;
            if (combat != null) combat.PickupsEnabled = false;
            foreach (var p in FindObjectsByType<CombatPickup>(FindObjectsInactive.Exclude))
                Destroy(p.gameObject);

            // Esperar a que la ronda este en curso: durante la panoramica de entrada los
            // luchadores no tienen control y la ventana mediria una pantalla de presentacion.
            float waited = 0f;
            while (waited < 12f && (!boss.ControlsEnabled || !player.ControlsEnabled))
            {
                waited += Time.deltaTime;
                yield return null;
            }
            if (!boss.ControlsEnabled)
            {
                Fail("la ronda no llego a estar en curso en 12 s: nada que medir.");
                sb.AppendLine("");
                sb.AppendLine($"VERDICT: FAIL ({fails} problemas)");
                File.WriteAllText(path, sb.ToString());
                Debug.Log(sb.ToString());
                yield break;
            }

            // El jugador observado no se defiende: sin esto la jefa (ahora agresiva de
            // verdad) lo noquea en segundos y la ventana se corta sin ver los poderes. El
            // modo invulnerable deja registrar el golpe, el stun y el medidor: la IA sigue
            // peleando contra algo real, solo que no muere.
            player.DevSetInvulnerable(true);
            boss.DevSetInvulnerable(true);

            var usedTypes = _usedTypes;
            var propRecords = _propRecords;
            PropRecord currentProp = null;
            int blockFrames = 0, totalFrames = 0, bossAttacks = 0, specialsFired = 0;
            bool wasAttacking = false;
            bool wasCasting = false;
            float t = 0f;
            var attackSpec = Attacks.FromButton(AttackButton.HeavyPunch);

            float attackEvery = 1.1f, nextAttack = 0.6f;

            while (t < windowSeconds && !boss.IsKO)
            {
                float dt = Time.deltaTime;
                t += dt;
                totalFrames++;

                // El jugador observado no se defiende: sin esto la jefa (ahora agresiva de
                // verdad) lo noquea en segundos y la ventana se corta sin ver los poderes.

                // El jugador no es un saco: ataca para que la jefa tenga que decidir. Sin
                // ataques entrantes la rama defensiva no se ejercita y la medicion de
                // agresividad no diria nada.
                if (t >= nextAttack && !player.IsAttacking && !player.IsCastingSpecial && player.ControlsEnabled)
                {
                    player.StartAttack(attackSpec);
                    nextAttack = t + attackEvery;
                }

                if (boss.IsBlocking) blockFrames++;
                if (boss.IsAttacking && !wasAttacking) bossAttacks++;
                wasAttacking = boss.IsAttacking;

                if (boss.IsCastingSpecial && !wasCasting)
                {
                    specialsFired++;
                    var s = boss.ActiveSpecial;
                    string ty = s != null ? s.type : "?";
                    usedTypes.TryGetValue(ty, out var c);
                    usedTypes[ty] = c + 1;
                    if (s != null && !string.IsNullOrEmpty(s.propResource))
                    {
                        currentProp = new PropRecord { type = ty };
                        propRecords.Add(currentProp);
                    }
                }
                if (!boss.IsCastingSpecial) { currentProp = null; }
                wasCasting = boss.IsCastingSpecial;

                if (currentProp != null)
                {
                    currentProp.framesCasting++;
                    var prop = boss.ActiveProp;
                    if (prop != null)
                    {
                        currentProp.everSeen = true;
                        int enabled = 0;
                        foreach (var r in prop.GetComponentsInChildren<Renderer>(true))
                            if (r.enabled && r.gameObject.activeInHierarchy) enabled++;
                        if (enabled > currentProp.maxRenderers) currentProp.maxRenderers = enabled;
                        var b = prop.GetComponentInChildren<Renderer>() != null
                            ? prop.GetComponentInChildren<Renderer>().bounds.size
                            : Vector3.zero;
                        float vol = b.x * b.y * b.z;
                        if (vol > currentProp.maxBoundsVolume) currentProp.maxBoundsVolume = vol;
                        float dHand = Vector3.Distance(prop.transform.position, boss.LeftPalmPosition);
                        if (dHand < currentProp.minHandDistance) currentProp.minHandDistance = dHand;
                    }
                }

                yield return null;
            }

            sb.AppendLine($"  ventana observada: {t:F1} s ({totalFrames} frames), window={windowSeconds:F0}s");
            sb.AppendLine("");
            sb.AppendLine("  --- especiales lanzados por la IA en la ventana ---");
            string used = "";
            foreach (var kv in usedTypes) used += $"{kv.Key}x{kv.Value} ";
            sb.AppendLine("      " + (used.Trim().Length == 0 ? "(ninguno)" : used.Trim()));
            // La ventana natural no garantiza ver TODOS los poderes: el medidor tarda y la IA
            // elige el momento. Lo que se exige es que lance AL MENOS uno y que la cobertura
            // completa se comprueba abajo, forzando cada indice.
            if (usedTypes.Count == 0 && t >= 20f)
                Fail("la jefa no lanzo NINGUN especial en " + t.ToString("F0") + " s con el medidor lleno.");

            sb.AppendLine("");
            sb.AppendLine("  --- cobertura forzada de los poderes disponibles ---");
            // Cada poder disponible se lanza una vez, con el rival puesto a su alcance. Se
            // comprueba tres cosas por poder: que el Animator ENTRE en su estado (si falta el
            // estado en el controller, el sintoma exterior es "no hace nada"), que el especial
            // dure su `duration` y que el instrumento aparezca si el poder lo declara.
            var covered = new List<string>();
            for (int i = 0; i < boss.AvailableSpecialCount; i++)
            {
                var s = boss.AvailableSpecial(i);
                if (s == null || !s.IsCombatSpecial) continue;
                var pp = player.transform.position;
                pp.x = boss.transform.position.x + (boss.transform.position.x < 0f ? 1.9f : -1.9f);
                player.transform.position = pp;
                yield return CastOnce(boss, player, i, sb);
                if (_lastCastOk) covered.Add(s.type);
            }
            if (covered.Count != boss.AvailableSpecialCount)
                Fail($"solo se pudieron lanzar {covered.Count} de {boss.AvailableSpecialCount} poderes: {string.Join(",", covered)}");
            else Ok($"{covered.Count} poderes forzados y lanzados: {string.Join(",", covered)}");

            sb.AppendLine("");
            sb.AppendLine("  --- instrumentos (prop) ---");
            if (propRecords.Count == 0)
            {
                sb.AppendLine("  (sin observacion: no lanzo ningun poder con instrumento en la ventana)");
            }
            foreach (var r in propRecords)
            {
                string line = $"{r.type}: frames={r.framesCasting} visto={(r.everSeen ? "SI" : "NO")} " +
                              $"renderers={r.maxRenderers} volumen={r.maxBoundsVolume:F4} " +
                              $"mano={r.minHandDistance:F3} m";
                sb.AppendLine("      " + line);
                if (!r.everSeen) { Fail($"'{r.type}': el prop NUNCA existio durante la carga."); }
                else if (r.maxRenderers == 0) { Fail($"'{r.type}': el prop existe pero no pinta (0 renderers activos)."); }
                else if (r.minHandDistance > 0.6f) { Fail($"'{r.type}': el prop llega a estar a {r.minHandDistance:F2} m de la mano: mal anclado."); }
            }

            sb.AppendLine("");
            sb.AppendLine("  --- estrés del instrumento (guitarra) ---");
            // La rotacion natural no garantiza suficientes guitarrazos, y el defecto que se
            // reporto era INTERMITENTE. Se fuerza el poder de Alex varias veces, en las tres
            // situaciones que pueden perder el prop:
            //   · cast repetido (el DestroyProp del inicio tiene que limpiar el anterior);
            //   · detras de otro poder con prop (acordeon en vuelo: el Fighter cede su
            //     propiedad al BoomerangFx y el siguiente prop no puede heredar ese estado);
            //   · despues de que le den durante la carga (CancelSpecial si no limpia, el
            //     siguiente especial nace con el prop viejo o nulo).
            int clubIdx = -1;
            for (int i = 0; i < boss.AvailableSpecialCount; i++)
            {
                var s = boss.AvailableSpecial(i);
                if (s != null && s.type == "club") { clubIdx = i; break; }
            }
            int propIdx = -1;
            for (int i = 0; i < boss.AvailableSpecialCount; i++)
            {
                var s = boss.AvailableSpecial(i);
                if (s != null && s.type == "boomerang") { propIdx = i; break; }
            }
            if (clubIdx < 0)
            {
                sb.AppendLine("      (no force guitarrazo: Alex Llora no esta entre los derrotados de esta partida)");
            }
            else
            {
                int guitarFails = 0, guitarCasts = 0;
                // Se cuentan los guitarrazos que DE VERDAD entraron en su estado, no los
                // registros de prop: si la ronda se acaba a mitad de un intento, el cast no
                // entra y contar su prop como "guitarrazo superado" infla la cifra.
                void CountClub()
                {
                    if (!_lastCastEntered) return;
                    guitarCasts++;
                    if (!_lastCastOk) guitarFails++;
                }
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    // El jugador noquea por tiempo si gana las rondas: con la pelea terminada la
                    // jefa ya no está en escena y seguir midiendo no significa nada.
                    if (boss == null || player == null)
                    {
                        sb.AppendLine("      (la pelea termino y se destruyo los luchadores: se detiene el estres)");
                        break;
                    }
                    // La ronda tiene su reloj de 60 s y el estres va al final de la sonda, así
                    // que casi siempre llega con la ronda agotada. Esperar a la SIGUIENTE ronda
                    // en lugar de parar: si no, la fase quedaba en 2-3 intentos y el recuento
                    // dependía de cuánto hubiera consumido la ventana natural.
                    float rearm = 0f;
                    while (!boss.ControlsEnabled && rearm < 9f)
                    {
                        if (rearm == 0f) sb.AppendLine("      (ronda agotada: se espera a la siguiente)");
                        rearm += Time.deltaTime;
                        yield return null;
                    }
                    if (!boss.ControlsEnabled)
                    {
                        sb.AppendLine($"      (se detiene el estres tras {attempt} intentos: no vuelve a haber ronda activa)");
                        break;
                    }
                    // Colocar al rival dentro del alcance del guitarrazo (2,6 m).
                    var pp = player.transform.position;
                    pp.x = boss.transform.position.x + (boss.transform.position.x < 0f ? 2.2f : -2.2f);
                    player.transform.position = pp;

                    if (propIdx >= 0 && attempt % 3 == 2)
                        yield return CastOnce(boss, player, propIdx, sb);
                    yield return CastOnce(boss, player, clubIdx, sb);
                    CountClub();
                    if (attempt % 4 == 3 && boss.IsCastingSpecial == false)
                    {
                        // Interrumpir con un golpe y volver a intentar: el camino de CANCEL.
                        player.StartAttack(attackSpec);
                        yield return new WaitForSecondsRealtime(0.25f);
                        yield return CastOnce(boss, player, clubIdx, sb);
                        CountClub();
                    }
                }
                if (guitarCasts < 2)
                    Fail($"solo {guitarCasts} guitarrazos llegaron a entrar en su estado (la ronda dura 60 s y el estres necesita al menos 2).");
                else if (guitarFails > 0) Fail($"{guitarFails}/{guitarCasts} guitarrazos sin instrumento visible y anclado a la mano.");
                else Ok($"{guitarCasts} guitarrazos forzados: la guitarra aparecio y estuvo sujeta en todos");
            }

            sb.AppendLine("");
            sb.AppendLine("  --- IA ---");
            float blockFraction = totalFrames > 0 ? blockFrames / (float)totalFrames : 0f;
            sb.AppendLine($"  guardia={blockFraction * 100f:F1}% del tiempo  ataquesIniciados={bossAttacks}  especiales={specialsFired}");
            // Con el rodillo por frame, la jefa pasaba la mayoria del asalto con la guardia
            // alzada. 35 % es holgado para un jefe que bloquea un tercio de lo que recibe.
            if (blockFraction > 0.35f)
                Fail($"la jefa pasa el {blockFraction * 100f:F1}% del tiempo en guardia: sigue demasiado defensiva.");
            else Ok($"guardia solo el {blockFraction * 100f:F1}% del tiempo");
            if (bossAttacks < 6) Fail($"solo inicio {bossAttacks} ataques en {t:F0} s: le falta presión.");
            else Ok($"{bossAttacks} ataques iniciados");

            sb.AppendLine("");
            sb.AppendLine(fails == 0 ? "VERDICT: PASS" : $"VERDICT: FAIL ({fails} problemas)");
            File.WriteAllText(path, sb.ToString());
            Debug.Log(sb.ToString());
        }
    }
}
#endif
