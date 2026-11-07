using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KOM
{
    /// <summary>
    /// Precarga de recursos con host persistente.
    ///
    /// El coste real medido no es el disco: es el <b>primer Instantiate de cada
    /// personaje</b> (~780 ms en editor, ~1 ms a partir del segundo). Ese pico es el bind
    /// del AnimatorController: Unity enlaza todos los clips del controller a los huesos la
    /// primera vez que aparece un Animator que lo usa. Por eso el juego se atascaba al
    /// entrar a la selección y al abrir un combate: cada luchador pagaba su bind en el
    /// peor momento posible.
    ///
    /// Estrategia (la de un AAA con recursos limitados):
    ///   1. Pagar TODOS los binds una sola vez, en una pantalla de carga con progreso real.
    ///   2. Conservar una instancia ya montada de cada luchador (inactiva, hija de este
    ///      host en DontDestroyOnLoad) para que la selección la REUTILICE: 0 ms al cambiar
    ///      de personaje.
    ///   3. El host sobrevive al cambio de escena, así que la precarga ya no muere al salir
    ///      de la historia (era el otro motivo de que la selección congelara).
    /// </summary>
    public sealed class KomPreloader : MonoBehaviour
    {
        const int WarmSize = 96;

        static KomPreloader _host;

        /// <summary>
        /// Todos los recursos críticos están listos. El flag vive en el HOST, no en un
        /// static suelto: al salir de Play el host se destruye y un static podía quedar
        /// en true, saltándose la precarga en la siguiente sesión (los previews se
        /// instanciaban al navegar y el juego daba tirones de ~0,8 s).
        /// </summary>
        public static bool IsWarm => _host != null && _host._warmed;
        /// <summary>0..1 del calentamiento.</summary>
        public static float Progress { get; private set; }
        /// <summary>Etiqueta de lo que se está preparando.</summary>
        public static string Label { get; private set; } = "Preparando el torneo";

        readonly Dictionary<string, GameObject> _previews = new Dictionary<string, GameObject>();
        // Segunda instancia por personaje jugable. El selector de dos jugadores dibuja
        // DOS luchadores a la vez (y permite el espejo), asi que necesita una copia
        // caliente de cada uno. Se paga aqui, en la pantalla de carga, y no al pasar
        // las cards: instanciar un rig en el primer hover costaba ~0,8 s por personaje.
        readonly Dictionary<string, GameObject> _previewCopies = new Dictionary<string, GameObject>();
        Coroutine _routine;
        bool _running;
        bool _warmed;
        bool _warmStageGeometry = true;

        public static KomPreloader Ensure()
        {
            if (_host != null) return _host;
            var go = new GameObject("KomPreloader");
            DontDestroyOnLoad(go);
            _host = go.AddComponent<KomPreloader>();
            Progress = 0f;
            Label = "Preparando el torneo";
            return _host;
        }

        /// <summary>Arranca el calentamiento una sola vez. Idempotente.</summary>
        public static void Begin()
        {
            var host = Ensure();
            if (host._running || host._warmed) return;
            host._running = true;
            host._routine = host.StartCoroutine(host.WarmRoutine());
        }

        /// <summary>
        /// Variante esperable: arranca el host persistente y reporta su progreso. Si quien
        /// llama muere (cambio de escena), el calentamiento SIGUE en el host.
        /// </summary>
        public static IEnumerator Warm(System.Action<float, string> onProgress = null, bool warmStageGeometry = true)
        {
            Ensure()._warmStageGeometry = warmStageGeometry;
            Begin();
            while (!IsWarm)
            {
                onProgress?.Invoke(Progress, Label);
                yield return null;
            }
            onProgress?.Invoke(1f, "Todo listo");
        }

        /// <summary>
        /// Devuelve la instancia ya montada de un luchador (inactiva hasta ahora) o null si
        /// todavía no está lista. La instancia sigue siendo hija del host: sobrevive a los
        /// cambios de escena y se reutiliza en la siguiente selección.
        /// </summary>
        public static GameObject TakePreview(string id)
        {
            if (_host == null || string.IsNullOrEmpty(id)) return null;
            if (!_host._previews.TryGetValue(id, out var go) || go == null) return null;
            go.SetActive(true);
            return go;
        }

        /// <summary>
        /// Segunda instancia caliente del personaje (para el jugador 2). Es independiente
        /// de la primera para que el espejo pueda mostrar dos luchadores iguales.
        /// </summary>
        public static GameObject TakePreviewCopy(string id)
        {
            if (_host == null || string.IsNullOrEmpty(id)) return null;
            if (!_host._previewCopies.TryGetValue(id, out var go) || go == null) return null;
            go.SetActive(true);
            return go;
        }

        /// <summary>Vuelve a esconder todas las instancias (al salir de la selección).</summary>
        public static void ReleasePreviews()
        {
            if (_host == null) return;
            foreach (var pair in _host._previews)
                if (pair.Value != null) pair.Value.SetActive(false);
            foreach (var pair in _host._previewCopies)
                if (pair.Value != null) pair.Value.SetActive(false);
        }

        public static bool HasPreview(string id) =>
            _host != null && _host._previews.TryGetValue(id, out var go) && go != null;

        public static bool HasPreviewCopy(string id) =>
            _host != null && _host._previewCopies.TryGetValue(id, out var go) && go != null;

        /// <summary>Copias calientes disponibles (diagnostico del selector de dos jugadores).</summary>
        public static int PreviewCopyCount => _host != null ? _host._previewCopies.Count : 0;

        // ------------------------------------------------------------------ rutina

        IEnumerator WarmRoutine()
        {
            var characters = KomData.Characters.characters;
            var stages = KomData.Stages.stages;
            int charCount = characters != null ? characters.Length : 0;
            int stageCount = stages != null ? stages.Length : 0;

            const float WCharacters = 0.60f;
            const float WStages = 0.22f;
            const float WProps = 0.05f;
            const float WCrowd = 0.02f;
            const float WSprites = 0.11f;
            const float WTotal = WCharacters + WStages + WProps + WCrowd + WSprites;

            float done = 0f;
            float started = Time.realtimeSinceStartup;
            float worstStep = 0f;
            string worstLabel = "-";

            void Report(string label)
            {
                Label = label;
                Progress = Mathf.Clamp01(done / WTotal);
            }
            void Note(float t0, string label)
            {
                float dt = (Time.realtimeSinceStartup - t0) * 1000f;
                if (dt > worstStep) { worstStep = dt; worstLabel = label; }
            }

            // Prioridad alta: mientras se carga no hay juego que atender.
            var previousPriority = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = ThreadPriority.High;

            var warmGo = new GameObject("KomPreloadCamera", typeof(Camera));
            warmGo.transform.SetParent(transform, false);
            warmGo.transform.position = new Vector3(0f, 1.2f, -4.2f);
            warmGo.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.9f, 0f) - warmGo.transform.position);
            var warm = warmGo.GetComponent<Camera>();
            warm.clearFlags = CameraClearFlags.SolidColor;
            warm.backgroundColor = Color.black;
            warm.fieldOfView = 33f;
            warm.nearClipPlane = 0.1f;
            warm.farClipPlane = 40f;
            warm.enabled = false;
            var rt = RenderTexture.GetTemporary(WarmSize, WarmSize, 24);
            warm.targetTexture = rt;

            var lightGo = new GameObject("KomPreloadLight", typeof(Light));
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.rotation = Quaternion.Euler(35f, -28f, 0f);
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1.2f;
            light.enabled = false;

            Report("Preparando el torneo");
            yield return null;

            // --- 1. Luchadores: bind del Animator UNA vez y la instancia se queda viva.
            for (int i = 0; i < charCount; i++)
            {
                var def = characters[i];
                if (def == null || string.IsNullOrEmpty(def.prefabResource)) continue;
                Report($"Luchador · {def.displayName}");
                float t0 = Time.realtimeSinceStartup;

                var request = Resources.LoadAsync<GameObject>(def.prefabResource);
                while (!request.isDone) yield return null;

                if (request.asset is GameObject prefab && !_previews.ContainsKey(def.id))
                {
                    var go = Instantiate(prefab, transform);
                    go.name = "Preview_" + def.id;
                    PreparePreview(go);
                    // Dos renders: compilan las variantes de shader y suben las texturas.
                    light.enabled = true;
                    warm.enabled = true;
                    warm.Render();
                    warm.Render();
                    warm.enabled = false;
                    light.enabled = false;
                    go.SetActive(false);
                    _previews[def.id] = go;
                }

                // Copia caliente para el jugador 2 del selector local. Solo jugables:
                // la jefa nunca se elige y no necesita segunda instancia.
                if (request.asset is GameObject copyPrefab && def.playable && !_previewCopies.ContainsKey(def.id))
                {
                    var copy = Instantiate(copyPrefab, transform);
                    copy.name = "PreviewCopy_" + def.id;
                    PreparePreview(copy);
                    copy.SetActive(false);
                    _previewCopies[def.id] = copy;
                }

                Note(t0, "luchador " + def.id);
                done += WCharacters / Mathf.Max(1, charCount);
                Report("Luchadores listos");
                yield return null;
            }

            // --- 2. Escenarios.
            var loadedStages = new HashSet<string>();
            bool warmedGeometry = false;
            for (int i = 0; i < stageCount; i++)
            {
                var stage = stages[i];
                if (stage == null || string.IsNullOrEmpty(stage.prefabResource)) continue;
                if (!loadedStages.Add(stage.prefabResource)) continue;
                Report($"Escenario · {stage.displayName}");
                float t0 = Time.realtimeSinceStartup;

                var request = Resources.LoadAsync<GameObject>(stage.prefabResource);
                while (!request.isDone) yield return null;

                if (_warmStageGeometry && !warmedGeometry && request.asset is GameObject stagePrefab)
                {
                    warmedGeometry = true;
                    var go = Instantiate(stagePrefab, new Vector3(0f, 0f, 30f), Quaternion.identity);
                    light.enabled = true;
                    warm.enabled = true;
                    warm.Render();
                    warm.enabled = false;
                    light.enabled = false;
                    Destroy(go);
                }

                Note(t0, "escenario " + stage.id);
                done += WStages / Mathf.Max(1, loadedStages.Count + 1);
                Report("Escenarios listos");
                yield return null;
            }

            // --- 3. Props de los especiales.
            string[] props = { "Props/acordeon", "Props/guitarra", "Props/microfono" };
            for (int i = 0; i < props.Length; i++)
            {
                float t0 = Time.realtimeSinceStartup;
                var request = Resources.LoadAsync<GameObject>(props[i]);
                while (!request.isDone) yield return null;
                Note(t0, "prop " + props[i]);
                done += WProps / props.Length;
                yield return null;
            }
            Report("Objetos listos");

            // --- 3-bis. Publico de grada: atlas, material y layouts declarados. El quad
            // de prueba delante de la camara de precarga compila el shader y sube la
            // textura; sin esto el primer frame del combate pagaba la compilacion.
            var crowdLayouts = new HashSet<string>();
            foreach (var stage in stages)
                if (stage != null && !string.IsNullOrEmpty(stage.crowdLayout))
                    crowdLayouts.Add(stage.crowdLayout);
            if (crowdLayouts.Count > 0)
            {
                Report("Público de grada");
                var crowdMatRequest = Resources.LoadAsync<Material>("Crowd/CrowdMat");
                while (!crowdMatRequest.isDone) yield return null;
                var crowdAtlasRequest = Resources.LoadAsync<Texture2D>("Crowd/crowd_atlas");
                while (!crowdAtlasRequest.isDone) yield return null;
                foreach (var layoutPath in crowdLayouts)
                {
                    var layoutRequest = Resources.LoadAsync<TextAsset>(layoutPath);
                    while (!layoutRequest.isDone) yield return null;
                }
                if (crowdMatRequest.asset is Material crowdMat)
                {
                    var warmQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    warmQuad.name = "CrowdWarmQuad";
                    warmQuad.transform.SetParent(transform, false);
                    warmQuad.transform.position = new Vector3(0f, 0.9f, 0f);
                    warmQuad.transform.rotation = Quaternion.LookRotation(warmQuad.transform.position - warmGo.transform.position);
                    warmQuad.transform.localScale = Vector3.one * 0.3f;
                    warmQuad.GetComponent<MeshRenderer>().sharedMaterial = crowdMat;
                    light.enabled = true;
                    warm.enabled = true;
                    warm.Render();
                    warm.Render();
                    warm.enabled = false;
                    light.enabled = false;
                    Destroy(warmQuad);
                }
                done += WCrowd;
                Report("Público listo");
                yield return null;
            }

            // --- 4. Retratos, cabezas y cards de la interfaz.
            var spritePaths = new List<string>();
            if (characters != null)
            {
                foreach (var def in characters)
                {
                    if (def == null) continue;
                    if (!string.IsNullOrEmpty(def.portraitResource)) spritePaths.Add(def.portraitResource);
                    if (!string.IsNullOrEmpty(def.cardResource)) spritePaths.Add(def.cardResource);
                    if (!string.IsNullOrEmpty(def.headResource)) spritePaths.Add(def.headResource);
                }
            }
            for (int i = 0; i < spritePaths.Count; i++)
            {
                UIFactory.LoadSprite(spritePaths[i]);
                done += WSprites / Mathf.Max(1, spritePaths.Count);
                if ((i & 3) == 3) yield return null;
            }
            Report("Todo listo");

            warm.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(warmGo);
            Destroy(lightGo);
            Application.backgroundLoadingPriority = previousPriority;

            float total = Time.realtimeSinceStartup - started;
            Debug.Log($"[KOM] Precarga terminada en {total:0.00} s · peor paso {worstStep:0} ms ({worstLabel}).");

            Progress = 1f;
            Label = "Todo listo";
            _warmed = true;
            _running = false;
            _routine = null;
        }

        /// <summary>
        /// Deja la instancia lista para usarse como preview: sin lógica de combate, sin
        /// colliders, con la pose de Idle aplicada y con su sombra de contacto.
        /// </summary>
        static void PreparePreview(GameObject go)
        {
            var fighter = go.GetComponent<Fighter>();
            if (fighter != null) fighter.enabled = false;

            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.SetLayerWeight(0, 1f);
                if (animator.layerCount > 1) animator.SetLayerWeight(1, 0f);
                animator.Play("Idle", 0, 0f);
                animator.Update(0f);
            }

            KomVisual.StripColliders(go);

            var shadow = KomFx.CreateGroundShadow(go.transform, 1.25f, 0.6f);
            shadow.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        }
    }
}
