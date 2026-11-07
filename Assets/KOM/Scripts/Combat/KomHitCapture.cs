#if UNITY_EDITOR
using UnityEngine;

namespace KOM
{
    /// <summary>
    /// Banco de pruebas del FEEL de un golpe normal (solo editor).
    ///
    /// Captura el frame EXACTO del contacto y despues congela el editor para que el
    /// puente pueda sacar una segunda imagen sin prisa. Hace falta capturar dentro de
    /// la corrutina: `Debug.Break()` pausa al final del frame y la lectura del Game
    /// View del puente llega ~0.3 s tarde, cinco veces la vida del flash.
    ///
    /// Uso desde el puente:
    ///   eval 'KOM.EditorTools.KomHitShot.Setup("juan_grabriel","peso_plomo")'
    ///   eval 'KOM.EditorTools.KomHitShot.Punch("HeavyPunch","punch")'
    ///   capture Logs/play/punch_frozen.png --source game
    ///   editor pause false
    /// </summary>
    public sealed class KomHitCapture : MonoBehaviour
    {
        Fighter _attacker;
        Fighter _defender;
        AttackSpec _spec;
        string _prefix = "hit";
        // Retardo desde el instante teorico de impacto. 0.016 s = un frame a 60 Hz:
        // el flash ya esta en su pico y el hitstop sigue vigente.
        float _delay = 0.016f;

        public void Configure(Fighter attacker, Fighter defender, AttackSpec spec, string prefix, float delay)
        {
            _attacker = attacker;
            _defender = defender;
            _spec = spec;
            _prefix = string.IsNullOrEmpty(prefix) ? "hit" : prefix;
            _delay = delay;
            StartCoroutine(Run());
        }

        System.Collections.IEnumerator Run()
        {
            if (_attacker == null || _spec == null) { Destroy(gameObject); yield break; }

            // Esperar a que la RONDA este en curso. Si se ataca durante la
            // presentacion, BeginRoundRoutine resetea posiciones justo en el
            // impacto y la captura sale sin contacto (medido: sep 3.17 m).
            var combat = _attacker.Combat;
            float guard = Time.time + 10f;
            while (combat != null && !combat.RoundActive && Time.time < guard) yield return null;

            // Ahora si: el rival queda congelado y el atacante habilitado (la ronda
            // acaba de reescribir los dos flags).
            _attacker.ControlsEnabled = true;
            if (_defender != null) _defender.ControlsEnabled = false;

            // Reintento: StartAttack se rechaza en silencio si el luchador esta en
            // hitstun, en recuperacion o bloqueando. El rival se congela en CADA
            // intento (la ronda reescribe el flag al arrancar) para que no siga
            // golpeando mientras se espera un hueco.
            bool started = false;
            for (int attempt = 0; attempt < 240 && !started; attempt++)
            {
                if (_defender != null) _defender.ControlsEnabled = false;
                _attacker.ControlsEnabled = true;
                _attacker.StartAttack(_spec);
                started = _attacker.IsAttacking;
                if (!started) yield return null;
            }
            if (!started)
            {
                Debug.LogWarning("[KOM][HITSHOT] el atacante no pudo arrancar " + _spec.type +
                    $" (ko={_attacker.IsKO} grounded={_attacker.IsGrounded})");
                Destroy(gameObject);
                yield break;
            }

            float start = Time.time;
            float hitTime = _spec.HitTimeAt(0);
            float impact = start + hitTime + _delay;

            // El rival se mantiene a distancia de golpe HASTA justo antes del
            // contacto: el reset de ronda, el empuje o la propia IA lo movian y la
            // captura salia sin impacto. Los ultimos 40 ms se dejan libres para que
            // el golpe sea honesto.
            while (Time.time < impact - 0.04f)
            {
                KeepInRange();
                yield return null;
            }
            while (Time.time < impact) yield return null;

            // El frame que interesa es ESTE. Se congela el editor con Debug.Break():
            // ScreenCapture tarda ~0.3 s en devolver la imagen y captura el frame
            // SIGUIENTE, cinco veces la vida del flash (medido). Congelando, el
            // puente saca la imagen exacta sin prisa.
            Publish();
            Debug.Break();
            Destroy(gameObject);
        }

        void KeepInRange()
        {
            if (_attacker == null || _defender == null) return;
            float facing = _attacker.Facing >= 0f ? 1f : -1f;
            var p = _defender.transform.position;
            float want = _attacker.transform.position.x + facing * 1.0f;
            if (Mathf.Abs(p.x - want) > 0.02f)
            {
                p.x = want;
                _defender.transform.position = p;
            }
        }

        // Evidencia numerica: cada canal tiene que estar encendido en el frame del
        // impacto. Un cero aqui es un canal que no se disparo.
        void Publish()
        {
            int vfx = Object.FindObjectsByType<HitVfx>().Length;
            int shells = 0;
            float bodyFlash = 0f;
            var flashes = Object.FindObjectsByType<HitFlashFx>();
            for (int i = 0; i < flashes.Length; i++)
            {
                if (flashes[i] == null || !flashes[i].DebugVisible) continue;
                shells += flashes[i].DebugShellCount;
                bodyFlash = Mathf.Max(bodyFlash, flashes[i].DebugIntensity);
            }

            var cam = Camera.main;
            float fov = cam != null ? cam.fieldOfView : 0f;
            float flash = 0f, lines = 0f, ring = 0f, weight = 0f;
            if (cam != null)
            {
                var screen = cam.GetComponentInChildren<ImpactScreenFx>(true);
                if (screen != null)
                {
                    flash = screen.DebugFlash;
                    lines = screen.DebugLines;
                    ring = screen.DebugRing;
                }
            }
            var post = Object.FindAnyObjectByType<HitPostFx>();
            if (post != null) weight = post.DebugWeight;

            string hit = "no-defender";
            if (_defender != null)
            {
                float sep = Mathf.Abs(_defender.transform.position.x - _attacker.transform.position.x);
                hit = $"sep={sep:F2} hurt={_defender.IsHit} ko={_defender.IsKO} " +
                      $"atkPos={_attacker.transform.position.x:F2} defPos={_defender.transform.position.x:F2} " +
                      $"attacking={_attacker.IsAttacking}";
            }

            Debug.Log($"[KOM][HITSHOT] spec={_spec.type} {hit} vfx={vfx} shells={shells} " +
                      $"bodyFlash={bodyFlash:F2} screenFlash={flash:F2} lines={lines:F2} " +
                      $"ring={ring:F2} postWeight={weight:F2} fov={fov:F2} prefix={_prefix}");
        }
    }
}
#endif
