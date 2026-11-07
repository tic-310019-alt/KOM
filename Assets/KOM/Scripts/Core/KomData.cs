using System;
using System.Collections.Generic;
using UnityEngine;

namespace KOM
{
    [Serializable]
    public class SpecialDef
    {
        // "beam" (orbe de energia), "chain" (latigo), "boomerang" (acordeon que vuelve)
        // o "club" (guitarra a dos manos). Data-driven por personaje.
        public string type = "none";
        public string animatorState = "";
        public string displayName = "";
        public string subtitle = "";
        // Traducción al inglés. KomData.LoadAll la aplica sobre displayName/subtitle
        // cuando el idioma es inglés; el JSON sigue siendo la fuente de verdad.
        public string displayNameEn = "";
        public string subtitleEn = "";
        public string color = "#F5C84A";
        public float duration = 2.2f;
        public float releaseTime = 1.08f;
        public float activeDuration = 0.45f;
        public float damage = 26f;
        public float range = 5.2f;
        public float projectileSpeed = 9f;
        public float knockback = 1.9f;
        public float hitstop = 0.16f;
        public float guardDamage = 0.55f;
        public float hitboxWidth = 0.9f;
        public float hitboxHeight = 0.95f;
        public float hitboxCenterY = 1.15f;
        public float hitStun = 0.8f;
        public bool high;
        // Solo cadena: numero de eslabones, tiempo de extension y caida del latigo.
        public float chainLinks = 26f;
        public float extendTime = 0.16f;
        public float chainSag = 0.38f;
        // Props (acordeon / guitarra). El prefab vive en Resources/Props.
        public string propResource = "";
        public float propScale = 1f;
        // Boomerang: giro del prop y velocidad de vuelta.
        public float spinDegrees = 1080f;
        public float returnSpeed = 13f;
        // Club: tiempos de impacto en segundos del estado (uno por golpe).
        public float[] hitTimes = new float[0];
        // Escala del dano por golpe (el combo reparte el total).
        public float hitDamageScale = 1f;

        public bool IsCombatSpecial => string.Equals(type, "beam", StringComparison.Ordinal)
            || string.Equals(type, "chain", StringComparison.Ordinal)
            || string.Equals(type, "boomerang", StringComparison.Ordinal)
            || string.Equals(type, "club", StringComparison.Ordinal)
            || string.Equals(type, "wave", StringComparison.Ordinal)
            || string.Equals(type, "slam", StringComparison.Ordinal);

        public int HitCount => hitTimes != null && hitTimes.Length > 0 ? hitTimes.Length : 1;

        public AttackSpec BuildAttackSpec()
        {
            return new AttackSpec
            {
                type = AttackType.HeavyPunch,
                displayName = displayName,
                animatorState = animatorState,
                duration = duration,
                hitTime = releaseTime,
                damage = damage,
                range = range,
                height = hitboxCenterY,
                knockback = knockback,
                hitstop = hitstop,
                activeDuration = activeDuration,
                hitboxWidth = hitboxWidth,
                hitboxHeight = hitboxHeight,
                hitboxCenterY = hitboxCenterY,
                high = high,
                heavy = true,
                upperBody = false,
                guardDamage = guardDamage,
                hitReaction = "HitHeavy",
                hitStun = hitStun
            };
        }
    }

    [Serializable]
    public class CharacterDef
    {
        public string id;
        public string displayName;
        public string genre;
        public string tagline;
        // Traducción al inglés (ver KomData.ApplyLanguage).
        public string displayNameEn;
        public string genreEn;
        public string taglineEn;
        public string specialNameEn;
        public string accentColor;
        public string portraitResource;
        public string cardResource;
        public string headResource;
        public string prefabResource;
        public bool playable;
        public bool isBoss;
        public int maxHealth = 100;
        public float walkSpeed = 3f;
        public float dashSpeed = 7f;
        public float damageScale = 1f;
        public float hurtboxRadius = 0.34f;
        public float hurtboxHeight = 1.5f;
        public float crouchHurtboxHeight = 0.85f;
        public string specialName;
        public SpecialDef special;
        // La jefa final absorbe los poderes del resto: lista extra de especiales que
        // se alternan de forma determinista (sin azar) segun la distancia al rival.
        public SpecialDef[] extraSpecials;

