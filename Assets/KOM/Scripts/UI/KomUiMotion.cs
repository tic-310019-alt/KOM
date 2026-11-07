using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace KOM
{
    /// <summary>
    /// Curvas y rutinas de animacion de interfaz.
    ///
    /// Todo se mueve con <c>Time.unscaledDeltaTime</c>: la interfaz no puede congelarse
    /// cuando el juego pausa o cuando un hitstop baja el tiempo. Las duraciones viven aqui
    /// para que las cinco pantallas se muevan con el mismo ritmo.
    /// </summary>
    public static class KomUiMotion
    {
        public const float Fast = 0.14f;
        public const float Normal = 0.22f;
        public const float Slow = 0.38f;
        public const float Entrance = 0.5f;

        public static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float k = 1f - t;
            return 1f - k * k * k;
        }

        public static float EaseOutQuint(float t)
        {
            t = Mathf.Clamp01(t);
            float k = 1f - t;
            return 1f - k * k * k * k * k;
        }

        public static float EaseInOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        public static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        public static IEnumerator Fade(CanvasGroup group, float from, float to, float duration)
        {
            if (group == null) yield break;
            if (duration <= 0f)
            {
                group.alpha = to;
                yield break;
            }
            float t = 0f;
            group.alpha = from;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, EaseOutCubic(t / duration));
                yield return null;
            }
            group.alpha = to;
        }

        public static IEnumerator Move(RectTransform rt, Vector2 from, Vector2 to, float duration, Func<float, float> ease = null)
        {
            if (rt == null) yield break;
            ease ??= EaseOutCubic;
            if (duration <= 0f)
            {
                rt.anchoredPosition = to;
                yield break;
            }
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                rt.anchoredPosition = Vector2.LerpUnclamped(from, to, ease(t / duration));
                yield return null;
            }
            rt.anchoredPosition = to;
        }

        public static IEnumerator Scale(RectTransform rt, float from, float to, float duration, Func<float, float> ease = null)
        {
            if (rt == null) yield break;
            ease ??= EaseOutCubic;
            if (duration <= 0f)
            {
                rt.localScale = Vector3.one * to;
                yield break;
            }
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                rt.localScale = Vector3.one * Mathf.LerpUnclamped(from, to, ease(t / duration));
                yield return null;
            }
            rt.localScale = Vector3.one * to;
        }

        public static IEnumerator Fill(Image image, float from, float to, float duration)
        {
            if (image == null) yield break;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                image.fillAmount = Mathf.Lerp(from, to, EaseOutCubic(t / duration));
                yield return null;
            }
            image.fillAmount = to;
        }

        public static IEnumerator Tint(Graphic graphic, Color from, Color to, float duration)
        {
            if (graphic == null) yield break;
            if (duration <= 0f)
            {
                graphic.color = to;
                yield break;
            }
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                graphic.color = Color.Lerp(from, to, EaseOutCubic(t / duration));
                yield return null;
            }
            graphic.color = to;
        }
    }

    /// <summary>
    /// Grano de pelicula animado sobre un RawImage. A 12 Hz y baja opacidad convierte una
    /// superficie plana en una copia fotografica; es lo que impide que los paneles negros
    /// se lean como "rectangulo de color" en vez de material.
    /// </summary>
    public sealed class KomGrain : MonoBehaviour
    {
        RawImage _image;
        float _fps = 12f;
        float _next;
        System.Random _rng = new System.Random(7);

        public void Bind(RawImage image, float fps = 12f)
        {
            _image = image;
            _fps = Mathf.Max(1f, fps);
        }

        void Update()
        {
            if (_image == null) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f / _fps;
            var uv = _image.uvRect;
            uv.x = (float)_rng.NextDouble();
            uv.y = (float)_rng.NextDouble();
            _image.uvRect = uv;
        }
    }

    /// <summary>
    /// Deriva lenta de posicion. La portada del menu se mueve dos pixeles por segundo: no
    /// se nota conscientemente, pero la pantalla deja de estar "congelada".
    /// </summary>
    public sealed class KomDrift : MonoBehaviour
    {
        public Vector2 amplitude = new Vector2(6f, 4f);
        public float speed = 0.12f;
        RectTransform _rt;
        Vector2 _base;
        float _t;

        void Awake()
        {
            _rt = (RectTransform)transform;
            _base = _rt.anchoredPosition;
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime * speed;
            _rt.anchoredPosition = _base + new Vector2(Mathf.Sin(_t * 6.283f) * amplitude.x,
                                                       Mathf.Cos(_t * 5.117f) * amplitude.y);
        }
    }

    /// <summary>
    /// Fila de menu con estado de foco propio. El juego se juega con teclado y mando, asi
    /// que el foco NO puede depender del raton: el controlador llama a <see cref="SetFocus"/>
    /// y el puntero solo lo imita.
    /// </summary>
    public sealed class KomMenuRow : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler
    {
        public Image fill;
        public Image rule;
        public Graphic chevron;
        public Text index;
        public Text label;
        public Button button;
        public bool selected;
        public Action<KomMenuRow> onHover;

        Color _labelBase = KomTheme.TextMid;
        float _focus;

        public void Bind(Button b, Image fillImage, Image ruleImage, Graphic chevronGraphic, Text indexLabel, Text mainLabel)
        {
            button = b;
            fill = fillImage;
            rule = ruleImage;
            chevron = chevronGraphic;
            index = indexLabel;
            label = mainLabel;
            if (button != null) button.transition = Selectable.Transition.None;
            Apply(0f);
        }

        public void SetFocus(bool on, bool instant = false)
        {
            selected = on;
            if (instant)
            {
                _focus = on ? 1f : 0f;
                Apply(_focus);
            }
        }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
        {
            onHover?.Invoke(this);
        }

        void Update()
        {
            float target = selected ? 1f : 0f;
            if (Mathf.Approximately(_focus, target)) return;
            _focus = Mathf.MoveTowards(_focus, target, Time.unscaledDeltaTime * 9f);
            Apply(_focus);
        }

        void Apply(float k)
        {
            if (fill != null)
            {
                var c = KomTheme.RowFocus;
                c.a = k;
                fill.color = c;
            }
            if (rule != null)
                rule.rectTransform.localScale = new Vector3(Mathf.Lerp(0f, 1f, k), 1f, 1f);
            if (chevron != null)
            {
                var c = chevron.color;
                c.a = Mathf.Lerp(0f, 1f, k);
                chevron.color = c;
                chevron.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-8f, 0f, k), 0f);
            }
            if (label != null) label.color = Color.Lerp(_labelBase, KomTheme.TextHi, k);
            if (index != null) index.color = Color.Lerp(KomTheme.TextLo, KomTheme.AccentHot, k);
        }
    }
}
