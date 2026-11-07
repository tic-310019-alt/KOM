using UnityEngine;

namespace KOM
{
    // Los dos recolectables de arena. Son comida y bebida: el chiste mexicano del
    // "desayuno de campeones" convertido en mecanica.
    public enum PickupType
    {
        // Pan dulce: recupera vida.
        Concha,
        // Refresco de vidrio: recarga la barra de estamina (la misma que gasta bloquear).
        Soda
    }

    // Recolectable de arena: cae del cielo, bota una vez, flota con un aura blanca y se
    // consume al pasar por encima.
    //
    // Todo el objeto va en BLANCO Y NEGRO como el escenario (shader KOM/PropBW); lo unico
    // que lo separa del fondo es el aura: fresnel blanco en el material + halo aditivo
    // que mira a camara + anillo pulsante en el suelo. Sin esa capa, un prop gris sobre un
    // escenario gris es invisible.
    public sealed class CombatPickup : MonoBehaviour
    {
        const float FallStart = 3.6f;
        const float FallGravity = 18f;
        // Rebote corto: se lee "ha caido aqui" sin que el objeto se arrastre.
        const float BounceFactor = 0.34f;
        // Altura de reposo. Estaba en 0.78 y el prop flotaba a la altura del zocalo del
        // telon: en La Madrugadora (telon a metro y medio del plano de pelea) parecia
        // apoyado en la pared y, por tanto, fuera del ring. A 0.45 se lee sobre el suelo.
        const float RestHeight = 0.45f;
        // Z de la linea de combate: la misma que usan los luchadores al colocarse.
        public const float LaneZ = 0.2f;
        const float BobAmplitude = 0.075f;
        const float BobSpeed = 2.3f;
        const float SpinSpeed = 52f;
        // SIN halo aditivo. Un glow blanco detras de un objeto B/N lo lava y lo convierte
        // en un fantasma: se ve el resplandor, no el objeto. La legibilidad la dan el
        // MATERIAL (banda de valor con contraste interno) y el ANILLO del suelo, que marca
        // el sitio sin tocar al prop.

        enum State { Falling, Ready, Gone }

        public PickupType Type { get; private set; }
        public bool Available => _state == State.Ready;

        CombatController _combat;
        State _state = State.Falling;
        float _height = FallStart;
        float _velocity;
        float _age;
        float _phase;
        Transform _body;
        Transform _visual;
        // Los dos FBX salen de Blender con el eje Z arriba. El refresco es vertical: se
        // endereza y gira sobre el eje vertical del MUNDO. La concha es un disco plano:
        // se tumba casi horizontal con la corteza mirando a camara y gira sobre su propia
        // normal, que es lo que la hace legible como concha y no como un plato.
        Quaternion _baseRotation = Quaternion.identity;
        bool _spinAroundWorldUp;

        public static CombatPickup Create(PickupType type, Vector3 ground, CombatController combat)
        {
            string resource = type == PickupType.Concha ? "Props/pickup_concha" : "Props/pickup_soda";
            var prefab = Resources.Load<GameObject>(resource);
            if (prefab == null)
            {
                Debug.LogError("[KOM] Falta Resources/" + resource + ".prefab");
                return null;
            }
            var root = new GameObject("Pickup_" + type);
            root.transform.position = ground;
            var pickup = root.AddComponent<CombatPickup>();
            pickup.Type = type;
            pickup._combat = combat;
            pickup.Build(prefab);
            return pickup;
        }