        public int SpecialCount
        {
            get
            {
                int count = 0;
                if (special != null && special.IsCombatSpecial) count++;
                if (extraSpecials != null)
                    foreach (var s in extraSpecials)
                        if (s != null && s.IsCombatSpecial) count++;
                return count;
            }
        }

        public SpecialDef GetSpecial(int index)
        {
            int seen = 0;
            if (special != null && special.IsCombatSpecial)
            {
                if (seen == index) return special;
                seen++;
            }
            if (extraSpecials != null)
            {
                foreach (var s in extraSpecials)
                {
                    if (s == null || !s.IsCombatSpecial) continue;
                    if (seen == index) return s;
                    seen++;
                }
            }
            return special != null && special.IsCombatSpecial ? special : null;
        }

        public float MaxSpecialRange
        {
            get
            {
                float max = 0f;
                for (int i = 0; i < SpecialCount; i++)
                {
                    var s = GetSpecial(i);
                    if (s != null && s.range > max) max = s.range;
                }
                return max;
            }
        }

        public bool HasSpecial => SpecialCount > 0;
    }

    [Serializable]
    public class CharacterCatalog
    {
        public CharacterDef[] characters = Array.Empty<CharacterDef>();
    }

    [Serializable]
    public class StageDef
    {
        public string id;
        public string displayName;
        public string subtitle;
        public string displayNameEn;
        public string subtitleEn;
        public string theme;
        public string accentColor;
        public string skyColor;
        public string floorColor;
        public string propColor;
        public float cameraDistance = 8f;
        public float cameraHeight = 2f;
        // Altura a la que mira la camara de combate (0 = 1.0 m, el pecho). Subirla encuadra
        // el decorado alto del fondo sin alejar la camara.
        public float cameraLookY = 1f;

        // ENCUADRE POR DATOS. Si cameraFill > 0 la camara NO usa cameraDistance/cameraHeight:
        // resuelve la distancia para que un luchador de referencia ocupe esa fraccion de la
        // altura de pantalla y coloca la altura con el angulo pedido. Asi el encuadre se
        // define por lo que se quiere VER (cobertura + angulo), no por metros al azar.
        //   cameraFov    campo de vision vertical (grados)
        //   cameraFill   fraccion de la altura de pantalla que ocupa el luchador (0.46 = 46%)
        //   cameraPitch  inclinacion hacia abajo respecto a la horizontal (grados)
        public float cameraFov = 34f;
        public float cameraFill;
        public float cameraPitch = 12f;
        // PRESENTACION. Al empezar el asalto la camara arranca a esta distancia x la de
        // pelea y entra hasta el encuadre normal. Es un tope POR ESCENARIO: alejarse de
        // mas descubre los bordes cortados del decorado (suelo, telon, paredes).
        // 1 = sin presentacion.
        public float cameraIntroZoom = 1.25f;

        public float arenaHalfWidth = 6f;

        // Música de fondo del combate (Resources, sin extensión). La pista es parte del
        // escenario: La Madrugadora es íntima, el Zócalo es la final. Si falta, KomAudio
        // cae a la pista 1 para que un escenario nuevo nunca se quede mudo.
        public string musicResource;

