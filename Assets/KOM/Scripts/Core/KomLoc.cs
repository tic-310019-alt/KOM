using UnityEngine;

namespace KOM
{
    /// <summary>
    /// Idioma del juego (español / inglés) y traducción de los literales de interfaz.
    ///
    /// REGLA DE USO: los textos que viven en los JSON (historia, fichas de personaje,
    /// escenarios, rótulos del mapa) NO pasan por aquí: cada definición lleva su par de
    /// campos `*En` y <see cref="KomData"/> los aplica al cargar. Aquí solo está lo que
    /// se escribe en el código.
    ///
    /// El par (español, inglés) va en la propia llamada, no en una tabla de claves: así
    /// el texto inglés se lee junto al punto donde se usa, no se puede olvidar una clave
    /// y el español sigue siendo la fuente de verdad si algo falta.
    ///
    /// El idioma se guarda en PlayerPrefs y sobrevive entre sesiones. Cambiarlo recarga
    /// los catálogos de datos (<see cref="KomData.Reload"/>), así que debe hacerse entre
    /// escenas: las pantallas que ya tienen referencias a definiciones cargadas seguirían
    /// mostrando el idioma anterior hasta reconstruirse.
    /// </summary>
    public static class KomLoc
    {
        public enum Lang { Spanish = 0, English = 1 }

        const string PrefKey = "kom.lang";

        static Lang _lang;
        static bool _loaded;

        public static Lang Language
        {
            get
            {
                EnsureLoaded();
                return _lang;
            }
        }

        public static bool IsEnglish => Language == Lang.English;

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _lang = PlayerPrefs.GetInt(PrefKey, (int)Lang.Spanish) == (int)Lang.English
                ? Lang.English
                : Lang.Spanish;
        }

        public static void SetLanguage(Lang lang)
        {
            EnsureLoaded();
            if (_lang == lang) return;
            _lang = lang;
            PlayerPrefs.SetInt(PrefKey, (int)lang);
            PlayerPrefs.Save();
            // Los catálogos se releen del JSON y vuelven a aplicar el idioma.
            KomData.Reload();
        }

        public static void Toggle()
        {
            SetLanguage(IsEnglish ? Lang.Spanish : Lang.English);
        }

        /// <summary>Nombre del idioma en su propia lengua, para el selector.</summary>
        public static string LanguageName(Lang lang)
        {
            return lang == Lang.English ? "ENGLISH" : "ESPAÑOL";
        }

        public static string CurrentLanguageName => LanguageName(Language);

        /// <summary>Literal de interfaz: español si el juego está en español, inglés si no.</summary>
        public static string T(string es, string en)
        {
            return IsEnglish ? en : es;
        }

        /// <summary>Literal de interfaz con formato. Los marcadores son {0}, {1}…</summary>
        public static string T(string es, string en, params object[] args)
        {
            return string.Format(IsEnglish ? en : es, args);
        }
    }
}
