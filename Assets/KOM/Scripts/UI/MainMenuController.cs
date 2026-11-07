using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KOM
{
    /// <summary>
    /// Menu principal.
    ///
    /// Composicion en dos mitades: la portada del elenco (blanco y negro) ocupa el tercio
    /// derecho a sangre y la columna izquierda es la identidad del torneo. El sello del
    /// logo cierra el titular KING, pegado a la G. La onda de audio nace en el borde
    /// inferior y sube: es el unico elemento que se mueve solo, y por eso la pantalla
    /// respira sin animar el texto.
    ///
    /// Reglas de UX que aplica:
    ///  - Una accion primaria (JUGAR / CONTINUAR) y dos secundarias, en una sola columna.
    ///  - Navegacion con teclado y mando ademas del raton: el foco es un estado explicito.
    ///  - Nada de texto de relleno: ni cabecera, ni contadores, ni rotulos de depuracion.
    ///  - El estado de la partida se dice antes de entrar (CONTINUAR solo si hay torneo).
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        GameSession _session;
        Canvas _canvas;
        GameObject _controlsOverlay;
        GameObject _settingsOverlay;

        sealed class SettingRow
        {
            public Text label;
            public Text value;
            public Text prev;
            public Text next;
            public Image accent;
            public System.Action<int> change;
            public System.Func<string> read;
            public string desc;
            public bool enabled = true;

            public void Refresh()
            {
                if (value != null && read != null) value.text = read();
                ApplyColors(false);
            }

            public void SetFocus(bool on)
            {
                ApplyColors(on);
            }

            public void ApplyColors(bool on)
            {
                var labelColor = !enabled ? KomTheme.TextLo : on ? KomTheme.TextHi : KomTheme.TextMid;
                if (label != null) label.color = labelColor;
                if (value != null) value.color = !enabled ? KomTheme.TextLo : on ? KomTheme.TextHi : KomTheme.TextMid;
                if (prev != null) prev.color = enabled ? (on ? KomTheme.AccentHot : KomTheme.TextLo) : KomTheme.Line;
                if (next != null) next.color = enabled ? (on ? KomTheme.AccentHot : KomTheme.TextLo) : KomTheme.Line;
                if (accent != null) accent.color = on ? KomTheme.AccentHot : KomTheme.Line;
            }
        }

        readonly List<SettingRow> _settingRows = new List<SettingRow>();
        int _settingsFocus;
        Text _settingDesc;

        EqualizerUI _equalizer;
        RectTransform _logoMark;
        Image _logoGlow;
        float _pulse;

        readonly List<KomMenuRow> _rows = new List<KomMenuRow>();
        int _focus;

        void Start()
        {
            _session = GameSession.Ensure();
            // El menu es la unica pantalla que apaga el modo local: asi CONTINUAR sigue
            // apuntando al torneo aunque se haya jugado un versus entre medias. Los
            // luchadores elegidos y las rondas se conservan mientras dure el modo.
            if (_session.VersusMode) _session.ExitVersus();
            BuildCamera();
            BuildUI();
            KomAudio.Menu();
            ScreenFader.FadeIn();
        }

        public void BuildEditorPreview()
        {
            _session = GameSession.Ensure();
            BuildCamera();
            BuildUI(false);
        }

        /// <summary>
        /// Preview del panel de ajustes: la preview normal lo deja cerrado y no hay forma
        /// de revisarlo sin jugar. Solo lo usa la herramienta de editor.
        /// </summary>
        public void BuildEditorPreviewSettings()
        {
            BuildEditorPreview();
            OpenSettings();
        }

        /// <summary>Preview de la referencia de controles, por el mismo motivo.</summary>
        public void BuildEditorPreviewControls()
        {
            BuildEditorPreview();
            OpenControls();
        }

        void BuildCamera()
        {
            if (Camera.main != null) return;
            var camGo = new GameObject("MainCamera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            camGo.transform.position = new Vector3(0f, 0f, -10f);
        }

        bool HasProgress => _session != null && _session.HasRun && !_session.IsRunComplete;

        void Update()
        {
            PulseAudio();
            if (_settingsOverlay != null && _settingsOverlay.activeSelf)
            {
                if (InputReader.CancelPressed()) { CloseSettings(); return; }
                if (InputReader.MoveDownPressed()) MoveSettingsFocus(1);
                if (InputReader.MoveUpPressed()) MoveSettingsFocus(-1);
                if (InputReader.MoveLeftPressed()) ChangeSetting(-1);
                if (InputReader.MoveRightPressed()) ChangeSetting(1);
                if (InputReader.ConfirmPressed()) ChangeSetting(1);
                return;
            }
            if (_controlsOverlay != null && _controlsOverlay.activeSelf)
            {
                if (InputReader.CancelPressed() || InputReader.ConfirmPressed()) CloseControls();
                return;
            }
            if (InputReader.MoveDownPressed()) MoveFocus(1);
            if (InputReader.MoveUpPressed()) MoveFocus(-1);
            if (InputReader.ConfirmPressed()) Activate();
        }

        /// <summary>
        /// La onda ya late sola; aqui solo se traduce su energia a un acento muy leve del
        /// sello: respiracion de escala y de halo, sin rebotes.
        /// </summary>
        void PulseAudio()
        {
            if (_equalizer == null) return;
            _pulse = Mathf.Lerp(_pulse, _equalizer.Level, Time.unscaledDeltaTime * 4f);

            if (_logoMark != null)
                _logoMark.localScale = Vector3.one * (1f + _pulse * 0.012f);

            if (_logoGlow != null)
            {
                var c = _logoGlow.color;
                c.a = 0.04f + _pulse * 0.05f;
                _logoGlow.color = c;
            }
        }

        void MoveFocus(int delta)
        {
            if (_rows.Count == 0) return;
            int next = Mathf.Clamp(_focus + delta, 0, _rows.Count - 1);
            if (next == _focus) return;
            _focus = next;
            for (int i = 0; i < _rows.Count; i++) _rows[i].SetFocus(i == _focus);
        }

        void SetFocus(int index, bool instant = false)
        {
            _focus = Mathf.Clamp(index, 0, _rows.Count - 1);
            for (int i = 0; i < _rows.Count; i++) _rows[i].SetFocus(i == _focus, instant);
        }

        void Activate()
        {
            if (_rows.Count == 0) return;
            var button = _rows[Mathf.Clamp(_focus, 0, _rows.Count - 1)].button;
            if (button != null && button.interactable) button.onClick.Invoke();
        }

        void BuildUI(bool animate = true, int focus = 0)
        {
            UIFactory.DestroyCanvas(_canvas);
            _rows.Clear();
            _settingRows.Clear();
            var canvas = UIFactory.CreateCanvas("MainMenuCanvas");
            _canvas = canvas;
            var root = (RectTransform)canvas.transform;

            var ink = UIFactory.AddImage(root, "Ink", UIFactory.WhiteSprite(), Color.black);
            UIFactory.Stretch(ink.rectTransform);

            BuildKeyArt(root);
            BuildIdentity(root);
            BuildWaveBand(root);
            BuildMenu(root);

            BuildControlsOverlay(root);
            BuildSettingsOverlay(root);

            // En el editor la preview no ejecuta Update, asi que el foco inicial se aplica
            // de golpe; en juego el resto de transiciones si se animan.
            SetFocus(focus, true);
            if (animate) StartCoroutine(IntroSequence());
        }

        // ------------------------------------------------------------------ key art

        void BuildKeyArt(Transform root)
        {
            var art = UIFactory.Rect(root, "KeyArt");
            UIFactory.Stretch(art);

            // El velo va DEBAJO de la portada, no encima. Es la unica forma de que ningun
            // rectangulo pueda tapar o cortar a los luchadores: el arte tiene el fondo
            // transparente, asi que el degradado sigue oscureciendo el lienzo detras de la
            // columna de texto (y el borde del retrato se funde en negro), pero los pixeles
            // opacos de los personajes no se mezclan con el. Antes iba encima y el primer
            // luchador perdia hasta el 55 % de su brillo.
            var left = UIFactory.AddImage(art, "ScrimLeft", UIFactory.HorizontalScrim(0.99f, 1.35f), Color.white);
            left.rectTransform.anchorMin = new Vector2(0f, 0f);
            left.rectTransform.anchorMax = new Vector2(0f, 1f);
            left.rectTransform.pivot = new Vector2(0f, 0.5f);
            // Corto a proposito: la caida llega a 0 mucho antes de su borde derecho, asi que
            // aunque el lienzo sea mas estrecho (o mas ancho) que 1920 nunca aparece un
            // escalon vertical sobre la portada.
            left.rectTransform.sizeDelta = new Vector2(1180f, 0f);
            left.rectTransform.anchoredPosition = Vector2.zero;

            var cover = UIFactory.LoadSprite("UI/Cover/portada");
            if (cover != null)
            {
                // La portada se ancla a la derecha y se escala a la altura del grupo: entra
                // completa (los siete luchadores) pero sin comerse la columna de texto. El
                // fondo de la ilustracion es negro puro, igual que el lienzo, asi que no se
                // ve ningun borde de imagen.
                var img = UIFactory.AddImage(art, "Cover", cover, new Color(0.92f, 0.92f, 0.92f, 1f));
                img.rectTransform.anchorMin = new Vector2(1f, 0f);
                img.rectTransform.anchorMax = new Vector2(1f, 0f);
                img.rectTransform.pivot = new Vector2(1f, 0f);
                float h = 838f;
                float w = h * (cover.rect.width / cover.rect.height);
                img.rectTransform.sizeDelta = new Vector2(w, h);
                img.rectTransform.anchoredPosition = new Vector2(8f, -6f);
                img.gameObject.AddComponent<KomDrift>().amplitude = new Vector2(6f, 0f);
            }

            var bottom = UIFactory.AddImage(art, "ScrimBottom",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.97f)), Color.white);
            bottom.rectTransform.anchorMin = new Vector2(0f, 0f);
            bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottom.rectTransform.pivot = new Vector2(0.5f, 0f);
            bottom.rectTransform.sizeDelta = new Vector2(0f, 340f);
            bottom.rectTransform.anchoredPosition = Vector2.zero;

            var top = UIFactory.AddImage(art, "ScrimTop",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0.92f), new Color(0f, 0f, 0f, 0f)), Color.white);
            top.rectTransform.anchorMin = new Vector2(0f, 1f);
            top.rectTransform.anchorMax = new Vector2(1f, 1f);
            top.rectTransform.pivot = new Vector2(0.5f, 1f);
            top.rectTransform.sizeDelta = new Vector2(0f, 220f);
            top.rectTransform.anchoredPosition = Vector2.zero;

            UIFactory.AddVignette(art, 0.5f);
            UIFactory.AddGrain(art, 0.022f);
        }

        // ------------------------------------------------------------------ identidad

        void BuildIdentity(Transform root)
        {
            var col = UIFactory.Rect(root, "Identity");
            col.anchorMin = new Vector2(0f, 1f);
            col.anchorMax = new Vector2(0f, 1f);
            col.pivot = new Vector2(0f, 1f);
            col.sizeDelta = new Vector2(820f, 470f);
            col.anchoredPosition = new Vector2(KomTheme.Margin, -168f);

            var kicker = UIFactory.AddLabel(col, "Kicker", KomLoc.T("Torneo del Bienestar", "Wellbeing Tournament"));
            UIFactory.Place(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(700f, 20f));

            var title = UIFactory.AddDisplayText(col, "Title", "KING", KomTheme.DisplayXL, KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(-6f, -30f), new Vector2(760f, 150f));

            BuildSeal(col, title);

            var title2 = UIFactory.AddDisplayText(col, "Title2", "OF MUSIC", 92, KomTheme.Accent, TextAnchor.UpperLeft);
            UIFactory.Place(title2.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(-2f, -164f), new Vector2(760f, 110f));

            var rule = UIFactory.AddImage(col, "Rule", UIFactory.WhiteSprite(), KomTheme.LineStrong);
            UIFactory.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -282f), new Vector2(96f, 1f));

            var diamond = UIFactory.AddDiamond(col, "Diamond", KomTheme.Accent, 7f);
            UIFactory.Place(diamond.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(108f, -282f), new Vector2(7f, 7f));

            // Una sola frase. El parrafo anterior (generos, trono y derrotas) explicaba el
            // juego dos veces y competia con el titular: la portada no es un manual.
            var tagline = UIFactory.AddText(col, "Tagline",
                KomLoc.T("El torneo donde cada género se juega su voz.",
                         "The tournament where every genre stakes its voice."),
                KomTheme.Body, KomTheme.TextMid, TextAnchor.UpperLeft);
            UIFactory.Place(tagline.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -306f), new Vector2(680f, 30f));
        }

        /// <summary>
        /// Sello del torneo al final del titular, ocupando el hueco que deja la G.
        ///
        /// La marca vivia en la cabecera a 54 px y a ese tamano el grabado se leia como
        /// una mancha: el detalle del calendario desaparecia. Aqui se mide el ancho real
        /// del titular (<see cref="Text.preferredWidth"/>) y el sello se pega a su final
        /// con la altura de la linea de capitales, centrado optico con la palabra.
        /// </summary>
        void BuildSeal(Transform col, Text title)
        {
            var logo = UIFactory.LoadSprite("UI/Logo/kom_logo");
            if (logo == null) return;

            const float size = 132f;
            const float gap = 26f;
            float titleWidth = title != null && title.preferredWidth > 8f ? title.preferredWidth : 320f;
            float centerX = -6f + titleWidth + gap + size * 0.5f;
            // Centro de la linea de capitales: el titular mide 150 px y la tinta de KING
            // ocupa del 28 % al 99 % de esa caja.
            const float centerY = -125f;

            _logoGlow = UIFactory.AddGlow(col, "MarkGlow", new Color(1f, 1f, 1f, 0.05f));
            UIFactory.Place(_logoGlow.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(centerX, centerY), new Vector2(size * 2.4f, size * 2.4f));

            var mark = UIFactory.AddImage(col, "Seal", logo, Color.white);
            mark.preserveAspect = true;
            UIFactory.Place(mark.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(centerX, centerY), new Vector2(size, size));
            _logoMark = mark.rectTransform;
        }

        // ------------------------------------------------------------------ onda

        /// <summary>
        /// Onda de audio a sangre sobre el borde inferior: 96 trazos finos que nacen en el
        /// filo de la pantalla y se apagan hacia arriba. Es una textura, no un ecualizador:
        /// no compite con la portada ni con la columna de acciones, y no hay banda negra
        /// que la separe del borde.
        /// </summary>
        void BuildWaveBand(Transform root)
        {
            var band = UIFactory.Rect(root, "WaveBand");
            band.anchorMin = new Vector2(0f, 0f);
            band.anchorMax = new Vector2(1f, 0f);
            band.pivot = new Vector2(0.5f, 0f);
            band.sizeDelta = new Vector2(0f, 150f);
            band.anchoredPosition = Vector2.zero;

            _equalizer = band.gameObject.AddComponent<EqualizerUI>();
            _equalizer.barMax = 118f;
            _equalizer.barMin = 2f;
            _equalizer.Build(96, KomTheme.TextHi, 0.50f);
        }

        // ------------------------------------------------------------------ acciones

        void BuildMenu(Transform root)
        {
            var menu = UIFactory.Rect(root, "Menu");
            menu.anchorMin = new Vector2(0f, 1f);
            menu.anchorMax = new Vector2(0f, 1f);
            menu.pivot = new Vector2(0f, 1f);
            menu.sizeDelta = new Vector2(560f, 360f);
            menu.anchoredPosition = new Vector2(KomTheme.Margin, -580f);

            var entries = new List<(string label, System.Action action)>();
            if (HasProgress)
            {
                entries.Add((KomLoc.T("CONTINUAR TORNEO", "CONTINUE RUN"), () => BeginPreload(KomScenes.Map)));
                entries.Add((KomLoc.T("NUEVA PARTIDA", "NEW GAME"), StartRun));
            }
            else
            {
                entries.Add((KomLoc.T("JUGAR", "PLAY"), StartRun));
            }
            // Dos jugadores en la misma maquina: no pasa por la historia ni por el torneo.
            entries.Add((KomLoc.T("2 JUGADORES", "2 PLAYERS"), StartVersus));
            // El idioma se cambia desde aquí y no en un submenú: es una acción de un solo
            // clic y la fila ya dice en qué idioma está el juego. Los rótulos se quedan
            // cortos a propósito: la columna comparte pantalla con la portada y una fila
            // larga se mete en la cara de los luchadores.
            entries.Add((KomLoc.T("IDIOMA · ESPAÑOL", "LANGUAGE · EN"), ToggleLanguage));
            entries.Add((KomLoc.T("PANTALLA", "DISPLAY"), OpenSettings));
            entries.Add((KomLoc.T("CONTROLES", "CONTROLS"), OpenControls));
            entries.Add((KomLoc.T("SALIR", "QUIT"), QuitGame));

            const float rowH = 50f;
            for (int i = 0; i < entries.Count; i++)
            {
                BuildRow(menu, i, entries[i].label, entries[i].action, -i * rowH, rowH);
            }
        }

        void BuildRow(Transform parent, int index, string text, System.Action action, float y, float height)
        {
            var holder = UIFactory.Rect(parent, "Row" + index);
            holder.anchorMin = new Vector2(0f, 1f);
            holder.anchorMax = new Vector2(0f, 1f);
            holder.pivot = new Vector2(0f, 1f);
            holder.sizeDelta = new Vector2(520f, height);
            holder.anchoredPosition = new Vector2(0f, y);

            var hit = UIFactory.AddImage(holder, "Hit", UIFactory.WhiteSprite(), new Color(1f, 1f, 1f, 0f));
            UIFactory.Stretch(hit.rectTransform);
            hit.raycastTarget = true;
            var button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => action());

            // Sin relleno de fila: el rectangulo oscuro del foco tapaba a los personajes
            // de la portada. El foco se lee en el filete, el chevron y el valor del texto.
            var rule = UIFactory.AddHLine(holder, "Rule", KomTheme.Line, 1f);
            rule.rectTransform.anchorMin = new Vector2(0f, 0f);
            rule.rectTransform.anchorMax = new Vector2(1f, 0f);
            rule.rectTransform.pivot = new Vector2(0f, 0f);
            rule.rectTransform.anchoredPosition = Vector2.zero;

            var num = UIFactory.AddText(holder, "Index", (index + 1).ToString("00"), KomTheme.Micro, KomTheme.TextLo, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(num.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(4f, 2f), new Vector2(36f, 18f));

            var label = UIFactory.AddText(holder, "Label", text, KomTheme.Title, KomTheme.TextMid, TextAnchor.MiddleLeft, FontStyle.Normal);
            label.font = UIFactory.DisplayFont;
            // Sin salto: "PANTALLA Y GRÁFICOS" y "DISPLAY & GRAPHICS" entran en la caja,
            // pero un idioma con palabras mas largas no debe partir la fila en dos lineas.
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(46f, 2f), new Vector2(420f, 36f));

            var chevron = UIFactory.AddText(holder, "Chevron", "›", 30, KomTheme.AccentHot, TextAnchor.MiddleLeft, FontStyle.Normal);
            UIFactory.Place(chevron.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-10f, 4f), new Vector2(30f, 30f));
            var cc = chevron.color; cc.a = 0f; chevron.color = cc;

            var row = holder.gameObject.AddComponent<KomMenuRow>();
            row.Bind(button, null, rule, chevron, num, label);
            row.onHover = r => SetFocus(_rows.IndexOf(r));
            _rows.Add(row);
        }

        // ------------------------------------------------------------------ entrada animada

        IEnumerator IntroSequence()
        {
            yield return null;
            foreach (var row in _rows)
            {
                var group = row.GetComponent<CanvasGroup>();
                if (group == null) group = row.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
            }
            float t = 0f;
            while (t < 0.5f)
            {
                t += Time.unscaledDeltaTime;
                float k = KomUiMotion.EaseOutCubic(t / 0.5f);
                for (int i = 0; i < _rows.Count; i++)
                {
                    var group = _rows[i].GetComponent<CanvasGroup>();
                    if (group == null) continue;
                    float local = Mathf.Clamp01((k - i * 0.09f) / 0.55f);
                    group.alpha = local;
                    var rt = (RectTransform)_rows[i].transform;
                    rt.anchoredPosition = new Vector2(-18f * (1f - local), rt.anchoredPosition.y);
                }
                yield return null;
            }
        }

        // ------------------------------------------------------------------ overlays

        void BuildControlsOverlay(Transform root)
        {
            _controlsOverlay = UIFactory.Rect(root, "ControlsOverlay").gameObject;
            var overlayRt = (RectTransform)_controlsOverlay.transform;
            UIFactory.Stretch(overlayRt);

            var dim = UIFactory.AddImage(overlayRt, "Dim", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0.86f));
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            var panel = UIFactory.AddPanel(overlayRt, "Panel", new Color(0.031f, 0.035f, 0.039f, 0.99f), 4);
            // 610 y no 560: la columna del jugador 1 lleva ahora las dos filas de ataque
            // (una por modo) y con 560 la ultima se montaba encima del boton CONTINUAR.
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1120f, 610f));
            UIFactory.AddFrame(panel.transform, "Border", KomTheme.Line, 1f);

            var kicker = UIFactory.AddLabel(panel.transform, "Kicker", KomLoc.T("Referencia rápida", "Quick reference"), TextAnchor.UpperLeft);
            UIFactory.Place(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(56f, -48f), new Vector2(400f, 20f));

            var title = UIFactory.AddDisplayText(panel.transform, "Title", KomLoc.T("CONTROLES", "CONTROLS"), 54, KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(52f, -72f), new Vector2(600f, 70f));

            var close = UIFactory.AddGhostButton(panel.transform, "Close", KomLoc.T("CERRAR  ·  ESC", "CLOSE  ·  ESC"), CloseControls, 19);
            UIFactory.Place(close.image.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-56f, -58f), new Vector2(210f, 48f));

            var rule = UIFactory.AddHLine(panel.transform, "Rule", KomTheme.Line, 1f);
            UIFactory.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -158f), new Vector2(-112f, 1f));

            BuildControlsColumn(panel.transform, 56f, -196f,
                KomLoc.T("JUGADOR 1", "PLAYER 1"),
                new[]
                {
                    ("A / D", KomLoc.T("Caminar", "Walk")),
                    ("W", KomLoc.T("Saltar", "Jump")),
                    ("S", KomLoc.T("Agacharse", "Crouch")),
                    (KomLoc.T("SHIFT o L", "SHIFT or L"), KomLoc.T("Bloquear", "Block")),
                    (KomLoc.T("DOBLE A / D", "DOUBLE A / D"), KomLoc.T("Correr", "Run")),
                    // En versus la fila de ataque cambia de teclas: C/V/B/N y M. Antes el panel
                    // enseñaba G/H/J/K también en dos jugadores y esas teclas no hacen nada ahí.
                    ("G / H / J / K", KomLoc.T("Ataques · 1 jug.", "Attacks · 1P")),
                    ("C / V / B / N", KomLoc.T("Ataques · 2 jug.", "Attacks · 2P")),
                    ("U / M", KomLoc.T("Poder especial · 1 / 2 jug.", "Special · 1P / 2P")),
                });

            BuildControlsColumn(panel.transform, 424f, -196f,
                KomLoc.T("JUGADOR 2", "PLAYER 2"),
                new[]
                {
                    ("← / →", KomLoc.T("Caminar", "Walk")),
                    ("↑", KomLoc.T("Saltar", "Jump")),
                    ("↓", KomLoc.T("Agacharse", "Crouch")),
                    (KomLoc.T("SHIFT DER", "RIGHT SHIFT"), KomLoc.T("Bloquear", "Block")),
                    (KomLoc.T("DOBLE ← / →", "DOUBLE ← / →"), KomLoc.T("Correr", "Run")),
                    ("U / I / O / P", KomLoc.T("Ataques", "Attacks")),
                    ("Y", KomLoc.T("Poder especial", "Special power")),
                });

            BuildControlsColumn(panel.transform, 792f, -196f,
                KomLoc.T("SISTEMA Y COMBOS", "SYSTEM & COMBOS"),
                new[]
                {
                    ("G G G", KomLoc.T("Combo 1 · 4 golpes", "Combo 1 · 4 hits")),
                    ("H H H", KomLoc.T("Combo 2 · 5 golpes", "Combo 2 · 5 hits")),
                    ("J J J J", KomLoc.T("Combo 3 · 7 golpes", "Combo 3 · 7 hits")),
                    // Los mismos combos con las teclas de cada jugador en versus: repetir el
                    // botón de ataque 3 o 4 veces. Antes habia dos filas "2J · J1/J2" que
                    // repetian la lista de teclas y no aportaban nada.
                    (KomLoc.T("J1", "P1"), "Versus: C C C · V V V · B B B B"),
                    (KomLoc.T("J2", "P2"), "Versus: U U U · I I I · O O O O"),
                    (KomLoc.T("F1 / R3", "F1 / R3"), KomLoc.T("Esta pantalla", "This screen")),
                    ("ESC / START", KomLoc.T("Pausa", "Pause")),
                });

            _controlsOverlay.SetActive(false);
        }

        void BuildControlsColumn(Transform parent, float x, float y, string header, (string key, string text)[] items)
        {
            var col = UIFactory.Rect(parent, "Col_" + header);
            col.anchorMin = new Vector2(0f, 1f);
            col.anchorMax = new Vector2(0f, 1f);
            col.pivot = new Vector2(0f, 1f);
            col.sizeDelta = new Vector2(320f, 380f);
            col.anchoredPosition = new Vector2(x, y);

            var head = UIFactory.AddLabel(col, "Head", header, TextAnchor.UpperLeft);
            UIFactory.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(300f, 18f));

            var underline = UIFactory.AddHLine(col, "Underline", KomTheme.AccentDim, 1f);
            UIFactory.Place(underline.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -24f), new Vector2(34f, 1f));

            // Tecla y verbo comparten linea: en dos lineas el verbo parece pertenecer a la
            // tecla siguiente y la columna se estira el doble.
            float rowY = -50f;
            foreach (var (key, text) in items)
            {
                float keyW = Mathf.Max(38f, 16f + key.Length * 9f);
                var cap = UIFactory.AddKeycap(col, "Key_" + key, key, keyW);
                UIFactory.Place(cap, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, rowY), new Vector2(keyW, 24f));

                var label = UIFactory.AddText(col, "Text_" + key, text, 15, KomTheme.TextMid, TextAnchor.MiddleLeft);
                UIFactory.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(keyW + 12f, rowY + 1f), new Vector2(300f - keyW - 12f, 24f));
                rowY -= 32f;
            }
        }

        void OpenControls()
        {
            if (_controlsOverlay == null) return;
            _controlsOverlay.SetActive(true);
            _controlsOverlay.transform.SetAsLastSibling();
        }

        void CloseControls()
        {
            if (_controlsOverlay != null) _controlsOverlay.SetActive(false);
        }

        // ------------------------------------------------------------------ pantalla y gráficos

        /// <summary>
        /// Panel de pantalla y gráficos. Tres filas, cada una con su valor a la derecha y
        /// flechas para cambiarlo: se navega con ↑↓ y se modifica con ←→ o Enter, igual
        /// que el resto del menú, y el ratón puede pulsar la fila o las flechas.
        /// </summary>
        void BuildSettingsOverlay(Transform root)
        {
            _settingsOverlay = UIFactory.Rect(root, "SettingsOverlay").gameObject;
            var overlayRt = (RectTransform)_settingsOverlay.transform;
            UIFactory.Stretch(overlayRt);

            var dim = UIFactory.AddImage(overlayRt, "Dim", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0.86f));
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            var panel = UIFactory.AddPanel(overlayRt, "Panel", new Color(0.031f, 0.035f, 0.039f, 0.99f), 4);
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1120f, 500f));
            UIFactory.AddFrame(panel.transform, "Border", KomTheme.Line, 1f);

            var kicker = UIFactory.AddLabel(panel.transform, "Kicker", KomLoc.T("Sistema", "System"), TextAnchor.UpperLeft);
            UIFactory.Place(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(56f, -48f), new Vector2(400f, 20f));

            var title = UIFactory.AddDisplayText(panel.transform, "Title",
                KomLoc.T("PANTALLA Y GRÁFICOS", "DISPLAY & GRAPHICS"), 48, KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(52f, -70f), new Vector2(800f, 64f));

            var close = UIFactory.AddGhostButton(panel.transform, "Close", KomLoc.T("CERRAR  ·  ESC", "CLOSE  ·  ESC"), CloseSettings, 19);
            UIFactory.Place(close.image.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-56f, -58f), new Vector2(210f, 48f));

            var rule = UIFactory.AddHLine(panel.transform, "Rule", KomTheme.Line, 1f);
            UIFactory.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -156f), new Vector2(-112f, 1f));

            BuildSettingRow(panel.transform, 0, -186f,
                KomLoc.T("PANTALLA COMPLETA", "FULLSCREEN"),
                () => KomSettings.FullscreenLabel(),
                _ => KomSettings.ToggleFullscreen(),
                KomLoc.T("En pantalla completa manda el monitor: se usa su resolución nativa.",
                    "In fullscreen the monitor rules: its native resolution is used."));

            BuildSettingRow(panel.transform, 1, -258f,
                KomLoc.T("RESOLUCIÓN", "RESOLUTION"),
                () => KomSettings.ResolutionLabel(),
                dir => { if (!KomSettings.Fullscreen) KomSettings.CycleResolution(dir); },
                KomLoc.T("Solo se aplica en ventana.",
                    "Only applies in windowed mode."));

            BuildSettingRow(panel.transform, 2, -330f,
                KomLoc.T("CALIDAD GRÁFICA", "GRAPHICS QUALITY"),
                () => KomSettings.QualityLabel(KomSettings.QualityIndex),
                dir => KomSettings.CycleQuality(dir),
                KomLoc.T("Sombras, suavizado de bordes y oclusión ambiental.",
                    "Shadows, anti-aliasing and ambient occlusion."));

            _settingDesc = UIFactory.AddText(panel.transform, "SettingDesc", "",
                15, KomTheme.TextMid, TextAnchor.MiddleCenter);
            UIFactory.Place(_settingDesc.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 52f), new Vector2(900f, 20f));

            var hint = UIFactory.AddText(panel.transform, "Hint",
                KomLoc.T("↑↓ ELEGIR      ←→ CAMBIAR      ESC CERRAR", "↑↓ SELECT      ←→ CHANGE      ESC CLOSE"),
                11, KomTheme.TextLo, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 22f), new Vector2(700f, 16f));

            _settingsOverlay.SetActive(false);
        }

        void BuildSettingRow(Transform parent, int index, float y, string label,
            System.Func<string> read, System.Action<int> change, string desc)
        {
            var holder = UIFactory.Rect(parent, "Setting" + index);
            holder.anchorMin = new Vector2(0f, 1f);
            holder.anchorMax = new Vector2(0f, 1f);
            holder.pivot = new Vector2(0f, 1f);
            holder.sizeDelta = new Vector2(1008f, 58f);
            holder.anchoredPosition = new Vector2(56f, y);

            var bg = UIFactory.AddPanel(holder, "Bg", KomTheme.SurfaceLo, 3);
            UIFactory.Stretch(bg.rectTransform);
            UIFactory.AddFrame(holder, "Border", KomTheme.LineFaint, 1f);

            var accent = UIFactory.AddImage(holder, "Accent", UIFactory.WhiteSprite(), KomTheme.Line);
            UIFactory.Place(accent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(3f, 0f));

            var labelText = UIFactory.AddText(holder, "Label", label, 21, KomTheme.TextMid, TextAnchor.MiddleLeft);
            UIFactory.Place(labelText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(26f, 0f), new Vector2(560f, 30f));

            var valueText = UIFactory.AddText(holder, "Value", read(), 21, KomTheme.TextMid, TextAnchor.MiddleRight, FontStyle.Bold);
            valueText.font = UIFactory.DisplayFont;
            UIFactory.Place(valueText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(620f, 0f), new Vector2(220f, 30f));

            var prev = UIFactory.AddText(holder, "Prev", "‹", 30, KomTheme.TextLo, TextAnchor.MiddleCenter);
            UIFactory.Place(prev.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(878f, 2f), new Vector2(40f, 40f));

            var next = UIFactory.AddText(holder, "Next", "›", 30, KomTheme.TextLo, TextAnchor.MiddleCenter);
            UIFactory.Place(next.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(948f, 2f), new Vector2(40f, 40f));

            var row = new SettingRow
            {
                label = labelText,
                value = valueText,
                prev = prev,
                next = next,
                accent = accent,
                change = change,
                read = read,
                desc = desc
            };

            // Superficie de la fila: un clic avanza el valor (como Enter). Las flechas se
            // crean DESPUES para quedar por encima y poder retroceder con el ratón.
            var hit = UIFactory.AddImage(holder, "Hit", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0f));
            UIFactory.Stretch(hit.rectTransform);
            hit.raycastTarget = true;
            var button = hit.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => ChangeSetting(row, 1));

            AddArrowButton(holder, "PrevHit", 878f, () => ChangeSetting(row, -1));
            AddArrowButton(holder, "NextHit", 948f, () => ChangeSetting(row, 1));

            _settingRows.Add(row);
            row.Refresh();
        }

        void AddArrowButton(Transform parent, string name, float x, System.Action action)
        {
            var hit = UIFactory.AddImage(parent, name, UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0f));
            UIFactory.Place(hit.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(x, 2f), new Vector2(44f, 48f));
            hit.raycastTarget = true;
            var button = hit.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => action());
        }

        void OpenSettings()
        {
            if (_settingsOverlay == null) return;
            // Si el jugador redimensionó la ventana a mano, la fila tiene que decir la verdad.
            KomSettings.SyncWindow();
            _settingsFocus = FirstEnabledRow(0, 1);
            _settingsOverlay.SetActive(true);
            _settingsOverlay.transform.SetAsLastSibling();
            RefreshSettings();
        }

        void CloseSettings()
        {
            if (_settingsOverlay != null) _settingsOverlay.SetActive(false);
        }

        void MoveSettingsFocus(int delta)
        {
            if (_settingRows.Count == 0) return;
            int next = FirstEnabledRow(_settingsFocus + delta, delta >= 0 ? 1 : -1);
            if (next < 0) return;
            _settingsFocus = next;
            for (int i = 0; i < _settingRows.Count; i++) _settingRows[i].SetFocus(i == _settingsFocus);
            RefreshDescription();
        }

        /// <summary>
        /// Primera fila interactuable en la dirección pedida. Una fila deshabilitada (la
        /// resolución en pantalla completa) no debe poder enfocarse: el jugador pulsaría
        /// izquierda/derecha sin que ocurra nada.
        /// </summary>
        int FirstEnabledRow(int from, int step)
        {
            if (_settingRows.Count == 0) return 0;
            if (step == 0) step = 1;
            for (int i = Mathf.Clamp(from, 0, _settingRows.Count - 1);
                 i >= 0 && i < _settingRows.Count; i += step)
            {
                if (_settingRows[i].enabled) return i;
            }
            // Sin filas en esa dirección: conservar la actual si sigue siendo válida.
            return _settingRows[Mathf.Clamp(_settingsFocus, 0, _settingRows.Count - 1)].enabled
                ? Mathf.Clamp(_settingsFocus, 0, _settingRows.Count - 1)
                : -1;
        }

        void ChangeSetting(int direction)
        {
            if (_settingRows.Count == 0) return;
            ChangeSetting(_settingRows[Mathf.Clamp(_settingsFocus, 0, _settingRows.Count - 1)], direction);
        }

        void ChangeSetting(SettingRow row, int direction)
        {
            if (row == null || !row.enabled) return;
            row.change?.Invoke(direction);
            RefreshSettings();
        }

        void RefreshSettings()
        {
            for (int i = 0; i < _settingRows.Count; i++)
            {
                // En pantalla completa manda el monitor: la resolución no se puede elegir.
                _settingRows[i].enabled = i != 1 || !KomSettings.Fullscreen;
            }

            // Al activar pantalla completa la fila enfocada puede quedar deshabilitada.
            if (_settingRows.Count > 0 && !_settingRows[Mathf.Clamp(_settingsFocus, 0, _settingRows.Count - 1)].enabled)
                _settingsFocus = FirstEnabledRow(_settingsFocus, 1);

            for (int i = 0; i < _settingRows.Count; i++)
            {
                _settingRows[i].Refresh();
                _settingRows[i].SetFocus(i == _settingsFocus);
            }
            RefreshDescription();
        }

        void RefreshDescription()
        {
            if (_settingDesc == null) return;
            int i = Mathf.Clamp(_settingsFocus, 0, _settingRows.Count - 1);
            _settingDesc.text = _settingRows.Count > 0 ? (_settingRows[i].desc ?? "") : "";
        }

        /// <summary>
        /// Cambia el idioma y reconstruye la pantalla en el idioma nuevo. Se conserva la
        /// fila enfocada y se salta la entrada animada: el jugador ya estaba aquí.
        /// </summary>
        void ToggleLanguage()
        {
            int focus = _focus;
            KomLoc.Toggle();
            BuildUI(false, focus);
        }

        // ------------------------------------------------------------------ flujo

        // Una sola pantalla de carga para todo el juego (ScreenFader): la precarga se paga
        // ahi con progreso real y la escena no se activa hasta que termina.
        void BeginPreload(string sceneName)
        {
            if (_controlsOverlay != null) _controlsOverlay.SetActive(false);
            ScreenFader.Load(sceneName, true);
        }

        // La partida nueva entra por la historia: el capitulo de apertura hace de pantalla
        // de carga y calienta los recursos en segundo plano, sin modal ni barra.
        void StartRun()
        {
            _session.ResetRun();
            StoryFlow.Play(StoryFlow.Intro, KomScenes.CharacterSelect);
        }

        void StartVersus()
        {
            _session.BeginVersus();
            // La primera vez hay que pagar los binds de los luchadores: la precarga
            // muestra su progreso real en vez de congelar la seleccion.
            ScreenFader.Load(KomScenes.CharacterSelect, true);
        }

        void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
