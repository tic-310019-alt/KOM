using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace KOM
{
    /// <summary>
    /// HUD de combate.
    ///
    /// Reglas que aplica:
    ///  - Los datos vitales van ARRIBA y simetricos: vida grande, guardia y super debajo,
    ///    en el mismo orden en los dos lados para poder comparar de un vistazo.
    ///  - El reloj es el unico elemento centrado: es el que marca el ritmo de la ronda.
    ///  - La leyenda de teclas se apaga sola a los pocos segundos: informa al que empieza
    ///    y deja de tapar la pelea al que ya sabe jugar.
    ///  - Los avisos (banner) y los overlays usan el mismo sistema de diseno que los menus.
    /// </summary>
    public sealed class CombatHud : MonoBehaviour
    {
        Canvas _canvas;
        RectTransform _root;

        Text _playerName, _playerGenre, _enemyName, _enemyGenre;
        Image _playerFill, _playerGhost, _enemyFill, _enemyGhost;
        Image _playerBevel, _enemyBevel;
        Image _playerSuper, _enemySuper;
        Image _playerSuperBevel, _enemySuperBevel;
        Color _playerAccent, _enemyAccent;
        float _playerTarget = 1f, _enemyTarget = 1f;
        float _playerShown = 1f, _enemyShown = 1f;
        float _playerGhostShown = 1f, _enemyGhostShown = 1f;
        float _playerSuperShown, _enemySuperShown;
        float _playerSuperTarget, _enemySuperTarget;
        Image[] _playerPips, _enemyPips;

        // ------------------------------------------------------------------ paletas de barra
        //
        // La vida y la estamina son las DOS únicas barras con color del HUD, y el color
        // es información, no adorno: dice cuánto queda sin leer un número.
        //
        // VIDA — verde -> lima -> ámbar -> naranja -> rojo. Es la rampa clásica de pelea:
        // el verde tranquilo se vuelve cálido y luego rojo, así el jugador percibe el
        // peligro por el tono antes de mirar la longitud. Los pasos intermedios existen
        // para que el cruce no pase por un marrón sucio (interpolar verde->rojo en RGB
        // directo da exactamente eso).
        //
        // ESTAMINA — azul -> índigo -> violeta. Se queda en la FAMILIA FRÍA a propósito:
        // si la estamina se pusiera ámbar o roja competiría con la vida y el jugador no
        // sabría qué barra está en peligro. El violeta es el extremo "gastado" del azul
        // (misma temperatura, menos luz) y sigue siendo distinguible del rojo para la
        // mayoría de los daltonismos, porque conserva el canal azul alto.
        static readonly (float at, Color c)[] HealthRamp =
        {
            (1.00f, new Color(0.310f, 0.800f, 0.420f)),   // verde
            (0.68f, new Color(0.624f, 0.824f, 0.278f)),   // verde lima
            (0.46f, new Color(0.937f, 0.753f, 0.184f)),   // ámbar
            (0.28f, new Color(0.941f, 0.475f, 0.165f)),   // naranja
            (0.10f, new Color(0.863f, 0.227f, 0.192f)),   // rojo
            (0.00f, new Color(0.659f, 0.125f, 0.118f)),   // rojo hundido
        };

        // SÚPER — azul, y azul de verdad: es la única barra que NO habla de peligro sino de
        // carga. La rampa va de azul profundo (vacía) a azul eléctrico (lista), así el
        // jugador ve acercarse el especial por el brillo además de por la longitud. Al
        // llegar al 100 % la barra pulsa hacia blanco (ver SuperBarColor).
        static readonly (float at, Color c)[] SuperRamp =
        {
            (1.00f, new Color(0.310f, 0.769f, 1.000f)),   // azul eléctrico
            (0.60f, new Color(0.204f, 0.573f, 0.976f)),   // azul
            (0.25f, new Color(0.145f, 0.408f, 0.812f)),   // azul profundo
            (0.00f, new Color(0.098f, 0.278f, 0.588f)),   // azul apagado
        };

        // Por debajo de esta fracción la barra avisa (pulso lento, nunca estrobo).
        const float CriticalFraction = 0.22f;

        Text _roundLabel, _timer, _encounterLabel, _stageLabel;
        float _timeValue;
        bool _timeLow;

        GameObject _banner;
        CanvasGroup _bannerGroup;
        Text _bannerMain, _bannerSub;
        Image _bannerRule;
        Coroutine _bannerRoutine;

        GameObject _pauseOverlay, _resultOverlay;
        Action _resumeAction, _menuAction;
        Action _primaryAction, _secondaryAction;
        Text _resultTitle, _resultSub, _resultBody, _primaryLabel, _secondaryLabel;
        Text _resultKicker, _resultHint;
        Image _resultPlayerCard, _resultEnemyCard;
        UIFactory.CardAura _resultPlayerAura, _resultEnemyAura;
        Button _primaryButton, _secondaryButton;
        Image _hitFlash;
        Coroutine _flashRoutine;

        // Contador de golpes encadenados (feedback de recompensa).
        RectTransform _comboRoot;
        Text _comboNumber, _comboLabel;
        Image _comboRule;
        float _comboIdle = 99f;
        float _comboAlpha;
        float _comboPop;
        // Alfa propio del filete (sube con los combos largos). Se guarda aparte
        // porque el alfa final es _comboAlpha * este valor.
        float _comboRuleBaseAlpha = 0.45f;

        CanvasGroup _legendGroup;
        GameObject _legendCombos;
        bool _legendPersistent;
        bool _versus;
        int _roundsToWin = 2;
        float _legendTimer;
        bool _legendFading;

        GameObject _controlsOverlay;
        Button _controlsCloseButton;
        System.Action _controlsClose;
        public bool ControlsVisible => _controlsOverlay != null && _controlsOverlay.activeSelf;

        // Mapa consultable desde la pausa de campaña. Es una capa de interfaz: no
        // descarga la escena de combate, asi que al cerrar se vuelve a la MISMA pelea.
        GameObject _worldMapOverlay;
        Action _worldMapClose;
        Action _mapAction;
        Button _pauseMapButton;
        public bool WorldMapVisible => _worldMapOverlay != null && _worldMapOverlay.activeSelf;

        public bool ResultVisible => _resultOverlay != null && _resultOverlay.activeSelf;
        public bool PauseVisible => _pauseOverlay != null && _pauseOverlay.activeSelf;

        public void Initialize(CharacterDef player, CharacterDef enemy, string stageName, int encounter, int totalEncounters,
            bool versus = false, int roundsToWin = 2)
        {
            _versus = versus;
            _roundsToWin = Mathf.Clamp(roundsToWin, 1, 3);
            _playerAccent = KomData.UiAccent(player);
            _enemyAccent = KomData.UiAccent(enemy);
            _canvas = UIFactory.CreateCanvas("CombatHudCanvas", 100);
            _root = (RectTransform)_canvas.transform;

            // Bandas de legibilidad: el HUD tiene que leerse sobre escenarios casi blancos
            // (La Madrugadora) sin dibujar paneles con borde.
            var top = UIFactory.AddImage(_root, "HudTop",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0.86f), new Color(0f, 0f, 0f, 0f)), Color.white);
            UIFactory.Place(top.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 230f));

            var bottom = UIFactory.AddImage(_root, "HudBottom",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.78f)), Color.white);
            UIFactory.Place(bottom.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0f, 150f));

            BuildHealthSide(player, true, versus, _roundsToWin);
            BuildHealthSide(enemy, false, versus, _roundsToWin);
            BuildTimerAndLabels(stageName, encounter, totalEncounters, versus, _roundsToWin);
            BuildBanner();
            BuildComboCounter();
            BuildLegend();
            BuildPauseOverlay();
            BuildControlsOverlay();
            BuildResultOverlay();
            BuildWorldMapOverlay();
            BuildHitFlash();
            SetHealth(1f, 1f, true);
        }

        void BuildHealthSide(CharacterDef def, bool player, bool versus = false, int roundsToWin = 2)
        {
            var plate = UIFactory.Rect(_root, player ? "PlayerPlate" : "EnemyPlate");
            plate.anchorMin = player ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            plate.anchorMax = plate.anchorMin;
            plate.pivot = player ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            plate.anchoredPosition = player ? new Vector2(44f, -28f) : new Vector2(-44f, -28f);
            plate.sizeDelta = new Vector2(640f, 116f);

            var accent = player ? _playerAccent : _enemyAccent;

            // Retrato en el borde exterior: la cara queda fuera del camino de la barra.
            // La card coleccionable, la misma que se ve en el selector: el retrato
            // recortado era la pieza vieja y rompia la continuidad entre pantallas.
            var portrait = UIFactory.AddImage(plate, "Portrait", UIFactory.LoadSprite(def.cardResource), Color.white);
            portrait.preserveAspect = true;
            UIFactory.Place(portrait.rectTransform,
                new Vector2(player ? 0f : 1f, 0.5f), new Vector2(player ? 0f : 1f, 0.5f),
                new Vector2(player ? 0f : 1f, 0.5f),
                Vector2.zero, new Vector2(74f, 100f));

            var name = UIFactory.AddDisplayText(plate, "Name", def.displayName.ToUpperInvariant(), 27, KomTheme.TextHi,
                player ? TextAnchor.UpperLeft : TextAnchor.UpperRight);
            UIFactory.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(player ? 0f : 1f, 1f),
                new Vector2(player ? 86f : -86f, -2f), new Vector2(-200f, 34f));
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 15;
            name.resizeTextMaxSize = 27;
            if (player) _playerName = name; else _enemyName = name;

            var genre = UIFactory.AddText(plate, "Genre",
                (player ? "P1  ·  " : (versus ? "P2  ·  " : "CPU  ·  ")) + def.genre.ToUpperInvariant(), 11,
                new Color(accent.r, accent.g, accent.b), player ? TextAnchor.UpperLeft : TextAnchor.UpperRight, FontStyle.Bold);
            UIFactory.Place(genre.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(player ? 0f : 1f, 1f),
                new Vector2(player ? 88f : -88f, -36f), new Vector2(-200f, 16f));
            if (player) _playerGenre = genre; else _enemyGenre = genre;

            // Pips de ronda: uno por victoria necesaria. En versus al mejor de 5 hacen
            // falta TRES rombos; con la matriz fija de dos, el marcador mentia.
            int pipCount = Mathf.Max(1, roundsToWin);
            var pips = new Image[pipCount];
            for (int i = 0; i < pipCount; i++)
            {
                var pip = UIFactory.AddImage(plate, "Pip" + i, UIFactory.WhiteSprite(), new Color(0.18f, 0.19f, 0.19f, 1f));
                UIFactory.Place(pip.rectTransform, new Vector2(player ? 1f : 0f, 1f), new Vector2(player ? 1f : 0f, 1f),
                    new Vector2(player ? 1f : 0f, 1f),
                    new Vector2(player ? -6f - i * 20f : 6f + i * 20f, -8f), new Vector2(9f, 9f));
                pip.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                pips[i] = pip;
            }

            // Vida: la barra que manda. 14 px, con estela.
            var barBg = UIFactory.AddImage(plate, "BarBg", UIFactory.WhiteSprite(), new Color(0.10f, 0.105f, 0.11f, 1f));
            UIFactory.Place(barBg.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(player ? 0f : 1f, 1f),
                new Vector2(player ? 86f : -86f, -58f), new Vector2(-200f, 14f));

            var ghost = UIFactory.AddImage(barBg.transform, "Ghost", UIFactory.WhiteSprite(), new Color(0.86f, 0.86f, 0.86f, 0.55f));
            UIFactory.Stretch(ghost.rectTransform);
            ghost.type = Image.Type.Filled;
            ghost.fillMethod = Image.FillMethod.Horizontal;
            ghost.fillOrigin = player ? 0 : 1;
            ghost.fillAmount = 1f;

            var fill = UIFactory.AddImage(barBg.transform, "Fill", UIFactory.WhiteSprite(), KomTheme.TextHi);
            UIFactory.Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = player ? 0 : 1;
            fill.fillAmount = 1f;
            // El color se escribe directo (no pasa por UIFactory.Mono): es la única
            // excepción a la regla de grises, junto con la estamina, y está declarada.
            fill.color = RampColor(HealthRamp, 1f);

            // Luz de canto: una linea de 2 px sobre el borde superior de la parte LLENA.
            // Se probo un degradado vertical y lavaba el color (el blanco cubria media
            // barra y el verde se leia menta). La linea hace el mismo trabajo —dar volumen
            // de tubo— sin tocar el tono, y encaja con el lenguaje de filetes de 1 px del
            // resto de la interfaz.
            var bevel = UIFactory.AddImage(barBg.transform, "Bevel", UIFactory.WhiteSprite(),
                new Color(1f, 1f, 1f, 0.26f));
            bevel.rectTransform.anchorMin = new Vector2(0f, 1f);
            bevel.rectTransform.anchorMax = new Vector2(1f, 1f);
            bevel.rectTransform.pivot = new Vector2(0f, 1f);
            bevel.rectTransform.anchoredPosition = Vector2.zero;
            bevel.rectTransform.sizeDelta = new Vector2(0f, 2f);
            bevel.type = Image.Type.Filled;
            bevel.fillMethod = Image.FillMethod.Horizontal;
            bevel.fillOrigin = player ? 0 : 1;
            bevel.fillAmount = 1f;

            // Marcas de cuarto: ayudan a leer cuanto queda sin contar pixeles.
            for (int i = 1; i < 4; i++)
            {
                var tick = UIFactory.AddImage(barBg.transform, "Tick" + i, UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0.55f));
                UIFactory.Place(tick.rectTransform, new Vector2(i / 4f, 0f), new Vector2(i / 4f, 1f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(1f, 0f));
            }

            // SÚPER: la segunda y última barra. Ocupa el hilo que antes compartía con la
            // estamina, pero con cuerpo propio (6 px) porque ya no es un detalle: es la
            // única lectura además de la vida.
            //
            // La estamina (la que gasta bloquear) ya NO se dibuja. El estado sigue vivo en
            // `Fighter` y el aviso de guardia rota sale en el cartel; lo que se quitó es la
            // tercera barra.
            var superBg = UIFactory.AddImage(plate, "SuperBg", UIFactory.WhiteSprite(), new Color(0.075f, 0.08f, 0.085f, 1f));
            UIFactory.Place(superBg.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(player ? 0f : 1f, 1f),
                new Vector2(player ? 86f : -86f, -77f), new Vector2(-200f, 6f));
            var superFill = UIFactory.AddImage(superBg.transform, "SuperFill", UIFactory.WhiteSprite(), KomTheme.TextHi);
            UIFactory.Stretch(superFill.rectTransform);
            superFill.type = Image.Type.Filled;
            superFill.fillMethod = Image.FillMethod.Horizontal;
            superFill.fillOrigin = player ? 0 : 1;
            superFill.fillAmount = 0f;
            superFill.color = RampColor(SuperRamp, 0f);

            var superBevel = UIFactory.AddImage(superBg.transform, "SuperBevel", UIFactory.WhiteSprite(),
                new Color(1f, 1f, 1f, 0.22f));
            superBevel.rectTransform.anchorMin = new Vector2(0f, 1f);
            superBevel.rectTransform.anchorMax = new Vector2(1f, 1f);
            superBevel.rectTransform.pivot = new Vector2(0f, 1f);
            superBevel.rectTransform.anchoredPosition = Vector2.zero;
            superBevel.rectTransform.sizeDelta = new Vector2(0f, 1f);
            superBevel.type = Image.Type.Filled;
            superBevel.fillMethod = Image.FillMethod.Horizontal;
            superBevel.fillOrigin = player ? 0 : 1;
            superBevel.fillAmount = 0f;

            if (player)
            {
                _playerFill = fill;
                _playerGhost = ghost;
                _playerBevel = bevel;
                _playerSuper = superFill;
                _playerSuperBevel = superBevel;
                _playerPips = pips;
            }
            else
            {
                _enemyFill = fill;
                _enemyGhost = ghost;
                _enemyBevel = bevel;
                _enemySuper = superFill;
                _enemySuperBevel = superBevel;
                _enemyPips = pips;
            }
        }

        static string MatchFormatEs(int roundsToWin) =>
            roundsToWin <= 1 ? "A UNA" : $"AL MEJOR DE {roundsToWin * 2 - 1}";

        static string MatchFormatEn(int roundsToWin) =>
            roundsToWin <= 1 ? "SINGLE" : $"BEST OF {roundsToWin * 2 - 1}";

        void BuildTimerAndLabels(string stageName, int encounter, int total, bool versus = false, int roundsToWin = 2)
        {
            var plate = UIFactory.AddPanel(_root, "TimerPlate", new Color(0.043f, 0.047f, 0.051f, 0.94f), 3);
            UIFactory.Place(plate.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -28f), new Vector2(188f, 116f));
            UIFactory.AddFrame(plate.transform, "Border", KomTheme.Line, 1f);

            _roundLabel = UIFactory.AddLabel(plate.transform, "Round", KomLoc.T("RONDA 1", "ROUND 1"), TextAnchor.UpperCenter);
            UIFactory.Place(_roundLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), new Vector2(170f, 16f));

            _timer = UIFactory.AddDisplayText(plate.transform, "Timer", "60", 52, KomTheme.TextHi, TextAnchor.MiddleCenter);
            UIFactory.Place(_timer.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 2f), new Vector2(170f, 62f));

            string encounterText = versus
                ? KomLoc.T("VERSUS · " + MatchFormatEs(roundsToWin), "VERSUS · " + MatchFormatEn(roundsToWin))
                : KomLoc.T($"ENCUENTRO {encounter:00} / {total:00}", $"ENCOUNTER {encounter:00} / {total:00}");
            _encounterLabel = UIFactory.AddText(plate.transform, "Encounter", encounterText, 9,
                KomTheme.TextLo, TextAnchor.LowerCenter, FontStyle.Bold);
            UIFactory.Place(_encounterLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 10f), new Vector2(170f, 14f));

            _stageLabel = UIFactory.AddText(_root, "StageLabel", stageName.ToUpperInvariant(), 10, KomTheme.TextLo,
                TextAnchor.LowerLeft, FontStyle.Bold);
            UIFactory.Place(_stageLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(46f, 40f), new Vector2(500f, 16f));
        }

        void BuildBanner()
        {
            _banner = UIFactory.Rect(_root, "Banner").gameObject;
            var rt = (RectTransform)_banner.transform;
            rt.anchorMin = new Vector2(0.5f, 0.62f);
            rt.anchorMax = new Vector2(0.5f, 0.62f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1200f, 220f);
            _bannerGroup = _banner.AddComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f;
            _bannerGroup.blocksRaycasts = false;

            _bannerMain = UIFactory.AddDisplayText(_banner.transform, "Main", "", 76, KomTheme.TextHi, TextAnchor.MiddleCenter);
            UIFactory.Place(_bannerMain.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 30f), new Vector2(1140f, 100f));

            _bannerRule = UIFactory.AddImage(_banner.transform, "Rule", UIFactory.WhiteSprite(), KomTheme.Accent);
            UIFactory.Place(_bannerRule.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -24f), new Vector2(120f, 2f));

            _bannerSub = UIFactory.AddText(_banner.transform, "Sub", "", 19, KomTheme.TextMid, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_bannerSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -52f), new Vector2(1000f, 30f));
        }

        /// <summary>
        /// Leyenda de teclas compacta. Se apaga sola: informar no puede costar tapar la pelea.
        /// </summary>
        void BuildLegend()
        {
            var bar = UIFactory.Rect(_root, "Legend");
            bar.anchorMin = new Vector2(0.5f, 0f);
            bar.anchorMax = new Vector2(0.5f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(1720f, _versus ? 66f : 46f);
            bar.anchoredPosition = new Vector2(0f, _versus ? 24f : 14f);
            _legendGroup = bar.gameObject.AddComponent<CanvasGroup>();
            _legendGroup.alpha = 1f;

            if (_versus)
            {
                // Dos lineas, una por jugador: en una sola no caben dieciseis teclas y
                // el que juega en el teclado comunitario no deberia adivinar la mitad.
                string p1 = KomLoc.T(
                    "<b>J1</b>   A/D mover    <b>W</b> saltar    <b>S</b> agachar    <b>SHIFT</b> o <b>L</b> bloquear    " +
                    "<b>C</b>/<b>V</b> puños    <b>B</b>/<b>N</b> patadas    <b>M</b> súper",
                    "<b>P1</b>   A/D move    <b>W</b> jump    <b>S</b> crouch    <b>SHIFT</b> or <b>L</b> block    " +
                    "<b>C</b>/<b>V</b> punches    <b>B</b>/<b>N</b> kicks    <b>M</b> super");
                string p2 = KomLoc.T(
                    "<b>J2</b>   ←/→ mover    <b>↑</b> saltar    <b>↓</b> agachar    <b>SHIFT DER</b> bloquear    " +
                    "<b>U</b>/<b>I</b> puños    <b>O</b>/<b>P</b> patadas    <b>Y</b> súper    ·    <b>R3</b> controles    <b>ESC</b> pausa",
                    "<b>P2</b>   ←/→ move    <b>↑</b> jump    <b>↓</b> crouch    <b>RIGHT SHIFT</b> block    " +
                    "<b>U</b>/<b>I</b> punches    <b>O</b>/<b>P</b> kicks    <b>Y</b> super    ·    <b>R3</b> controls    <b>ESC</b> pause");
                var line1 = UIFactory.AddText(bar, "KeysP1", p1, 12, KomTheme.TextMid,
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Place(line1.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 30f), new Vector2(1700f, 18f));
                var line2 = UIFactory.AddText(bar, "KeysP2", p2, 12, KomTheme.TextMid,
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Place(line2.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 8f), new Vector2(1700f, 18f));
                _legendCombos = null;
                return;
            }

            // Una sola linea: las teclas en valor alto y el verbo en gris medio. Con
            // keycaps individuales no caben diez acciones y el jugador lee peor.
            string keys = KomLoc.T(
                "<b>A/D</b> mover    <b>W</b> saltar    <b>S</b> agachar    <b>SHIFT</b> o <b>L</b> bloquear    " +
                "<b>DOBLE A/D</b> correr    <b>G</b> puño rápido    <b>H</b> puño fuerte    " +
                "<b>J</b> patada rápida    <b>K</b> patada fuerte    <b>T</b> súper    " +
                "<b>F1</b> controles    <b>ESC</b> pausa",
                "<b>A/D</b> move    <b>W</b> jump    <b>S</b> crouch    <b>SHIFT</b> or <b>L</b> block    " +
                "<b>DOUBLE A/D</b> run    <b>G</b> light punch    <b>H</b> heavy punch    " +
                "<b>J</b> light kick    <b>K</b> heavy kick    <b>T</b> super    " +
                "<b>F1</b> controls    <b>ESC</b> pause");
            var line = UIFactory.AddText(bar, "Keys", keys, 13, KomTheme.TextMid, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 2f), new Vector2(1700f, 20f));

            // Segunda linea SOLO en el primer escenario: los combos son lo que no se
            // adivina jugando. En el resto de peleas estorbaria.
            var combos = UIFactory.AddText(bar, "Combos",
                "COMBOS   <b>G G G</b>  ·  <b>H H H</b>  ·  <b>J J J J</b>", 12,
                KomTheme.AccentDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(combos.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -2f), new Vector2(1200f, 18f));
            _legendCombos = combos.gameObject;
            _legendCombos.SetActive(false);
        }

        /// <summary>
        /// Primer escenario: la leyenda se queda fija y suma la linea de combos.
        /// </summary>
        public void SetLegendPersistent(bool on)
        {
            _legendPersistent = on;
            if (_legendCombos != null) _legendCombos.SetActive(on);
            if (on && _legendGroup != null) _legendGroup.alpha = 1f;
        }

        void BuildPauseOverlay()
        {
            _pauseOverlay = UIFactory.Rect(_root, "PauseOverlay").gameObject;
            UIFactory.Stretch((RectTransform)_pauseOverlay.transform);
            var dim = UIFactory.AddImage(_pauseOverlay.transform, "Dim", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0.78f));
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            var panel = UIFactory.AddPanel(_pauseOverlay.transform, "Panel", new Color(0.043f, 0.047f, 0.051f, 0.99f), 3);
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(560f, 440f));
            UIFactory.AddFrame(panel.transform, "Border", KomTheme.Line, 1f);

            var kicker = UIFactory.AddLabel(panel.transform, "Kicker", KomLoc.T("Combate en pausa", "Combat paused"), TextAnchor.UpperCenter);
            UIFactory.Place(kicker.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -40f), new Vector2(480f, 18f));

            var title = UIFactory.AddDisplayText(panel.transform, "Title", KomLoc.T("PAUSA", "PAUSE"), 56, KomTheme.TextHi, TextAnchor.UpperCenter);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -66f), new Vector2(480f, 66f));

            var sub = UIFactory.AddText(panel.transform, "Sub",
                _versus
                    ? KomLoc.T("La pelea puede esperar.", "The fight can wait.")
                    : KomLoc.T("El torneo puede esperar.", "The tournament can wait."),
                KomTheme.Small, KomTheme.TextMid, TextAnchor.UpperCenter);
            UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -136f), new Vector2(480f, 24f));

            // Los combos viven aqui y no en el HUD: en la pelea tapan y aqui se leen.
            var combos = UIFactory.AddText(panel.transform, "Combos",
                _versus
                    ? "COMBOS   J1 C+C+C · V+V+V · B+B+B+B      J2 U+U+U · I+I+I · O+O+O+O"
                    : "COMBOS   G+G+G   ·   H+H+H   ·   J+J+J+J", 10,
                KomTheme.TextLo, TextAnchor.UpperCenter, FontStyle.Bold);
            UIFactory.Place(combos.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -178f), new Vector2(520f, 18f));

            var resume = UIFactory.AddPrimaryButton(panel.transform, "Resume", KomLoc.T("REANUDAR COMBATE", "RESUME FIGHT"),
                () => _resumeAction?.Invoke(), 21);
            UIFactory.Place(resume.image.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 104f), new Vector2(380f, 58f));

            _pauseMapButton = UIFactory.AddGhostButton(panel.transform, "WorldMap",
                KomLoc.T("VER MAPA DEL MUNDO", "VIEW WORLD MAP"),
                () => _mapAction?.Invoke(), 18);
            UIFactory.Place(_pauseMapButton.image.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(380f, 50f));
            // El boton solo tiene sentido en campaña: en versus el mapa es la pantalla
            // anterior y no hay ruta de torneo que consultar.
            _pauseMapButton.gameObject.SetActive(!_versus);

            var menu = UIFactory.AddGhostButton(panel.transform, "Menu", KomLoc.T("SALIR AL MENÚ", "QUIT TO MENU"),
                () => _menuAction?.Invoke(), 19);
            UIFactory.Place(menu.image.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 40f), new Vector2(380f, 50f));

            var hint = UIFactory.AddText(panel.transform, "Hint", KomLoc.T("ESC  ·  REANUDAR", "ESC  ·  RESUME"), 10, KomTheme.TextLo,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 16f), new Vector2(300f, 16f));

            _pauseOverlay.SetActive(false);
        }

        // ------------------------------------------------------------------ controles
        // Una sola pantalla de referencia: sirve de TUTORIAL en el primer escenario
        // (con boton CONTINUAR) y de consulta en cualquier momento (F1 / SELECT).
        void BuildControlsOverlay()
        {
            _controlsOverlay = UIFactory.Rect(_root, "ControlsOverlay").gameObject;
            var overlayRt = (RectTransform)_controlsOverlay.transform;
            UIFactory.Stretch(overlayRt);

            var dim = UIFactory.AddImage(overlayRt, "Dim", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0.86f));
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            var panel = UIFactory.AddPanel(overlayRt, "Panel", new Color(0.031f, 0.035f, 0.039f, 0.99f), 4);
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1040f, 600f));
            UIFactory.AddFrame(panel.transform, "Border", KomTheme.Line, 1f);

            var title = UIFactory.AddDisplayText(panel.transform, "Title", KomLoc.T("CONTROLES", "CONTROLS"), 50, KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(48f, -50f), new Vector2(600f, 64f));

            var rule = UIFactory.AddHLine(panel.transform, "Rule", KomTheme.Line, 1f);
            UIFactory.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -146f), new Vector2(-104f, 1f));

            if (_versus)
            {
                BuildControlsColumn(panel.transform, 52f, -176f, KomLoc.T("JUGADOR 1", "PLAYER 1"),
                    new[]
                    {
                        ("A / D", KomLoc.T("Caminar", "Walk")),
                        ("W", KomLoc.T("Saltar", "Jump")),
                        ("S", KomLoc.T("Agacharse", "Crouch")),
                        (KomLoc.T("SHIFT o L", "SHIFT or L"), KomLoc.T("Bloquear", "Block")),
                        (KomLoc.T("DOBLE A / D", "DOUBLE A / D"), KomLoc.T("Correr", "Run")),
                        ("C / V", KomLoc.T("Puños", "Punches")),
                        ("B / N", KomLoc.T("Patadas", "Kicks")),
                        ("M", KomLoc.T("Poder especial", "Special power")),
                    });

                BuildControlsColumn(panel.transform, 392f, -176f, KomLoc.T("JUGADOR 2", "PLAYER 2"),
                    new[]
                    {
                        ("← / →", KomLoc.T("Caminar", "Walk")),
                        ("↑", KomLoc.T("Saltar", "Jump")),
                        ("↓", KomLoc.T("Agacharse", "Crouch")),
                        (KomLoc.T("SHIFT DER", "RIGHT SHIFT"), KomLoc.T("Bloquear", "Block")),
                        (KomLoc.T("DOBLE ← / →", "DOUBLE ← / →"), KomLoc.T("Correr", "Run")),
                        ("U / I", KomLoc.T("Puños", "Punches")),
                        ("O / P", KomLoc.T("Patadas", "Kicks")),
                        ("Y", KomLoc.T("Poder especial", "Special power")),
                    });

                BuildControlsColumn(panel.transform, 732f, -176f, KomLoc.T("SISTEMA Y COMBOS", "SYSTEM & COMBOS"),
                    new[]
                    {
                        ("C C C", KomLoc.T("Combo 1 · J1", "Combo 1 · P1")),
                        ("V V V", KomLoc.T("Combo 2 · J1", "Combo 2 · P1")),
                        ("B B B B", KomLoc.T("Combo 3 · J1", "Combo 3 · P1")),
                        ("J2", "U U U · I I I · O O O O"),
                        ("R3 / F1", KomLoc.T("Esta pantalla", "This screen")),
                        ("ESC / START", KomLoc.T("Pausa", "Pause")),
                    });
            }
            else
            {
                BuildControlsColumn(panel.transform, 52f, -176f, KomLoc.T("MOVIMIENTO", "MOVEMENT"),
                    new[]
                    {
                        ("A / D", KomLoc.T("Caminar", "Walk")),
                        ("W", KomLoc.T("Saltar", "Jump")),
                        ("S", KomLoc.T("Agacharse", "Crouch")),
                        (KomLoc.T("SHIFT o L", "SHIFT or L"), KomLoc.T("Bloquear", "Block")),
                        (KomLoc.T("DOBLE A / D", "DOUBLE A / D"), KomLoc.T("Correr", "Run")),
                    });

                BuildControlsColumn(panel.transform, 392f, -176f, KomLoc.T("ATAQUES", "ATTACKS"),
                    new[]
                    {
                        ("G", KomLoc.T("Golpe rápido", "Light punch")),
                        ("H", KomLoc.T("Golpe fuerte", "Heavy punch")),
                        ("J", KomLoc.T("Patada rápida", "Light kick")),
                        ("K", KomLoc.T("Patada fuerte", "Heavy kick")),
                        (KomLoc.T("CORRER + K", "RUN + K"), KomLoc.T("Patada voladora", "Flying kick")),
                        ("U", KomLoc.T("Poder especial", "Special power")),
                    });

                BuildControlsColumn(panel.transform, 732f, -176f, KomLoc.T("COMBOS", "COMBOS"),
                    new[]
                    {
                        ("G G G", KomLoc.T("Combo 1 - 4 golpes", "Combo 1 - 4 hits")),
                        ("H H H", KomLoc.T("Combo 2 - 5 golpes", "Combo 2 - 5 hits")),
                        ("J J J J", KomLoc.T("Combo 3 - 7 golpes", "Combo 3 - 7 hits")),
                        ("F1 / R3", KomLoc.T("Esta pantalla", "This screen")),
                        ("ESC", KomLoc.T("Pausa", "Pause")),
                    });
            }

            _controlsCloseButton = UIFactory.AddPrimaryButton(panel.transform, "Close", KomLoc.T("CONTINUAR", "CONTINUE"),
                () => { var action = _controlsClose; _controlsClose = null; action?.Invoke(); }, 20, 380f, 56f);
            UIFactory.Place(_controlsCloseButton.image.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(380f, 56f));

            _controlsOverlay.SetActive(false);
        }

        void BuildControlsColumn(Transform parent, float x, float y, string header, (string key, string text)[] items)
        {
            var col = UIFactory.Rect(parent, "Col_" + header);
            col.anchorMin = new Vector2(0f, 1f);
            col.anchorMax = new Vector2(0f, 1f);
            col.pivot = new Vector2(0f, 1f);
            col.sizeDelta = new Vector2(320f, 250f);
            col.anchoredPosition = new Vector2(x, y);

            var head = UIFactory.AddLabel(col, "Head", header, TextAnchor.UpperLeft);
            UIFactory.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(300f, 18f));

            var underline = UIFactory.AddHLine(col, "Underline", KomTheme.AccentDim, 1f);
            UIFactory.Place(underline.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -24f), new Vector2(34f, 1f));

            float rowY = -50f;
            foreach (var (key, text) in items)
            {
                float keyW = Mathf.Max(38f, 16f + key.Length * 9f);
                var cap = UIFactory.AddKeycap(col, "Key_" + key, key, keyW);
                UIFactory.Place(cap, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, rowY), new Vector2(keyW, 24f));

                var label = UIFactory.AddText(col, "Text_" + key, text, 14, KomTheme.TextMid, TextAnchor.MiddleLeft);
                UIFactory.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(keyW + 12f, rowY + 1f), new Vector2(300f - keyW - 12f, 24f));
                rowY -= 34f;
            }
        }

        /// <summary>
        /// Muestra u oculta la referencia de controles. `buttonLabel` cambia el texto del
        /// boton (CONTINUAR en el tutorial, CERRAR en la consulta) y `onClose` se llama al
        /// pulsarlo: el tutorial lo usa para arrancar la pelea.
        /// </summary>
        public void ShowControls(bool show, string buttonLabel = null, System.Action onClose = null)
        {
            if (_controlsOverlay == null) return;
            if (show)
            {
                _controlsClose = onClose;
                if (_controlsCloseButton != null && !string.IsNullOrEmpty(buttonLabel))
                {
                    var label = _controlsCloseButton.GetComponentInChildren<Text>();
                    if (label != null) label.text = buttonLabel;
                }
                _controlsOverlay.SetActive(true);
                _controlsOverlay.transform.SetAsLastSibling();
            }
            else
            {
                _controlsOverlay.SetActive(false);
                _controlsClose = null;
            }
        }

        void BuildResultOverlay()
        {
            _resultOverlay = UIFactory.Rect(_root, "ResultOverlay").gameObject;
            UIFactory.Stretch((RectTransform)_resultOverlay.transform);
            var dim = UIFactory.AddImage(_resultOverlay.transform, "Dim", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0.86f));
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            var panel = UIFactory.AddPanel(_resultOverlay.transform, "Panel", new Color(0.043f, 0.047f, 0.051f, 0.99f), 3);
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1200f, 620f));
            UIFactory.AddFrame(panel.transform, "Border", KomTheme.Line, 1f);

            var kicker = UIFactory.AddLabel(panel.transform, "Kicker", KomLoc.T("Resultado del encuentro", "Encounter result"), TextAnchor.UpperCenter);
            UIFactory.Place(kicker.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -42f), new Vector2(800f, 18f));
            _resultKicker = kicker;

            _resultTitle = UIFactory.AddDisplayText(panel.transform, "Title", KomLoc.T("VICTORIA", "VICTORY"), 60, KomTheme.TextHi, TextAnchor.UpperCenter);
            UIFactory.Place(_resultTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -64f), new Vector2(880f, 70f));

            _resultSub = UIFactory.AddText(panel.transform, "Sub", "", 15, KomTheme.Accent, TextAnchor.UpperCenter, FontStyle.Bold);
            UIFactory.Place(_resultSub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -136f), new Vector2(800f, 20f));

            _resultPlayerCard = UIFactory.AddImage(panel.transform, "CardP", null, Color.white);
            _resultPlayerCard.preserveAspect = true;
            UIFactory.Place(_resultPlayerCard.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(46f, -18f), new Vector2(232f, 310f));

            _resultEnemyCard = UIFactory.AddImage(panel.transform, "CardE", null, new Color(0.62f, 0.62f, 0.62f, 1f));
            _resultEnemyCard.preserveAspect = true;
            UIFactory.Place(_resultEnemyCard.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-46f, -18f), new Vector2(232f, 310f));

            // Aura de las dos tarjetas del resultado: el color del personaje enmarca la
            // ficha, y el ganador la lleva a plena intensidad mientras el perdedor se queda
            // en el contorno. El color se fija en ShowResult, cuando se sabe quién ganó.
            _resultPlayerAura = UIFactory.AddCardAura(_resultPlayerCard, KomTheme.Accent, 1f);
            _resultEnemyAura = UIFactory.AddCardAura(_resultEnemyCard, KomTheme.Accent, 1f);

            _resultBody = UIFactory.AddText(panel.transform, "Body", "", 19, KomTheme.TextMid, TextAnchor.MiddleCenter);
            UIFactory.Place(_resultBody.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 10f), new Vector2(520f, 170f));
            _resultBody.lineSpacing = 1.15f;

            _primaryButton = UIFactory.AddPrimaryButton(panel.transform, "Primary", KomLoc.T("CONTINUAR", "CONTINUE"),
                () => _primaryAction?.Invoke(), 22);
            UIFactory.Place(_primaryButton.image.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 86f), new Vector2(380f, 58f));
            _primaryLabel = _primaryButton.GetComponentInChildren<Text>();

            _secondaryButton = UIFactory.AddGhostButton(panel.transform, "Secondary", KomLoc.T("MENÚ", "MENU"),
                () => _secondaryAction?.Invoke(), 18);
            UIFactory.Place(_secondaryButton.image.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 26f), new Vector2(240f, 46f));
            _secondaryLabel = _secondaryButton.GetComponentInChildren<Text>();

            var hint = UIFactory.AddText(panel.transform, "Hint",
                KomLoc.T("ENTER  ·  CONTINUAR        ESC  ·  MENÚ", "ENTER  ·  CONTINUE        ESC  ·  MENU"), 10,
                KomTheme.TextLo, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 8f), new Vector2(700f, 14f));
            _resultHint = hint;

            _resultOverlay.SetActive(false);
        }

        // ------------------------------------------------------------------ mapa de campaña

        /// <summary>
        /// Vista del mapa del torneo desde la pausa. No es la pantalla de viaje: es una
        /// consulta. La escena de combate sigue viva debajo, congelada, y al cerrar se
        /// vuelve a la misma ronda sin resetear luchadores ni reloj.
        /// </summary>
        void BuildWorldMapOverlay()
        {
            _worldMapOverlay = UIFactory.Rect(_root, "WorldMapOverlay").gameObject;
            var overlay = (RectTransform)_worldMapOverlay.transform;
            UIFactory.Stretch(overlay);

            var dim = UIFactory.AddImage(overlay, "Dim", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0.94f));
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            var map = UIFactory.AddImage(overlay, "MapImage",
                UIFactory.LoadSprite("UI/Map/tournament_map"), new Color(0.9f, 0.9f, 0.9f, 1f));
            UIFactory.Stretch(map.rectTransform);

            var tint = UIFactory.AddImage(overlay, "Tint", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0.30f));
            UIFactory.Stretch(tint.rectTransform);

            UIFactory.AddVignette(overlay, 0.76f);

            var top = UIFactory.AddImage(overlay, "TopShade",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0.94f), new Color(0f, 0f, 0f, 0f)), Color.white);
            UIFactory.Place(top.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 230f));

            var bottom = UIFactory.AddImage(overlay, "BottomShade",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.96f)), Color.white);
            UIFactory.Place(bottom.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0f, 400f));

            UIFactory.AddGrain(overlay, 0.02f);

            var kicker = UIFactory.AddText(overlay, "Kicker",
                KomLoc.T("CONSULTA · RUTA DEL TORNEO", "VIEW · TOURNAMENT ROUTE"), 13, KomTheme.Accent,
                TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(KomTheme.Margin, -42f), new Vector2(760f, 18f));

            var title = UIFactory.AddDisplayText(overlay, "Title",
                KomLoc.T("MAPA DEL MUNDO", "WORLD MAP"), 44, KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(KomTheme.Margin - 4f, -62f), new Vector2(980f, 58f));

            var session = GameSession.Instance;
            var nodes = KomData.Tournament.nodes;
            int current = session != null && nodes.Length > 0
                ? Mathf.Clamp(session.EncounterIndex, 0, nodes.Length - 1)
                : 0;

            var counter = UIFactory.AddText(overlay, "Counter",
                nodes.Length > 0
                    ? KomLoc.T($"ENCUENTRO {current + 1:00} / {nodes.Length:00}",
                               $"ENCOUNTER {current + 1:00} / {nodes.Length:00}")
                    : "", 15, KomTheme.TextMid, TextAnchor.UpperRight, FontStyle.Bold);
            UIFactory.Place(counter.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-KomTheme.Margin, -46f), new Vector2(420f, 20f));

            var rule = UIFactory.AddHLine(overlay, "HeaderRule", KomTheme.Line, 1f);
            UIFactory.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -134f), new Vector2(-2f * KomTheme.Margin, 1f));

            // Pines de los nodos. El del JSON apunta ~16 px por encima del circulo
            // grabado en el mapa, igual que en la pantalla de viaje.
            var pinLayer = UIFactory.Rect(overlay, "MapPins");
            UIFactory.Stretch(pinLayer);
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null) continue;
                var nodeStage = KomData.GetStage(node.stageId);

                // La final comparte el recinto del Zocalo: se dibuja UN solo pin por
                // recinto y se marca con FINAL. Dos anillos superpuestos no informan.
                bool duplicate = false;
                for (int j = 0; j < i; j++)
                {
                    if (MapController.SameVenue(nodeStage, KomData.GetStage(nodes[j].stageId)))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate) continue;

                bool now = false;
                bool done = true;
                bool finalVenue = false;
                for (int j = i; j < nodes.Length; j++)
                {
                    if (!MapController.SameVenue(nodeStage, KomData.GetStage(nodes[j].stageId))) continue;
                    if (j == current) now = true;
                    if (j > current) done = false;
                    if (j == nodes.Length - 1) finalVenue = true;
                }
                if (now) done = false;

                var pin = UIFactory.Rect(pinLayer, "MapPin" + i);
                pin.anchorMin = pin.anchorMax = new Vector2(node.mapX, node.mapY);
                pin.pivot = new Vector2(0.5f, 0.5f);
                pin.anchoredPosition = Vector2.zero;
                pin.sizeDelta = new Vector2(190f, 120f);

                var glow = UIFactory.AddGlow(pin, "Glow", new Color(1f, 1f, 1f, now ? 0.22f : 0.03f));
                UIFactory.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, -16f), new Vector2(120f, 120f));

                var ring = UIFactory.AddImage(pin, "Ring", UIFactory.RingSprite(128),
                    now ? KomTheme.AccentHot : done ? new Color(0.55f, 0.56f, 0.55f, 0.72f)
                                                    : new Color(0.48f, 0.49f, 0.48f, 0.45f));
                UIFactory.Place(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, -16f), new Vector2(58f, 58f));

                var core = UIFactory.AddImage(pin, "Core", UIFactory.WhiteSprite(),
                    now ? KomTheme.AccentHot : new Color(0.62f, 0.63f, 0.62f, 0.75f));
                UIFactory.Place(core.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, -16f), new Vector2(8f, 8f));
                core.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

                var numberPlate = UIFactory.AddPanel(pin, "Number", new Color(0f, 0f, 0f, 0.82f), 2);
                UIFactory.Place(numberPlate.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, 34f), new Vector2(42f, 20f));
                var number = UIFactory.AddText(numberPlate.transform, "Label", (i + 1).ToString("00"), 10,
                    now ? KomTheme.TextHi : KomTheme.TextLo, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(number.rectTransform, 4f, 1f, 4f, 1f);

                if (finalVenue)
                {
                    var finalPlate = UIFactory.AddPanel(pin, "Final", new Color(0f, 0f, 0f, 0.86f), 2);
                    UIFactory.Place(finalPlate.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f), new Vector2(0f, 54f), new Vector2(56f, 16f));
                    var finalLabel = UIFactory.AddText(finalPlate.transform, "Label", "FINAL", 9,
                        KomTheme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UIFactory.Stretch(finalLabel.rectTransform, 2f, 1f, 2f, 1f);
                }

                if (now)
                {
                    var nowPlate = UIFactory.AddPanel(pin, "Now", KomTheme.AccentHot, 2);
                    UIFactory.Place(nowPlate.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f), new Vector2(0f, -58f), new Vector2(72f, 18f));
                    var nowLabel = UIFactory.AddText(nowPlate.transform, "Label",
                        KomLoc.T("AHORA", "NOW"), 9, KomTheme.TextOnLight, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UIFactory.Stretch(nowLabel.rectTransform, 2f, 1f, 2f, 1f);
                }
            }

            // Ficha inferior: jugador, encuentro actual y boton de vuelta.
            var info = UIFactory.AddPanel(overlay, "Info", new Color(0.043f, 0.047f, 0.051f, 0.975f), 3);
            UIFactory.Place(info.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 34f), new Vector2(1500f, 150f));
            UIFactory.AddFrame(info.transform, "Border", KomTheme.Line, 1f);

            var player = session != null ? session.PlayerDef : null;
            var playerCard = UIFactory.AddImage(info.transform, "PlayerCard",
                UIFactory.LoadSprite(player != null ? player.cardResource : null), Color.white);
            playerCard.preserveAspect = true;
            UIFactory.Place(playerCard.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(28f, 32f), new Vector2(64f, 86f));
            UIFactory.AddCardAura(playerCard, KomData.Accent(player), 1f);

            var playerName = UIFactory.AddDisplayText(info.transform, "PlayerName",
                player != null ? player.displayName.ToUpperInvariant() : "P1", 22, KomTheme.TextHi,
                TextAnchor.MiddleLeft);
            UIFactory.Place(playerName.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(108f, 88f), new Vector2(300f, 28f));

            var playerGenre = UIFactory.AddText(info.transform, "PlayerGenre",
                player != null ? player.genre.ToUpperInvariant() : "", 10, KomTheme.Accent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(playerGenre.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(110f, 64f), new Vector2(300f, 16f));

            var currentNodeDef = nodes.Length > 0 ? nodes[current] : null;
            var stage = currentNodeDef != null ? KomData.GetStage(currentNodeDef.stageId) : null;
            var currentKicker = UIFactory.AddText(info.transform, "CurrentKicker",
                KomLoc.T("ENCUENTRO ACTUAL", "CURRENT ENCOUNTER"), 10, KomTheme.Accent,
                TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(currentKicker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(440f, -16f), new Vector2(500f, 16f));

            var currentNode = UIFactory.AddDisplayText(info.transform, "CurrentNode",
                currentNodeDef != null ? currentNodeDef.label.ToUpperInvariant() : "—", 28,
                KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(currentNode.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(436f, -36f), new Vector2(660f, 32f));
            currentNode.resizeTextForBestFit = true;
            currentNode.resizeTextMinSize = 16;
            currentNode.resizeTextMaxSize = 28;

            var currentStage = UIFactory.AddText(info.transform, "CurrentStage",
                stage != null
                    ? stage.displayName + "  ·  " + stage.subtitle
                    : KomLoc.T("Torneo del Bienestar", "Wellbeing Tournament"),
                13, KomTheme.TextMid, TextAnchor.UpperLeft);
            UIFactory.Place(currentStage.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(440f, -72f), new Vector2(660f, 20f));

            var back = UIFactory.AddPrimaryButton(info.transform, "Back",
                KomLoc.T("VOLVER A LA PELEA  ·  ESC", "BACK TO THE FIGHT  ·  ESC"), CloseWorldMap, 19, 340f, 52f);
            UIFactory.Place(back.image.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-28f, 48f), new Vector2(340f, 52f));

            _worldMapOverlay.SetActive(false);
        }

        void BuildHitFlash()
        {
            _hitFlash = UIFactory.AddImage(_root, "HitFlash", UIFactory.WhiteSprite(), new Color(1f, 1f, 1f, 0f));
            UIFactory.Stretch(_hitFlash.rectTransform);
            _hitFlash.raycastTarget = false;
        }

        public void SetStageName(string stageName)
        {
            if (_stageLabel != null) _stageLabel.text = stageName.ToUpperInvariant();
        }

        public void SetHealth(float player01, float enemy01, bool instant = false)
        {
            _playerTarget = Mathf.Clamp01(player01);
            _enemyTarget = Mathf.Clamp01(enemy01);
            if (instant)
            {
                _playerShown = _playerTarget;
                _enemyShown = _enemyTarget;
                _playerGhostShown = _playerTarget;
                _enemyGhostShown = _enemyTarget;
            }
            if (_playerFill != null) _playerFill.fillAmount = _playerShown;
            if (_enemyFill != null) _enemyFill.fillAmount = _enemyShown;
            if (_playerBevel != null) _playerBevel.fillAmount = _playerShown;
            if (_enemyBevel != null) _enemyBevel.fillAmount = _enemyShown;
            if (_playerGhost != null) _playerGhost.fillAmount = _playerGhostShown;
            if (_enemyGhost != null) _enemyGhost.fillAmount = _enemyGhostShown;
            ApplyBarColors();
        }

        /// <summary>
        /// Color de las dos barras. Se recalcula cada frame desde la fracción MOSTRADA (no
        /// la objetivo): así el tono viaja con el drenaje y no salta de golpe cuando entra
        /// el daño.
        ///
        /// El pulso de criticidad de la vida es lento (0.8 Hz) y de amplitud baja (+12 %):
        /// avisa sin convertirse en una alarma. El súper tiene su propio aviso, que no es
        /// un pulso sino el brillo de la rampa al llenarse.
        /// </summary>
        void ApplyBarColors()
        {
            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * 0.8f);

            if (_playerFill != null) _playerFill.color = BarColor(HealthRamp, _playerShown, pulse);
            if (_enemyFill != null) _enemyFill.color = BarColor(HealthRamp, _enemyShown, pulse);
            if (_playerSuper != null) _playerSuper.color = SuperBarColor(_playerSuperShown);
            if (_enemySuper != null) _enemySuper.color = SuperBarColor(_enemySuperShown);

            // La estela de daño toma el tono de la barra: lo que se acaba de perder se lee
            // como parte de la misma barra, no como un segundo elemento blanco.
            if (_playerGhost != null) _playerGhost.color = GhostColor(HealthRamp, _playerShown);
            if (_enemyGhost != null) _enemyGhost.color = GhostColor(HealthRamp, _enemyShown);
        }

        static Color BarColor((float at, Color c)[] ramp, float fraction, float pulse)
        {
            var c = RampColor(ramp, fraction);
            if (fraction > CriticalFraction) return c;
            // Solo en crítico, y hacia el blanco: subir el valor mantiene el tono.
            float k = Mathf.InverseLerp(CriticalFraction, 0f, fraction);
            return Color.Lerp(c, Color.Lerp(c, Color.white, 0.35f), k) * pulse;
        }

        static Color GhostColor((float at, Color c)[] ramp, float fraction)
        {
            var c = RampColor(ramp, fraction);
            return new Color(c.r * 0.55f + 0.35f, c.g * 0.55f + 0.35f, c.b * 0.55f + 0.35f, 0.5f);
        }

        /// <summary>
        /// Interpola una rampa de paradas ordenadas de mayor a menor fracción. El suavizado
        /// es por tramo: sin él, cada parada se ve como un escalón de color.
        /// </summary>
        static Color RampColor((float at, Color c)[] ramp, float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            if (fraction >= ramp[0].at) return ramp[0].c;
            for (int i = 1; i < ramp.Length; i++)
            {
                if (fraction < ramp[i].at) continue;
                float span = Mathf.Max(0.0001f, ramp[i - 1].at - ramp[i].at);
                float t = Mathf.SmoothStep(0f, 1f, (fraction - ramp[i].at) / span);
                return Color.Lerp(ramp[i].c, ramp[i - 1].c, t);
            }
            return ramp[ramp.Length - 1].c;
        }

        public void SetSuper(float player01, float enemy01, bool instant = false)
        {
            float p = Mathf.Clamp01(player01);
            float e = Mathf.Clamp01(enemy01);
            _playerSuperTarget = p;
            _enemySuperTarget = e;
            if (instant)
            {
                _playerSuperShown = p;
                _enemySuperShown = e;
            }
            if (_playerSuper != null) _playerSuper.fillAmount = _playerSuperShown;
            if (_enemySuper != null) _enemySuper.fillAmount = _enemySuperShown;
        }

        public void SetRound(int round, int playerWins, int enemyWins)
        {
            if (_roundLabel != null) _roundLabel.text = KomLoc.T($"RONDA {round}", $"ROUND {round}");
            int pipCount = Mathf.Max(1, _roundsToWin);
            for (int i = 0; i < pipCount; i++)
            {
                if (_playerPips != null && i < _playerPips.Length)
                    _playerPips[i].color = i < playerWins ? KomTheme.AccentHot : new Color(0.18f, 0.19f, 0.19f, 1f);
                if (_enemyPips != null && i < _enemyPips.Length)
                    _enemyPips[i].color = i < enemyWins ? KomTheme.AccentHot : new Color(0.18f, 0.19f, 0.19f, 1f);
            }
        }

        public void SetTimer(float seconds)
        {
            _timeValue = Mathf.Max(0f, seconds);
            _timeLow = _timeValue <= 10f;
            if (_timer != null)
            {
                _timer.text = Mathf.CeilToInt(_timeValue).ToString("00");
                _timer.color = _timeLow ? new Color(1f, 1f, 1f, 1f) : KomTheme.TextHi;
            }
        }

        // ------------------------------------------------------------------
        // Contador de golpes encadenados
        // ------------------------------------------------------------------
        // Feedback de recompensa: en un juego de pelea, saber que llevas 6 golpes
        // seguidos es la mitad del placer de encadenarlos. Va en la tipografia
        // editorial del resto de la interfaz (Didone, filete fino) y en B/N, porque
        // el color pertenece al VFX.
        void BuildComboCounter()
        {
            var root = UIFactory.Rect(_root, "ComboCounter");
            // Abajo a la izquierda, dentro de la banda oscura inferior: el contador
            // es blanco y en los escenarios claros (La Madrugadora) sobre el muro se
            // perdia. Abajo el HUD ya tiene su velo y el numero siempre se lee.
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(0f, 0f);
            root.pivot = new Vector2(0f, 0f);
            root.anchoredPosition = new Vector2(52f, 44f);
            root.sizeDelta = new Vector2(260f, 150f);
            _comboRoot = root;

            _comboNumber = UIFactory.AddDisplayText(root, "ComboNumber", "0", 84, KomTheme.PrimaryHot,
                TextAnchor.UpperLeft);
            UIFactory.Place(_comboNumber.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), Vector2.zero, new Vector2(240f, 88f));

            _comboLabel = UIFactory.AddText(root, "ComboLabel", KomLoc.T("G O L P E S", "H I T S"), 13, KomTheme.TextMid,
                TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(_comboLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(3f, -100f), new Vector2(240f, 18f));
            var shadow = _comboLabel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            shadow.effectDistance = new Vector2(0f, -1f);

            _comboRule = UIFactory.AddHLine(root, "ComboRule", KomTheme.AccentHot, 1f);
            UIFactory.Place(_comboRule.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(2f, -124f), new Vector2(96f, 1f));

            // Sin CanvasGroup: el grupo reporta alpha 1 pero el canvas lo aplica en
            // su propia pasada, y con el editor en pausa o un tiron de frame el
            // contador se quedaba invisible con alpha "1" (defecto medido). El alfa
            // se aplica grafico a grafico y la visibilidad la manda SetActive.
            _comboRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Muestra el contador. `player` decide el lado (el golpe se cuenta del lado de
        /// quien lo da). Se llama en cada impacto; la ventana de vida la lleva el HUD.
        /// </summary>
        public void ShowCombo(int count, bool player, Color color)
        {
            if (this == null || !isActiveAndEnabled) return;
            if (_comboRoot == null || _comboNumber == null || _comboLabel == null) return;

            _comboRoot.anchorMin = _comboRoot.anchorMax = player ? new Vector2(0f, 0f) : new Vector2(1f, 0f);
            _comboRoot.pivot = player ? new Vector2(0f, 0f) : new Vector2(1f, 0f);
            _comboRoot.anchoredPosition = player ? new Vector2(52f, 44f) : new Vector2(-52f, 44f);

            var align = player ? TextAnchor.UpperLeft : TextAnchor.UpperRight;
            _comboNumber.alignment = align;
            _comboLabel.alignment = align;
            // El filete nace en el borde exterior del bloque.
            var ruleRt = _comboRule.rectTransform;
            ruleRt.anchorMin = ruleRt.anchorMax = player ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            ruleRt.pivot = player ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            ruleRt.anchoredPosition = player ? new Vector2(2f, -124f) : new Vector2(-2f, -124f);

            _comboNumber.text = count.ToString();
            // A partir de 4 golpes el contador deja de ser informativo y pasa a ser
            // celebracion: sube el cuerpo y se enciende.
            _comboNumber.fontSize = count >= 6 ? 96 : (count >= 4 ? 90 : 84);
            _comboNumber.color = count >= 4 ? KomTheme.PrimaryHot : KomTheme.TextHi;
            _comboRule.color = KomData.Mono(Color.Lerp(color, Color.white, 0.35f));
            _comboRuleBaseAlpha = count >= 4 ? 0.9f : 0.45f;
            _comboRule.color = new Color(_comboRule.color.r, _comboRule.color.g, _comboRule.color.b,
                _comboRuleBaseAlpha);

            _comboIdle = 0f;
            _comboPop = 1f;
            // Aparece ENTERO en el frame del golpe. Un fundido de entrada de 80 ms
            // retrasa el feedback justo cuando el jugador esta mirando el impacto;
            // el "pop" de escala ya da la sensacion de entrada.
            _comboAlpha = 1f;
            _comboRoot.gameObject.SetActive(true);
            ApplyComboAlpha();
        }

        public void HideCombo()
        {
            _comboIdle = 99f;
            _comboAlpha = 0f;
            ApplyComboAlpha();
            if (_comboRoot != null) _comboRoot.gameObject.SetActive(false);
        }

        // Alfa grafico a grafico (sin CanvasGroup, ver BuildComboCounter).
        void ApplyComboAlpha()
        {
            if (_comboNumber != null)
            {
                var c = _comboNumber.color;
                _comboNumber.color = new Color(c.r, c.g, c.b, _comboAlpha);
            }
            if (_comboLabel != null)
            {
                var c = _comboLabel.color;
                _comboLabel.color = new Color(c.r, c.g, c.b, _comboAlpha * 0.85f);
            }
            if (_comboRule != null)
            {
                var c = _comboRule.color;
                _comboRule.color = new Color(c.r, c.g, c.b, c.a > 0f ? _comboAlpha * _comboRuleBaseAlpha : 0f);
            }
        }

        void LateUpdate()
        {
            if (_comboRoot == null || _comboNumber == null) return;
            if (!_comboRoot.gameObject.activeSelf) return;
            // dt acotado: un tiron (carga, GC, pausa del editor) devuelve un
            // unscaledDeltaTime enorme y se comeria la ventana entera del contador
            // en un solo frame. Con el tope, el pop siempre se ve.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _comboIdle += dt;
            _comboPop = Mathf.MoveTowards(_comboPop, 0f, dt * 6.5f);
            // 2.0 s: el contador tiene que sobrevivir a la pausa entre golpes de un
            // combo lento (0.55 s entre pulsaciones) y a la lectura del jugador.
            float target = _comboIdle < 2f ? 1f : 0f;
            _comboAlpha = Mathf.MoveTowards(_comboAlpha, target, dt * (target > _comboAlpha ? 12f : 3.4f));
            ApplyComboAlpha();
            // Pop de escala: el numero "golpea" al aparecer y se asienta en 0.15 s.
            float pop = _comboPop * _comboPop;
            _comboNumber.rectTransform.localScale = Vector3.one * (1f + 0.10f * pop);
            if (_comboAlpha <= 0.001f) _comboRoot.gameObject.SetActive(false);
        }

        public void ShowBanner(string main, string sub, Color color, float hold = 0.9f)
        {
            // El HUD puede haberse destruido mientras una corrutina del combate seguia
            // viva (fin del play, cambio de escena). Sin esta guarda StartCoroutine lanza
            // MissingReferenceException y se pierde el resto de la ronda.
            if (this == null || !isActiveAndEnabled) return;
            if (_bannerGroup == null || _bannerMain == null || _bannerSub == null) return;
            // El rotulo tambien es B/N: el color del especial se queda en el VFX.
            color = KomData.Mono(color);
            if (_bannerRoutine != null) StopCoroutine(_bannerRoutine);
            _bannerRoutine = StartCoroutine(BannerRoutine(main, sub, color, hold));
        }

        IEnumerator BannerRoutine(string main, string sub, Color color, float hold)
        {
            if (_bannerGroup == null || _bannerMain == null || _bannerSub == null) yield break;
            _bannerMain.text = main;
            _bannerMain.color = color;
            _bannerSub.text = sub;
            _bannerGroup.alpha = 0f;
            _bannerMain.rectTransform.localScale = Vector3.one * 1.16f;
            float t = 0f;
            while (t < 0.16f)
            {
                if (_bannerGroup == null || _bannerMain == null) yield break;
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / 0.16f);
                _bannerGroup.alpha = k;
                _bannerMain.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.16f, 1f, k * k);
                yield return null;
            }
            _bannerGroup.alpha = 1f;
            _bannerMain.rectTransform.localScale = Vector3.one;
            yield return new WaitForSecondsRealtime(hold);
            if (_bannerGroup == null || _bannerMain == null) yield break;
            t = 0f;
            while (t < 0.24f)
            {
                if (_bannerGroup == null) yield break;
                t += Time.unscaledDeltaTime;
                _bannerGroup.alpha = 1f - Mathf.Clamp01(t / 0.24f);
                yield return null;
            }
            _bannerGroup.alpha = 0f;
            _bannerRoutine = null;
        }

        public void FlashScreen(Color color, float alpha = 0.3f, float duration = 0.18f)
        {
            if (this == null || !isActiveAndEnabled) return;
            if (_hitFlash == null) return;
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FlashRoutine(color, alpha, duration));
        }

        IEnumerator FlashRoutine(Color color, float alpha, float duration)
        {
            if (_hitFlash == null) yield break;
            color.a = alpha;
            _hitFlash.color = UIFactory.Mono(color);
            float t = 0f;
            while (t < duration)
            {
                if (_hitFlash == null) yield break;
                t += Time.unscaledDeltaTime;
                color.a = Mathf.Lerp(alpha, 0f, Mathf.Clamp01(t / duration));
                _hitFlash.color = UIFactory.Mono(color);
                yield return null;
            }
            _hitFlash.color = UIFactory.Mono(new Color(color.r, color.g, color.b, 0f));
            _flashRoutine = null;
        }

        public void ShowPause(bool show, Action resume = null, Action menu = null, Action map = null)
        {
            if (_pauseOverlay == null) return;
            if (show)
            {
                _resumeAction = resume;
                _menuAction = menu;
                _mapAction = map;
                if (_pauseMapButton != null)
                    _pauseMapButton.gameObject.SetActive(!_versus && map != null);
                if (_worldMapOverlay != null) _worldMapOverlay.SetActive(false);
                _pauseOverlay.SetActive(true);
                _pauseOverlay.transform.SetAsLastSibling();
            }
            else
            {
                _pauseOverlay.SetActive(false);
            }
        }

        /// <summary>
        /// Abre o cierra el mapa consultable. Se dibuja sobre el combate congelado; al
        /// cerrar se ejecuta el callback (volver a la pelea) sin recargar la escena.
        /// </summary>
        public void ShowWorldMap(bool show, Action onClose = null)
        {
            if (_worldMapOverlay == null) return;
            if (show)
            {
                _worldMapClose = onClose;
                if (_pauseOverlay != null) _pauseOverlay.SetActive(false);
                _worldMapOverlay.SetActive(true);
                _worldMapOverlay.transform.SetAsLastSibling();
            }
            else
            {
                _worldMapOverlay.SetActive(false);
                _worldMapClose = null;
            }
        }

        public void CloseWorldMap()
        {
            if (!WorldMapVisible) return;
            var callback = _worldMapClose;
            _worldMapOverlay.SetActive(false);
            _worldMapClose = null;
            callback?.Invoke();
        }

        /// <summary>Cambia el titular del resultado. En versus el titulo dice quien gano.</summary>
        public void SetResultTitle(string text)
        {
            if (_resultTitle != null) _resultTitle.text = text;
        }

        /// <summary>Rótulos del resultado que cambian de significado en versus.</summary>
        public void SetResultChrome(string kicker, string hint)
        {
            if (_resultKicker != null && !string.IsNullOrEmpty(kicker)) _resultKicker.text = kicker;
            if (_resultHint != null && !string.IsNullOrEmpty(hint)) _resultHint.text = hint;
        }

        public void ShowResult(bool win, bool final, CharacterDef player, CharacterDef enemy, string opponentGenre,
            string primaryLabel, string secondaryLabel, Action primary, Action secondary,
            string subOverride = null, string bodyOverride = null)
        {
            if (_resultOverlay == null || _resultTitle == null) return;
            _primaryAction = primary;
            _secondaryAction = secondary;
            _resultTitle.text = final && win
                ? KomLoc.T("CAMPEÓN DEL TORNEO", "TOURNAMENT CHAMPION")
                : (win ? KomLoc.T("VICTORIA", "VICTORY") : KomLoc.T("DERROTA", "DEFEAT"));
            _resultTitle.color = win ? KomTheme.TextHi : new Color(0.72f, 0.72f, 0.72f, 1f);
            _resultSub.text = win
                ? KomLoc.T("EL TORNEO CONTINÚA", "THE TOURNAMENT CONTINUES")
                : KomLoc.T("EL TORNEO NO TERMINA AQUÍ", "THE TOURNAMENT DOES NOT END HERE");
            if (final && win)
            {
                _resultSub.text = KomLoc.T("LA MÚSICA MEXICANA ESTÁ A SALVO", "MEXICAN MUSIC IS SAFE");
                _resultBody.text = KomLoc.T(
                    "Has derrotado a Flavia Cheinbaum.\n\nLa diversidad musical de México\nno será reducida a una sola voz.",
                    "You have defeated Flavia Cheinbaum.\n\nMexico's musical diversity\nwill not be reduced to a single voice.");
            }
            else if (win)
            {
                _resultBody.text = KomLoc.T(
                    $"VETO MUSICAL\n\nEl género {opponentGenre.ToUpperInvariant()}\nha sido eliminado del torneo.",
                    $"MUSICAL VETO\n\nThe {opponentGenre.ToUpperInvariant()} genre\nhas been erased from the tournament.");
            }
            else
            {
                _resultBody.text = KomLoc.T(
                    "Tu género pierde esta batalla…\n\npero el torneo aún no termina.\nVuelve a intentarlo.",
                    "Your genre loses this battle…\n\nbut the tournament is not over.\nTry again.");
            }

            _resultPlayerCard.sprite = UIFactory.LoadSprite(player != null ? player.cardResource : null);
            _resultEnemyCard.sprite = UIFactory.LoadSprite(enemy != null ? enemy.cardResource : null);
            _resultPlayerCard.color = win ? Color.white : new Color(0.62f, 0.62f, 0.62f, 1f);
            _resultEnemyCard.color = win ? new Color(0.62f, 0.62f, 0.62f, 1f) : Color.white;
            // Solo la ficha del ganador lleva luz; la del perdedor queda apagada.
            if (_resultPlayerAura != null)
                _resultPlayerAura.Set(KomData.Accent(player), win ? 1f : 0f);
            if (_resultEnemyAura != null)
                _resultEnemyAura.Set(KomData.Accent(enemy), win ? 0f : 1f);
            // El modo de dos jugadores no habla de torneo: sus textos llegan ya escritos.
            if (!string.IsNullOrEmpty(subOverride)) _resultSub.text = subOverride;
            if (!string.IsNullOrEmpty(bodyOverride)) _resultBody.text = bodyOverride;

            _primaryLabel.text = primaryLabel;
            _secondaryLabel.text = secondaryLabel;
            _secondaryButton.gameObject.SetActive(secondary != null);
            _resultOverlay.SetActive(true);
        }

        public void HideResult() => _resultOverlay.SetActive(false);

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // La leyenda entra al empezar la pelea y se retira sola.
            if (_legendGroup != null)
            {
                _legendTimer += dt;
                float target = (_legendPersistent && !ResultVisible) || _legendTimer < 9f || PauseVisible || ControlsVisible ? 1f : 0f;
                _legendGroup.alpha = Mathf.MoveTowards(_legendGroup.alpha, target, dt * (target > 0f ? 1.6f : 0.9f));
            }

            if (_playerFill == null) return;
            _playerShown = Mathf.MoveTowards(_playerShown, _playerTarget, dt * 0.8f);
            _enemyShown = Mathf.MoveTowards(_enemyShown, _enemyTarget, dt * 0.8f);
            _playerGhostShown = Mathf.MoveTowards(_playerGhostShown, _playerTarget, dt * 0.35f);
            _enemyGhostShown = Mathf.MoveTowards(_enemyGhostShown, _enemyTarget, dt * 0.35f);
            _playerFill.fillAmount = _playerShown;
            _enemyFill.fillAmount = _enemyShown;
            _playerGhost.fillAmount = _playerGhostShown;
            _enemyGhost.fillAmount = _enemyGhostShown;
            if (_playerBevel != null) _playerBevel.fillAmount = _playerShown;
            if (_enemyBevel != null) _enemyBevel.fillAmount = _enemyShown;

            // El tono sigue a la fracción mostrada: el color drena con la barra.
            ApplyBarColors();

            _playerSuperShown = Mathf.MoveTowards(_playerSuperShown, _playerSuperTarget, dt * 2.2f);
            _enemySuperShown = Mathf.MoveTowards(_enemySuperShown, _enemySuperTarget, dt * 2.2f);
            if (_playerSuper != null)
            {
                _playerSuper.fillAmount = _playerSuperShown;
                if (_playerSuperBevel != null) _playerSuperBevel.fillAmount = _playerSuperShown;
            }
            if (_enemySuper != null)
            {
                _enemySuper.fillAmount = _enemySuperShown;
                if (_enemySuperBevel != null) _enemySuperBevel.fillAmount = _enemySuperShown;
            }

            if (_timeLow && _timer != null)
            {
                float k = 1f + Mathf.Sin(Time.unscaledTime * 8f) * 0.06f;
                _timer.rectTransform.localScale = Vector3.one * k;
            }
            else if (_timer != null)
            {
                _timer.rectTransform.localScale = Vector3.one;
            }
        }

        /// <summary>
        /// La barra de súper, del azul profundo al eléctrico. Al llegar al 100 % late hacia
        /// blanco: es el único momento en que la barra pide atención, y coincide con el
        /// aviso de sonido y con el aura del personaje.
        /// </summary>
        static Color SuperBarColor(float fill)
        {
            var c = RampColor(SuperRamp, fill);
            if (fill >= 0.999f)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
                return Color.Lerp(c, Color.white, 0.22f + 0.32f * pulse);
            }
            return c;
        }
    }
}
