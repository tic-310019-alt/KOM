using UnityEngine;

namespace KOM
{
    /// <summary>
    /// Sonda de verificacion del salto: registra por frame la altura real del luchador y
    /// la fase normalizada del estado `Jump`, para comprobar que el cuerpo despega
    /// cuando el clip extiende las piernas y que aterriza cuando el clip aterriza.
    ///
    /// Uso en Play:
    ///   var p = FindFirstObjectByType&lt;KomJumpProbe&gt;() ?? new GameObject("JumpProbe").AddComponent&lt;KomJumpProbe&gt;();
    ///   p.Bind(fighter); fighter.SendMessage("Jump");
    ///   ... 1.5 s despues ... Debug.Log(p.Report);
    /// </summary>
    public sealed class KomJumpProbe : MonoBehaviour
    {
        Fighter _fighter;
        float _time;
        bool _done;

        public string Report { get; private set; } = string.Empty;
        public bool Done => _done;

        public void Bind(Fighter fighter)
        {
            _fighter = fighter;
            _time = 0f;
            _done = false;
            Report = string.Empty;
        }

        void Update()
        {
            if (_fighter == null || _done) return;
            _time += Time.deltaTime;
            var st = _fighter.Animator.GetCurrentAnimatorStateInfo(CombatTuning.BaseLayer);
            Report += string.Format("{0:F3}|{1:F3}|{2}|{3}\n", _time, _fighter.AirHeight,
                st.IsName("Jump") ? st.normalizedTime.ToString("F3") : "-", _fighter.DebugJumpState);
            if (_time > 1.15f) _done = true;
        }
    }
}
