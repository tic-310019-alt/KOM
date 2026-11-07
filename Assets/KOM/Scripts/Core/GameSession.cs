using System;
using System.Collections.Generic;
using UnityEngine;

namespace KOM
{
    public sealed class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }

        public string PlayerId { get; private set; }
        public string[] Opponents { get; private set; } = Array.Empty<string>();
        public int EncounterIndex { get; private set; }

        // El rival de cada encuentro se presenta en la ruleta justo antes de pelear. Se
        // recuerda cual fue el ultimo encuentro presentado para no repetir la escena al
        // volver al mapa o al reintentar un combate perdido.
        int _revealedEncounter = -1;

        public bool IsEncounterRevealed => _revealedEncounter == EncounterIndex;

        public void MarkEncounterRevealed()
        {
            _revealedEncounter = EncounterIndex;
        }

        // Momentos narrativos ya vistos en esta partida (ver StoryFlow.PlayOnce). Evita que
        // el veto o la antesala del combate final se repitan tras un reintento.
        readonly HashSet<string> _playedStories = new HashSet<string>();

        public bool HasPlayedStory(string key) => !string.IsNullOrEmpty(key) && _playedStories.Contains(key);

        public void MarkStoryPlayed(string key)
        {
            if (!string.IsNullOrEmpty(key)) _playedStories.Add(key);
        }

        // ------------------------------------------------------------------ versus local
        //
        // El modo de dos jugadores no usa el torneo: no hay rivales sorteados ni progreso,
        // solo dos luchadores elegidos a mano, un escenario y un numero de rondas. Por eso
        // vive aparte de PlayerId/Opponents y `HasRun` sigue siendo falso mientras dure.

        public bool VersusMode { get; private set; }
        public string VersusPlayer1Id { get; private set; }
        public string VersusPlayer2Id { get; private set; }
        public string VersusStageId { get; private set; }
        /// <summary>Rondas que hay que ganar. 1 = a una, 2 = al mejor de 3, 3 = al mejor de 5.</summary>
        public int VersusRoundsToWin { get; private set; } = 2;

        public CharacterDef VersusPlayer1Def => KomData.GetCharacter(VersusPlayer1Id);
        public CharacterDef VersusPlayer2Def => KomData.GetCharacter(VersusPlayer2Id);
        public StageDef VersusStage => KomData.GetStage(VersusStageId);

        public void BeginVersus()
        {
            // No se toca la partida del torneo: entrar a 2 jugadores no puede borrar un
            // CONTINUAR. El modo se apaga al volver al menu (MainMenuController.Start).
            VersusMode = true;
            VersusPlayer1Id = null;
            VersusPlayer2Id = null;
            VersusRoundsToWin = 2;
            var stages = KomData.Stages.stages;
            VersusStageId = stages.Length > 0 ? stages[0].id : null;
        }

        public void SetVersusFighter(int player, string id)
        {
            if (player <= 1) VersusPlayer1Id = id;
            else VersusPlayer2Id = id;
        }

        public void SetVersusStage(string stageId)
        {
            if (!string.IsNullOrEmpty(stageId)) VersusStageId = stageId;
        }

        public void SetVersusRounds(int roundsToWin)
        {
            VersusRoundsToWin = Mathf.Clamp(roundsToWin, 1, 3);
        }

        public void ExitVersus()
        {
            VersusMode = false;
            VersusPlayer1Id = null;
            VersusPlayer2Id = null;
            VersusStageId = null;
        }

        public bool HasRun => !string.IsNullOrEmpty(PlayerId);
        public bool IsRunComplete =>
            HasRun && KomData.Tournament.nodes.Length > 0 &&
            EncounterIndex >= KomData.Tournament.nodes.Length;

        public int NodeCount => KomData.Tournament.nodes.Length;

        public static GameSession Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("GameSession");
            return go.AddComponent<GameSession>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public CharacterDef PlayerDef => KomData.GetCharacter(PlayerId);

        public TournamentNodeDef CurrentNode
        {
            get
            {
                var nodes = KomData.Tournament.nodes;
                if (nodes.Length == 0) return null;
                return nodes[Mathf.Clamp(EncounterIndex, 0, nodes.Length - 1)];
            }
        }

        public string OpponentIdForNode(int nodeIndex)
        {
            var nodes = KomData.Tournament.nodes;
            if (nodes.Length == 0) return null;
            if (nodeIndex >= nodes.Length - 1) return KomData.Boss != null ? KomData.Boss.id : null;
            if (Opponents.Length == 0) return null;
            return Opponents[Mathf.Clamp(nodeIndex, 0, Opponents.Length - 1)];
        }

        public string CurrentOpponentId => IsRunComplete ? null : OpponentIdForNode(EncounterIndex);

        /// <summary>
        /// Rivales ya derrotados en esta partida. La jefa final hereda de ellos su poder
        /// especial (`Fighter.AbsorbSpecials`), asi que la lista de poderes de la pelea final
        /// depende del orden sorteado y no de una tabla fija.
        /// </summary>
        public string[] DefeatedOpponents
        {
            get
            {
                int n = Mathf.Clamp(EncounterIndex, 0, Opponents?.Length ?? 0);
                var arr = new string[n];
                for (int i = 0; i < n; i++) arr[i] = Opponents[i];
                return arr;
            }
        }

        public CharacterDef CurrentOpponentDef => KomData.GetCharacter(CurrentOpponentId);

        public void BeginRun(string playerId)
        {
            PlayerId = playerId;
            Opponents = ResolveOpponents(playerId);
            EncounterIndex = 0;
            _revealedEncounter = -1;
        }

        public void CompleteEncounter()
        {
            if (EncounterIndex < NodeCount) EncounterIndex++;
        }

