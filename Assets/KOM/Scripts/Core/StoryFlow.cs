using UnityEngine;

namespace KOM
{
    /// <summary>
    /// Puente entre el juego y la escena <see cref="KomScenes.Story"/>.
    ///
    /// La historia es UNA escena reutilizable: el capitulo se elige por id y la escena a la
    /// que hay que volver se pasa en <see cref="NextScene"/>. Los campos son estaticos
    /// porque no hay paso de argumentos entre escenas y la sesion ya es persistente.
    ///
    /// Momentos del torneo:
    ///   intro   · al empezar la partida (menu)          → CharacterSelect
    ///   veto    · al ganar el segundo combate           → Map
    ///   final   · al entrar al ultimo combate           → Combat
    ///   concert · al ganar el combate final             → Ending
    /// </summary>
    public static class StoryFlow
    {
        public const string Intro = "intro";
        public const string Veto = "veto";
        public const string Final = "final";
        public const string Concert = "concert";

        public static string Chapter { get; private set; }
        public static string NextScene { get; private set; }

        public static void Play(string chapter, string nextScene)
        {
            Chapter = chapter;
            NextScene = string.IsNullOrEmpty(nextScene) ? KomScenes.CharacterSelect : nextScene;
            Time.timeScale = 1f;
            // La intro es la primera pantalla del juego: aprovecha la transición para pagar
            // la precarga completa (binds de los siete luchadores incluidos) con la barra de
            // progreso real. Los demás capítulos ya la encuentran caliente.
            ScreenFader.Load(KomScenes.Story, chapter == Intro);
        }

        /// <summary>
        /// Igual que <see cref="Play"/> pero una sola vez por partida. Sirve para los
        /// momentos que se disparan al volver de un combate: si el jugador pierde y
        /// reintenta, la escena no se repite.
        /// </summary>
        public static bool PlayOnce(string key, string chapter, string nextScene)
        {
            var session = GameSession.Ensure();
            if (session.HasPlayedStory(key)) return false;
            session.MarkStoryPlayed(key);
            Play(chapter, nextScene);
            return true;
        }

        public static void Clear()
        {
            Chapter = null;
            NextScene = null;
        }
    }
}
