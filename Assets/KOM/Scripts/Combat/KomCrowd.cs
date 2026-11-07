using UnityEngine;

namespace KOM
{
    /// <summary>
    /// La grada como sistema de reacción al combate.
    ///
    /// Las porras y las canciones no son decorado de menú: son el público. Suenan cuando
    /// el público tendría algo que decir, y con un enfriamiento para que no se conviertan
    /// en ruido blanco.
    ///
    ///   · inicio del asalto · UNA sola voz: la canción del que pelea (o el himno en la
    ///                         final). Antes sonaban las dos seguidas y se pisaban;
    ///   · golpe que mueve a la grada · porra corta, nunca encima de una canción;
    ///   · súper listo · la grada empuja con una porra (una vez por asalto);
    ///   · alguien contra las cuerdas · la grada lo empuja (una vez por asalto y lado).
    ///
    /// Los tiempos largos no son timidez: las porras nuevas duran 8–17 s. Con el
    /// enfriamiento viejo (7 s) la grada encadenaba una porra detrás de otra sin callarse
    /// nunca. Aquí la regla es la contraria: la grada habla a ratos y deja respirar la
    /// pelea.
    ///
    /// Todo va con `onlyInScene = Combat`: si una voz se cruza con un cambio de escena, el
    /// reproductor la funde en vez de dejarla sonando sobre la selección o el mapa.
    /// </summary>
    public static class KomCrowd
    {
        // Una porra cada 20 s como mucho: la grada reacciona a los momentos, no a cada golpe.
        const float ChantCooldown = 20f;
        // Las canciones con nombre son más raras todavía: son un premio, no un adorno.
        const float CoreoCooldown = 26f;
        // Al empezar el asalto la canción de entrada ya está sonando: la grada espera.
        const float RoundGrace = 4f;

        static float _lastChant = -999f;
        static float _lastCoreo = -999f;
        static float _roundClock;
        static bool _superCalled;
        static bool _lowPlayer;
        static bool _lowEnemy;

        public static void ResetRound()
        {
            _superCalled = false;
            _lowPlayer = false;
            _lowEnemy = false;
            _roundClock = 0f;
        }

        /// <summary>Avanza el reloj del asalto. Lo llama el controlador de combate.</summary>
        public static void Tick(float dt) => _roundClock += dt;

        /// <summary>
        /// Canción de entrada del asalto. Una sola: en la final manda el himno del torneo,
        /// en la ronda 1 empuja el jugador y en la 2 responde el rival.
        /// </summary>
        public static void RoundIntro(string playerId, string enemyId, int round, bool finalFight)
        {
            _lastCoreo = Time.time;
            if (finalFight)
            {
                KomAudio.Porra(KomAudio.KingOfMusic, 0.92f, 0.35f, KomScenes.Combat);
                return;
            }
            string id = round == 2 ? enemyId : playerId;
            KomAudio.Coreo(id, 0.88f, 0.35f, KomScenes.Combat);
        }

        /// <summary>Golpe que mueve a la grada (fuerte, multi-golpe o especial).</summary>
        public static void BigHit(bool special)
        {
            Chant(special ? 0.66f : 0.58f, 0f, false);
        }

        /// <summary>El público corea el nombre del luchador (canción completa).</summary>
        public static void Coreo(string characterId, float volume, bool force = false)
        {
            if (string.IsNullOrEmpty(characterId)) return;
            if (!force && Time.time - _lastCoreo < CoreoCooldown) return;
            _lastCoreo = Time.time;
            KomAudio.Coreo(characterId, volume, 0f, KomScenes.Combat);
        }

        /// <summary>Súper listo: una vez por asalto, la grada empuja con una porra corta.</summary>
        public static void SuperReady(string characterId)
        {
            if (_superCalled) return;
            _superCalled = true;
            Chant(0.7f, 0.1f, true);
        }

        /// <summary>Alguien está contra las cuerdas: una vez por asalto y por luchador.</summary>
        public static void LowHealth(bool player, string characterId)
        {
            if (player)
            {
                if (_lowPlayer) return;
                _lowPlayer = true;
            }
            else
            {
                if (_lowEnemy) return;
                _lowEnemy = true;
            }
            Chant(0.64f, 0.1f, true);
        }

        static void Chant(float volume, float delay, bool force)
        {
            if (!force && Time.time - _lastChant < ChantCooldown) return;
            if (_roundClock < RoundGrace) return;
            _lastChant = Time.time;
            KomAudio.AmbientPorra(volume, delay, KomScenes.Combat);
        }
    }
}