#if UNITY_EDITOR
        // Diagnostico explicito para enfrentar a un rival concreto (p. ej. la jefa)
        // sin tocar el torneo. No existe en builds.
        public void DebugSetOpponents(string[] ids)
        {
            Opponents = ids ?? Array.Empty<string>();
        }
#endif

        public void ResetRun()
        {
            ExitVersus();
            PlayerId = null;
            Opponents = Array.Empty<string>();
            EncounterIndex = 0;
            _revealedEncounter = -1;
            _playedStories.Clear();
            StoryFlow.Clear();
        }

        /// <summary>
        /// Rivales del torneo para un jugador. El ROSTER se sortea (Fisher-Yates: cada
        /// partida enfrenta a tres rivales distintos), pero el ORDEN de los tres se fija
        /// por poder de combate ascendente. Antes tambien se barajaba el orden y la ronda
        /// 1 podia caer contra Alex (112 PV, 1.15 de dano) mientras la 3 caia contra
        /// Natanael (95 PV, 0.95): la dificultad no era progresiva. Ahora el jugador se
        /// enfrenta al mas flojo, luego al intermedio y luego al mas fuerte, y la final
        /// sigue siendo la jefa. El resultado se guarda en <see cref="Opponents"/> y el
        /// mapa/combate ven siempre el mismo rival para el mismo encuentro.
        /// </summary>
        public static string[] ResolveOpponents(string playerId)
        {
            var pool = new List<string>();
            foreach (var id in KomData.Tournament.opponentRotation)
            {
                if (id == playerId) continue;
                var def = KomData.GetCharacter(id);
                if (def == null || !def.playable) continue;
                if (pool.Contains(id)) continue;
                pool.Add(id);
            }

            // Fisher-Yates: cada partida sortea un roster distinto.
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            var result = new List<string>();
            foreach (var id in pool)
            {
                result.Add(id);
                if (result.Count >= 3) break;
            }

            // La ruta es progresiva aunque el roster sea aleatorio: el mas flojo abre y
            // el mas fuerte cierra. La comparacion es determinista (desempate por id).
            result.Sort((a, b) =>
            {
                int cmp = CombatPower(KomData.GetCharacter(a)).CompareTo(CombatPower(KomData.GetCharacter(b)));
                return cmp != 0 ? cmp : string.CompareOrdinal(a, b);
            });
            return result.ToArray();
        }

        /// <summary>
        /// Poder de combate para ordenar la ruta. Es una medida EXPLICITA de lo que el
        /// jugador siente: vida efectiva, movilidad para cerrar distancia y dano del
        /// especial. El mismo JSON da la misma ruta.
        /// </summary>
        public static float CombatPower(CharacterDef def)
        {
            if (def == null) return float.MaxValue;
            var special = def.GetSpecial(0);
            float specialDamage = special != null ? special.damage : 0f;
            return def.maxHealth * def.damageScale + def.dashSpeed * 2f + specialDamage * 0.35f;
        }
    }
}
