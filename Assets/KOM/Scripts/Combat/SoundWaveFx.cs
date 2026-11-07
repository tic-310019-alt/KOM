using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KOM
{
    // =====================================================================================
    // ONDA DE SONIDO (Nota Final — Jose Jose)
    // =====================================================================================
    // El cantante carga con la voz y lanza un FRENTE DE CHOQUE de largo alcance. No es una
    // bola de plasma: es una onda. Se construye con tres frentes elipticos (tori) cuyo eje
    // apunta en la direccion de avance, un nucleo brillante pequeno, cintas de estela y
    // notas musicales que salen despedidas. Los frentes se abren y se aplanan al viajar
    // (dispersion), que es lo que la lee como sonido y no como proyectil solido.
    public sealed class SoundWaveFx : MonoBehaviour
    {
        Fighter _owner;
        SpecialDef _special;
        Color _color;
        Vector3 _dir;
        float _speed;
        float _traveled;
        float _maxTravel;
        float _age;
        Transform _core;
        readonly List<Transform> _fronts = new List<Transform>();
        readonly List<Material> _frontMats = new List<Material>();
        readonly List<float> _frontPhase = new List<float>();
        readonly List<Transform> _discs = new List<Transform>();
        readonly List<Material> _discMats = new List<Material>();
        KomRibbon _trailWide;
        KomRibbon _trailCore;
        ParticleSystem _notes;
        ParticleSystem _streaks;
        Light _light;
        readonly List<Material> _materials = new List<Material>();
        Material _coreMat;

        // Frente base: ancho 1.55 m, alto 1.15 m. Es mas ancho que alto a proposito: una
        // onda de voz se abre en abanico, no en circulo perfecto.
        const float FrontWidth = 1.45f;
        const float FrontHeight = 0.98f;

        public void Init(Fighter owner, SpecialDef special, Vector3 origin, Vector3 dir)
        {
            _owner = owner;
            _special = special;
            _color = KomData.SpecialColor(owner != null ? owner.Def : null, special);
            _dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.right;
            _speed = Mathf.Max(2f, special != null ? special.projectileSpeed : 11f);
            _maxTravel = (special != null ? special.range : 7f) + 2.5f;
            transform.position = origin;
            transform.rotation = Quaternion.LookRotation(_dir, Vector3.up);

            // Nucleo: pequeno y muy brillante. Da el punto de origen de la onda; el volumen
            // lo ponen los frentes, no el.
            _coreMat = StageBuilder.GetEmissive(Color.Lerp(_color, Color.white, 0.85f), 2.0f);
            var core = new GameObject("WaveCore");
            core.transform.SetParent(transform, false);
            core.transform.localScale = Vector3.one * 0.16f;
            core.AddComponent<MeshFilter>().sharedMesh = KomSpecialFx.SphereMesh;
            var coreRenderer = core.AddComponent<MeshRenderer>();
            coreRenderer.sharedMaterial = _coreMat;
            coreRenderer.shadowCastingMode = ShadowCastingMode.Off;
            coreRenderer.receiveShadows = false;
            _core = core.transform;

            // Tres frentes desfasados: el primero marca el golpe, los otros dos dan grosor
            // y estela. Sin desfase se leen como un solo aro plano.
            for (int i = 0; i < 3; i++)
            {
                var ring = KomSpecialFx.CreateRing3D(transform, "WaveFront" + i,
                    Color.Lerp(_color, Color.white, 0.10f + i * 0.18f), 1f, 0.42f, 0.55f - i * 0.12f);
                // El eje del toro es su +Z local: con la raiz mirando a _dir, el frente
                // queda perpendicular al avance sin rotacion extra.
                ring.transform.localRotation = Quaternion.identity;
                _fronts.Add(ring.transform);
                _frontMats.Add(ring.GetComponent<MeshRenderer>().sharedMaterial);
                _frontPhase.Add(i * 0.12f);
            }

            // Frente SUAVE: dos planos con textura de anillo orientados perpendicular al
            // avance. Los tori dan el borde 3D; estos dan el cuerpo de la onda. Sin ellos
            // el efecto se lee como alambre, no como golpe de sonido.
            for (int i = 0; i < 3; i++)
            {
                var disc = KomSpecialFx.CreateGlowQuad(transform, "WaveDisc" + i,
                    Color.Lerp(_color, Color.white, 0.25f + i * 0.3f),
                    FrontWidth * (1.30f - i * 0.20f), FrontHeight * (1.30f - i * 0.20f),
                    KomFx.SoftRingTexture, 0.85f - i * 0.18f);
                disc.transform.localPosition = new Vector3(0f, 0f, 0.02f + i * 0.05f);
                _discs.Add(disc.transform);
                _discMats.Add(disc.GetComponent<MeshRenderer>().sharedMaterial);
                _materials.Add(disc.GetComponent<MeshRenderer>().sharedMaterial);
            }

            _trailWide = KomSpecialFx.CreateRibbon(transform, "WaveRibbonWide", _color, 0.42f, 0.22f, 0.28f);
            _trailCore = KomSpecialFx.CreateRibbon(transform, "WaveRibbonCore",
                Color.Lerp(_color, Color.white, 0.8f), 0.14f, 0.18f, 0.9f);

            var noteMat = KomSpecialFx.CreateParticleMaterial(KomSpecialFx.NoteTexture);
            _materials.Add(noteMat);
            _notes = KomSpecialFx.CreateParticles(transform, "WaveNotes", _color, false, 26f,
                0.6f, 1.15f, 0.09f, 0.18f, noteMat);
            var noteShape = _notes.shape;
            noteShape.shapeType = ParticleSystemShapeType.Sphere;
            noteShape.radius = 0.55f;
            var noteMain = _notes.main;
            noteMain.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.9f);
            noteMain.startRotation = new ParticleSystem.MinMaxCurve(-0.7f, 0.7f);
            noteMain.gravityModifier = -0.03f;

            var sparkMat = KomSpecialFx.CreateParticleMaterial();
            _materials.Add(sparkMat);
            _streaks = KomSpecialFx.CreateStreaks(transform, "WaveStreaks", _color, 40f,
                0.16f, 0.32f, 0.015f, 0.04f, sparkMat);
            var streakShape = _streaks.shape;
            streakShape.shapeType = ParticleSystemShapeType.Sphere;
            streakShape.radius = 0.45f;

            _light = KomSpecialFx.CreateLight(transform, _color, 3.4f, 1.0f);
        }

        void Update()
        {
            if (_owner == null) { Destroy(gameObject); return; }
            float dt = Time.deltaTime;
            transform.position += _dir * _speed * dt;
            _traveled += _speed * dt;
            _age += dt;

            float t = Mathf.Clamp01(_traveled / Mathf.Max(0.01f, _maxTravel));
            // Dispersion: la onda se abre y pierde altura al alejarse (como el sonido).
            float widen = 1f + 0.55f * t;
            float flatten = 1f - 0.28f * t;

            for (int i = 0; i < _fronts.Count; i++)
            {
                var front = _fronts[i];
                if (front == null) continue;
                float k = Mathf.Repeat(_age * 1.5f + _frontPhase[i], 1f);
                float grow = 0.72f + 0.5f * k;
                front.localScale = new Vector3(
                    FrontWidth * widen * grow,
                    FrontHeight * flatten * grow,
                    0.22f + 0.10f * (1f - k));
                front.localPosition = new Vector3(0f, 0f, -0.10f - 0.16f * i);
                front.localRotation = Quaternion.AngleAxis(_age * 26f + i * 40f, Vector3.forward);
                var mat = _frontMats[i];
                if (mat != null)
                    mat.SetFloat("_Intensity", Mathf.Sin(k * Mathf.PI) * (1.05f - i * 0.22f) * (1f - 0.35f * t));
            }

            for (int i = 0; i < _discs.Count; i++)
            {
                var disc = _discs[i];
                if (disc == null) continue;
                float k = Mathf.Repeat(_age * 1.9f + i * 0.34f, 1f);
                float g = 0.80f + 0.60f * k;
                disc.localScale = new Vector3(
                    FrontWidth * widen * g, FrontHeight * flatten * g, 1f);
                disc.localPosition = new Vector3(0f, 0f, 0.02f + i * 0.05f - 0.12f * k);
                var dm = _discMats[i];
                if (dm != null) dm.SetFloat("_Intensity", Mathf.Sin(k * Mathf.PI) * (0.95f - i * 0.18f) * (1f - 0.4f * t));
            }

            if (_core != null) _core.localScale = Vector3.one * (0.16f * (1f + Mathf.Sin(_age * 30f) * 0.08f));
            if (_light != null) _light.intensity = (1.0f - 0.4f * t) * (1f + Mathf.Sin(_age * 26f) * 0.12f);

            // Colision BARRIDA: se muestrea el segmento recorrido este frame. A 12 m/s un
            // frame largo (hitch del editor, primer frame tras cargar shaders) avanza mas
            // que la ventana del hitbox y el proyectil atraviesa al rival sin golpearlo.
            bool hit = false;
            Vector3 hitAt = transform.position;
            Vector3 from = transform.position - _dir * _speed * dt;
            float step = 0.25f;
            float span = Vector3.Distance(from, transform.position);
            int steps = Mathf.Max(1, Mathf.CeilToInt(span / step));
            for (int i = 1; i <= steps; i++)
            {
                Vector3 probe = Vector3.Lerp(from, transform.position, i / (float)steps);
                if (_owner.ResolveSpecialHit(probe, _special, true)) { hit = true; hitAt = probe; break; }
            }

            if (hit)
            {
                // El contacto ocurre en el borde del hurtbox: el fogonazo se acerca 30 cm
                // al cuerpo para que se lea sobre el rival y no flotando en el aire.
                Vector3 hitPoint = hitAt + _dir * 0.30f;
                KomSpecialFx.Impact(hitPoint, _color, 1.35f, true);
                KomSpecialFx.AuraRing(hitPoint, _color, 1.25f);
                KomSpecialFx.NoteBurst(hitPoint, _color, 16);
                KomSpecialFx.GroundShock(hitPoint, _color, 0.9f);
                if (_notes != null) _notes.Emit(20);
                Destroy(gameObject);
                return;
            }
            if (_traveled >= _maxTravel || Mathf.Abs(transform.position.x) > 16f)
            {
                KomSpecialFx.Impact(transform.position, _color, 0.8f, true);
                Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            if (_trailWide != null) _trailWide.Stop();
            if (_trailCore != null) _trailCore.Stop();
            foreach (var mat in _materials)
                if (mat != null) Destroy(mat);
            // _coreMat viene de StageBuilder.GetEmissive (cache compartida): no se destruye.
        }
    }
}
