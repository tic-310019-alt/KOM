using UnityEngine;

namespace KOM
{
    /// <summary>
    /// Orquestador del "feel" de los golpes.
    ///
    /// Un golpe es un evento unico, pero se siente en SIETE canales distintos. Si cada
    /// canal se dispara desde un sitio diferente, el resultado es ruido: el temblor no
    /// coincide con el flash, el flash no coincide con el hitstop y el golpe se lee
    /// como un glitch. Aqui se calcula UNA intensidad y de ella se derivan todos los
    /// canales, asi que subir el peso de un ataque sube el conjunto de forma coherente.
    ///
    /// Ademas cada ataque tiene su PROPIA silueta (`HitStyle`): un jab, un recto, una
    /// patada y una voladora no pueden compartir el mismo efecto, o el jugador deja de
    /// distinguir lo que hizo. El estilo tambien decide cuanto de ese golpe llega a la
    /// pantalla: el flash global se reserva a los golpes que pesan.
    ///
    /// Canales, en el orden en que el cerebro los procesa:
    ///
    ///   1. HITSTOP     (Fighter/CombatController) congelacion local de los cuerpos.
    ///   2. FLASH       (HitFlashFx) el cuerpo del que recibe se quema a blanco.
    ///   3. VFX         (HitVfx) capas segun el estilo del ataque.
    ///   4. PANTALLA    (ImpactScreenFx) lineas de foco y onda desde el punto real.
    ///   5. CAMARA      (CombatCameraRig) traslacion + balanceo + FOV kick.
    ///   6. POST        (HitPostFx) aberracion cromatica, lente, contraste.
    ///   7. INTERFAZ    (CombatHud) velo de dano y contador de golpes encadenados.
    /// </summary>
    public sealed class HitFeedback : MonoBehaviour
    {
        // Ventana en la que dos impactos se consideran "el mismo intercambio". Es mas
        // larga que el hitstun ligero para que un combo rapido cuente como cadena.
        const float ChainWindow = 0.9f;
        const float ComboWindow = 1.05f;
        // Por debajo de este intervalo entre golpes se aplica la atenuacion de densidad.
        const float DenseInterval = 0.14f;

        CombatCameraRig _rig;
        Camera _camera;
        CombatHud _hud;
        Fighter _player;
        ImpactScreenFx _screen;
        HitPostFx _post;

        int _chain;
        float _chainAge = 99f;
        int _combo;
        float _comboAge = 99f;
        bool _comboIsPlayer;

        public static HitFeedback Attach(Transform host, CombatCameraRig rig, Camera camera,
            CombatHud hud, Fighter player)
        {
            if (host == null) return null;
            var go = new GameObject("HitFeedback");
            go.transform.SetParent(host, false);
            var fx = go.AddComponent<HitFeedback>();
            fx._rig = rig;
            fx._camera = camera;
            fx._hud = hud;
            fx._player = player;
            fx._screen = ImpactScreenFx.Ensure(camera);
            fx._post = HitPostFx.Attach(go.transform, camera);
            return fx;
        }

        /// <summary>
        /// Peso normalizado del impacto a partir del dano real, no del nombre del
        /// ataque: asi un ajuste de balance se traduce solo en mas o menos sensacion.
        /// </summary>
        static float Weight(float damage, bool heavy, bool blocked, float multiplier)
        {
            float w = Mathf.Clamp01(damage / 16f);
            if (heavy) w = Mathf.Lerp(w, 1f, 0.35f);
            if (blocked) w *= 0.42f;
            return Mathf.Clamp(w * multiplier, 0.10f, 1.7f);
        }

        static Vector3 HitDirection(Fighter attacker, Fighter defender)
        {
            Vector3 dir = Vector3.zero;
            if (attacker != null && defender != null)
            {
                dir = defender.transform.position - attacker.transform.position;
                dir.y = 0f;
            }
            if (dir.sqrMagnitude < 0.0001f)
                dir = Vector3.right * (attacker != null && attacker.Facing < 0f ? -1f : 1f);
            return dir.normalized;
        }

