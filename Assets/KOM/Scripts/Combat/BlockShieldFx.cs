using System.Collections.Generic;
using UnityEngine;

namespace KOM
{
    /// <summary>
    /// Escudo de bloqueo como AURA pegada al cuerpo.
    ///
    /// No es un cartel delante de la camara: se dibuja sobre una COPIA del
    /// SkinnedMeshRenderer del luchador (la misma tecnica que el aura de super), asi que
    /// sigue la pose y rodea el cuerpo. Lo que lo convierte en escudo es la directriz del
    /// shader: el filo fresnel se refuerza en las caras que miran al RIVAL, no a la camara.
    ///
    /// Es tenue a proposito: un escudo opaco tapa al luchador y se lee como un cartel.
    /// </summary>
    public sealed class BlockShieldFx : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int HotId = Shader.PropertyToID("_HotColor");
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int GuardId = Shader.PropertyToID("_Guard");
        static readonly int OpenId = Shader.PropertyToID("_Open");
        static readonly int SeedId = Shader.PropertyToID("_Seed");
        static readonly int ForwardId = Shader.PropertyToID("_Forward");
        static readonly int FeetYId = Shader.PropertyToID("_FeetY");
        static readonly int BandCenterId = Shader.PropertyToID("_BandCenter");
        static readonly int BandHeightId = Shader.PropertyToID("_BandHeight");

        Fighter _owner;
        readonly List<SkinnedMeshRenderer> _shells = new List<SkinnedMeshRenderer>();
        readonly List<Material> _materials = new List<Material>();
        float _seed;

        float _alpha;
        float _target;
        float _flash;
        float _open;
        float _guard = 1f;
        float _shatter;

        public static BlockShieldFx Attach(Fighter owner, Color accent)
        {
            if (owner == null) return null;
            var go = new GameObject("BlockShield");
            go.transform.SetParent(owner.transform, false);
            var fx = go.AddComponent<BlockShieldFx>();
            fx.Build(owner, accent);
            return fx;
        }

        void Build(Fighter owner, Color accent)
        {
            _owner = owner;
            _seed = Random.value * 10f;

            var shader = Shader.Find("KOM/BlockShield");
            if (shader == null)
            {
                Debug.LogWarning("[KOM] Falta el shader KOM/BlockShield: el bloqueo no tendra escudo.");
                return;
            }

            // Azul frio con un tinte del personaje: magia, no el color del poder.
            Color magic = new Color(0.42f, 0.80f, 1f);
            Color baseColor = Color.Lerp(magic, accent, 0.10f);
            Color hot = Color.Lerp(baseColor, Color.white, 0.75f);

            // Se copian las mallas del PERSONAJE (hermanos/ancestros), no las de este objeto.
            var root = owner.transform;
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers == null) return;

            for (int i = 0; i < renderers.Length; i++)
            {
                var src = renderers[i];
                if (src == null || src.sharedMesh == null) continue;
                if (src.gameObject.name.EndsWith("_AuraShell", System.StringComparison.Ordinal)) continue;

                var mat = new Material(shader);
                mat.name = "BlockShield_" + owner.CharacterId;
                mat.SetColor(ColorId, baseColor);
                mat.SetColor(HotId, hot);
                mat.SetFloat(AlphaId, 0f);
                mat.SetFloat(FlashId, 0f);
                mat.SetFloat(GuardId, 1f);
                mat.SetFloat(OpenId, 0f);
                mat.SetFloat(SeedId, _seed);
                mat.SetVector(ForwardId, new Vector4(1f, 0f, 0f, 0f));
                mat.SetFloat(FeetYId, 0f);
                mat.SetFloat(BandCenterId, 1.05f);
                mat.SetFloat(BandHeightId, 0.95f);
                _materials.Add(mat);

                var go = new GameObject(src.name + "_BlockShell");
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
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.receiveShadows = false;
                smr.allowOcclusionWhenDynamic = false;

                int subMeshes = Mathf.Max(1, src.sharedMesh.subMeshCount);
                var mats = new Material[subMeshes];
                for (int m = 0; m < subMeshes; m++) mats[m] = mat;
                smr.sharedMaterials = mats;

                int shapes = src.sharedMesh.blendShapeCount;
                for (int b = 0; b < shapes; b++) smr.SetBlendShapeWeight(b, src.GetBlendShapeWeight(b));

                smr.enabled = false;
                _shells.Add(smr);
            }
        }

        public void SetVisible(bool on, float dt)
        {
            _target = on ? 1f : 0f;
            if (_shatter > 0f)
            {
                _shatter = Mathf.Max(0f, _shatter - dt * 3.2f);
                _target = 0f;
            }
            float rate = _target > _alpha
                ? 1f / Mathf.Max(0.01f, CombatTuning.ShieldFadeIn)
                : 1f / Mathf.Max(0.01f, CombatTuning.ShieldFadeOut);
            _alpha = Mathf.MoveTowards(_alpha, _target, rate * dt);
            // La cascara se "abre" desde el cuerpo al aparecer.
            _open = Mathf.MoveTowards(_open, _alpha, dt * 6.5f);
        }

        public void SetGuard(float fraction)
        {
            _guard = Mathf.Clamp01(fraction);
        }

        public void Flash(float strength)
        {
            _flash = Mathf.Clamp01(_flash + Mathf.Clamp01(strength));
        }

        public void Shatter()
        {
            _flash = 1f;
            _shatter = 1f;
            _open = 0f;
            _target = 0f;
        }

        public void Reset()
        {
            _alpha = 0f;
            _target = 0f;
            _flash = 0f;
            _open = 0f;
            _guard = 1f;
            _shatter = 0f;
            for (int i = 0; i < _shells.Count; i++)
                if (_shells[i] != null) _shells[i].enabled = false;
        }

        void LateUpdate()
        {
            if (_owner == null || _shells.Count == 0) return;

            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 4.5f);
            bool show = _alpha > 0.004f;
            for (int i = 0; i < _shells.Count; i++)
            {
                var shell = _shells[i];
                if (shell != null && shell.enabled != show) shell.enabled = show;
            }
            if (!show) return;

            // La directriz es la del RIVAL: el escudo se enciende del lado del golpe.
            float facing = _owner.Facing >= 0f ? 1f : -1f;
            var forward = new Vector4(facing, 0f, 0f, 0f);

            for (int i = 0; i < _materials.Count; i++)
            {
                var mat = _materials[i];
                if (mat == null) continue;
                mat.SetFloat(AlphaId, _alpha);
                mat.SetFloat(OpenId, _open);
                mat.SetFloat(FlashId, _flash);
                mat.SetFloat(GuardId, _guard);
                mat.SetFloat(SeedId, _seed);
                mat.SetVector(ForwardId, forward);
                // Los pies del luchador son el origen de la faja del torso.
                mat.SetFloat(FeetYId, _owner.transform.position.y);
            }
        }

        void OnDestroy()
        {
            for (int i = 0; i < _shells.Count; i++)
                if (_shells[i] != null) Destroy(_shells[i].gameObject);
            for (int i = 0; i < _materials.Count; i++)
                if (_materials[i] != null) Destroy(_materials[i]);
        }
    }
}