        // Superficie de pelea del ARTE, medida con tools/blender/analyze_materials.py sobre el
        // material del suelo de pelea: la tarima de La Madrugadora (16.8 m) y el mosaico de
        // Bellas Artes (22.4 m). Es la referencia para comprobar que la plataforma no es un
        // descampado alrededor del area jugable.
        //   arenaSurfaceWidth     ancho medido en el .blend
        //   arenaSurfaceRefScale  prefabScale con el que se midio (1 en La Madrugadora, 100 en
        //                         Bellas Artes, cuyo FBX entra en centimetros). El ancho en
        //                         metros del mundo es width * prefabScale / refScale.
        public float arenaSurfaceWidth;
        public float arenaSurfaceRefScale = 1f;
        public string prefabResource;
        // Encuadre del escenario real. Se aplica en runtime para poder ajustarlo sin
        // reexportar: el prefab queda en el origen y estos valores lo llevan a que la
        // PLATAFORMA de pelea quede centrada en x=0,z=0 con su superficie en y=0.
        // Algunos .blend tienen el objeto rotado y el FBX sale Z-arriba (tumbado):
        // prefabRotationX lo endereza. Se aplica antes que el giro en Y.
        public float prefabRotationX;
        public float prefabRotationY;
        public float prefabOffsetX;
        public float prefabOffsetY;
        public float prefabOffsetZ;
        public float prefabScale = 1f;
        // Multiplicador de la receta de luz (las naves reales necesitan mas que la arena
        // procedural para la que se calibro).
        public float lightScale = 1f;

        // Tono del mundo (shader KOM/StageBW, ajustes GLOBALES). Cada nave tiene un valor
        // base muy distinto: el marmol de La Madrugadora es casi blanco y sin bajar el
        // gain se come a los personajes; el mosaico de Bellas Artes ya trae contraste y
        // solo aguanta un empujon pequeno.
        //   worldGain     <1 oscurece el mundo entero
        //   worldContrast >1 endurece la curva
        //   poolStrength  0 = sin charco de luz; 1 = el suelo se apaga del todo en el borde
        public float worldGain = 1f;
        public float worldContrast = 1f;
        public float poolStrength;
        public float poolInner = 3.5f;
        public float poolOuter = 14f;

        // Bruma de profundidad (atmosferica). Sube lo lejano hacia un gris medio; es lo
        // que separa el decorado del suelo de pelea. hazeStrength 0 la desactiva.
        public float hazeStrength;
        public float hazeStart = 14f;
        public float hazeEnd = 45f;
        public float hazeValue = 0.42f;

        // Publico de grada (Resources, sin extension). Vacio = escenario sin publico.
        // El layout lo hornea KomCrowdTools midiendo las bancadas reales; el runtime solo
        // construye la malla. Ver docs/CROWD.md.
        public string crowdLayout;
    }

    [Serializable]
    public class StageCatalog
    {
        public StageDef[] stages = Array.Empty<StageDef>();
    }

    // Una pagina de la introduccion narrativa. El texto vive en Resources/Data/story.json
    // para poder reescribirlo sin recompilar; la imagen es un sprite de Resources/UI/Story.
    [Serializable]
    public class StoryPageDef
    {
        public string kicker;
        public string title;
        public string body;
        public string quote;
        // Traducción al inglés (ver KomData.ApplyLanguage).
        public string kickerEn;
        public string titleEn;
        public string bodyEn;
        public string quoteEn;
        public string image;
        // Duracion de la deriva de camara en segundos. 0 = la calcula el controlador a
        // partir del numero de palabras. Las paginas unicas usan un valor largo para que
        // el zoom siga avanzando mientras el jugador lee.
        public float dwell;
        // Opacidad de la mancha que protege el pie de foto. 0 = la de por defecto (0.70).
        // Las ilustraciones con zonas claras bajo el texto piden un poco mas.
        public float wash;

        public int WordCount
        {
            get
            {
                if (string.IsNullOrEmpty(body)) return 0;
                int words = 1;
                foreach (char c in body)
                    if (c == ' ') words++;
                return words;
            }
        }
    }

    // Un momento narrativo del torneo. La misma escena Story sirve para todos: el
    // capitulo se elige por id ("intro", "veto", "final", "concert") y cada uno tiene
    // su propio boton de salida.
    [Serializable]
    public class StoryChapterDef
    {
        public string id;
        public string hint;
        public string hintEn;
        public StoryPageDef[] pages = Array.Empty<StoryPageDef>();

        public int Count => pages != null ? pages.Length : 0;
    }

