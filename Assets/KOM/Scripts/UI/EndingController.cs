using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KOM
{
    /// <summary>
    /// Cierre del torneo.
    ///
    /// Es la otra cara de la seleccion: la tarjeta del campeon ocupa el lado derecho y la
    /// izquierda cuenta lo que ha pasado (nombre, genero, cierre narrativo y los rivales
    /// superados, con su tarjeta). Sin texto de relleno y sin repetir el nombre que ya
    /// lleva impreso la tarjeta en grande: aqui el nombre va en la ficha, la tarjeta es el
    /// trofeo.
    /// </summary>
    public sealed class EndingController : MonoBehaviour
    {
        GameSession _session;
        Canvas _canvas;
        RectTransform _card;
        Vector2 _cardBase;
        float _time;

        void Start()
        {
            _session = GameSession.Ensure();
            if (!_session.HasRun || !_session.IsRunComplete)
            {
                ScreenFader.Load(KomScenes.MainMenu);
                return;
            }
            BuildCamera();
            BuildUI();
            // El tema vuelve para el cierre y, detras, una porra de fiesta.
            KomAudio.Menu();
            KomAudio.Porra(KomAudio.LaChona, 0.8f, 0.9f, KomScenes.Ending);
            ScreenFader.FadeIn();
        }

        /// <summary>
        /// Preview del editor: simula un torneo terminado para poder revisar la pantalla
        /// sin jugarlo entero.
        /// </summary>
        public void BuildEditorPreview(string playerId = "juan_grabriel")
        {
            _session = GameSession.Ensure();
            _session.ResetRun();
            _session.BeginRun(playerId);
            for (int i = 0; i < _session.NodeCount; i++) _session.CompleteEncounter();
            BuildCamera();
            BuildUI();
        }

        void BuildCamera()
        {
            var go = new GameObject("EndingCamera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            go.transform.position = new Vector3(0f, 0f, -10f);
        }

        void BuildUI()
        {
            UIFactory.DestroyCanvas(_canvas);
            var canvas = UIFactory.CreateCanvas("EndingCanvas");
            _canvas = canvas;
            var root = (RectTransform)canvas.transform;

            var ink = UIFactory.AddImage(root, "Ink", UIFactory.WhiteSprite(), Color.black);
            UIFactory.Stretch(ink.rectTransform);

            // Un foco suave detras de la tarjeta: el unico "premio" luminoso de la pantalla.
            var glow = UIFactory.AddGlow(root, "ChampionGlow", new Color(1f, 1f, 1f, 0.14f));
            UIFactory.Place(glow.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-260f, 20f), new Vector2(1100f, 1100f));

            UIFactory.AddVignette(root, 0.7f);
            UIFactory.AddGrain(root, 0.022f);

            var player = _session.PlayerDef;

            BuildCard(root, player);
            BuildText(root, player);
            BuildFooter(root);
        }

        void BuildCard(Transform root, CharacterDef player)
        {
            var holder = UIFactory.Rect(root, "ChampionCard");
            holder.anchorMin = new Vector2(1f, 0.5f);
            holder.anchorMax = new Vector2(1f, 0.5f);
            holder.pivot = new Vector2(1f, 0.5f);
            // 430x573 = la proporción real de la tarjeta (0.75). Con 585 el dibujo dejaba
            // 12 px de banda y el aura habría enmarcado el hueco, no la ilustración.
            holder.sizeDelta = new Vector2(430f, 573f);
            holder.anchoredPosition = new Vector2(-KomTheme.Margin - 20f, 10f);

            var art = UIFactory.AddImage(holder, "Art",
                UIFactory.LoadSprite(player != null ? player.cardResource : null), Color.white);
            art.preserveAspect = true;
            UIFactory.Stretch(art.rectTransform);
            UIFactory.AddCardAura(art, KomData.Accent(player), 1f);

            var brackets = UIFactory.AddCornerBrackets(holder, "Brackets", KomTheme.AccentHot, 38f, 2f);
            UIFactory.Stretch(brackets, -14f, -14f, -14f, -14f);

            _card = holder;
            _cardBase = holder.anchoredPosition;
        }

        void BuildText(Transform root, CharacterDef player)
        {
            var col = UIFactory.Rect(root, "Column");
            col.anchorMin = new Vector2(0f, 1f);
            col.anchorMax = new Vector2(0f, 1f);
            col.pivot = new Vector2(0f, 1f);
            col.sizeDelta = new Vector2(900f, 700f);
            col.anchoredPosition = new Vector2(KomTheme.Margin, -92f);

            // El nombre del torneo sale del catalogo, igual que en el mapa y el menu: si
            // cambia el JSON, el cierre no se queda con el nombre viejo.
            var tournament = KomData.Tournament;
            string tournamentName = tournament != null && !string.IsNullOrEmpty(tournament.title)
                ? tournament.title
                : KomLoc.T("Torneo del Bienestar", "Wellbeing Tournament");
            var kicker = UIFactory.AddLabel(col, "Kicker",
                tournamentName + KomLoc.T(" · Modo historia", " · Story mode"));
            UIFactory.Place(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(700f, 20f));

            var title = UIFactory.AddDisplayText(col, "Title", KomLoc.T("CAMPEÓN", "CHAMPION"), KomTheme.DisplayL, KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(-5f, -24f), new Vector2(760f, 100f));

            var title2 = UIFactory.AddDisplayText(col, "Title2", KomLoc.T("DEL TORNEO", "OF THE TOURNAMENT"), 52, KomTheme.Accent, TextAnchor.UpperLeft);
            UIFactory.Place(title2.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(-2f, -116f), new Vector2(760f, 66f));

            var rule = UIFactory.AddImage(col, "Rule", UIFactory.WhiteSprite(), KomTheme.LineStrong);
            UIFactory.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -204f), new Vector2(96f, 1f));
            var diamond = UIFactory.AddDiamond(col, "Diamond", KomTheme.Accent, 7f);
            UIFactory.Place(diamond.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(108f, -204f), new Vector2(7f, 7f));

            var name = UIFactory.AddText(col, "Name", player != null ? player.displayName : "P1",
                34, KomTheme.TextHi, TextAnchor.UpperLeft);
            name.font = UIFactory.DisplayFont;
            UIFactory.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -226f), new Vector2(700f, 44f));

            var genre = UIFactory.AddLabel(col, "Genre", player != null ? player.genre : "", TextAnchor.UpperLeft);
            UIFactory.Place(genre.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(2f, -274f), new Vector2(700f, 18f));

            var body = UIFactory.AddText(col, "Body",
                KomLoc.T(
                    "El torneo terminó y nadie se puso la corona.\nLos géneros borrados vuelven uno por uno, de la mano de quienes los recuerdan.",
                    "The tournament is over and nobody put on the crown.\nThe erased genres return one by one, led by those who remember them."),
                KomTheme.Body, KomTheme.TextMid, TextAnchor.UpperLeft);
            UIFactory.Place(body.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -308f), new Vector2(820f, 72f));
            body.lineSpacing = 1.15f;

            BuildRoster(col);

            var thanks = UIFactory.AddDisplayText(col, "Thanks", KomLoc.T("GRACIAS POR JUGAR", "THANK YOU FOR PLAYING"), 30, KomTheme.TextLo, TextAnchor.UpperLeft);
            UIFactory.Place(thanks.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -604f), new Vector2(600f, 40f));

            var button = UIFactory.AddPrimaryButton(root, "Menu", KomLoc.T("VOLVER AL MENÚ  ·  ENTER", "BACK TO MENU  ·  ENTER"), GoMenu, 22);
            UIFactory.Place(button.image.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(KomTheme.Margin, 58f), new Vector2(400f, 62f));
        }

        void BuildRoster(Transform col)
        {
            var label = UIFactory.AddLabel(col, "DefeatedLabel", KomLoc.T("Rivales superados", "Rivals defeated"), TextAnchor.UpperLeft);
            UIFactory.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(2f, -420f), new Vector2(600f, 18f));

            var ids = new List<string>(_session.Opponents ?? new string[0]);
            var boss = KomData.Boss;
            if (boss != null) ids.Add(boss.id);

            float x = 0f;
            for (int i = 0; i < ids.Count; i++)
            {
                var def = KomData.GetCharacter(ids[i]);
                if (def == null) continue;
                var sprite = UIFactory.LoadSprite(def.cardResource);
                float h = 116f;
                float w = sprite != null ? h * (sprite.rect.width / sprite.rect.height) : h * 0.74f;
                var img = UIFactory.AddImage(col, "Defeated" + i, sprite, new Color(0.8f, 0.8f, 0.8f, 1f));
                img.preserveAspect = true;
                UIFactory.Place(img.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(x, -446f), new Vector2(w, h));
                // Sin luz: el protagonista de la pantalla es el campeon.
                UIFactory.AddCardAura(img, KomData.Accent(def), 0f);
                x += w + 10f;
            }
        }

        void BuildFooter(Transform root)
        {
            var bar = UIFactory.Rect(root, "Footer");
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

            int rivals = (_session.Opponents != null ? _session.Opponents.Length : 0) + 1;
            var status = UIFactory.AddText(bar, "Status",
                KomLoc.T($"{rivals} RIVALES  ·  1 TORNEO  ·  1 CAMPEÓN",
                         $"{rivals} RIVALS  ·  1 TOURNAMENT  ·  1 CHAMPION"),
                KomTheme.Micro, KomTheme.TextLo, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(status.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(KomTheme.Margin, 0f), new Vector2(700f, 18f));

            var hint = UIFactory.AddText(bar, "Hint", KomLoc.T("ENTER  ·  VOLVER AL MENÚ", "ENTER  ·  BACK TO MENU"), KomTheme.Micro,
                KomTheme.TextLo, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(hint.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-KomTheme.Margin, 0f), new Vector2(400f, 18f));
        }

        void Update()
        {
            _time += Time.unscaledDeltaTime;
            if (_card != null)
                _card.anchoredPosition = _cardBase + new Vector2(0f, Mathf.Sin(_time * 0.7f) * 6f);
            if (InputReader.ConfirmPressed()) GoMenu();
        }

        void GoMenu()
        {
            _session.ResetRun();
            ScreenFader.Load(KomScenes.MainMenu);
        }
    }
}
