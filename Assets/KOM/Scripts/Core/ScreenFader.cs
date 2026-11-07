using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KOM
{
    /// <summary>
    /// Fundido entre escenas + pantalla de carga.
    ///
    /// La pantalla de carga ya no es un adorno: muestra el PROGRESO REAL de dos trabajos que
    /// antes se pagaban a escondidas y a golpes:
    ///   · la precarga (binds de Animator, escenarios, sprites) → <see cref="KomPreloader"/>,
    ///   · la carga asíncrona de la escena.
    ///
    /// La escena se carga con `allowSceneActivation = false`, así que se activa cuando la
    /// barra llega al 100 % y no antes: sin esto Unity cambiaba de escena a mitad de la
    /// barra y el último tramo se veía congelado.
    /// </summary>
    public sealed class ScreenFader : MonoBehaviour
    {
        // Tiempo mínimo con la pantalla de carga visible: por debajo de esto un fundido
        // rápido se lee como un parpadeo y no como una transición.
        const float MinLoadingTime = 0.30f;

        static ScreenFader _instance;
        Image _image;
        GameObject _loadingGroup;
        CanvasGroup _loadingCanvas;
        Image _loadingFill;
        // Sello giratorio y porcentaje: la pantalla de carga tiene que dar senal de
        // vida. Una barra que se llena a tirones no distingue "cargando" de "colgado".
        RectTransform _loadingSeal;
        Text _loadingPercent;
        float _loadingSpin;
        Coroutine _routine;
        bool _isLoading;

        public static ScreenFader Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("ScreenFader");
            DontDestroyOnLoad(go);
            var fader = go.AddComponent<ScreenFader>();
            fader.Build();
            return fader;
        }

        void Build()
        {
            if (_image != null) return;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            // Mismo scaler que el resto de la interfaz (UIFactory.CreateCanvas): con el
            // CanvasScaler por defecto (píxel constante) la pantalla de carga se quedaba
            // en píxeles nativos y, en una vista de 2560x1440, el sello y la barra salían
            // un tercio más pequeños que cualquier otra pantalla del juego.
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var go = new GameObject("Fade", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _image = go.GetComponent<Image>();
            _image.color = Color.black;
            _image.raycastTarget = true;

            BuildLoadingScreen();
        }

        void BuildLoadingScreen()
        {
            _loadingGroup = new GameObject("LoadingGroup", typeof(RectTransform), typeof(CanvasGroup));
            _loadingGroup.transform.SetParent(transform, false);
            var root = (RectTransform)_loadingGroup.transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            _loadingCanvas = _loadingGroup.GetComponent<CanvasGroup>();
            _loadingCanvas.blocksRaycasts = false;
            _loadingCanvas.alpha = 0f;

            // Negro pleno: la pantalla de carga es una pantalla, no un velo.
            var dim = UIFactory.AddImage(root, "Dim", UIFactory.WhiteSprite(), Color.black);
            UIFactory.Stretch(dim.rectTransform);

            var content = UIFactory.Rect(root, "Content");
            content.anchorMin = new Vector2(0.5f, 0.5f);
            content.anchorMax = new Vector2(0.5f, 0.5f);
            content.pivot = new Vector2(0.5f, 0.5f);
            content.sizeDelta = new Vector2(900f, 400f);
            content.anchoredPosition = Vector2.zero;

            // Solo el sello, la barra y el porcentaje. Sin rotulos: una pantalla de
            // carga no necesita explicar que esta cargando, solo mostrar que avanza.
            var logo = UIFactory.LoadSprite("UI/Logo/kom_logo");
            if (logo != null)
            {
                var seal = UIFactory.AddImage(content, "Seal", logo, Color.white);
                seal.preserveAspect = true;
                // Pivote en el CENTRO: el sello tiene que girar sobre si mismo, en su
                // sitio. Con pivote en el borde superior (0.5, 1) orbitaba alrededor de
                // su esquina y parecia que se salia de la pantalla. El anclaje sigue
                // siendo el borde superior del bloque, asi que se compensa media altura.
                UIFactory.Place(seal.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, -134f), new Vector2(268f, 268f));
                _loadingSeal = seal.rectTransform;
            }

            var track = UIFactory.AddImage(content, "Track", UIFactory.WhiteSprite(), new Color(1f, 1f, 1f, 0.12f));
            UIFactory.Place(track.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 72f), new Vector2(780f, 7f));

            _loadingFill = UIFactory.AddImage(track.transform, "Fill", UIFactory.WhiteSprite(), KomTheme.TextHi);
            UIFactory.Stretch(_loadingFill.rectTransform);
            _loadingFill.type = Image.Type.Filled;
            _loadingFill.fillMethod = Image.FillMethod.Horizontal;
            _loadingFill.fillOrigin = 0;
            _loadingFill.fillAmount = 0f;

            // Porcentaje bajo la barra: es la unica cifra de la pantalla y va en la
            // tipografia de sistema del HUD (mayusculas, cuerpo bajo, color tenue).
            _loadingPercent = UIFactory.AddText(content, "Percent", "0 %", 18, KomTheme.TextMid,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_loadingPercent.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 32f), new Vector2(240f, 24f));
            var percentShadow = _loadingPercent.gameObject.AddComponent<Shadow>();
            percentShadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            percentShadow.effectDistance = new Vector2(0f, -1f);

            _loadingGroup.SetActive(false);
        }

        void Update()
        {
            if (_loadingGroup == null || !_loadingGroup.activeSelf) return;
            if (_loadingSeal == null) return;
            // Giro continuo del sello. 42 grados/s: se lee como un disco girando, no
            // como un spinner de espera; y al ser constante, no distrae.
            _loadingSpin = Mathf.Repeat(_loadingSpin + Time.unscaledDeltaTime * 42f, 360f);
            _loadingSeal.localRotation = Quaternion.Euler(0f, 0f, -_loadingSpin);
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            Build();
        }

        public static void FadeIn()
        {
            var fader = Ensure();
            if (fader._image == null) return;
            if (fader._isLoading) return;
            if (fader._image.color.a <= 0.01f) return;
            if (fader._routine != null) fader.StopCoroutine(fader._routine);
            fader._routine = fader.StartCoroutine(fader.FadeRoutine(0f, 0.3f, false));
        }

        /// <summary>
        /// Cambia de escena con pantalla de carga. Con <paramref name="preloadFirst"/> se paga
        /// la precarga completa (binds de los siete luchadores incluidos) ANTES de activar la
        /// escena, con la barra mostrando el progreso real.
        /// </summary>
        public static void Load(string sceneName, bool preloadFirst = false)
        {
            var fader = Ensure();
            if (fader._isLoading) return;
            fader._isLoading = true;
            Time.timeScale = 1f;
            if (fader._routine != null) fader.StopCoroutine(fader._routine);
            fader._routine = fader.StartCoroutine(fader.LoadRoutine(sceneName, preloadFirst));
        }

        IEnumerator LoadRoutine(string sceneName, bool preloadFirst)
        {
            yield return FadeRoutine(1f, 0.18f, true);

            ShowLoadingInternal();
            float started = Time.realtimeSinceStartup;
            float preloadShare = preloadFirst ? 0.75f : 0f;
            SetProgressInternal(0f, "Preparando el torneo");

            if (preloadFirst && !KomPreloader.IsWarm)
            {
                KomPreloader.Begin();
                while (!KomPreloader.IsWarm)
                {
                    SetProgressInternal(KomPreloader.Progress * preloadShare, KomPreloader.Label);
                    yield return null;
                }
                SetProgressInternal(preloadShare, "Cargando escenario");
            }

            var op = SceneManager.LoadSceneAsync(sceneName);
            if (op != null)
            {
                // Se activa cuando la barra llega arriba: sin esto la escena cambiaba a
                // mitad de barra y el tramo final se quedaba congelado.
                op.allowSceneActivation = false;
                while (op.progress < 0.9f)
                {
                    SetProgressInternal(Mathf.Lerp(preloadShare, 0.96f, op.progress / 0.9f), "Cargando escenario");
                    yield return null;
                }
                SetProgressInternal(1f, "Todo listo");

                float elapsed = Time.realtimeSinceStartup - started;
                if (elapsed < MinLoadingTime) yield return new WaitForSecondsRealtime(MinLoadingTime - elapsed);

                op.allowSceneActivation = true;
                while (!op.isDone) yield return null;
            }

            // Un frame para que la escena nueva haga su Start antes de descubrirla.
            yield return null;
            HideLoadingInternal();
            yield return FadeRoutine(0f, 0.26f, false);
            _isLoading = false;
            _routine = null;
        }

        /// <summary>Muestra la pantalla de carga (API pública para otros sistemas).</summary>
        public static void ShowLoading()
        {
            var fader = Ensure();
            fader.ShowLoadingInternal();
        }

        /// <summary>Fija el progreso y la etiqueta de la pantalla de carga.</summary>
        public static void SetProgress(float value, string label)
        {
            var fader = Ensure();
            fader.SetProgressInternal(value, label);
        }

        public static void HideLoading()
        {
            if (_instance != null) _instance.HideLoadingInternal();
        }

        void ShowLoadingInternal()
        {
            if (_loadingGroup == null) return;
            _loadingGroup.SetActive(true);
            _loadingGroup.transform.SetAsLastSibling();
            _loadingCanvas.alpha = 0f;
            SetProgressInternal(0f, "Preparando el torneo");
        }

        void HideLoadingInternal()
        {
            if (_loadingGroup == null) return;
            _loadingGroup.SetActive(false);
        }

        // `label` se conserva por compatibilidad de llamadas, pero no se muestra.
        void SetProgressInternal(float value, string label)
        {
            value = Mathf.Clamp01(value);
            if (_loadingFill != null) _loadingFill.fillAmount = value;
            if (_loadingPercent != null) _loadingPercent.text = Mathf.RoundToInt(value * 100f) + " %";
            // Aparece de golpe: el fundido a negro ya la ha tapado.
            if (_loadingCanvas != null && _loadingCanvas.alpha < 1f) _loadingCanvas.alpha = 1f;
        }

        IEnumerator FadeRoutine(float targetAlpha, float duration, bool block)
        {
            _image.raycastTarget = block || targetAlpha > 0.01f;
            float start = _image.color.a;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, duration));
                t = t * t * (3f - 2f * t);
                var c = _image.color;
                c.a = Mathf.Lerp(start, targetAlpha, t);
                _image.color = c;
                yield return null;
            }
            var final = _image.color;
            final.a = targetAlpha;
            _image.color = final;
            _image.raycastTarget = block && targetAlpha > 0.5f;
            if (targetAlpha <= 0.01f) _image.raycastTarget = false;
        }
    }
}
