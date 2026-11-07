using UnityEngine;
using UnityEngine.Rendering;

namespace KOM
{
    // Anclaje de props rigidos (acordeon, guitarra) a las manos del luchador.
    //
    // Reglas medidas en este proyecto:
    //  - Los huesos del rig llevan escala 100 (unidades de Blender). Un prop hijo del
    //    hueso sin compensar sale 100x mas grande que el personaje.
    //  - El origen de DEF-hand.* es la MUNECA, no la palma: el prop se ancla en el
    //    punto de agarre real (mezcla muneca -> indice), no en el hueso.
    //  - La guitarra NO se cuelga con una rotacion local fija: se empuna por el mastil y
    //    la orientacion la resuelve KomClubGrip en cada frame (ver mas abajo).
    public static class KomPropGrip
    {
        // Punto de agarre de la guitarra a lo largo de su eje largo (0 = cuerpo, 1 = pala),
        // medido con KomSpecialAudit.PropShape: el cuerpo ancho ocupa 0.02-0.35, el mastil
        // 0.56-0.85 y la pala 0.90-1.00.
        //
        // El pivote del FBX esta en 0.90 (casi la punta de la pala). Empunada ahi la guitarra
        // es un bate de 1.16 m con TODO el peso en el extremo y el mango por detras de la
        // muneca: al girar la mano el cuerpo barria en cualquier direccion y el arma no se
        // leia como espada (medido: 100-135 grados entre el cuerpo y la linea pecho->palma
        // en pleno combo). A 0.82 la mano agarra el mastil, la pala queda 0.20 m por detras
        // y el cuerpo pasa a ser la hoja: 0.96 m por delante del puno.
        public const float GuitarGripNorm = 0.82f;

        // Rotacion de semilla (espacio de DEF-hand.L): solo vale para el primer frame y para
        // los bancos de prueba que no corren el solver. Calibrada en el frame de golpe del
        // clip Two Hand Club Combo (t=0.525):
        //   KomPropLab.CalibrateGuitarAim("alex_llora","Combo",0.525f,0f,-0.39f,0.92f)
        public static readonly Vector3 GuitarLocalEuler = new Vector3(62.84f, 277.13f, 249.37f);
        public static readonly Vector3 GuitarLocalOffset = Vector3.zero;
        public static readonly float GuitarScale = 0.92f;

        // El acordeon se sostiene entre las dos manos: ancho del prop alineado con el
        // eje mano-mano, teclado hacia arriba y fuelle mirando al frente.
        public static readonly float AccordionScale = 1.0f;

        public static GameObject Spawn(string resourceName, Transform parent)
        {
            if (string.IsNullOrEmpty(resourceName)) return null;
            var prefab = Resources.Load<GameObject>(resourceName);
            if (prefab == null)
            {
                Debug.LogError("[KOM] Falta el prop en Resources: " + resourceName);
                return null;
            }
            var go = Object.Instantiate(prefab, parent != null ? parent : null);
            go.name = prefab.name;
            // La escala de autoria del prefab NO es 1 (los FBX vienen en unidades de
            // Blender: acordeon 100, guitarra 39.68). Se guarda para no perderla al
            // reescalar el prop para el especial.
            var tag = go.AddComponent<KomPropTag>();
            tag.BaseScale = go.transform.localScale;
            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
                KomFx.SafeDestroy(collider);
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            return go;
        }

        public static Vector3 BaseScale(GameObject prop)
        {
            if (prop == null) return Vector3.one;
            var tag = prop.GetComponent<KomPropTag>();
            return tag != null ? tag.BaseScale : prop.transform.localScale;
        }

        // Punto de agarre real dentro de la mano (muneca -> nudillo, 60 %).
        public static Vector3 PalmLocal(Transform hand, Transform finger)
        {
            if (hand == null) return Vector3.zero;
            if (finger == null) return Vector3.zero;
            Vector3 world = Vector3.Lerp(hand.position, finger.position, 0.6f);
            return hand.InverseTransformPoint(world);
        }

        public static Vector3 PalmWorld(Transform hand, Transform finger)
        {
            if (hand == null) return Vector3.zero;
            if (finger == null) return hand.position;
            return Vector3.Lerp(hand.position, finger.position, 0.6f);
        }

