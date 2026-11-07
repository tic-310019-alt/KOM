using System;

namespace KOM
{
    public enum AttackType
    {
        LightPunch,
        HeavyPunch,
        LightKick,
        HeavyKick,
        LowKick,
        Uppercut,
        DropKick,
        QuadPunch,
        Combo1,
        Combo2,
        Combo3
    }

    // Los CUATRO botones de ataque de la especificacion. Cada uno tiene UNA accion y solo
    // una: no hay modificadores por agacharse ni por correr. Esa era la fuente de que el
    // mismo boton hiciera cosas distintas segun el contexto.
    public enum AttackButton
    {
        LightPunch,   // G: golpe rapido
        HeavyPunch,   // H: golpe fuerte
        LightKick,    // J: patada rapida
        HeavyKick     // K: patada fuerte
    }

    [Serializable]
    public sealed class AttackSpec
    {
        public AttackType type;
        public string displayName;
        public string animatorState;
        public float duration;
        public float hitTime;
        public float damage;
        public float range;
        public float height;
        public float knockback;
        public float hitstop;
        public float activeDuration;
        public float hitboxForward;
        public float hitboxWidth;
        public float hitboxHeight;
        public float hitboxCenterY;
        public bool high;
        public bool heavy;
        public bool upperBody;
        // Rompe la guardia: dano a la barra de guardia (0..1).
        public float guardDamage = 0.15f;
        // Un uppercut conecta a un rival en salto (el resto ignora a >0.4 m).
        public bool antiAir;
        // Avance durante la ejecucion (drop kick / low kick).
        public float lunge;
        public float lungeDuration;
        // Multi-golpe: QuadPunch conecta 4 veces. El resto de ataques, 1.
        public int hitCount = 1;
        public float hitInterval;
        // Tiempos de golpe EXPLICITOS en segundos del estado (los combos por secuencia
        // no tienen cadencia uniforme). Si esta vacio se usa hitTime + i*hitInterval.
        public float[] hitTimes;
        // Reaccion especial del rival al conectar (p. ej. GroinHit) y su hitstun.
        public string hitReaction;
        // Ventana de indefension del rival. Es el "frame advantage" del golpe: si es
        // mayor que `duration / speed`, el atacante recupera antes y puede encadenar.
        public float hitStun;
        // Velocidad fija del estado. 0 = reproducir el clip completo en `duration`.
        // Los ataques con cola larga (uppercut/dropkick) se cortan al terminar la accion.
        public float animSpeed;

        // Multiplicador de reproduccion en juego (1 = como lo horneo KomSetup, que
        // ajusta la velocidad del estado a clip.length / duration). Todo el timing
        // jugable se divide por este valor: duracion, ventanas de golpe y avance. Asi
        // el clip y la logica siguen mirando EXACTAMENTE el mismo frame, sin rehornear.
        public float speed = 1f;

        /// <summary>Duracion real del ataque en juego, en segundos.</summary>
        public float Window => duration / (speed > 0.01f ? speed : 1f);

        /// <summary>Instante real del golpe `index` dentro de la ventana jugable.</summary>
        public float HitTimeAt(int index)
        {
            float raw = hitTimes != null && index < hitTimes.Length
                ? hitTimes[index]
                : hitTime + index * hitInterval;
            return raw / (speed > 0.01f ? speed : 1f);
        }

        /// <summary>Duracion real de la ventana activa del golpe.</summary>
        public float ActiveWindow => activeDuration / (speed > 0.01f ? speed : 1f);

        /// <summary>Duracion real del avance declarado.</summary>
        public float LungeWindow => lungeDuration / (speed > 0.01f ? speed : 1f);
    }

    public static class Attacks
    {
        // ---------------------------------------------------------------------------
        // FRAME DATA
        // ---------------------------------------------------------------------------
        // `duration` sigue siendo el ajuste del clip (KomSetup hornea
        // state.speed = clip.length / duration). `speed` acelera la reproduccion en
        // juego sin tocar ese horneado: la duracion jugable es duration / speed.
        // Referencia a 60 Hz: 0.05 s = 3 frames.
        //
        // `hitStun` se declara por golpe. La diferencia contra la duracion es el
        // frame advantage en impacto: positivo = el atacante recupera antes.

        public static readonly AttackSpec LightPunch = new AttackSpec
        {
            type = AttackType.LightPunch, displayName = "Golpe rapido", animatorState = "LightPunch",
            // Alcance: la caja llega a 0.86 + 0.31 = 1.17 m del centro, que con el radio de
            // la hurtbox rival (~0.33) da 1.50 m. Antes se quedaba en 1.39 y el jab se
            // sentia corto. `range` acompana a la caja para que la IA no decida con otro numero.
            duration = 0.62f, hitTime = 0.26f, damage = 5f, range = 1.50f, height = 1.35f,
            knockback = 0.5f, hitstop = 0.055f, activeDuration = 0.10f,
            hitboxForward = 0.86f, hitboxWidth = 0.62f, hitboxHeight = 0.35f, hitboxCenterY = 1.35f,
            high = true, heavy = false, upperBody = true, guardDamage = 0.14f,
            speed = 1.30f, hitStun = 0.52f
        };