    [Serializable]
    public class StoryCatalog
    {
        public StoryChapterDef[] chapters = Array.Empty<StoryChapterDef>();

        public StoryChapterDef Find(string id)
        {
            if (chapters == null) return null;
            foreach (var c in chapters)
                if (c != null && string.Equals(c.id, id, StringComparison.Ordinal)) return c;
            return null;
        }

        public StoryChapterDef First => chapters != null && chapters.Length > 0 ? chapters[0] : null;
    }

    [Serializable]
    public class TournamentNodeDef
    {
        public string id;
        public string stageId;
        public string label;
        public string labelEn;
        public string aiProfile;
        public int difficulty = 1;
        public int seed;
        public float mapX;
        public float mapY;
    }

    [Serializable]
    public class TournamentData
    {
        public string title;
        public string titleEn;
        public string[] opponentRotation = Array.Empty<string>();
        public TournamentNodeDef[] nodes = Array.Empty<TournamentNodeDef>();
    }

    public static class KomData
    {
        static CharacterCatalog _characters;
        static StageCatalog _stages;
        static TournamentData _tournament;
        static StoryCatalog _story;
        static Dictionary<string, CharacterDef> _characterById;
        static Dictionary<string, StageDef> _stageById;

        public static CharacterCatalog Characters
        {
            get
            {
                if (_characters == null) LoadAll();
                return _characters;
            }
        }

        public static StageCatalog Stages
        {
            get
            {
                if (_stages == null) LoadAll();
                return _stages;
            }
        }

        public static TournamentData Tournament
        {
            get
            {
                if (_tournament == null) LoadAll();
                return _tournament;
            }
        }

        public static StoryCatalog Story
        {
            get
            {
                if (_story == null) LoadAll();
                return _story;
            }
        }

        static void LoadAll()
        {
            _characters = LoadJson<CharacterCatalog>("Data/characters");
            _stages = LoadJson<StageCatalog>("Data/stages");
            _tournament = LoadJson<TournamentData>("Data/tournament");
            _story = LoadJson<StoryCatalog>("Data/story");
            _characterById = new Dictionary<string, CharacterDef>();
            foreach (var c in _characters.characters)
            {
                if (c != null && !string.IsNullOrEmpty(c.id)) _characterById[c.id] = c;
            }
            _stageById = new Dictionary<string, StageDef>();
            foreach (var s in _stages.stages)
            {
                if (s != null && !string.IsNullOrEmpty(s.id)) _stageById[s.id] = s;
            }
            ApplyLanguage();
        }

        /// <summary>
        /// Vuelca la traducción inglesa sobre los campos que consume el juego. Se hace al
        /// cargar y no en cada lectura para que el resto del código siga leyendo
        /// `displayName` sin saber que hay dos idiomas: una sola puerta, ningun sitio
        /// donde se olvide traducir una ficha.
        ///
        /// Si un campo `*En` viene vacío se conserva el español: un texto sin traducir es
        /// un texto legible, no una cadena vacía en pantalla.
        /// </summary>
        static void ApplyLanguage()
        {
            if (!KomLoc.IsEnglish) return;

            foreach (var c in _characters.characters)
            {
                if (c == null) continue;
                c.displayName = Pick(c.displayNameEn, c.displayName);
                c.genre = Pick(c.genreEn, c.genre);
                c.tagline = Pick(c.taglineEn, c.tagline);
                c.specialName = Pick(c.specialNameEn, c.specialName);
                ApplySpecial(c.special);
                if (c.extraSpecials != null)
                    foreach (var s in c.extraSpecials) ApplySpecial(s);
            }

            foreach (var s in _stages.stages)
            {
                if (s == null) continue;
                s.displayName = Pick(s.displayNameEn, s.displayName);
                s.subtitle = Pick(s.subtitleEn, s.subtitle);
            }

            if (_tournament != null)
            {
                _tournament.title = Pick(_tournament.titleEn, _tournament.title);
                if (_tournament.nodes != null)
                    foreach (var n in _tournament.nodes)
                        if (n != null) n.label = Pick(n.labelEn, n.label);
            }

            if (_story != null && _story.chapters != null)
            {
                foreach (var ch in _story.chapters)
                {
                    if (ch == null) continue;
                    ch.hint = Pick(ch.hintEn, ch.hint);
                    if (ch.pages == null) continue;
                    foreach (var p in ch.pages)
                    {
                        if (p == null) continue;
                        p.kicker = Pick(p.kickerEn, p.kicker);
                        p.title = Pick(p.titleEn, p.title);
                        p.body = Pick(p.bodyEn, p.body);
                        p.quote = Pick(p.quoteEn, p.quote);
                    }
                }
            }
        }

