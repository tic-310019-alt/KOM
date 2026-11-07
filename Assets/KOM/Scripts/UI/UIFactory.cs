using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace KOM
{
    /// <summary>
    /// Fabrica de interfaz. Todo lo que se ve en pantalla pasa por aqui.
    ///
    /// DISENO DEL JUEGO: el mundo va en BLANCO Y NEGRO salvo los personajes, sus
    /// habilidades y sus auras. La INTERFAZ tambien va en gris. Por eso cada color plano
    /// se colapsa a su luminancia en <see cref="Mono"/>: es el unico punto de entrada, asi
    /// que no hay forma de colar croma en un panel por descuido. Las TEXTURAS (tarjetas,
    /// portada, logo) no se tocan: son los personajes y conservan su valor.
    ///
    /// Las dos unicas excepciones son las barras de vida y super del HUD de combate y el
    /// aura de las tarjetas de personaje (`AddCardAura`): las tres escriben su color
    /// directo sobre el Image porque ahi el tono es informacion (ver `KomTheme`).
    ///
    /// Los valores de color, medida y tipografia viven en <see cref="KomTheme"/>.
    /// </summary>
    public static class UIFactory
    {
        static Font _font;
        static Font _displayFont;
        static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

        static Sprite MakeSprite(Texture2D texture, Vector4 border = default)
        {
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        // Cuerpo: serif legible (Playfair Display Regular). Titulos: la italica de alto
        // contraste (Didone) que comparte lenguaje con el wordmark del logo.
        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.Load<Font>("UI/Fonts/PlayfairDisplay-Regular");
                    if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                return _font;
            }
        }

        public static Font DisplayFont
        {
            get
            {
                if (_displayFont == null)
                {
                    _displayFont = Resources.Load<Font>("UI/Fonts/PlayfairDisplay-Italic");
                    if (_displayFont == null) _displayFont = Font;
                }
                return _displayFont;
            }
        }

        public static void ResetCache()
        {
            SpriteCache.Clear();
        }

        public static Canvas CreateCanvas(string name, int sortOrder = 0)
        {
            // Un canvas con el mismo nombre es una construccion previa de ESTA pantalla:
            // se elimina antes de crear el nuevo para que reejecutar una preview no apile
            // dos interfaces una encima de otra.
            //
            // Solo se destruye si de verdad es un Canvas. El controlador que construye la
            // pantalla puede llamarse igual (CombatHud), y destruirlo mataba el HUD en el
            // mismo frame en que se creaba.
            var previous = GameObject.Find(name);
            if (previous != null && previous.GetComponent<Canvas>() != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(previous);
                else UnityEngine.Object.DestroyImmediate(previous);
            }

            ResetCache();
            EnsureEventSystem();
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        /// <summary>
        /// Destruye un canvas construido por codigo. Necesario para que
        /// <c>BuildEditorPreview</c> sea IDEMPOTENTE: el MenuItem de preview puede ejecutarse
        /// dos veces sobre la misma escena y sin esto se apilan dos interfaces.
        /// </summary>
        public static void DestroyCanvas(Canvas canvas)
        {
            if (canvas == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(canvas.gameObject);
            else UnityEngine.Object.DestroyImmediate(canvas.gameObject);
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(go);
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        // DISENO DEL JUEGO: todo el mundo va en BLANCO Y NEGRO salvo los personajes, sus
        // habilidades y sus auras. La INTERFAZ tambien va en gris.
        //
        // Este es el UNICO punto por el que pasan paneles, botones, textos, glows y
        // degradados, asi que aqui se colapsa cada color plano a su luminancia. Las
        // TEXTURAS (retratos de personaje, logo) no se tocan: son los personajes y
        // conservan su color, igual que en el combate.
        // Es idempotente: Mono(Mono(c)) == Mono(c), asi que se puede llamar sin miedo en
        // sitios que ya venian en gris.
        public static Color Mono(Color c)
        {
            float l = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
            return new Color(l, l, l, c.a);
        }

        // ------------------------------------------------------------------ imagenes

        public static Image AddImage(Transform parent, string name, Sprite sprite, Color color, Image.Type type = Image.Type.Simple)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = Mono(color);
            img.type = type;
            img.raycastTarget = false;
            return img;
        }

        public static Image AddPanel(Transform parent, string name, Color color, int radius = 6)
        {
            var img = AddImage(parent, name, RoundedSprite(radius), color, Image.Type.Sliced);
            img.pixelsPerUnitMultiplier = 1f;
            return img;
        }

        /// <summary>Panel con borde de 1 px: la forma base de casi toda la interfaz nueva.</summary>
        public static Image AddFramedPanel(Transform parent, string name, Color fill, Color line, int radius = 6)
        {
            var img = AddPanel(parent, name, fill, radius);
            AddFrame(img.transform, "Border", line, 1f);
            return img;
        }

        public static Image AddGlow(Transform parent, string name, Color color)
        {
            return AddImage(parent, name, GlowSprite(), color);
        }

        /// <summary>Linea horizontal de 1 px. El grosor real es el de la referencia 1920x1080.</summary>
        public static Image AddHLine(Transform parent, string name, Color color, float thickness = 1f)
        {
            var img = AddImage(parent, name, WhiteSprite(), color);
            img.rectTransform.sizeDelta = new Vector2(0f, thickness);
            return img;
        }

        public static Image AddVLine(Transform parent, string name, Color color, float thickness = 1f)
        {
            var img = AddImage(parent, name, WhiteSprite(), color);
            img.rectTransform.sizeDelta = new Vector2(thickness, 0f);
            return img;
        }

        /// <summary>Marco de 1 px que se ajusta al rect padre. Devuelve el contenedor.</summary>
        public static RectTransform AddFrame(Transform parent, string name, Color color, float thickness = 1f)
        {
            var root = Rect(parent, name);
            Stretch(root);
            var top = AddHLine(root, "Top", color, thickness);
            top.rectTransform.anchorMin = new Vector2(0f, 1f);
            top.rectTransform.anchorMax = new Vector2(1f, 1f);
            top.rectTransform.pivot = new Vector2(0.5f, 1f);
            top.rectTransform.anchoredPosition = Vector2.zero;

            var bottom = AddHLine(root, "Bottom", color, thickness);
            bottom.rectTransform.anchorMin = new Vector2(0f, 0f);
            bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottom.rectTransform.pivot = new Vector2(0.5f, 0f);
            bottom.rectTransform.anchoredPosition = Vector2.zero;

            var left = AddVLine(root, "Left", color, thickness);
            left.rectTransform.anchorMin = new Vector2(0f, 0f);
            left.rectTransform.anchorMax = new Vector2(0f, 1f);
            left.rectTransform.pivot = new Vector2(0f, 0.5f);
            left.rectTransform.anchoredPosition = Vector2.zero;

            var right = AddVLine(root, "Right", color, thickness);
            right.rectTransform.anchorMin = new Vector2(1f, 0f);
            right.rectTransform.anchorMax = new Vector2(1f, 1f);
            right.rectTransform.pivot = new Vector2(1f, 0.5f);
            right.rectTransform.anchoredPosition = Vector2.zero;
            return root;
        }

        /// <summary>
        /// Cuatro esquinas en L. Marca la tarjeta seleccionada sin encerrarla en un marco
        /// completo, que taparia el arte.
        /// </summary>
        public static RectTransform AddCornerBrackets(Transform parent, string name, Color color, float length = 26f, float thickness = 2f)
        {
            var root = Rect(parent, name);
            Stretch(root);
            for (int i = 0; i < 4; i++)
            {
                bool right = (i & 1) != 0;
                bool top = (i & 2) != 0;
                var anchor = new Vector2(right ? 1f : 0f, top ? 1f : 0f);

                var corner = Rect(root, "Corner" + i);
                corner.anchorMin = anchor;
                corner.anchorMax = anchor;
                corner.pivot = anchor;
                corner.anchoredPosition = Vector2.zero;
                corner.sizeDelta = new Vector2(length, length);

                var h = AddImage(corner, "H", WhiteSprite(), color);
                h.rectTransform.anchorMin = anchor;
                h.rectTransform.anchorMax = anchor;
                h.rectTransform.pivot = anchor;
                h.rectTransform.anchoredPosition = Vector2.zero;
                h.rectTransform.sizeDelta = new Vector2(length, thickness);

                var v = AddImage(corner, "V", WhiteSprite(), color);
                v.rectTransform.anchorMin = anchor;
                v.rectTransform.anchorMax = anchor;
                v.rectTransform.pivot = anchor;
                v.rectTransform.anchoredPosition = Vector2.zero;
                v.rectTransform.sizeDelta = new Vector2(thickness, length);
            }
            return root;
        }

        /// <summary>
        /// Rombo de 10 px: separador y bala de lista. Se rota 45 grados.
        /// </summary>
        public static Image AddDiamond(Transform parent, string name, Color color, float size = 9f)
        {
            var img = AddImage(parent, name, WhiteSprite(), color);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            return img;
        }

        // ------------------------------------------------------------------ aura de tarjeta
        //
        // Las tarjetas son ilustraciones A COLOR y, sobre el fondo negro, un PNG pegado
        // se lee como un recorte: no tiene peso ni pertenencia. El aura resuelve las dos
        // cosas de golpe —le da al personaje un halo de SU color y convierte el borde en
        // una pieza de la interfaz— con tres capas:
        //
        //   · RIM   un rectangulo redondeado 3 px mayor que la tarjeta, del color del
        //           personaje. Es el contorno: lo que hace que no parezca un PNG suelto.
        //   · GLOW  el resplandor radial ancho y suave, para que el color respire fuera.
        //   · EDGE  filete de 1 px sobre el borde mismo de la tarjeta, el mas brillante.
        //
        // Las tres van con el color de acento del personaje (`KomData.Accent`), que es el
        // que ya usan su aura de combate y su especial: la tarjeta y el luchador son la
        // misma identidad cromatica. Es la segunda excepcion declarada a la regla de
        // grises (ver KomTheme), y solo aplica a las tarjetas de personaje.
        public sealed class CardAura
        {
            /// <summary>Resplandor radial detrás de la tarjeta.</summary>
            public Image Glow;

            /// <summary>`strength` 0 = sin luz, 1 = tarjeta activa.</summary>
            public void Set(Color accent, float strength)
            {
                strength = Mathf.Clamp01(strength);
                if (Glow == null) return;
                // Solo la tarjeta activa lleva luz. Apagada se desactiva el objeto en vez
                // de dejarlo con alfa 0: siete resplandores transparentes siguen costando
                // su draw call.
                bool on = strength > 0.02f;
                if (Glow.enabled != on) Glow.enabled = on;
                if (on) Glow.color = new Color(accent.r, accent.g, accent.b, 0.28f * strength);
            }
        }

        /// <summary>
        /// Tamaño que ocupa un sprite con `preserveAspect` dentro de una caja. Se usa para
        /// que un marco o un aura abrace el DIBUJO y no el hueco: si la tarjeta se estira a
        /// una caja de otra proporción, quedan bandas vacías que el contorno delataría.
        /// </summary>
        public static Vector2 FitSize(Sprite sprite, Vector2 box)
        {
            if (sprite == null || box.x <= 0f || box.y <= 0f) return box;
            float ratio = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
            if (box.x / box.y > ratio) return new Vector2(box.y * ratio, box.y);
            return new Vector2(box.x, box.x / ratio);
        }

        /// <summary>
        /// Aura de tarjeta de personaje: un contorno del color del luchador.
        ///
        /// Se construye como HIJOS de la tarjeta y no como imágenes hermanas a propósito:
        /// un hijo puede dibujarse fuera del rect del padre, así el contorno funciona con
        /// cualquier anclaje (tarjetas estiradas incluidas) sin tener que calcular tamaños
        /// ni depender de que el layout ya haya corrido. El resplandor ancho es opcional y
        /// lo aporta quien ya tenía uno detrás de la tarjeta (`Glow`).
        /// </summary>
        public static CardAura AddCardAura(Image card, Color accent, float strength = 1f)
        {
            if (card == null) return null;
            var crt = card.rectTransform;

            // LUZ DIFUSA, sin marco. Un halo ceñido al borde (por suave que sea) siempre
            // se lee como un filete de color: la luz nace con fuerza justo donde acaba el
            // dibujo. Lo que hacia la version en blanco y negro —y lo que se busca aqui—
            // es lo contrario: un resplandor radial ANCHO y centrado detras de la tarjeta,
            // que se apaga en todas direcciones y deja que el color parezca salir de
            // dentro de la ficha. Sin lineas, sin contorno.
            var size = crt.rect.size;
            if (size.x < 1f || size.y < 1f) size = crt.sizeDelta;

            var glow = AddImage(crt.parent, card.name + "Aura", GlowSprite(), accent);
            var rt = glow.rectTransform;
            rt.anchorMin = crt.anchorMin;
            rt.anchorMax = crt.anchorMax;
            rt.pivot = crt.pivot;
            // El resplandor crece con la tarjeta pero con tope: en una ficha de 66x88
            // un halo de 2,2x se come el panel; en la tira grande, en cambio, necesita aire.
            var pad = new Vector2(Mathf.Min(size.x * 0.55f, 88f), Mathf.Min(size.y * 0.42f, 88f));
            rt.offsetMin = crt.offsetMin - pad;
            rt.offsetMax = crt.offsetMax + pad;
            rt.SetSiblingIndex(crt.GetSiblingIndex());

            var aura = new CardAura { Glow = glow };
            aura.Set(accent, strength);
            return aura;
        }

        // ------------------------------------------------------------------ texto

        public static Text AddText(Transform parent, string name, string text, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, name);
            var label = rt.gameObject.AddComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = size;
            label.color = Mono(color);
            label.alignment = alignment;
            label.fontStyle = style;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = true;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>
        /// Titular de pantalla: Didone italica con sombra suave. La sombra sustituye al
        /// contorno de 2.5 px, que engordaba las letras y las hacia parecer un cartel.
        /// </summary>
        public static Text AddDisplayText(Transform parent, string name, string text, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var label = AddText(parent, name, text, size, color, alignment, FontStyle.Normal);
            label.font = DisplayFont;
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(0f, -2f);
            shadow.useGraphicAlpha = true;
            return label;
        }

        /// <summary>Compatibilidad con las pantallas antiguas: titular con contorno.</summary>
        public static Text AddOutlinedText(Transform parent, string name, string text, int size, Color color,
            Color outlineColor, TextAnchor alignment = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Bold)
        {
            var label = AddText(parent, name, text, size, color, alignment, style);
            label.font = DisplayFont;
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = Mono(outlineColor);
            shadow.effectDistance = new Vector2(0f, -2f);
            shadow.useGraphicAlpha = true;
            return label;
        }

        /// <summary>
        /// Etiqueta de sistema: mayusculas, cuerpo regular y color bajo. Es el nivel mas
        /// bajo de la jerarquia y no debe competir nunca con un titular.
        /// </summary>
        public static Text AddLabel(Transform parent, string name, string text, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var label = AddText(parent, name, text != null ? text.ToUpperInvariant() : "", KomTheme.Label, KomTheme.TextLo, alignment, FontStyle.Bold);
            return label;
        }

        // ------------------------------------------------------------------ controles

        public static Button AddButton(Transform parent, string name, string text, Color background, Color foreground,
            Action onClick, int fontSize = 26, int radius = 6)
        {
            var img = AddPanel(parent, name, background, radius);
            img.raycastTarget = true;
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.None;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            var label = AddText(img.transform, "Label", text, fontSize, foreground, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.font = DisplayFont;
            Stretch(label.rectTransform, 10f, 4f, 10f, 4f);
            var fx = img.gameObject.AddComponent<KomButtonState>();
            fx.Bind(button, img, label, background);
            return button;
        }

        /// <summary>Boton primario: relleno casi blanco, texto oscuro. Una sola accion por pantalla.</summary>
        public static Button AddPrimaryButton(Transform parent, string name, string text, Action onClick, int fontSize = 24, float width = 360f, float height = 62f)
        {
            return AddButton(parent, name, text, KomTheme.Primary, KomTheme.TextOnLight, onClick, fontSize, 4);
        }

        /// <summary>Boton secundario: superficie con borde de 1 px.</summary>
        public static Button AddGhostButton(Transform parent, string name, string text, Action onClick, int fontSize = 21)
        {
            var img = AddPanel(parent, name, KomTheme.SurfaceLo, 4);
            img.raycastTarget = true;
            AddFrame(img.transform, "Border", KomTheme.Line, 1f);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.None;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            var label = AddText(img.transform, "Label", text, fontSize, KomTheme.TextMid, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.font = DisplayFont;
            Stretch(label.rectTransform, 12f, 4f, 12f, 4f);
            var fx = img.gameObject.AddComponent<KomButtonState>();
            fx.Bind(button, img, label, KomTheme.SurfaceLo);
            return button;
        }

        /// <summary>Tecla o boton de mando dibujado: el jugador no tiene que adivinar la tecla.</summary>
        public static RectTransform AddKeycap(Transform parent, string name, string key, float width = 0f)
        {
            float w = width > 0f ? width : Mathf.Max(34f, 16f + key.Length * 9f);
            var bg = AddPanel(parent, name, KomTheme.SurfaceHi, 3);
            bg.rectTransform.sizeDelta = new Vector2(w, 26f);
            AddFrame(bg.transform, "Border", KomTheme.Line, 1f);
            var label = AddText(bg.transform, "Key", key, 13, KomTheme.TextMid, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(label.rectTransform, 4f, 1f, 4f, 1f);
            return bg.rectTransform;
        }

        // ------------------------------------------------------------------ texturas procedurales

        public static Sprite LoadSprite(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return null;
            if (SpriteCache.TryGetValue(resourcePath, out var cached)) return cached;
            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite == null)
            {
                var tex = Resources.Load<Texture2D>(resourcePath);
                if (tex != null) sprite = MakeSprite(tex);
            }
            SpriteCache[resourcePath] = sprite;
            return sprite;
        }

        public static Sprite WhiteSprite()
        {
            if (SpriteCache.TryGetValue("__white", out var cached)) return cached;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply();
            var sprite = MakeSprite(tex);
            SpriteCache["__white"] = sprite;
            return sprite;
        }

        public static Sprite RoundedSprite(int radius = 22, int size = 64)
        {
            string key = "round_" + radius + "_" + size;
            if (SpriteCache.TryGetValue(key, out var cached)) return cached;
            radius = Mathf.Clamp(radius, 1, size / 2 - 1);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            float r = radius;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(r - (x + 0.5f), (x + 0.5f) - (size - r));
                    float dy = Mathf.Max(r - (y + 0.5f), (y + 0.5f) - (size - r));
                    float dist = Mathf.Sqrt(Mathf.Max(0f, dx) * Mathf.Max(0f, dx) + Mathf.Max(0f, dy) * Mathf.Max(0f, dy));
                    float alpha = Mathf.Clamp01(r - dist + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            var border = new Vector4(radius + 1, radius + 1, radius + 1, radius + 1);
            var sprite = MakeSprite(tex, border);
            SpriteCache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// Rectángulo redondeado con caída suave hacia afuera (9 rebanadas). Es el halo de
        /// las tarjetas: un `GlowSprite` radial NO sirve aquí porque su energía está en el
        /// centro, justo detrás del dibujo, y el borde visible queda casi transparente.
        /// Este sprite es opaco en el interior y se apaga en `feather` px hacia afuera, así
        /// que al ponerlo detrás de una tarjeta lo que se ve es una aureola pareja que
        /// sigue el contorno, no una mancha.
        /// </summary>
        public static Sprite SoftRectSprite(int radius = 10, int feather = 18, int size = 128)
        {
            string key = $"softrect_{radius}_{feather}_{size}";
            if (SpriteCache.TryGetValue(key, out var cached)) return cached;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float inner = half - feather;              // mitad del area solida
            float r = Mathf.Max(1f, radius);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f - half;
                    float py = y + 0.5f - half;
                    float qx = Mathf.Abs(px) - (inner - r);
                    float qy = Mathf.Abs(py) - (inner - r);
                    float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                               Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                    float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                    float a = 1f - Mathf.SmoothStep(0f, feather, d);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            var border = new Vector4(r + feather, r + feather, r + feather, r + feather);
            var sprite = MakeSprite(tex, border);
            SpriteCache[key] = sprite;
            return sprite;
        }

        public static Sprite GlowSprite(int size = 128)
        {
            string key = "glow_" + size;
            if (SpriteCache.TryGetValue(key, out var cached)) return cached;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float ny = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny));
                    float a = Mathf.Pow(d, 2.4f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            var sprite = MakeSprite(tex);
            SpriteCache[key] = sprite;
            return sprite;
        }

        public static Sprite RingSprite(int size = 96)
        {
            string key = "ring_" + size;
            if (SpriteCache.TryGetValue(key, out var cached)) return cached;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float ny = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float a = Mathf.Clamp01((0.94f - r) * 22f) * Mathf.Clamp01((r - 0.78f) * 22f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            var sprite = MakeSprite(tex);
            SpriteCache[key] = sprite;
            return sprite;
        }

        public static Sprite CircleSprite(int size = 128)
        {
            string key = "circle_" + size;
            if (SpriteCache.TryGetValue(key, out var cached)) return cached;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            float radius = size * 0.5f - 1f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - size * 0.5f;
                    float dy = y + 0.5f - size * 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - d + 0.75f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            var sprite = MakeSprite(tex);
            SpriteCache[key] = sprite;
            return sprite;
        }

        public static Sprite VerticalGradient(Color top, Color bottom, int height = 256)
        {
            top = Mono(top);
            bottom = Mono(bottom);
            var tex = new Texture2D(4, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < height; y++)
            {
                var c = Color.Lerp(bottom, top, y / (float)(height - 1));
                for (int x = 0; x < 4; x++) tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return MakeSprite(tex);
        }

        /// <summary>
        /// Blanco con alfa vertical: opaco abajo y transparente arriba. Es la piel de las
        /// barras de la onda del menu, que tienen que fundirse con el pie de la pantalla
        /// en vez de terminar en un corte recto.
        /// </summary>
        public static Sprite VerticalFadeSprite(float power = 1.45f, int height = 128)
        {
            string key = "vfade_" + power.ToString("0.00") + "_" + height;
            if (SpriteCache.TryGetValue(key, out var cached)) return cached;
            var tex = new Texture2D(4, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < height; y++)
            {
                float t = y / (float)(height - 1);          // 0 abajo, 1 arriba
                float a = Mathf.Pow(1f - t, Mathf.Max(0.05f, power));
                for (int x = 0; x < 4; x++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            var sprite = MakeSprite(tex);
            SpriteCache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// Degradado horizontal con caida exponencial. Es el velo que permite poner texto
        /// encima de la portada sin apagar la ilustracion: opaco en el borde, aire en el
        /// centro.
        /// </summary>
        public static Sprite HorizontalScrim(float strength = 0.96f, float curve = 1.6f, int width = 256)
        {
            string key = "hscrim_" + strength + "_" + curve + "_" + width;
            if (SpriteCache.TryGetValue(key, out var cached)) return cached;
            var tex = new Texture2D(width, 4, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int x = 0; x < width; x++)
            {
                float t = x / (float)(width - 1);          // 0 izquierda, 1 derecha
                float a = Mathf.Clamp01(strength * Mathf.Pow(1f - t, curve));
                for (int y = 0; y < 4; y++) tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
            }
            tex.Apply();
            var sprite = MakeSprite(tex);
            SpriteCache[key] = sprite;
            return sprite;
        }

        // Velo negro para poner texto encima de una ilustracion. A diferencia de
        // VerticalGradient la caida NO es lineal: con curve < 1 el negro se queda
        // concentrado abajo y el tercio alto de la imagen respira intacto. Es lo que
        // hace legible el pie de foto sin comerse el dibujo.
        public static Sprite ScrimSprite(float strength = 0.94f, float curve = 0.55f, int height = 256)
        {
            var tex = new Texture2D(4, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < height; y++)
            {
                float t = y / (float)(height - 1);            // 0 abajo, 1 arriba
                float a = Mathf.Clamp01(strength * Mathf.Pow(1f - t, curve));
                for (int x = 0; x < 4; x++) tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
            }
            tex.Apply();
            return MakeSprite(tex);
        }

        // Mancha de proteccion para el pie de foto: negro con caida radial parabolica.
        // Se estira como elipse detras del texto y su borde es tan suave que no se lee
        // como un panel; solo oscurece donde hay letras.
        public static Sprite WashSprite(int size = 192)
        {
            string key = "wash_" + size;
            if (SpriteCache.TryGetValue(key, out var cached)) return cached;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float ny = (y + 0.5f) / size * 2f - 1f;
                    float r2 = Mathf.Clamp01(nx * nx + ny * ny);
                    float a = Mathf.Pow(1f - r2, 1.35f);
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            var sprite = MakeSprite(tex);
            SpriteCache[key] = sprite;
            return sprite;
        }

        // Grano de pelicula: ruido gris con alfa constante. Se dibuja como RawImage con
        // uvRect desplazado a ~12 Hz; a baja opacidad da textura de copia fotografica.
        public static Sprite NoiseSprite(int size = 256, int seed = 20261101)
        {
            string key = "noise_" + size + "_" + seed;
            if (SpriteCache.TryGetValue(key, out var cached)) return cached;
            var rng = new System.Random(seed);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                float v = 0.32f + (float)rng.NextDouble() * 0.44f;
                byte b = (byte)(Mathf.Clamp01(v) * 255f);
                pixels[i] = new Color32(b, b, b, 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            var sprite = MakeSprite(tex);
            SpriteCache[key] = sprite;
            return sprite;
        }

        public static Image AddVignette(Transform parent, float strength = 0.72f)
        {
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                {
                    float nx = (x + 0.5f) / 256f * 2f - 1f;
                    float ny = (y + 0.5f) / 256f * 2f - 1f;
                    float d = Mathf.Sqrt(nx * nx * 0.8f + ny * ny * 0.55f);
                    float a = Mathf.SmoothStep(0.38f, 1.05f, d) * strength;
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
                }
            }
            tex.Apply();
            var sprite = MakeSprite(tex);
            var img = AddImage(parent, "Vignette", sprite, Color.white);
            Stretch(img.rectTransform);
            return img;
        }

        /// <summary>Grano animado a pantalla completa. Opacidad tipica 0.05.</summary>
        public static RawImage AddGrain(Transform parent, float alpha = 0.05f)
        {
            var rt = Rect(parent, "Grain");
            Stretch(rt);
            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.texture = NoiseSprite().texture;
            raw.color = new Color(1f, 1f, 1f, alpha);
            raw.raycastTarget = false;
            // Repeticion suficiente para que un texel ocupe ~1 px en 1920x1080: sin esto
            // el ruido se estira y se lee como manchas, no como grano.
            raw.uvRect = new Rect(0f, 0f, 8f, 4.5f);
            var anim = rt.gameObject.AddComponent<KomGrain>();
            anim.Bind(raw, 12f);
            return raw;
        }

        public static void AddTopBottomShade(Transform parent, float alpha = 0.8f)
        {
            var top = AddImage(parent, "TopShade", VerticalGradient(new Color(0f, 0f, 0f, alpha), new Color(0f, 0f, 0f, 0f)), Color.white);
            top.rectTransform.anchorMin = new Vector2(0f, 1f);
            top.rectTransform.anchorMax = new Vector2(1f, 1f);
            top.rectTransform.pivot = new Vector2(0.5f, 1f);
            top.rectTransform.sizeDelta = new Vector2(0f, 260f);
            top.rectTransform.anchoredPosition = Vector2.zero;

            var bottom = AddImage(parent, "BottomShade", VerticalGradient(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, alpha)), Color.white);
            bottom.rectTransform.anchorMin = new Vector2(0f, 0f);
            bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottom.rectTransform.pivot = new Vector2(0.5f, 0f);
            bottom.rectTransform.sizeDelta = new Vector2(0f, 300f);
            bottom.rectTransform.anchoredPosition = Vector2.zero;
        }
    }

    /// <summary>
    /// Estado visual de un boton sin depender de <see cref="Selectable.Transition"/>: el
    /// tinte por color no puede aclarar un relleno casi blanco ni oscurecer uno
    /// transparente. Aqui se interpola el color real del grafico.
    /// </summary>
    public sealed class KomButtonState : MonoBehaviour,
        UnityEngine.EventSystems.IPointerEnterHandler,
        UnityEngine.EventSystems.IPointerExitHandler,
        UnityEngine.EventSystems.IPointerDownHandler,
        UnityEngine.EventSystems.IPointerUpHandler
    {
        Button _button;
        Image _background;
        Text _label;
        Color _base;
        Color _baseLabel;
        float _hover;
        float _hoverTarget;
        float _press;

        public void Bind(Button button, Image background, Text label, Color baseColor)
        {
            _button = button;
            _background = background;
            _label = label;
            _base = baseColor;
            _baseLabel = label != null ? label.color : KomTheme.TextHi;
        }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => _hoverTarget = 1f;
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { _hoverTarget = 0f; _press = 0f; }
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) => _press = 1f;
        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e) => _press = 0f;

        void Update()
        {
            if (_background == null) return;
            float dt = Time.unscaledDeltaTime;

            // Un boton no interactuable tiene que VERSE no interactuable: el tinte de
            // Selectable no basta cuando el relleno es casi blanco.
            if (_button != null && !_button.interactable)
            {
                _hover = 0f;
                _press = 0f;
                _background.color = new Color(0.075f, 0.08f, 0.085f, 1f);
                if (_label != null) _label.color = new Color(KomTheme.TextLo.r, KomTheme.TextLo.g, KomTheme.TextLo.b, 0.75f);
                return;
            }

            _hover = Mathf.MoveTowards(_hover, _hoverTarget, dt * 8f);
            _press = Mathf.MoveTowards(_press, 0f, dt * 10f);

            bool light = _base.r > 0.5f;
            var target = light
                ? Color.Lerp(_base, Color.white, _hover * 0.6f - _press * 0.22f)
                : Color.Lerp(_base, KomTheme.SurfaceHi, _hover);
            _background.color = target;

            if (_label != null)
            {
                var labelTarget = light
                    ? _baseLabel
                    : Color.Lerp(_baseLabel, KomTheme.TextHi, _hover);
                _label.color = labelTarget;
            }
        }
    }
}
