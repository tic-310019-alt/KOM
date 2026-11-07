using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KOM
{
    /// <summary>
    /// Seleccion de luchador y presentacion del rival.
    ///
    /// La pantalla es un TRIPTICO separado por filetes de 1 px:
    ///
    ///   identidad (izquierda)  |  el luchador en 3D (centro)  |  el rival (derecha)
    ///
    /// Cada luchador aparece UNA sola vez: el elegido esta en el centro y su ficha a la
    /// izquierda; el hueco de la derecha pertenece al rival, que empieza como incognita.
    /// La tira inferior es el elenco completo y el unico selector.
    ///
    /// La misma escena tiene dos modos:
    ///   Select  · se elige luchador, la ruleta sortea al primer rival y se va al mapa.
    ///   Reveal  · el jugador ya esta fijo; la ruleta presenta al rival del proximo
    ///             encuentro y de ahi se entra directo al combate.
    /// </summary>
    public sealed class CharacterSelectController : MonoBehaviour
    {
        enum Phase { Browse, Locking, Roulette, Versus, Leaving }

        sealed class CardUI
        {
            public GameObject root;
            public RectTransform rect;
            public Image art;
            public UIFactory.CardAura aura;
            public Color accent;
            public Image locked;
            public GameObject tag;
            public Text tagLabel;
            public Button button;
            public CharacterDef def;
            public float baseX;
            public float width;
            public float height;
            public float focus;
            public float hot;
        }

        sealed class VersusPick
        {
            public int index;
            public bool locked;
            public CharacterDef hover;
            public GameObject preview;
            public string previewId;
            public Image card;
            public UIFactory.CardAura cardAura;
            public Text name;
            public Text genre;
            public Text special;
            public Text status;
            public Text device;
            public Image accent;
        }

        sealed class VersusCardUI
        {
            public RectTransform root;
            public Image art;
            public UIFactory.CardAura aura;
            public RectTransform p1Frame;
            public RectTransform p2Frame;
            public GameObject p1Mark;
            public GameObject p2Mark;
            public Text p1Label;
            public Text p2Label;
            public CharacterDef def;
            public Vector2 basePos;
            public float hot1;
            public float hot2;
        }

        GameSession _session;
        bool _revealMode;
        // Dos jugadores locales: los dos eligen a la vez sobre el mismo elenco.
        bool _versus;
        readonly List<CharacterDef> _all = new List<CharacterDef>();
        readonly List<CharacterDef> _playable = new List<CharacterDef>();
        readonly List<CardUI> _cards = new List<CardUI>();
        CharacterDef _selected;
        CharacterDef _rival;
        int _selectedIndex;
        Phase _phase = Phase.Browse;

        Camera _camera;
        Canvas _canvas;
        RectTransform _browseUi;
        RectTransform _strip;
        RectTransform _scan;
        Text _scanLabel;

        GameObject _playerPreview;
        GameObject _enemyPreview;
        readonly Dictionary<string, GameObject> _previewPool = new Dictionary<string, GameObject>();

        Text _infoName;
        Text _infoGenre;
        Text _infoTagline;
        Text _infoSpecial;
        Text _infoSpecialSub;
        readonly Image[] _statFills = new Image[3];
        readonly Text[] _statValues = new Text[3];

        GameObject _rivalUnknown;
        GameObject _rivalKnown;
        Image _rivalCard;
        Image _rivalFlash;
        UIFactory.CardAura _rivalAura;
        Text _rivalName;
        Text _rivalGenre;

        Button _confirmButton;
        Text _confirmLabel;

        // Selector simultaneo de dos jugadores. Cada jugador tiene su propio cursor,
        // su ficha y su preview; el elenco es UNO solo y se comparte.
        FighterControls _p1Menu;
        FighterControls _p2Menu;
        VersusPick _p1Pick;
        VersusPick _p2Pick;
        readonly List<VersusCardUI> _versusCards = new List<VersusCardUI>();
        readonly Dictionary<string, GameObject> _versusP1Pool = new Dictionary<string, GameObject>();
        readonly Dictionary<string, GameObject> _versusP2Pool = new Dictionary<string, GameObject>();
        CanvasGroup _versusReadyGroup;
        Text _versusReadyTitle;
        Text _versusReadyNames;
        bool _versusFinishing;

        RectTransform _versusUi;
        Text _vsText;
        Text _playerVsName;
        Text _enemyVsName;
        Text _playerVsGenre;
        Text _enemyVsGenre;
        Text _vsTag;
        Button _nextButton;
        Text _nextLabel;

        Image _flash;
        CanvasGroup _toastGroup;
        Text _toastText;
        Coroutine _toastRoutine;
        Coroutine _flashRoutine;

        // Encuadres medidos sobre 1920x1080: el luchador solo ocupa la columna central,
        // con los pies por encima de la tira de tarjetas.
        static readonly Vector3 SoloCamPos = new Vector3(0f, 1.12f, -5.05f);
        static readonly Vector3 SoloCamLook = new Vector3(0f, 0.62f, 0.30f);
        static readonly Vector3 DuoCamPos = new Vector3(0f, 1.30f, -5.30f);
        static readonly Vector3 DuoCamLook = new Vector3(0f, 0.80f, 0.15f);

        static readonly Vector3 PlayerSoloPos = new Vector3(0f, 0f, 0.30f);
        static readonly Vector3 PlayerDuoPos = new Vector3(-1.14f, 0f, 0.15f);
        static readonly Vector3 EnemyDuoPos = new Vector3(1.22f, 0f, 0.15f);
        // El selector simultaneo deja 330 px de ficha a cada lado: los luchadores se
        // acercan un poco al centro para que ni el brazo de Juan ni la guitarra de Alex
        // invadan los paneles.
        static readonly Vector3 VersusPlayerPos = new Vector3(-1.00f, 0f, 0.15f);
        static readonly Vector3 VersusEnemyPos = new Vector3(1.04f, 0f, 0.15f);

        // Pose del retrato: el perfil de combate (90°) con 18° hacia camara, que es la
        // misma que usa el juego para presentar a un luchador. De frente (152°) se veian
        // los defectos del modelo y la pose no contaba nada.
        static readonly Quaternion SoloPose = KomVisual.PreviewRotation(1f);
        // Plano de dos: misma pose 3/4 y ESPEJADA exacta. El rival estaba a 238 grados
        // (238 = 118 + 120, no el espejo de 118): se le veia la cara a camara en vez de
        // cuadrarse con el jugador. El espejo de 118 es -118.
        // Los dos usan el MISMO 3/4 canonico (PreviewRotation), espejado: el jugador a 108
        // y el rival a -108 (252). Antes el rival estaba a 238 y se le veia la cara a
        // camara en vez de cuadrarse con el jugador.
        static readonly Quaternion PlayerDuoPose = KomVisual.PreviewRotation(1f);
        static readonly Quaternion EnemyDuoPose = KomVisual.PreviewRotation(-1f);

        const float StripCardH = 214f;
        // 34 px: el aura de cada tarjeta necesita aire para no mezclarse con la vecina.
        const float StripGap = 34f;
        const float StripY = 66f;
        const float ColumnTop = 910f;
        const float ColumnBottom = 320f;

        void Start()
        {
            _session = GameSession.Ensure();
            ClearStalePreviews();
            BuildData();
            _versus = _session.VersusMode;
            _revealMode = _session.HasRun && !_versus;
            _selected = _revealMode ? _session.PlayerDef : FirstPlayable();
            _rival = _revealMode ? _session.CurrentOpponentDef : null;

            BuildCamera();
            BuildDiorama();

            if (_versus)
            {
                BuildVersusSelectUI();
                KomAudio.Menu();
                ScreenFader.FadeIn();
                return;
            }

            BuildUI();
            Select(IndexOf(_selected));

            KomAudio.Menu();
            ScreenFader.FadeIn();

            if (_revealMode) StartCoroutine(RevealSequence());
            else StartCoroutine(IntroSequence());
        }

        public void BuildEditorPreview()
        {
            _session = GameSession.Ensure();
            ClearStalePreviews();
            BuildData();
            _versus = false;
            _revealMode = false;
            // La preview se reconstruye sobre la MISMA instancia del controlador: si la
            // anterior era de revelacion o VS, la fase quedaba en Locking/Roulette y
            // Select(0) salia por su guarda sin ficha ni personaje. Se resetea siempre.
            _phase = Phase.Browse;
            _selected = FirstPlayable();
            BuildCamera();
            BuildDiorama();
            BuildUI();
            Select(0);
        }

        /// <summary>Preview del modo revelacion (ruleta + VS) sin tocar la partida guardada.</summary>
        public void BuildRevealPreview()
        {
            _session = GameSession.Ensure();
            if (!_session.HasRun) _session.BeginRun("juan_grabriel");
            BuildData();
            _versus = false;
            _revealMode = true;
            _phase = Phase.Browse;
            _selected = _session.PlayerDef;
            _rival = _session.CurrentOpponentDef;
            BuildCamera();
            BuildDiorama();
            BuildUI();
            SelectFixed(_selected);
        }

        /// <summary>Preview del cartel de VS: jugador y rival ya plantados en la arena.</summary>
        public void BuildVersusPreview()
        {
            _session = GameSession.Ensure();
            if (!_session.HasRun) _session.BeginRun("juan_grabriel");
            BuildData();
            _versus = false;
            _revealMode = true;
            _phase = Phase.Browse;
            _selected = _session.PlayerDef;
            _rival = _session.CurrentOpponentDef;
            BuildCamera();
            BuildDiorama();
            BuildUI();
            SelectFixed(_selected);
            if (_rival == null) return;

            RevealRival(_rival, false);

            if (_playerPreview != null)
            {
                _playerPreview.transform.SetPositionAndRotation(PlayerDuoPos, PlayerDuoPose);
                _playerPreview.transform.localScale = Vector3.one;
            }
            if (_enemyPreview != null) _enemyPreview.SetActive(false);
            _enemyPreview = SpawnPreview(_rival, EnemyDuoPos, EnemyDuoPose);

            _camera.transform.position = DuoCamPos;
            _camera.transform.rotation = Quaternion.LookRotation(DuoCamLook - DuoCamPos);
            _camera.fieldOfView = 33f;

            ShowVersus(_rival);
        }

        /// <summary>
        /// Preview del selector simultaneo de dos jugadores. No toca la partida guardada:
        /// monta el mismo estado que crea el menu al pulsar 2 JUGADORES.
        /// </summary>
        public void BuildVersusSelectPreview()
        {
            _session = GameSession.Ensure();
            if (!_session.VersusMode) _session.BeginVersus();
            _session.SetVersusFighter(1, "juan_grabriel");
            _session.SetVersusFighter(2, "alex_llora");
            ClearStalePreviews();
            BuildData();
            _versus = true;
            _revealMode = false;
            _phase = Phase.Browse;
            _selected = FirstPlayable();
            BuildCamera();
            BuildDiorama();
            BuildVersusSelectUI();
        }

        /// <summary>
        /// Una preview del editor puede dejar luchadores sueltos en la escena. Si se entra
        /// en Play con ellos dentro se ven DOS veces el mismo personaje.
        /// </summary>
        void ClearStalePreviews()
        {
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>())
            {
                if (t != null && t.parent == null && t.name.StartsWith("Preview_"))
                    Destroy(t.gameObject);
            }
        }

        void BuildData()
        {
            _all.Clear();
            _playable.Clear();
            _cards.Clear();
            var chars = KomData.Characters.characters;
            if (chars == null) return;
            foreach (var c in chars)
            {
                if (c == null) continue;
                _all.Add(c);
                if (c.playable) _playable.Add(c);
            }
        }

        CharacterDef FirstPlayable()
        {
            foreach (var c in _all) if (c.playable) return c;
            return _all.Count > 0 ? _all[0] : null;
        }

        int IndexOf(CharacterDef def)
        {
            if (def == null) return 0;
            int i = _all.FindIndex(d => d.id == def.id);
            return i < 0 ? 0 : i;
        }

        void BuildCamera()
        {
            var camGo = new GameObject("SelectCamera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            _camera = camGo.GetComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 80f;
            _camera.fieldOfView = 32f;
            _camera.transform.position = SoloCamPos;
            _camera.transform.rotation = Quaternion.LookRotation(SoloCamLook - SoloCamPos);
            // La seleccion es una ficha de personaje, no una fotografia: el grano de
            // pelicula ensuciaba el retrato y el fondo por igual.
            VisualMood.Apply(_camera, filmGrain: false);
        }

        void BuildDiorama()
        {
            var accent = KomData.UiAccent(_selected != null ? _selected : FirstPlayable());
            StageBuilder.BuildSelectDiorama(accent, _versus);
        }

        // ------------------------------------------------------------------ estructura

        void BuildUI()
        {
            UIFactory.DestroyCanvas(_canvas);
            _canvas = UIFactory.CreateCanvas("SelectCanvas");
            var root = (RectTransform)_canvas.transform;

            _browseUi = UIFactory.Rect(root, "BrowseUI");
            UIFactory.Stretch(_browseUi);

            var vignette = UIFactory.AddVignette(_browseUi, 0.55f);
            vignette.transform.SetAsFirstSibling();

            var leftShade = UIFactory.AddImage(_browseUi, "LeftShade", UIFactory.HorizontalScrim(0.92f, 1.7f), Color.white);
            leftShade.rectTransform.anchorMin = new Vector2(0f, 0f);
            leftShade.rectTransform.anchorMax = new Vector2(0f, 1f);
            leftShade.rectTransform.pivot = new Vector2(0f, 0.5f);
            leftShade.rectTransform.sizeDelta = new Vector2(760f, 0f);
            leftShade.rectTransform.anchoredPosition = Vector2.zero;

            var rightShade = UIFactory.AddImage(_browseUi, "RightShade", UIFactory.HorizontalScrim(0.86f, 1.4f), Color.white);
            rightShade.rectTransform.anchorMin = new Vector2(1f, 0f);
            rightShade.rectTransform.anchorMax = new Vector2(1f, 1f);
            rightShade.rectTransform.pivot = new Vector2(1f, 0.5f);
            rightShade.rectTransform.sizeDelta = new Vector2(520f, 0f);
            rightShade.rectTransform.anchoredPosition = Vector2.zero;
            rightShade.rectTransform.localScale = new Vector3(-1f, 1f, 1f);

            var bottom = UIFactory.AddImage(_browseUi, "BottomShade",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.98f)), Color.white);
            bottom.rectTransform.anchorMin = new Vector2(0f, 0f);
            bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottom.rectTransform.pivot = new Vector2(0.5f, 0f);
            bottom.rectTransform.sizeDelta = new Vector2(0f, 380f);
            bottom.rectTransform.anchoredPosition = Vector2.zero;

            BuildHeader();
            BuildColumns();
            BuildInfoPanel();
            BuildRivalPanel();
            BuildStrip();
            BuildRoulette();
            BuildFooter();
            BuildVsUi();
            BuildToast();
            BuildFlash();

            _browseUi.SetAsFirstSibling();
            _versusUi.SetAsLastSibling();
        }

        void BuildHeader()
        {
            var kicker = UIFactory.AddLabel(_browseUi, "Kicker",
                _revealMode
                    ? KomLoc.T("P1 · El torneo continúa", "P1 · The tournament continues")
                    : KomLoc.T("P1 · Elige al representante de tu género", "P1 · Choose your genre's champion"));
            UIFactory.Place(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(KomTheme.Margin, -44f), new Vector2(760f, 20f));

            var title = UIFactory.AddDisplayText(_browseUi, "Title",
                _revealMode ? KomLoc.T("TU SIGUIENTE RIVAL", "YOUR NEXT RIVAL") : KomLoc.T("SELECCIONA TU LUCHADOR", "SELECT YOUR FIGHTER"),
                44, KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(KomTheme.Margin - 4f, -64f), new Vector2(980f, 58f));

            // Cabecera derecha: numero de encuentro y recinto. No repite al rival.
            int total = Mathf.Max(1, _session.NodeCount);
            int current = Mathf.Clamp(_session.EncounterIndex + 1, 1, total);
            var counter = UIFactory.AddText(_browseUi, "Counter",
                KomLoc.T($"ENCUENTRO {current:00} / {total:00}", $"ENCOUNTER {current:00} / {total:00}"), 15,
                KomTheme.TextMid, TextAnchor.UpperRight, FontStyle.Bold);
            UIFactory.Place(counter.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-KomTheme.Margin, -46f), new Vector2(420f, 20f));

            var node = _session.CurrentNode;
            var stage = node != null ? KomData.GetStage(node.stageId) : null;
            var venue = UIFactory.AddText(_browseUi, "Venue",
                stage != null ? stage.displayName.ToUpperInvariant() : KomLoc.T("TORNEO DEL BIENESTAR", "WELLBEING TOURNAMENT"),
                11, KomTheme.TextLo, TextAnchor.UpperRight, FontStyle.Bold);
            UIFactory.Place(venue.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-KomTheme.Margin, -72f), new Vector2(420f, 18f));

            var rule = UIFactory.AddHLine(_browseUi, "HeaderRule", KomTheme.Line, 1f);
            UIFactory.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -142f), new Vector2(-2f * KomTheme.Margin, 1f));
        }

        /// <summary>Los dos filetes verticales que convierten la pantalla en un triptico.</summary>
        void BuildColumns()
        {
            float height = ColumnTop - ColumnBottom;
            float centerY = (ColumnTop + ColumnBottom) * 0.5f;

            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? 560f : 1500f;
                var line = UIFactory.AddVLine(_browseUi, "ColumnRule" + i, KomTheme.LineFaint, 1f);
                UIFactory.Place(line.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f),
                    new Vector2(x, centerY), new Vector2(1f, height));

                // Un remate en rombo arriba y abajo: el filete se lee como intencion.
                var top = UIFactory.AddDiamond(_browseUi, "RuleTop" + i, KomTheme.Line, 4f);
                UIFactory.Place(top.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f),
                    new Vector2(x, ColumnTop), new Vector2(4f, 4f));
                var bottom = UIFactory.AddDiamond(_browseUi, "RuleBottom" + i, KomTheme.Line, 4f);
                UIFactory.Place(bottom.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f),
                    new Vector2(x, ColumnBottom), new Vector2(4f, 4f));
            }
        }

        void BuildInfoPanel()
        {
            var panel = UIFactory.Rect(_browseUi, "InfoPanel");
            panel.anchorMin = new Vector2(0f, 1f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.sizeDelta = new Vector2(430f, 540f);
            panel.anchoredPosition = new Vector2(KomTheme.Margin, -170f);

            var rule = UIFactory.AddImage(panel, "Accent", UIFactory.WhiteSprite(), KomTheme.Accent);
            UIFactory.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(44f, 2f));

            _infoName = UIFactory.AddDisplayText(panel, "Name", "—", 50, KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(_infoName.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(-3f, -14f), new Vector2(430f, 62f));
            _infoName.resizeTextForBestFit = true;
            _infoName.resizeTextMinSize = 28;
            _infoName.resizeTextMaxSize = 50;

            _infoGenre = UIFactory.AddLabel(panel, "Genre", "", TextAnchor.UpperLeft);
            UIFactory.Place(_infoGenre.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -84f), new Vector2(430f, 18f));

            _infoTagline = UIFactory.AddText(panel, "Tagline", "", KomTheme.Small, KomTheme.TextMid, TextAnchor.UpperLeft);
            UIFactory.Place(_infoTagline.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -110f), new Vector2(410f, 46f));
            _infoTagline.lineSpacing = 1.12f;

            string[] labels =
            {
                KomLoc.T("VIDA", "HEALTH"),
                KomLoc.T("VELOCIDAD", "SPEED"),
                KomLoc.T("PODER", "POWER")
            };
            for (int i = 0; i < 3; i++)
            {
                float y = -186f - i * 32f;
                var lab = UIFactory.AddLabel(panel, "StatLabel" + i, labels[i], TextAnchor.MiddleLeft);
                UIFactory.Place(lab.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, y), new Vector2(96f, 18f));

                var track = UIFactory.AddImage(panel, "StatTrack" + i, UIFactory.WhiteSprite(), new Color(0.145f, 0.150f, 0.156f, 1f));
                UIFactory.Place(track.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(98f, y - 5f), new Vector2(240f, 6f));

                var fill = UIFactory.AddImage(track.transform, "Fill", UIFactory.WhiteSprite(), KomTheme.AccentHot);
                UIFactory.Stretch(fill.rectTransform);
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = 0;
                fill.fillAmount = 1f;
                _statFills[i] = fill;

                var value = UIFactory.AddText(panel, "StatValue" + i, "", 13, KomTheme.TextMid, TextAnchor.MiddleRight, FontStyle.Bold);
                UIFactory.Place(value.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(344f, y), new Vector2(72f, 18f));
                _statValues[i] = value;
            }

            var divider = UIFactory.AddHLine(panel, "Divider", KomTheme.LineFaint, 1f);
            UIFactory.Place(divider.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -294f), new Vector2(410f, 1f));

            var specialKicker = UIFactory.AddLabel(panel, "SpecialKicker", KomLoc.T("Poder especial", "Special power"), TextAnchor.UpperLeft);
            UIFactory.Place(specialKicker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -310f), new Vector2(410f, 18f));

            _infoSpecial = UIFactory.AddText(panel, "Special", "", 19, KomTheme.TextHi, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(_infoSpecial.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -332f), new Vector2(420f, 26f));

            _infoSpecialSub = UIFactory.AddText(panel, "SpecialSub", "", KomTheme.Micro, KomTheme.TextLo, TextAnchor.UpperLeft);
            UIFactory.Place(_infoSpecialSub.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -358f), new Vector2(420f, 32f));

            // Accion. En modo revelacion no hay nada que confirmar: manda la ruleta.
            _confirmButton = UIFactory.AddPrimaryButton(_browseUi, "Confirm",
                _revealMode ? KomLoc.T("SORTEANDO…", "DRAWING…") : KomLoc.T("CONFIRMAR LUCHADOR", "CONFIRM FIGHTER"), ConfirmSelection, 22, 400f, 62f);
            UIFactory.Place(_confirmButton.image.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(KomTheme.Margin, -640f), new Vector2(400f, 62f));
            _confirmLabel = _confirmButton.GetComponentInChildren<Text>();

            if (_revealMode)
            {
                _confirmButton.interactable = false;
                _confirmButton.image.color = new Color(0.075f, 0.08f, 0.085f, 1f);
                if (_confirmLabel != null) _confirmLabel.color = new Color(KomTheme.TextLo.r, KomTheme.TextLo.g, KomTheme.TextLo.b, 0.75f);
            }
        }

        /// <summary>
        /// Columna derecha: el rival. Empieza como incognita y es el unico sitio de la
        /// pantalla donde se muestra al oponente, asi que no compite con el luchador
        /// elegido (que ya esta en 3D y en la tira).
        /// </summary>
        void BuildRivalPanel()
        {
            var head = UIFactory.AddLabel(_browseUi, "RivalKicker",
                _revealMode
                    ? KomLoc.T("Rival de este encuentro", "Rival for this encounter")
                    : KomLoc.T("Rival por revelar", "Rival to be revealed"), TextAnchor.MiddleCenter);
            UIFactory.Place(head.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                new Vector2(1674f, 846f), new Vector2(360f, 18f));

            var slot = UIFactory.Rect(_browseUi, "RivalSlot");
            UIFactory.Place(slot, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(1674f, 610f), new Vector2(286f, 418f));

            var frame = UIFactory.AddPanel(slot, "Surface", new Color(0.043f, 0.047f, 0.051f, 0.92f), 2);
            UIFactory.Stretch(frame.rectTransform);
            UIFactory.AddFrame(slot, "Border", KomTheme.Line, 1f);
            UIFactory.AddCornerBrackets(slot, "Brackets", KomTheme.LineStrong, 22f, 2f);

            _rivalUnknown = UIFactory.Rect(slot, "Unknown").gameObject;
            UIFactory.Stretch((RectTransform)_rivalUnknown.transform);
            // Solo la incognita: la carta no explica el sorteo, el sorteo se ve ocurrir.
            var q = UIFactory.AddDisplayText(_rivalUnknown.transform, "Question", "?", 92, KomTheme.TextMid, TextAnchor.MiddleCenter);
            UIFactory.Place(q.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(200f, 120f));

            _rivalKnown = UIFactory.Rect(slot, "Known").gameObject;
            UIFactory.Stretch((RectTransform)_rivalKnown.transform);
            _rivalCard = UIFactory.AddImage(_rivalKnown.transform, "Card", null, Color.white);
            _rivalCard.preserveAspect = true;
            UIFactory.Stretch(_rivalCard.rectTransform, 2f, 2f, 2f, 2f);
            _rivalFlash = UIFactory.AddImage(_rivalKnown.transform, "Flash", UIFactory.WhiteSprite(), new Color(1f, 1f, 1f, 0f));
            UIFactory.Stretch(_rivalFlash.rectTransform);
            _rivalKnown.SetActive(false);

            _rivalName = UIFactory.AddDisplayText(_browseUi, "RivalName", "—", 26, KomTheme.TextHi, TextAnchor.MiddleCenter);
            UIFactory.Place(_rivalName.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                new Vector2(1674f, 372f), new Vector2(400f, 32f));

            _rivalGenre = UIFactory.AddText(_browseUi, "RivalGenre", "", KomTheme.Micro, KomTheme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_rivalGenre.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                new Vector2(1674f, 348f), new Vector2(400f, 18f));
        }

        void BuildStrip()
        {
            _strip = UIFactory.Rect(_browseUi, "Strip");
            _strip.anchorMin = new Vector2(0.5f, 0f);
            _strip.anchorMax = new Vector2(0.5f, 0f);
            _strip.pivot = new Vector2(0.5f, 0f);
            _strip.sizeDelta = new Vector2(1500f, StripCardH + 60f);
            _strip.anchoredPosition = new Vector2(0f, StripY);

            // Se centra el grupo: con siete tarjetas y un hueco fijo, el ancho sobrante se
            // reparte a los lados para que la tira quede opticamente centrada.
            float total = 0f;
            var widths = new float[_all.Count];
            for (int i = 0; i < _all.Count; i++)
            {
                var sprite = UIFactory.LoadSprite(_all[i].cardResource);
                widths[i] = sprite != null ? StripCardH * (sprite.rect.width / sprite.rect.height) : StripCardH * 0.68f;
                total += widths[i] + (i > 0 ? StripGap : 0f);
            }
            float x = (_strip.sizeDelta.x - total) * 0.5f;

            for (int i = 0; i < _all.Count; i++)
            {
                var card = BuildCard(i, x, widths[i]);
                _cards.Add(card);
                x += widths[i] + StripGap;
            }
        }

        CardUI BuildCard(int index, float x, float width)
        {
            var def = _all[index];
            var sprite = UIFactory.LoadSprite(def.cardResource);
            float h = StripCardH;
            float w = width;

            var holder = UIFactory.Rect(_strip, "Card" + index);
            holder.anchorMin = new Vector2(0f, 0f);
            holder.anchorMax = new Vector2(0f, 0f);
            holder.pivot = new Vector2(0f, 0f);
            holder.sizeDelta = new Vector2(w, h);
            holder.anchoredPosition = new Vector2(x, 0f);

            var art = UIFactory.AddImage(holder, "Art", sprite, new Color(0.62f, 0.62f, 0.62f, 1f));
            art.preserveAspect = true;
            UIFactory.Stretch(art.rectTransform);
            art.raycastTarget = true;

            // Aura del color del personaje: contorno + resplandor. Es lo que separa la
            // tarjeta del fondo negro y lo que hace que las siete se lean como un elenco
            // y no como siete PNG pegados.
            var accent = KomData.Accent(def);
            var aura = UIFactory.AddCardAura(art, accent, 0f);

            var button = art.gameObject.AddComponent<Button>();
            button.targetGraphic = art;
            button.transition = Selectable.Transition.None;
            int captured = index;
            button.onClick.AddListener(() => OnCardClicked(captured));

            Image locked = null;
            if (!def.playable)
            {
                locked = UIFactory.AddImage(holder, "Locked", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0.46f));
                UIFactory.Stretch(locked.rectTransform);
            }

            var tag = UIFactory.Rect(holder, "Tag");
            tag.anchorMin = new Vector2(0f, 1f);
            tag.anchorMax = new Vector2(0f, 1f);
            tag.pivot = new Vector2(0f, 0f);
            tag.sizeDelta = new Vector2(w, 18f);
            tag.anchoredPosition = new Vector2(0f, 6f);
            var tagLabel = UIFactory.AddLabel(tag, "Label", "", TextAnchor.MiddleCenter);
            UIFactory.Stretch(tagLabel.rectTransform);
            tag.gameObject.SetActive(false);

            if (!def.playable)
            {
                tag.gameObject.SetActive(true);
                tagLabel.text = KomLoc.T("JEFA FINAL", "FINAL BOSS");
                tagLabel.color = KomTheme.TextLo;
            }
            else if (_revealMode && _selected != null && def.id == _selected.id)
            {
                tag.gameObject.SetActive(true);
                tagLabel.text = KomLoc.T("TÚ", "YOU");
                tagLabel.color = KomTheme.AccentHot;
            }

            return new CardUI
            {
                root = holder.gameObject,
                rect = holder,
                art = art,
                aura = aura,
                accent = accent,
                locked = locked,
                tag = tag.gameObject,
                tagLabel = tagLabel,
                button = button,
                def = def,
                baseX = x,
                width = w,
                height = h
            };
        }

        void BuildRoulette()
        {
            _scan = UIFactory.Rect(_strip, "Scan");
            _scan.anchorMin = new Vector2(0f, 0f);
            _scan.anchorMax = new Vector2(0f, 0f);
            _scan.pivot = new Vector2(0.5f, 0.5f);
            _scan.sizeDelta = new Vector2(260f, StripCardH + 40f);
            _scan.anchoredPosition = new Vector2(0f, StripCardH * 0.5f);

            var glow = UIFactory.AddGlow(_scan, "Glow", new Color(1f, 1f, 1f, 0.16f));
            UIFactory.Stretch(glow.rectTransform);

            // Sin las dos verticales del scan: la tira va inclinada y dos lineas rectas
            // a plomo no seguian la orientacion de las cards; parecian un error de render.
            _scan.gameObject.SetActive(false);

            // Rotulo del sorteo: va en la banda del pie, como un ticker. Sobre la tira
            // competia con el luchador y tapaba sus piernas.
            _scanLabel = UIFactory.AddDisplayText(_browseUi, "ScanLabel", KomLoc.T("SORTEANDO EL RIVAL", "DRAWING THE RIVAL"), 24, KomTheme.TextHi, TextAnchor.MiddleCenter);
            UIFactory.Place(_scanLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 12f), new Vector2(720f, 30f));
            _scanLabel.gameObject.SetActive(false);
        }

        void BuildFooter()
        {
            var bar = UIFactory.Rect(_browseUi, "Footer");
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(0f, 54f);
            bar.anchoredPosition = Vector2.zero;

            var rule = UIFactory.AddHLine(bar, "Rule", KomTheme.LineFaint, 1f);
            rule.rectTransform.anchorMin = new Vector2(0f, 1f);
            rule.rectTransform.anchorMax = new Vector2(1f, 1f);
            rule.rectTransform.pivot = new Vector2(0.5f, 1f);
            rule.rectTransform.anchoredPosition = Vector2.zero;

            // El pie solo dice como se navega: el recuento del elenco ya lo dice la tira.
            float cursor = -KomTheme.Margin;
            if (!_revealMode)
            {
                cursor -= AddFooterHint(bar, cursor, "ENTER", KomLoc.T("Confirmar", "Confirm"));
                cursor -= 26f;
                cursor -= AddFooterHint(bar, cursor, "A / D", KomLoc.T("Elegir", "Choose"));
                cursor -= 26f;
            }
            cursor -= AddFooterHint(bar, cursor, "ESC", KomLoc.T("Volver", "Back"));
        }

        float AddFooterHint(Transform parent, float right, string key, string verb)
        {
            var verbLabel = UIFactory.AddLabel(parent, "Hint_" + key, verb, TextAnchor.MiddleRight);
            float verbWidth = 92f;
            UIFactory.Place(verbLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(right, 0f), new Vector2(verbWidth, 18f));

            float keyWidth = Mathf.Max(44f, 18f + key.Length * 11f);
            var cap = UIFactory.AddKeycap(parent, "Key_" + key, key, keyWidth);
            UIFactory.Place(cap, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(right - verbWidth - 10f, 0f), new Vector2(keyWidth, 26f));

            return verbWidth + 10f + keyWidth;
        }

        // ------------------------------------------------------------------ VS

        void BuildVsUi()
        {
            _versusUi = UIFactory.Rect(_canvas.transform, "VersusUI");
            UIFactory.Stretch(_versusUi);
            _versusUi.gameObject.SetActive(false);

            // El VS es un cartel, no una sala a oscuras: el velo solo tiene que separar
            // los nombres de la escena, no apagar a los dos luchadores.
            var dim = UIFactory.AddImage(_versusUi, "Dim", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0.20f));
            UIFactory.Stretch(dim.rectTransform);

            var bottom = UIFactory.AddImage(_versusUi, "BottomFade",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.92f)), Color.white);
            UIFactory.Place(bottom.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0f, 460f));

            var top = UIFactory.AddImage(_versusUi, "TopFade",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0.88f), new Color(0f, 0f, 0f, 0f)), Color.white);
            UIFactory.Place(top.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 240f));

            _vsTag = UIFactory.AddLabel(_versusUi, "Tag", "", TextAnchor.MiddleCenter);
            UIFactory.Place(_vsTag.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -96f), new Vector2(900f, 20f));

            var venue = UIFactory.AddDisplayText(_versusUi, "Venue", "", 30, KomTheme.TextMid, TextAnchor.MiddleCenter);
            UIFactory.Place(venue.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -122f), new Vector2(900f, 40f));
            venue.name = "Venue";

            // Filete central: divide el cartel en dos mitades sin dibujar ningun marco.
            var line = UIFactory.AddVLine(_versusUi, "CenterLine", KomTheme.Line, 1f);
            UIFactory.Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 520f), new Vector2(1f, 560f));

            _vsText = UIFactory.AddDisplayText(_versusUi, "VS", "VS", 84, KomTheme.TextHi, TextAnchor.MiddleCenter);
            UIFactory.Place(_vsText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 34f), new Vector2(300f, 120f));

            var vsDiamond = UIFactory.AddDiamond(_versusUi, "VsDiamond", KomTheme.Accent, 7f);
            UIFactory.Place(vsDiamond.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 118f), new Vector2(7f, 7f));

            _playerVsName = UIFactory.AddDisplayText(_versusUi, "PlayerName", "", 54, KomTheme.TextHi, TextAnchor.LowerLeft);
            UIFactory.Place(_playerVsName.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(KomTheme.Margin, 190f), new Vector2(720f, 70f));
            _playerVsName.resizeTextForBestFit = true;
            _playerVsName.resizeTextMinSize = 26;
            _playerVsName.resizeTextMaxSize = 54;

            _playerVsGenre = UIFactory.AddLabel(_versusUi, "PlayerGenre", "", TextAnchor.LowerLeft);
            UIFactory.Place(_playerVsGenre.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(KomTheme.Margin, 160f), new Vector2(720f, 20f));

            _enemyVsName = UIFactory.AddDisplayText(_versusUi, "EnemyName", "", 54, KomTheme.TextHi, TextAnchor.LowerRight);
            UIFactory.Place(_enemyVsName.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-KomTheme.Margin, 190f), new Vector2(720f, 70f));
            _enemyVsName.resizeTextForBestFit = true;
            _enemyVsName.resizeTextMinSize = 26;
            _enemyVsName.resizeTextMaxSize = 54;

            _enemyVsGenre = UIFactory.AddLabel(_versusUi, "EnemyGenre", "", TextAnchor.LowerRight);
            UIFactory.Place(_enemyVsGenre.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-KomTheme.Margin, 160f), new Vector2(720f, 20f));

            var playerRule = UIFactory.AddHLine(_versusUi, "PlayerRule", KomTheme.LineStrong, 1f);
            UIFactory.Place(playerRule.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(KomTheme.Margin, 268f), new Vector2(240f, 1f));

            var enemyRule = UIFactory.AddHLine(_versusUi, "EnemyRule", KomTheme.LineStrong, 1f);
            UIFactory.Place(enemyRule.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-KomTheme.Margin, 268f), new Vector2(240f, 1f));

            _nextButton = UIFactory.AddPrimaryButton(_versusUi, "NextButton", KomLoc.T("PELEAR  ·  ENTER", "FIGHT  ·  ENTER"), GoNext, 22, 340f, 60f);
            UIFactory.Place(_nextButton.image.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 56f), new Vector2(340f, 60f));
            _nextLabel = _nextButton.GetComponentInChildren<Text>();

            UIFactory.AddCornerBrackets(_versusUi, "Frame", KomTheme.LineFaint, 46f, 1f);
        }

        void BuildToast()
        {
            var panel = UIFactory.AddPanel(_canvas.transform, "Toast", KomTheme.Surface, 4);
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 8f), new Vector2(760f, 40f));
            UIFactory.AddFrame(panel.transform, "Border", KomTheme.Line, 1f);
            _toastGroup = panel.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.alpha = 0f;
            _toastGroup.blocksRaycasts = false;
            _toastText = UIFactory.AddText(panel.transform, "Text", "", KomTheme.Small, KomTheme.TextMid, TextAnchor.MiddleCenter);
            UIFactory.Stretch(_toastText.rectTransform, 12f, 4f, 12f, 4f);
        }

        void BuildFlash()
        {
            _flash = UIFactory.AddImage(_canvas.transform, "Flash", UIFactory.WhiteSprite(), new Color(1f, 1f, 1f, 0f));
            UIFactory.Stretch(_flash.rectTransform);
            _flash.raycastTarget = false;
        }

        // ------------------------------------------------------------------ interaccion

        void Update()
        {
            if (_versus)
            {
                UpdateVersusSelect();
                AnimateVersusCards(Time.unscaledDeltaTime);
                return;
            }

            if (_phase == Phase.Browse && !_revealMode)
            {
                if (InputReader.MoveLeftPressed()) Select((_selectedIndex - 1 + _all.Count) % _all.Count);
                if (InputReader.MoveRightPressed()) Select((_selectedIndex + 1) % _all.Count);
                if (InputReader.ConfirmPressed()) ConfirmSelection();
                if (InputReader.CancelPressed()) ScreenFader.Load(KomScenes.MainMenu);
            }
            else if (_phase == Phase.Browse && _revealMode)
            {
                if (InputReader.CancelPressed()) ScreenFader.Load(KomScenes.MainMenu);
            }
            else if (_phase == Phase.Versus)
            {
                if (InputReader.ConfirmPressed()) GoNext();
            }

            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                float target = i == _selectedIndex ? 1f : 0f;
                if (!Mathf.Approximately(card.focus, target))
                {
                    card.focus = Mathf.MoveTowards(card.focus, target, dt * 7f);
                }
                if (!Mathf.Approximately(card.hot, _hotIndex == i ? 1f : 0f))
                {
                    card.hot = Mathf.MoveTowards(card.hot, _hotIndex == i ? 1f : 0f, dt * 10f);
                }
                ApplyCardState(card);
            }
        }

        int _hotIndex = -1;

        void ApplyCardState(CardUI card)
        {
            float k = card.focus;
            float hot = card.hot;
            float lift = Mathf.Lerp(0f, 26f, k) + hot * 30f;
            float scale = Mathf.Lerp(1f, 1.06f, k) + hot * 0.05f;
            card.rect.anchoredPosition = new Vector2(card.baseX, lift);
            card.rect.localScale = new Vector3(scale, scale, 1f);

            // Las cards nuevas son ilustraciones A COLOR: el reposo no puede bajarlas
            // tanto como cuando eran grises o el color se apaga y deja de leerse.
            float v = Mathf.Lerp(0.62f, 1f, k);
            v = Mathf.Lerp(v, 1f, hot);
            card.art.color = new Color(v, v, v, 1f);

            // La luz es de la tarjeta activa: el resto va apagada. El barrido de la ruleta
            // la lleva al maximo un instante.
            if (card.aura != null) card.aura.Set(card.accent, k + hot * 0.5f);

            if (card.locked != null)
            {
                var lc = card.locked.color;
                lc.a = Mathf.Lerp(0.52f, 0.26f, k);
                card.locked.color = lc;
            }
        }

        void Select(int index)
        {
            if (_revealMode || _phase != Phase.Browse || _all.Count == 0) return;
            index = Mathf.Clamp(index, 0, _all.Count - 1);
            _selectedIndex = index;
            _selected = _all[index];

            _infoName.text = _selected.displayName;
            _infoGenre.text = _selected.genre.ToUpperInvariant();
            _infoTagline.text = _selected.tagline;
            var special = _selected.GetSpecial(0);
            _infoSpecial.text = special != null ? special.displayName : KomLoc.T("Sin poder especial", "No special power");
            _infoSpecialSub.text = special != null && !string.IsNullOrEmpty(special.subtitle)
                ? special.subtitle
                : "";
            UpdateStats();

            if (_confirmLabel != null)
            {
                _confirmLabel.text = _selected.playable
                    ? KomLoc.T("CONFIRMAR LUCHADOR", "CONFIRM FIGHTER")
                    : KomLoc.T("JEFA FINAL · BLOQUEADA", "FINAL BOSS · LOCKED");
                _confirmLabel.color = _selected.playable
                    ? KomTheme.TextOnLight
                    : new Color(KomTheme.TextLo.r, KomTheme.TextLo.g, KomTheme.TextLo.b, 0.75f);
            }
            if (_confirmButton != null)
            {
                _confirmButton.interactable = _selected.playable;
                _confirmButton.image.color = _selected.playable
                    ? KomTheme.Primary
                    : new Color(0.075f, 0.08f, 0.085f, 1f);
            }

            SpawnPlayerPreview(_selected, PlayerSoloPos, SoloPose);
        }

        /// <summary>Fija la ficha del jugador en modo revelacion (no hay seleccion).</summary>
        void SelectFixed(CharacterDef def)
        {
            if (def == null) return;
            _selectedIndex = IndexOf(def);
            _infoName.text = def.displayName;
            _infoGenre.text = def.genre.ToUpperInvariant();
            _infoTagline.text = def.tagline;
            var special = def.GetSpecial(0);
            _infoSpecial.text = special != null ? special.displayName : KomLoc.T("Sin poder especial", "No special power");
            _infoSpecialSub.text = special != null && !string.IsNullOrEmpty(special.subtitle) ? special.subtitle : "";
            UpdateStats();
            SpawnPlayerPreview(def, PlayerSoloPos, SoloPose);
        }

        void UpdateStats()
        {
            // Rangos REALES del catalogo: vida 95..140, dash 3.0..3.4, dano 0.95..1.25.
            // walkSpeed vale 0.55 en todos los luchadores (es un campo heredado), asi que la
            // barra de velocidad usa dashSpeed, que si diferencia.
            float health = Mathf.Clamp01((_selected.maxHealth - 90f) / 55f);
            float speed = Mathf.Clamp01((_selected.dashSpeed - 2.92f) / 0.56f);
            float power = Mathf.Clamp01((_selected.damageScale - 0.90f) / 0.40f);
            _statFills[0].fillAmount = health;
            _statFills[1].fillAmount = speed;
            _statFills[2].fillAmount = power;
            _statValues[0].text = _selected.maxHealth.ToString();
            _statValues[1].text = _selected.dashSpeed.ToString("0.00");
            _statValues[2].text = _selected.damageScale.ToString("0.00");
        }

        // ------------------------------------------------------------------ previews 3D

        void SpawnPlayerPreview(CharacterDef def, Vector3 position, Quaternion rotation)
        {
            if (_playerPreview != null) _playerPreview.SetActive(false);
            _playerPreview = SpawnPreview(def, position, rotation);
        }

        GameObject SpawnPreview(CharacterDef def, Vector3 position, Quaternion rotation)
        {
            if (def == null) return null;

            if (_previewPool.TryGetValue(def.id, out var cached) && cached != null)
            {
                cached.transform.SetPositionAndRotation(position, rotation);
                cached.transform.localScale = Vector3.one;
                cached.SetActive(true);
                return cached;
            }

            // Instancia YA montada por la precarga (bind del Animator pagado en la pantalla
            // de carga). Sin esto, el primer clic en cada luchador costaba ~780 ms de bind.
            var warm = KomPreloader.TakePreview(def.id);
            if (warm != null)
            {
                warm.name = "Preview_" + def.id;
                warm.transform.SetPositionAndRotation(position, rotation);
                warm.transform.localScale = Vector3.one;
                _previewPool[def.id] = warm;
                return warm;
            }

            // Respaldo: entrar directo a la escena sin pasar por la precarga.
            var prefab = Resources.Load<GameObject>(def.prefabResource);
            if (prefab == null)
            {
                Debug.LogError($"[KOM] Falta el prefab {def.prefabResource}");
                return null;
            }
            var go = Instantiate(prefab, position, rotation);
            go.name = "Preview_" + def.id;
            var fighter = go.GetComponent<Fighter>();
            if (fighter != null) fighter.enabled = false;
            var animator = go.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                // La instancia nace con la pose guardada del prefab, que no es bind.
                // Sin Rebind, los huesos que el clip no anima (cara congelada) quedan
                // deformados en el preview.
                animator.Rebind();
                animator.SetLayerWeight(0, 1f);
                if (animator.layerCount > 1) animator.SetLayerWeight(1, 0f);
                animator.Play("Idle", 0, 0f);
            }
            var shadow = KomFx.CreateGroundShadow(go.transform, 1.25f, 0.6f);
            shadow.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            KomVisual.StripColliders(go);
            _previewPool[def.id] = go;
            return go;
        }

        // Las instancias del preview son hijas del host de precarga (DontDestroyOnLoad):
        // hay que esconderlas al salir o seguirian renderizandose en la escena siguiente.
        void OnDestroy()
        {
            KomPreloader.ReleasePreviews();
        }

        void OnCardClicked(int index)
        {
            if (_revealMode || _phase != Phase.Browse) return;
            Select(index);
        }

        // ------------------------------------------------------------------ flujo

        void ConfirmSelection()
        {
            if (_revealMode || _phase != Phase.Browse || _selected == null) return;
            if (!_selected.playable)
            {
                ShowToast(KomLoc.T("Flavia Cheinbaum es la jefa final. No puede elegirse como P1.",
                                   "Flavia Cheinbaum is the final boss. She cannot be picked as P1."));
                return;
            }
            _phase = Phase.Locking;
            _confirmButton.gameObject.SetActive(false);
            _session.BeginRun(_selected.id);
            StartCoroutine(RouletteRoutine());
        }

        // ------------------------------------------------------------------ versus local

        // El selector de dos jugadores no es el de historia con dos pasos: es UNA tira
        // compartida con dos cursores. Los dos pueden moverse y confirmar al mismo tiempo,
        // como en una recreativa, y el personaje de cada uno se ve en su columna y en el
        // diorama. No hay ruleta, ni turnos, ni revelacion: aqui nadie esconde nada.
        const float VersusStripH = 196f;
        const float VersusCardGap = 22f;

        void BuildVersusSelectUI()
        {
            _phase = Phase.Browse;
            _versusFinishing = false;
            UIFactory.DestroyCanvas(_canvas);

            _p1Menu = new FighterControls(0, true);
            _p2Menu = new FighterControls(1, true);
            _versusCards.Clear();
            _versusP1Pool.Clear();
            _versusP2Pool.Clear();

            if (_playable.Count == 0) return;
            int first = PlayableIndexOf(_session.VersusPlayer1Id);
            int second = PlayableIndexOf(_session.VersusPlayer2Id);
            if (first < 0) first = 0;
            if (second < 0) second = (first + 1) % _playable.Count;
            _p1Pick = new VersusPick { index = first, hover = _playable[first] };
            _p2Pick = new VersusPick { index = second, hover = _playable[second] };

            // El plano de presentacion es el mismo "dos" del VS: los dos luchadores en
            // el centro, las columnas de ficha en los bordes.
            _camera.transform.position = DuoCamPos;
            _camera.transform.rotation = Quaternion.LookRotation(DuoCamLook - DuoCamPos);
            _camera.fieldOfView = 33f;

            _canvas = UIFactory.CreateCanvas("VersusSelectCanvas");
            var root = (RectTransform)_canvas.transform;

            var vignette = UIFactory.AddVignette(root, 0.55f);
            vignette.transform.SetAsFirstSibling();

            var leftShade = UIFactory.AddImage(root, "LeftShade",
                UIFactory.HorizontalScrim(0.94f, 1.55f), Color.white);
            leftShade.rectTransform.anchorMin = new Vector2(0f, 0f);
            leftShade.rectTransform.anchorMax = new Vector2(0f, 1f);
            leftShade.rectTransform.pivot = new Vector2(0f, 0.5f);
            leftShade.rectTransform.sizeDelta = new Vector2(430f, 0f);
            leftShade.rectTransform.anchoredPosition = Vector2.zero;

            var rightShade = UIFactory.AddImage(root, "RightShade",
                UIFactory.HorizontalScrim(0.94f, 1.55f), Color.white);
            rightShade.rectTransform.anchorMin = new Vector2(1f, 0f);
            rightShade.rectTransform.anchorMax = new Vector2(1f, 1f);
            rightShade.rectTransform.pivot = new Vector2(1f, 0.5f);
            rightShade.rectTransform.sizeDelta = new Vector2(430f, 0f);
            rightShade.rectTransform.anchoredPosition = Vector2.zero;
            rightShade.rectTransform.localScale = new Vector3(-1f, 1f, 1f);

            var top = UIFactory.AddImage(root, "TopShade",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0.82f), new Color(0f, 0f, 0f, 0f)), Color.white);
            top.rectTransform.anchorMin = new Vector2(0f, 1f);
            top.rectTransform.anchorMax = new Vector2(1f, 1f);
            top.rectTransform.pivot = new Vector2(0.5f, 1f);
            top.rectTransform.sizeDelta = new Vector2(0f, 210f);
            top.rectTransform.anchoredPosition = Vector2.zero;

            var bottom = UIFactory.AddImage(root, "BottomShade",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.98f)), Color.white);
            bottom.rectTransform.anchorMin = new Vector2(0f, 0f);
            bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottom.rectTransform.pivot = new Vector2(0.5f, 0f);
            bottom.rectTransform.sizeDelta = new Vector2(0f, 344f);
            bottom.rectTransform.anchoredPosition = Vector2.zero;

            // Marca de agua central: el VS se lee sin competir con las dos figuras.
            var vs = UIFactory.AddDisplayText(root, "Vs", "VS", 190, new Color(1f, 1f, 1f, 0.045f),
                TextAnchor.MiddleCenter);
            UIFactory.Place(vs.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 82f), new Vector2(420f, 220f));

            var centerRule = UIFactory.AddVLine(root, "CenterRule", KomTheme.LineFaint, 1f);
            UIFactory.Place(centerRule.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -468f), new Vector2(1f, 528f));

            BuildVersusHeader(root);
            BuildVersusPlayerPanel(root, 0);
            BuildVersusPlayerPanel(root, 1);
            BuildVersusStrip(root);
            BuildVersusFooter(root);
            BuildVersusReady(root);

            RefreshVersusPick(0);
            RefreshVersusPick(1);

            // Los dos cursores ya estan sobre su ficha al abrir: el jugador tiene que ver
            // de un vistazo donde esta cada uno, tambien en la preview del editor (donde
            // no corre Update).
            for (int i = 0; i < _versusCards.Count; i++)
            {
                var card = _versusCards[i];
                card.hot1 = card.def == _p1Pick.hover ? 1f : 0f;
                card.hot2 = card.def == _p2Pick.hover ? 1f : 0f;
            }
            AnimateVersusCards(0f);
        }

        void BuildVersusHeader(Transform root)
        {
            var kicker = UIFactory.AddText(root, "Kicker", KomLoc.T("MODO LOCAL · DOS JUGADORES", "LOCAL MODE · TWO PLAYERS"),
                13, KomTheme.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(KomTheme.Margin, -42f), new Vector2(760f, 18f));

            var title = UIFactory.AddDisplayText(root, "Title", KomLoc.T("ELIGE TU LUCHADOR", "CHOOSE YOUR FIGHTER"),
                44, KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(KomTheme.Margin - 4f, -62f), new Vector2(980f, 58f));

            var hint = UIFactory.AddText(root, "Hint",
                KomLoc.T("Los dos eligen a la vez · J1 y J2 tienen su propio cursor",
                         "Both pick at once · P1 and P2 have their own cursor"),
                14, KomTheme.TextMid, TextAnchor.UpperRight);
            UIFactory.Place(hint.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-KomTheme.Margin, -48f), new Vector2(820f, 20f));

            var hint2 = UIFactory.AddText(root, "Hint2",
                KomLoc.T("Pueden elegir al mismo luchador", "They may pick the same fighter"),
                11, KomTheme.TextLo, TextAnchor.UpperRight, FontStyle.Bold);
            UIFactory.Place(hint2.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-KomTheme.Margin, -72f), new Vector2(820f, 18f));

            var rule = UIFactory.AddHLine(root, "HeaderRule", KomTheme.Line, 1f);
            UIFactory.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -142f), new Vector2(-2f * KomTheme.Margin, 1f));
        }

        void BuildVersusPlayerPanel(Transform root, int player)
        {
            bool left = player == 0;
            var pick = player == 0 ? _p1Pick : _p2Pick;
            if (pick == null) pick = new VersusPick();

            var panel = UIFactory.Rect(root, left ? "P1Panel" : "P2Panel");
            UIFactory.Place(panel, new Vector2(left ? 0f : 1f, 1f), new Vector2(left ? 0f : 1f, 1f),
                new Vector2(left ? 0f : 1f, 1f),
                new Vector2(left ? KomTheme.Margin : -KomTheme.Margin, -176f), new Vector2(330f, 552f));

            var surface = UIFactory.AddPanel(panel, "Surface", new Color(0.043f, 0.047f, 0.051f, 0.92f), 3);
            UIFactory.Stretch(surface.rectTransform);
            UIFactory.AddFrame(panel, "Border", KomTheme.Line, 1f);

            pick.accent = UIFactory.AddImage(panel, "Accent", UIFactory.WhiteSprite(),
                left ? KomTheme.AccentHot : KomTheme.LineStrong);
            UIFactory.Place(pick.accent.rectTransform, new Vector2(left ? 0f : 1f, 0f), new Vector2(left ? 0f : 1f, 1f),
                new Vector2(left ? 0f : 1f, 0.5f), Vector2.zero, new Vector2(3f, 0f));

            var head = UIFactory.AddText(panel, "Head",
                left ? KomLoc.T("JUGADOR 1", "PLAYER 1") : KomLoc.T("JUGADOR 2", "PLAYER 2"),
                15, KomTheme.TextHi, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -24f), new Vector2(160f, 20f));

            pick.device = UIFactory.AddText(panel, "Device", "", 10, KomTheme.TextLo,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(pick.device.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-24f, -24f), new Vector2(150f, 18f));

            pick.status = UIFactory.AddText(panel, "Status", KomLoc.T("ELIGIENDO", "CHOOSING"), 11,
                KomTheme.TextLo, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(pick.status.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -48f), new Vector2(220f, 16f));

            var cardSlot = UIFactory.Rect(panel, "CardSlot");
            UIFactory.Place(cardSlot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -66f), new Vector2(210f, 300f));

            pick.card = UIFactory.AddImage(cardSlot, "Card", null, Color.white);
            pick.card.preserveAspect = true;
            pick.card.rectTransform.anchorMin = pick.card.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            pick.card.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            pick.card.rectTransform.anchoredPosition = Vector2.zero;
            pick.card.rectTransform.sizeDelta = new Vector2(200f, 296f);
            pick.cardAura = UIFactory.AddCardAura(pick.card, KomTheme.Accent, 1f);

            pick.name = UIFactory.AddDisplayText(panel, "Name", "—", 30, KomTheme.TextHi, TextAnchor.MiddleCenter);
            UIFactory.Place(pick.name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -374f), new Vector2(310f, 38f));
            pick.name.resizeTextForBestFit = true;
            pick.name.resizeTextMinSize = 16;
            pick.name.resizeTextMaxSize = 30;

            pick.genre = UIFactory.AddText(panel, "Genre", "", 11, KomTheme.TextMid,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(pick.genre.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -408f), new Vector2(310f, 16f));

            var divider = UIFactory.AddHLine(panel, "Divider", KomTheme.LineFaint, 1f);
            UIFactory.Place(divider.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -430f), new Vector2(282f, 1f));

            var specialKicker = UIFactory.AddText(panel, "SpecialKicker", KomLoc.T("PODER ESPECIAL", "SPECIAL POWER"),
                9, KomTheme.TextLo, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(specialKicker.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -440f), new Vector2(300f, 14f));

            pick.special = UIFactory.AddText(panel, "Special", "—", 15, KomTheme.TextMid,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(pick.special.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -454f), new Vector2(300f, 34f));
            pick.special.horizontalOverflow = HorizontalWrapMode.Wrap;
            pick.special.verticalOverflow = VerticalWrapMode.Truncate;
        }

        void BuildVersusStrip(Transform root)
        {
            var strip = UIFactory.Rect(root, "VersusStrip");
            strip.anchorMin = new Vector2(0.5f, 0f);
            strip.anchorMax = new Vector2(0.5f, 0f);
            strip.pivot = new Vector2(0.5f, 0f);
            strip.sizeDelta = new Vector2(1360f, VersusStripH + 28f);
            strip.anchoredPosition = new Vector2(0f, 54f);

            float total = 0f;
            var widths = new float[_all.Count];
            for (int i = 0; i < _all.Count; i++)
            {
                var sprite = UIFactory.LoadSprite(_all[i].cardResource);
                widths[i] = sprite != null
                    ? VersusStripH * (sprite.rect.width / sprite.rect.height)
                    : VersusStripH * 0.68f;
                total += widths[i] + (i > 0 ? VersusCardGap : 0f);
            }
            float x = (strip.sizeDelta.x - total) * 0.5f;
            for (int i = 0; i < _all.Count; i++)
            {
                _versusCards.Add(BuildVersusCard(strip, i, x, widths[i]));
                x += widths[i] + VersusCardGap;
            }
        }

        VersusCardUI BuildVersusCard(Transform strip, int index, float x, float width)
        {
            var def = _all[index];
            var sprite = UIFactory.LoadSprite(def.cardResource);
            var holder = UIFactory.Rect(strip, "VCard" + index);
            holder.anchorMin = new Vector2(0f, 0f);
            holder.anchorMax = new Vector2(0f, 0f);
            holder.pivot = new Vector2(0f, 0f);
            holder.sizeDelta = new Vector2(width, VersusStripH);
            holder.anchoredPosition = new Vector2(x, 0f);

            var art = UIFactory.AddImage(holder, "Art", sprite, new Color(0.55f, 0.55f, 0.55f, 1f));
            art.preserveAspect = true;
            UIFactory.Stretch(art.rectTransform);
            art.raycastTarget = false;
            var aura = UIFactory.AddCardAura(art, KomData.Accent(def), 0f);

            if (!def.playable)
            {
                var locked = UIFactory.AddImage(holder, "Locked", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0.52f));
                UIFactory.Stretch(locked.rectTransform);

                var lockedTag = UIFactory.AddPanel(holder, "LockedTag", new Color(0f, 0f, 0f, 0.86f), 2);
                UIFactory.Place(lockedTag.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(92f, 18f));
                var lockedLabel = UIFactory.AddText(lockedTag.transform, "Label",
                    KomLoc.T("JEFA FINAL", "FINAL BOSS"), 9, KomTheme.TextLo,
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(lockedLabel.rectTransform, 2f, 1f, 2f, 1f);
            }

            var p1Frame = UIFactory.AddCornerBrackets(holder, "P1Frame", KomTheme.AccentHot, 16f, 2f);
            p1Frame.gameObject.SetActive(false);
            var p2Frame = UIFactory.AddFrame(holder, "P2Frame", KomTheme.TextHi, 1.5f);
            p2Frame.gameObject.SetActive(false);

            var p1Mark = BuildVersusMark(holder, "P1Mark", "J1", true);
            var p2Mark = BuildVersusMark(holder, "P2Mark", "J2", false);

            return new VersusCardUI
            {
                root = holder,
                art = art,
                aura = aura,
                p1Frame = p1Frame,
                p2Frame = p2Frame,
                p1Mark = p1Mark,
                p2Mark = p2Mark,
                p1Label = p1Mark.GetComponentInChildren<Text>(),
                p2Label = p2Mark.GetComponentInChildren<Text>(),
                def = def,
                basePos = holder.anchoredPosition
            };
        }

        GameObject BuildVersusMark(Transform parent, string name, string text, bool top)
        {
            var plate = UIFactory.AddPanel(parent, name, top ? KomTheme.AccentHot : KomTheme.SurfaceHi, 2);
            UIFactory.Place(plate.rectTransform, new Vector2(1f, top ? 1f : 0f), new Vector2(1f, top ? 1f : 0f),
                new Vector2(1f, top ? 1f : 0f),
                new Vector2(-2f, top ? -2f : 2f), new Vector2(42f, 18f));
            var label = UIFactory.AddText(plate.transform, "Label", text, 10,
                top ? KomTheme.TextOnLight : KomTheme.TextHi, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(label.rectTransform, 2f, 1f, 2f, 1f);
            plate.gameObject.SetActive(false);
            return plate.gameObject;
        }

        void BuildVersusFooter(Transform root)
        {
            var bar = UIFactory.Rect(root, "Footer");
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(0f, 52f);
            bar.anchoredPosition = Vector2.zero;

            var rule = UIFactory.AddHLine(bar, "Rule", KomTheme.LineFaint, 1f);
            rule.rectTransform.anchorMin = new Vector2(0f, 1f);
            rule.rectTransform.anchorMax = new Vector2(1f, 1f);
            rule.rectTransform.pivot = new Vector2(0.5f, 1f);
            rule.rectTransform.anchoredPosition = Vector2.zero;

            var left = UIFactory.AddText(bar, "P1Hint",
                KomLoc.T("<b>J1</b>   A / D  elegir     <b>C</b>  confirmar     <b>B</b>  cambiar",
                         "<b>P1</b>   A / D  choose     <b>C</b>  confirm     <b>B</b>  change"),
                12, KomTheme.TextMid, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(left.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(KomTheme.Margin, 0f), new Vector2(640f, 20f));

            var right = UIFactory.AddText(bar, "P2Hint",
                KomLoc.T("<b>J2</b>   ← / →  elegir     <b>U</b>  confirmar     <b>O</b>  cambiar",
                         "<b>P2</b>   ← / →  choose     <b>U</b>  confirm     <b>O</b>  change"),
                12, KomTheme.TextMid, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(right.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-KomTheme.Margin, 0f), new Vector2(640f, 20f));

            var exit = UIFactory.AddText(bar, "Exit", KomLoc.T("ESC  ·  SALIR", "ESC  ·  EXIT"), 10,
                KomTheme.TextLo, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(exit.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(300f, 16f));
        }

        void BuildVersusReady(Transform root)
        {
            var overlay = UIFactory.Rect(root, "ReadyOverlay");
            UIFactory.Stretch(overlay);
            var dim = UIFactory.AddImage(overlay, "Dim", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0.84f));
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = false;

            _versusReadyTitle = UIFactory.AddDisplayText(overlay, "Title", "¡LISTOS!", 92,
                KomTheme.TextHi, TextAnchor.MiddleCenter);
            UIFactory.Place(_versusReadyTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 46f), new Vector2(1200f, 120f));

            var rule = UIFactory.AddHLine(overlay, "Rule", KomTheme.Accent, 2f);
            UIFactory.Place(rule.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -26f), new Vector2(160f, 2f));

            _versusReadyNames = UIFactory.AddDisplayText(overlay, "Names", "", 30,
                KomTheme.TextMid, TextAnchor.MiddleCenter);
            UIFactory.Place(_versusReadyNames.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -62f), new Vector2(1400f, 50f));

            _versusReadyGroup = overlay.gameObject.AddComponent<CanvasGroup>();
            _versusReadyGroup.alpha = 0f;
            _versusReadyGroup.blocksRaycasts = false;
            overlay.gameObject.SetActive(false);
        }

        void UpdateVersusSelect()
        {
            if (_p1Pick == null || _p2Pick == null || _versusFinishing) return;
            if (_phase != Phase.Browse) return;

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                _phase = Phase.Leaving;
                _session.ExitVersus();
                ScreenFader.Load(KomScenes.MainMenu);
                return;
            }

            UpdateVersusPlayer(0);
            UpdateVersusPlayer(1);
        }

        void UpdateVersusPlayer(int player)
        {
            var pick = player == 0 ? _p1Pick : _p2Pick;
            var controls = player == 0 ? _p1Menu : _p2Menu;
            if (pick == null || controls == null) return;

            if (pick.locked)
            {
                if (controls.MenuCancelPressed())
                {
                    pick.locked = false;
                    _session.SetVersusFighter(player + 1, null);
                    RefreshVersusPick(player);
                }
                return;
            }

            if (_playable.Count == 0) return;
            bool moved = false;
            if (controls.MoveLeftPressed())
            {
                pick.index = (pick.index - 1 + _playable.Count) % _playable.Count;
                moved = true;
            }
            if (controls.MoveRightPressed())
            {
                pick.index = (pick.index + 1) % _playable.Count;
                moved = true;
            }
            if (moved)
            {
                pick.hover = _playable[pick.index];
                RefreshVersusPick(player);
            }

            if (controls.MenuConfirmPressed()) ConfirmVersusPick(player);
        }

        void ConfirmVersusPick(int player)
        {
            var pick = player == 0 ? _p1Pick : _p2Pick;
            if (pick == null || pick.locked || pick.hover == null) return;
            pick.locked = true;
            _session.SetVersusFighter(player + 1, pick.hover.id);
            RefreshVersusPick(player);
            if (_p1Pick.locked && _p2Pick.locked) StartCoroutine(FinishVersusSelection());
        }

        int PlayableIndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            return _playable.FindIndex(d => d.id == id);
        }

        void RefreshVersusPick(int player)
        {
            var pick = player == 0 ? _p1Pick : _p2Pick;
            if (pick == null || pick.hover == null) return;
            var def = pick.hover;
            var sprite = UIFactory.LoadSprite(def.cardResource);
            if (pick.card != null)
            {
                pick.card.sprite = sprite;
                pick.card.color = Color.white;
                var fitted = UIFactory.FitSize(sprite, new Vector2(202f, 296f));
                pick.card.rectTransform.sizeDelta = fitted;
            }
            if (pick.cardAura != null) pick.cardAura.Set(KomData.Accent(def), 1f);
            if (pick.name != null) pick.name.text = def.displayName;
            if (pick.genre != null)
            {
                pick.genre.text = def.genre.ToUpperInvariant();
                pick.genre.color = KomTheme.AccentFor(def);
            }
            if (pick.special != null)
            {
                var special = def.GetSpecial(0);
                pick.special.text = special != null ? special.displayName : KomLoc.T("Sin poder", "No power");
            }
            if (pick.accent != null) pick.accent.color = KomTheme.AccentFor(def);
            if (pick.device != null)
                pick.device.text = (player == 0 ? _p1Menu : _p2Menu).DeviceLabel;
            if (pick.status != null)
            {
                pick.status.text = pick.locked
                    ? KomLoc.T("LISTO", "READY")
                    : KomLoc.T("ELIGIENDO", "CHOOSING");
                pick.status.color = pick.locked ? KomTheme.AccentHot : KomTheme.TextLo;
            }
            SpawnVersusPreview(player);
        }

        void SpawnVersusPreview(int player)
        {
            var pick = player == 0 ? _p1Pick : _p2Pick;
            if (pick == null || pick.hover == null) return;
            if (pick.preview != null && pick.previewId == pick.hover.id && pick.preview.activeSelf) return;
            if (pick.preview != null) pick.preview.SetActive(false);

            var pool = player == 0 ? _versusP1Pool : _versusP2Pool;
            if (!pool.TryGetValue(pick.hover.id, out var go) || go == null)
            {
                go = CreateVersusPreview(pick.hover, player);
                if (go == null) return;
                pool[pick.hover.id] = go;
            }

            go.transform.SetPositionAndRotation(
                player == 0 ? VersusPlayerPos : VersusEnemyPos,
                player == 0 ? PlayerDuoPose : EnemyDuoPose);
            go.transform.localScale = Vector3.one;
            go.SetActive(true);
            var animator = go.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Play("Idle", 0, 0f);
            }
            pick.preview = go;
            pick.previewId = pick.hover.id;
        }

        GameObject CreateVersusPreview(CharacterDef def, int player)
        {
            // J1 reutiliza la instancia ya montada por la precarga. J2 necesita una copia
            // propia: si los dos eligen al mismo luchador hay que poder verlo dos veces.
            if (player == 0)
            {
                var warm = KomPreloader.TakePreview(def.id);
                if (warm != null)
                {
                    warm.name = "PreviewVS1_" + def.id;
                    return warm;
                }
            }
            else
            {
                // El jugador 2 usa la SEGUNDA instancia caliente. Instanciar el rig en
                // el primer hover costaba ~0,8 s y se notaba como un tiron al pasar las
                // cards; ahora las dos copias se pagan en la pantalla de carga.
                var warmCopy = KomPreloader.TakePreviewCopy(def.id);
                if (warmCopy != null)
                {
                    warmCopy.name = "PreviewVS2_" + def.id;
                    return warmCopy;
                }
            }

            var prefab = Resources.Load<GameObject>(def.prefabResource);
            if (prefab == null)
            {
                Debug.LogError($"[KOM] Falta el prefab {def.prefabResource}");
                return null;
            }
            var go = Instantiate(prefab);
            go.name = "PreviewVS" + (player + 1) + "_" + def.id;
            PrepareVersusPreview(go);
            return go;
        }

        static void PrepareVersusPreview(GameObject go)
        {
            var fighter = go.GetComponent<Fighter>();
            if (fighter != null) fighter.enabled = false;
            var animator = go.GetComponentInChildren<Animator>();
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

        void AnimateVersusCards(float dt)
        {
            if (_p1Pick == null || _p2Pick == null) return;
            for (int i = 0; i < _versusCards.Count; i++)
            {
                var card = _versusCards[i];
                bool on1 = card.def == _p1Pick.hover;
                bool on2 = card.def == _p2Pick.hover;
                card.hot1 = Mathf.MoveTowards(card.hot1, on1 ? 1f : 0f, dt * 9f);
                card.hot2 = Mathf.MoveTowards(card.hot2, on2 ? 1f : 0f, dt * 9f);

                float pop = Mathf.Clamp01(card.hot1 + card.hot2);
                card.root.anchoredPosition = new Vector2(card.basePos.x, 18f * pop);
                card.root.localScale = Vector3.one * (1f + 0.045f * pop);

                float value = Mathf.Lerp(0.55f, 1f, pop);
                if (!card.def.playable) value *= 0.72f;
                card.art.color = new Color(value, value, value, 1f);
                if (card.aura != null)
                    card.aura.Set(KomData.Accent(card.def), pop * 0.85f + (card.def.playable ? 0f : 0.1f));

                if (card.p1Frame != null) card.p1Frame.gameObject.SetActive(on1 && card.hot1 > 0.01f);
                if (card.p2Frame != null) card.p2Frame.gameObject.SetActive(on2 && card.hot2 > 0.01f);
                if (card.p1Mark != null)
                {
                    card.p1Mark.SetActive(on1 && card.hot1 > 0.01f);
                    if (on1 && card.p1Label != null)
                        card.p1Label.text = _p1Pick.locked ? "J1 OK" : "J1";
                }
                if (card.p2Mark != null)
                {
                    card.p2Mark.SetActive(on2 && card.hot2 > 0.01f);
                    if (on2 && card.p2Label != null)
                        card.p2Label.text = _p2Pick.locked ? "J2 OK" : "J2";
                }
            }

            if (_p1Pick.device != null) _p1Pick.device.text = _p1Menu.DeviceLabel;
            if (_p2Pick.device != null) _p2Pick.device.text = _p2Menu.DeviceLabel;
        }

        IEnumerator FinishVersusSelection()
        {
            _versusFinishing = true;
            _phase = Phase.Leaving;
            _session.SetVersusFighter(1, _p1Pick.hover.id);
            _session.SetVersusFighter(2, _p2Pick.hover.id);

            if (_versusReadyGroup != null)
            {
                _versusReadyGroup.gameObject.SetActive(true);
                _versusReadyGroup.transform.SetAsLastSibling();
                _versusReadyGroup.alpha = 0f;
            }
            if (_versusReadyTitle != null) _versusReadyTitle.text = KomLoc.T("¡LISTOS!", "READY!");
            if (_versusReadyNames != null)
                _versusReadyNames.text = _p1Pick.hover.displayName.ToUpperInvariant() +
                                         "   VS   " + _p2Pick.hover.displayName.ToUpperInvariant();

            float t = 0f;
            while (t < 0.22f)
            {
                t += Time.unscaledDeltaTime;
                if (_versusReadyGroup != null)
                    _versusReadyGroup.alpha = Mathf.Clamp01(t / 0.22f);
                yield return null;
            }
            if (_versusReadyGroup != null) _versusReadyGroup.alpha = 1f;
            yield return new WaitForSecondsRealtime(0.82f);
            ScreenFader.Load(KomScenes.Map);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Ganchos de verificacion (el puente de editor no inyecta teclas en el Input
        /// System del juego). No existen en builds.
        /// </summary>
        public void DebugVersusMove(int player, int delta)
        {
            if (!_versus || _playable.Count == 0) return;
            var pick = player == 0 ? _p1Pick : _p2Pick;
            if (pick == null || pick.locked) return;
            pick.index = (pick.index + delta + _playable.Count) % _playable.Count;
            pick.hover = _playable[pick.index];
            RefreshVersusPick(player);
            // En edicion no corre Update: los marcos y las etiquetas J1/J2 se
            // actualizan aqui para que la preview muestre el cursor real.
            for (int i = 0; i < _versusCards.Count; i++)
            {
                var card = _versusCards[i];
                card.hot1 = card.def == _p1Pick.hover ? 1f : 0f;
                card.hot2 = card.def == _p2Pick.hover ? 1f : 0f;
            }
            AnimateVersusCards(0f);
        }

        public void DebugVersusConfirm(int player)
        {
            if (!_versus) return;
            ConfirmVersusPick(player);
        }

        /// <summary>
        /// Recorre todas las cards del jugador 2 y devuelve el tiempo en ms. Sirve para
        /// comprobar que la precarga de las copias elimino los tirones de navegacion.
        /// </summary>
        public float DebugCycleP2()
        {
            if (!_versus || _playable.Count == 0) return -1f;
            float t0 = Time.realtimeSinceStartup;
            for (int i = 0; i < _playable.Count; i++) DebugVersusMove(1, 1);
            return (Time.realtimeSinceStartup - t0) * 1000f;
        }

        public string DebugVersusState =>
            _p1Pick == null || _p2Pick == null
                ? "versus no inicializado"
                : $"P1={_p1Pick.hover.id}/{(_p1Pick.locked ? "OK" : "elige")} " +
                  $"P2={_p2Pick.hover.id}/{(_p2Pick.locked ? "OK" : "elige")} " +
                  $"fin={_versusFinishing} p1dev={_p1Menu.DeviceLabel} p2dev={_p2Menu.DeviceLabel}";
#endif

        /// <summary>Entrada del modo revelacion: pausa breve y ruleta automatica.</summary>
        IEnumerator RevealSequence()
        {
            _phase = Phase.Browse;
            SelectFixed(_selected);
            _confirmLabel.text = KomLoc.T("PRESENTANDO RIVAL", "PRESENTING RIVAL");
            yield return new WaitForSecondsRealtime(0.85f);
            _phase = Phase.Locking;
            yield return StartCoroutine(RouletteRoutine());
        }

        IEnumerator RouletteRoutine()
        {
            _phase = Phase.Roulette;
            var targetDef = _revealMode ? _rival : _session.CurrentOpponentDef;
            if (targetDef == null)
            {
                Debug.LogError("[KOM] No se pudo resolver el rival del encuentro.");
                _phase = Phase.Browse;
                yield break;
            }

            int targetCard = IndexOf(targetDef);
            int targetSpin = _playable.FindIndex(d => d.id == targetDef.id);

            _scanLabel.gameObject.SetActive(true);
            // "Sortear al rival" se lee como esquivarlo; el sorteo es del rival, no contra él.
            _scanLabel.text = _revealMode
                ? KomLoc.T("SORTEANDO EL PRÓXIMO RIVAL", "DRAWING THE NEXT RIVAL")
                : KomLoc.T("SORTEANDO EL PRIMER RIVAL", "DRAWING THE FIRST RIVAL");
            _scan.gameObject.SetActive(true);

            if (targetSpin >= 0)
            {
                int count = _playable.Count;
                int start = Random.Range(0, count);
                int offset = (targetSpin - start + count) % count;
                int steps = count * 2 + offset + count;

                for (int k = 0; k <= steps; k++)
                {
                    int idx = (start + k) % count;
                    FocusScan(IndexOf(_playable[idx]));
                    float t = k / (float)steps;
                    yield return new WaitForSecondsRealtime(Mathf.Lerp(0.045f, 0.26f, Mathf.Pow(t, 2.7f)));
                }
            }
            else
            {
                // La jefa final no esta en la rotacion: se revela sin sorteo.
                for (int k = 0; k < 12; k++)
                {
                    if (_playable.Count > 0) FocusScan(IndexOf(_playable[k % _playable.Count]));
                    yield return new WaitForSecondsRealtime(0.06f + k * 0.02f);
                }
            }

            FocusScan(targetCard);
            _hotIndex = targetCard;
            yield return new WaitForSecondsRealtime(0.30f);

            _scanLabel.text = targetDef.displayName.ToUpperInvariant();
            StartFlash(0.32f, 0.3f);
            yield return StartCoroutine(FlyCardToRival(_cards[targetCard], targetDef));

            _scan.gameObject.SetActive(false);
            _scanLabel.gameObject.SetActive(false);
            _hotIndex = -1;

            yield return StartCoroutine(SpawnEnemyRoutine(targetDef));
            yield return new WaitForSecondsRealtime(0.35f);
            yield return StartCoroutine(EnterVersus(targetDef));
        }

        void FocusScan(int cardIndex)
        {
            if (cardIndex < 0 || cardIndex >= _cards.Count) return;
            _hotIndex = cardIndex;
            var card = _cards[cardIndex];
            _scan.sizeDelta = new Vector2(card.width + 64f, StripCardH + 46f);
            _scan.anchoredPosition = new Vector2(card.baseX + card.width * 0.5f, StripCardH * 0.5f);
        }

        /// <summary>
        /// La tarjeta elegida vuela de la tira al hueco del rival. Es el gesto que conecta
        /// "esto es el elenco" con "este es tu oponente": sin el, la ruleta termina en un
        /// cambio de estado sin continuidad espacial.
        /// </summary>
        IEnumerator FlyCardToRival(CardUI card, CharacterDef def)
        {
            var slot = (RectTransform)_rivalKnown.transform.parent;
            var sprite = UIFactory.LoadSprite(def.cardResource);
            var flight = UIFactory.AddImage(_browseUi, "Flight", sprite, Color.white);
            flight.preserveAspect = true;
            flight.raycastTarget = false;

            Vector3 fromPos = card.rect.TransformPoint(new Vector3(card.width * 0.5f, card.height * 0.5f, 0f));
            Vector3 toPos = slot.position;
            Vector3 fromScale = Vector3.one;
            Vector3 toScale = Vector3.one * Mathf.Clamp(slot.rect.height / card.height, 0.4f, 1.4f);

            var rt = flight.rectTransform;
            rt.sizeDelta = new Vector2(card.width, card.height);
            rt.position = fromPos;
            rt.localScale = fromScale;

            float t = 0f;
            const float dur = 0.46f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = KomUiMotion.EaseInOutCubic(t / dur);
                rt.position = Vector3.LerpUnclamped(fromPos, toPos, k);
                rt.localScale = Vector3.LerpUnclamped(fromScale, toScale, k);
                var c = flight.color;
                c.a = Mathf.Lerp(1f, 0.92f, k);
                flight.color = c;
                yield return null;
            }

            RevealRival(def);
            Destroy(flight.gameObject);
            yield return new WaitForSecondsRealtime(0.12f);
        }

        void RevealRival(CharacterDef def, bool animate = true)
        {
            _rival = def;
            _rivalUnknown.SetActive(false);
            _rivalKnown.SetActive(true);
            var rivalSprite = UIFactory.LoadSprite(def.cardResource);
            _rivalCard.sprite = rivalSprite;
            _rivalCard.color = Color.white;

            // La tarjeta del rival se ajusta a la proporción REAL del dibujo para que el
            // aura lo abrace: estirada al hueco (286x418) el contorno quedaría más alto
            // que la ilustración y se verían dos bandas de color arriba y abajo.
            var cardRect = _rivalCard.rectTransform;
            var fitted = UIFactory.FitSize(rivalSprite, new Vector2(282f, 414f));
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.sizeDelta = fitted;

            if (_rivalAura == null)
                _rivalAura = UIFactory.AddCardAura(_rivalCard, KomData.Accent(def), 1f);
            _rivalAura.Set(KomData.Accent(def), 1f);
            _rivalName.text = def.displayName;
            _rivalGenre.text = def.genre.ToUpperInvariant();

            var head = _browseUi.Find("RivalKicker");
            if (head != null)
            {
                var label = head.GetComponent<Text>();
                if (label != null) label.text = _revealMode
                    ? KomLoc.T("RIVAL DE ESTE ENCUENTRO", "RIVAL FOR THIS ENCOUNTER")
                    : KomLoc.T("RIVAL REVELADO", "RIVAL REVEALED");
            }

            if (animate && Application.isPlaying) StartCoroutine(RevealPulse());
        }

        IEnumerator RevealPulse()
        {
            var c = _rivalFlash.color;
            c.a = 0.55f;
            _rivalFlash.color = c;
            float t = 0f;
            while (t < 0.5f)
            {
                t += Time.unscaledDeltaTime;
                c.a = Mathf.Lerp(0.55f, 0f, Mathf.Clamp01(t / 0.5f));
                _rivalFlash.color = c;
                yield return null;
            }
            c.a = 0f;
            _rivalFlash.color = c;
        }

        IEnumerator SpawnEnemyRoutine(CharacterDef def)
        {
            if (_enemyPreview != null) _enemyPreview.SetActive(false);
            _enemyPreview = SpawnPreview(def, EnemyDuoPos, EnemyDuoPose);
            if (_enemyPreview != null)
            {
                var animator = _enemyPreview.GetComponentInChildren<Animator>();
                if (animator != null) animator.Play("Idle", 0, 0f);
                float t = 0f;
                while (t < 0.3f)
                {
                    t += Time.unscaledDeltaTime;
                    float k = KomUiMotion.EaseOutCubic(t / 0.3f);
                    _enemyPreview.transform.localScale = Vector3.one * Mathf.Lerp(0.88f, 1f, k);
                    yield return null;
                }
                _enemyPreview.transform.localScale = Vector3.one;
            }
        }

        IEnumerator EnterVersus(CharacterDef enemy)
        {
            // El jugador se aparta y la camara se abre: el VS es un plano de dos.
            if (_playerPreview != null)
                StartCoroutine(MoveTransform(_playerPreview.transform, PlayerDuoPos, PlayerDuoPose, 0.55f));
            StartCoroutine(MoveCamera(DuoCamPos, DuoCamLook, 33f, 0.6f));

            yield return new WaitForSecondsRealtime(0.1f);
            ShowVersus(enemy);
        }

        void ShowVersus(CharacterDef enemy)
        {
            _browseUi.gameObject.SetActive(false);
            _versusUi.gameObject.SetActive(true);

            _vsTag.text = _revealMode
                ? KomLoc.T("RIVAL REVELADO · A LA ARENA", "RIVAL REVEALED · TO THE ARENA")
                : KomLoc.T("RIVAL REVELADO · ENFRENTAMIENTO 01", "RIVAL REVEALED · ENCOUNTER 01");
            var venue = _versusUi.Find("Venue")?.GetComponent<Text>();
            if (venue != null)
            {
                var node = _session.CurrentNode;
                var stage = node != null ? KomData.GetStage(node.stageId) : null;
                // Sin escenario (nodo incompleto) se cae al nombre del torneo del catálogo,
                // que ya viene en el idioma activo; nunca a un literal en español.
                var tournament = KomData.Tournament;
                venue.text = stage != null
                    ? stage.displayName
                    : tournament != null && !string.IsNullOrEmpty(tournament.title)
                        ? tournament.title
                        : KomLoc.T("Torneo del Bienestar", "Wellbeing Tournament");
            }

            _playerVsName.text = _selected.displayName;
            _enemyVsName.text = enemy.displayName;
            _playerVsGenre.text = _selected.genre.ToUpperInvariant();
            _enemyVsGenre.text = enemy.genre.ToUpperInvariant();
            _nextLabel.text = _revealMode
                ? KomLoc.T("PELEAR  ·  ENTER", "FIGHT  ·  ENTER")
                : KomLoc.T("IR AL MAPA  ·  ENTER", "GO TO MAP  ·  ENTER");

            if (Application.isPlaying)
            {
                StartCoroutine(PunchVs());
            }
            _phase = Phase.Versus;
        }

        IEnumerator MoveCamera(Vector3 pos, Vector3 look, float fov, float duration)
        {
            if (_camera == null) yield break;
            Vector3 fromPos = _camera.transform.position;
            Quaternion fromRot = _camera.transform.rotation;
            Quaternion toRot = Quaternion.LookRotation(look - pos);
            float fromFov = _camera.fieldOfView;

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = KomUiMotion.EaseInOutCubic(t / duration);
                _camera.transform.position = Vector3.LerpUnclamped(fromPos, pos, k);
                _camera.transform.rotation = Quaternion.SlerpUnclamped(fromRot, toRot, k);
                _camera.fieldOfView = Mathf.Lerp(fromFov, fov, k);
                yield return null;
            }
            _camera.transform.position = pos;
            _camera.transform.rotation = toRot;
            _camera.fieldOfView = fov;
        }

        IEnumerator MoveTransform(Transform tr, Vector3 pos, Quaternion rot, float duration)
        {
            if (tr == null) yield break;
            Vector3 fromPos = tr.position;
            Quaternion fromRot = tr.rotation;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = KomUiMotion.EaseInOutCubic(t / duration);
                tr.position = Vector3.LerpUnclamped(fromPos, pos, k);
                tr.rotation = Quaternion.SlerpUnclamped(fromRot, rot, k);
                yield return null;
            }
            tr.position = pos;
            tr.rotation = rot;
        }

        IEnumerator PunchVs()
        {
            _vsText.rectTransform.localScale = Vector3.one * 1.5f;
            var c = _vsText.color;
            c.a = 0f;
            _vsText.color = c;
            float t = 0f;
            while (t < 0.34f)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / 0.34f);
                _vsText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, k * k);
                c.a = KomUiMotion.EaseOutCubic(k);
                _vsText.color = c;
                yield return null;
            }
            _vsText.rectTransform.localScale = Vector3.one;
        }

        void GoNext()
        {
            if (_phase != Phase.Versus) return;
            _phase = Phase.Leaving;
            // El rival ya se ha presentado: el mapa puede mostrarlo y entrar directo.
            _session.MarkEncounterRevealed();
            ScreenFader.Load(_revealMode ? KomScenes.Combat : KomScenes.Map);
        }

        IEnumerator IntroSequence()
        {
            yield return null;
            var group = _browseUi.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            float t = 0f;
            while (t < 0.4f)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = KomUiMotion.EaseOutCubic(t / 0.4f);
                yield return null;
            }
            group.alpha = 1f;
        }

        void ShowToast(string message)
        {
            if (_toastRoutine != null) StopCoroutine(_toastRoutine);
            _toastText.text = message;
            _toastGroup.alpha = 1f;
            _toastRoutine = StartCoroutine(ToastRoutine());
        }

        IEnumerator ToastRoutine()
        {
            yield return new WaitForSecondsRealtime(1.8f);
            float t = 0f;
            while (t < 0.35f)
            {
                t += Time.unscaledDeltaTime;
                _toastGroup.alpha = 1f - Mathf.Clamp01(t / 0.35f);
                yield return null;
            }
            _toastGroup.alpha = 0f;
            _toastRoutine = null;
        }

        void StartFlash(float alpha, float duration)
        {
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FlashRoutine(alpha, duration));
        }

        IEnumerator FlashRoutine(float alpha, float duration)
        {
            var c = _flash.color;
            c.a = alpha;
            _flash.color = c;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                c.a = Mathf.Lerp(alpha, 0f, Mathf.Clamp01(t / duration));
                _flash.color = c;
                yield return null;
            }
            c.a = 0f;
            _flash.color = c;
            _flashRoutine = null;
        }
    }
}