        static void ApplySpecial(SpecialDef special)
        {
            if (special == null) return;
            special.displayName = Pick(special.displayNameEn, special.displayName);
            special.subtitle = Pick(special.subtitleEn, special.subtitle);
        }

        static string Pick(string translated, string fallback)
        {
            return string.IsNullOrEmpty(translated) ? fallback : translated;
        }

        static T LoadJson<T>(string resourcePath) where T : new()
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                Debug.LogError($"[KOM] No se encontro Resources/{resourcePath}.json");
                return new T();
            }
            try
            {
                return JsonUtility.FromJson<T>(asset.text) ?? new T();
            }
            catch (Exception e)
            {
                Debug.LogError($"[KOM] JSON invalido en {resourcePath}: {e.Message}");
                return new T();
            }
        }

        // Vacia la cache: el banco de pruebas edita los JSON y necesita leer los
        // valores nuevos sin recargar el dominio (el juego real los lee al arrancar).
        public static void Reload()
        {
            _characters = null;
            _stages = null;
            _tournament = null;
            _story = null;
            _characterById = null;
            _stageById = null;
        }

        public static CharacterDef GetCharacter(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_characterById == null) LoadAll();
            return _characterById.TryGetValue(id, out var def) ? def : null;
        }

        public static StageDef GetStage(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_stageById == null) LoadAll();
            return _stageById.TryGetValue(id, out var def) ? def : null;
        }

        public static CharacterDef Boss
        {
            get
            {
                foreach (var c in Characters.characters)
                    if (c != null && c.isBoss) return c;
                return null;
            }
        }

        public static List<CharacterDef> Playable
        {
            get
            {
                var list = new List<CharacterDef>();
                foreach (var c in Characters.characters)
                    if (c != null && c.playable) list.Add(c);
                return list;
            }
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var color))
                return color;
            return fallback;
        }

        public static Color Accent(CharacterDef def)
        {
            return ParseColor(def != null ? def.accentColor : null, new Color(0.9f, 0.71f, 0.29f));
        }

        // Acento para la INTERFAZ: el juego es blanco y negro salvo personajes, habilidades
        // y auras, asi que el HUD y los menus usan el mismo acento pero en gris. Los VFX
        // siguen llamando a Accent() y conservan el color.
        public static Color UiAccent(CharacterDef def) => Mono(Accent(def));

        public static Color UiAccent(StageDef def) => Mono(Accent(def));

        public static Color Mono(Color c)
        {
            float lum = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
            return new Color(lum, lum, lum, c.a);
        }

        // Color de la habilidad (orbe/cadena). El aura usa SIEMPRE el color del
        // personaje (Accent); este es el color propio del poder.
        public static Color SpecialColor(CharacterDef def)
        {
            if (def != null && def.HasSpecial)
                return ParseColor(def.GetSpecial(0).color, Accent(def));
            return Accent(def);
        }

        public static Color SpecialColor(CharacterDef def, SpecialDef special)
        {
            if (special != null) return ParseColor(special.color, Accent(def));
            return SpecialColor(def);
        }

        public static Color Accent(StageDef def)
        {
            return ParseColor(def != null ? def.accentColor : null, new Color(0.9f, 0.71f, 0.29f));
        }

        public static string CurrentOpponentSeed(string nodeId, int difficulty)
        {
            return $"{nodeId}:{difficulty}";
        }
    }
}