        // Cuelga el prop del hueso compensando la escala del rig y anclandolo en la palma.
        // gripAnchorLocal (opcional) es el punto del PROP que debe quedar en la palma, en
        // unidades del prop: sirve para empunar por el centro de una pieza larga (el
        // microfono) en vez de por su pivote. Se resta ya rotado al espacio de la mano.
        public static void AttachToHand(GameObject prop, Transform hand, Transform finger,
            Quaternion localRotation, Vector3 localOffset, float scale, Vector3 gripAnchorLocal = default)
        {
            if (prop == null || hand == null) return;
            // La escala de autoria se fija UNA vez. Sin esto, un segundo AttachToHand lee el
            // localScale ya compensado (0.365) como si fuera el de autoria y encoge el prop
            // 100x por llamada: en el banco de pruebas desaparecia a la tercera fase.
            if (prop.GetComponent<KomPropTag>() == null)
            {
                var tag = prop.AddComponent<KomPropTag>();
                tag.BaseScale = prop.transform.localScale;
            }
            prop.transform.SetParent(hand, false);
            prop.transform.localRotation = localRotation;
            Vector3 palm = PalmLocal(hand, finger);
            float handScale = hand.lossyScale.x;
            float k = handScale > 0.0001f ? 1f / handScale : 1f;
            // Escala de autoria del prop x compensacion de la escala del rig (100).
            prop.transform.localScale = BaseScale(prop) * (k * scale);
            Vector3 anchor = Vector3.Scale(gripAnchorLocal, prop.transform.localScale);
            prop.transform.localPosition = palm + localOffset - localRotation * anchor;
        }

        // Punto del prop que debe coincidir con la palma, a partir de su posicion a lo largo
        // del eje largo (0 = punta del cuerpo, 1 = punta de la pala). Se mide sobre los bounds
        // del renderer para no depender de numeros escritos a mano.
        public static Vector3 GripAnchor(GameObject prop, float norm)
        {
            if (prop == null) return Vector3.zero;
            var renderer = prop.GetComponentInChildren<Renderer>();
            if (renderer == null) return Vector3.zero;
            var b = renderer.localBounds;
            return new Vector3(b.center.x, Mathf.Lerp(b.min.y, b.max.y, Mathf.Clamp01(norm)), b.center.z);
        }

        // Guitarra como ESPADA: se empuna por el mastil y la orientacion la resuelve
        // KomClubGrip. `chest` es el hueso del pecho (DEF-spine.003) y `root` el transform del
        // luchador (de ahi sale la cota del suelo para no atravesarla con el cuerpo).
        public static void AttachGuitar(GameObject prop, Transform hand, Transform finger,
            Transform chest = null, Transform root = null)
        {
            if (prop == null || hand == null) return;
            Vector3 anchor = GripAnchor(prop, GuitarGripNorm);
            AttachToHand(prop, hand, finger, Quaternion.Euler(GuitarLocalEuler), GuitarLocalOffset,
                GuitarScale, anchor);
            var grip = prop.GetComponent<KomClubGrip>();
            if (grip == null) grip = prop.AddComponent<KomClubGrip>();
            grip.Bind(hand, finger, chest, root, anchor);
        }

        // Microfono: se empuna por el conector/yugo (el pivote del FBX ya esta ahi, medido
        // con tools/blender/extract_mic.py). Se sostiene en la mano DERECHA, que es la que
        // sube a la boca al cantar; la IZQUIERDA queda libre para lanzar la onda.
        //
        // El eje largo del prop es su +Z local y mide 0.0044 (0.44 m de mundo). Para que la
        // rejilla quede en la boca se empuna por el CENTRO (anchor 0.0022) y se orienta al
        // punto medio entre mano y boca medido en el frame de canto (t=0.20 del clip):
        //   dir mano->boca = (-0.562, 0.383, -0.733), distancia 0.271 m
        //   -> MicLocalEuler = (308.19, 18.46, 12.15)
        public static readonly Vector3 MicLocalEuler = new Vector3(308.19f, 18.46f, 12.15f);
        public static readonly Vector3 MicLocalOffset = Vector3.zero;
        public static readonly Vector3 MicGripAnchor = new Vector3(0f, 0f, 0.0022f);
        public static readonly float MicScale = 1.0f;

        public static void AttachMic(GameObject prop, Transform hand, Transform finger)
        {
            AttachToHand(prop, hand, finger, Quaternion.Euler(MicLocalEuler), MicLocalOffset,
                MicScale, MicGripAnchor);
        }