        void Build(GameObject prefab)
        {
            // Fase aleatoria por instancia: dos recolectables nunca pulsan a la vez.
            _phase = Random.value * 10f;

            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            _body.localPosition = new Vector3(0f, FallStart, 0f);

            var visual = Instantiate(prefab, _body);
            visual.name = "Visual";
            _visual = visual.transform;
            _visual.localPosition = Vector3.zero;
            // Blender exporta con Z arriba: el refresco se endereza y gira sobre el eje
            // vertical; la concha se inclina para leerse en perspectiva y gira sobre su
            // propia normal.
            if (Type == PickupType.Soda)
            {
                _baseRotation = Quaternion.Euler(-90f, 0f, 0f);
                _spinAroundWorldUp = true;
            }
            else
            {
                // La concha es un disco plano con la corteza hacia +Z. Con -35° la cara
                // miraba ARRIBA Y HACIA ATRAS: la camara veia el reverso liso y parecia
                // un plato. -120° la tumba 30° por debajo de la horizontal y gira la
                // corteza hacia la camara, que es la lectura de la referencia.
                _baseRotation = Quaternion.Euler(-120f, 0f, 0f);
                _spinAroundWorldUp = false;
            }
            _visual.localRotation = _baseRotation;
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
                KomFx.SafeDestroy(collider);

            // Sin anillo en el suelo: el halo competia con la senal del escenario y hacia
            // que el recolectable pareciera flotar sobre una diana. El objeto se lee por
            // su material y por el impacto de la caida.
        }

        void Update()
        {
            if (_state == State.Gone) return;
            float dt = Time.deltaTime;
            _age += dt;

            if (_state == State.Falling) UpdateFall(dt);
            else UpdateIdle(dt);
        }

        void UpdateFall(float dt)
        {
            _velocity -= FallGravity * dt;
            _height += _velocity * dt;
            if (_height > RestHeight)
            {
                _body.localPosition = new Vector3(0f, _height, 0f);
                return;
            }

            _height = RestHeight;
            if (Mathf.Abs(_velocity) > 1.6f)
            {
                _velocity = -_velocity * BounceFactor;
                // El polvo del aterrizaje: anuncia el punto exacto donde ha caido.
                KomFx.ImpactRing(transform.position + Vector3.up * 0.05f, Color.white, 0.60f);
                return;
            }

            _velocity = 0f;
            _state = State.Ready;
            _body.localPosition = new Vector3(0f, _height, 0f);
            KomFx.ImpactRing(transform.position + Vector3.up * 0.05f, Color.white, 0.85f);
        }

        void UpdateIdle(float dt)
        {
            float bob = Mathf.Sin(_age * BobSpeed + _phase) * BobAmplitude;
            _body.localPosition = new Vector3(0f, _height + bob, 0f);
            if (_visual != null)
            {
                float spin = _age * SpinSpeed;
                _visual.localRotation = _spinAroundWorldUp
                    // Gira sobre la vertical del mundo: el refresco se queda de pie.
                    ? Quaternion.AngleAxis(spin, Vector3.up) * _baseRotation
                    // Gira sobre su normal: la concha da vueltas sobre si misma.
                    : _baseRotation * Quaternion.AngleAxis(spin, Vector3.forward);
            }

            if (_age > CombatTuning.PickupLifetime)
            {
                Expire();
                return;
            }
            TryCollect();
        }

        void TryCollect()
        {
            if (_combat == null) return;
            if (TryCollectBy(_combat.Player)) return;
            TryCollectBy(_combat.Enemy);
        }

        bool TryCollectBy(Fighter fighter)
        {
            if (fighter == null || fighter.IsKO || fighter.IsFrozen) return false;
            if (!fighter.IsGrounded) return false;
            float dx = Mathf.Abs(fighter.transform.position.x - transform.position.x);
            float dz = Mathf.Abs(fighter.transform.position.z - transform.position.z);
            if (dx > CombatTuning.PickupCollectRadius + fighter.CurrentHurtboxRadius) return false;
            if (dz > 0.95f) return false;
            Collect(fighter);
            return true;
        }

        void Collect(Fighter fighter)
        {
            _state = State.Gone;
            var point = transform.position + Vector3.up * RestHeight;
            KomFx.ImpactRing(point, Color.white, 1.15f);
            KomFx.HitBurst(point, Color.white);
            _combat?.OnPickupCollected(fighter, Type, this);
            Destroy(gameObject);
        }

        void Expire()
        {
            _state = State.Gone;
            KomFx.ImpactRing(transform.position + Vector3.up * RestHeight,
                new Color(0.65f, 0.65f, 0.65f), 0.7f);
            _combat?.OnPickupExpired(this);
            Destroy(gameObject);
        }
    }
}