        public static readonly AttackSpec HeavyPunch = new AttackSpec
        {
            type = AttackType.HeavyPunch, displayName = "Golpe fuerte", animatorState = "HeavyPunch",
            duration = 0.95f, hitTime = 0.48f, damage = 12f, range = 1.55f, height = 1.38f,
            knockback = 1.0f, hitstop = 0.095f, activeDuration = 0.12f,
            hitboxForward = 0.85f, hitboxWidth = 0.72f, hitboxHeight = 0.38f, hitboxCenterY = 1.38f,
            high = true, heavy = true, upperBody = true, guardDamage = 0.24f,
            speed = 1.30f, hitStun = 0.80f
        };

        public static readonly AttackSpec LightKick = new AttackSpec
        {
            type = AttackType.LightKick, displayName = "Patada rapida", animatorState = "LightKick",
            duration = 0.72f, hitTime = 0.33f, damage = 8f, range = 1.65f, height = 0.85f,
            knockback = 0.7f, hitstop = 0.065f, activeDuration = 0.12f,
            hitboxForward = 0.90f, hitboxWidth = 0.70f, hitboxHeight = 0.65f, hitboxCenterY = 0.80f,
            high = false, heavy = false, upperBody = false, guardDamage = 0.16f,
            speed = 1.30f, hitStun = 0.58f
        };

        public static readonly AttackSpec HeavyKick = new AttackSpec
        {
            type = AttackType.HeavyKick, displayName = "Patada fuerte", animatorState = "HeavyKick",
            duration = 1.12f, hitTime = 0.56f, damage = 15f, range = 1.80f, height = 0.95f,
            knockback = 1.35f, hitstop = 0.115f, activeDuration = 0.15f,
            hitboxForward = 1.02f, hitboxWidth = 0.80f, hitboxHeight = 0.80f, hitboxCenterY = 0.90f,
            high = false, heavy = true, upperBody = false, guardDamage = 0.30f,
            speed = 1.30f, hitStun = 0.95f
        };

        // Patada baja desde agachado. Usa el clip de patada (Kicking) con hitbox baja y
        // provoca la reaccion real de golpe bajo (Kick To The Groin => GroinHit).
        public static readonly AttackSpec LowKick = new AttackSpec
        {
            type = AttackType.LowKick, displayName = "Patada baja", animatorState = "LightKick",
            duration = 0.95f, hitTime = 0.45f, damage = 7f, range = 1.55f, height = 0.40f,
            knockback = 0.65f, hitstop = 0.06f, activeDuration = 0.14f,
            hitboxForward = 0.88f, hitboxWidth = 0.76f, hitboxHeight = 0.38f, hitboxCenterY = 0.30f,
            high = false, heavy = false, upperBody = false, guardDamage = 0.18f,
            lunge = 0.65f, lungeDuration = 0.32f,
            hitReaction = "GroinHit", hitStun = 1.00f, speed = 1.25f
        };

        // Antiaereo: conecta a un rival que salta y castiga la guardia.
        public static readonly AttackSpec Uppercut = new AttackSpec
        {
            type = AttackType.Uppercut, displayName = "Uppercut", animatorState = "Uppercut",
            duration = 0.85f, hitTime = 0.68f, damage = 13f, range = 1.35f, height = 1.60f,
            knockback = 1.0f, hitstop = 0.10f, activeDuration = 0.15f,
            hitboxForward = 0.68f, hitboxWidth = 0.62f, hitboxHeight = 1.05f, hitboxCenterY = 1.20f,
            high = false, heavy = true, upperBody = false, guardDamage = 0.34f,
            antiAir = true, lunge = 0.45f, lungeDuration = 0.30f, animSpeed = 2.0f,
            speed = 1.20f, hitStun = 0.95f
        };

        // Patada voladora de sprint: mucho alcance y avance. Derriba: usa la reaccion
        // `Stunned` (que acaba en el suelo) y el luchador se levanta con `GetUp`. Antes
        // esta reaccion solo se veia en el guard break y el clip quedaba desaprovechado.
        public static readonly AttackSpec DropKick = new AttackSpec
        {
            type = AttackType.DropKick, displayName = "Patada en carrera", animatorState = "DropKick",
            duration = 0.90f, hitTime = 0.72f, damage = 16f, range = 1.95f, height = 0.85f,
            knockback = 1.6f, hitstop = 0.12f, activeDuration = 0.15f,
            hitboxForward = 1.10f, hitboxWidth = 0.86f, hitboxHeight = 0.85f, hitboxCenterY = 0.82f,
            high = false, heavy = true, upperBody = false, guardDamage = 0.42f,
            lunge = 2.6f, lungeDuration = 0.42f, animSpeed = 2.0f,
            hitReaction = "Stunned", speed = 1.20f, hitStun = 1.05f
        };

