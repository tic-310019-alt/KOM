using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KOM
{
    // =====================================================================================
    // BOOMERANG (Acordeon de Poder — Roman Ayala / Flavia)
    // =====================================================================================
    // El acordeon real (prefab de Resources/Props) sale de entre las manos, vuela girando
    // hacia el rival, golpea y VUELVE a las manos del dueno. La trayectoria es un ida y
    // vuelta con arco (no una recta): el acordeon se lee como boomerang, no como bala.
    // Toda la pose se mide en mundo; el prop conserva su malla y su textura PBR.
    public sealed class BoomerangFx : MonoBehaviour
    {
        Fighter _owner;
        SpecialDef _special;
        GameObject _prop;
        Renderer[] _propRenderers;
        Color _color;
        Color _accent;
        Vector3 _origin;
        Vector3 _turnPoint;
        Vector3 _catchPoint;
        Vector3 _side;
        float _outTime;
        float _returnTime;
        float _t;
        float _spinAngle;
        float _spinSpeed;
        float _traveled;
        bool _hit;
        bool _caught;
        int _phase;
        float _phaseStart;
        Vector3 _turnFrom;
        KomRibbon _trailWide;
        KomRibbon _trailCore;
        ParticleSystem _notes;
        ParticleSystem _sparks;
        readonly List<KomBoltFx> _bolts = new List<KomBoltFx>();
        Light _light;
        Material _trailMatA;
        Material _trailMatB;
        Material _noteMat;
        Material _sparkMat;
        readonly List<Material> _materials = new List<Material>();

        public Vector3 CurrentPosition => _prop != null ? _prop.transform.position : transform.position;
        public float Traveled => _traveled;
        public bool Hit => _hit;

        public void Init(Fighter owner, SpecialDef special, GameObject prop, Vector3 origin, Vector3 target)
        {
            _owner = owner;
            _special = special;
            _prop = prop;
            _color = KomData.SpecialColor(owner != null ? owner.Def : null, special);
            _accent = KomData.Accent(owner != null ? owner.Def : null);
            _origin = origin;
            Vector3 toTarget = target - origin;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;
            Vector3 dir = dist > 0.001f ? toTarget / dist : Vector3.forward;
            float range = special != null ? Mathf.Max(1.5f, special.range) : 4.5f;
            _turnPoint = origin + dir * Mathf.Min(range, Mathf.Max(dist, range * 0.55f));
            _turnPoint.y = Mathf.Max(origin.y, target.y);
            _catchPoint = origin;
            _side = Vector3.Cross(Vector3.up, dir);
            if (_side.sqrMagnitude < 0.0001f) _side = Vector3.right;
            _side.Normalize();
            float speed = special != null ? Mathf.Max(3f, special.projectileSpeed) : 9f;
            _outTime = Mathf.Clamp(dist / speed, 0.22f, 0.85f);
            _returnTime = Mathf.Clamp(_outTime * 1.25f, 0.3f, 1.1f);
            _spinSpeed = special != null ? Mathf.Max(360f, special.spinDegrees) : 1080f;

            if (_prop != null)
            {
                _prop.transform.SetParent(transform, true);
                _prop.transform.position = origin;
                _prop.transform.localScale = KomPropGrip.BaseScale(_prop) *
                    (special != null ? Mathf.Max(0.2f, special.propScale) : 1f);
                _propRenderers = _prop.GetComponentsInChildren<Renderer>(true);
            }

            // Estelas: cinta ancha (volumen) + filamento brillante, como el resto de especiales.
            _trailWide = KomSpecialFx.CreateRibbon(transform, "BoomerangTrailWide", _color, 0.62f, 0.26f, 0.55f);
            _trailCore = KomSpecialFx.CreateRibbon(transform, "BoomerangTrailCore",
                Color.Lerp(_color, Color.white, 0.7f), 0.22f, 0.2f, 1.6f);

            _noteMat = KomSpecialFx.CreateParticleMaterial(KomSpecialFx.NoteTexture);
            _materials.Add(_noteMat);
            _notes = KomSpecialFx.CreateParticles(transform, "BoomerangNotes", _color, false, 18f,
                0.5f, 1.0f, 0.10f, 0.20f, _noteMat);
            var noteShape = _notes.shape;
            noteShape.shapeType = ParticleSystemShapeType.Sphere;
            noteShape.radius = 0.28f;
            var noteMain = _notes.main;
            noteMain.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.7f);
            noteMain.startRotation = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            noteMain.gravityModifier = -0.04f;

            _sparkMat = KomSpecialFx.CreateParticleMaterial();
            _materials.Add(_sparkMat);
            _sparks = KomSpecialFx.CreateStreaks(transform, "BoomerangSparks", _color, 34f,
                0.14f, 0.3f, 0.02f, 0.05f, _sparkMat);
            var sparkShape = _sparks.shape;
            sparkShape.shapeType = ParticleSystemShapeType.Sphere;
            sparkShape.radius = 0.26f;

            for (int i = 0; i < 3; i++)
            {
                var bolt = KomSpecialFx.CreateBolt(transform, "BoomerangBolt" + i,
                    Vector3.zero, new Vector3(0.2f * (i + 1), 0.1f * i, 0.12f), _accent, 0.010f, 0.10f, 4700 + i, 0.05f);
                _bolts.Add(bolt);
            }

            _light = KomSpecialFx.CreateLight(transform, _color, 3.0f, 1.3f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _t += dt;

            Vector3 pos = _turnPoint;
            if (_phase == 0)
            {
                float k = Mathf.Clamp01(_t / _outTime);
                float ease = 1f - Mathf.Pow(1f - k, 1.7f);
                pos = Vector3.Lerp(_origin, _turnPoint, ease);
                // Arco de boomerang: sube al salir y baja al llegar.
                pos += Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.34f);
                pos += _side * (Mathf.Sin(k * Mathf.PI) * 0.18f);
                if (k >= 1f) BeginTurn(pos);
            }
            else if (_phase == 1)
            {
                float k = Mathf.Clamp01((_t - _phaseStart) / TurnTime);
                pos = _turnFrom + _side * (k * 0.55f) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.24f);
                if (k >= 1f)
                {
                    _phase = 2;
                    _phaseStart = _t;
                    _turnPoint = pos;
                }
            }
            else if (_phase == 2)
            {
                float k = Mathf.Clamp01((_t - _phaseStart) / _returnTime);
                float ease = k * k * (3f - 2f * k);
                Vector3 target = _owner != null ? _owner.HandMidPosition : _catchPoint;
                pos = Vector3.Lerp(_turnPoint, target, ease);
                pos += Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.55f);
                pos += _side * (Mathf.Sin(k * Mathf.PI) * 0.85f);
                if (k >= 1f && !_caught) Catch();
            }

            if (_prop != null)
            {
                Vector3 prev = _prop.transform.position;
                _prop.transform.position = pos;
                _traveled += Vector3.Distance(prev, pos);
                _spinAngle += _spinSpeed * dt;
                // Giro tipo boomerang: sobre el eje vertical, con una inclinacion fija.
                var tilt = Quaternion.Euler(12f, 0f, 18f);
                _prop.transform.rotation = Quaternion.AngleAxis(_spinAngle, Vector3.up) * tilt;
            }

            for (int i = 0; i < _bolts.Count; i++)
            {
                var bolt = _bolts[i];
                if (bolt == null) continue;
                bolt.transform.position = pos;
                float ang = _t * 7f + i * 2.1f;
                bolt.SetStart(Vector3.zero);
                bolt.SetEnd(new Vector3(Mathf.Cos(ang) * 0.34f, Mathf.Sin(ang * 1.3f) * 0.22f, Mathf.Sin(ang) * 0.3f));
            }
            if (_light != null)
            {
                _light.transform.position = pos;
                _light.intensity = 1.2f + Mathf.Sin(Time.time * 22f) * 0.2f;
            }
            if (_notes != null) _notes.transform.position = pos;
            if (_sparks != null) _sparks.transform.position = pos;

            // Colision solo a la ida: la vuelta es el regreso a la mano.
            if (_phase == 0 && _prop != null && _owner != null)
            {
                if (_owner.ResolveSpecialHit(pos, _special, true))
                {
                    KomSpecialFx.Impact(pos, _color, 1.15f, true);
                    KomSpecialFx.NoteBurst(pos, _color, 14);
                    KomSpecialFx.DustBurst(pos, new Color(0.55f, 0.5f, 0.44f), 10, 1f);
                    if (_notes != null) _notes.Emit(18);
                    BeginTurn(pos);
                }
            }
        }

        const float TurnTime = 0.18f;

        void BeginTurn(Vector3 from)
        {
            _phase = 1;
            _phaseStart = _t;
            _turnFrom = from;
            _hit = true;
        }

        void Catch()
        {
            _caught = true;
            Vector3 at = _owner != null ? _owner.HandMidPosition : transform.position;
            KomSpecialFx.MuzzleFlash(at, _color, 0.8f);
            KomSpecialFx.AuraRing(at, _color, 1.0f);
            if (_owner != null) _owner.OnPropReturned(at);
            Destroy(gameObject, 0.02f);
        }

        void OnDestroy()
        {
            if (_trailWide != null) _trailWide.Stop();
            if (_trailCore != null) _trailCore.Stop();
            foreach (var mat in _materials)
                if (mat != null) Destroy(mat);
            if (_prop != null) Destroy(_prop);
        }
    }

    // =====================================================================================
    // CLUB (Guitarrazo de Rock — Alex Llora / Flavia)
    // =====================================================================================
    // La guitarra va anclada a la mano (KomPropGrip): la estela y los arcos siguen el
    // CUERPO real del prop, asi el rastro sale del mastil y no de un punto fijo.
    public sealed class ClubFx : MonoBehaviour
    {
        Fighter _owner;
        SpecialDef _special;
        GameObject _prop;
        Color _color;
        Color _accent;
        KomRibbon _trailWide;
        KomRibbon _trailCore;
        ParticleSystem _sparks;
        readonly List<KomBoltFx> _bolts = new List<KomBoltFx>();
        Light _light;
        Material _sparkMat;
        Material _noteMat;
        readonly List<Material> _materials = new List<Material>();
        float _t;
        bool _charged = true;

        public void Init(Fighter owner, SpecialDef special, GameObject prop)
        {
            _owner = owner;
            _special = special;
            _prop = prop;
            _color = KomData.SpecialColor(owner != null ? owner.Def : null, special);
            _accent = KomData.Accent(owner != null ? owner.Def : null);

            // Cintas en la raiz: el mesh se construye en mundo y sigue el cuerpo del prop.
            _trailWide = KomSpecialFx.CreateRibbon(transform, "ClubTrailWide", _color, 1.05f, 0.30f, 0.85f);
            _trailCore = KomSpecialFx.CreateRibbon(transform, "ClubTrailCore",
                Color.Lerp(_color, Color.white, 0.75f), 0.34f, 0.24f, 2.2f);

            _sparkMat = KomSpecialFx.CreateParticleMaterial();
            _materials.Add(_sparkMat);
            _sparks = KomSpecialFx.CreateStreaks(transform, "ClubSparks", _color, 26f,
                0.16f, 0.34f, 0.02f, 0.055f, _sparkMat);
            var shape = _sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.18f;

            _noteMat = KomSpecialFx.CreateParticleMaterial(KomSpecialFx.NoteTexture);
            _materials.Add(_noteMat);

            for (int i = 0; i < 2; i++)
            {
                var bolt = KomSpecialFx.CreateBolt(transform, "ClubBolt" + i,
                    Vector3.zero, Vector3.up * 0.5f, _accent, 0.012f, 0.12f, 6100 + i, 0.05f);
                _bolts.Add(bolt);
            }
            _light = KomSpecialFx.CreateLight(transform, _color, 3.4f, 1.5f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _t += dt;
            if (_prop == null) return;
            Vector3 body = KomPropGrip.BodyCenter(_prop);
            Vector3 pivot = _prop.transform.position;
            if (_trailWide != null) _trailWide.Follow = _prop.transform;
            if (_sparks != null) _sparks.transform.position = Vector3.Lerp(pivot, body, 0.6f);
            if (_light != null)
            {
                _light.transform.position = Vector3.Lerp(pivot, body, 0.5f);
                _light.intensity = _charged ? 1.4f + Mathf.Sin(Time.time * 24f) * 0.25f : 0.9f;
            }
            for (int i = 0; i < _bolts.Count; i++)
            {
                var bolt = _bolts[i];
                if (bolt == null) continue;
                bolt.transform.position = pivot;
                bolt.SetStart(_prop.transform.InverseTransformPoint(pivot));
                bolt.SetEnd(_prop.transform.InverseTransformPoint(Vector3.Lerp(pivot, body, 0.55f + i * 0.2f)));
            }
        }

        // Impacto: capas (fogonazo + anillos + polvo + chispas + notas + luz) en el punto
        // real del cuerpo de la guitarra.
        public void Impact(Vector3 point, float power)
        {
            KomSpecialFx.Impact(point, _color, 1.35f * power, true);
            KomSpecialFx.AuraRing(point, _color, 1.1f * power);
            KomSpecialFx.NoteBurst(point, _color, 10);
            if (_sparks != null)
            {
                _sparks.transform.position = point;
                _sparks.Emit(Mathf.RoundToInt(26 * power));
            }
            KomSpecialFx.GroundShock(point, _color, 1.1f * power);
            KomSpecialFx.DustBurst(point, new Color(0.55f, 0.5f, 0.44f), Mathf.RoundToInt(14 * power), power);
        }

        public void Discharge()
        {
            _charged = false;
        }

        void OnDestroy()
        {
            if (_trailWide != null) _trailWide.Stop();
            if (_trailCore != null) _trailCore.Stop();
            foreach (var mat in _materials)
                if (mat != null) Destroy(mat);
        }
    }

    // =====================================================================================
    // CARGA DE INSTRUMENTO (acordeon / guitarra)
    // =====================================================================================
    // Sustituye al orbe de plasma cuando el especial lleva prop: un orbe entre las manos
    // se leia como "la bola de fuego de Juan" y tapaba el instrumento. Aqui el prota es
    // el PROP: se le clona la malla con el shader de aura (fresnel aditivo), se le cruzan
    // anillos que giran y las notas/chispas CONVERGEN hacia el mientras carga.
    public sealed class InstrumentChargeFx : MonoBehaviour
    {
        Fighter _owner;
        SpecialDef _special;
        GameObject _prop;
        Color _color;
        Color _hot;
        readonly List<GameObject> _shells = new List<GameObject>();
        readonly List<Material> _shellMats = new List<Material>();
        readonly List<float> _shellScales = new List<float>();
        readonly List<Transform> _rings = new List<Transform>();
        readonly List<Vector3> _ringAxes = new List<Vector3>();
        readonly List<Material> _ringMats = new List<Material>();
        readonly List<float> _ringPhase = new List<float>();
        ParticleSystem _notes;
        ParticleSystem _sparks;
        Light _light;
        Material _noteMat;
        Material _sparkMat;
        float _progress;
        float _t;
        bool _released;

        public void Init(Fighter owner, SpecialDef special, GameObject prop)
        {
            _owner = owner;
            _special = special;
            _prop = prop;
            _color = KomData.SpecialColor(owner != null ? owner.Def : null, special);
            _hot = Color.Lerp(_color, Color.white, 0.55f);
            if (_prop == null) return;

            // Cascara de aura sobre la malla REAL del instrumento (mismo shader que el
            // aura del personaje: fresnel aditivo, sin borde duro).
            foreach (var renderer in _prop.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var shell = new GameObject(renderer.name + "_ChargeShell");
                shell.transform.SetParent(renderer.transform, false);
                shell.transform.localPosition = Vector3.zero;
                shell.transform.localRotation = Quaternion.identity;
                shell.transform.localScale = Vector3.one;
                shell.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var mat = KomSpecialFx.CreateAuraMaterial(_color, _hot, 0.35f);
                if (mat == null) { Destroy(shell); continue; }
                // _Expand va en espacio de OBJETO: la malla del prop esta autorada en
                // unidades diminutas (0.03) y el nodo la escala x100. Sin dividir por la
                // escala, un expand de 1 cm se convierte en 1 metro de halo.
                float worldScale = Mathf.Max(0.0001f, renderer.transform.lossyScale.x);
                mat.SetFloat("_Expand", 0.018f / worldScale);
                mat.SetFloat("_BodyHeight", 1f);
                var sr = shell.AddComponent<MeshRenderer>();
                sr.sharedMaterial = mat;
                sr.shadowCastingMode = ShadowCastingMode.Off;
                sr.receiveShadows = false;
                sr.sortingOrder = 4;
                _shells.Add(shell);
                _shellMats.Add(mat);
                _shellScales.Add(worldScale);
            }

            // Ondas de sonido: anillos finos que nacen en el instrumento y se ABREN hacia
            // fuera, desfasados entre si. Es lo que separa esta carga del orbe de plasma
            // del rayo: aqui no hay esfera, hay resonancia. Los anillos anteriores giraban
            // cruzados sobre un prop pequeno y, sumados a la cascara, se leian como una
            // bola dorada (el "fireball" de Juan).
            for (int i = 0; i < 3; i++)
            {
                var ring = KomSpecialFx.CreateRing3D(transform, "ChargeRing" + i, _color, 0.34f, 0.22f, 0.5f);
                _rings.Add(ring.transform);
                _ringMats.Add(ring.GetComponent<MeshRenderer>().sharedMaterial);
                _ringPhase.Add(i / 3f);
                _ringAxes.Add(new Vector3(0.35f * i, 1f, 0.4f * (i % 2 == 0 ? 1f : -1f)).normalized);
            }

            // Notas que CONVERGEN al instrumento (radial negativo).
            _noteMat = KomSpecialFx.CreateParticleMaterial(KomSpecialFx.NoteTexture);
            _notes = KomSpecialFx.CreateParticles(transform, "ChargeNotes", _color, true, 12f,
                0.55f, 1.0f, 0.09f, 0.17f, _noteMat);
            var noteShape = _notes.shape;
            noteShape.shapeType = ParticleSystemShapeType.Sphere;
            noteShape.radius = 0.75f;
            KomSpecialFx.SetVelocity(_notes, Vector3.zero, -1.15f, 0f, 0f, 0f);
            var noteMain = _notes.main;
            noteMain.startRotation = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            noteMain.gravityModifier = 0f;

            _sparkMat = KomSpecialFx.CreateParticleMaterial();
            _sparks = KomSpecialFx.CreateStreaks(transform, "ChargeSparks", _color, 22f,
                0.18f, 0.36f, 0.015f, 0.04f, _sparkMat);
            var sparkShape = _sparks.shape;
            sparkShape.shapeType = ParticleSystemShapeType.Sphere;
            sparkShape.radius = 0.55f;
            KomSpecialFx.SetVelocity(_sparks, Vector3.zero, -1.9f, 0f, 0f, 0f);

            _light = KomSpecialFx.CreateLight(transform, _color, 2.2f, 0.45f);
        }

        public void SetProgress(float progress)
        {
            _progress = Mathf.Clamp01(progress);
        }

        void LateUpdate()
        {
            if (_prop == null || _released) return;
            _t += Time.deltaTime;
            float p = _progress;
            float pulse = 0.75f + Mathf.Sin(_t * (7f + 9f * p)) * 0.25f;
            for (int i = 0; i < _shellMats.Count; i++)
            {
                var mat = _shellMats[i];
                if (mat == null) continue;
                // Rim fino: el instrumento conserva su silueta y su material. Antes el
                // contorno aditivo subia a ~2.9 y el acordeon desaparecia dentro de una
                // bola dorada.
                mat.SetFloat("_Intensity", (0.18f + 0.75f * p) * pulse);
                mat.SetFloat("_Expand", (0.004f + 0.006f * p) / _shellScales[i]);
            }
            Vector3 center = KomPropGrip.BodyCenter(_prop);
            transform.position = center;
            for (int i = 0; i < _rings.Count; i++)
            {
                var ring = _rings[i];
                if (ring == null) continue;
                // Onda que se abre y se apaga: nace pegada al instrumento y muere al
                // doblar su tamano. El desfase por anillo evita el efecto "aro fijo".
                float k = Mathf.Repeat(_t * (0.85f + 0.5f * p) + _ringPhase[i], 1f);
                float s = Mathf.Lerp(0.14f, 0.74f + 0.2f * p, k);
                ring.localRotation = Quaternion.AngleAxis(_ringAxes[i].x * 26f + _t * 10f, _ringAxes[i]);
                ring.localScale = new Vector3(s, s, 0.22f);
                var ringMat = _ringMats[i];
                if (ringMat != null) ringMat.SetFloat("_Intensity", Mathf.Sin(k * Mathf.PI) * (0.32f + 0.45f * p));
            }
            if (_light != null)
            {
                _light.transform.position = center;
                _light.intensity = 0.25f + 0.85f * p * pulse;
            }
            if (_notes != null) _notes.transform.position = center;
            if (_sparks != null) _sparks.transform.position = center;
        }

        public void Release()
        {
            if (_released) return;
            _released = true;
            Vector3 at = _prop != null ? KomPropGrip.BodyCenter(_prop) : transform.position;
            KomSpecialFx.MuzzleFlash(at, _color, 0.95f);
            KomSpecialFx.AuraRing(at, _color, 1.15f);
            KomSpecialFx.NoteBurst(at, _color, 12);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            foreach (var shell in _shells)
                if (shell != null) Destroy(shell);
            foreach (var mat in _shellMats)
                if (mat != null) Destroy(mat);
            if (_noteMat != null) Destroy(_noteMat);
            if (_sparkMat != null) Destroy(_sparkMat);
        }
    }
}
