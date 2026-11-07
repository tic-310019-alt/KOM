namespace KOM
{
    public static class KomScenes
    {
        public const string MainMenu = "MainMenu";
        // Introduccion narrativa: tres paginas ilustradas entre el menu y la seleccion.
        // Es tambien la ventana de precarga silenciosa del resto del juego.
        public const string Story = "Story";
        public const string CharacterSelect = "CharacterSelect";
        public const string Map = "Map";
        public const string Combat = "Combat";
        public const string Ending = "Ending";

        public static readonly string[] All =
        {
            MainMenu, Story, CharacterSelect, Map, Combat, Ending
        };
    }
}