        // Cuarto golpe de la cadena G -> H: cuatro impactos rapidos de verdad.
        public static readonly AttackSpec QuadPunch = new AttackSpec
        {
            type = AttackType.QuadPunch, displayName = "Combo de cuatro", animatorState = "QuadPunch",
            duration = 0.95f, hitTime = 0.24f, damage = 4f, range = 1.50f, height = 1.36f,
            knockback = 0.30f, hitstop = 0.035f, activeDuration = 0.08f,
            hitboxForward = 0.82f, hitboxWidth = 0.74f, hitboxHeight = 0.42f, hitboxCenterY = 1.34f,
            high = true, heavy = true, upperBody = false, guardDamage = 0.075f,
            lunge = 0.9f, lungeDuration = 0.26f, hitCount = 4, hitInterval = 0.10f,
            speed = 1.30f, hitStun = 0.55f
        };

        // ---------------------------------------------------------------------------
        // COMBOS POR SECUENCIA (4 pulsaciones)
        // ---------------------------------------------------------------------------
        // Los tres clips vienen de los FBX COMBO*. El contenido de cada uno se midio con
        // tools/blender/inspect_fbx.py (picos de extension de las manos), y de ahi salen
        // los hitTimes. `duration` comprime el clip para que no dure 4.7 s en combate.
        //
        // G + G + G -> COMBO1QUADPUNCH (51 frames): 4 golpes seguidos.
        public static readonly AttackSpec Combo1 = new AttackSpec
        {
            type = AttackType.Combo1, displayName = "Combo cuadruple", animatorState = "Combo1",
            duration = 1.67f, hitTime = 0.47f, damage = 4.5f, range = 1.55f, height = 1.36f,
            knockback = 0.34f, hitstop = 0.045f, activeDuration = 0.09f,
            hitboxForward = 0.84f, hitboxWidth = 0.78f, hitboxHeight = 0.46f, hitboxCenterY = 1.34f,
            high = true, heavy = true, upperBody = false, guardDamage = 0.08f,
            lunge = 0.55f, lungeDuration = 0.30f,
            hitCount = 4, hitTimes = new[] { 0.47f, 0.70f, 0.93f, 1.17f },
            speed = 1.20f, hitStun = 0.48f
        };

        // K + J + H + H -> Uppercut JabCOMBO2 (93 frames): 5 golpes, el ultimo arriba.
        public static readonly AttackSpec Combo2 = new AttackSpec
        {
            type = AttackType.Combo2, displayName = "Combo de uppercut", animatorState = "Combo2",
            duration = 2.20f, hitTime = 1.00f, damage = 5f, range = 1.60f, height = 1.42f,
            knockback = 0.40f, hitstop = 0.045f, activeDuration = 0.10f,
            hitboxForward = 0.86f, hitboxWidth = 0.80f, hitboxHeight = 0.52f, hitboxCenterY = 1.32f,
            high = true, heavy = true, upperBody = false, guardDamage = 0.09f,
            lunge = 0.75f, lungeDuration = 0.45f,
            hitCount = 5, hitTimes = new[] { 1.00f, 1.24f, 1.44f, 1.63f, 1.82f },
            speed = 1.18f, hitStun = 0.48f
        };

