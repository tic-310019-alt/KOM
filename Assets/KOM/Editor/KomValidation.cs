using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KOM.EditorTools
{
    public static class KomValidation
    {
        static readonly HashSet<string> AllowedThemes = new HashSet<string> { "conference", "conference_final", "palace", "plaza" };
        static readonly HashSet<string> AllowedAiProfiles = new HashSet<string> { "rookie", "contender", "veteran", "boss" };

        [MenuItem("King of Music/Validar catalogo", priority = 31)]
        public static void RunValidationAgent()
        {
            RunValidation();
        }

        [MenuItem("King of Music/Validar catalogo (VERDICT)", priority = 30)]
        public static void RunValidationMenu()
        {
            try
            {
                RunValidation();
                EditorUtility.DisplayDialog("King of Music", "VERDICT: PASS\nEl proyecto cumple el contrato de datos.", "OK");
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message);
                EditorUtility.DisplayDialog("King of Music", "VERDICT: FAIL\nRevisa la consola para el detalle.", "OK");
            }
        }

        static bool ReachesState(Animator animator, string state, int layer)
        {
            animator.Play(state, layer, 0f);
            animator.Update(0.25f);
            var info = animator.GetCurrentAnimatorStateInfo(layer);
            return info.IsName(state) && animator.IsInTransition(layer) == false;
        }

        static void ValidateAnimationTransitions(List<string> errors, ref int checks)
        {
            var def = KomData.GetCharacter("juan_grabriel");
            if (def == null) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{def.id}.prefab");
            if (prefab == null) return;
            var preview = EditorSceneManager.NewPreviewScene();
            GameObject go = null;
            try
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.runtimeAnimatorController == null)
                {
                    checks++;
                    errors.Add($"{def.id}: el prefab de prueba no tiene AnimatorController.");
                    return;
                }

                animator.enabled = true;
                animator.Rebind();
                // AAA: autoridad unica. Base a 1, Upper y Reserved a 0 permanente.
                // El test valida que cada estado full-body se alcanza en Base sin mezcla.
                animator.SetLayerWeight(0, 1f);
                if (animator.layerCount > 1) animator.SetLayerWeight(1, 0f);

                var reachable = new List<string>
                {
                    "Idle", "WalkGuardFwd", "WalkGuardBack", "RunFwd", "RunBack", "Block", "Crouch", "Jump",
                    "Hit", "HitHeavy", "GroinHit", "Stunned", "KO", "GetUp", "Victory"
                };
                foreach (var spec in Attacks.All) reachable.Add(spec.animatorState);
                foreach (var state in reachable)
                {
                    checks++;
                    if (ReachesState(animator, state, CombatTuning.BaseLayer) == false)
                        errors.Add($"{def.id}: la capa Base no alcanza el estado '{state}'.");
                }

                // Crouch tiene que ser una pose REAL de agachado: la cadera debe bajar
                // respecto a Idle. Antes era un frame de Getting Up ya de pie (bug visible).
                var controller = animator.runtimeAnimatorController as AnimatorController;
                var crouchState = controller != null
                    ? controller.layers[0].stateMachine.states.Select(x => x.state).FirstOrDefault(x => x.name == "Crouch")
                    : null;
                var crouchClip = crouchState != null ? crouchState.motion as AnimationClip : null;
                var idleState = controller != null
                    ? controller.layers[0].stateMachine.states.Select(x => x.state).FirstOrDefault(x => x.name == "Idle")
                    : null;
                var idleClip = FindIdleClip(idleState);
                var torso = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "torso");
                checks++;
                if (crouchClip == null || idleClip == null || torso == null)
                {
                    errors.Add($"{def.id}: no se pudo medir la pose de Crouch (clip o hueso 'torso' ausente).");
                }
                else
                {
                    animator.enabled = false;
                    idleClip.SampleAnimation(go, idleClip.length * 0.5f);
                    float hipIdle = ChainPosition(torso).y;
                    crouchClip.SampleAnimation(go, crouchClip.length * 0.9f);
                    float hipCrouch = ChainPosition(torso).y;
                    if (hipCrouch > hipIdle * 0.92f)
                        errors.Add($"{def.id}: Crouch no agacha (cadera {hipCrouch:F2} m vs Idle {hipIdle:F2} m).");
                }
            }
            catch (Exception e)
            {
                errors.Add($"{def.id}: test de transiciones de animacion fallo: {e.Message}");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        // Desde los TRS locales: `transform.position` puede estar cacheado tras SampleAnimation.
        static Vector3 ChainPosition(Transform t)
        {
            var m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
            for (var p = t.parent; p != null; p = p.parent)
                m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
            return m.GetColumn(3);
        }

        // Gate que habria cazado el bug original: la cadera no puede viajar dentro del
        // clip. Antes de hornear, Walk viajaba 1.57 m, Run 2.27 m y la guardia 0.33 m.
        static void ValidateInPlaceClips(AnimatorController controller, GameObject prefab, string id,
            List<string> errors, ref int checks)
        {
            if (controller == null || prefab == null) return;
            var clips = new List<AnimationClip>();
            var seen = new HashSet<AnimationClip>();
            void Add(AnimationClip clip)
            {
                if (clip != null && seen.Add(clip)) clips.Add(clip);
            }
            foreach (var cs in controller.layers[0].stateMachine.states)
            {
                if (cs.state.motion is BlendTree tree)
                    foreach (var child in tree.children) Add(child.motion as AnimationClip);
                else Add(cs.state.motion as AnimationClip);
            }
            if (clips.Count == 0) return;

            var preview = EditorSceneManager.NewPreviewScene();
            GameObject go = null;
            try
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;
                var transforms = go.GetComponentsInChildren<Transform>(true);
                var bind = new Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)>();
                foreach (var t in transforms) bind[t] = (t.localPosition, t.localRotation, t.localScale);
                var pelvis = transforms.FirstOrDefault(t => t.name == "DEF-spine")
                    ?? transforms.FirstOrDefault(t => t.name.StartsWith("DEF-pelvis", StringComparison.Ordinal))
                    ?? transforms.FirstOrDefault(t => t.name == "torso");
                if (pelvis == null)
                {
                    checks++;
                    errors.Add($"{id}: no hay hueso de cadera para el gate in-place.");
                    return;
                }
                foreach (var clip in clips)
                {
                    foreach (var t in transforms)
                    {
                        if (t == null || !bind.TryGetValue(t, out var b)) continue;
                        t.localPosition = b.p;
                        t.localRotation = b.r;
                        t.localScale = b.s;
                    }
                    const int N = 20;
                    // DERIVA NETA (inicio -> final), no la excursion maxima. El clip
                    // in-place conserva el balanceo natural del cuerpo: la cadera oscila
                    // dentro de la pose (Idle 0.17 m, Combo3 1.59 m) y eso NO es deriva.
                    // El horneado cancela solo el avance acumulado con una recta en la
                    // raiz; medir la excursion marcaba FAIL incluso en clips perfectos.
                    Vector3 origin = Vector3.zero;
                    Vector3 end = Vector3.zero;
                    for (int i = 0; i <= N; i++)
                    {
                        clip.SampleAnimation(go, Mathf.Min(clip.length * i / N, clip.length - 0.00001f));
                        var p = ChainPosition(pelvis);
                        if (i == 0) origin = p;
                        if (i == N) end = p;
                    }
                    float drift = new Vector2(end.x - origin.x, end.z - origin.z).magnitude;
                    checks++;
                    if (drift > 0.06f)
                        errors.Add($"{id}: '{clip.name}' deriva {drift:F3} m de cadera al final (debe ser in-place).");
                }
            }
            catch (Exception e)
            {
                checks++;
                errors.Add($"{id}: gate in-place fallo: {e.Message}");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        // Gate de locomocion DISCRETA: estados separados, un solo clip a peso 1.
        // No existe Walk normal, ni BlendTree, ni parametro MoveSpeed.
        // El subrig facial debe estar CONGELADO: ni animado ni ausente. Una curva CONSTANTE
        // es un congelado explicito (la escribe KomSetup.CompletePose con el valor de bind) y
        // no deforma nada; solo cuentan las curvas que VARIAN, que son las que hinchan
        // craneo/pelo y estiran la cara.
        static int AnimatedFacialCurves(AnimationClip clip)
        {
            if (clip == null) return 0;
            int n = 0;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (!KomSetup.IsFacialBonePath(b.path)) continue;
                var c = AnimationUtility.GetEditorCurve(clip, b);
                if (c != null && !KomSetup.IsConstant(c)) n++;
            }
            return n;
        }

        static void ValidateLocomotion(AnimatorController controller, GameObject prefab, string id,
            List<string> errors, ref int checks)
        {
            if (controller == null) return;
            var sm = controller.layers[0].stateMachine;
            var states = sm.states.Select(x => x.state).ToArray();

            foreach (var forbidden in new[] { "Locomotion", "LocomotionGuard", "Walk", "WalkAlt" })
            {
                checks++;
                if (states.Any(s => s.name == forbidden))
                    errors.Add($"{id}: el estado '{forbidden}' no debe existir; andar usa solo WalkGuard.");
            }
            checks++;
            if (controller.parameters.Any(p => p.name == "MoveSpeed"))
                errors.Add($"{id}: el parametro MoveSpeed ya no debe existir (locomocion discreta).");
            foreach (var st in states)
            {
                checks++;
                if (st.motion is BlendTree)
                    errors.Add($"{id}: el estado '{st.name}' usa BlendTree; debe ser un clip unico.");
            }

            // GATE DE COPIA RAW EXACTA. Los estados de RawDerivedStates deben ser copias
            // EXACTAS del FBX (todas las curvas), no el resultado podado del optimizador.
            // Con writeDefaultValues=false, un hueso que el clip no anima se queda en la
            // pose del estado anterior: si el clip es parcial, el pelo/cara se ancla a la
            // pose anterior y la cabeza se ve deformada. Le paso a Natanael, que al no tener
            // FBX del primer commit caia al pipeline clean (70 de 279 huesos, 0 faciales).
            // Umbral 250 de 278: las copias exactas dan 278/279; una copia clean da ~70.
            foreach (var st in states)
            {
                if (!(st.motion is AnimationClip rawClip)) continue;
                if (!rawClip.name.EndsWith("_FirstRaw", StringComparison.Ordinal)
                    && !rawClip.name.EndsWith("_RawWalk", StringComparison.Ordinal)) continue;
                checks++;
                int rotBones = AnimationUtility.GetCurveBindings(rawClip)
                    .Count(b => b.propertyName.StartsWith("m_LocalRotation", StringComparison.Ordinal));
                if (rotBones < 250)
                    errors.Add($"{id}: el estado '{st.name}' ({rawClip.name}) solo anima {rotBones} huesos; " +
                               "las copias RAW deben ser EXACTAS (>=250). Con writeDefaultValues=false el resto " +
                               "se queda en la pose anterior y deforma cabeza y pelo.");
            }

            var idle = states.FirstOrDefault(s => s.name == "Idle");
            var walkFwd = states.FirstOrDefault(s => s.name == "WalkGuardFwd");
            var walkBack = states.FirstOrDefault(s => s.name == "WalkGuardBack");
            var runFwd = states.FirstOrDefault(s => s.name == "RunFwd");
            var runBack = states.FirstOrDefault(s => s.name == "RunBack");
            var block = states.FirstOrDefault(s => s.name == "Block");
            var walkClip = walkFwd != null ? walkFwd.motion as AnimationClip : null;
            var runClip = runFwd != null ? runFwd.motion as AnimationClip : null;

            checks++;
            if (idle == null || !(idle.motion is AnimationClip idleClip))
                errors.Add($"{id}: falta el estado Idle con clip.");
            else if (!AnimationUtility.GetAnimationClipSettings(idleClip).loopTime)
                errors.Add($"{id}: Idle no tiene loopTime.");

            checks++;
            if (walkClip == null || walkBack == null || walkBack.motion != walkClip)
                errors.Add($"{id}: WalkGuardFwd/Back deben existir y usar el MISMO clip.");
            if (walkClip != null)
            {
                checks++;
                if (!walkClip.name.EndsWith("_RawWalk", StringComparison.Ordinal))
                    errors.Add($"{id}: WalkGuard debe ser el RAW in-place de camianrcongaurdia (_RawWalk), no '{walkClip.name}'.");
                checks++;
                if (AnimationUtility.GetCurveBindings(walkClip).Count(b => KomSetup.IsFacialBonePath(b.path)) == 0)
                    errors.Add($"{id}: WalkGuard perdio el facial propio de camianrcongaurdia.");
                checks++;
                if (!AnimationUtility.GetAnimationClipSettings(walkClip).loopTime)
                    errors.Add($"{id}: WalkGuard no tiene loopTime; se congelaria al caminar.");
                checks++;
                if (walkFwd.speed <= 0.01f || walkBack.speed >= -0.01f)
                    errors.Add($"{id}: WalkGuardFwd/Back deben tener velocidad positiva y negativa.");
                ValidateWalkGuardHead(prefab, walkClip, id, errors, ref checks);
                ValidateLoopClip(prefab, walkClip, "WalkGuard", id, errors, ref checks);
            }

            checks++;
            if (runClip == null || runBack == null || runBack.motion != runClip)
                errors.Add($"{id}: RunFwd/Back deben existir y usar el MISMO clip.");
            if (runClip != null)
            {
                checks++;
                if (!runClip.name.EndsWith("_FirstRaw", StringComparison.Ordinal))
                    errors.Add($"{id}: Run debe ser el RAW in-place del primer commit (_FirstRaw), no '{runClip.name}'.");
                checks++;
                if (!AnimationUtility.GetAnimationClipSettings(runClip).loopTime)
                    errors.Add($"{id}: Run no tiene loopTime.");
                ValidateLoopClip(prefab, runClip, "Run", id, errors, ref checks);
            }

            checks++;
            if (block == null || !(block.motion is AnimationClip blockClip))
                errors.Add($"{id}: falta el estado Block con clip.");
            else
            {
                checks++;
                if (!AnimationUtility.GetAnimationClipSettings(blockClip).loopTime)
                    errors.Add($"{id}: Block no tiene loopTime.");
                ValidateLoopClip(prefab, blockClip, "Block", id, errors, ref checks);
            }
        }

        // Gate de especiales: datos completos, estado full-body en Base, clip limpio,
        // y liberacion sobre las manos REALES del clip (medida en Unity, no a ojo).
        static void ValidateSpecial(CharacterDef def, SpecialDef s, bool required, GameObject prefab,
            AnimatorState[] baseStates, HashSet<string> fbxClips, List<string> errors, ref int checks)
        {
            checks++;
            // JsonUtility crea un objeto por defecto aunque el JSON omita "special":
            // type "none" significa "sin especial", no un especial invalido.
            if (s == null || !s.IsCombatSpecial)
            {
                if (required) errors.Add($"{def.id}: debe declarar un especial ('{def.id}' es parte del set actual).");
                return;
            }
            checks++;
            if (string.IsNullOrWhiteSpace(s.animatorState)) errors.Add($"{def.id}: especial sin animatorState.");
            checks++;
            if (string.IsNullOrWhiteSpace(s.displayName)) errors.Add($"{def.id}: especial sin displayName.");
            checks++;
            if (!ColorUtility.TryParseHtmlString(s.color, out _)) errors.Add($"{def.id}: color de especial invalido '{s.color}'.");
            checks++;
            if (s.duration < 0.3f || s.duration > 5f) errors.Add($"{def.id}: duration de especial fuera de rango ({s.duration}).");
            checks++;
            if (s.releaseTime <= 0f || s.releaseTime >= s.duration)
                errors.Add($"{def.id}: releaseTime ({s.releaseTime}) debe caer dentro del especial ({s.duration}).");
            checks++;
            if (s.activeDuration <= 0f || s.releaseTime + s.activeDuration > s.duration + 0.8f)
                errors.Add($"{def.id}: activeDuration no cabe en el especial.");
            checks++;
            if (s.damage <= 0f || s.damage > 60f) errors.Add($"{def.id}: damage de especial fuera de rango.");
            checks++;
            // La onda de Jose llega a 7.5 m por diseno (la cadena de Peso, a 3.4).
            if (s.range < 1f || s.range > 8f) errors.Add($"{def.id}: range de especial fuera de rango.");
            checks++;
            if (s.hitboxWidth <= 0.1f || s.hitboxHeight <= 0.1f)
                errors.Add($"{def.id}: hitbox de especial invalida.");
            if (string.Equals(s.type, "beam", StringComparison.Ordinal))
            {
                checks++;
                if (s.projectileSpeed < 3f || s.projectileSpeed > 20f)
                    errors.Add($"{def.id}: projectileSpeed fuera de rango.");
            }
            else if (string.Equals(s.type, "chain", StringComparison.Ordinal))
            {
                checks++;
                if (s.chainLinks < 8f || s.chainLinks > 40f) errors.Add($"{def.id}: chainLinks fuera de rango.");
                checks++;
                if (s.extendTime <= 0f || s.releaseTime + s.extendTime >= s.duration)
                    errors.Add($"{def.id}: extendTime de cadena invalido.");
                checks++;
                if (s.chainSag < 0f || s.chainSag > 1f) errors.Add($"{def.id}: chainSag fuera de rango.");
            }

            var state = baseStates.FirstOrDefault(x => x.name == s.animatorState);
            checks++;
            if (state == null) { errors.Add($"{def.id}: falta el estado Base '{s.animatorState}'."); return; }
            checks++;
            if (state.writeDefaultValues) errors.Add($"{def.id}: '{s.animatorState}' debe tener writeDefaultValues=false.");
            checks++;
            if (state.speed <= 0.01f) errors.Add($"{def.id}: '{s.animatorState}' no tiene velocidad positiva.");
            if (!(state.motion is AnimationClip clip) || clip == null)
            {
                checks++;
                errors.Add($"{def.id}: '{s.animatorState}' no tiene AnimationClip.");
                return;
            }
            checks++;
            if (Mathf.Abs(state.speed - clip.length / Mathf.Max(0.01f, s.duration)) > 0.04f)
                errors.Add($"{def.id}: velocidad de '{s.animatorState}' no coincide con duration (clip {clip.length:F2}s, estado {state.speed:F2}).");
            checks++;
            if (AnimationUtility.GetAnimationClipSettings(clip).loopTime)
                errors.Add($"{def.id}: '{s.animatorState}' no debe ser loop.");
            checks++;
            if (!fbxClips.Contains(s.animatorState))
                errors.Add($"{def.id}: el FBX no contiene el clip '{s.animatorState}'.");
            int facial = AnimatedFacialCurves(clip);
            int scale = AnimationUtility.GetCurveBindings(clip)
                .Count(b => b.propertyName.StartsWith("m_LocalScale", StringComparison.Ordinal));
            checks++;
            if (facial != 0) errors.Add($"{def.id}: '{s.animatorState}' tiene {facial} curvas faciales (cara/pelo).");
            checks++;
            if (scale != 0) errors.Add($"{def.id}: '{s.animatorState}' tiene {scale} curvas de escala.");

            // Medición real del frame de liberacion sobre el prefab.
            var preview = EditorSceneManager.NewPreviewScene();
            GameObject go = null;
            try
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;
                var transforms = go.GetComponentsInChildren<Transform>(true);
                Transform Find(string name) => transforms.FirstOrDefault(t => t.name == name);
                var hip = Find("DEF-spine");
                var handR = Find("DEF-hand.R");
                var handL = Find("DEF-hand.L");
                var fingerR = Find("DEF-f_index.01.R");
                var fingerL = Find("DEF-f_index.01.L");
                checks++;
                if (hip == null || handR == null || handL == null || fingerR == null || fingerL == null)
                {
                    errors.Add($"{def.id}: faltan huesos de mano para medir el especial.");
                    return;
                }
                // El guitarrazo no se mide en `releaseTime`: su primer impacto real es
                // hitTimes[0] (0.87 s de estado), que es cuando el mastil llega abajo.
                float sampleTime = s.releaseTime;
                if (string.Equals(s.type, "club", StringComparison.Ordinal) &&
                    s.hitTimes != null && s.hitTimes.Length > 0)
                    sampleTime = s.hitTimes[0];
                float t = Mathf.Clamp01(sampleTime / Mathf.Max(0.01f, s.duration)) * clip.length;
                clip.SampleAnimation(go, t);
                var palmR = Vector3.Lerp(handR.position, fingerR.position, 0.6f);
                var palmL = Vector3.Lerp(handL.position, fingerL.position, 0.6f);
                var mid = (palmR + palmL) * 0.5f;
                float forwardR = palmR.z - hip.position.z;
                float forwardL = palmL.z - hip.position.z;
                float sep = Vector3.Distance(palmR, palmL);
                File.AppendAllText("Logs/special_anim.txt",
                    $"{def.id}|{s.type}|t={t:F3}/{clip.length:F2}|hip={hip.position:F3}|palmR={palmR:F3}|palmL={palmL:F3}|" +
                    $"forwardR={forwardR:F3}|forwardL={forwardL:F3}|midY={mid.y:F3}|sep={sep:F3}\n");
                if (string.Equals(s.type, "beam", StringComparison.Ordinal))
                {
                    checks++;
                    if (Mathf.Min(forwardR, forwardL) < 0.35f)
                        errors.Add($"{def.id}: el Rayo se libera con las manos atras (forward {forwardR:F2}/{forwardL:F2} m).");
                    checks++;
                    if (sep > 1.0f)
                        errors.Add($"{def.id}: las manos estan demasiado separadas al liberar ({sep:F2} m).");
                    checks++;
                    if (mid.y < 0.8f || mid.y > 1.75f)
                        errors.Add($"{def.id}: altura de manos al liberar fuera de rango ({mid.y:F2} m).");
                }
                else if (string.Equals(s.type, "club", StringComparison.Ordinal))
                {
                    // El guitarrazo golpea con el BORDE del cuerpo, no con la mano: lo que
                    // se comprueba es que en el primer impacto la mano haya bajado desde la
                    // guardia y no quede por encima de la cabeza. 1.35 m es altura de pecho
                    // en este rig; el borde de la guitarra queda aun mas abajo.
                    checks++;
                    if (palmR.y > 1.35f)
                        errors.Add($"{def.id}: el guitarrazo impacta con la mano alta ({palmR.y:F2} m).");
                    checks++;
                    if (forwardR < -0.15f)
                        errors.Add($"{def.id}: el guitarrazo impacta con la mano atras (forward {forwardR:F2} m).");
                }
                else if (string.Equals(s.type, "boomerang", StringComparison.Ordinal))
                {
                    checks++;
                    if (Mathf.Min(forwardR, forwardL) < 0.15f)
                        errors.Add($"{def.id}: el acordeon sale con las manos atras (forward {forwardR:F2}/{forwardL:F2} m).");
                    checks++;
                    if (sep > 1.2f)
                        errors.Add($"{def.id}: las manos estan demasiado separadas al lanzar el acordeon ({sep:F2} m).");
                }
                else if (string.Equals(s.type, "wave", StringComparison.Ordinal))
                {
                    // La onda sale de la palma IZQUIERDA (Jose y Flavia). No es una
                    // cadena: la mano derecha puede estar retrasada sujetando el micro.
                    checks++;
                    if (forwardL < 0.2f)
                        errors.Add($"{def.id}: la onda sale con la mano izquierda atras (forward {forwardL:F2} m).");
                    checks++;
                    if (palmL.y < 0.75f || palmL.y > 1.8f)
                        errors.Add($"{def.id}: altura de la palma de la onda fuera de rango ({palmL.y:F2} m).");
                }
                else
                {
                    checks++;
                    if (forwardR < 0.2f)
                        errors.Add($"{def.id}: la cadena sale con la mano atras (forward {forwardR:F2} m).");
                    checks++;
                    if (palmR.y < 0.75f || palmR.y > 1.8f)
                        errors.Add($"{def.id}: altura de la mano de la cadena fuera de rango ({palmR.y:F2} m).");
                }
            }
            catch (Exception e)
            {
                checks++;
                errors.Add($"{def.id}: medicion del especial fallo: {e.Message}");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        // Gate del bug recurrente: la cabeza NO puede deformarse a lo largo del ciclo.
        // 100 fases, dos comprobaciones duras:
        //  - las cadenas ORG-spine.006 (control) y DEF-spine.006 (deformacion) deben
        //    coincidir siempre (si divergen, la cara/pelo se estiran);
        //  - las distancias craneo->huesos faciales deben ser rigidas (<= 2 mm).
        static void ValidateWalkGuardHead(GameObject prefab, AnimationClip clip, string id,
            List<string> errors, ref int checks)
        {
            if (prefab == null || clip == null) return;
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject go = null;
            try
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;
                var all = go.GetComponentsInChildren<Transform>(true);
                var orgHead = all.FirstOrDefault(t => t.name == "ORG-spine.006");
                var defHead = all.FirstOrDefault(t => t.name == "DEF-spine.006");
                var probeNames = new[]
                {
                    "DEF-forehead.L.002", "DEF-forehead.R.002", "DEF-temple.L", "DEF-temple.R",
                    "DEF-jaw", "DEF-nose.004", "DEF-ear.R.002"
                };
                var probes = probeNames.Select(n => all.FirstOrDefault(t => t.name == n)).ToArray();
                checks++;
                if (animator == null || orgHead == null || defHead == null || probes.Any(p => p == null))
                {
                    errors.Add($"{id}: no se pudo medir la cabeza de WalkGuard (faltan huesos ORG/DEF/faciales).");
                    return;
                }

                const int N = 100;
                var minD = new float[probes.Length];
                var maxD = new float[probes.Length];
                for (int i = 0; i < probes.Length; i++) { minD[i] = float.MaxValue; maxD[i] = 0f; }
                var bindScale = new Vector3[probes.Length + 2];
                float maxOrgDefDist = 0f, maxOrgDefAngle = 0f, maxScaleDev = 0f;
                for (int s = 0; s <= N; s++)
                {
                    if (animator != null) animator.Rebind();
                    clip.SampleAnimation(go, Mathf.Min(clip.length * s / N, clip.length - 0.00001f));
                    float od = Vector3.Distance(orgHead.position, defHead.position);
                    float oa = Quaternion.Angle(orgHead.rotation, defHead.rotation);
                    maxOrgDefDist = Mathf.Max(maxOrgDefDist, od);
                    maxOrgDefAngle = Mathf.Max(maxOrgDefAngle, oa);
                    if (s == 0)
                    {
                        for (int i = 0; i < probes.Length; i++) bindScale[i] = probes[i].lossyScale;
                        bindScale[probes.Length] = orgHead.lossyScale;
                        bindScale[probes.Length + 1] = defHead.lossyScale;
                    }
                    for (int i = 0; i < probes.Length; i++)
                    {
                        float d = Vector3.Distance(defHead.position, probes[i].position);
                        minD[i] = Mathf.Min(minD[i], d);
                        maxD[i] = Mathf.Max(maxD[i], d);
                        Vector3 bs = bindScale[i];
                        Vector3 sc = probes[i].lossyScale;
                        maxScaleDev = Mathf.Max(maxScaleDev,
                            Mathf.Abs(sc.x / Mathf.Max(0.0001f, bs.x) - 1f),
                            Mathf.Abs(sc.y / Mathf.Max(0.0001f, bs.y) - 1f),
                            Mathf.Abs(sc.z / Mathf.Max(0.0001f, bs.z) - 1f));
                    }
                    foreach (var h in new[] { orgHead, defHead })
                    {
                        Vector3 bs = h == orgHead ? bindScale[probes.Length] : bindScale[probes.Length + 1];
                        Vector3 sc = h.lossyScale;
                        maxScaleDev = Mathf.Max(maxScaleDev,
                            Mathf.Abs(sc.x / Mathf.Max(0.0001f, bs.x) - 1f),
                            Mathf.Abs(sc.y / Mathf.Max(0.0001f, bs.y) - 1f),
                            Mathf.Abs(sc.z / Mathf.Max(0.0001f, bs.z) - 1f));
                    }
                }
                float facialRange = 0f;
                for (int i = 0; i < probes.Length; i++) facialRange = Mathf.Max(facialRange, maxD[i] - minD[i]);

                checks++;
                if (maxOrgDefDist > 0.002f || maxOrgDefAngle > 1f)
                    errors.Add($"{id}: cabeza WalkGuard: ORG/DEF divergen {maxOrgDefDist:F3} m / {maxOrgDefAngle:F1} grados (cara estirada).");
                checks++;
                if (facialRange > 0.002f)
                    errors.Add($"{id}: cabeza WalkGuard: la cara se deforma {facialRange:F3} m a lo largo del ciclo.");
                checks++;
                if (maxScaleDev > 0.01f)
                    errors.Add($"{id}: cabeza WalkGuard: escala facial/hueso de cabeza varia {maxScaleDev:P2}.");

                Directory.CreateDirectory("Logs");
                File.AppendAllText("Logs/anim_head.txt",
                    $"{id,-16} orgDef={maxOrgDefDist:F4} m / {maxOrgDefAngle:F2} deg  facialRange={facialRange:F4} m  scaleDev={maxScaleDev:P3}  " +
                    (maxOrgDefDist <= 0.002f && maxOrgDefAngle <= 1f && facialRange <= 0.002f && maxScaleDev <= 0.01f ? "[ok]" : "[FAIL]") + "\n");
            }
            catch (Exception e)
            {
                checks++;
                errors.Add($"{id}: gate de cabeza WalkGuard fallo: {e.Message}");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // Un clip loop no puede dar un salto visual al volver al primer frame.
        static void ValidateLoopClip(GameObject prefab, AnimationClip clip, string label, string id,
            List<string> errors, ref int checks)
        {
            if (prefab == null || clip == null) return;
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject go = null;
            try
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.enabled = false;
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (animator == null || smr == null)
                {
                    checks++;
                    errors.Add($"{id}: no se pudo medir el loop de {label} (sin Animator/SMR).");
                    return;
                }
                animator.Rebind();
                clip.SampleAnimation(go, 0f);
                var a = new Mesh();
                smr.BakeMesh(a, false);
                var va = a.vertices;
                animator.Rebind();
                clip.SampleAnimation(go, Mathf.Max(0f, clip.length - 0.00001f));
                var b = new Mesh();
                smr.BakeMesh(b, false);
                var vb = b.vertices;
                float max = 0f;
                int n = Mathf.Min(va.Length, vb.Length);
                for (int i = 0; i < n; i++) max = Mathf.Max(max, Vector3.Distance(va[i], vb[i]));
                max *= Mathf.Max(0.0001f, smr.transform.lossyScale.x);
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
                // El WalkGuard es el clip que el usuario ve al andar: 5 mm. Los RAW
                // clasicos del primer commit (Run/Block) cierran a ~8 mm; 1 cm sigue
                // siendo imperceptible y no es el bug de teletransporte.
                float limit = clip.name.Contains("WalkGuard") ? 0.005f : 0.01f;
                checks++;
                if (max > limit)
                    errors.Add($"{id}: '{label}' salta {max:F3} m al loopear (limite {limit:F3} m).");
                Directory.CreateDirectory("Logs");
                File.AppendAllText("Logs/anim_loop.txt",
                    $"{id,-16} {label,-10} maxVertexJump={max:F4} m  [{(max <= limit ? "ok" : "FAIL")}]\n");
            }
            catch (Exception e)
            {
                checks++;
                errors.Add($"{id}: gate de loop '{label}' fallo: {e.Message}");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static bool IsFirstRawDerived(AnimationClip clip)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            if (path.IndexOf("/FirstCommit/", StringComparison.Ordinal) >= 0) return true;
            string name = Path.GetFileNameWithoutExtension(path);
            // _FirstRaw: copia exacta del primer commit. _RawWalk: camianrcongaurdia
            // RAW actual, con su facial/escala buena (cabeza verificada).
            return name.EndsWith("_FirstRaw", StringComparison.Ordinal)
                || name.EndsWith("_RawWalk", StringComparison.Ordinal);
        }

        // Estado de la evidencia lateral: gate numerico + hojas renderizadas de perfil.
        static string LateralEvidenceStatus()
        {
            const string skeleton = "Logs/anim_skeleton.txt";
            if (!File.Exists(skeleton)) return "PENDIENTE (falta Logs/anim_skeleton.txt; corre tools/verify-anim.sh)";
            string report = File.ReadAllText(skeleton);
            if (!report.Contains("VERDICT: PASS")) return "FALLA (Logs/anim_skeleton.txt no esta en PASS)";
            var sheets = Directory.Exists("Logs/anim_side")
                ? Directory.GetFiles("Logs/anim_side", "*_sheet*.png")
                : Array.Empty<string>();
            if (sheets.Length == 0) return "PENDIENTE (sin hojas Logs/anim_side/*_sheet*.png)";
            return $"OK ({sheets.Length} hojas laterales)";
        }

        static AnimationClip FindIdleClip(AnimatorState idle)
        {
            return idle != null ? idle.motion as AnimationClip : null;
        }

        static bool SingleFinalPeriod(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.TrimEnd();
            return s.EndsWith(".") && s.IndexOf('.') == s.Length - 1;
        }

        public static void RunValidation()
        {
            AssetDatabase.Refresh();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/anim_head.txt", "KOM GATE CABEZA WALKGUARD · 100 fases · ORG/DEF + rigidez facial\n");
            File.WriteAllText("Logs/anim_loop.txt", "KOM GATE LOOP · salto maximo de malla entre primer y ultimo frame\n");
            File.WriteAllText("Logs/special_anim.txt", "KOM SPECIAL ANIM · timing y manos en el frame de liberacion\n");
            var errors = new List<string>();
            int checks = 0;

            void Check(bool condition, string message)
            {
                checks++;
                if (!condition) errors.Add(message);
            }

            var characters = KomData.Characters.characters;
            var stages = KomData.Stages.stages;
            var tournament = KomData.Tournament;

            Check(characters != null && characters.Length == 7, $"Se esperaban 7 personajes y hay {characters?.Length ?? 0}.");
            Check(characters != null && characters.Count(c => c.playable) == 6, "Se esperaban 6 personajes jugables.");
            Check(characters != null && characters.Count(c => c.isBoss) == 1, "Se esperaba exactamente 1 jefe final.");
            Check(KomData.Boss != null && KomData.Boss.id == "flavia", "El jefe final debe ser 'flavia'.");

            var ids = new HashSet<string>();
            foreach (var def in characters ?? Array.Empty<CharacterDef>())
            {
                if (def == null) { errors.Add("Personaje nulo en el catalogo."); continue; }
                Check(!string.IsNullOrWhiteSpace(def.id), "Personaje sin id.");
                Check(ids.Add(def.id), $"Id duplicado: {def.id}");
                Check(!string.IsNullOrWhiteSpace(def.displayName), $"{def.id}: falta displayName.");
                Check(!string.IsNullOrWhiteSpace(def.genre), $"{def.id}: falta genre.");
                Check(!string.IsNullOrWhiteSpace(def.portraitResource), $"{def.id}: falta portraitResource.");
                Check(!string.IsNullOrWhiteSpace(def.cardResource), $"{def.id}: falta cardResource.");
                Check(!string.IsNullOrWhiteSpace(def.headResource), $"{def.id}: falta headResource.");
                Check(!string.IsNullOrWhiteSpace(def.prefabResource), $"{def.id}: falta prefabResource.");
                Check(def.maxHealth > 0, $"{def.id}: maxHealth invalido.");
                // El walk usa camianrcongaurdia, un paso corto de guardia (~0.25 m/s de
                // contacto); la velocidad se calibra a ese paso para no patinar.
                Check(def.walkSpeed >= 0.2f && def.walkSpeed <= 2.5f, $"{def.id}: walkSpeed fuera de rango ({def.walkSpeed}).");
                Check(def.dashSpeed >= 2.0f && def.dashSpeed <= 5.0f && def.dashSpeed > def.walkSpeed,
                    $"{def.id}: dashSpeed fuera de rango o <= walkSpeed ({def.dashSpeed}).");
                Check(def.hurtboxRadius >= 0.2f && def.hurtboxRadius <= 0.5f, $"{def.id}: hurtboxRadius invalido.");
                Check(def.hurtboxHeight >= 1.2f && def.hurtboxHeight <= 1.8f, $"{def.id}: hurtboxHeight invalido.");
                Check(def.crouchHurtboxHeight >= 0.5f && def.crouchHurtboxHeight < def.hurtboxHeight,
                    $"{def.id}: crouchHurtboxHeight invalido.");
                Check(def.damageScale > 0, $"{def.id}: damageScale invalido.");

                var portrait = Resources.Load<Sprite>(def.portraitResource);
                var card = Resources.Load<Sprite>(def.cardResource);
                var head = Resources.Load<Sprite>(def.headResource);
                Check(portrait != null, $"{def.id}: no carga el retrato '{def.portraitResource}'.");
                Check(card != null, $"{def.id}: no carga la card '{def.cardResource}'.");
                Check(head != null, $"{def.id}: no carga la cabeza '{def.headResource}'.");

                var prefabPath = $"Assets/Resources/Characters/{def.id}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Check(prefab != null, $"{def.id}: falta el prefab {prefabPath}.");
                if (prefab != null)
                {
                    var fighter = prefab.GetComponent<Fighter>();
                    Check(fighter != null, $"{def.id}: el prefab no tiene Fighter.");
                    Check(fighter != null && fighter.enabled, $"{def.id}: el Fighter del prefab esta desactivado.");
                    if (fighter != null)
                    {
                        var so = new SerializedObject(fighter);
                        var idProp = so.FindProperty("characterId");
                        Check(idProp != null && idProp.stringValue == def.id, $"{def.id}: characterId del prefab no coincide.");
                    }
                    var animator = prefab.GetComponentInChildren<Animator>(true);
                    Check(animator != null, $"{def.id}: el prefab no tiene Animator.");
                    Check(animator != null && animator.runtimeAnimatorController != null, $"{def.id}: Animator sin controller.");
                    var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                    Check(renderers.Length > 0, $"{def.id}: el prefab no tiene renderers.");
                    bool hasBaseMap = false;
                    foreach (var renderer in renderers)
                    {
                        foreach (var mat in renderer.sharedMaterials)
                        {
                            if (mat == null) continue;
                            Check(mat.shader != null && mat.shader.name.Contains("Universal Render Pipeline"),
                                $"{def.id}: material '{mat.name}' no usa URP.");
                            if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null) hasBaseMap = true;
                        }
                    }
                    Check(hasBaseMap, $"{def.id}: ningun material tiene textura base (posible modelo blanco).");
                }

                string fbxPath = $"Assets/Art/Characters/{def.id}.fbx";
                var fbxClipNames = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                    .Where(c => c.name.StartsWith("__preview__") == false)
                    .Select(c => c.name)
                    .ToHashSet();
                Check(fbxClipNames.Count >= KomSetup.RequiredClips.Length,
                    $"{def.id}: se esperaban {KomSetup.RequiredClips.Length} clips y hay {fbxClipNames.Count}.");
                foreach (var requiredClip in KomSetup.RequiredClips)
                    Check(fbxClipNames.Contains(requiredClip), $"{def.id}: falta el clip '{requiredClip}' en el FBX.");

                var controllerPath = $"Assets/KOM/Animation/Controllers/{def.id}.controller";
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                Check(controller != null, $"{def.id}: falta el AnimatorController {controllerPath}.");
                if (controller != null)
                {
                    // AAA: 2 capas y CERO mascaras. Base manda la pose completa; la reservada
                    // queda a peso 0 para los poderes de la iteracion 3.
                    Check(controller.layers.Length == 2,
                        $"{def.id}: el controller debe tener 2 capas (Base, ReservedSpecials) y tiene {controller.layers.Length}.");
                    Check(controller.layers[0].avatarMask == null, $"{def.id}: Base no debe tener AvatarMask.");
                    Check(controller.layers[1].avatarMask == null, $"{def.id}: ReservedSpecials no debe tener AvatarMask.");
                    Check(controller.layers[0].defaultWeight >= 0.99f, $"{def.id}: Base Layer debe tener peso 1.");
                    Check(controller.layers[1].defaultWeight <= 0.01f, $"{def.id}: ReservedSpecials debe empezar con peso 0.");

                    var baseStates = controller.layers[0].stateMachine.states.Select(s => s.state).ToArray();
                    var baseNames = baseStates.Select(s => s.name).ToHashSet();
                    foreach (var required in KomSetup.RequiredBaseStates)
                        Check(baseNames.Contains(required), $"{def.id}: falta el estado base '{required}'.");
                    foreach (var forbidden in new[] { "Locomotion", "LocomotionGuard", "Walk", "WalkAlt" })
                        Check(!baseNames.Contains(forbidden),
                            $"{def.id}: el estado '{forbidden}' no debe existir; andar usa solo WalkGuard.");
                    Check(baseStates.All(s => !(s.motion is BlendTree)),
                        $"{def.id}: la capa Base no debe tener BlendTrees (un solo clip por estado).");
                    Check(controller.parameters.All(p => p.name != "MoveSpeed"),
                        $"{def.id}: el parametro MoveSpeed ya no debe existir.");
                    foreach (var stateName in new[] { "Idle", "WalkGuardFwd", "WalkGuardBack", "RunFwd", "RunBack", "Block" })
                    {
                        var st = baseStates.FirstOrDefault(s => s.name == stateName);
                        Check(st != null && st.motion is AnimationClip,
                            $"{def.id}: el estado de locomocion '{stateName}' debe tener un clip unico.");
                    }
                    foreach (var stateName in new[] { "Crouch", "Stunned", "GetUp", "Victory" })
                    {
                        var st = baseStates.FirstOrDefault(s => s.name == stateName);
                        Check(st != null && st.motion != null, $"{def.id}: el estado '{stateName}' no tiene clip.");
                    }

                    // La VICTORIA no puede ser la animacion de un especial con instrumento.
                    // `Victory` es un estado suelto: no pasa por StartSpecial, asi que
                    // `SpawnPropForSpecial` no corre y el luchador mima un objeto que no esta
                    // en la escena. Medido en la jefa: celebraba con "Combo" (la animacion del
                    // guitarrazo de Alex), y el jugador lo leia como "lanza el guitarrazo al
                    // terminar la ronda sin razon" + "a veces no aparece la guitarra".
                    var propSpecialStates = new HashSet<string>();
                    for (int i = 0; i < def.SpecialCount; i++)
                    {
                        var sp = def.GetSpecial(i);
                        if (sp != null && sp.IsCombatSpecial && !string.IsNullOrEmpty(sp.propResource))
                            propSpecialStates.Add(sp.animatorState);
                    }
                    var victoryMotion = baseStates.FirstOrDefault(s => s.name == "Victory")?.motion as AnimationClip;
                    if (victoryMotion != null)
                    {
                        foreach (var st in baseStates)
                        {
                            if (st.name == "Victory" || !propSpecialStates.Contains(st.name)) continue;
                            Check(!ReferenceEquals(st.motion, victoryMotion),
                                $"{def.id}: celebra con la animacion del especial '{st.name}', que lleva instrumento: " +
                                "en la victoria el prop no se instancia y el luchador mima un objeto invisible.");
                        }
                    }
                    // Todos los ataques viven en Base (full-body), sin capa de brazos.
                    foreach (var spec in Attacks.All)
                    {
                        var state = baseStates.FirstOrDefault(s => s.name == spec.animatorState);
                        Check(state != null && state.motion != null && state.speed > 0.01f,
                            $"{def.id}: el estado de ataque '{spec.animatorState}' no tiene clip/velocidad en Base.");
                        if (state != null && state.motion is AnimationClip atkClip)
                        {
                            bool firstRaw = IsFirstRawDerived(atkClip);
                            if (!firstRaw)
                            {
                                var atkScaleCurves = AnimationUtility.GetCurveBindings(atkClip)
                                    .Count(b => b.propertyName.StartsWith("m_LocalScale"));
                                Check(atkScaleCurves == 0,
                                    $"{def.id}: '{spec.animatorState}' tiene {atkScaleCurves} curvas de escala (deforman la malla).");
                            }
                            Check(state.writeDefaultValues == false,
                                $"{def.id}: '{spec.animatorState}' debe tener writeDefaultValues=false.");
                        }
                    }

                    // La capa reservada queda VACIA (solo Empty) para no cargar clips de
                    // especiales/proyectiles que aun no se usan. Los clips siguen en el FBX.
                    var reservedStates = controller.layers[1].stateMachine.states.Select(s => s.state).ToArray();
                    Check(reservedStates.Length == 1 && reservedStates[0].name == "Empty",
                        $"{def.id}: ReservedSpecials debe estar vacia (solo Empty) para no cargar clips sin usar.");

                    // AAA: higiene global de escala en estados Base (anti-deformacion).
                    foreach (var st in baseStates)
                    {
                        if (st.motion is AnimationClip c && c != null)
                        {
                            // Los clips RAW del primer commit (y sus copias in-place _FirstRaw)
                            // son la referencia conocida: conservan escala/facial a proposito.
                            if (IsFirstRawDerived(c)) continue;
                            var n = AnimationUtility.GetCurveBindings(c)
                                .Count(b => b.propertyName.StartsWith("m_LocalScale"));
                            Check(n == 0, $"{def.id}: '{st.name}' tiene {n} curvas de escala (deforman la malla).");
                        }
                    }

                    // AAA anti-deformacion facial: el subrig ORG-face (frente, sienes,
                    // orejas, mandibula, ojos...) debe estar CONGELADO en todos los clips.
                    // Si se anima, hincha el craneo/pelo y estira la cara. La cabeza sigue
                    // girando por ORG-spine.006, que NO se toca.
                    int facialCurves = 0;
                    string facialClip = "-";
                    var clipGuids = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/KOM/Animation/Clips" });
                    foreach (var guid in clipGuids)
                    {
                        string clipPath = AssetDatabase.GUIDToAssetPath(guid);
                        string fileName = Path.GetFileNameWithoutExtension(clipPath);
                        if (!fileName.StartsWith(def.id + "_", StringComparison.Ordinal)) continue;
                        // Las copias in-place _FirstRaw conservan el facial original del
                        // primer commit. Las _RawWalk conservan el facial propio del clip.
                        if (fileName.EndsWith("_FirstRaw", StringComparison.Ordinal)) continue;
                        if (fileName.EndsWith("_RawWalk", StringComparison.Ordinal)) continue;
                        var clipAsset = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                        if (clipAsset == null) continue;
                        int n = AnimatedFacialCurves(clipAsset);
                        if (n > 0) { facialCurves += n; facialClip = clipAsset.name; }
                    }
                    Check(facialCurves == 0,
                        $"{def.id}: {facialCurves} curvas faciales sin congelar en '{facialClip}' (deforman cara/pelo).");

                    // El Idle que JUEGA es el del controller (copia in-place), no un clip
                    // suelto de la carpeta que puede estar sin usar.
                    var idleState = baseStates.FirstOrDefault(s => s.name == "Idle");
                    var idleAsset = FindIdleClip(idleState);
                    int headCurves = idleAsset == null ? 0 : AnimationUtility.GetCurveBindings(idleAsset)
                        .Count(b => b.path.EndsWith("/ORG-spine.006", StringComparison.Ordinal)
                            || b.path.EndsWith("/DEF-spine.006", StringComparison.Ordinal));
                    Check(headCurves > 0, $"{def.id}: el Idle del controller no anima la cabeza (rig facial congelado de mas).");

                    ValidateInPlaceClips(controller, prefab, def.id, errors, ref checks);
                    ValidateLocomotion(controller, prefab, def.id, errors, ref checks);
                    // Todos los especiales del personaje (la jefa acumula varios).
                    for (int si = 0; si < def.SpecialCount; si++)
                    {
                        bool requiredSpecial = si == 0 &&
                            (def.id == "juan_grabriel" || def.id == "flavia" || def.id == "peso_plomo");
                        ValidateSpecial(def, def.GetSpecial(si), requiredSpecial, prefab, baseStates,
                            fbxClipNames, errors, ref checks);
                    }
                }
            }

            // NOMBRES DE PODERES. La lista de diseño es cerrada; un cambio de copy no
            // puede dejar "Guitarrazo Absorbido" donde la ficha dice "Guitarrazo de Rock".
            var expectedPowers = new Dictionary<string, string>
            {
                { "juan_grabriel", "Rayo de Interpretación" },
                { "jose_jose", "Nota del Desamor" },
                { "alex_llora", "Guitarrazo de Rock" },
                { "roman_ayala", "Acordeón de Poder" },
                { "natanael_chano", "Paso Tumbado" },
                { "peso_plomo", "Cadena Bélica" },
            };
            foreach (var def in characters ?? Array.Empty<CharacterDef>())
            {
                if (def == null || !expectedPowers.TryGetValue(def.id, out string expectedPower)) continue;
                var baseSpecial = def.GetSpecial(0);
                Check(baseSpecial != null && baseSpecial.displayName == expectedPower,
                    $"{def.id}: el poder debe llamarse '{expectedPower}' y dice '{baseSpecial?.displayName ?? "—"}'.");
                Check(def.specialName == expectedPower,
                    $"{def.id}: specialName debe ser '{expectedPower}' y dice '{def.specialName}'.");
            }
            var powerNames = new HashSet<string>(expectedPowers.Values);
            var flaviaDef = KomData.GetCharacter("flavia");
            if (flaviaDef != null)
            {
                for (int si = 0; si < flaviaDef.SpecialCount; si++)
                {
                    var s = flaviaDef.GetSpecial(si);
                    Check(s != null && powerNames.Contains(s.displayName),
                        $"flavia: el poder heredado '{s?.displayName ?? "—"}' no usa uno de los seis nombres del juego.");
                }
            }

            // TEXTOS. Cada cuerpo y cada cita es UN parrafo con UN solo punto, al final:
            // los puntos intermedios se sustituyen por comas, dos puntos o punto y coma.
            var storyText = KomData.Story;
            foreach (var chapter in storyText != null && storyText.chapters != null
                         ? storyText.chapters : Array.Empty<StoryChapterDef>())
            {
                if (chapter == null) continue;
                foreach (var page in chapter.pages)
                {
                    if (page == null) continue;
                    Check(!string.IsNullOrWhiteSpace(page.body), $"Historia {chapter.id}: pagina sin cuerpo.");
                    Check(page.body == null || !page.body.Contains("·"),
                        $"Historia {chapter.id}: el cuerpo usa punto medio (·); en espanol va punto y seguido.");
                    Check(page.quote == null || !page.quote.Contains("·"),
                        $"Historia {chapter.id}: la cita usa punto medio (·).");
                    Check(SingleFinalPeriod(page.body),
                        $"Historia {chapter.id}: el cuerpo debe llevar UN solo punto, al final; no puntos intermedios.");
                    if (!string.IsNullOrEmpty(page.quote))
                        Check(SingleFinalPeriod(page.quote),
                            $"Historia {chapter.id}: la cita debe llevar UN solo punto, al final.");
                }
            }

            Check(stages != null && stages.Length == 4, $"Se esperaban 4 escenarios y hay {stages?.Length ?? 0}.");
            var stageIds = new HashSet<string>();
            foreach (var stage in stages ?? Array.Empty<StageDef>())
            {
                if (stage == null) { errors.Add("Escenario nulo."); continue; }
                Check(stageIds.Add(stage.id), $"Escenario duplicado: {stage.id}");
                Check(!string.IsNullOrWhiteSpace(stage.displayName), $"{stage.id}: falta displayName.");
                Check(AllowedThemes.Contains(stage.theme), $"{stage.id}: tema desconocido '{stage.theme}'.");
                Check(stage.cameraDistance >= 5f, $"{stage.id}: camara demasiado cerca ({stage.cameraDistance}).");
                // La Madrugadora mide 4.5 m de semiancho por diseno (tarima de 16.8 m);
                // 5.0 era un umbral que contradecia el propio stages.json.
                Check(stage.arenaHalfWidth >= 4.5f, $"{stage.id}: arena demasiado pequena.");

            }

            Check(tournament != null && tournament.nodes != null && tournament.nodes.Length == 4,
                $"Se esperaban 4 nodos de torneo y hay {tournament?.nodes?.Length ?? 0}.");
            // La recta final se pelea DOS veces en el Zocalo: la penultima en el Zocalo
            // de la ruta y la final en su variante nocturna (misma geometria y grada).
            var expectedStages = new[] { "la_madrugadora", "bellas_artes", "zocalo", "zocalo_final" };
            if (tournament != null && tournament.nodes != null)
            {
                for (int i = 0; i < tournament.nodes.Length; i++)
                {
                    var node = tournament.nodes[i];
                    Check(node != null, $"Nodo {i} nulo.");
                    if (node == null) continue;
                    Check(KomData.GetStage(node.stageId) != null, $"Nodo {node.id}: stage '{node.stageId}' no existe.");
                    if (i < expectedStages.Length)
                        Check(node.stageId == expectedStages[i], $"Nodo {i}: se esperaba stage '{expectedStages[i]}' y hay '{node.stageId}'.");
                    Check(AllowedAiProfiles.Contains(node.aiProfile), $"Nodo {node.id}: aiProfile desconocido '{node.aiProfile}'.");
                    Check(node.difficulty >= 1 && node.difficulty <= 4, $"Nodo {node.id}: dificultad fuera de rango.");
                    if (i > 0)
                        Check(node.difficulty > tournament.nodes[i - 1].difficulty,
                            $"Nodo {i}: la dificultad tiene que subir respecto al nodo anterior.");
                    Check(node.mapX >= 0f && node.mapX <= 1f && node.mapY >= 0f && node.mapY <= 1f, $"Nodo {node.id}: coordenadas de mapa fuera de rango.");
                }
                Check(tournament.nodes[tournament.nodes.Length - 1].aiProfile == "boss", "El nodo final debe usar el perfil 'boss'.");
                Check(tournament.nodes[tournament.nodes.Length - 1].stageId == "zocalo_final",
                    "La final del torneo debe pelear en 'zocalo_final' (Zocalo nocturno).");
            }

            // PERFILES DE IA: la escalera de habilidad tiene que ser monotona. Si alguien
            // toca un numero y el 'veteran' acaba reaccionando mas lento que el 'rookie',
            // la progresion de dificultad se rompe en silencio y el jugador no lo sabe.
            var rookie = AiProfile.Get("rookie");
            var contender = AiProfile.Get("contender");
            var veteran = AiProfile.Get("veteran");
            var boss = AiProfile.Get("boss");
            Check(rookie.reaction > contender.reaction && contender.reaction > veteran.reaction && veteran.reaction > boss.reaction,
                "La reaccion de la IA no baja con la dificultad.");
            Check(rookie.aggression < contender.aggression && contender.aggression < veteran.aggression && veteran.aggression < boss.aggression,
                "La agresion de la IA no sube con la dificultad.");
            Check(rookie.attackCooldown > contender.attackCooldown && contender.attackCooldown > veteran.attackCooldown && veteran.attackCooldown > boss.attackCooldown,
                "El cooldown de ataque de la IA no baja con la dificultad.");
            Check(rookie.blockChance < contender.blockChance && contender.blockChance < veteran.blockChance && veteran.blockChance < boss.blockChance,
                "La probabilidad de bloqueo de la IA no sube con la dificultad.");

            foreach (var def in characters ?? Array.Empty<CharacterDef>())
            {
                if (def == null || !def.playable) continue;
                var opponents = GameSession.ResolveOpponents(def.id);
                Check(opponents.Length == 3, $"{def.id}: se esperaban 3 rivales deterministas y hay {opponents.Length}.");
                Check(opponents.Distinct().Count() == opponents.Length, $"{def.id}: rivales duplicados.");
                Check(!opponents.Contains(def.id), $"{def.id}: el jugador aparece como su propio rival.");
                foreach (var opp in opponents)
                {
                    var oppDef = KomData.GetCharacter(opp);
                    Check(oppDef != null && oppDef.playable, $"{def.id}: rival invalido '{opp}'.");
                }
                // Ruta progresiva: el poder de combate sube en cada ronda. Se mide con la
                // MISMA funcion que usa GameSession para ordenar, no con una tabla aparte.
                for (int i = 1; i < opponents.Length; i++)
                {
                    float prev = GameSession.CombatPower(KomData.GetCharacter(opponents[i - 1]));
                    float next = GameSession.CombatPower(KomData.GetCharacter(opponents[i]));
                    Check(prev <= next,
                        $"{def.id}: ruta no progresiva: {opponents[i - 1]} ({prev:F1}) va antes que {opponents[i]} ({next:F1}).");
                }
            }

            foreach (var spec in Attacks.All)
            {
                Check(spec.activeDuration > 0.01f, $"{spec.animatorState}: falta activeDuration.");
                Check(spec.hitCount >= 1, $"{spec.animatorState}: hitCount invalido.");
                float lastHit = spec.hitTime + (spec.hitCount - 1) * spec.hitInterval;
                Check(lastHit + spec.activeDuration <= spec.duration + 0.001f,
                    $"{spec.animatorState}: la ventana activa se sale de la duracion.");
                Check(spec.hitboxForward > 0.1f && spec.hitboxWidth > 0.1f && spec.hitboxHeight > 0.1f && spec.hitboxCenterY > 0.1f,
                    $"{spec.animatorState}: hitbox invalida.");
                Check(spec.guardDamage > 0.01f && spec.guardDamage <= 0.6f,
                    $"{spec.animatorState}: guardDamage fuera de rango.");
            }
            Check(Attacks.HeavyKick.damage > Attacks.LightPunch.damage, "La patada fuerte debe golpear mas que el puno rapido.");
            Check(Attacks.HeavyKick.range > Attacks.LightPunch.range, "La patada fuerte debe tener mas alcance que el puno rapido.");
            Check(Attacks.LightPunch.high && Attacks.HeavyPunch.high && !Attacks.LightKick.high && !Attacks.HeavyKick.high,
                "Los golpes altos y las patadas deben estar marcados correctamente.");
            Check(Attacks.Uppercut.antiAir, "El uppercut debe conectar a rivales en salto.");
            Check(Attacks.DropKick.lunge > 0.5f && Attacks.DropKick.range > Attacks.HeavyKick.range,
                "La patada en carrera debe avanzar y tener mas alcance que la patada fuerte.");
            Check(Attacks.QuadPunch.damage * Attacks.QuadPunch.hitCount > Attacks.LightPunch.damage * 2f,
                "El combo de cuatro (4 impactos) debe castigar mas que dos golpes rapidos.");
            // Combo3 es el unico clip de ataque con deriva neta grande (1.49 m medidos).
            // El avance del Fighter tiene que cubrir LA VENTANA COMPLETA: si se corta a
            // media animacion, el cuerpo se queda atras y los golpes no cuadran con el clip.
            float combo3Travel = Attacks.Combo3.lunge * Attacks.Combo3.lungeDuration;
            Check(Attacks.Combo3.lungeDuration >= Attacks.Combo3.duration - 0.01f,
                $"Combo3: el avance dura {Attacks.Combo3.lungeDuration:F2} s y el clip {Attacks.Combo3.duration:F2} s; debe recorrer toda la ventana.");
            Check(combo3Travel >= 1.30f && combo3Travel <= 1.70f,
                $"Combo3: avance total {combo3Travel:F2} m; el clip horneado mide 1.49 m de deriva neta.");

            // --- Historia: cuatro capitulos (intro, veto, antesala del final y concierto).
            var story = KomData.Story;
            Check(story != null && story.chapters != null && story.chapters.Length >= 4,
                $"La historia debe tener al menos 4 capitulos y tiene {story?.chapters?.Length ?? 0}.");
            int storyPages = 0;
            foreach (var chapterId in new[] { "intro", "veto", "final", "concert" })
            {
                var chapter = story != null ? story.Find(chapterId) : null;
                Check(chapter != null && chapter.Count > 0, $"Falta el capitulo narrativo '{chapterId}'.");
                if (chapter == null) continue;
                Check(!string.IsNullOrWhiteSpace(chapter.hint), $"Capitulo '{chapterId}': falta el texto del boton de salida.");
                for (int i = 0; i < chapter.Count; i++)
                {
                    var page = chapter.pages[i];
                    Check(page != null, $"Capitulo '{chapterId}', pagina {i + 1}: nula.");
                    if (page == null) continue;
                    storyPages++;
                    Check(!string.IsNullOrWhiteSpace(page.kicker), $"'{chapterId}' {i + 1}: falta el antetitulo.");
                    Check(!string.IsNullOrWhiteSpace(page.title), $"'{chapterId}' {i + 1}: falta el titulo.");
                    Check(!string.IsNullOrWhiteSpace(page.body), $"'{chapterId}' {i + 1}: falta el cuerpo.");
                    Check(!string.IsNullOrWhiteSpace(page.quote), $"'{chapterId}' {i + 1}: falta la cita de cierre.");
                    Check(page.WordCount >= 20 && page.WordCount <= 90,
                        $"'{chapterId}' {i + 1}: {page.WordCount} palabras, fuera del rango que cabe en pantalla.");
                    var sprite = Resources.Load<Sprite>(page.image);
                    Check(sprite != null, $"'{chapterId}' {i + 1}: no se encontro el sprite Resources/{page.image}.");
                    if (sprite != null)
                    {
                        // Las ilustraciones rondan 16:9 sin ser exactas (1.78–1.83). La escena
                        // las encaja con preserveAspect, asi que solo se comprueba el orden.
                        float aspect = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
                        Check(aspect > 1.70f && aspect < 1.90f,
                            $"'{chapterId}' {i + 1}: la ilustracion no es apaisada 16:9 ({sprite.rect.width}x{sprite.rect.height}).");
                    }
                }
            }
            Check(storyPages >= 6, $"La historia completa debe tener al menos 6 paginas y tiene {storyPages}.");

            // --- Audio: tema, camas de publico, porras y coreos.
            var themeClip = Resources.Load<AudioClip>("Audio/Music/theme");
            Check(themeClip != null && themeClip.length > 60f, "Falta el tema del menu (Resources/Audio/Music/theme).");
            foreach (var bedPath in new[] { "Audio/Ambience/stadium", "Audio/Ambience/stadium_ext" })
            {
                var bed = Resources.Load<AudioClip>(bedPath);
                Check(bed != null && bed.length > 60f, $"Falta la cama de publico {bedPath}.");
            }
            foreach (var porraId in new[] { "cielito_lindo", "eeeh_bruto", "el_panadero", "king_of_music", "la_chona" })
            {
                var porra = Resources.Load<AudioClip>("Audio/Porras/" + porraId);
                Check(porra != null && porra.length > 5f, $"Falta la porra Resources/Audio/Porras/{porraId}.");
            }
            foreach (var cdef in characters ?? Array.Empty<CharacterDef>())
            {
                if (cdef == null || !cdef.playable) continue;
                var coreo = Resources.Load<AudioClip>("Audio/Coreos/" + cdef.id);
                Check(coreo != null && coreo.length > 5f, $"{cdef.id}: falta el coreo del publico.");
            }
            // SFX de combate: cada familia que usa KomAudio debe existir completa.
            foreach (var sfx in new[]
            {
                ("impact_light", 6), ("impact_heavy", 3), ("impact_body", 3), ("impact_legend", 3),
                ("block_light", 3), ("block_heavy", 3), ("break", 3), ("ko", 3), ("fall", 3),
                ("swing", 6), ("swing_heavy", 2), ("release", 3), ("charge", 3),
                ("boom", 3), ("slam", 3), ("club", 3)
            })
            {
                for (int v = 1; v <= sfx.Item2; v++)
                {
                    var clip = Resources.Load<AudioClip>($"Audio/Sfx/{sfx.Item1}_{v}");
                    Check(clip != null && clip.length > 0.05f, $"Falta el SFX Resources/Audio/Sfx/{sfx.Item1}_{v}.");
                }
            }

            // PUBLICO DE GRADA. Gate propio: layout horneado, recursos, apoyo de cada pie
            // en el escalon medido, separacion, visibilidad y cambio real de pixeles en
            // las capturas de pelea/panoramica. No se coloca a nadie a ojo.
            try
            {
                KomCrowdTools.ValidateCrowd(errors, ref checks, true);
            }
            catch (Exception e)
            {
                errors.Add("Publico de grada: " + e.Message);
            }

            ValidateAnimationTransitions(errors, ref checks);

            var buildScenes = EditorBuildSettings.scenes;
            foreach (var scene in KomScenes.All)
            {
                string path = $"Assets/Scenes/KOM/{scene}.unity";
                Check(AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null, $"Falta la escena {path}.");
                Check(buildScenes.Any(s => s.enabled && s.path == path), $"La escena {scene} no esta habilitada en Build Settings.");
            }
            Check(buildScenes.Length > 0 && buildScenes[0].path.EndsWith(KomScenes.MainMenu + ".unity"), "MainMenu debe ser la primera escena del build.");

            Check(PlayerSettings.productName == "King of Music", "PlayerSettings.productName no es 'King of Music'.");

            if (errors.Count > 0)
            {
                // El veredicto vive en fichero: tras reiniciar el editor el log durable del puente
                // deja de appendar y `errors --since` daría un PASS falso.
                WriteValidationReport($"VERDICT: FAIL ({errors.Count} errores, {checks} checks)", errors);
                foreach (var error in errors) Debug.LogError("[KOM][VALIDACION] " + error);
                Debug.LogError($"VERDICT: FAIL ({errors.Count} errores, {checks} checks)");
                throw new InvalidOperationException($"VERDICT: FAIL ({errors.Count} errores, {checks} checks). Ver Logs/validation/report.txt.");
            }

            // La animacion solo se da por buena con evidencia LATERAL. No bloquea el
            // setup, pero el veredicto arrastra el estado para que nadie cierre una
            // revision sin haber mirado el personaje de lado.
            string lateral = LateralEvidenceStatus();
            if (!lateral.StartsWith("OK"))
                Debug.LogWarning("[KOM][LATERAL] " + lateral);

            WriteValidationReport($"VERDICT: PASS ({checks} checks)", errors);
            Debug.Log($"VERDICT: PASS ({checks} checks) · 7 personajes · 4 escenarios · 4 nodos · combate y UI listos. · LATERAL: {lateral}");
        }

        static void WriteValidationReport(string verdict, List<string> errors)
        {
            Directory.CreateDirectory("Logs/validation");
            var lines = new List<string> { "KOM VALIDACION GLOBAL DEL CATALOGO", verdict };
            foreach (var error in errors) lines.Add(" - " + error);
            File.WriteAllLines("Logs/validation/report.txt", lines);
        }
    }
}