        // Acordeon entre las manos: el eje X del prop (fuelle) va del torso hacia la
        // mano derecha, el teclado (Y) hacia arriba y la cara (Z) al frente.
        public static void PlaceBetweenHands(GameObject prop, Vector3 rightPalm, Vector3 leftPalm,
            Vector3 forward, float scale, float yawOffset = 0f)
        {
            if (prop == null) return;
            prop.transform.SetParent(null, true);
            Vector3 mid = (rightPalm + leftPalm) * 0.5f;
            Vector3 span = leftPalm - rightPalm;
            if (span.sqrMagnitude < 0.0004f) span = Vector3.right * 0.4f;
            Vector3 x = span.normalized;
            Vector3 fwd = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            Vector3 z = (fwd - x * Vector3.Dot(fwd, x)).normalized;
            if (z.sqrMagnitude < 0.0001f) z = Vector3.forward;
            Vector3 y = Vector3.Cross(z, x).normalized;
            var rot = Quaternion.LookRotation(z, y) * Quaternion.Euler(0f, yawOffset, 0f);
            prop.transform.SetPositionAndRotation(mid, rot);
            prop.transform.localScale = BaseScale(prop) * scale;
        }

        // Centro del cuerpo del prop en mundo (para impactos y estelas): bounds del
        // renderer llevados al espacio del prop.
        public static Vector3 BodyCenter(GameObject prop)
        {
            if (prop == null) return Vector3.zero;
            var renderer = prop.GetComponentInChildren<Renderer>();
            if (renderer == null) return prop.transform.position;
            return prop.transform.TransformPoint(renderer.localBounds.center);
        }

        // Borde de golpe del cuerpo (el extremo que impacta). El centro del cuerpo se
        // queda corto contra un rival alto; el borde es el punto real del golpe.
        public static Vector3 BodyEdge(GameObject prop)
        {
            if (prop == null) return Vector3.zero;
            var renderer = prop.GetComponentInChildren<Renderer>();
            if (renderer == null) return prop.transform.position;
            var b = renderer.localBounds;
            var local = Vector3.Lerp(b.center, new Vector3(b.center.x, b.min.y, b.center.z), 0.72f);
            return prop.transform.TransformPoint(local);
        }
    }

    // =====================================================================================
    // AGARRE DE ESPADA (guitarra de Alex Llora)
    // =====================================================================================
    // Una rotacion local fija no sirve para este clip: Two Hand Club Combo gira el cuerpo
    // 173 grados y mueve la muneca por todo el arco, asi que la guitarra (que es una pieza
    // rigida de 1.16 m) terminaba apuntando hacia atras o cruzada sobre el pecho en los
    // frames intermedios. Medido con KomSpecialAudit.GripReport antes del arreglo: 100-135
    // grados entre el eje del cuerpo y la linea pecho->palma, cuando un arma empunada debe
    // dar ~0.
    //
    // El solver hace tres cosas por frame, sin tocar la animacion:
    //   1. AIM: el cuerpo de la guitarra apunta en la direccion pecho -> palma. Es la
    //      extension natural de los brazos, y por construccion NUNCA se mete en el torso.
    //   2. GIRO: la cara plana del cuerpo mira a la CAMARA. Con la tangente de la mano el
    //      instrumento se ponia de canto justo cuando va en alto -el punto de vista mas
    //      frecuente de la pelea- y se leia como un hacha.
    //   3. SUELO: si el cuerpo fuese a atravesar el piso se levanta el aim lo justo. El
    //      clip baja las manos a 0.83 m y la guitarra mide 0.96 m hasta el borde.
    public sealed class KomClubGrip : MonoBehaviour
    {
        // Constantes de seguimiento. El aim es rapido (55 ms) para que no se separe del
        // brazo; el giro sobre el eje del arma es mas lento para que no vibre.
        const float AimTime = 0.055f;
        const float FaceTime = 0.09f;
        const float FloorMargin = 0.12f;

        Transform _hand;
        Transform _finger;
        Transform _chest;
        Transform _root;
        Vector3 _anchor;
        Vector3 _aim = Vector3.forward;
        Vector3 _face = Vector3.up;
        Vector3 _palmPrev;
        float _bodyLength = 1f;
        bool _seeded;
        static Camera _view;

        public Vector3 Aim => _aim;
        public Vector3 Face => _face;

        public void Bind(Transform hand, Transform finger, Transform chest, Transform root, Vector3 anchor)
        {
            _hand = hand;
            _finger = finger;
            _chest = chest;
            _root = root;
            _anchor = anchor;
            var renderer = GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                var b = renderer.localBounds;
                float worldScale = Mathf.Max(0.0001f, renderer.transform.lossyScale.y);
                // Distancia del punto de agarre al borde del cuerpo, en metros de mundo.
                _bodyLength = Mathf.Max(0.25f, (anchor.y - b.min.y) * worldScale);
            }
            _seeded = false;
            Snap();
        }

