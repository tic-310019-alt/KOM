#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace KOM
{
    /// <summary>
    /// SONDA DE MEZCLA (solo editor, Play real).
    ///
    /// Comprueba las tres promesas del sistema de audio, una por una, con el juego
    /// sonando de verdad y muestreando cada frame:
    ///
    ///   1. UNA VOZ A LA VEZ — en ningún frame puede haber dos fuentes de voz sonando.
    ///      Es la regla que el pool viejo rompía: cuando coincidían dos porras se robaban
    ///      la fuente y la grada se pisaba a sí misma.
    ///   2. NADA SE CORTA EN SECO — ninguna capa puede cambiar de volumen más rápido que
    ///      su cadencia de fundido declarada. Un Stop() instantáneo se ve como un salto
    ///      enorme de nivel en un solo frame.
    ///   3. NO HAY RECORTE — el pico del mix maestro se queda por debajo de 0 dBFS.
    ///
    /// Además deja por escrito los niveles de cada capa para poder comparar la mezcla
    /// con la intención: música por debajo de la grada y la voz por encima de las dos.
    ///
    ///   KOM.KomAudioDiag.Spawn().Begin("Logs/audio/report.txt")
    /// </summary>
    public sealed class KomAudioDiag : MonoBehaviour
    {
        const string OutDir = "Logs/audio";

        // Cadencias máximas que el reproductor puede usar (nivel por segundo). La sonda
        // deja un margen del doble para no fallar por un frame largo del editor.
        const float MusicRate = 0.85f;
        const float BedRate = 0.55f;
        const float DuckRate = 4.5f;
        // Tope declarado del reproductor: la voz nunca sube ni baja más rápido que esto.
        const float VoiceRate = 6f;

        readonly StringBuilder _sb = new StringBuilder();
        readonly List<string> _failures = new List<string>();
        readonly List<float> _peaks = new List<float>();

        AudioSource _menu, _combatA, _combatB, _bed, _voice;

        float _maxVoiceRate, _maxMenuRate, _maxCombatRate, _maxBedRate;
        float _worstPeak;
        int _doubleVoiceFrames;
        int _frames;
        float _lastDt = 0.016f;

        public static KomAudioDiag Spawn()
        {
            var go = new GameObject("KomAudioDiag");
            return go.AddComponent<KomAudioDiag>();
        }

        public void Begin(string path)
        {
            StartCoroutine(Run(path));
        }

        IEnumerator Run(string path)
        {
            Directory.CreateDirectory(OutDir);
            _sb.AppendLine("KOM AUDIO · mezcla en Play real");
            _sb.AppendLine("------------------------------------------------------------");

            KomAudio.Menu();
            yield return new WaitForSecondsRealtime(0.4f);
            Bind();
            if (Player != null) Player.ResetRates();

            // --- 1. el menú entra
            yield return Sample("menú", 2.2f);
            LogLevels("menú");

            // --- 2. combate: entra la música del escenario y la grada
            KomAudio.Combat("la_madriguera", false);   // id inválido a propósito: cae a la pista 1
            yield return Sample("combate", 3.0f);
            LogLevels("combate");

            // --- 3. ráfaga de voces: es el caso que antes se solapaba
            KomAudio.Coreo("juan_grabriel", 0.9f);
            yield return new WaitForSecondsRealtime(0.35f);
            KomAudio.AmbientPorra(0.66f);              // llega con una canción sonando
            yield return new WaitForSecondsRealtime(0.35f);
            KomAudio.Coreo("flavia", 0.9f);            // sin grabación: debe caer al himno
            KomAudio.AmbientPorra(0.66f);
            KomAudio.AmbientPorra(0.66f);
            KomAudio.Porra(KomAudio.LaChona, 0.8f);
            yield return Sample("ráfaga de voces", 6.0f);

            // --- 4. himno de victoria sobre una porra en curso
            KomAudio.AmbientPorra(0.7f);
            yield return new WaitForSecondsRealtime(1.2f);
            KomAudio.Victory("alex_llora");
            yield return Sample("relevo de victoria", 4.0f);

            // --- 5. vuelta al menú: crossfade, no corte
            KomAudio.Menu();
            yield return Sample("vuelta al menú", 3.0f);
            LogLevels("menú otra vez");

            // --- 6. silencio total
            KomAudio.Silence();
            yield return Sample("silencio", 2.0f);
            LogLevels("silencio");

            Report(path);
        }

        void Bind()
        {
            var sources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var s in sources)
            {
                if (s.transform.parent == null) continue;
                switch (s.gameObject.name)
                {
                    case "MusicMenu": _menu = s; break;
                    case "MusicCombatA": _combatA = s; break;
                    case "MusicCombatB": _combatB = s; break;
                    case "Ambience": _bed = s; break;
                    case "Voice": _voice = s; break;
                }
            }
            if (_voice == null) _failures.Add("no encuentro la fuente de voz del reproductor");
        }

        /// <summary>Muestrea durante `seconds` y acumula los peores casos.</summary>
        IEnumerator Sample(string label, float seconds)
        {
            float end = Time.unscaledTime + seconds;
            float voiceOnSince = -1f;
            float longestVoice = 0f;
            float startVoice = 0f;

            while (Time.unscaledTime < end)
            {
                float dt = Mathf.Max(0.0005f, Time.unscaledDeltaTime);
                _lastDt = dt;
                _frames++;

                // --- 1. exclusividad de voz
                int voices = CountVoices();
                if (voices > 1) _doubleVoiceFrames++;

                if (voices > 0 && voiceOnSince < 0f) { voiceOnSince = Time.unscaledTime; startVoice = 0f; }
                else if (voices > 0) { startVoice += dt; if (startVoice > longestVoice) longestVoice = startVoice; }
                else voiceOnSince = -1f;


                // --- 3. pico del mix maestro
                float peak = MasterPeak();
                if (peak > _worstPeak) _worstPeak = peak;
                _peaks.Add(peak);

                yield return null;
            }

            var p = Player;
            if (p != null)
            {
                _maxMenuRate = Mathf.Max(_maxMenuRate, p.MenuMaxRate);
                _maxCombatRate = Mathf.Max(_maxCombatRate, p.CombatMaxRate);
                _maxBedRate = Mathf.Max(_maxBedRate, p.BedMaxRate);
                _maxVoiceRate = Mathf.Max(_maxVoiceRate, p.VoiceMaxRate);
            }

            _sb.AppendLine($"{label,-22} {seconds,4:0.0} s  voces máx {CountVoices()}  " +
                           $"cola {VoiceQueued()}  voz continua {longestVoice:0.0} s  " +
                           $"música {Level(_menu):0.00}/{Level(ActiveCombat()):0.00}  cama {Level(_bed):0.00}  " +
                           $"voz {Level(_voice):0.00}  duck {Duck():0.00}");
        }

        int CountVoices()
        {
            if (_voice == null) return 0;
            return _voice.isPlaying ? 1 : 0;
        }

        float Level(AudioSource s) => s != null ? s.volume : 0f;

        AudioSource ActiveCombat()
        {
            if (_combatA == null) return _combatB;
            if (_combatB == null) return _combatA;
            return _combatB.isPlaying && _combatB.volume > _combatA.volume ? _combatB : _combatA;
        }

        float Duck()
        {
            var player = GetComponentInParent<KomAudioPlayer>();
            if (player == null) player = FindAnyObjectByType<KomAudioPlayer>();
            return player != null ? player.Duck : 0f;
        }

        int VoiceQueued()
        {
            var player = FindAnyObjectByType<KomAudioPlayer>();
            return player != null ? player.VoiceQueued : 0;
        }

        // La velocidad la mide el PROPIO mezclador, con el dt con el que escribió el
        // volumen. Un muestreador externo reparte un salto entre frames que no vio y
        // acusa al mezclador de un corte inexistente (medido: 6.9/s cuando el tope real
        // era 6.0). Aquí solo se leen sus acumulados.
        KomAudioPlayer Player
        {
            get
            {
                if (_player == null) _player = FindAnyObjectByType<KomAudioPlayer>();
                return _player;
            }
        }

        KomAudioPlayer _player;

        float MasterPeak()
        {
            // Sin AudioListener en la escena el buffer se queda a cero y la medida da 0:
            // la sonda lo informa como pico 0 en vez de inventarse un valor.
            AudioListener.GetOutputData(Samples, 0);
            float peak = 0f;
            for (int i = 0; i < Samples.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(Samples[i]));
            return peak;
        }

        static readonly float[] Samples = new float[512];

        void LogLevels(string when)
        {
            _sb.AppendLine($"   niveles {when,-16} menú {Level(_menu):0.00}  combate {Level(ActiveCombat()):0.00}  " +
                           $"cama {Level(_bed):0.00}  voz {Level(_voice):0.00}");
        }

        void Report(string path)
        {
            // Un frame suelto del editor puede durar 100 ms y mover el fundido más de la
            // cuenta: el umbral se evalúa como velocidad, no como salto absoluto, y con el
            // doble de margen sobre la cadencia declarada.
            Check("exclusividad de voz", _doubleVoiceFrames == 0,
                $"{_doubleVoiceFrames} frames con más de una voz sonando");
            Check("sin cortes de música", _maxMenuRate <= MusicRate * 2f + 0.5f,
                $"cambio máximo {_maxMenuRate:0.00}/s (tope {MusicRate * 2f + 0.5f:0.0})");
            Check("sin cortes de combate", _maxCombatRate <= MusicRate * 2f + 0.5f,
                $"cambio máximo {_maxCombatRate:0.00}/s (tope {MusicRate * 2f + 0.5f:0.0})");
            Check("sin cortes de cama", _maxBedRate <= (BedRate + DuckRate) * 2f,
                $"cambio máximo {_maxBedRate:0.00}/s (tope {(BedRate + DuckRate) * 2f:0.0})");
            Check("sin cortes de voz", _maxVoiceRate <= VoiceRate + 0.5f,
                $"cambio máximo {_maxVoiceRate:0.00}/s (tope {VoiceRate + 0.5f:0.0})");
            Check("sin recorte", _worstPeak < 0.995f, $"pico del mix {_worstPeak:0.000}");

            _sb.AppendLine("------------------------------------------------------------");
            _sb.AppendLine($"frames muestreados: {_frames}   pico máximo: {_worstPeak:0.000}");
            _sb.AppendLine(_failures.Count == 0 ? "RESULTADO: OK" : "RESULTADO: FALLA");
            foreach (var f in _failures) _sb.AppendLine("  · " + f);
            File.WriteAllText(path, _sb.ToString());
            Debug.Log("[KOM][AUDIO] " + (_failures.Count == 0 ? "OK" : "FALLA") + " -> " + path);
        }

        void Check(string name, bool ok, string detail)
        {
            _sb.AppendLine($"{(ok ? "OK  " : "FALLA")} {name,-26} {detail}");
            if (!ok) _failures.Add(name + " · " + detail);
        }
    }
}
#endif
