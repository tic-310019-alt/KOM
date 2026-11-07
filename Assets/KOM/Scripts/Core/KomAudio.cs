using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KOM
{
    /// <summary>
    /// Fachada de audio del juego. Todo pasa por aquí; nadie crea AudioSources sueltos.
    ///
    /// CUATRO CAPAS, como una mezcla de estadio:
    ///
    ///   · MENÚ     — el tema del menú. Suena en menú, selección, mapa y cierre, y se
    ///                apaga cuando empieza una pelea o una escena de historia.
    ///   · COMBATE  — una pista por escenario, ligera y por debajo de todo lo demás.
    ///                Entra con fundido al empezar el asalto y se va al terminar.
    ///   · CAMA     — el murmullo del público. Una sola pista en bucle, con dos versiones
    ///                (normal y final). Los WAV se prepararon con costura de cola sobre
    ///                la cabecera (`tools/prepare_audio.py`) para que el bucle no tenga
    ///                salto.
    ///   · VOZ      — porras y canciones de personaje. UN SOLO canal, con cola.
    ///
    /// LA REGLA QUE MANDA: las voces NUNCA se solapan entre sí. Antes había un pool de
    /// cuatro fuentes que se robaban la una a la otra cuando coincidían dos porras, y el
    /// resultado era una grada que se pisaba a sí misma. Ahora hay una fuente de voz y
    /// una cola con prioridad: si llega una petición mientras suena otra, espera su
    /// turno (o se descarta si es una porra y lo que suena es una canción). Mientras hay
    /// voz, las camas bajan solas (ducking) para que nada compita.
    ///
    /// NADA SE CORTA EN SECO: cada entrada y cada salida es un fundido. La voz se funde
    /// en los últimos 0.7 s de su propio clip, así que termina antes de que el archivo se
    /// acabe y no hay click. Un cambio de escena funde la voz en 0.4 s en vez de matarla.
    ///
    /// NIVELES (medidos sobre los WAV ya normalizados por RMS a -20/-24/-18 dBFS):
    /// menú -25.7 dB, combate -25.7 dB, cama -24 dB, voz -18 dB. La voz manda 6-8 dB
    /// sobre las camas y los impactos (pico 0 dB) mandan sobre todo.
    /// </summary>
    public static class KomAudio
    {
        // ------------------------------------------------------------------ clips
        // Resources/Audio/Porras (sin extensión).
        public const string CielitoLindo = "cielito_lindo";
        public const string EeehBruto = "eeeh_bruto";
        public const string ElPanadero = "el_panadero";
        public const string FierroViejo = "fierro_viejo";
        public const string KingOfMusic = "king_of_music";
        public const string LaChona = "la_chona";

        // La porra del nombre del juego queda RESERVADA para el clímax: la pelea final
        // y el concierto. El resto rota para que la grada no repita.
        static readonly string[] AmbientPorras = { CielitoLindo, EeehBruto, ElPanadero, LaChona, FierroViejo };

        // ------------------------------------------------------------------ niveles
        public const float MusicLevel = 0.52f;      // -20 dBFS de pista -> -25.7 en mezcla
        public const float CombatMusicLevel = 0.52f;
        public const float AmbienceFight = 1.0f;    // -24 dBFS de pista -> -24 en mezcla
        public const float AmbienceStory = 0.34f;
        public const float AmbienceConcert = 0.60f;

        static KomAudioPlayer _player;

        static KomAudioPlayer Player
        {
            get
            {
                if (_player != null) return _player;
                var go = new GameObject("KomAudio");
                if (Application.isPlaying) Object.DontDestroyOnLoad(go);
                _player = go.AddComponent<KomAudioPlayer>();
                return _player;
            }
        }

        // ------------------------------------------------------------------ capas
        /// <summary>Tema del menú. Idempotente: si ya suena, no lo reinicia.</summary>
        public static void Menu() => Player.PlayMenu();

        /// <summary>
        /// Combate: música ligera del escenario + cama de público. Sustituye al par
        /// Silence()+Ambience() porque las dos capas entran juntas y coordinadas.
        /// </summary>
        public static void Combat(string stageId, bool finalFight = false)
            => Player.PlayCombat(stageId, finalFight);

        /// <summary>
        /// Música de escena suelta (sin tocar la cama). La usa el capítulo previo a la
        /// final: el tema del Zócalo entra ya en la narración y, como la pista es la misma
        /// que la del combate final, al cargar la pelea NO se reinicia: sube de nivel y
        /// sigue. La historia y la pelea comparten un solo gesto musical.
        /// </summary>
        public static void SceneMusic(string resourcePath, float level = CombatMusicLevel)
            => Player.PlayCombatTrack(resourcePath, level);

        /// <summary>Cama de público. finalFight usa la versión "extensión".</summary>
        public static void Ambience(float volume, bool finalFight = false)
            => Player.PlayAmbience(volume, finalFight);

        /// <summary>Baja todo a cero con la cadencia de fundido del reproductor.</summary>
        public static void Silence() => Player.SilenceAll();

        // ------------------------------------------------------------------ voces
        /// <summary>Porra concreta de la grada.</summary>
        public static void Porra(string clip, float volume, float delay = 0f, string onlyInScene = null)
            => Player.RequestVoice("Audio/Porras/" + clip, volume, delay, onlyInScene, KomAudioPlayer.VoiceKind.Chant);

        /// <summary>Porra rotativa (nunca la del nombre del juego).</summary>
        public static void AmbientPorra(float volume, float delay = 0f, string onlyInScene = null)
            => Player.RequestVoice("Audio/Porras/" + Player.NextAmbientPorra(AmbientPorras), volume, delay,
                onlyInScene, KomAudioPlayer.VoiceKind.Chant);

        /// <summary>
        /// Canción del luchador. Si ese personaje no tiene grabación propia (Flavia es la
        /// única sin ella), la grada canta el himno del torneo en vez de dejar el momento
        /// en silencio: es el mismo recurso que usa la final.
        /// </summary>
        public static void Coreo(string characterId, float volume, float delay = 0f, string onlyInScene = null)
        {
            if (string.IsNullOrEmpty(characterId)) return;
            Player.RequestVoice("Audio/Coreos/" + characterId, volume, delay, onlyInScene,
                KomAudioPlayer.VoiceKind.Song, "Audio/Porras/" + KingOfMusic);
        }

        /// <summary>Victoria de combate: la canción del ganador manda sobre cualquier porra.</summary>
        public static void Victory(string winnerId)
        {
            Player.RequestVoice("Audio/Coreos/" + winnerId, 0.95f, 0.25f, KomScenes.Combat,
                KomAudioPlayer.VoiceKind.Hymn, "Audio/Porras/" + KingOfMusic);
        }

        /// <summary>Concierto final: la porra del nombre del juego, y a los segundos otra vez.</summary>
        public static void Concert()
        {
            Porra(KingOfMusic, 0.95f, 0.2f, KomScenes.Story);
            Porra(KingOfMusic, 0.72f, 15.5f, KomScenes.Story);
        }

        // ------------------------------------------------------------------ SFX de combate
        //
        // Cada familia tiene variantes y rota de forma determinista (sin Random). Los clips
        // se normalizaron por pico a -1 dB al prepararlos: los dos packs venian con 9 dB de
        // diferencia entre si y, sin eso, unos golpes tapaban a otros.
        //
        // Volumen relativo al impacto: el golpe seco manda, el aire del golpe va debajo y
        // la grada sigue oyendose por encima de todo lo demas.

        const float VolLight = 0.62f;
        const float VolHeavy = 0.72f;
        const float VolBody = 0.66f;
        const float VolLegend = 0.85f;
        const float VolBlock = 0.55f;
        const float VolBreak = 0.80f;
        const float VolKo = 0.95f;
        const float VolSwing = 0.28f;
        const float VolSwingHeavy = 0.34f;
        const float VolCharge = 0.45f;
        const float VolRelease = 0.55f;
        const float VolBoom = 0.80f;
        const float VolSlam = 0.85f;
        const float VolClub = 0.80f;

        static readonly Dictionary<string, int> Cursors = new Dictionary<string, int>();

        static void PlayFamily(string family, int count, float volume, float delay = 0f, string scene = null)
        {
            int total = Mathf.Max(1, count);
            int idx = Cursors.TryGetValue(family, out var cursor) ? cursor : 0;
            Cursors[family] = (idx + 1) % total;
            Player.PlayClip($"Audio/Sfx/{family}_{idx + 1}", volume, delay, scene);
        }

        /// <summary>Aire del golpe. Suena al arrancar la animacion; el impacto llega despues.</summary>
        public static void Swing(AttackSpec spec)
        {
            if (spec == null) return;
            if (spec.heavy || spec.hitCount > 1) PlayFamily("swing_heavy", 2, VolSwingHeavy, 0f, KomScenes.Combat);
            else PlayFamily("swing", 6, VolSwing, 0f, KomScenes.Combat);
        }

        /// <summary>Impacto. La familia depende del ataque: puno, cuerpo, leyenda o bloqueo.</summary>
        public static void Impact(AttackSpec spec, bool blocked)
        {
            if (spec == null) return;
            if (blocked)
            {
                Block(spec.heavy);
                return;
            }
            switch (spec.type)
            {
                case AttackType.Uppercut:
                case AttackType.DropKick:
                case AttackType.Combo3:
                    PlayFamily("impact_legend", 3, VolLegend, 0f, KomScenes.Combat);
                    return;
                case AttackType.LightKick:
                case AttackType.HeavyKick:
                case AttackType.LowKick:
                    PlayFamily("impact_body", 3, VolBody, 0f, KomScenes.Combat);
                    return;
                case AttackType.QuadPunch:
                case AttackType.Combo1:
                    PlayFamily("impact_light", 6, VolLight, 0f, KomScenes.Combat);
                    return;
                default:
                    if (spec.heavy) PlayFamily("impact_heavy", 3, VolHeavy, 0f, KomScenes.Combat);
                    else PlayFamily("impact_light", 6, VolLight, 0f, KomScenes.Combat);
                    return;
            }
        }

        public static void Block(bool heavy)
            => PlayFamily(heavy ? "block_heavy" : "block_light", 3, VolBlock, 0f, KomScenes.Combat);

        public static void GuardBreak() => PlayFamily("break", 3, VolBreak, 0f, KomScenes.Combat);

        /// <summary>KO: sting del golpe y, medio segundo despues, el cuerpo contra el suelo.</summary>
        public static void Knockout()
        {
            PlayFamily("ko", 3, VolKo, 0.05f, KomScenes.Combat);
            PlayFamily("fall", 3, 0.75f, 0.55f, KomScenes.Combat);
        }

        public static void SpecialCharge() => PlayFamily("charge", 3, VolCharge, 0f, KomScenes.Combat);

        public static void SpecialRelease() => PlayFamily("release", 3, VolRelease, 0f, KomScenes.Combat);

        // Recolectable cogido: el brillo del "release" a volumen bajo. No es un golpe,
        // asi que no compite con el impacto ni tapa el aire del siguiente ataque.
        public static void Pickup() => PlayFamily("release", 3, VolRelease * 0.55f, 0f, KomScenes.Combat);

        /// <summary>Golpe del poder: cada especial suena a lo que es.</summary>
        public static void SpecialImpact(SpecialDef special, bool blocked)
        {
            if (blocked)
            {
                Block(true);
                return;
            }
            string type = special != null ? special.type : null;
            switch (type)
            {
                case "slam":
                    PlayFamily("slam", 3, VolSlam, 0f, KomScenes.Combat);
                    return;
                case "club":
                case "boomerang":
                    PlayFamily("club", 3, VolClub, 0f, KomScenes.Combat);
                    return;
                default:
                    PlayFamily("boom", 3, VolBoom, 0f, KomScenes.Combat);
                    return;
            }
        }

        /// <summary>Aviso de que la barra de super esta llena (solo el jugador).</summary>
        public static void SuperReady() => Player.PlayClip("Audio/Sfx/charge_3", 0.38f, 0f, KomScenes.Combat);
    }

    /// <summary>
    /// Reproductor persistente. Vive fuera de las escenas para que la música y el público
    /// no se corten al cambiar de pantalla.
    ///
    /// Dos escalas de tiempo conviven aquí y conviene no confundirlas:
    ///   · NIVEL   — cuánto debe sonar una capa (0..1). Cambia lento y con fundido.
    ///   · DUCK    — cuánto se aparta una cama porque hay una voz. Cambia rápido.
    /// La voz se calcula aparte, con su propio fundido de entrada y de cola.
    /// </summary>
    public sealed class KomAudioPlayer : MonoBehaviour
    {
        public enum VoiceKind
        {
            /// <summary>Porra de la grada: se descarta si hay una canción sonando.</summary>
            Chant = 0,
            /// <summary>Canción de personaje: espera su turno.</summary>
            Song = 1,
            /// <summary>Himno de victoria: releva a una porra con un fundido corto.</summary>
            Hymn = 2
        }

        struct Pending
        {
            public float at;
            public string path;
            public float volume;
            public string scene;
            public VoiceKind kind;
            public string fallback;
            /// <summary>Efecto de combate: va al pool, nunca al canal de voz.</summary>
            public bool sfx;
        }

        struct QueuedVoice
        {
            public AudioClip clip;
            public float volume;
            public VoiceKind kind;
        }

        // --- fuentes
        AudioSource _menu;
        AudioSource _combatA, _combatB;
        AudioSource _bed;
        AudioSource _voice;
        AudioSource[] _shots;
        float[] _shotTarget;

        // --- música
        float _menuTarget, _menuLevel;
        float _combatTarget, _combatLevelA, _combatLevelB;
        bool _combatOnB;      // cual de las dos fuentes lleva la pista activa
        float _bedTarget, _bedLevel;
        float _duck;

        // --- voz
        readonly List<QueuedVoice> _queue = new List<QueuedVoice>();
        readonly List<Pending> _pending = new List<Pending>();
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        float _voiceLen, _voiceTime, _voiceGain;
        float _voiceGap;
        float _voiceCut = -1f;          // > 0 mientras se funde por salida forzada
        float _voiceCutLen = 1f;
        VoiceKind _voiceKind;

        int _shotCursor;
        int _porraCursor;
        string _scene;

        // Cadencias de fundido en unidades de nivel por segundo (tiempo sin escalar:
        // el hitstop y la pausa no deben congelar la mezcla).
        const float MusicFade = 0.85f;
        const float BedFade = 0.55f;
        const float VoiceFadeIn = 0.22f;
        const float VoiceFadeOut = 0.70f;
        const float VoiceGapSeconds = 0.55f;
        const float SceneVoiceFade = 0.40f;
        // Tope duro de subida/bajada de la voz, en nivel por segundo. La rampa calculada
        // ya es más lenta que esto; el tope existe para que un frame largo (una carga, un
        // GC, el editor repintando) no pueda producir un salto audible: el invariante
        // "la voz nunca salta" se aplica en la SALIDA, no en la fórmula.
        const float VoiceRampPerSecond = 6f;
        const int QueueLimit = 2;

        // Cuánto se apartan las camas cuando hay voz. La música cede más que la grada:
        // la grada y la porra son el mismo público, no compiten.
        const float MusicDuck = 0.52f;
        const float BedDuck = 0.64f;

        public bool VoicePlaying => _voice != null && _voice.isPlaying;
        public int VoiceQueued => _queue.Count;
        public float VoiceLevel => _voice != null ? _voice.volume : 0f;
        public float MenuVolume => _menu != null ? _menu.volume : 0f;
        public float CombatVolume => ActiveCombat != null ? ActiveCombat.volume : 0f;
        public float BedVolume => _bed != null ? _bed.volume : 0f;
        public float Duck => _duck;
        public string VoiceClipName => _voice != null && _voice.clip != null ? _voice.clip.name : "";
        // Diagnostico: por que termino la ultima voz y en que punto del clip.
        public string VoiceStopReason { get; private set; } = "";
        public float VoiceTime => _voiceTime;
        public float VoiceLen => _voiceLen;

        AudioSource ActiveCombat => _combatOnB ? _combatB : _combatA;

        void Awake()
        {
            _menu = NewSource("MusicMenu", true);
            _combatA = NewSource("MusicCombatA", true);
            _combatB = NewSource("MusicCombatB", true);
            _bed = NewSource("Ambience", true);
            _voice = NewSource("Voice", false);
            _shots = new AudioSource[4];
            _shotTarget = new float[_shots.Length];
            for (int i = 0; i < _shots.Length; i++) _shots[i] = NewSource("Shot" + i, false);
            _scene = SceneManager.GetActiveScene().name;
        }

        AudioSource NewSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0f;      // 2D: es banda sonora, no un objeto del mundo
            src.volume = 0f;
            src.bypassEffects = true;
            src.bypassListenerEffects = true;
            return src;
        }

        AudioClip Clip(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_clips.TryGetValue(path, out var cached)) return cached;
            var clip = Resources.Load<AudioClip>(path);
            if (clip == null) Debug.LogWarning($"[KOM] Falta el audio Resources/{path}");
            _clips[path] = clip;
            return clip;
        }

        /// <summary>Igual que <see cref="Clip"/> pero sin aviso: para rutas con sustituto.</summary>
        AudioClip ClipQuiet(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_clips.TryGetValue(path, out var cached)) return cached;
            var clip = Resources.Load<AudioClip>(path);
            _clips[path] = clip;
            return clip;
        }

        // ------------------------------------------------------------------ capas

        public void PlayMenu()
        {
            var clip = Clip("Audio/Music/theme");
            if (clip == null) return;
            if (_menu.clip != clip)
            {
                _menu.clip = clip;
                _menu.volume = 0f;
                _menu.Play();
            }
            else if (!_menu.isPlaying)
            {
                _menu.UnPause();
                if (!_menu.isPlaying) _menu.Play();
            }
            _menuTarget = KomAudio.MusicLevel;
            // El tema es de interior: fuera de la arena no hay grada de fondo, y una
            // porra no sobrevive al cambio de pantalla (se funde, nunca se corta).
            _bedTarget = 0f;
            _combatTarget = 0f;
            ClearVoice(SceneVoiceFade);
        }

        /// <summary>
        /// Música de combate del escenario. La pista se elige por datos (stages.json) y la
        /// cama entra con ella. Si ya está sonando la misma pista no se reinicia: volver a
        /// entrar en el mismo escenario no corta la música.
        /// </summary>
        public void PlayCombat(string stageId, bool finalFight)
        {
            PlayCombatTrack(CombatTrack(stageId, finalFight), KomAudio.CombatMusicLevel);
            PlayAmbience(finalFight ? KomAudio.AmbienceFight * 0.92f : KomAudio.AmbienceFight, finalFight);
        }

        /// <summary>
        /// Pone una pista en la capa de música de escena. Idempotente: si ya suena esa
        /// misma pista no la reinicia (así la narración previa a la final y la pelea
        /// comparten el tema sin que se corte al cambiar de escena).
        /// </summary>
        public void PlayCombatTrack(string resourcePath, float level)
        {
            var clip = Clip(resourcePath);
            if (clip != null)
            {
                var active = ActiveCombat;
                if (active.clip == clip && active.isPlaying)
                {
                    // misma pista: solo se asegura el nivel
                }
                else if (active.clip == clip && !active.isPlaying)
                {
                    active.UnPause();
                    if (!active.isPlaying) active.Play();
                }
                else
                {
                    // Pista distinta: entra en la fuente libre y la anterior se funde.
                    var next = _combatOnB ? _combatA : _combatB;
                    next.clip = clip;
                    next.volume = 0f;
                    next.Play();
                    _combatOnB = !_combatOnB;
                }
            }
            _combatTarget = Mathf.Clamp01(level);
            _menuTarget = 0f;
        }

        static string CombatTrack(string stageId, bool finalFight)
        {
            if (finalFight) return "Audio/Music/combat_3";
            // La pista vive en el escenario (stages.json); si falta, se cae a la primera.
            var stage = KomData.GetStage(stageId);
            if (stage != null && !string.IsNullOrEmpty(stage.musicResource)) return stage.musicResource;
            switch (stageId)
            {
                case "bellas_artes": return "Audio/Music/combat_2";
                case "zocalo":
                case "zocalo_final": return "Audio/Music/combat_3";
                default: return "Audio/Music/combat_1";
            }
        }

        public void PlayAmbience(float volume, bool finalFight)
        {
            var clip = Clip(finalFight ? "Audio/Ambience/stadium_ext" : "Audio/Ambience/stadium");
            if (clip == null) return;
            if (_bed.clip != clip)
            {
                _bed.clip = clip;
                _bed.volume = 0f;
                _bed.Play();
            }
            else if (!_bed.isPlaying)
            {
                _bed.UnPause();
                if (!_bed.isPlaying) _bed.Play();
            }
            _bedTarget = Mathf.Clamp01(volume);
        }

        public void SilenceAll()
        {
            _menuTarget = 0f;
            _combatTarget = 0f;
            _bedTarget = 0f;
            ClearVoice(SceneVoiceFade);
        }

        // ------------------------------------------------------------------ voces

        public string NextAmbientPorra(string[] pool)
        {
            if (pool == null || pool.Length == 0) return null;
            var pick = pool[_porraCursor % pool.Length];
            _porraCursor++;
            return pick;
        }

        /// <summary>
        /// Pide una voz. Si no hay ninguna sonando arranca ya; si hay otra, espera en cola.
        /// Una porra que llega mientras suena una canción se DESCARTA: cuando le tocara el
        /// turno ya no tendría sentido (habría pasado el momento del golpe).
        /// </summary>
        public void RequestVoice(string path, float volume, float delay, string onlyInScene,
            VoiceKind kind, string fallback = null)
        {
            var req = new Pending
            {
                at = Time.unscaledTime + Mathf.Max(0f, delay),
                path = path,
                volume = Mathf.Clamp01(volume),
                scene = onlyInScene,
                kind = kind,
                fallback = fallback
            };
            if (delay > 0.01f) _pending.Add(req);
            else StartVoice(req);
        }

        void StartVoice(Pending req)
        {
            // La canción del personaje puede no existir (Flavia no tiene grabación): se
            // busca sin avisar y se cae al himno, que es una solución musical, no un error.
            var clip = string.IsNullOrEmpty(req.fallback) ? Clip(req.path) : ClipQuiet(req.path);
            if (clip == null && !string.IsNullOrEmpty(req.fallback)) clip = Clip(req.fallback);
            if (clip == null) return;

            // Relevo del himno: una victoria no puede esperar 12 s a que termine una porra.
            // Se funde la que suena en 0.45 s y entra la nueva: es un relevo, no un corte.
            if (req.kind == VoiceKind.Hymn && VoicePlaying && _voiceKind == VoiceKind.Chant)
                ClearVoice(0.45f);

            if (VoicePlaying || _voiceGap > 0f)
            {
                if (req.kind == VoiceKind.Chant && (VoicePlaying && _voiceKind != VoiceKind.Chant))
                    return;     // la porra caduca: mejor perderla que pisar la canción
                Enqueue(clip, req.volume, req.kind);
                return;
            }
            BeginVoice(clip, req.volume, req.kind);
        }

        void Enqueue(AudioClip clip, float volume, VoiceKind kind)
        {
            if (_queue.Count >= QueueLimit)
            {
                // Cola llena: la petición de menos peso se cae. Una canción siempre
                // desplaza a una porra; entre iguales, la más antigua cede el sitio.
                int weakest = -1;
                for (int i = 0; i < _queue.Count; i++)
                {
                    if (_queue[i].kind < kind) { weakest = i; break; }
                }
                if (weakest < 0)
                {
                    if (kind == VoiceKind.Chant) return;
                    weakest = 0;
                }
                _queue.RemoveAt(weakest);
            }
            _queue.Add(new QueuedVoice { clip = clip, volume = volume, kind = kind });
        }

        void BeginVoice(AudioClip clip, float volume, VoiceKind kind)
        {
            _voice.Stop();
            _voice.clip = clip;
            _voice.time = 0f;
            _voice.volume = 0f;
            _voice.Play();
            _voiceLen = clip.length;
            _voiceTime = 0f;
            _voiceGain = Mathf.Clamp01(volume);
            _voiceKind = kind;
            _voiceCut = -1f;
        }

        /// <summary>Funde la voz actual y vacía la cola. `fade` en segundos.</summary>
        void ClearVoice(float fade)
        {
            _queue.Clear();
            if (!VoicePlaying) { _voiceGap = 0f; return; }
            _voiceCutLen = Mathf.Max(0.02f, fade);
            _voiceCut = _voiceCutLen;
        }

        // ------------------------------------------------------------------ SFX

        public void PlayClip(string path, float volume, float delay, string onlyInScene)
        {
            var clip = Clip(path);
            if (clip == null) return;
            if (delay > 0.01f)
            {
                _pending.Add(new Pending
                {
                    at = Time.unscaledTime + delay,
                    path = path,
                    volume = Mathf.Clamp01(volume),
                    scene = onlyInScene,
                    kind = VoiceKind.Chant,
                    sfx = true
                });
                return;
            }
            int idx = NextShotIndex();
            var src = _shots[idx];
            src.Stop();
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume);
            src.Play();
            _shotTarget[idx] = src.volume;
        }

        int NextShotIndex()
        {
            for (int i = 0; i < _shots.Length; i++)
            {
                int idx = (_shotCursor + i) % _shots.Length;
                if (!_shots[idx].isPlaying)
                {
                    _shotCursor = (idx + 1) % _shots.Length;
                    return idx;
                }
            }
            // Todas ocupadas: se roba la siguiente del turno (un golpe que se pisa con
            // otro es preferible a perder el impacto).
            int steal = _shotCursor;
            _shotCursor = (_shotCursor + 1) % _shots.Length;
            return steal;
        }

        // ------------------------------------------------------------------ mezcla

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            string scene = SceneManager.GetActiveScene().name;

            // Un canto no sobrevive a un cambio de pantalla: si el jugador se va a mitad
            // de una porra, se funde en vez de aparecer en la escena siguiente.
            if (scene != _scene)
            {
                _scene = scene;
                _pending.Clear();
                ClearVoice(SceneVoiceFade);
                for (int i = 0; i < _shotTarget.Length; i++) _shotTarget[i] = 0f;
            }

            // Los niveles de partida se toman ANTES de tocar nada: TickVoice escribe el
            // volumen de la voz dentro de este mismo frame, y medir despues daria 0.
            float prevMenu = _menu.volume, prevBed = _bed.volume;
            float prevA = _combatA.volume, prevB = _combatB.volume;
            float prevVoice = _voice.volume;

            TickPending(scene);
            TickVoice(dt);

            // --- niveles de cama y música (lentos)
            _menuLevel = Mathf.MoveTowards(_menuLevel, _menuTarget, dt * MusicFade);
            _bedLevel = Mathf.MoveTowards(_bedLevel, _bedTarget, dt * BedFade);
            _combatLevelA = Mathf.MoveTowards(_combatLevelA, _combatOnB ? 0f : _combatTarget, dt * MusicFade);
            _combatLevelB = Mathf.MoveTowards(_combatLevelB, _combatOnB ? _combatTarget : 0f, dt * MusicFade);

            // --- ducking (rápido al entrar, lento al salir: nunca "bombea")
            float duckTarget = VoicePlaying ? 1f : 0f;
            float duckRate = duckTarget > _duck ? 4.5f : 1.4f;
            _duck = Mathf.MoveTowards(_duck, duckTarget, dt * duckRate);

            _menu.volume = _menuLevel * Mathf.Lerp(1f, MusicDuck, _duck);
            _combatA.volume = _combatLevelA * Mathf.Lerp(1f, MusicDuck, _duck);
            _combatB.volume = _combatLevelB * Mathf.Lerp(1f, MusicDuck, _duck);
            _bed.volume = _bedLevel * Mathf.Lerp(1f, BedDuck, _duck);

            // Telemetría de la mezcla: la velocidad REAL aplicada por frame. Se mide aquí,
            // con el mismo dt con el que se escribió el volumen, y no desde fuera: un
            // muestreador externo reparte un salto entre los frames que no vio y atribuye
            // al mezclador un corte que no existió.
            Measure(prevMenu, _menu.volume, dt, ref _menuMaxRate);
            Measure(Mathf.Max(prevA, prevB), Mathf.Max(_combatA.volume, _combatB.volume), dt, ref _combatMaxRate);
            Measure(prevBed, _bed.volume, dt, ref _bedMaxRate);
            Measure(prevVoice, _voice.volume, dt, ref _voiceMaxRate);

            // Pausar en silencio: una cama en bucle decodifica aunque no se oiga.
            PauseIfSilent(_menu, _menuTarget, _menuLevel);
            PauseIfSilent(_bed, _bedTarget, _bedLevel);
            if (_combatTarget <= 0f)
            {
                PauseIfSilent(_combatA, 0f, _combatLevelA);
                PauseIfSilent(_combatB, 0f, _combatLevelB);
            }

            for (int i = 0; i < _shots.Length; i++)
            {
                var src = _shots[i];
                if (!src.isPlaying) continue;
                if (Mathf.Abs(src.volume - _shotTarget[i]) > 0.001f)
                    src.volume = Mathf.MoveTowards(src.volume, _shotTarget[i], dt * 1.4f);
                if (_shotTarget[i] <= 0.001f && src.volume <= 0.003f) src.Stop();
            }
        }

        // ------------------------------------------------------------------ telemetría
        float _menuMaxRate, _combatMaxRate, _bedMaxRate, _voiceMaxRate;

        public float MenuMaxRate => _menuMaxRate;
        public float CombatMaxRate => _combatMaxRate;
        public float BedMaxRate => _bedMaxRate;
        public float VoiceMaxRate => _voiceMaxRate;

        public void ResetRates()
        {
            _menuMaxRate = _combatMaxRate = _bedMaxRate = _voiceMaxRate = 0f;
        }

        static void Measure(float before, float after, float dt, ref float worst)
        {
            if (dt <= 0f) return;
            float rate = Mathf.Abs(after - before) / dt;
            if (rate > worst) worst = rate;
        }

        static void PauseIfSilent(AudioSource src, float target, float level)
        {
            if (target <= 0f && level <= 0.002f && src.isPlaying) src.Pause();
        }

        void TickPending(string scene)
        {
            if (_pending.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (!string.IsNullOrEmpty(p.scene) && p.scene != scene)
                {
                    _pending.RemoveAt(i);
                    continue;
                }
                if (now < p.at) continue;
                _pending.RemoveAt(i);
                // Un SFX va a su pool; una voz entra por la cola exclusiva.
                if (p.sfx) PlayClip(p.path, p.volume, 0f, p.scene);
                else StartVoice(p);
            }
        }

        /// <summary>
        /// La voz: fundido de entrada corto, fundido de cola sobre el propio clip (así el
        /// archivo nunca se acaba de golpe) y espera entre voces para que dos porras no se
        /// encadenen sin respirar.
        /// </summary>
        void TickVoice(float dt)
        {
            if (_voiceGap > 0f) _voiceGap = Mathf.Max(0f, _voiceGap - dt);

            if (VoicePlaying)
            {
                _voiceTime += dt;
                float fade = 1f;
                if (_voiceTime < VoiceFadeIn) fade = _voiceTime / VoiceFadeIn;
                float remain = _voiceLen - _voiceTime;
                if (remain < VoiceFadeOut) fade = Mathf.Min(fade, Mathf.Max(0f, remain / VoiceFadeOut));
                if (_voiceCut > 0f)
                {
                    _voiceCut -= dt;
                    fade = Mathf.Min(fade, Mathf.Max(0f, _voiceCut / _voiceCutLen));
                }
                _voice.volume = Mathf.MoveTowards(_voice.volume, _voiceGain * Mathf.Clamp01(fade),
                    dt * VoiceRampPerSecond);

                if (_voiceCut <= 0f && _voiceCut > -1f)
                {
                    VoiceStopReason = $"fundido forzado en t={_voiceTime:0.00}/{_voiceLen:0.00}";
                    _voice.Stop(); _voiceCut = -1f; _voiceGap = 0.2f;
                }
                else if (remain <= 0.03f)
                {
                    VoiceStopReason = $"fin de clip en t={_voiceTime:0.00}/{_voiceLen:0.00}";
                    _voice.Stop(); _voiceCut = -1f; _voiceGap = VoiceGapSeconds;
                }
            }
            else if (_voiceCut > 0f)
            {
                _voiceCut = -1f;
            }

            if (!VoicePlaying && _voiceGap <= 0f && _queue.Count > 0)
            {
                var next = _queue[0];
                _queue.RemoveAt(0);
                BeginVoice(next.clip, next.volume, next.kind);
            }
        }
    }
}
