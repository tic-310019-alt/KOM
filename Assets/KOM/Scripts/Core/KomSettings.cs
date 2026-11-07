using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KOM
{
    /// <summary>
    /// Ajustes de pantalla y gráficos, persistidos en PlayerPrefs.
    ///
    /// Qué se guarda y por qué:
    ///  - Pantalla completa: se aplica como <see cref="FullScreenMode.FullScreenWindow"/>
    ///    (sin bordes) y no como exclusiva. Alt-Tab en exclusiva provoca un cambio de modo
    ///    del monitor de ~1 s en Windows, y este juego se juega en sesiones cortas.
    ///  - Resolución: SOLO manda en ventana. En pantalla completa se usa siempre la
    ///    resolución nativa del monitor, que es lo único que mantiene la imagen y la
    ///    interfaz a 1:1. Aplicar aquí una resolución de ventana guardada (1280x720, por
    ///    ejemplo) hacía que el juego renderizara a 720p y el monitor lo estirara: todo
    ///    —texto incluido— se veía borroso. La fila se muestra como NATIVA y no se deja
    ///    ciclar en pantalla completa porque no hay nada que elegir.
    ///  - Calidad: índice de QualitySettings. El proyecto trae dos niveles reales
    ///    ("Mobile" y "PC") con pipelines URP distintos —MSAA, HDR, sombras y SSAO—, no
    ///    un cambio cosmético. El nivel "Mobile" estuvo excluido de Standalone, así que
    ///    en el build solo existía un nivel y el ciclo no hacía nada; por eso los ajustes
    ///    parecían decorativos.
    ///
    /// Al arrancar se aplica SIEMPRE lo guardado, y la primera vez se abre a pantalla
    /// completa en la resolución nativa: es un juego de pelea y es la única forma de que
    /// la primera impresión no sea una ventana de 720p estirada.
    /// </summary>
    public static class KomSettings
    {
        const string KeyFullscreen = "kom.display.fullscreen";
        const string KeyWidth = "kom.display.width";
        const string KeyHeight = "kom.display.height";
        const string KeyQuality = "kom.display.quality";

        static bool _loaded;
        static bool _fullscreen;
        static Vector2Int _window;
        static int _windowIndex;
        static int _quality;

        static readonly List<Vector2Int> _resolutions = new List<Vector2Int>();
        static Vector2Int _native;

        // ------------------------------------------------------------------ estado

        public static bool Fullscreen
        {
            get { EnsureLoaded(); return _fullscreen; }
        }

        public static int ResolutionCount
        {
            get { EnsureLoaded(); return _resolutions.Count; }
        }

        /// <summary>Tamaño de ventana elegido. En pantalla completa no se aplica.</summary>
        public static Vector2Int Resolution
        {
            get
            {
                EnsureLoaded();
                return _window.x > 0 && _window.y > 0
                    ? _window
                    : new Vector2Int(Screen.width, Screen.height);
            }
        }

        public static int ResolutionIndex
        {
            get { EnsureLoaded(); return _windowIndex; }
        }

        public static int QualityIndex
        {
            get { EnsureLoaded(); return _quality; }
        }

        public static int QualityCount => Mathf.Max(1, QualitySettings.names.Length);

        /// <summary>Resolución nativa del monitor: lo que se aplica en pantalla completa.</summary>
        public static Vector2Int NativeResolution
        {
            get { EnsureLoaded(); return _native; }
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            RefreshNative();
            BuildResolutions();

            // Primera vez: pantalla completa. Un juego de pelea se juega así, y sobre todo
            // evita heredar la ventana de 1280x720 que Unity guarda en el registro.
            _fullscreen = !PlayerPrefs.HasKey(KeyFullscreen)
                || PlayerPrefs.GetInt(KeyFullscreen) == 1;

            int w = PlayerPrefs.GetInt(KeyWidth, 0);
            int h = PlayerPrefs.GetInt(KeyHeight, 0);
            if (w <= 0 || h <= 0)
            {
                w = Mathf.Min(_native.x, 1920);
                h = Mathf.Min(_native.y, 1080);
            }
            _window = ClampToDesktop(new Vector2Int(w, h));
            _windowIndex = ClosestIndex(_window);

            _quality = PlayerPrefs.HasKey(KeyQuality)
                ? Mathf.Clamp(PlayerPrefs.GetInt(KeyQuality), 0, QualityCount - 1)
                : Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, QualityCount - 1);
        }

        // ------------------------------------------------------------------ arranque

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            EnsureLoaded();
            ApplyQuality();
            ApplyDisplay();
            // Una sola linea de diagnostico: es lo primero que hay que mirar si alguien
            // reporta que el juego se ve blando (resolucion aplicada vs. nativa).
            Debug.Log($"[KOM] Pantalla {ResolutionLabel()} "
                + (_fullscreen ? "pantalla completa" : "ventana")
                + $" · nativa {_native.x}x{_native.y} · calidad {QualityLabel(_quality)}"
                + $" ({QualityCount} niveles) · AA {PreferredAntialiasing()}");
        }

        // ------------------------------------------------------------------ mutadores

        public static void SetFullscreen(bool on)
        {
            EnsureLoaded();
            if (_fullscreen == on) return;
            _fullscreen = on;
            PlayerPrefs.SetInt(KeyFullscreen, on ? 1 : 0);
            PlayerPrefs.Save();
            if (!on)
            {
                // Al volver a ventana, el tamaño guardado no puede exceder el escritorio.
                _window = ClampToDesktop(_window);
                _windowIndex = ClosestIndex(_window);
            }
            ApplyDisplay();
        }

        public static void ToggleFullscreen()
        {
            SetFullscreen(!Fullscreen);
        }

        public static void CycleResolution(int direction)
        {
            EnsureLoaded();
            if (_resolutions.Count == 0) return;
            _windowIndex = (_windowIndex + direction + _resolutions.Count) % _resolutions.Count;
            _window = _resolutions[_windowIndex];
            PlayerPrefs.SetInt(KeyWidth, _window.x);
            PlayerPrefs.SetInt(KeyHeight, _window.y);
            PlayerPrefs.Save();
            ApplyDisplay();
        }

        /// <summary>
        /// Adopta el tamaño real de la ventana si el jugador la redimensionó a mano. Sin
        /// esto la fila seguiría mostrando el tamaño anterior y el siguiente ciclo daría
        /// un salto inesperado.
        /// </summary>
        public static void SyncWindow()
        {
            EnsureLoaded();
            if (_fullscreen) return;
            var actual = new Vector2Int(Screen.width, Screen.height);
            if (actual.x <= 0 || actual.y <= 0) return;
            if (actual == _window) return;
            _window = actual;
            _windowIndex = ClosestIndex(actual);
        }

        public static void SetQuality(int index)
        {
            EnsureLoaded();
            index = Mathf.Clamp(index, 0, QualityCount - 1);
            if (_quality == index) return;
            _quality = index;
            PlayerPrefs.SetInt(KeyQuality, index);
            PlayerPrefs.Save();
            ApplyQuality();
        }

        public static void CycleQuality(int direction)
        {
            SetQuality((QualityIndex + direction + QualityCount) % QualityCount);
        }

        static void ApplyDisplay()
        {
            RefreshNative();
            if (_fullscreen)
            {
                // Pantalla completa = resolución del monitor. Cualquier otra cosa la
                // escala el driver y se ve exactamente igual que una imagen de 720p.
                Screen.SetResolution(_native.x, _native.y, FullScreenMode.FullScreenWindow);
            }
            else
            {
                var r = Resolution;
                Screen.SetResolution(r.x, r.y, FullScreenMode.Windowed);
            }
        }

        static void ApplyQuality()
        {
            // applyExpensiveChanges: cambia también el pipeline URP, no solo los ajustes
            // baratos. Sin esto el nivel de calidad no toca MSAA ni las sombras.
            QualitySettings.SetQualityLevel(_quality, true);
            ApplyAntialiasing();
        }

        /// <summary>
        /// El antialiasing de cámara tiene que ser coherente con el del pipeline: FXAA
        /// sobre MSAA emborrona la imagen y no aporta nada. El proyecto usaba FXAA siempre,
        /// que es parte de por qué todo se veía blando.
        /// </summary>
        public static AntialiasingMode PreferredAntialiasing()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            int msaa = urp != null ? urp.msaaSampleCount : 1;
            return msaa > 1
                ? AntialiasingMode.None
                : AntialiasingMode.FastApproximateAntialiasing;
        }

        static void ApplyAntialiasing()
        {
            var mode = PreferredAntialiasing();
            var cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                var cam = cameras[i];
                if (cam == null) continue;
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null) data.antialiasing = mode;
            }
        }

        // ------------------------------------------------------------------ etiquetas

        public static string ResolutionLabel()
        {
            var r = Fullscreen ? NativeResolution : Resolution;
            return $"{r.x} × {r.y}";
        }

        /// <summary>
        /// Nombre del nivel de calidad para el jugador. Los assets se llaman "Mobile" y
        /// "PC" (nombres internos del proyecto); lo que se enseña es lo que hacen.
        /// </summary>
        public static string QualityLabel(int index)
        {
            var names = QualitySettings.names;
            if (names == null || names.Length == 0) return KomLoc.T("CALIDAD", "QUALITY");
            string name = names[Mathf.Clamp(index, 0, names.Length - 1)];
            switch (name)
            {
                case "Mobile": return KomLoc.T("RENDIMIENTO", "PERFORMANCE");
                case "PC": return KomLoc.T("CALIDAD", "QUALITY");
                default: return name.ToUpperInvariant();
            }
        }

        public static string FullscreenLabel()
        {
            return Fullscreen ? KomLoc.T("SÍ", "ON") : KomLoc.T("NO", "OFF");
        }

        // ------------------------------------------------------------------ resoluciones

        static void RefreshNative()
        {
            // Display.main.systemWidth es la resolución del escritorio, no la del modo
            // actual: leerla así evita que la nativa se "contamine" con una ventana pequeña.
            int w = Display.main.systemWidth;
            int h = Display.main.systemHeight;
            if (w <= 0 || h <= 0)
            {
                w = Screen.currentResolution.width;
                h = Screen.currentResolution.height;
            }
            if (w <= 0 || h <= 0)
            {
                w = Screen.width;
                h = Screen.height;
            }
            _native = new Vector2Int(w, h);
        }

        static void BuildResolutions()
        {
            _resolutions.Clear();
            var seen = new HashSet<long>();

            foreach (var r in Screen.resolutions)
            {
                // Por debajo de 1024x576 la interfaz (medida a 1920x1080) deja de leerse.
                if (r.width < 1024 || r.height < 576) continue;
                long key = ((long)r.width << 32) | (uint)r.height;
                if (!seen.Add(key)) continue;
                _resolutions.Add(new Vector2Int(r.width, r.height));
            }

            // Algunos drivers no informan ninguna resolución (escritorio remoto, VM).
            if (_resolutions.Count == 0)
            {
                _resolutions.Add(new Vector2Int(1280, 720));
                _resolutions.Add(new Vector2Int(1600, 900));
                _resolutions.Add(new Vector2Int(1920, 1080));
                _resolutions.Add(new Vector2Int(2560, 1440));
            }

            _resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        }

        static Vector2Int ClampToDesktop(Vector2Int size)
        {
            return new Vector2Int(
                Mathf.Clamp(size.x, 1024, Mathf.Max(1024, _native.x)),
                Mathf.Clamp(size.y, 576, Mathf.Max(576, _native.y)));
        }

        static int ClosestIndex(Vector2Int size)
        {
            int best = 0;
            long bestDelta = long.MaxValue;
            for (int i = 0; i < _resolutions.Count; i++)
            {
                long dw = _resolutions[i].x - size.x;
                long dh = _resolutions[i].y - size.y;
                long delta = dw * dw + dh * dh;
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = i;
                }
            }
            return best;
        }
    }
}
