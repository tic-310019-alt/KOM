using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KOM
{
    // =====================================================================================
    // CARGA
    // =====================================================================================
    // Orbe de plasma entre las manos (beam) o en la palma (chain). Construido por capas:
    // nucleo blanco -> cascara de plasma -> anillos 3D girando -> arcos electricos ->
    // particulas que CONVERGEN al centro. Nada de billboards planos gigantes.
    public sealed class SpecialChargeFx : MonoBehaviour
    {
        Fighter _owner;
        SpecialDef _special;
        Color _color;
        Color _coreColor;
        Transform _core;
        Transform _shell;
        Transform _halo;
        readonly List<Transform> _rings = new List<Transform>();
        readonly List<Vector3> _ringAxes = new List<Vector3>();
        readonly List<float> _ringSpeeds = new List<float>();
        readonly List<KomBoltFx> _bolts = new List<KomBoltFx>();
        ParticleSystem _particles;
        Light _light;
        Material _shellMat;
        Material _coreMat;
        readonly List<Material> _materials = new List<Material>();
        readonly List<GameObject> _palmGlows = new List<GameObject>();
        float _t;
        float _duration;
        bool _small;
        float _boltRadius;
        float _progressOverride = -1f;

        // La carga se dibuja con el tiempo NORMALIZADO del Animator (no con un timer
        // paralelo): el orbe llega a su tamano maximo EXACTAMENTE en el frame de
        // liberacion aunque el clip vaya a otra velocidad.
        public void SetProgress(float p) => _progressOverride = Mathf.Clamp01(p);

        public void Init(Fighter owner, SpecialDef special)
        {
            _owner = owner;
            _special = special;
            // Los especiales con prop (acordeon/guitarra) usan la carga compacta: un
            // orbe de 0.46 m tapaba el prop y no se veia el agarre.
            _small = special != null && (special.type == "chain" || !string.IsNullOrEmpty(special.propResource));
            _color = _small
                ? KomData.Accent(owner != null ? owner.Def : null)
                : KomData.SpecialColor(owner != null ? owner.Def : null, special);
            _coreColor = Color.Lerp(_color, Color.white, 0.55f);
            _duration = Mathf.Max(0.05f, special != null ? special.releaseTime : 0.5f);
            transform.position = Anchor();

            float coreDia = _small ? 0.07f : 0.12f;
            _coreMat = StageBuilder.GetEmissive(Color.Lerp(_color, Color.white, 0.7f), 1.5f);
            var core = new GameObject("ChargeCore");
            core.transform.SetParent(transform, false);
            core.transform.localScale = Vector3.one * coreDia;
            core.AddComponent<MeshFilter>().sharedMesh = KomSpecialFx.SphereMesh;
            var coreRenderer = core.AddComponent<MeshRenderer>();
            coreRenderer.sharedMaterial = _coreMat;
            coreRenderer.shadowCastingMode = ShadowCastingMode.Off;
            coreRenderer.receiveShadows = false;
            _core = core.transform;

            float shellDia = _small ? 0.17f : 0.46f;
            var shell = KomSpecialFx.CreateOrb(transform, "ChargeShell", _color, _coreColor, shellDia, 2.0f);
            _shell = shell.transform;
            _shellMat = shell.GetComponent<MeshRenderer>().sharedMaterial;

            var halo = KomSpecialFx.CreateGlowQuad(transform, "ChargeHalo", _color,
                _small ? 0.20f : 0.72f, _small ? 0.20f : 0.72f, KomSpecialFx.GlowTexture, 0.20f);
            _halo = halo.transform;
            _materials.Add(halo.GetComponent<MeshRenderer>().sharedMaterial);

            int ringCount = _small ? 2 : 3;
            for (int i = 0; i < ringCount; i++)
            {
                float radius = (_small ? 0.125f : 0.225f) * (1f + i * 0.07f);
                var ring = KomSpecialFx.CreateRing3D(transform, "ChargeRing" + i,
                    Color.Lerp(_color, Color.white, i * 0.25f), radius, 1.1f, 0.40f);
                _rings.Add(ring.transform);
                _ringAxes.Add(i == 0 ? Vector3.up : (i == 1 ? Vector3.right : Vector3.forward));
                ring.transform.localRotation = Quaternion.Euler(i == 0 ? 64f : (i == 1 ? -38f : 22f), i * 51f, i * 33f);
                _ringSpeeds.Add((i % 2 == 0 ? 1f : -1f) * (120f + i * 45f));
                _materials.Add(ring.GetComponent<MeshRenderer>().sharedMaterial);
            }

            _boltRadius = _small ? 0.15f : 0.3f;
            int bolts = _small ? 3 : 6;
            for (int i = 0; i < bolts; i++)
            {
                float ang = i / (float)bolts * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang) * 0.7f, Mathf.Sin(ang) * 0.5f);
                var bolt = KomSpecialFx.CreateBolt(transform, "ChargeBolt" + i,
                    Vector3.zero, dir * _boltRadius, _coreColor, _small ? 0.008f : 0.014f,
                    _boltRadius * 0.55f, 1300 + i, 0.05f);
                _bolts.Add(bolt);
            }

            var particleMat = KomSpecialFx.CreateParticleMaterial();
            _materials.Add(particleMat);
            _particles = KomSpecialFx.CreateParticles(transform, "ChargeParticles", _color, true,
                _small ? 26f : 64f, 0.2f, 0.5f, 0.018f, 0.055f, particleMat);
            var shape = _particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = _small ? 0.2f : 0.42f;
            // Radial NEGATIVO: las particulas caen hacia el centro (esta cargando).
            KomSpecialFx.SetVelocity(_particles, Vector3.zero, -2.9f, 0f, 2.2f, 0f);

            _light = KomSpecialFx.CreateLight(transform, _color, _small ? 2.2f : 3.0f, 0.8f);

            if (owner != null && !_small)
            {
                AddPalmGlow(owner.RightPalmPosition, 0.26f);
                AddPalmGlow(owner.LeftPalmPosition, 0.26f);
            }
            else if (owner != null)
            {
                AddPalmGlow(owner.RightPalmPosition, 0.22f);
            }
        }

        void AddPalmGlow(Vector3 position, float size)
        {
            var go = KomSpecialFx.CreateGlowQuad(transform, "PalmGlow", _coreColor, size, size,
                KomSpecialFx.GlowTexture, 1.5f);
            go.transform.position = position;
            _materials.Add(go.GetComponent<MeshRenderer>().sharedMaterial);
            _palmGlows.Add(go);
        }

        Vector3 Anchor()
        {
            if (_owner == null) return transform.position;
            return _small ? _owner.RightPalmPosition : _owner.HandMidPosition;
        }

        void Update()
        {
            if (_owner == null) { Destroy(gameObject); return; }
            _t += Time.deltaTime;
            transform.position = Anchor();
            float p = _progressOverride >= 0f
                ? _progressOverride
                : Mathf.Clamp01(_t / _duration);
            // Curva de carga: arranque lento, aceleracion al final (el orbe "se llena").
            float grow = Mathf.Pow(p, 0.55f);
            float pulse = 1f + Mathf.Sin(Time.time * 21f) * 0.05f;
            float scale = Mathf.Lerp(0.35f, 1f, grow) * pulse;

            if (_core != null) _core.localScale = Vector3.one * ((_small ? 0.07f : 0.12f) * scale);
            if (_shell != null)
            {
                float d = (_small ? 0.17f : 0.46f) * scale;
                _shell.localScale = Vector3.one * d;
            }
            if (_shellMat != null)
                _shellMat.SetFloat("_Intensity", Mathf.Lerp(0.65f, 1.05f, grow));
            if (_halo != null)
            {
                float g = (_small ? 0.34f : 0.72f) * scale;
                _halo.localScale = new Vector3(g, g, 1f);
            }
            for (int i = 0; i < _rings.Count; i++)
            {
                var ring = _rings[i];
                if (ring == null) continue;
                float r = (_small ? 0.125f : 0.225f) * (1f + i * 0.07f) * scale * (1f + Mathf.Sin(Time.time * 9f + i) * 0.05f);
                ring.localScale = new Vector3(r, r, 0.9f);
                ring.Rotate(_ringAxes[i], _ringSpeeds[i] * Time.deltaTime, Space.Self);
            }
            for (int i = 0; i < _bolts.Count; i++)
            {
                if (_bolts[i] == null) continue;
                float ang = i / (float)_bolts.Count * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang) * 0.7f, Mathf.Sin(ang) * 0.5f);
                _bolts[i].SetEnd(dir * _boltRadius * scale * 1.05f);
                _bolts[i].SetFade(Mathf.Lerp(0.25f, 1f, grow));
            }
            if (_light != null) _light.intensity = Mathf.Lerp(0.25f, 0.85f, grow) * pulse;
            if (_particles != null)
            {
                var emission = _particles.emission;
                emission.rateOverTime = Mathf.Lerp(12f, _small ? 40f : 90f, grow);
            }
            for (int i = 0; i < _palmGlows.Count; i++)
            {
                var glow = _palmGlows[i];
                if (glow == null) continue;
                Vector3 pos = _owner == null ? glow.transform.position :
                    (i == 0 ? _owner.RightPalmPosition : _owner.LeftPalmPosition);
                glow.transform.position = pos;
                float s = 0.22f + 0.1f * Mathf.Sin(Time.time * 17f + i);
                glow.transform.localScale = new Vector3(s, s, 1f);
            }
        }

        public void Release()
        {
            if (_owner != null)
            {
                Vector3 at = Anchor();
                KomSpecialFx.MuzzleFlash(at, _color, _small ? 0.5f : 1.1f);
                KomSpecialFx.AuraRing(at, _color, _small ? 0.7f : 1.6f);
            }
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            foreach (var mat in _materials)
                if (mat != null) Destroy(mat);
            if (_shellMat != null) Destroy(_shellMat);
            // _coreMat viene de StageBuilder.GetEmissive (cache compartida): no se destruye.
        }
    }

    // =====================================================================================
    // PROYECTIL (Rayo de Interpretacion)
    // =====================================================================================
    // Cometa de plasma: nucleo blanco + cascara + anillos en orbita + arcos + doble
    // estela con perfil suave + chispas alargadas + luz. La colision usa el AABB del JSON.
    public sealed class SpecialProjectileFx : MonoBehaviour
    {
        Fighter _owner;
        SpecialDef _special;
        Color _color;
        Color _coreColor;
        Vector3 _dir;
        float _speed;
        float _traveled;
        float _maxTravel;
        Transform _core;
        Transform _shell;
        Transform _halo;
        readonly List<Transform> _rings = new List<Transform>();
        readonly List<KomBoltFx> _bolts = new List<KomBoltFx>();
        KomRibbon _trailWide;
        KomRibbon _trailCore;
        ParticleSystem _streaks;
        Light _light;
        Material _shellMat;
        Material _coreMat;
        readonly List<Material> _materials = new List<Material>();
        float _age;
        const float ShellDia = 0.46f;

        public void Init(Fighter owner, SpecialDef special, Vector3 origin, Vector3 dir)
        {
            _owner = owner;
            _special = special;
            _color = KomData.SpecialColor(owner != null ? owner.Def : null, special);
            _coreColor = Color.Lerp(_color, Color.white, 0.60f);
            _dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.right;
            _speed = Mathf.Max(1f, special != null ? special.projectileSpeed : 8f);
            _maxTravel = (special != null ? special.range : 4f) + 2.2f;
            transform.position = origin;

            _coreMat = StageBuilder.GetEmissive(Color.Lerp(_color, Color.white, 0.8f), 1.7f);
            var core = new GameObject("BeamCore");
            core.transform.SetParent(transform, false);
            core.transform.localScale = Vector3.one * 0.15f;
            core.AddComponent<MeshFilter>().sharedMesh = KomSpecialFx.SphereMesh;
            var coreRenderer = core.AddComponent<MeshRenderer>();
            coreRenderer.sharedMaterial = _coreMat;
            coreRenderer.shadowCastingMode = ShadowCastingMode.Off;
            coreRenderer.receiveShadows = false;
            _core = core.transform;

            var shell = KomSpecialFx.CreateOrb(transform, "BeamShell", _color, _coreColor, ShellDia, 2.6f);
            _shell = shell.transform;
            _shellMat = shell.GetComponent<MeshRenderer>().sharedMaterial;

            var halo = KomSpecialFx.CreateGlowQuad(transform, "BeamHalo", _color, 0.72f, 0.72f,
                KomSpecialFx.GlowTexture, 0.34f);
            _halo = halo.transform;
            _materials.Add(halo.GetComponent<MeshRenderer>().sharedMaterial);

            for (int i = 0; i < 3; i++)
            {
                var ring = KomSpecialFx.CreateRing3D(transform, "BeamRing" + i,
                    Color.Lerp(_color, Color.white, 0.15f + i * 0.2f), 0.20f + i * 0.035f, 0.5f, 0.45f);
                ring.transform.localRotation = Quaternion.Euler(i * 62f, i * 41f, i * 23f);
                _rings.Add(ring.transform);
                _materials.Add(ring.GetComponent<MeshRenderer>().sharedMaterial);
            }

            for (int i = 0; i < 4; i++)
            {
                float ang = i / 4f * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), Mathf.Cos(ang * 1.7f));
                var bolt = KomSpecialFx.CreateBolt(transform, "BeamBolt" + i, Vector3.zero, d.normalized * 0.26f,
                    _coreColor, 0.012f, 0.13f, 2400 + i, 0.05f);
                _bolts.Add(bolt);
            }

            _trailWide = KomSpecialFx.CreateRibbon(transform, "BeamRibbonWide", _color, 0.60f, 0.30f, 0.85f);
            _trailCore = KomSpecialFx.CreateRibbon(transform, "BeamRibbonCore",
                Color.Lerp(_color, Color.white, 0.75f), 0.20f, 0.24f, 2.0f);

            var streakMat = KomSpecialFx.CreateParticleMaterial();
            _materials.Add(streakMat);
            _streaks = KomSpecialFx.CreateStreaks(transform, "BeamStreaks", _color, 45f,
                0.14f, 0.28f, 0.02f, 0.05f, streakMat);
            var shape = _streaks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.16f;

            _light = KomSpecialFx.CreateLight(transform, _color, 3.2f, 1.1f);
        }

        void Update()
        {
            if (_owner == null) { Destroy(gameObject); return; }
            float dt = Time.deltaTime;
            transform.position += _dir * _speed * dt;
            _traveled += _speed * dt;
            _age += dt;
            float pulse = 1f + Mathf.Sin(_age * 24f) * 0.06f;
            if (_core != null) _core.localScale = Vector3.one * (0.15f * pulse);
            if (_shell != null) _shell.localScale = Vector3.one * (ShellDia * pulse);
            if (_shellMat != null) _shellMat.SetFloat("_Intensity", 1.15f + Mathf.Sin(_age * 19f) * 0.18f);
            if (_halo != null)
            {
                float g = 0.72f * (0.92f + 0.1f * pulse);
                _halo.localScale = new Vector3(g, g, 1f);
            }
            for (int i = 0; i < _rings.Count; i++)
            {
                var ring = _rings[i];
                if (ring == null) continue;
                ring.Rotate(Vector3.up, (220f + i * 90f) * dt, Space.Self);
                ring.Rotate(Vector3.right, (140f + i * 60f) * dt, Space.Self);
                float r = (0.20f + i * 0.035f) * (0.95f + Mathf.Sin(_age * 13f + i) * 0.05f);
                ring.localScale = new Vector3(r, r, 0.9f);
            }
            if (_light != null) _light.intensity = 1.1f + Mathf.Sin(_age * 21f) * 0.2f;

            if (_owner.ResolveSpecialHit(transform.position, _special, true))
            {
                // El contacto ocurre en el borde del hurtbox; el fogonazo se acerca
                // 30 cm al cuerpo para que se lea sobre el rival, no en el aire.
                KomSpecialFx.Impact(transform.position + _dir * 0.30f, _color, 1.25f);
                Destroy(gameObject);
                return;
            }
            if (_traveled >= _maxTravel || Mathf.Abs(transform.position.x) > 13f)
            {
                KomSpecialFx.Impact(transform.position, _color, 0.7f);
                Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            // Las cintas viven fuera del proyectil: se les dice que dejen de muestrear
            // y se consumen solas (asi el impacto no corta la estela de golpe).
            if (_trailWide != null) _trailWide.Stop();
            if (_trailCore != null) _trailCore.Stop();
            foreach (var mat in _materials)
                if (mat != null) Destroy(mat);
            if (_shellMat != null) Destroy(_shellMat);
            // _coreMat viene de StageBuilder.GetEmissive (cache compartida): no se destruye.
        }
    }

    // =====================================================================================
    // CADENA (Cadena Belica)
    // =====================================================================================
    // Eslabones ovalados REALES con paso constante (la cadena no se estira: crece desde
    // la mano). Curva de latigo con salida desde la palma, caida de catenaria, onda
    // viajera y latigazo final. Peso metalico en la punta. El punto 0 de la curva es
    // SIEMPRE RightPalmPosition.
    public sealed class ChainWhipFx : MonoBehaviour
    {
        Fighter _owner;
        SpecialDef _special;
        Color _color;
        Color _metalGlow;
        Vector3 _target;
        float _extend;
        float _hold;
        const float RetractTime = 0.3f;
        float _t;
        float _crack;
        float _pitch;
        int _linkCount;
        Transform[] _links;
        Transform _tipWeight;
        LineRenderer _coreLine;
        LineRenderer _glowLine;
        ParticleSystem _tipSparks;
        ParticleSystem _trailSparks;
        GameObject _handGlow;
        Light _light;
        Material _silver;
        Material _tipMat;
        readonly List<Material> _materials = new List<Material>();
        const int Samples = 80;
        readonly Vector3[] _curve = new Vector3[Samples];
        readonly float[] _arc = new float[Samples];

        public Vector3 CurrentTip { get; private set; }
        public Vector3 TargetTip => _target;
        public float AnchorError { get; private set; }
        public float LinkSpacingError { get; private set; }
        public int ActiveLinks { get; private set; }

        public void Init(Fighter owner, SpecialDef special, Transform hand, Transform finger, Vector3 anchor, Vector3 target)
        {
            _owner = owner;
            _special = special;
            _color = KomData.SpecialColor(owner != null ? owner.Def : null, special);
            _metalGlow = KomData.Accent(owner != null ? owner.Def : null);
            _target = target;
            _extend = Mathf.Max(0.06f, special != null ? special.extendTime : 0.16f);
            _hold = Mathf.Max(0.1f, special != null ? special.activeDuration : 0.5f);
            _linkCount = Mathf.Clamp(Mathf.RoundToInt(special != null ? special.chainLinks : 26f), 8, 40);
            // Paso constante: la cadena mide `range` con `chainLinks` eslabones.
            float range = Mathf.Max(1f, special != null ? special.range : 3.4f);
            _pitch = range / _linkCount;

            _silver = KomSpecialFx.CreateChainSilver(_metalGlow);

            // Pool de eslabones: se activan solo los que caben en la longitud actual.
            _links = new Transform[_linkCount + 8];
            for (int i = 0; i < _links.Length; i++)
            {
                var link = new GameObject("ChainLink" + i);
                link.transform.SetParent(transform, false);
                var filter = link.AddComponent<MeshFilter>();
                filter.sharedMesh = KomSpecialFx.LinkMesh;
                var renderer = link.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _silver;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                link.transform.localScale = Vector3.one * _pitch;
                link.SetActive(false);
                _links[i] = link.transform;
            }

            // Peso metalico en la punta (como en el concepto de la Cadena Belica).
            var tip = new GameObject("ChainTipWeight");
            tip.transform.SetParent(transform, false);
            tip.AddComponent<MeshFilter>().sharedMesh = KomSpecialFx.SphereMesh;
            var tipRenderer = tip.AddComponent<MeshRenderer>();
            _tipMat = KomSpecialFx.CreateChainSilver(_metalGlow);
            _tipMat.SetFloat("_Glow", 0.75f);
            tipRenderer.sharedMaterial = _tipMat;
            tipRenderer.shadowCastingMode = ShadowCastingMode.Off;
            tipRenderer.receiveShadows = false;
            tip.transform.localScale = Vector3.one * (_pitch * 1.45f);
            _tipWeight = tip.transform;

            Color filament = Color.Lerp(_metalGlow, new Color(0.65f, 0.9f, 1f), 0.35f);
            _coreLine = CreateLine("ChainCore", 0.006f, filament, 0.55f, 5);
            _glowLine = CreateLine("ChainGlow", 0.05f, filament * 0.55f, 0.22f, 3);
            _coreLine.textureMode = LineTextureMode.Stretch;
            _glowLine.textureMode = LineTextureMode.Stretch;

            var handGlow = KomSpecialFx.CreateGlowQuad(transform, "ChainHandGlow", _metalGlow, 0.18f, 0.18f,
                KomSpecialFx.GlowTexture, 0.5f);
            _handGlow = handGlow;
            _materials.Add(handGlow.GetComponent<MeshRenderer>().sharedMaterial);


            var particleMat = KomSpecialFx.CreateParticleMaterial();
            _materials.Add(particleMat);
            _tipSparks = KomSpecialFx.CreateParticles(transform, "ChainTipSparks", _color, false, 0f,
                0.14f, 0.34f, 0.02f, 0.06f, particleMat);
            var shape = _tipSparks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;
            var main = _tipSparks.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.4f);

            var trailMat = KomSpecialFx.CreateParticleMaterial();
            _materials.Add(trailMat);
            _trailSparks = KomSpecialFx.CreateStreaks(transform, "ChainTrailSparks", _color, 26f,
                0.16f, 0.3f, 0.02f, 0.05f, trailMat);
            var tshape = _trailSparks.shape;
            tshape.shapeType = ParticleSystemShapeType.Sphere;
            tshape.radius = 0.08f;

            _light = KomSpecialFx.CreateLight(transform, _color, 3.2f, 1.4f);
            _light.transform.position = anchor;
        }

        LineRenderer CreateLine(string name, float width, Color color, float intensity, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            var mat = KomSpecialFx.CreateAdditiveMaterial(Color.white, KomSpecialFx.RibbonTexture, intensity);
            _materials.Add(mat);
            line.material = mat;
            line.useWorldSpace = true;
            line.positionCount = Samples;
            line.widthMultiplier = width;
            line.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.15f, 1f), new Keyframe(0.85f, 0.85f), new Keyframe(1f, 0.2f));
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.startColor = color;
            line.endColor = color * 0.5f;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = order;
            return line;
        }

        float ExtensionAt(float t)
        {
            if (t <= 0f) return 0f;
            if (t < _extend)
            {
                float k = t / _extend;
                // Salida explosiva y frenada: el latigo acelera y "cruje" al final.
                return 1f - Mathf.Pow(1f - k, 3.0f);
            }
            if (t < _extend + _hold) return 1f;
            float r = (t - _extend - _hold) / RetractTime;
            return r >= 1f ? 0f : 1f - Mathf.SmoothStep(0f, 1f, r);
        }

        void Update()
        {
            if (_owner == null) { Destroy(gameObject); return; }
            float dt = Time.deltaTime;
            _t += dt;

            Vector3 anchor = _owner.RightPalmPosition;
            float extension = ExtensionAt(_t);
            if (extension >= 0.999f && _crack <= 0f) _crack = 1f;
            if (_crack > 0f) _crack = Mathf.Max(0f, _crack - dt * 5.5f);

            Vector3 toTarget = _target - anchor;
            float dist = toTarget.magnitude;
            if (dist < 0.0001f) { toTarget = Vector3.forward; dist = 0.0001f; }
            Vector3 dir = toTarget / dist;
            Vector3 reachVec = dir * (dist * extension);
            Vector3 tip = anchor + reachVec;

            // Salida desde la mano: la cadena sale por donde apunta la mano, no por
            // una linea recta al rival. Se limita el angulo para que nunca se doble.
            Vector3 handDir = dir;
            if (_owner != null)
            {
                Vector3 palm = _owner.RightPalmPosition;
                Vector3 hd = _owner.RightHandDirection;
                if (hd.sqrMagnitude > 0.0001f) handDir = hd.normalized;
            }
            float ang = Vector3.Angle(handDir, dir);
            Vector3 exit = ang > 62f ? Vector3.Slerp(handDir, dir, (ang - 62f) / Mathf.Max(1f, ang)) : handDir;

            Vector3 side = Vector3.Cross(Vector3.up, dir);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
            side.Normalize();
            Vector3 up = Vector3.Cross(dir, side).normalized;

            float sag = (float)(_special != null ? _special.chainSag : 0.38f) * 1.75f *
                        (1f - extension * 0.45f) * Mathf.Clamp(dist / 3f, 0.35f, 1f);
            float amp = Mathf.Lerp(0.16f, 0.065f, extension) * Mathf.Clamp(dist / 3f, 0.35f, 1f);
            float phase = Time.time * 22f;
            float bend = Mathf.Clamp(dist * 0.14f, 0.08f, 0.42f);

            for (int i = 0; i < Samples; i++)
            {
                float t = i / (Samples - 1f);
                Vector3 p = Vector3.Lerp(anchor, tip, t);
                // Curvatura de salida de la mano (decae rapido).
                p += (exit - dir) * (bend * Mathf.Exp(-t * 6.0f));
                // Caida de cadena colgando.
                p -= Vector3.up * (sag * 4f * t * (1f - t));
                // Onda viajera del latigo (crece hacia la punta).
                p += side * (Mathf.Sin(t * Mathf.PI * 2.1f - phase) * amp * Mathf.Pow(t, 1.1f));
                p += up * (Mathf.Sin(t * Mathf.PI * 3.1f + phase * 0.7f) * amp * 0.32f * t);
                // Arco lateral permanente: lee como latigo aunque este tenso.
                p += side * (Mathf.Sin(t * Mathf.PI) * 0.14f * extension);
                // Latigazo final en la punta.
                p += side * (Mathf.Sin(t * Mathf.PI * 1.6f) * _crack * 0.16f * t * t);
                p.y = Mathf.Max(0.07f, p.y);
                if (i == 0) p = anchor;
                else if (i == Samples - 1) p = tip;
                _curve[i] = p;
            }

            // Longitud de arco acumulada (para colocar eslabones a paso constante).
            _arc[0] = 0f;
            for (int i = 1; i < Samples; i++)
                _arc[i] = _arc[i - 1] + Vector3.Distance(_curve[i - 1], _curve[i]);
            float totalLength = _arc[Samples - 1];

            CurrentTip = _curve[Samples - 1];
            AnchorError = Vector3.Distance(anchor, _curve[0]);
            PlaceLinks(totalLength);

            _coreLine.SetPositions(_curve);
            _glowLine.SetPositions(_curve);
            if (_handGlow != null) _handGlow.transform.position = anchor;
            if (_tipWeight != null)
            {
                _tipWeight.position = CurrentTip;
                _tipWeight.localScale = Vector3.one * (_pitch * 1.45f);
            }
            if (_tipSparks != null) _tipSparks.transform.position = CurrentTip;
            if (_trailSparks != null) _trailSparks.transform.position = Vector3.Lerp(anchor, CurrentTip, 0.72f);
            if (_light != null)
            {
                _light.transform.position = Vector3.Lerp(anchor, CurrentTip, 0.55f);
                _light.intensity = 0.9f + extension * 1.1f;
            }
            if (_t >= _extend + _hold + RetractTime) Destroy(gameObject);
        }

        // Coloca cada eslabon a `_pitch` de distancia de arco: el paso es CONSTANTE,
        // asi que la cadena no se estira cuando se extiende, crece desde la mano.
        void PlaceLinks(float totalLength)
        {
            if (_links == null || _links.Length == 0) return;
            int needed = Mathf.Clamp(Mathf.CeilToInt(totalLength / Mathf.Max(0.001f, _pitch)), 0, _links.Length);
            ActiveLinks = needed;
            int segment = 0;
            float minSpacing = float.MaxValue;
            float maxSpacing = 0f;
            float prevPos = -1f;
            for (int link = 0; link < _links.Length; link++)
            {
                if (link >= needed)
                {
                    if (_links[link].gameObject.activeSelf) _links[link].gameObject.SetActive(false);
                    continue;
                }
                if (!_links[link].gameObject.activeSelf) _links[link].gameObject.SetActive(true);
                float distance = link * _pitch;
                while (segment < Samples - 2 && _arc[segment + 1] < distance) segment++;
                float span = Mathf.Max(0.0001f, _arc[segment + 1] - _arc[segment]);
                float f = Mathf.Clamp01((distance - _arc[segment]) / span);
                Vector3 position = Vector3.Lerp(_curve[segment], _curve[segment + 1], f);
                Vector3 tangent = _curve[Mathf.Min(segment + 2, Samples - 1)] - _curve[Mathf.Max(segment - 1, 0)];
                if (tangent.sqrMagnitude < 0.000001f) tangent = Vector3.forward;
                tangent.Normalize();
                var rotation = Quaternion.LookRotation(tangent, Vector3.up) *
                               Quaternion.Euler(0f, 0f, link % 2 == 0 ? 0f : 90f);
                _links[link].position = position;
                _links[link].rotation = rotation;
                if (prevPos >= 0f)
                {
                    float spacing = distance - prevPos;
                    if (spacing < minSpacing) minSpacing = spacing;
                    if (spacing > maxSpacing) maxSpacing = spacing;
                }
                prevPos = distance;
            }
            LinkSpacingError = maxSpacing > 0f ? Mathf.Max(maxSpacing - _pitch, _pitch - minSpacing) : 0f;
        }

        public void PlayImpact()
        {
            if (_tipSparks != null) _tipSparks.Emit(30);
            KomSpecialFx.AuraRing(CurrentTip, _color, 0.85f);
        }

        void OnDestroy()
        {
            foreach (var mat in _materials)
                if (mat != null) Destroy(mat);
            if (_silver != null) Destroy(_silver);
            if (_tipMat != null) Destroy(_tipMat);
        }
    }

    // =====================================================================================
    // IMPACTO
    // =====================================================================================
    // Onda de choque en capas: fogonazo, dos anillos a distinto ritmo, anillo de suelo,
    // columna de luz, esfera de plasma que se abre, chispas radiales, esquirlas y luz.
    public sealed class SpecialImpactFx : MonoBehaviour
    {
        readonly List<Material> _materials = new List<Material>();
        Transform _flash;
        Transform _ringA;
        Transform _ringB;
        Transform _groundRing;
        Transform _pillar;
        Transform _orb;
        Material _orbMat;
        float _orbBase;
        float _scale;
        float _t;
        bool _physical;
        const float Duration = 0.72f;
        Light _light;

        public void Init(Color color, float scale, bool physical = false)
        {
            _scale = Mathf.Max(0.2f, scale);
            Color hot = Color.Lerp(color, Color.white, 0.6f);
            _physical = physical;

            _flash = AddQuad("ShockFlash", hot, 0.78f, KomSpecialFx.GlowTexture, 3.0f);
            _ringA = AddRing("ShockRingA", color, 0.62f, 0.85f, true);
            _ringB = AddRing("ShockRingB", Color.Lerp(color, Color.white, 0.35f), 0.44f, 0.65f, true);
            _ringA.gameObject.AddComponent<BillboardFx>();
            _ringB.gameObject.AddComponent<BillboardFx>();

            if (!_physical)
            {
                _orbBase = 0.26f * _scale;
                var orb = KomSpecialFx.CreateOrb(transform, "ShockOrb", color, hot, _orbBase, 2.3f);
                _orb = orb.transform;
                _orbMat = orb.GetComponent<MeshRenderer>().sharedMaterial;
            }

            _pillar = KomSpecialFx.CreateGlowQuad(transform, "ShockPillar", Color.Lerp(color, Color.white, 0.3f),
                0.26f, 2.4f, KomSpecialFx.GlowTexture, 0.7f).transform;
            _materials.Add(_pillar.GetComponent<MeshRenderer>().sharedMaterial);

            _groundRing = KomSpecialFx.CreateRingQuad(transform, "ShockGround", color, 0.8f, 0.6f, true).transform;
            _groundRing.localPosition = new Vector3(0f, 0.05f - transform.position.y, 0f);
            _groundRing.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _materials.Add(_groundRing.GetComponent<MeshRenderer>().sharedMaterial);

            var particleMat = KomSpecialFx.CreateParticleMaterial();
            _materials.Add(particleMat);
            var sparks = KomSpecialFx.CreateParticles(transform, "ShockSparks", color, false, 0f,
                0.22f, 0.55f, 0.035f, 0.14f, particleMat);
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.loop = false;
            main.duration = 0.2f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.2f, 10f);
            var emission = sparks.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });
            sparks.Play();

            var streakMat = KomSpecialFx.CreateParticleMaterial();
            _materials.Add(streakMat);
            var streaks = KomSpecialFx.CreateStreaks(transform, "ShockStreaks", color, 0f,
                0.28f, 0.5f, 0.03f, 0.07f, streakMat);
            streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var sMain = streaks.main;
            sMain.loop = false;
            sMain.duration = 0.2f;
            sMain.startSpeed = new ParticleSystem.MinMaxCurve(4f, 12f);
            var sEmission = streaks.emission;
            sEmission.rateOverTime = 0f;
            sEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 34) });
            streaks.Play();

            var emberMat = KomSpecialFx.CreateParticleMaterial();
            _materials.Add(emberMat);
            var embers = KomSpecialFx.CreateParticles(transform, "ShockEmbers", color, false, 0f,
                0.45f, 0.95f, 0.045f, 0.12f, emberMat);
            embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var emberMain = embers.main;
            emberMain.loop = false;
            emberMain.duration = 0.25f;
            emberMain.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 2.8f);
            emberMain.gravityModifier = 0.2f;
            var emberEmission = embers.emission;
            emberEmission.rateOverTime = 0f;
            emberEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });
            embers.Play();

            _light = KomSpecialFx.CreateLight(transform, color, 3.2f * _scale, 2.4f);
        }

        Transform AddQuad(string name, Color color, float size, Texture texture, float intensity)
        {
            var go = KomSpecialFx.CreateGlowQuad(transform, name, color, size * _scale, size * _scale, texture, intensity);
            _materials.Add(go.GetComponent<MeshRenderer>().sharedMaterial);
            return go.transform;
        }

        Transform AddRing(string name, Color color, float size, float intensity, bool soft = false)
        {
            var go = KomSpecialFx.CreateRingQuad(transform, name, color, size * _scale, intensity, soft);
            _materials.Add(go.GetComponent<MeshRenderer>().sharedMaterial);
            return go.transform;
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / Duration);
            float fade = 1f - k;
            float ease = 1f - Mathf.Pow(1f - k, 2.4f);

            if (_flash != null)
            {
                float s = 1.5f * _scale * (0.35f + ease * 1.5f);
                _flash.localScale = new Vector3(s, s, 1f);
            }
            if (_ringA != null)
            {
                float s = 0.62f * _scale * (0.35f + ease * 1.55f);
                _ringA.localScale = new Vector3(s, s, 1f);
            }
            if (_ringB != null)
            {
                float s = 0.44f * _scale * (0.3f + Mathf.Pow(Mathf.Clamp01(k * 1.25f), 0.8f) * 1.75f);
                _ringB.localScale = new Vector3(s, s, 1f);
            }
            if (_groundRing != null)
            {
                float s = 0.8f * _scale * (0.4f + ease * 1.55f);
                _groundRing.localScale = new Vector3(s, s, 1f);
            }
            if (_pillar != null)
            {
                float w = 0.26f * _scale * (1f - k * 0.6f);
                float h = 2.4f * _scale * (0.55f + ease * 0.8f);
                _pillar.localScale = new Vector3(w, h, 1f);
            }
            if (_orb != null)
            {
                float cs = _orbBase * (0.4f + ease * 1.9f) * (1f - k * k);
                _orb.localScale = Vector3.one * cs;
            }
            if (_orbMat != null) _orbMat.SetFloat("_Intensity", 2.4f * fade);

            foreach (var mat in _materials)
            {
                if (mat == null) continue;
                var c = mat.GetColor("_Color");
                float dim = 0.06f + fade * 0.94f;
                mat.SetColor("_Color", new Color(c.r * dim, c.g * dim, c.b * dim, c.a));
            }
            if (_light != null) _light.intensity = 2.4f * fade * fade;
            if (_t >= Duration) Destroy(gameObject);
        }

        void OnDestroy()
        {
            foreach (var mat in _materials)
                if (mat != null) Destroy(mat);
            if (_orbMat != null) Destroy(_orbMat);
        }
    }

    // =====================================================================================
    // AURA DE SUPER
    // =====================================================================================
    // Sale del CONTORNO de la piel: clona los SkinnedMeshRenderer del personaje y los
    // dibuja con KOM/AuraShell (halo de silueta + rim fresnel). No es un quad detras
    // del personaje: la luz nace del cuerpo.
    public sealed class SuperAuraFx : MonoBehaviour
    {
        Color _color;
        readonly List<GameObject> _shells = new List<GameObject>();
        readonly List<Material> _mats = new List<Material>();
        ParticleSystem _particles;
        Light _light;
        Material _particleMat;
        bool _charged;
        float _level;
        float _intensityScale = 1f;

        public void Init(Color color)
        {
            _color = color;
            Color hot = Color.Lerp(color, Color.white, 0.42f);
            BuildShells(hot);

            _particleMat = KomSpecialFx.CreateParticleMaterial();
            _particles = KomSpecialFx.CreateParticles(transform, "AuraParticles", color, true, 14f,
                0.5f, 1.15f, 0.025f, 0.075f, _particleMat);
            var shape = _particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.62f, 1.35f, 0.45f);
            shape.position = new Vector3(0f, 0.85f, 0f);
            var main = _particles.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
            KomSpecialFx.SetVelocity(_particles, new Vector3(0f, 0.42f, 0f), 0f, 0f, 0f, 0f);

            _light = KomSpecialFx.CreateLight(transform, color, 2.8f, 0.55f);
            _light.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            _charged = true;
            SetCharged(false);
        }

        void BuildShells(Color hot)
        {
            // El aura vive como hijo del Fighter: hay que buscar los SkinnedMeshRenderer
            // del PERSONAJE (hermanos/ancestros), no los descendientes de este objeto.
            var root = transform.parent != null ? transform.parent : transform;
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers == null || renderers.Length == 0) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                var src = renderers[i];
                if (src == null || src.sharedMesh == null) continue;
                if (src.gameObject.name.EndsWith("_AuraShell", System.StringComparison.Ordinal)) continue;

                var mat = KomSpecialFx.CreateAuraMaterial(_color, hot, 0.9f);
                if (mat == null) return;
                _mats.Add(mat);

                var go = new GameObject(src.name + "_AuraShell");
                go.transform.SetParent(src.transform.parent, false);
                go.transform.localPosition = src.transform.localPosition;
                go.transform.localRotation = src.transform.localRotation;
                go.transform.localScale = src.transform.localScale;

                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = src.sharedMesh;
                smr.bones = src.bones;
                smr.rootBone = src.rootBone;
                smr.localBounds = src.localBounds;
                smr.updateWhenOffscreen = true;
                smr.forceMatrixRecalculationPerRender = true;
                smr.skinnedMotionVectors = false;
                smr.shadowCastingMode = ShadowCastingMode.Off;
                smr.receiveShadows = false;
                smr.allowOcclusionWhenDynamic = false;

                int subMeshes = Mathf.Max(1, src.sharedMesh.subMeshCount);
                var mats = new Material[subMeshes];
                for (int m = 0; m < subMeshes; m++) mats[m] = mat;
                smr.sharedMaterials = mats;

                int shapes = src.sharedMesh.blendShapeCount;
                for (int b = 0; b < shapes; b++) smr.SetBlendShapeWeight(b, src.GetBlendShapeWeight(b));

                go.SetActive(false);
                _shells.Add(go);
            }
        }

        // Los especiales con prop (guitarra/acordeon) bajan el aura: si no, el
        // contorno en HDR se come el prop y la animacion.
        public void SetIntensityScale(float scale)
        {
            _intensityScale = Mathf.Clamp(scale, 0.1f, 1.5f);
        }

        public void SetCharged(bool charged)
        {
            if (_charged == charged) return;
            _charged = charged;
            if (_particles != null)
            {
                var emission = _particles.emission;
                emission.enabled = charged;
                if (charged) _particles.Emit(30);
            }
            if (_light != null) _light.enabled = charged;
            if (charged)
            {
                for (int i = 0; i < _shells.Count; i++)
                    if (_shells[i] != null) _shells[i].SetActive(true);
                KomSpecialFx.AuraRing(transform.position, _color, 1.7f);
            }
        }

        void Update()
        {
            float target = _charged ? 1f : 0f;
            // Sube rapido (0.22 s) y baja suave (0.5 s): el aura "prende".
            float speed = _charged ? 4.5f : 2.0f;
            _level = Mathf.MoveTowards(_level, target, Time.deltaTime * speed);

            for (int i = 0; i < _mats.Count; i++)
            {
                var mat = _mats[i];
                if (mat == null) continue;
                float pulse = 0.85f + Mathf.Sin(Time.time * 5.4f) * 0.12f + Mathf.Sin(Time.time * 11.7f) * 0.05f;
                mat.SetFloat("_Intensity", 2.9f * _intensityScale * _level * pulse);
                // Sin inflado: ver KomSpecialFx.CreateAuraMaterial. La intensidad ya la
                // llevan _Intensity y el pulso; empujar los vertices hacia fuera deforma
                // visualmente al personaje.
                mat.SetFloat("_Expand", 0f);
            }
            if (_level <= 0.001f && !_charged)
            {
                for (int i = 0; i < _shells.Count; i++)
                    if (_shells[i] != null && _shells[i].activeSelf) _shells[i].SetActive(false);
            }
            if (_light != null)
                _light.intensity = (0.4f + Mathf.Sin(Time.time * 5.4f) * 0.12f) * _level * _intensityScale;
            if (_particles != null)
            {
                var emission = _particles.emission;
                emission.rateOverTime = 16f * _level + Mathf.Sin(Time.time * 5f) * 3f * _level;
            }
        }

        void OnDestroy()
        {
            if (_particleMat != null) Destroy(_particleMat);
            foreach (var mat in _mats)
                if (mat != null) Destroy(mat);
            foreach (var shell in _shells)
                if (shell != null) Destroy(shell);
        }
    }
}