        // Resuelve la pose sin suavizar. La usan el primer frame y los bancos de prueba
        // (KomSpecialAudit.PropEdgeProfile / GripReport), que miden una pose concreta.
        public void Snap()
        {
            if (_hand == null) return;
            Vector3 palm = KomPropGrip.PalmWorld(_hand, _finger);
            Solve(palm, out var aim, out var face);
            _aim = aim;
            _face = face;
            _palmPrev = palm;
            _seeded = true;
            Apply(palm);
        }

        void LateUpdate()
        {
            // La mano puede morir con el especial: sin esta guarda el prop se iria al origen
            // durante el frame en que se destruye.
            if (_hand == null || _finger == null) return;
            if (!_seeded) { Snap(); return; }
            float dt = Time.deltaTime;
            if (dt <= 0.00001f) return;
            Vector3 palm = KomPropGrip.PalmWorld(_hand, _finger);
            Solve(palm, out var aimWanted, out var faceWanted);

            float ka = 1f - Mathf.Exp(-dt / AimTime);
            _aim = Vector3.Slerp(_aim, aimWanted, ka).normalized;
            float kf = 1f - Mathf.Exp(-dt / FaceTime);
            Vector3 face = Vector3.Slerp(_face, faceWanted, kf);
            face -= _aim * Vector3.Dot(face, _aim);
            if (face.sqrMagnitude < 1e-6f) face = Vector3.Cross(_aim, Vector3.right);
            if (face.sqrMagnitude < 1e-6f) face = Vector3.Cross(_aim, Vector3.forward);
            _face = face.normalized;

            Apply(palm);
            _palmPrev = palm;
        }

        void Apply(Vector3 palm)
        {
            // LookRotation(+Z = filo, +Y = -aim): la guitarra tiene el cuerpo en su -Y, asi
            // que -Y debe caer sobre el aim para que la hoja salga hacia delante.
            var rot = Quaternion.LookRotation(_face, -_aim);
            Vector3 anchorWorld = Vector3.Scale(_anchor, transform.lossyScale);
            transform.SetPositionAndRotation(palm - rot * anchorWorld, rot);
        }

        void Solve(Vector3 palm, out Vector3 aim, out Vector3 face)
        {
            aim = _chest != null ? palm - _chest.position : _aim;
            if (aim.sqrMagnitude < 0.0025f) aim = _aim;
            aim = aim.sqrMagnitude > 1e-6f ? aim.normalized : Vector3.forward;

            // Suelo: el cuerpo no puede atravesar el piso. Se sube el aim lo justo para que
            // el borde del cuerpo quede FloorMargin por encima de los pies.
            float feet = _root != null ? _root.position.y : 0f;
            float minY = (feet + FloorMargin - palm.y) / Mathf.Max(0.3f, _bodyLength);
            if (aim.y < minY)
            {
                Vector3 flat = new Vector3(aim.x, 0f, aim.z);
                if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
                flat.Normalize();
                float up = Mathf.Clamp(minY, -1f, 1f);
                float out2 = Mathf.Sqrt(Mathf.Max(0f, 1f - up * up));
                aim = (flat * out2 + Vector3.up * up).normalized;
            }

            // GIRO: la cara plana del cuerpo mira a la camara. Con la tangente de la mano
            // el instrumento se ponia de canto justo cuando va en alto (el punto de vista
            // mas frecuente de la pelea) y se leia como un hacha, no como una guitarra.
            // La camara de combate mira casi fija hacia +Z, asi que basta con proyectar
            // "hacia la camara" sobre el plano perpendicular al arma.
            if (_view == null) _view = Camera.main;
            Vector3 toView = _view != null ? -_view.transform.forward : Vector3.back;
            face = toView - aim * Vector3.Dot(toView, aim);
            // Arma apuntando a la camara: el giro no esta definido, se conserva el anterior.
            if (face.sqrMagnitude < 0.0004f) face = _face - aim * Vector3.Dot(_face, aim);
            if (face.sqrMagnitude < 1e-6f) face = Vector3.Cross(aim, Vector3.right);
            if (face.sqrMagnitude < 1e-6f) face = Vector3.Cross(aim, Vector3.forward);
            face.Normalize();
        }
    }
}

namespace KOM
{
    // Guarda la escala con la que se autoro el prefab del prop (no es 1: los FBX
    // vienen en unidades de Blender). Sin esto, reescalar el prop para el especial
    // lo deja 40-100x mas pequeno y desaparece.
    public sealed class KomPropTag : MonoBehaviour
    {
        public Vector3 BaseScale = Vector3.one;
    }
}
