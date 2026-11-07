using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KOM
{
    /// <summary>
    /// Mapa del torneo.
    ///
    /// El grabado de la Ciudad de Mexico YA trae rotulados los tres recintos y la ruta
    /// punteada, asi que la interfaz no repite nada de eso: solo dibuja el ESTADO encima
    /// de cada pin (completado / siguiente / pendiente) y una ficha de combate abajo con
    /// el escenario, el rival y la dificultad.
    ///
    /// La final se disputa en el Zócalo: no tiene pin propio, se marca sobre el pin
    /// del tercer recinto. Dos chinchetas en el mismo sitio serian una errata, no una ruta.
    /// </summary>
    public sealed class MapController : MonoBehaviour
    {
        sealed class NodeUI
        {
            public RectTransform rect;
            public Image glow;
            public Image ring;
            public Image core;
            public Text tag;
            public Button button;
            public int nodeIndex;
            // Marca de "este es el combate que toca". Antes se deducia comparando el
            // texto de la placa con "SIGUIENTE", que se rompe en cuanto la interfaz
            // cambia de idioma.
            public bool current;
        }

        sealed class VersusStageEntry
        {
            public StageDef stage;
            public float mapX;
            public float mapY;
            public RectTransform pin;
            public Image glow;
            public Image ring;
            public Image core;
            public Text tag;
        }

        sealed class VersusRoundChip
        {
            public Image fill;
            public RectTransform frame;
            public Text label;
            public Image accent;
        }

        GameSession _session;
        Canvas _canvas;
        readonly List<NodeUI> _nodes = new List<NodeUI>();
        int _finalPinIndex;
        Text _toastText;
        CanvasGroup _toastGroup;
        Coroutine _toastRoutine;
        NodeUI _currentNodeUi;
        float _pulse;

        Text _stageName;
        Text _stageSubtitle;
        Text _stageNodeLabel;
        Text _opponentName;
        Text _opponentGenre;
        Image _opponentCard;
        UIFactory.CardAura _playerCardAura;
        UIFactory.CardAura _opponentCardAura;
        Image[] _difficultyPips;
        Button _fightButton;
        GameObject _completePanel;

        // Modo de dos jugadores: el mapa del torneo no pinta nada, asi que esta pantalla
        // se convierte en el selector compartido de recinto y de rondas.
        bool _versus;
        int _versusStage;
        int _versusRounds;              // 0 = a una, 1 = al mejor de 3, 2 = al mejor de 5
        // El grabado del mapa tiene el circulo del recinto ~25 px por debajo del punto
        // que declara el JSON. El anillo se dibuja sobre el circulo impreso, no sobre el
        // numero de nodo: -16 es la correccion medida sobre la captura a 1920x1080.
        const float VersusPinY = -16f;
        const float StoryPinY = -16f;
        readonly List<VersusStageEntry> _versusStages = new List<VersusStageEntry>();
        readonly List<VersusRoundChip> _versusRoundChips = new List<VersusRoundChip>();
        FighterControls _versusP1;
        FighterControls _versusP2;
        Text _versusStageName;
        Text _versusStageSubtitle;
        Text _versusStageCounter;
        Text _versusP1Device;
        Text _versusP2Device;
        Text _versusP1Name;
        Text _versusP2Name;
        float _versusPulse;

        void Start()
        {
            _session = GameSession.Ensure();
            if (_session.VersusMode)
            {
                BuildCamera();
                BuildVersusUI();
                KomAudio.Menu();
                ScreenFader.FadeIn();
                return;
            }
            if (!_session.HasRun)
            {
                ScreenFader.Load(KomScenes.CharacterSelect);
                return;
            }

            BuildCamera();
            BuildUI();
            KomAudio.Menu();
            ScreenFader.FadeIn();
        }

        public void BuildEditorPreview(string playerId = "juan_grabriel")
        {
            _session = GameSession.Ensure();
            if (_session.VersusMode) _session.ExitVersus();
            if (!_session.HasRun) _session.BeginRun(playerId);
            BuildCamera();
            BuildUI();
        }

        void BuildCamera()
        {
            var camGo = new GameObject("MapCamera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            camGo.transform.position = new Vector3(0f, 0f, -10f);
        }

        void BuildUI()
        {
            UIFactory.DestroyCanvas(_canvas);
            _nodes.Clear();
            _canvas = UIFactory.CreateCanvas("MapCanvas");
            var root = (RectTransform)_canvas.transform;

            var ink = UIFactory.AddImage(root, "Ink", UIFactory.WhiteSprite(), Color.black);
            UIFactory.Stretch(ink.rectTransform);

            var map = UIFactory.AddImage(root, "MapImage", UIFactory.LoadSprite("UI/Map/tournament_map"),
                new Color(0.9f, 0.9f, 0.9f, 1f));
            UIFactory.Stretch(map.rectTransform);

            var tint = UIFactory.AddImage(root, "Tint", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0.18f));
            UIFactory.Stretch(tint.rectTransform);

            UIFactory.AddVignette(root, 0.82f);

            var top = UIFactory.AddImage(root, "TopShade",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0.92f), new Color(0f, 0f, 0f, 0f)), Color.white);
            UIFactory.Place(top.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 240f));

            var bottom = UIFactory.AddImage(root, "BottomShade",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.96f)), Color.white);
            UIFactory.Place(bottom.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0f, 440f));

            UIFactory.AddGrain(root, 0.022f);

            BuildHeader(root);
            BuildNodes(root);
            BuildBottomPanel(root);
            BuildToast(root);
            BuildCompletePanel(root);
        }

        void BuildHeader(Transform root)
        {
            var kicker = UIFactory.AddLabel(root, "Kicker", KomLoc.T("Modo historia · Ruta de encuentros", "Story mode · Encounter route"));
            UIFactory.Place(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(KomTheme.Margin, -48f), new Vector2(700f, 20f));

            var title = UIFactory.AddDisplayText(root, "Header", KomLoc.T("MAPA DEL TORNEO", "TOURNAMENT MAP"), 46, KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(KomTheme.Margin - 4f, -70f), new Vector2(900f, 60f));

            var rule = UIFactory.AddHLine(root, "HeaderRule", KomTheme.Line, 1f);
            UIFactory.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(KomTheme.Margin, -134f), new Vector2(96f, 1f));
        }

        // ------------------------------------------------------------------ pines

        void BuildNodes(Transform root)
        {
            var layer = UIFactory.Rect(root, "Nodes");
            UIFactory.Stretch(layer);
            var nodes = KomData.Tournament.nodes;
            // El ultimo nodo no se dibuja aparte: reutiliza el pin del recinto donde se
            // celebra la final (antes La Madrugadora, ahora el Zocalo). Asi la ruta no
            // duplica un pin en el mapa cuando dos peleas comparten escenario.
            int pins = Mathf.Max(1, nodes.Length - 1);
            _finalPinIndex = FinalPinIndex(nodes);
            for (int i = 0; i < pins && i < nodes.Length; i++)
            {
                var ui = BuildPin(layer, i, nodes[i]);
                _nodes.Add(ui);
            }
            foreach (var pin in _nodes) RefreshPin(pin);
            foreach (var pin in _nodes)
                if (pin.current) _currentNodeUi = pin;
        }

        // Ultimo pin dibujado que comparte RECINTO con el nodo final. Es lo que
        // convierte el pin del Zocalo en "FINAL · SIGUIENTE" cuando llega el combate 4.
        // No basta comparar stageId: la final usa `zocalo_final` (misma plaza, misma
        // grada, luz distinta) y el pin de la ruta es `zocalo`. Se comparan prefab y
        // transform, que es lo que define el lugar fisico en el mapa.
        static int FinalPinIndex(TournamentNodeDef[] nodes)
        {
            if (nodes == null || nodes.Length < 2) return 0;
            var finalStage = KomData.GetStage(nodes[nodes.Length - 1].stageId);
            if (finalStage == null) return 0;
            for (int i = nodes.Length - 2; i >= 0; i--)
            {
                var stage = nodes[i] != null ? KomData.GetStage(nodes[i].stageId) : null;
                if (stage != null && SameVenue(stage, finalStage)) return i;
            }
            return 0;
        }

        public static bool SameVenue(StageDef a, StageDef b)
        {
            // Dos escenarios procedurales comparten prefab vacio y transform cero: no son
            // el mismo lugar. Solo se reutiliza el pin de un recinto REAL e identico.
            if (string.IsNullOrEmpty(a.prefabResource) || a.prefabResource != b.prefabResource)
                return false;
            return Mathf.Approximately(a.prefabOffsetX, b.prefabOffsetX) &&
                   Mathf.Approximately(a.prefabOffsetY, b.prefabOffsetY) &&
                   Mathf.Approximately(a.prefabOffsetZ, b.prefabOffsetZ) &&
                   Mathf.Approximately(a.prefabRotationX, b.prefabRotationX) &&
                   Mathf.Approximately(a.prefabRotationY, b.prefabRotationY) &&
                   Mathf.Approximately(a.prefabScale, b.prefabScale);
        }

        NodeUI BuildPin(Transform layer, int index, TournamentNodeDef node)
        {
            var holder = UIFactory.Rect(layer, "Pin_" + index);
            holder.anchorMin = new Vector2(node.mapX, node.mapY);
            holder.anchorMax = new Vector2(node.mapX, node.mapY);
            holder.pivot = new Vector2(0.5f, 0.5f);
            holder.sizeDelta = new Vector2(200f, 110f);
            holder.anchoredPosition = Vector2.zero;

            var glow = UIFactory.AddGlow(holder, "Glow", new Color(1f, 1f, 1f, 0.16f));
            UIFactory.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, StoryPinY), new Vector2(140f, 140f));

            var hit = UIFactory.AddImage(holder, "Hit", UIFactory.CircleSprite(), new Color(0f, 0f, 0f, 0.02f));
            UIFactory.Place(hit.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, StoryPinY), new Vector2(78f, 78f));
            hit.raycastTarget = true;
            var button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            int captured = index;
            button.onClick.AddListener(() => OnNodeClicked(captured));

            // Anillo fino alrededor del circulo ya impreso en el grabado.
            var ring = UIFactory.AddImage(holder, "Ring", UIFactory.RingSprite(128), KomTheme.TextHi);
            UIFactory.Place(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, StoryPinY), new Vector2(58f, 58f));

            var core = UIFactory.AddImage(holder, "Core", UIFactory.WhiteSprite(), KomTheme.TextHi);
            UIFactory.Place(core.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, StoryPinY), new Vector2(7f, 7f));
            core.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            var plate = UIFactory.AddPanel(holder, "TagPlate", new Color(0f, 0f, 0f, 0.78f), 2);
            UIFactory.Place(plate.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f),
                new Vector2(0f, 40f), new Vector2(150f, 22f));

            var tag = UIFactory.AddText(plate.transform, "Tag", "", 11, KomTheme.TextHi, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(tag.rectTransform, 6f, 1f, 6f, 1f);

            return new NodeUI
            {
                rect = holder,
                glow = glow,
                ring = ring,
                core = core,
                tag = tag,
                button = button,
                nodeIndex = index
            };
        }

        void RefreshPin(NodeUI pin)
        {
            int index = pin.nodeIndex;
            int current = _session.EncounterIndex;
            bool complete = _session.IsRunComplete;
            bool isFinalNode = index == _finalPinIndex &&
                current >= KomData.Tournament.nodes.Length - 1 && !complete;

            bool done = !complete && index < current;
            bool now = !complete && (index == current || isFinalNode);
            pin.current = now;

            var plate = pin.tag.transform.parent.gameObject;
            if (done && !isFinalNode)
            {
                pin.ring.color = new Color(0.50f, 0.52f, 0.51f, 0.85f);
                pin.core.color = new Color(0.50f, 0.52f, 0.51f, 0.85f);
                pin.glow.color = new Color(1f, 1f, 1f, 0.04f);
                pin.tag.text = KomLoc.T("COMPLETADO", "COMPLETED");
                pin.tag.color = new Color(0.55f, 0.56f, 0.55f, 1f);
                pin.button.interactable = false;
                plate.SetActive(true);
            }
            else if (now)
            {
                pin.ring.color = KomTheme.AccentHot;
                pin.core.color = KomTheme.AccentHot;
                pin.glow.color = new Color(1f, 1f, 1f, 0.22f);
                pin.tag.text = isFinalNode
                    ? KomLoc.T("FINAL · SIGUIENTE", "FINAL · NEXT")
                    : KomLoc.T("SIGUIENTE", "NEXT");
                pin.tag.color = KomTheme.AccentHot;
                plate.SetActive(true);
            }
            else
            {
                pin.ring.color = new Color(0.62f, 0.63f, 0.62f, 0.5f);
                pin.core.color = new Color(0.62f, 0.63f, 0.62f, 0.5f);
                pin.glow.color = new Color(1f, 1f, 1f, 0.02f);
                pin.tag.text = complete ? KomLoc.T("COMPLETADO", "COMPLETED") : "";
                pin.tag.color = new Color(0.58f, 0.59f, 0.58f, 1f);
                // El grabado ya rotula el recinto: una placa "PENDIENTE" encima solo estorba.
                plate.SetActive(complete);
            }
        }

        // ------------------------------------------------------------------ ficha

        void BuildBottomPanel(Transform root)
        {
            var node = _session.CurrentNode;
            var stage = node != null ? KomData.GetStage(node.stageId) : null;
            var opponent = _session.CurrentOpponentDef;
            var player = _session.PlayerDef;

            var panel = UIFactory.AddPanel(root, "BottomPanel", new Color(0.043f, 0.047f, 0.051f, 0.975f), 3);
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 40f), new Vector2(1728f, 184f));
            UIFactory.AddFrame(panel.transform, "Border", KomTheme.Line, 1f);

            var accent = UIFactory.AddImage(panel.transform, "Accent", UIFactory.WhiteSprite(), KomTheme.Accent);
            UIFactory.Place(accent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(3f, 0f));

            // --- jugador
            // La misma card coleccionable del selector: el busto (headResource) recorta la
            // silueta y desentona con la ficha del rival, que ya usa la card.
            var portrait = UIFactory.AddImage(panel.transform, "PlayerPortrait",
                UIFactory.LoadSprite(player != null ? player.cardResource : null), Color.white);
            portrait.preserveAspect = true;
            UIFactory.Place(portrait.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(40f, 6f), new Vector2(66f, 88f));
            _playerCardAura = UIFactory.AddCardAura(portrait, KomData.Accent(player), 1f);

            var pName = UIFactory.AddDisplayText(panel.transform, "PlayerName",
                player != null ? player.displayName.ToUpperInvariant() : "P1", 24, KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(pName.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(122f, -32f), new Vector2(300f, 30f));

            var pGenre = UIFactory.AddLabel(panel.transform, "PlayerGenre", player != null ? player.genre : "", TextAnchor.UpperLeft);
            UIFactory.Place(pGenre.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(124f, -64f), new Vector2(300f, 18f));

            int total = _session.NodeCount;
            int current = Mathf.Min(_session.EncounterIndex + 1, total);
            var progress = UIFactory.AddText(panel.transform, "Progress",
                KomLoc.T($"ENCUENTRO {current:00} / {total:00}", $"ENCOUNTER {current:00} / {total:00}"), KomTheme.Micro,
                KomTheme.TextLo, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(progress.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(124f, -88f), new Vector2(300f, 16f));

            float segW = 64f;
            for (int i = 0; i < total; i++)
            {
                bool done = i < _session.EncounterIndex || _session.IsRunComplete;
                bool now = i == _session.EncounterIndex && !_session.IsRunComplete;
                var seg = UIFactory.AddImage(panel.transform, "Seg" + i, UIFactory.WhiteSprite(),
                    done ? KomTheme.AccentHot : now ? KomTheme.Accent : new Color(0.18f, 0.19f, 0.19f, 1f));
                UIFactory.Place(seg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(124f + i * (segW + 6f), -118f), new Vector2(segW, 3f));
            }

            var divider1 = UIFactory.AddVLine(panel.transform, "Divider1", KomTheme.Line, 1f);
            UIFactory.Place(divider1.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(500f, 0f), new Vector2(1f, -52f));

            // --- escenario
            var kicker = UIFactory.AddLabel(panel.transform, "Kicker", KomLoc.T("Próximo combate", "Next fight"), TextAnchor.UpperLeft);
            UIFactory.Place(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(544f, -30f), new Vector2(400f, 18f));

            _stageName = UIFactory.AddDisplayText(panel.transform, "StageName", stage != null ? stage.displayName : "—",
                36, KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(_stageName.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(541f, -50f), new Vector2(560f, 46f));

            _stageSubtitle = UIFactory.AddText(panel.transform, "StageSub", stage != null ? stage.subtitle : "",
                KomTheme.Small, KomTheme.TextMid, TextAnchor.UpperLeft);
            UIFactory.Place(_stageSubtitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(544f, -100f), new Vector2(560f, 24f));

            _stageNodeLabel = UIFactory.AddText(panel.transform, "NodeLabel", node != null ? node.label.ToUpperInvariant() : "",
                KomTheme.Micro, KomTheme.TextLo, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(_stageNodeLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(544f, 30f), new Vector2(560f, 16f));

            var divider2 = UIFactory.AddVLine(panel.transform, "Divider2", KomTheme.Line, 1f);
            UIFactory.Place(divider2.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(1096f, 0f), new Vector2(1f, -52f));

            // --- rival
            // El rival no se conoce hasta que la ruleta lo presenta: el mapa anuncia el
            // recinto y la dificultad, no el nombre. Asi la revelacion tiene algo que decir.
            bool rivalKnown = _session.IsEncounterRevealed;
            _opponentCard = UIFactory.AddImage(panel.transform, "OpponentCard",
                rivalKnown ? UIFactory.LoadSprite(opponent != null ? opponent.cardResource : null) : null, Color.white);
            _opponentCard.preserveAspect = true;
            UIFactory.Place(_opponentCard.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(1136f, 6f), new Vector2(66f, 88f));
            if (rivalKnown)
                _opponentCardAura = UIFactory.AddCardAura(_opponentCard, KomData.Accent(opponent), 0f);

            if (!rivalKnown)
            {
                var slot = UIFactory.AddPanel(panel.transform, "OpponentSlot", KomTheme.SurfaceLo, 2);
                UIFactory.Place(slot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(1136f, 6f), new Vector2(66f, 96f));
                UIFactory.AddFrame(slot.transform, "Border", KomTheme.Line, 1f);
                var mark = UIFactory.AddDisplayText(slot.transform, "Mark", "?", 40, KomTheme.TextLo, TextAnchor.MiddleCenter);
                UIFactory.Stretch(mark.rectTransform);
            }

            var oppTag = UIFactory.AddLabel(panel.transform, "OppTag",
                rivalKnown ? KomLoc.T("Rival", "Rival") : KomLoc.T("Rival por revelar", "Rival to be revealed"), TextAnchor.UpperLeft);
            UIFactory.Place(oppTag.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(1216f, -30f), new Vector2(230f, 18f));

            _opponentName = UIFactory.AddDisplayText(panel.transform, "OpponentName",
                rivalKnown && opponent != null ? opponent.displayName : "— — —", 24, KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(_opponentName.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(1214f, -50f), new Vector2(180f, 30f));
            _opponentName.resizeTextForBestFit = true;
            _opponentName.resizeTextMinSize = 14;
            _opponentName.resizeTextMaxSize = 24;

            _opponentGenre = UIFactory.AddText(panel.transform, "OpponentGenre",
                rivalKnown && opponent != null ? opponent.genre.ToUpperInvariant() : KomLoc.T("SE SORTEA EN LA ARENA", "DRAWN IN THE ARENA"),
                11, KomTheme.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(_opponentGenre.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(1216f, -82f), new Vector2(230f, 16f));

            _difficultyPips = new Image[4];
            int difficulty = node != null ? node.difficulty : 1;
            for (int i = 0; i < 4; i++)
            {
                var pip = UIFactory.AddImage(panel.transform, "Pip" + i, UIFactory.WhiteSprite(),
                    i < difficulty ? KomTheme.AccentHot : new Color(0.18f, 0.19f, 0.19f, 1f));
                UIFactory.Place(pip.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(1216f + i * 18f, 42f), new Vector2(9f, 9f));
                pip.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                _difficultyPips[i] = pip;
            }

            _fightButton = UIFactory.AddPrimaryButton(panel.transform, "Fight", KomLoc.T("COMBATIR  ·  ENTER", "FIGHT  ·  ENTER"), StartCombat, 22);
            UIFactory.Place(_fightButton.image.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-36f, 14f), new Vector2(292f, 60f));

            var hint = UIFactory.AddText(panel.transform, "Hint", KomLoc.T("ESC  ·  MENÚ", "ESC  ·  MENU"), KomTheme.Micro,
                KomTheme.TextLo, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-38f, 26f), new Vector2(300f, 16f));
        }

        void BuildToast(Transform root)
        {
            var panel = UIFactory.AddPanel(root, "Toast", KomTheme.Surface, 3);
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 244f), new Vector2(720f, 44f));
            UIFactory.AddFrame(panel.transform, "Border", KomTheme.Line, 1f);
            _toastGroup = panel.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.alpha = 0f;
            _toastGroup.blocksRaycasts = false;
            _toastText = UIFactory.AddText(panel.transform, "Text", "", KomTheme.Small, KomTheme.TextMid, TextAnchor.MiddleCenter);
            UIFactory.Stretch(_toastText.rectTransform, 12f, 4f, 12f, 4f);
        }

        void BuildCompletePanel(Transform root)
        {
            _completePanel = UIFactory.Rect(root, "CompletePanel").gameObject;
            UIFactory.Stretch((RectTransform)_completePanel.transform);
            var dim = UIFactory.AddImage(_completePanel.transform, "Dim", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0.82f));
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            var panel = UIFactory.AddPanel(_completePanel.transform, "Panel", new Color(0.043f, 0.047f, 0.051f, 0.99f), 3);
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(760f, 400f));
            UIFactory.AddFrame(panel.transform, "Border", KomTheme.Line, 1f);

            var kicker = UIFactory.AddLabel(panel.transform, "Kicker", KomLoc.T("Torneo del Bienestar", "Wellbeing Tournament"), TextAnchor.UpperCenter);
            UIFactory.Place(kicker.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -56f), new Vector2(600f, 20f));

            var title = UIFactory.AddDisplayText(panel.transform, "Title", KomLoc.T("TORNEO COMPLETADO", "TOURNAMENT COMPLETE"), 42,
                KomTheme.TextHi, TextAnchor.UpperCenter);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -84f), new Vector2(700f, 56f));

            var body = UIFactory.AddText(panel.transform, "Body",
                KomLoc.T("Todos los encuentros han sido disputados.\nEl destino del género ganador está por revelarse.",
                         "Every encounter has been fought.\nThe fate of the winning genre is about to be revealed."),
                KomTheme.Small, KomTheme.TextMid, TextAnchor.UpperCenter);
            UIFactory.Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -160f), new Vector2(620f, 60f));

            var final = UIFactory.AddPrimaryButton(panel.transform, "Final", KomLoc.T("VER FINAL  ·  ENTER", "SEE THE ENDING  ·  ENTER"),
                () => ScreenFader.Load(KomScenes.Ending), 22);
            UIFactory.Place(final.image.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 52f), new Vector2(320f, 60f));

            _completePanel.SetActive(_session.IsRunComplete);
            if (_session.IsRunComplete)
            {
                _fightButton.gameObject.SetActive(false);
                foreach (var n in _nodes) n.button.interactable = false;
            }
        }

        void Update()
        {
            if (_versus) { UpdateVersus(); return; }
            _pulse += Time.unscaledDeltaTime;
            if (_currentNodeUi != null)
            {
                float k = 1f + Mathf.Sin(_pulse * 2.4f) * 0.05f;
                _currentNodeUi.ring.rectTransform.localScale = Vector3.one * k;
                var glow = _currentNodeUi.glow.color;
                glow.a = 0.18f + Mathf.Sin(_pulse * 2.4f) * 0.06f;
                _currentNodeUi.glow.color = glow;
            }

            if (_session.IsRunComplete)
            {
                if (InputReader.ConfirmPressed()) ScreenFader.Load(KomScenes.Ending);
                return;
            }
            if (InputReader.ConfirmPressed()) StartCombat();
            if (InputReader.CancelPressed()) ScreenFader.Load(KomScenes.MainMenu);
        }

        // ------------------------------------------------------------------ versus local

        static readonly int[] VersusRoundWins = { 1, 2, 3 };

        void BuildVersusUI()
        {
            _versus = true;
            _versusP1 = new FighterControls(0, true);
            _versusP2 = new FighterControls(1, true);
            _versusRoundChips.Clear();
            BuildVersusStageEntries();

            UIFactory.DestroyCanvas(_canvas);
            _canvas = UIFactory.CreateCanvas("VersusMapCanvas", 90);
            var root = (RectTransform)_canvas.transform;

            var ink = UIFactory.AddImage(root, "Ink", UIFactory.WhiteSprite(), Color.black);
            UIFactory.Stretch(ink.rectTransform);

            var map = UIFactory.AddImage(root, "MapImage", UIFactory.LoadSprite("UI/Map/tournament_map"),
                new Color(0.9f, 0.9f, 0.9f, 1f));
            UIFactory.Stretch(map.rectTransform);

            var tint = UIFactory.AddImage(root, "Tint", UIFactory.WhiteSprite(), new Color(0f, 0f, 0f, 0.26f));
            UIFactory.Stretch(tint.rectTransform);

            UIFactory.AddVignette(root, 0.78f);

            var top = UIFactory.AddImage(root, "TopShade",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0.92f), new Color(0f, 0f, 0f, 0f)), Color.white);
            UIFactory.Place(top.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 220f));

            var bottom = UIFactory.AddImage(root, "BottomShade",
                UIFactory.VerticalGradient(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.96f)), Color.white);
            UIFactory.Place(bottom.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0f, 420f));

            UIFactory.AddGrain(root, 0.022f);

            BuildVersusHeader(root);
            BuildVersusPins(root);
            BuildVersusPanel(root);
            BuildVersusFooter(root);

            _versusStage = IndexOfVersusStage(_session.VersusStageId);
            _versusRounds = Mathf.Clamp(_session.VersusRoundsToWin - 1, 0, VersusRoundWins.Length - 1);
            RefreshVersus();
        }

        void BuildVersusStageEntries()
        {
            _versusStages.Clear();
            var nodes = KomData.Tournament.nodes;
            foreach (var node in nodes)
            {
                if (node == null) continue;
                var stage = KomData.GetStage(node.stageId);
                if (stage == null) continue;
                // El Zocalo de la gran final comparte recinto con el de la ruta: para
                // elegir escenario es el MISMO sitio, solo cambia la luz del capitulo.
                bool sameVenue = false;
                foreach (var other in _versusStages)
                    if (SameVenue(other.stage, stage)) { sameVenue = true; break; }
                if (sameVenue) continue;
                _versusStages.Add(new VersusStageEntry
                {
                    stage = stage,
                    mapX = node.mapX,
                    mapY = node.mapY
                });
            }
        }

        void BuildVersusHeader(Transform root)
        {
            var kicker = UIFactory.AddText(root, "Kicker", KomLoc.T("MODO LOCAL · DOS JUGADORES", "LOCAL MODE · TWO PLAYERS"),
                13, KomTheme.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(KomTheme.Margin, -42f), new Vector2(760f, 18f));

            var title = UIFactory.AddDisplayText(root, "Header", KomLoc.T("ELIGE ESCENARIO", "CHOOSE A STAGE"),
                46, KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(KomTheme.Margin - 4f, -62f), new Vector2(900f, 58f));

            var hint = UIFactory.AddText(root, "Hint",
                KomLoc.T("El mapa y las rondas los elige cualquiera de los dos",
                         "Either player chooses the stage and the rounds"),
                13, KomTheme.TextMid, TextAnchor.UpperRight);
            UIFactory.Place(hint.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-KomTheme.Margin, -50f), new Vector2(760f, 20f));

            var rule = UIFactory.AddHLine(root, "HeaderRule", KomTheme.Line, 1f);
            UIFactory.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -134f), new Vector2(-2f * KomTheme.Margin, 1f));
        }

        void BuildVersusPins(Transform root)
        {
            var layer = UIFactory.Rect(root, "VersusPins");
            UIFactory.Stretch(layer);

            for (int i = 0; i < _versusStages.Count; i++)
            {
                var entry = _versusStages[i];
                var holder = UIFactory.Rect(layer, "StagePin" + i);
                holder.anchorMin = holder.anchorMax = new Vector2(entry.mapX, entry.mapY);
                holder.pivot = new Vector2(0.5f, 0.5f);
                holder.anchoredPosition = Vector2.zero;
                holder.sizeDelta = new Vector2(270f, 116f);

                var glow = UIFactory.AddGlow(holder, "Glow", new Color(1f, 1f, 1f, 0.12f));
                UIFactory.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, VersusPinY), new Vector2(154f, 154f));

                var hit = UIFactory.AddImage(holder, "Hit", UIFactory.CircleSprite(), new Color(0f, 0f, 0f, 0.02f));
                UIFactory.Place(hit.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, VersusPinY), new Vector2(96f, 96f));
                hit.raycastTarget = true;
                var button = hit.gameObject.AddComponent<Button>();
                button.targetGraphic = hit;
                button.transition = Selectable.Transition.None;
                int captured = i;
                button.onClick.AddListener(() => SelectVersusStage(captured));

                var ring = UIFactory.AddImage(holder, "Ring", UIFactory.RingSprite(128), KomTheme.TextMid);
                UIFactory.Place(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, VersusPinY), new Vector2(76f, 76f));

                var core = UIFactory.AddImage(holder, "Core", UIFactory.WhiteSprite(), KomTheme.TextMid);
                UIFactory.Place(core.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, VersusPinY), new Vector2(9f, 9f));
                core.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

                var plate = UIFactory.AddPanel(holder, "TagPlate", new Color(0f, 0f, 0f, 0.82f), 2);
                UIFactory.Place(plate.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, 46f), new Vector2(44f, 20f));
                var tag = UIFactory.AddText(plate.transform, "Tag", (i + 1).ToString("00"), 10, KomTheme.TextLo,
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(tag.rectTransform, 6f, 1f, 6f, 1f);

                entry.pin = holder;
                entry.glow = glow;
                entry.ring = ring;
                entry.core = core;
                entry.tag = tag;
            }
        }

        void BuildVersusPanel(Transform root)
        {
            var panel = UIFactory.AddPanel(root, "VersusPanel", new Color(0.043f, 0.047f, 0.051f, 0.975f), 3);
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 36f), new Vector2(1728f, 260f));
            UIFactory.AddFrame(panel.transform, "Border", KomTheme.Line, 1f);

            var accent = UIFactory.AddImage(panel.transform, "Accent", UIFactory.WhiteSprite(), KomTheme.Accent);
            UIFactory.Place(accent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(3f, 0f));

            // --- J1
            var p1Card = UIFactory.AddImage(panel.transform, "P1Card",
                UIFactory.LoadSprite(_session.VersusPlayer1Def != null ? _session.VersusPlayer1Def.cardResource : null),
                Color.white);
            p1Card.preserveAspect = true;
            UIFactory.Place(p1Card.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(28f, 66f), new Vector2(96f, 128f));
            UIFactory.AddCardAura(p1Card, KomData.Accent(_session.VersusPlayer1Def), 1f);

            var p1Caption = UIFactory.AddText(panel.transform, "P1Caption", KomLoc.T("JUGADOR 1", "PLAYER 1"),
                10, KomTheme.TextLo, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(p1Caption.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(140f, 176f), new Vector2(300f, 16f));

            _versusP1Name = UIFactory.AddDisplayText(panel.transform, "P1Name",
                _session.VersusPlayer1Def != null ? _session.VersusPlayer1Def.displayName.ToUpperInvariant() : "J1",
                24, KomTheme.TextHi, TextAnchor.MiddleLeft);
            UIFactory.Place(_versusP1Name.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(138f, 148f), new Vector2(270f, 30f));

            var p1Genre = UIFactory.AddText(panel.transform, "P1Genre",
                _session.VersusPlayer1Def != null ? _session.VersusPlayer1Def.genre.ToUpperInvariant() : "",
                11, KomTheme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(p1Genre.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(140f, 126f), new Vector2(270f, 16f));

            _versusP1Device = UIFactory.AddText(panel.transform, "P1Device", "", 9, KomTheme.TextLo,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_versusP1Device.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(140f, 104f), new Vector2(270f, 14f));

            var divider1 = UIFactory.AddVLine(panel.transform, "Divider1", KomTheme.Line, 1f);
            UIFactory.Place(divider1.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(420f, 0f), new Vector2(1f, -52f));

            // --- escenario
            var kicker = UIFactory.AddText(panel.transform, "StageKicker",
                KomLoc.T("RECINTO SELECCIONADO", "SELECTED VENUE"), 10, KomTheme.Accent,
                TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(kicker.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(444f, 224f), new Vector2(500f, 16f));

            _versusStageName = UIFactory.AddDisplayText(panel.transform, "StageName", "—", 34,
                KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(_versusStageName.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(440f, 188f), new Vector2(980f, 44f));
            _versusStageName.resizeTextForBestFit = true;
            _versusStageName.resizeTextMinSize = 20;
            _versusStageName.resizeTextMaxSize = 34;

            _versusStageSubtitle = UIFactory.AddText(panel.transform, "StageSub", "", 13,
                KomTheme.TextMid, TextAnchor.UpperLeft);
            UIFactory.Place(_versusStageSubtitle.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(0f, 0f), new Vector2(444f, 162f), new Vector2(980f, 20f));

            _versusStageCounter = UIFactory.AddText(panel.transform, "StageCounter", "", 9,
                KomTheme.TextLo, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(_versusStageCounter.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(0f, 0f), new Vector2(444f, 146f), new Vector2(500f, 14f));

            var roundsKicker = UIFactory.AddText(panel.transform, "RoundsKicker", KomLoc.T("RONDAS", "ROUNDS"),
                10, KomTheme.TextLo, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(roundsKicker.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(444f, 120f), new Vector2(200f, 16f));

            string[] roundNames =
            {
                KomLoc.T("A UNA", "SINGLE"),
                KomLoc.T("AL MEJOR DE 3", "BEST OF 3"),
                KomLoc.T("AL MEJOR DE 5", "BEST OF 5")
            };
            for (int i = 0; i < roundNames.Length; i++)
            {
                var chip = UIFactory.AddFramedPanel(panel.transform, "RoundChip" + i, KomTheme.SurfaceLo,
                    KomTheme.Line, 3);
                UIFactory.Place(chip.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(444f + i * 180f, 74f), new Vector2(168f, 42f));
                var label = UIFactory.AddText(chip.transform, "Label", roundNames[i], 14,
                    KomTheme.TextMid, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(label.rectTransform, 8f, 3f, 8f, 3f);
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 9;
                label.resizeTextMaxSize = 14;

                var chipAccent = UIFactory.AddImage(chip.transform, "Accent", UIFactory.WhiteSprite(),
                    new Color(1f, 1f, 1f, 0f));
                UIFactory.Place(chipAccent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                    new Vector2(0f, 0.5f), Vector2.zero, new Vector2(3f, 0f));

                int captured = i;
                var hit = UIFactory.AddImage(chip.transform, "Hit", UIFactory.WhiteSprite(),
                    new Color(0f, 0f, 0f, 0f));
                UIFactory.Stretch(hit.rectTransform);
                hit.raycastTarget = true;
                var button = hit.gameObject.AddComponent<Button>();
                button.targetGraphic = hit;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => SelectVersusRounds(captured));

                _versusRoundChips.Add(new VersusRoundChip
                {
                    fill = chip,
                    frame = (RectTransform)chip.transform.Find("Border"),
                    label = label,
                    accent = chipAccent
                });
            }

            var fight = UIFactory.AddPrimaryButton(panel.transform, "Fight",
                KomLoc.T("PELEAR  ·  C / U", "FIGHT  ·  C / U"), StartVersusFight, 20, 320f, 52f);
            UIFactory.Place(fight.image.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(800f, 12f), new Vector2(320f, 52f));

            var divider2 = UIFactory.AddVLine(panel.transform, "Divider2", KomTheme.Line, 1f);
            UIFactory.Place(divider2.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(1430f, 0f), new Vector2(1f, -52f));

            // --- J2 (espejo de J1)
            var p2Card = UIFactory.AddImage(panel.transform, "P2Card",
                UIFactory.LoadSprite(_session.VersusPlayer2Def != null ? _session.VersusPlayer2Def.cardResource : null),
                Color.white);
            p2Card.preserveAspect = true;
            UIFactory.Place(p2Card.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-28f, 66f), new Vector2(96f, 128f));
            UIFactory.AddCardAura(p2Card, KomData.Accent(_session.VersusPlayer2Def), 1f);

            var p2Caption = UIFactory.AddText(panel.transform, "P2Caption", KomLoc.T("JUGADOR 2", "PLAYER 2"),
                10, KomTheme.TextLo, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(p2Caption.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-138f, 176f), new Vector2(170f, 16f));

            _versusP2Name = UIFactory.AddDisplayText(panel.transform, "P2Name",
                _session.VersusPlayer2Def != null ? _session.VersusPlayer2Def.displayName.ToUpperInvariant() : "J2",
                24, KomTheme.TextHi, TextAnchor.MiddleRight);
            UIFactory.Place(_versusP2Name.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-138f, 148f), new Vector2(170f, 30f));
            _versusP2Name.resizeTextForBestFit = true;
            _versusP2Name.resizeTextMinSize = 13;
            _versusP2Name.resizeTextMaxSize = 24;

            var p2Genre = UIFactory.AddText(panel.transform, "P2Genre",
                _session.VersusPlayer2Def != null ? _session.VersusPlayer2Def.genre.ToUpperInvariant() : "",
                11, KomTheme.Accent, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(p2Genre.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-138f, 126f), new Vector2(170f, 16f));

            _versusP2Device = UIFactory.AddText(panel.transform, "P2Device", "", 9, KomTheme.TextLo,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(_versusP2Device.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-138f, 104f), new Vector2(170f, 14f));
        }

        void BuildVersusFooter(Transform root)
        {
            var bar = UIFactory.Rect(root, "Footer");
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(0f, 32f);
            bar.anchoredPosition = Vector2.zero;

            var hint = UIFactory.AddText(bar, "Hint",
                KomLoc.T("← / →  ESCENARIO     ↑ / ↓  RONDAS     C o U  PELEAR     B u O  CAMBIAR LUCHADORES     ESC  MENÚ",
                         "← / →  STAGE     ↑ / ↓  ROUNDS     C or U  FIGHT     B or O  CHANGE FIGHTERS     ESC  MENU"),
                10, KomTheme.TextLo, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1700f, 16f));
        }

        int IndexOfVersusStage(string stageId)
        {
            for (int i = 0; i < _versusStages.Count; i++)
                if (_versusStages[i].stage.id == stageId) return i;
            return 0;
        }

        void SelectVersusStage(int index)
        {
            if (_versusStages.Count == 0) return;
            _versusStage = (index + _versusStages.Count) % _versusStages.Count;
            RefreshVersus();
        }

        void SelectVersusRounds(int index)
        {
            _versusRounds = (index + VersusRoundWins.Length) % VersusRoundWins.Length;
            RefreshVersus();
        }

        void RefreshVersus()
        {
            if (_versusStages.Count == 0) return;
            _versusStage = Mathf.Clamp(_versusStage, 0, _versusStages.Count - 1);
            _versusRounds = Mathf.Clamp(_versusRounds, 0, VersusRoundWins.Length - 1);

            var entry = _versusStages[_versusStage];
            if (_versusStageName != null) _versusStageName.text = entry.stage.displayName.ToUpperInvariant();
            if (_versusStageSubtitle != null) _versusStageSubtitle.text = entry.stage.subtitle;
            int wins = VersusRoundWins[_versusRounds];
            string matchEs = wins <= 1 ? "A UNA" : $"AL MEJOR DE {wins * 2 - 1}";
            string matchEn = wins <= 1 ? "SINGLE" : $"BEST OF {wins * 2 - 1}";
            if (_versusStageCounter != null)
                _versusStageCounter.text = KomLoc.T(
                    $"ESCENARIO {_versusStage + 1:00} / {_versusStages.Count:00}   ·   {matchEs}",
                    $"STAGE {_versusStage + 1:00} / {_versusStages.Count:00}   ·   {matchEn}");
            if (_versusP1Device != null) _versusP1Device.text = _versusP1.DeviceLabel;
            if (_versusP2Device != null) _versusP2Device.text = _versusP2.DeviceLabel;

            for (int i = 0; i < _versusStages.Count; i++)
            {
                var pin = _versusStages[i];
                bool on = i == _versusStage;
                if (pin.pin != null) pin.pin.localScale = Vector3.one;
                if (pin.ring != null) pin.ring.color = on ? KomTheme.AccentHot : new Color(0.55f, 0.56f, 0.55f, 0.65f);
                if (pin.core != null) pin.core.color = on ? KomTheme.AccentHot : new Color(0.55f, 0.56f, 0.55f, 0.65f);
                if (pin.glow != null) pin.glow.color = new Color(1f, 1f, 1f, on ? 0.24f : 0.04f);
                if (pin.tag != null)
                {
                    pin.tag.text = (i + 1).ToString("00");
                    pin.tag.color = on ? KomTheme.TextHi : KomTheme.TextLo;
                }
            }

            for (int i = 0; i < _versusRoundChips.Count; i++)
            {
                var chip = _versusRoundChips[i];
                bool on = i == _versusRounds;
                if (chip.fill != null) chip.fill.color = on ? KomTheme.SurfaceHi : KomTheme.SurfaceLo;
                if (chip.label != null) chip.label.color = on ? KomTheme.TextHi : KomTheme.TextMid;
                if (chip.accent != null)
                    chip.accent.color = on
                        ? KomTheme.AccentHot
                        : new Color(KomTheme.AccentHot.r, KomTheme.AccentHot.g, KomTheme.AccentHot.b, 0f);
                if (chip.frame != null)
                {
                    foreach (var img in chip.frame.GetComponentsInChildren<Image>(true))
                        img.color = on ? KomTheme.AccentHot : KomTheme.Line;
                }
            }
        }

        void UpdateVersus()
        {
            _versusPulse += Time.unscaledDeltaTime;
            if (_versusStages.Count > 0)
            {
                var selected = _versusStages[_versusStage];
                if (selected.pin != null)
                {
                    float k = 1f + Mathf.Sin(_versusPulse * 2.6f) * 0.05f;
                    selected.pin.localScale = Vector3.one * k;
                }
                if (selected.glow != null)
                {
                    var c = selected.glow.color;
                    c.a = 0.22f + Mathf.Sin(_versusPulse * 2.6f) * 0.06f;
                    selected.glow.color = c;
                }
            }

            if (_versusP1Device != null) _versusP1Device.text = _versusP1.DeviceLabel;
            if (_versusP2Device != null) _versusP2Device.text = _versusP2.DeviceLabel;

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                ScreenFader.Load(KomScenes.MainMenu);
                return;
            }

            // J u O: volver a la seleccion de luchadores sin perder lo elegido.
            if (_versusP1.MenuCancelPressed() || _versusP2.MenuCancelPressed())
            {
                ScreenFader.Load(KomScenes.CharacterSelect);
                return;
            }

            if (_versusP1.MoveLeftPressed() || _versusP2.MoveLeftPressed()) SelectVersusStage(_versusStage - 1);
            if (_versusP1.MoveRightPressed() || _versusP2.MoveRightPressed()) SelectVersusStage(_versusStage + 1);
            if (_versusP1.MenuUpPressed() || _versusP2.MenuUpPressed()) SelectVersusRounds(_versusRounds - 1);
            if (_versusP1.MenuDownPressed() || _versusP2.MenuDownPressed()) SelectVersusRounds(_versusRounds + 1);

            if (_versusP1.MenuConfirmPressed() || _versusP2.MenuConfirmPressed()) StartVersusFight();
        }

        void StartVersusFight()
        {
            if (_versusStages.Count == 0) return;
            _session.SetVersusStage(_versusStages[Mathf.Clamp(_versusStage, 0, _versusStages.Count - 1)].stage.id);
            _session.SetVersusRounds(VersusRoundWins[Mathf.Clamp(_versusRounds, 0, VersusRoundWins.Length - 1)]);
            ScreenFader.Load(KomScenes.Combat);
        }

        /// <summary>Preview del selector versus del mapa (solo editor).</summary>
        public void BuildVersusEditorPreview()
        {
            _session = GameSession.Ensure();
            if (!_session.VersusMode)
            {
                _session.BeginVersus();
                _session.SetVersusFighter(1, "juan_grabriel");
                _session.SetVersusFighter(2, "alex_llora");
            }
            BuildCamera();
            BuildVersusUI();
        }

#if UNITY_EDITOR
        public void DebugVersusChoose(int stageIndex, int roundIndex)
        {
            SelectVersusStage(stageIndex);
            SelectVersusRounds(roundIndex);
        }

        public void DebugVersusFight() => StartVersusFight();

        public void DebugVersusBack() => ScreenFader.Load(KomScenes.CharacterSelect);

        public string DebugVersusState =>
            _versusStages.Count == 0
                ? "versus no inicializado"
                : $"stage={_versusStages[_versusStage].stage.id} " +
                  $"rounds={VersusRoundWins[_versusRounds]} " +
                  $"p1={_versusP1.DeviceLabel} p2={_versusP2.DeviceLabel}";
#endif

        void OnNodeClicked(int index)
        {
            if (_session.IsRunComplete) return;
            int current = _session.EncounterIndex;
            bool finalNode = current >= KomData.Tournament.nodes.Length - 1;
            bool isCurrentVenue = index == current || (index == 0 && finalNode);
            if (!isCurrentVenue)
            {
                ShowToast(index < current
                    ? KomLoc.T("Ese encuentro ya está completado.", "That encounter is already complete.")
                    : KomLoc.T("Primero completa el encuentro marcado como SIGUIENTE.",
                               "First complete the encounter marked NEXT."));
                return;
            }
            StartCombat();
        }

        void StartCombat()
        {
            if (_session.IsRunComplete) return;

            // La presentacion del rival ocurre SIEMPRE antes de la pelea: si este
            // encuentro aun no se ha sorteado, el mapa manda a la ruleta y ella decide
            // cuando se entra al combate.
            string next = _session.IsEncounterRevealed ? KomScenes.Combat : KomScenes.CharacterSelect;

            // Antes del ultimo combate se presenta a la organizadora. PlayOnce evita que
            // se repita si el jugador vuelve al mapa despues de verlo.
            bool last = _session.EncounterIndex >= _session.NodeCount - 1;
            if (last && StoryFlow.PlayOnce("final", StoryFlow.Final, next)) return;

            ScreenFader.Load(next);
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
            yield return new WaitForSecondsRealtime(1.7f);
            float t = 0f;
            while (t < 0.3f)
            {
                t += Time.unscaledDeltaTime;
                _toastGroup.alpha = 1f - Mathf.Clamp01(t / 0.3f);
                yield return null;
            }
            _toastGroup.alpha = 0f;
            _toastRoutine = null;
        }
    }
}
