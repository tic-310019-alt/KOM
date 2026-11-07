using UnityEngine;

namespace KOM
{
    /// <summary>
    /// Sistema de diseno de la interfaz.
    ///
    /// La direccion de arte del juego es BLANCO Y NEGRO: el mundo pierde el color en el
    /// material y solo los personajes, sus poderes y sus VFX lo conservan. La interfaz
    /// respeta esa regla, asi que aqui NO hay croma: la jerarquia se construye con
    /// VALOR (casi negro -> casi blanco), peso tipografico y lineas de 1 px.
    ///
    /// Reglas que este archivo hace cumplir:
    ///  - Un unico acento (plata calida) para lo accionable y lo seleccionado.
    ///  - Tres niveles de texto, no mas. Si algo necesita un cuarto, esta mal jerarquizado.
    ///  - Los paneles son superficies OPACAS con borde de 1 px: nunca vidrios translucidos
    ///    sobre la ilustracion, que ensucian la lectura.
    ///  - Todo se mide sobre 1920x1080 con un margen vivo de 96 px.
    ///
    /// LAS EXCEPCIONES DECLARADAS, y solo estas dos:
    ///  · Las barras de VIDA y SÚPER del HUD de combate (verde→rojo y azul). El color ahí
    ///    ES el dato —cuánto queda— y el jugador las lee de reojo sin apartar la vista de
    ///    la pelea. Viven en `CombatHud.HealthRamp` / `SuperRamp`.
    ///  · El AURA de las tarjetas de personaje (`UIFactory.AddCardAura`), del color de
    ///    cada luchador: es lo que hace que una ilustración a color se lea como una ficha
    ///    del juego y no como un PNG pegado sobre el fondo.
    /// Las dos escriben el color directo sobre el `Image`, sin pasar por
    /// <see cref="UIFactory.Mono"/>. Ninguna otra pieza de interfaz debe saltarse Mono.
    /// </summary>
    public static class KomTheme
    {
        // ------------------------------------------------------------------ superficies
        /// <summary>Fondo absoluto. Es el negro de la sala, no un gris.</summary>
        public static readonly Color Ink = new Color(0.016f, 0.018f, 0.021f, 1f);
        /// <summary>Fondo de zona (cabecera, pie, bandas).</summary>
        public static readonly Color InkSoft = new Color(0.031f, 0.035f, 0.039f, 1f);
        /// <summary>Superficie de panel principal.</summary>
        public static readonly Color Surface = new Color(0.055f, 0.060f, 0.065f, 1f);
        /// <summary>Superficie elevada (fila de boton, tarjeta).</summary>
        public static readonly Color SurfaceHi = new Color(0.086f, 0.091f, 0.096f, 1f);
        /// <summary>Superficie hundida (fondo de barra).</summary>
        public static readonly Color SurfaceLo = new Color(0.035f, 0.039f, 0.043f, 1f);

        // ------------------------------------------------------------------ lineas
        // MEDIDO: el proyecto esta en espacio de color LINEAL, asi que el mezclado de la
        // interfaz ocurre en lineal y un blanco con alfa 0.12 no se percibe como un 12 %
        // sino como un 39 % (alfa 0.12 -> lineal 0.12 -> sRGB 0.39). Por eso los velos
        // translucidos se declaran por VALOR PERCIBIDO y se convierten con White().
        // Las superficies OPACAS no necesitan conversion: se dibujan tal cual.
        public static readonly Color LineFaint = White(0.10f);
        public static readonly Color Line = White(0.18f);
        public static readonly Color LineStrong = White(0.36f);

        /// <summary>Blanco translucido con el alfa que produce el valor percibido pedido.</summary>
        public static Color White(float perceived)
        {
            float a = Mathf.Pow(Mathf.Clamp01(perceived), 2.2f);
            return new Color(1f, 1f, 1f, a);
        }

        // ------------------------------------------------------------------ texto
        /// <summary>Titulares y datos que el jugador debe leer primero.</summary>
        public static readonly Color TextHi = new Color(0.945f, 0.941f, 0.929f, 1f);
        /// <summary>Cuerpo y descripciones.</summary>
        public static readonly Color TextMid = new Color(0.694f, 0.702f, 0.694f, 1f);
        /// <summary>Etiquetas, metadatos, ayudas. Nunca por debajo de 0.42: no se lee.</summary>
        public static readonly Color TextLo = new Color(0.451f, 0.463f, 0.455f, 1f);
        /// <summary>Texto sobre relleno claro.</summary>
        public static readonly Color TextOnLight = new Color(0.043f, 0.047f, 0.051f, 1f);

        // ------------------------------------------------------------------ acento
        /// <summary>
        /// Acento unico de la interfaz: el oro del logo colapsado a luminancia. Se usa en
        /// filetes, subrayados y estados seleccionados; nunca en parrafos.
        /// </summary>
        public static readonly Color Accent = new Color(0.722f, 0.722f, 0.722f, 1f);
        public static readonly Color AccentHot = new Color(0.945f, 0.945f, 0.945f, 1f);
        public static readonly Color AccentDim = new Color(0.722f, 0.722f, 0.722f, 0.45f);
        /// <summary>Relleno del boton primario (casi blanco) y su hover.</summary>
        public static readonly Color Primary = new Color(0.898f, 0.894f, 0.878f, 1f);
        public static readonly Color PrimaryHot = new Color(1f, 0.996f, 0.980f, 1f);

        /// <summary>Relleno solido de una fila en reposo (evita depender del alfa).</summary>
        public static readonly Color RowIdle = new Color(0.055f, 0.058f, 0.062f, 1f);
        /// <summary>Relleno solido de una fila bajo el foco.</summary>
        public static readonly Color RowFocus = new Color(0.098f, 0.102f, 0.106f, 1f);

        // ------------------------------------------------------------------ medidas
        public const float Margin = 96f;
        public const float Gutter = 32f;
        public const float HeaderH = 132f;
        public const float FooterH = 92f;

        // ------------------------------------------------------------------ tipografia
        public const int DisplayXL = 128;
        public const int DisplayL = 84;
        public const int DisplayM = 56;
        public const int DisplayS = 38;
        public const int Title = 27;
        public const int Body = 20;
        public const int Small = 17;
        public const int Label = 14;
        public const int Micro = 12;

        /// <summary>Opacidad de un elemento deshabilitado. Un solo valor en todo el juego.</summary>
        public const float DisabledAlpha = 0.34f;

        /// <summary>
        /// Convierte un color de personaje/escenario al gris de interfaz. Se mantiene aqui
        /// (y no solo en UIFactory) porque los controladores lo llaman para los acentos.
        /// </summary>
        public static Color Mono(Color c)
        {
            float l = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
            return new Color(l, l, l, c.a);
        }

        /// <summary>Acento de interfaz de un personaje, ya en gris y con un suelo de valor.</summary>
        public static Color AccentFor(CharacterDef def)
        {
            var c = Mono(KomData.Accent(def));
            // Un acento demasiado oscuro no se distingue del panel: se eleva al 0.62.
            float l = Mathf.Max(c.r, 0.62f);
            return new Color(l, l, l, 1f);
        }
    }
}
