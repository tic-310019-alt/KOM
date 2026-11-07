using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KOM
{
    // =====================================================================================
    // ONDA DE GOLPE EN EL SUELO (El Paso Tumbado — Natanael Chano)
    // =====================================================================================
    // El personaje corre, salta y cae a plomo. Al tocar el suelo se abre una onda radial
    // que viaja por el piso y golpea al rival cuando el frente la alcanza, sin importar la
    // direccion: no es un proyectil, es un area que crece.
    //
    // Capas: tres aros planos que se abren a distinta velocidad, un disco suave que da
    // cuerpo al frente, columna de polvo en el punto de impacto, escombros y luz. El daño
    // se resuelve con ResolveSlamHit cuando el radio cruza la distancia al rival.
    public sealed class GroundSlamFx : MonoBehaviour
    {
        Fighter _owner;
        SpecialDef _special;
        Color _color;
        Vector3 _center;
        float _radius;
        float _maxRadius;
        float _speed;
        float _age;
        bool _hit;
        bool _resolved;
        Transform _impact;
        readonly List<Transform> _rings = new List<Transform>();
        readonly List<Material> _ringMats = new List<Material>();
        readonly List<float> _ringDelay = new List<float>();
        Transform _disc;
        Material _discMat;
        ParticleSystem _dust;
        ParticleSystem _debris;
        Light _light;
        readonly List<Material> _materials = new List<Material>();
        Material _flashMat;
        float _lastRadius;

        // Grosor del frente de la onda, en metros. Es tambien la ventana de daño: si el
        // rival esta dentro de +-band del radio actual, le toca.
        const float Band = 0.38f;

        public void Init(Fighter owner, SpecialDef special, Vector3 center)
        {
            _owner = owner;
            _special = special;
            _color = KomData.SpecialColor(owner != null ? owner.Def : null, special);
            _center = center;
            _maxRadius = Mathf.Max(1.5f, special != null ? special.range : 4.5f);
            // La onda tarda ~0.42 s en llegar al borde: legible y esquivable de un salto.
            _speed = _maxRadius / 0.34f;
            transform.position = new Vector3(center.x, 0.045f, center.z);

            // Aros planos: se abren desde el punto de impacto.
            for (int i = 0; i < 3; i++)
            {
                var ring = KomSpecialFx.CreateRing3D(transform, "SlamRing" + i,
                    Color.Lerp(_color, Color.white, 0.12f + i * 0.22f), 1f, 0.62f, 1.35f - i * 0.25f);
                ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                _rings.Add(ring.transform);
                _ringMats.Add(ring.GetComponent<MeshRenderer>().sharedMaterial);
                _ringDelay.Add(i * 0.035f);
            }

            // Disco suave: el cuerpo del frente (los aros solos se leen como alambre).
            var disc = KomSpecialFx.CreateGlowQuad(transform, "SlamDisc",
                Color.Lerp(_color, Color.white, 0.35f), 1f, 1f, KomFx.SoftRingTexture, 0.0f);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            _disc = disc.transform;
            _discMat = disc.GetComponent<MeshRenderer>().sharedMaterial;
            _materials.Add(_discMat);

            // Columna de polvo vertical en el punto de impacto.
            var dustMat = KomSpecialFx.CreateParticleMaterial();
            _materials.Add(dustMat);
            _dust = KomSpecialFx.CreateParticles(transform, "SlamDust",
                new Color(0.72f, 0.68f, 0.60f), false, 34f, 0.45f, 0.95f, 0.10f, 0.26f, dustMat);
            // Circulo con grosor 0: las particulas nacen SOLO en el borde. Moviendo el
            // radio cada frame, el polvo forma una pared que viaja con el frente en vez de
            // quedar como una nube en el centro.
            var dustShape = _dust.shape;
            dustShape.shapeType = ParticleSystemShapeType.Circle;
            dustShape.radiusThickness = 0f;
            dustShape.radius = 0.25f;
            dustShape.arc = 360f;
            var dustMain = _dust.main;
            dustMain.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
            dustMain.gravityModifier = -0.12f;
            var dustEmission = _dust.emission;
            dustEmission.rateOverTime = 150f;
            _dust.Emit(30);

            // Escombros: chispas alargadas que salen rasantes.
            var debrisMat = KomSpecialFx.CreateParticleMaterial();
            _materials.Add(debrisMat);
            _debris = KomSpecialFx.CreateStreaks(transform, "SlamDebris", _color, 42f,
                0.18f, 0.34f, 0.02f, 0.05f, debrisMat);
            var debrisShape = _debris.shape;
            debrisShape.shapeType = ParticleSystemShapeType.Hemisphere;
            debrisShape.radius = 0.30f;
            var debrisMain = _debris.main;
            debrisMain.startSpeed = new ParticleSystem.MinMaxCurve(3.0f, 6.5f);
            debrisMain.gravityModifier = 0.6f;
            _debris.Emit(38);

            // Fogonazo del impacto: breve, en el punto exacto donde pisa.
            var flash = KomSpecialFx.CreateGlowQuad(transform, "SlamFlash",
                Color.Lerp(_color, Color.white, 0.55f), 2.6f, 2.6f, KomFx.GlowTexture, 1.5f);
            flash.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            flash.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            _impact = flash.transform;
            _flashMat = flash.GetComponent<MeshRenderer>().sharedMaterial;
            _materials.Add(_flashMat);

            _light = KomSpecialFx.CreateLight(transform, _color, 4.2f, 1.6f);
        }

        public float Radius => _radius;

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            _lastRadius = _radius;
            _radius = Mathf.Min(_maxRadius, _radius + _speed * dt);
            float t = Mathf.Clamp01(_radius / Mathf.Max(0.01f, _maxRadius));

            for (int i = 0; i < _rings.Count; i++)
            {
                var ring = _rings[i];
                if (ring == null) continue;
                float local = Mathf.Clamp01((_radius - _maxRadius * _ringDelay[i]) / Mathf.Max(0.01f, _maxRadius));
                float r = Mathf.Max(0.05f, _radius - _maxRadius * _ringDelay[i]);
                ring.localScale = new Vector3(r, r, 0.34f - i * 0.06f);
                var mat = _ringMats[i];
                if (mat != null)
                    mat.SetFloat("_Intensity", (1.45f - i * 0.26f) * (1f - local * 0.85f));
            }

            if (_dust != null)
            {
                var sh = _dust.shape;
                sh.radius = Mathf.Max(0.25f, _radius);
            }

            if (_disc != null)
            {
                float d = Mathf.Max(0.1f, _radius * 2f);
                _disc.localScale = new Vector3(d, d, 1f);
                if (_discMat != null) _discMat.SetFloat("_Intensity", 0.75f * (1f - t * 0.9f));
            }

            if (_impact != null)
            {
                float k = Mathf.Clamp01(_age / 0.16f);
                _impact.localScale = Vector3.one * (1.6f * (1f + k * 0.9f));
                if (_flashMat != null) _flashMat.SetFloat("_Intensity", 1.1f * (1f - k));
            }

            if (_light != null) _light.intensity = 1.6f * (1f - t * 0.85f);

            // Daño: el frente cruza la distancia al rival. Se resuelve una sola vez y se
            // usa el radio REAL del frame, no un temporizador paralelo.
            if (!_hit && !_resolved && _owner != null && _special != null)
            {
                if (_owner.ResolveSlamHit(_special, _center, _radius, Band))
                {
                    _hit = true;
                    _resolved = true;
                }
                else
                {
                    // Si el frente ya paso su alcance maximo sin tocarlo, no se reintenta.
                    var opp = _owner.Opponent;
                    if (opp != null)
                    {
                        Vector3 d = opp.transform.position - _center;
                        d.y = 0f;
                        if (d.magnitude + Band + opp.CurrentHurtboxRadius < _radius) _resolved = true;
                    }
                }
            }

            if (_radius >= _maxRadius - 0.001f && _age > 0.55f) Destroy(gameObject);
        }

        void OnDestroy()
        {
            foreach (var mat in _materials)
                if (mat != null) Destroy(mat);
        }
    }
}
