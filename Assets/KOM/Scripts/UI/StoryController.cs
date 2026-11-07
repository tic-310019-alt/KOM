using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KOM
{
    /// <summary>
    /// Introduccion narrativa: cuatro ilustraciones en blanco y negro presentadas como
    /// una secuencia cinematografica.
    ///
    /// Puesta en escena:
    ///  - La ilustracion ocupa la pantalla completa con un movimiento lentisimo de
    ///    camara (Ken Burns) alternando el sentido en cada pagina.
    ///  - Un velo negro de caida convexa protege el pie de foto: el tercio alto de la
    ///    imagen queda intacto y la parte baja se hunde en negro.
    ///  - El texto entra escalonado (antetitulo, titulo, filete, cuerpo, cita) con un
    ///    desplazamiento corto hacia arriba; nada aparece de golpe.
    ///  - Grano de pelicula a baja opacidad y vineta: textura de copia fotografica.
    ///  - El avance es automatico con un tiempo de lectura proporcional al texto, pero
    ///    se puede adelantar o retroceder. La ultima pagina espera confirmacion.
    ///
    /// Mientras tanto, en segundo plano, <see cref="KomPreloader"/> calienta luchadores,
    /// escenarios y sprites: la historia hace de pantalla de carga, sin barra ni corte.
    /// </summary>
    public sealed class StoryController : MonoBehaviour
    {
        enum Phase { In, Read, Out, Closing }

        const float TransitionDuration = 0.95f;
        const float IntroFade = 0.75f;
        const float BottomMargin = 116f;
        const float BodyWidth = 880f;
        const float TitleWidth = 980f;
        const float DefaultWash = 0.70f;

        sealed class Reveal
        {
            public RectTransform rt;
            public Graphic graphic;
            public Vector2 basePos;
            public float baseAlpha = 1f;
            public float delay;
            public float duration = 0.5f;
            public float rise = 18f;
            public float baseWidth;
            public bool growWidth;
        }

        CanvasGroup _root;
        readonly Image[] _layers = new Image[2];
        readonly RectTransform[] _layerRt = new RectTransform[2];
        int _front;

        Text _kicker;
        Text _title;
        Text _body;
        Text _quote;
        RectTransform _ruleRt;
        readonly List<Reveal> _reveals = new List<Reveal>();

        Text _counter;
        Text _hint;
        Text _skip;
        Image _textWash;
        Button _advanceButton;
        readonly Image[] _tickBg = new Image[4];
        readonly Image[] _tickFill = new Image[4];
        RawImage _grain;
        Image _black;

        StoryChapterDef _chapter;
        StoryPageDef[] _pages = new StoryPageDef[0];
        int _page;
        float _t;
        float _dwell = 11f;
        float _outT;
        Phase _phase = Phase.Read;
        float _inputLock = 0.5f;
        float _clock;
        float _textFade = 1f;
        bool _started;
        bool _preloadDone;

        // ------------------------------------------------------------------ ciclo

        void Start()
        {
            BuildCamera();
            BuildUI();

            var catalog = KomData.Story;
            _chapter = catalog != null ? catalog.Find(StoryFlow.Chapter) : null;
            if (_chapter == null && catalog != null) _chapter = catalog.First;
            _pages = _chapter != null && _chapter.pages != null ? _chapter.pages : new StoryPageDef[0];
            if (_pages.Length == 0)
            {
                Debug.LogWarning("[KOM] story.json sin paginas: se salta la historia.");
                ScreenFader.Load(NextScene());
                return;
            }

            _started = true;
            _layers[0].sprite = LoadPageSprite(0);
            _layers[0].color = Color.white;
            ApplyKenBurns(_layerRt[0], 0f, 0);
            ApplyPage(0);

            // La historia no lleva música: se apaga el tema y queda el murmullo del
            // estadio, más bajo cuanto más íntimo es el momento.
            KomAudio.Silence();
            KomAudio.Ambience(ChapterAmbience(_chapter != null ? _chapter.id : null));
            if (_chapter != null && _chapter.id == StoryFlow.Concert) KomAudio.Concert();
            // La víspera de la final se cuenta con el tema del Zócalo ya sonando: la
            // narración y la pelea comparten pista, así que al entrar al combate la música
            // no se reinicia, solo sube. El silencio previo no era tensión, era un hueco.
            else if (_chapter != null && _chapter.id == StoryFlow.Final)
                KomAudio.SceneMusic("Audio/Music/combat_3", 0.44f);

            StartCoroutine(KomPreloader.Warm((progress, label) =>
            {
                if (progress < 1f || _preloadDone) return;
                _preloadDone = true;
                Debug.Log("[KOM] Recursos calientes antes de la seleccion de personaje.");
            }));

            ScreenFader.FadeIn();
        }

        // El público nunca desaparece del todo: está en el anuncio, en el veto y en el
        // palacio. Solo cambia de volumen.
        static float ChapterAmbience(string chapterId)
        {
            switch (chapterId)
            {
                case StoryFlow.Concert: return KomAudio.AmbienceConcert;
                case StoryFlow.Veto: return 0.18f;
                case StoryFlow.Final: return 0.26f;
                default: return KomAudio.AmbienceStory;
            }
        }

        void Update()
        {
            if (!_started) return;
            float dt = Time.unscaledDeltaTime;
            _clock += dt;
            _inputLock -= dt;
            _root.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_clock / IntroFade));

            if (_phase == Phase.Closing) return;

            if (_phase == Phase.Out)
            {
                StepTransition(dt);
            }
            else
            {
                _t += dt;
                if (_phase == Phase.Read && _page < _pages.Length - 1 && _t >= _dwell) Advance();
            }

            AnimateLayers();
            AnimateText();
            AnimateChrome();
            HandleInput();
        }

        // ------------------------------------------------------------------ entrada

        void HandleInput()
        {
            if (_inputLock > 0f) return;

            if (InputReader.CancelPressed())
            {
                BeginOutro();
                return;
            }
            if (InputReader.MoveLeftPressed() && _page > 0 && _phase != Phase.Out)
            {
                Previous();
                return;
            }
            if (InputReader.ConfirmPressed() || InputReader.MoveRightPressed())
            {
                if (_phase == Phase.Out) return;
                if (_page >= _pages.Length - 1) BeginOutro();
                else Advance();
            }
        }

        // Un clic en cualquier parte (o el boton Sur del mando via EventSystem) avanza.
        void OnClickAdvance()
        {
            if (_inputLock > 0f || _phase == Phase.Out || _phase == Phase.Closing) return;
            if (_page >= _pages.Length - 1) BeginOutro();
            else Advance();
        }

        void Advance()
        {
            if (_phase == Phase.Out || _phase == Phase.Closing) return;
            if (_page >= _pages.Length - 1)
            {
                BeginOutro();
                return;
            }

            _phase = Phase.Out;
            _outT = 0f;
            _inputLock = 0.2f;

            int next = _page + 1;
            int back = 1 - _front;
            _layers[back].sprite = LoadPageSprite(next);
            _layers[back].color = new Color(1f, 1f, 1f, 0f);
            // La entrante tiene que quedar ARRIBA de la saliente: si se quedara debajo,
            // el fundido ocurriria por detras de una capa opaca y la imagen no cambiaria.
            _layers[back].transform.SetAsLastSibling();
            ApplyKenBurns(_layerRt[back], 0f, next);
        }

        void StepTransition(float dt)
        {
            _outT += dt;
            float k = Mathf.Clamp01(_outT / TransitionDuration);
            int back = 1 - _front;

            float imgK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.14f, 0.94f, k));
            var c = _layers[back].color;
            c.a = imgK;
            _layers[back].color = c;
            _textFade = 1f - Mathf.Clamp01(k / 0.28f);

            if (k < 1f) return;

            _front = back;
            _layers[_front].color = Color.white;
            _page++;
            _phase = Phase.Read;
            _t = 0f;
            _textFade = 1f;
            ApplyPage(_page);
        }

        void Previous()
        {
            if (_page <= 0) return;
            _page--;
            _phase = Phase.Read;
            _t = 0f;
            _textFade = 1f;
            _inputLock = 0.15f;
            _layers[_front].sprite = LoadPageSprite(_page);
            _layers[_front].color = Color.white;
            _layers[_front].transform.SetAsLastSibling();
            ApplyKenBurns(_layerRt[_front], 0f, _page);
            ApplyPage(_page);
        }

        void BeginOutro()
        {
            if (_phase == Phase.Closing) return;
            _phase = Phase.Closing;
            _root.alpha = 1f;
            if (_advanceButton != null) _advanceButton.interactable = false;
            StartCoroutine(OutroRoutine());
        }

        IEnumerator OutroRoutine()
        {
            const float dur = 0.65f;
            float t = 0f;
            var c = _black.color;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                c.a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                _black.color = c;
                yield return null;
            }
            c.a = 1f;
            _black.color = c;
            ScreenFader.Load(NextScene());
        }

        // Escena a la que se vuelve al terminar el capitulo. Si alguien abre la escena
        // directamente en el editor (sin pasar por StoryFlow) cae en la seleccion.
        static string NextScene()
        {
            return string.IsNullOrEmpty(StoryFlow.NextScene) ? KomScenes.CharacterSelect : StoryFlow.NextScene;
        }

        // ------------------------------------------------------------------ imagen

        Sprite LoadPageSprite(int index)
        {
            if (_pages == null || index < 0 || index >= _pages.Length) return null;
            return UIFactory.LoadSprite(_pages[index].image);
        }

        void AnimateLayers()
        {
            if (_phase != Phase.In && _phase != Phase.Read) return;
            var rt = _layerRt[_front];
            if (rt == null) return;
            float p = _dwell > 0f ? Mathf.Clamp01(_t / _dwell) : 1f;
            ApplyKenBurns(rt, p, _page);
        }

        // Ken Burns: zoom muy lento y deriva corta. La amplitud del paneo (14 px) cabe
        // dentro del margen que deja el zoom (1.045 deja 43 px por lado), asi que nunca
        // se ve el borde de la ilustracion.
        void ApplyKenBurns(RectTransform rt, float p, int page)
        {
            if (rt == null) return;
            bool reverse = (page & 1) == 1;
            float t = reverse ? 1f - p : p;
            float zoom = Mathf.Lerp(1.045f, 1.105f, t);
            rt.localScale = new Vector3(zoom, zoom, 1f);
            Vector2 from = reverse ? new Vector2(13f, -9f) : new Vector2(-14f, 10f);
            Vector2 to = reverse ? new Vector2(-13f, 9f) : new Vector2(14f, -10f);
            rt.anchoredPosition = Vector2.Lerp(from, to, t);
        }

        // ------------------------------------------------------------------ texto

        void ApplyPage(int index)
        {
            if (index < 0 || index >= _pages.Length) return;
            var page = _pages[index];
            if (page == null) return;

            _kicker.text = Track(page.kicker);
            _title.text = page.title;
            _body.text = page.body;
            _quote.text = string.IsNullOrEmpty(page.quote) ? string.Empty : "\u201C" + page.quote + "\u201D";

            LayoutTextBlock();
            BuildReveals();

            if (_textWash != null)
                _textWash.color = new Color(1f, 1f, 1f, page.wash > 0f ? page.wash : DefaultWash);

            _dwell = page.dwell > 0f
                ? page.dwell
                : Mathf.Clamp(6.5f + page.WordCount * 0.115f, 9f, 16.5f);
            _t = 0f;
            _phase = Phase.Read;
            _textFade = 1f;
        }

        void LayoutTextBlock()
        {
            float quoteH = Measure(_quote, BodyWidth);
            float bodyH = Measure(_body, BodyWidth);
            float titleH = Measure(_title, TitleWidth);
            float kickerH = Measure(_kicker, TitleWidth);

            float yQuote = BottomMargin;
            float yBody = yQuote + (quoteH > 0f ? quoteH + 20f : 0f);
            float yRule = yBody + bodyH + 26f;
            float yTitle = yRule + 15f;
            float yKicker = yTitle + titleH + 8f;

            Place(_quote.rectTransform, BodyWidth, quoteH, yQuote);
            Place(_body.rectTransform, BodyWidth, bodyH, yBody);
            _ruleRt.sizeDelta = new Vector2(64f, 2f);
            _ruleRt.anchoredPosition = new Vector2(150f, yRule);
            Place(_title.rectTransform, TitleWidth, titleH, yTitle);
            Place(_kicker.rectTransform, TitleWidth, kickerH, yKicker);
        }

        static void Place(RectTransform rt, float width, float height, float y)
        {
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(150f, y);
        }

        static float Measure(Text text, float width)
        {
            if (text == null) return 0f;
            text.rectTransform.sizeDelta = new Vector2(width, 0f);
            if (string.IsNullOrEmpty(text.text)) return 0f;
            return Mathf.Max(0f, text.preferredHeight);
        }

        void BuildReveals()
        {
            _reveals.Clear();
            AddReveal(_kicker, 0.34f, 0.45f, 14f, 0.86f);
            AddReveal(_title, 0.44f, 0.55f, 20f, 1f);
            AddReveal(_ruleRt.GetComponent<Graphic>(), 0.62f, 0.50f, 0f, 0.55f, true);
            AddReveal(_body, 0.66f, 0.60f, 16f, 0.93f);
            AddReveal(_quote, 0.90f, 0.55f, 12f, 0.80f);
        }

        void AddReveal(Graphic graphic, float delay, float duration, float rise, float alpha, bool growWidth = false)
        {
            if (graphic == null) return;
            var rt = graphic.rectTransform;
            _reveals.Add(new Reveal
            {
                rt = rt,
                graphic = graphic,
                basePos = rt.anchoredPosition,
                baseAlpha = alpha,
                delay = delay,
                duration = duration,
                rise = rise,
                baseWidth = rt.sizeDelta.x,
                growWidth = growWidth
            });
        }

        void AnimateText()
        {
            for (int i = 0; i < _reveals.Count; i++)
            {
                var r = _reveals[i];
                float k = Mathf.Clamp01((_t - r.delay) / Mathf.Max(0.0001f, r.duration));
                float e = Mathf.SmoothStep(0f, 1f, k);
                var c = r.graphic.color;
                c.a = r.baseAlpha * e * _textFade;
                r.graphic.color = c;
                r.rt.anchoredPosition = r.basePos + new Vector2(0f, -r.rise * (1f - e));
                if (r.growWidth)
                    r.rt.sizeDelta = new Vector2(r.baseWidth * e, r.rt.sizeDelta.y);
            }
        }

        void AnimateChrome()
        {
            // Un capitulo de una sola pagina es un momento entre combates, no una lectura
            // por entregas: no lleva contador ni barras, solo la invitacion a continuar.
            bool single = _pages.Length <= 1;
            bool last = _page >= _pages.Length - 1;
            float appear = Mathf.Clamp01((_t - (single ? 1.9f : 1.1f)) / 0.6f);

            _counter.enabled = !single;
            if (!single)
            {
                var cc = _counter.color;
                cc.a = 0.72f * appear;
                _counter.color = cc;
                _counter.text = $"{_page + 1:00} / {_pages.Length:00}";
            }

            float pulse = 0.55f + 0.30f * Mathf.Sin(_clock * 2.4f);
            var hc = _hint.color;
            hc.a = (last ? Mathf.Max(pulse, 0.72f) : pulse) * appear;
            _hint.color = hc;

            string exit = _chapter != null && !string.IsNullOrEmpty(_chapter.hint)
                ? _chapter.hint
                : KomLoc.T("COMENZAR EL TORNEO", "START THE TOURNAMENT");
            _hint.text = last
                ? KomLoc.T("ENTER · ", "ENTER · ") + exit
                : KomLoc.T("ENTER · CONTINUAR", "ENTER · CONTINUE");

            var sc = _skip.color;
            sc.a = 0.44f * Mathf.Clamp01((_t - (single ? 2.4f : 1.6f)) / 0.8f);
            _skip.color = sc;

            for (int i = 0; i < _tickFill.Length; i++)
            {
                bool has = !single && i < _pages.Length;
                _tickBg[i].enabled = has;
                _tickFill[i].enabled = has;
                if (!has) continue;
                float fill = i < _page ? 1f : i > _page ? 0f : Mathf.Clamp01(_t / Mathf.Max(0.001f, _dwell));
                if (last && i == _page) fill = 1f;
                _tickFill[i].fillAmount = fill;
                var tc = _tickFill[i].color;
                tc.a = 0.9f * appear;
                _tickFill[i].color = tc;
                var bc = _tickBg[i].color;
                bc.a = 0.22f * appear;
                _tickBg[i].color = bc;
            }

            if (_grain != null)
            {
                var r = _grain.uvRect;
                float s = Mathf.Floor(_clock * 12f);
                r.x = Mathf.Repeat(s * 0.371f, 1f);
                r.y = Mathf.Repeat(s * 0.619f, 1f);
                _grain.uvRect = r;
            }
        }

        // ------------------------------------------------------------------ construccion

        void BuildCamera()
        {
            if (Camera.main != null) return;
            var go = new GameObject("StoryCamera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            go.transform.position = new Vector3(0f, 0f, -10f);
        }

        void BuildUI()
        {
            var canvas = UIFactory.CreateCanvas("StoryCanvas");
            var root = (RectTransform)canvas.transform;
            _root = canvas.gameObject.AddComponent<CanvasGroup>();
            _root.alpha = 0f;

            var bg = UIFactory.AddImage(root, "Bg", UIFactory.WhiteSprite(), Color.black);
            UIFactory.Stretch(bg.rectTransform);

            // Las dos capas de ilustracion viven en su propio contenedor: asi se puede
            // subir la entrante al frente del par sin que se dibuje por encima del velo,
            // del grano ni del texto.
            var pageRoot = UIFactory.Rect(root, "Pages");
            UIFactory.Stretch(pageRoot);
            for (int i = 0; i < 2; i++)
            {
                var img = UIFactory.AddImage(pageRoot, "Page" + i, null, new Color(1f, 1f, 1f, 0f));
                UIFactory.Stretch(img.rectTransform);
                // Las ilustraciones no son todas 16:9 exactas (1.78–1.83): se encajan sin
                // deformar y el fondo negro se come las tiras que sobren.
                img.preserveAspect = true;
                _layers[i] = img;
                _layerRt[i] = img.rectTransform;
            }

            var top = UIFactory.AddImage(root, "TopShade",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0.55f), new Color(0f, 0f, 0f, 0f)), Color.white);
            top.rectTransform.anchorMin = new Vector2(0f, 1f);
            top.rectTransform.anchorMax = new Vector2(1f, 1f);
            top.rectTransform.pivot = new Vector2(0.5f, 1f);
            top.rectTransform.sizeDelta = new Vector2(0f, 210f);
            top.rectTransform.anchoredPosition = Vector2.zero;

            var scrim = UIFactory.AddImage(root, "BottomScrim", UIFactory.ScrimSprite(0.96f, 1.15f), Color.white);
            scrim.rectTransform.anchorMin = new Vector2(0f, 0f);
            scrim.rectTransform.anchorMax = new Vector2(1f, 0f);
            scrim.rectTransform.pivot = new Vector2(0.5f, 0f);
            scrim.rectTransform.sizeDelta = new Vector2(0f, 820f);
            scrim.rectTransform.anchoredPosition = Vector2.zero;

            // Mancha eliptica bajo el bloque de texto: refuerza la legibilidad justo donde
            // hay letras (el velo inferior por si solo no cubre la mitad derecha del cuerpo).
            var wash = UIFactory.AddImage(root, "TextWash", UIFactory.WashSprite(), new Color(1f, 1f, 1f, DefaultWash));
            UIFactory.Place(wash.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(640f, 322f), new Vector2(1560f, 800f));
            _textWash = wash;

            UIFactory.AddVignette(root, 0.5f);

            var grainRt = UIFactory.Rect(root, "Grain");
            UIFactory.Stretch(grainRt);
            _grain = grainRt.gameObject.AddComponent<RawImage>();
            _grain.texture = UIFactory.NoiseSprite().texture;
            _grain.color = new Color(1f, 1f, 1f, 0.055f);
            _grain.raycastTarget = false;
            _grain.uvRect = new Rect(0f, 0f, 4.2f, 2.36f);

            BuildTextBlock(root);
            BuildChrome(root);

            // Superficie de avance a pantalla completa: un clic en cualquier sitio pasa la
            // pagina, y con un Selectable el boton Sur del mando (Submit) tambien funciona
            // sin depender de leer el teclado a mano.
            var hit = UIFactory.AddImage(root, "AdvanceHit", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0f));
            UIFactory.Stretch(hit.rectTransform);
            hit.raycastTarget = true;
            _advanceButton = hit.gameObject.AddComponent<Button>();
            _advanceButton.transition = Selectable.Transition.None;
            _advanceButton.navigation = new Navigation { mode = Navigation.Mode.None };
            _advanceButton.onClick.AddListener(OnClickAdvance);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_advanceButton.gameObject);

            var black = UIFactory.AddImage(root, "Black", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0f));
            UIFactory.Stretch(black.rectTransform);
            black.raycastTarget = true;
            _black = black;
        }

        void BuildTextBlock(RectTransform root)
        {
            var light = new Color(0.975f, 0.965f, 0.935f);

            _kicker = UIFactory.AddText(root, "Kicker", "", 17, new Color(light.r, light.g, light.b, 0.86f),
                TextAnchor.LowerLeft, FontStyle.Normal);
            _kicker.rectTransform.anchorMin = Vector2.zero;
            _kicker.rectTransform.anchorMax = Vector2.zero;
            _kicker.rectTransform.pivot = Vector2.zero;
            _kicker.horizontalOverflow = HorizontalWrapMode.Overflow;

            _title = UIFactory.AddText(root, "Title", "", 56, light, TextAnchor.LowerLeft, FontStyle.Normal);
            _title.font = UIFactory.DisplayFont;
            _title.rectTransform.anchorMin = Vector2.zero;
            _title.rectTransform.anchorMax = Vector2.zero;
            _title.rectTransform.pivot = Vector2.zero;
            _title.lineSpacing = 0.98f;
            AddShadow(_title, 0.62f);

            var rule = UIFactory.AddImage(root, "Rule", UIFactory.WhiteSprite(), new Color(light.r, light.g, light.b, 0.55f));
            rule.rectTransform.anchorMin = Vector2.zero;
            rule.rectTransform.anchorMax = Vector2.zero;
            rule.rectTransform.pivot = Vector2.zero;
            _ruleRt = rule.rectTransform;

            _body = UIFactory.AddText(root, "Body", "", 26, new Color(light.r, light.g, light.b, 0.98f), TextAnchor.LowerLeft);
            _body.rectTransform.anchorMin = Vector2.zero;
            _body.rectTransform.anchorMax = Vector2.zero;
            _body.rectTransform.pivot = Vector2.zero;
            _body.lineSpacing = 1.4f;
            AddShadow(_body, 0.55f);

            _quote = UIFactory.AddText(root, "Quote", "", 23, new Color(light.r, light.g, light.b, 0.86f), TextAnchor.LowerLeft);
            _quote.font = UIFactory.DisplayFont;
            _quote.rectTransform.anchorMin = Vector2.zero;
            _quote.rectTransform.anchorMax = Vector2.zero;
            _quote.rectTransform.pivot = Vector2.zero;
            _quote.lineSpacing = 1.2f;
            AddShadow(_quote, 0.5f);
        }

        // Sombra corta y casi negra: separa el texto del dibujo sin dibujar un contorno.
        // useGraphicAlpha hace que acompane al fundido de entrada en vez de quedarse fija.
        static void AddShadow(Text text, float alpha)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, alpha);
            shadow.effectDistance = new Vector2(0f, -2f);
            shadow.useGraphicAlpha = true;
        }

        void BuildChrome(RectTransform root)
        {
            // Esquina inferior derecha: el contador y las barras son blancos finos y en
            // algunas paginas caen sobre una pantalla luminosa. Una mancha suave los
            // asienta sin dibujar un panel.
            var corner = UIFactory.AddImage(root, "ChromeWash", UIFactory.WashSprite(), new Color(1f, 1f, 1f, 0.6f));
            UIFactory.Place(corner.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(-200f, 150f), new Vector2(760f, 420f));

            var skipWash = UIFactory.AddImage(root, "SkipWash", UIFactory.WashSprite(), new Color(1f, 1f, 1f, 0.5f));
            UIFactory.Place(skipWash.rectTransform, Vector2.one, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(-150f, -60f), new Vector2(480f, 210f));

            _skip = UIFactory.AddText(root, "Skip", KomLoc.T("ESC · SALTAR HISTORIA", "ESC · SKIP STORY"), 14,
                new Color(0.92f, 0.92f, 0.9f, 0.34f), TextAnchor.UpperRight, FontStyle.Normal);
            UIFactory.Place(_skip.rectTransform, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-70f, -54f), new Vector2(420f, 24f));
            AddShadow(_skip, 0.6f);

            _counter = UIFactory.AddText(root, "Counter", "01 / 04", 15,
                new Color(0.92f, 0.92f, 0.9f, 0.55f), TextAnchor.LowerRight, FontStyle.Normal);
            UIFactory.Place(_counter.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-70f, 168f), new Vector2(260f, 22f));
            AddShadow(_counter, 0.6f);

            var ticks = UIFactory.Rect(root, "Ticks");
            UIFactory.Place(ticks, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-70f, 142f), new Vector2(4f * 44f, 2f));
            for (int i = 0; i < 4; i++)
            {
                var bg = UIFactory.AddImage(ticks, "TickBg" + i, UIFactory.WhiteSprite(), new Color(1f, 1f, 1f, 0.22f));
                bg.rectTransform.anchorMin = new Vector2(1f, 0.5f);
                bg.rectTransform.anchorMax = new Vector2(1f, 0.5f);
                bg.rectTransform.pivot = new Vector2(1f, 0.5f);
                bg.rectTransform.sizeDelta = new Vector2(40f, 2f);
                bg.rectTransform.anchoredPosition = new Vector2(-(3 - i) * 44f, 0f);

                var fill = UIFactory.AddImage(bg.rectTransform, "TickFill" + i, UIFactory.WhiteSprite(), new Color(1f, 1f, 1f, 0.9f));
                UIFactory.Stretch(fill.rectTransform);
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = 0;
                fill.fillAmount = 0f;
                _tickBg[i] = bg;
                _tickFill[i] = fill;
            }

            _hint = UIFactory.AddText(root, "Hint", KomLoc.T("ENTER · CONTINUAR", "ENTER · CONTINUE"), 16,
                new Color(0.95f, 0.94f, 0.9f, 0.7f), TextAnchor.LowerRight, FontStyle.Normal);
            _hint.font = UIFactory.DisplayFont;
            UIFactory.Place(_hint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-70f, 100f), new Vector2(560f, 26f));
            AddShadow(_hint, 0.6f);
        }

        // ------------------------------------------------------------------ utilidades

        // Tracking manual: uGUI no tiene letter-spacing y el antetitulo en versalitas
        // necesita aire entre letras para leerse como portada y no como etiqueta.
        static string Track(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length * 2);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ' ')
                {
                    sb.Append("  ");
                    continue;
                }
                sb.Append(c);
                if (i < s.Length - 1) sb.Append(' ');
            }
            return sb.ToString();
        }
    }
}