        /// <summary>Estilo visual de cada ataque. Es la tabla que da identidad al combate.</summary>
        public static HitStyle StyleFor(AttackType type)
        {
            switch (type)
            {
                case AttackType.LightPunch: return HitStyle.Jab;
                case AttackType.HeavyPunch: return HitStyle.Straight;
                case AttackType.LightKick: return HitStyle.KickLight;
                case AttackType.HeavyKick: return HitStyle.KickHeavy;
                case AttackType.LowKick: return HitStyle.LowSweep;
                case AttackType.Uppercut: return HitStyle.Rising;
                case AttackType.DropKick: return HitStyle.FlyingKick;
                default: return HitStyle.Rapid;
            }
        }

        // ---------------------------------------------------------------------
        // Golpe normal
        // ---------------------------------------------------------------------
        public void Landed(Fighter attacker, Fighter defender, AttackSpec spec, bool blocked, Vector3 point)
        {
            if (spec == null || defender == null) return;
            // Un puno no es magia: el impacto normal va casi blanco, con solo un
            // recuerdo del color del personaje. El color pleno se reserva al especial.
            var color = Color.Lerp(KomData.Accent(attacker != null ? attacker.Def : null), Color.white, 0.35f);
            float w = Weight(spec.damage, spec.heavy, blocked, 1f);
            // Los multi-golpe (QuadPunch, combos) reparten su dano: se les da un poco
            // mas de peso por impacto para que el conjunto no se lea debil.
            if (spec.hitCount > 1) w *= 1.18f;

            var style = blocked ? HitStyle.Guard : StyleFor(spec.type);
            HitVfx.ScreenWeights(style, out float flash, out float lines, out float ring, out float cam);
            Apply(attacker, defender, point, HitDirection(attacker, defender), w, blocked, color,
                style, flash, lines, ring, cam, true);
            CountCombo(attacker, defender, blocked, color);
        }

        // ---------------------------------------------------------------------
        // Golpe de poder
        // ---------------------------------------------------------------------
        public void SpecialLanded(Fighter attacker, Fighter defender, SpecialDef special, bool blocked, Vector3 point)
        {
            if (special == null || defender == null) return;
            var color = KomData.SpecialColor(attacker != null ? attacker.Def : null, special);
            float w = Weight(special.damage, true, blocked, 1.55f);
            // El especial ya trae su propio `SpecialImpactFx`: aqui NO se anade otro
            // impacto de mundo (se sumaban dos explosiones y el golpe se leia sucio).
            Apply(attacker, defender, point, HitDirection(attacker, defender), w, blocked, color,
                HitStyle.Straight, 0.42f, 0.62f, 0.85f, 1.35f, false);
            CountCombo(attacker, defender, blocked, color);
        }

