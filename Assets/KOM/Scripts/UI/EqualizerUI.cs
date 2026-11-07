using UnityEngine;
using UnityEngine.UI;

namespace KOM
{
    /// <summary>
    /// Onda de audio dibujada con barras de interfaz.
    ///
    /// NO lee el espectro del tema. La musica del juego tiene dinamica irregular y las
    /// barras saltaban sin ritmo: en una portada eso se lee como ruido, no como musica.
    /// Aqui la onda es una composicion determinista de tres senos de baja frecuencia que
    /// avanza despacio y siempre en la misma direccion, asi que se percibe como textura
    /// viva y no como un ecualizador de fiesta.
    ///
    /// Cada barra lleva el degradado vertical horneado en el sprite: opaca en la base y
    /// transparente en la punta. La onda se funde con el pie de la pantalla en lugar de
    /// terminar en un borde duro. La interfaz es BLANCO Y NEGRO, asi que la energia se
    /// expresa con ALTURA y VALOR, nunca con croma.
    /// </summary>
    public sealed class EqualizerUI : MonoBehaviour
    {
        RectTransform _rt;
        Image[] _bars;
        float[] _value;
        Sprite _barSprite;
        Color _color;
        float _alpha = 0.46f;
        float _time;

        /// <summary>Altura maxima de una barra, en pixeles de la referencia 1920x1080.</summary>
        public float barMax = 74f;
        /// <summary>Altura de reposo: las barras nunca desaparecen del todo.</summary>
        public float barMin = 2f;
        /// <summary>Velocidad de asentamiento (mas alto = responde antes).</summary>
        public float response = 3.2f;
        /// <summary>Velocidad de la onda, en radianes por segundo.</summary>
        public float speed = 1f;

        /// <summary>Energia media 0..1. La usa el menu para latir con la onda.</summary>
        public float Level { get; private set; }

        public void Build(int count, Color color, float alphaScale = 0.46f)
        {
            _rt = (RectTransform)transform;
            count = Mathf.Max(8, count);
            _bars = new Image[count];
            _value = new float[count];
            _color = UIFactory.Mono(color);
            _alpha = alphaScale;
            _barSprite = UIFactory.VerticalFadeSprite();

            for (int i = 0; i < count; i++)
            {
                _bars[i] = MakeBar("Bar" + i);
                // Estado inicial con forma de onda: sin esto la primera imagen (y la
                // preview del editor, que no ejecuta Update) saldria plana.
                _value[i] = Shape(i, count, 0f);
            }

            ApplyLayout();
        }

        Image MakeBar(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            var img = go.GetComponent<Image>();
            img.sprite = _barSprite;
            img.type = Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        void Update()
        {
            if (_bars == null || _rt == null || _bars.Length == 0) return;

            float dt = Time.unscaledDeltaTime;
            _time += dt * speed;
            float k = 1f - Mathf.Exp(-response * dt);
            int count = _bars.Length;

            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                float target = Shape(i, count, _time);
                _value[i] = Mathf.Lerp(_value[i], target, k);
                sum += _value[i];
            }
            Level = Mathf.Clamp01(sum / count);

            ApplyLayout();
        }

        /// <summary>
        /// El canvas se construye antes de que el CanvasScaler aplique la referencia: en
        /// ese primer frame el ancho del rect es el de la pantalla en pixeles. Sin este
        /// recálculo, la preview del editor (que no ejecuta Update) repartia las barras
        /// sobre el ancho equivocado y la mitad quedaba fuera del cuadro.
        /// </summary>
        void OnRectTransformDimensionsChange()
        {
            if (_bars != null && _bars.Length > 0) ApplyLayout();
        }

        void ApplyLayout()
        {
            int count = _bars.Length;
            float width = LayoutWidth();
            if (width <= 1f) width = 1920f;

            float step = width / count;
            // Barra fina: con 96 trazos la onda se lee como una trama, no como un grafico.
            float barWidth = Mathf.Max(2f, step * 0.17f);
            float left = -width * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float v = Mathf.Clamp01(_value[i]);
                var rt = _bars[i].rectTransform;
                rt.sizeDelta = new Vector2(barWidth, barMin + v * barMax);
                rt.anchoredPosition = new Vector2(left + i * step + (step - barWidth) * 0.5f, 0f);

                var c = _color;
                c.a = _alpha * (0.42f + 0.58f * v);
                _bars[i].color = c;
            }
        }

        /// <summary>
        /// Ancho del rect en unidades de referencia. El CanvasScaler aplica su factor un
        /// frame despues de construir la interfaz: en ese primer frame <c>rect.width</c>
        /// vale el ancho de la PANTALLA en pixeles (2560 en la Game View del editor) y la
        /// onda se repartia sobre 2560 px dentro de un canvas de 1920. La preview no
        /// ejecuta Update, asi que el error se quedaba congelado en la captura.
        /// </summary>
        float LayoutWidth()
        {
            var canvas = GetComponentInParent<Canvas>();
            var scaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
            if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                && scaler.referenceResolution.x > 1f)
            {
                // Si el rect ya mide lo que mide la PANTALLA en pixeles, el CanvasScaler
                // todavia no ha aplicado su factor: se usa la referencia. En cuanto el
                // rect pasa a unidades de referencia (o el canvas se ensancha en 21:9),
                // manda el rect real.
                float rect = _rt.rect.width;
                if (rect > 1f && !Mathf.Approximately(rect, Screen.width)) return rect;

                float span = _rt.anchorMax.x - _rt.anchorMin.x;
                return scaler.referenceResolution.x * span + _rt.sizeDelta.x;
            }
            return _rt.rect.width;
        }

        /// <summary>
        /// Tres senos de frecuencia espacial distinta y deriva lenta. La suma se queda en
        /// una banda estrecha de alturas: ni barras muertas ni picos que rompan el borde.
        /// </summary>
        float Shape(int i, int count, float t)
        {
            float u = count <= 1 ? 0f : i / (float)(count - 1);
            float a = Mathf.Sin(u * 8.6f + t * 0.85f);
            float b = Mathf.Sin(u * 19.4f - t * 0.52f + 1.9f);
            float c = Mathf.Sin(u * 3.2f + t * 0.31f + 0.6f);
            float v = 0.46f + 0.26f * a + 0.16f * b + 0.10f * c;

            // Los extremos se apagan: la onda entra y sale del cuadro, no se corta.
            float edge = Mathf.Clamp01(Mathf.Min(u, 1f - u) * 18f);
            return Mathf.Clamp01((0.10f + v * 0.86f) * Mathf.Lerp(0.22f, 1f, edge));
        }
    }
}