        // J + G + K + H -> COMBO3CUIDADONOTIENEINPLACE (142 frames): 7 golpes.
        // OJO: este clip NO esta in-place; el horneado lo cancela como al resto.
        //
        // AVANCE MEDIDO (2026-11-02): la compensacion in-place del clip mide 1.49 m de
        // deriva neta en el armature (medido en los 7 personajes: 1.40-1.53 m). El clip
        // original es un combo que CAMINA hacia delante; al hornearlo in-place la malla
        // se queda en el sitio y el Fighter tiene que recorrer esa distancia. Antes se
        // declaraban 0.85 m/s durante 0.60 s (0.51 m total, media ventana): el cuerpo
        // avanzaba 0.51 m, el clip seguia desplazandose y el impacto visual no cuadraba
        // con el golpe real. La compensacion del horneado es LINEAL en toda la toma, asi
        // que el avance debe ser lineal en TODA la ventana:
        //     lunge = 1.49 / 2.80 = 0.532 m/s   ·   lungeDuration = duration = 2.80 s
        // Total = 1.49 m. Con `speed` 1.15 la ventana real es 2.435 s y el desplazamiento
        // por segundo de juego es 1.49/2.435 = 0.612 m/s, exactamente el del clip.
        public static readonly AttackSpec Combo3 = new AttackSpec
        {
            type = AttackType.Combo3, displayName = "Combo tumbado", animatorState = "Combo3",
            duration = 2.80f, hitTime = 0.34f, damage = 4f, range = 1.55f, height = 1.40f,
            // 0.30 por golpe: el combo avanza con el clip y el rival NO puede salir de la
            // hitbox entre impactos. Con 0.42 el sexto/séptimo golpe quedaban 5-9 cm
            // fuera porque el retroceso acumulado ganaba a los 0.61 m/s del avance.
            knockback = 0.30f, hitstop = 0.045f, activeDuration = 0.09f,
            hitboxForward = 0.84f, hitboxWidth = 0.80f, hitboxHeight = 0.50f, hitboxCenterY = 1.30f,
            high = true, heavy = true, upperBody = false, guardDamage = 0.08f,
            lunge = 0.532f, lungeDuration = 2.80f,
            hitCount = 7, hitTimes = new[] { 0.34f, 0.66f, 1.09f, 1.23f, 1.37f, 1.51f, 1.75f },
            speed = 1.15f, hitStun = 0.48f
        };

        // Secuencias: 1=G (golpe rapido), 2=H (fuerte), 3=J (patada rapida), 4=K (patada fuerte).
        //
        // 2026-11-04: los combos pasan a ser MONOTONOS (un boton repetido) a proposito. Las
        // secuencias mezcladas de cuatro teclas se reconocian pero no se ejecutaban: el
        // jugador tenia que recordar tres ordenes distintas y acertar la ventana de 0.55 s
        // entre cada pulsacion. Repetir el mismo boton es lo que ya hace la mano cuando
        // machaca, asi que el combo sale sin estudiarlo. Las longitudes son distintas (3, 3
        // y 4) y `MatchCombo` compara el FINAL del historial, asi que funcionan juntas sin
        // ambiguedad: G G G no puede confundirse con J J J J.
        static readonly int[][] ComboSequences =
        {
            new[] { 1, 1, 1 },        // G G G      -> combo cuadruple
            new[] { 2, 2, 2 },        // H H H      -> combo de uppercut
            new[] { 3, 3, 3, 3 }      // J J J J    -> combo tumbado
        };

        static readonly AttackSpec[] ComboSpecs = { Combo1, Combo2, Combo3 };

        // Token de secuencia del boton (1..4). Es el alfabeto de los combos.
        public static int Token(AttackButton button)
        {
            switch (button)
            {
                case AttackButton.LightPunch: return 1;
                case AttackButton.HeavyPunch: return 2;
                case AttackButton.LightKick: return 3;
                default: return 4;
            }
        }

        // UN boton -> UNA accion. Sin contexto, sin excepciones. Esta es la unica tabla que
        // consulta el jugador; la especificacion de diseno cabe entera aqui.
        public static AttackSpec FromButton(AttackButton button)
        {
            switch (button)
            {
                case AttackButton.LightPunch: return LightPunch;
                case AttackButton.HeavyPunch: return HeavyPunch;
                case AttackButton.LightKick: return LightKick;
                default: return HeavyKick;
            }
        }

        // Devuelve el combo si el FINAL del historial coincide con una secuencia.
        public static AttackSpec MatchCombo(int[] history, int count)
        {
            if (history == null || count <= 0) return null;
            for (int c = 0; c < ComboSequences.Length; c++)
            {
                var seq = ComboSequences[c];
                if (count < seq.Length) continue;
                bool ok = true;
                for (int i = 0; i < seq.Length; i++)
                {
                    if (history[count - seq.Length + i] != seq[i]) { ok = false; break; }
                }
                if (ok) return ComboSpecs[c];
            }
            return null;
        }

        // Camino de la IA. Conserva los ataques con contexto (uppercut desde agachado,
        // patada en carrera) porque son decisiones suyas, no botones del jugador.
        public static AttackSpec FromIntent(in FighterIntent intent, bool sprinting, bool crouching)
        {
            if (crouching)
            {
                if (intent.heavyPunch) return Uppercut;
                if (intent.lightKick) return LowKick;
                return null;
            }
            if (sprinting && intent.heavyKick) return DropKick;
            if (intent.lightPunch) return LightPunch;
            if (intent.heavyPunch) return HeavyPunch;
            if (intent.lightKick) return LightKick;
            if (intent.heavyKick) return HeavyKick;
            return null;
        }

        public static readonly AttackSpec[] All =
        {
            LightPunch, HeavyPunch, LightKick, HeavyKick,
            LowKick, Uppercut, DropKick, QuadPunch,
            Combo1, Combo2, Combo3
        };
    }
}