        void Apply(Fighter attacker, Fighter defender, Vector3 point, Vector3 dir,
            float w, bool blocked, Color color, HitStyle style,
            float flashScale, float lineScale, float ringScale, float camScale, bool worldVfx)
        {
            // El punto de contacto REAL es la superficie de la hurtbox, no el punto
            // medio entre los dos cuerpos: con el punto medio el VFX nace flotando a
            // medio metro del pecho.
            Vector3 contact = point;
            if (defender != null)
            {
                contact = defender.transform.position - dir * (defender.CurrentHurtboxRadius * 0.85f);
                contact.y = point.y;
            }

            // Densidad: dos golpes en el mismo frame o a 8 Hz no deben sumar sus
            // efectos, deben repartirlos.
            bool dense = _chainAge < DenseInterval;
            float density = dense ? 0.68f : 1f;
            float cam = w * density * camScale;

            // --- camara ---------------------------------------------------------
            if (_rig != null)
            {
                float strength = 0.035f + cam * 0.19f;
                float duration = 0.11f + cam * 0.12f;
                float roll = 0.30f + cam * 2.2f;
                float fov = 0.55f + cam * 1.9f;
                _rig.ShakeEx(strength, duration, dir.x, roll, fov);
                if (!blocked) _rig.Punch(0.03f + cam * 0.13f);
            }

            // --- pantalla -------------------------------------------------------
            // Escalada por estilo: el jab apenas mancha la pantalla, la voladora si.
            if (_screen != null)
                _screen.Play(contact, color, 0.42f + w * 0.95f, blocked, flashScale, lineScale, ringScale);

            // --- post-proceso ---------------------------------------------------
            if (_post != null) _post.Pulse((blocked ? 0.18f : 0.26f) + w * 0.62f * camScale);

            // --- VFX de mundo ---------------------------------------------------
            if (worldVfx) HitVfx.Play(contact, dir, color, 0.40f + w * 1.0f, style);

            // --- destello del cuerpo --------------------------------------------
            float flash = blocked ? 0.34f + w * 0.26f : 0.42f + w * 0.55f;
            defender.FlashImpact(flash, blocked ? 1f : 0.16f, blocked ? 18f : 12f);

            // --- interfaz -------------------------------------------------------
            if (_hud != null)
            {
                if (_player != null && defender == _player)
                {
                    _hud.FlashScreen(blocked ? new Color(0.40f, 0.80f, 1f) : new Color(1f, 0.22f, 0.18f),
                        blocked ? 0.10f : 0.18f, blocked ? 0.16f : 0.20f);
                }
                else
                {
                    _hud.FlashScreen(new Color(1f, 0.92f, 0.78f), blocked ? 0.04f : 0.07f, 0.10f);
                }
            }

            // --- cadena ---------------------------------------------------------
            _chain = _chainAge < ChainWindow ? _chain + 1 : 1;
            _chainAge = 0f;
        }

        void CountCombo(Fighter attacker, Fighter defender, bool blocked, Color color)
        {
            if (blocked || _hud == null || attacker == null) return;
            _combo = _comboAge < ComboWindow ? _combo + 1 : 1;
            _comboAge = 0f;
            _comboIsPlayer = _player != null && attacker == _player;
            if (_combo >= 2) _hud.ShowCombo(_combo, _comboIsPlayer, color);
        }

        // ---------------------------------------------------------------------
        // Guardia rota: el golpe que mas se tiene que notar
        // ---------------------------------------------------------------------
        public void GuardBroken(Fighter defender, Vector3 point)
        {
            if (defender == null) return;
            var ice = new Color(0.55f, 0.86f, 1f);

            if (_rig != null)
            {
                _rig.ShakeEx(0.30f, 0.32f, defender.Facing, 3.2f, 3.4f);
                _rig.Punch(0.22f);
            }
            if (_screen != null) _screen.Play(point, ice, 1.35f, false, 0.30f, 0.70f, 1.0f);
            if (_post != null) _post.Pulse(0.9f);
            // El escudo estalla: un impacto ancho y frio, no el de un punetazo.
            HitVfx.Play(point, Vector3.up, ice, 1.30f, HitStyle.Guard);
            defender.FlashImpact(1.30f, 0.05f, 8f);
            if (_hud != null) _hud.FlashScreen(ice, 0.16f, 0.22f);
        }

        // ---------------------------------------------------------------------
        // KO
        // ---------------------------------------------------------------------
        public void KnockedOut(Fighter loser, Vector3 point)
        {
            if (loser == null) return;
            if (_rig != null)
            {
                _rig.ShakeEx(0.38f, 0.52f, loser.Facing, 4.2f, 4.6f);
                _rig.Punch(0.30f);
            }
            if (_screen != null) _screen.Play(point, Color.white, 1.6f, false, 0.55f, 1.0f, 1.0f);
            if (_post != null) _post.Pulse(1f);
            HitVfx.Play(point, Vector3.up, Color.white, 1.6f, HitStyle.FlyingKick);
            loser.FlashImpact(1.6f, 0f, 5.5f);
            if (_hud != null) _hud.FlashScreen(Color.white, 0.28f, 0.30f);
        }

        /// <summary>Corta todos los pulsos (fin de ronda, pausa, salida).</summary>
        public void Clear()
        {
            _chain = 0;
            _combo = 0;
            _chainAge = 99f;
            _comboAge = 99f;
            if (_screen != null) _screen.Clear();
            if (_post != null) _post.Clear();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _chainAge += dt;
            _comboAge += dt;
        }
    }
}
