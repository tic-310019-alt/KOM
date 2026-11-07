#if UNITY_EDITOR || DEBUG
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KOM
{
    /// <summary>
    /// Menu de desarrollo oculto. Se abre y se cierra con F9.
    ///
    /// Existe para revisar combates y escenas sin jugarse el torneo entero: mata al rival,
    /// declara ganado el encuentro, pierde la ronda a proposito, llena la barra de super o
    /// salta a otra pantalla. No se dibuja hasta que se pulsa la tecla y no se compila en
    /// builds de release.
    ///
    /// Reglas que aplica:
    ///  - Al abrirse CONGELA el combate (no la pausa normal) para que la IA no golpee
    ///    mientras se elige una accion; al cerrarse lo reanuda.
    ///  - Cada accion cierra el menu ANTES de ejecutarse: el KO y el cartel de resultado
    ///    necesitan el Animator despierto y el controlador sin congelar.
    ///  - Los botones de combate se deshabilitan si no hay combate activo.
    ///  - Si cambia la escena, el menu se cierra solo: nunca queda un panel huerfano.
    /// </summary>
    public sealed class DevMenu : MonoBehaviour
    {
        const string DefaultPlayerId = "juan_grabriel";
        const Key ToggleKey = Key.F9;

        static DevMenu _instance;

        Canvas _canvas;
        GameObject _panel;
        bool _open;
        bool _keyHeld;
        CombatController _combat;
        Text _context;
        readonly List<Button> _combatButtons = new List<Button>();

        /// <summary>Estado del panel. Lo usan las comprobaciones del puente.</summary>
        public bool IsOpen => _open;

        /// <summary>Abre o cierra el panel sin pasar por el teclado (herramientas).</summary>
        public void Toggle() => SetOpen(!_open);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_instance != null) return;
            var go = new GameObject("DevMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<DevMenu>();
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            BuildUI();
            SetOpen(false);
        }

        void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
        void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_open) SetOpen(false);
            _combat = null;
        }

        void Update()
        {
            // Flanco manual en vez de wasPressedThisFrame: el estado del teclado puede
            // cambiar en un update de input distinto al del frame (pausas, inyeccion de
            // teclas desde el puente) y wasPressedThisFrame se perdia.
            var keyboard = Keyboard.current;
            bool held = keyboard != null && keyboard[ToggleKey].isPressed;
            if (held && !_keyHeld) SetOpen(!_open);
            _keyHeld = held;
        }

        // ------------------------------------------------------------------ estado

        void SetOpen(bool open)
        {
            _open = open;
            if (_panel != null) _panel.SetActive(open);
            if (open)
            {
                _combat = UnityEngine.Object.FindAnyObjectByType<CombatController>();
                if (_combat != null) _combat.SetDevFrozen(true);
                RefreshContext();
            }
            else if (_combat != null)
            {
                _combat.SetDevFrozen(false);
                _combat = null;
            }
        }

        void RefreshContext()
        {
            bool combat = _combat != null && _combat.DevActionsAvailable;
            for (int i = 0; i < _combatButtons.Count; i++)
                if (_combatButtons[i] != null) _combatButtons[i].interactable = combat;

            if (_context == null) return;
            if (!combat)
            {
                _context.text = "SIN COMBATE ACTIVO";
                _context.color = KomTheme.TextLo;
                return;
            }
            string rival = _combat.Enemy.Def != null ? _combat.Enemy.Def.displayName.ToUpperInvariant() : "RIVAL";
            _context.text = "COMBATE ACTIVO · " + rival;
            _context.color = KomTheme.Accent;
        }

        void RunCombat(Action<CombatController> action)
        {
            var combat = _combat;
            SetOpen(false);
            if (combat == null) return;
            // Cierra la pausa normal si estaba abierta: el KO necesita el Animator vivo.
            combat.DevForceResume();
            action(combat);
        }

        void RunScene(Action action)
        {
            SetOpen(false);
            action?.Invoke();
        }

        // ------------------------------------------------------------------ acciones

        void KillEnemy() => RunCombat(c => c.DevKillEnemy());

        void LoseRound() => RunCombat(c => c.DevKillPlayer());

        void WinEncounter() => RunCombat(c => c.DevWinEncounter());

        void FillSuper() => RunCombat(c => { if (c.Player != null) c.Player.FillSuperDebug(); });

        void GoMenu() => RunScene(() => ScreenFader.Load(KomScenes.MainMenu));

        void GoSelect() => RunScene(() => ScreenFader.Load(KomScenes.CharacterSelect));

        void GoMap() => RunScene(() =>
        {
            EnsureRun();
            ScreenFader.Load(KomScenes.Map);
        });

        void GoCombat() => RunScene(() =>
        {
            EnsureRun();
            ScreenFader.Load(KomScenes.Combat);
        });

        void RestartRun() => RunScene(() =>
        {
            GameSession.Ensure().ResetRun();
            ScreenFader.Load(KomScenes.CharacterSelect);
        });

        static void EnsureRun()
        {
            var session = GameSession.Ensure();
            if (session.HasRun && !session.IsRunComplete) return;
            session.ResetRun();
            session.BeginRun(DefaultPlayerId);
        }

        // ------------------------------------------------------------------ interfaz

        void BuildUI()
        {
            _canvas = UIFactory.CreateCanvas("DevCanvas", 4000);
            // El canvas nace como objeto raiz; el menu vive en DontDestroyOnLoad y sin
            // esto se quedaria sin panel al cambiar de escena.
            DontDestroyOnLoad(_canvas.gameObject);
            var root = (RectTransform)_canvas.transform;

            _panel = UIFactory.Rect(root, "DevPanel").gameObject;
            var panel = (RectTransform)_panel.transform;
            panel.anchorMin = new Vector2(1f, 0.5f);
            panel.anchorMax = new Vector2(1f, 0.5f);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.sizeDelta = new Vector2(560f, 772f);
            panel.anchoredPosition = new Vector2(-48f, 0f);

            // Alfa 1, no 0.985: en espacio lineal un 1.5 % de alfa deja pasar el 13 % del
            // fondo y el grabado del escenario se leia a traves del panel.
            var bg = UIFactory.AddPanel(panel, "BG", new Color(0.03f, 0.032f, 0.035f, 1f), 4);
            UIFactory.Stretch(bg.rectTransform);
            bg.raycastTarget = true;
            UIFactory.AddFrame(bg.transform, "Border", KomTheme.Line, 1f);

            var kicker = UIFactory.AddLabel(panel, "Kicker", "Herramientas", TextAnchor.UpperLeft);
            UIFactory.Place(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(32f, -26f), new Vector2(300f, 18f));

            var title = UIFactory.AddDisplayText(panel, "Title", "MODO DESARROLLO", 34, KomTheme.TextHi, TextAnchor.UpperLeft);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -46f), new Vector2(400f, 46f));

            var hint = UIFactory.AddText(panel, "Hint", "F9  ·  CERRAR", 12, KomTheme.TextLo, TextAnchor.UpperRight, FontStyle.Bold);
            UIFactory.Place(hint.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-32f, -30f), new Vector2(160f, 18f));

            _context = UIFactory.AddText(panel, "Context", "SIN COMBATE ACTIVO", 11, KomTheme.TextLo, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(_context.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(32f, -98f), new Vector2(490f, 18f));

            AddSection(panel, "COMBATE", -134f);
            AddCombatRow(panel, "MATAR AL RIVAL", -162f, KillEnemy);
            AddCombatRow(panel, "GANAR EL ENCUENTRO", -214f, WinEncounter);
            AddCombatRow(panel, "PERDER LA RONDA", -266f, LoseRound);
            AddCombatRow(panel, "SÚPER LLENA", -318f, FillSuper);

            AddSection(panel, "ESCENAS", -386f);
            AddSceneRow(panel, "MENÚ", 32f, -414f, GoMenu);
            AddSceneRow(panel, "SELECCIÓN", 288f, -414f, GoSelect);
            AddSceneRow(panel, "MAPA", 32f, -466f, GoMap);
            AddSceneRow(panel, "COMBATE", 288f, -466f, GoCombat);

            AddSection(panel, "PARTIDA", -534f);
            AddSceneRow(panel, "REINICIAR PARTIDA", 32f, -562f, RestartRun, 496f);

            var note = UIFactory.AddText(panel, "Note",
                "Solo en editor y builds de desarrollo. F9 oculta este panel.",
                11, KomTheme.TextLo, TextAnchor.LowerLeft);
            UIFactory.Place(note.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(32f, 22f), new Vector2(500f, 18f));

            _panel.SetActive(false);
        }

        void AddSection(Transform parent, string text, float y)
        {
            var head = UIFactory.AddLabel(parent, "Head_" + text, text, TextAnchor.UpperLeft);
            head.color = KomTheme.Accent;
            UIFactory.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(32f, y), new Vector2(400f, 16f));

            var rule = UIFactory.AddHLine(parent, "Rule_" + text, KomTheme.AccentDim, 1f);
            UIFactory.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(32f, y - 20f), new Vector2(34f, 1f));
        }

        void AddCombatRow(Transform parent, string label, float y, Action action)
        {
            var button = AddAction(parent, label, 32f, y, 496f, 44f, action);
            _combatButtons.Add(button);
        }

        void AddSceneRow(Transform parent, string label, float x, float y, Action action, float width = 240f)
        {
            AddAction(parent, label, x, y, width, 44f, action);
        }

        Button AddAction(Transform parent, string label, float x, float y, float width, float height, Action action)
        {
            // Boton propio con etiqueta en valor alto: el fantasma estandar deja el texto
            // en gris medio y sobre este panel se leia con esfuerzo.
            var button = UIFactory.AddButton(parent, "Btn_" + label, label,
                new Color(0.075f, 0.080f, 0.086f, 1f), KomTheme.TextHi, action, 16, 3);
            UIFactory.AddFrame(button.image.transform, "Border", KomTheme.Line, 1f);
            UIFactory.Place(button.image.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(width, height));
            return button;
        }
    }
}
#endif
