namespace KOM
{
    public static class CombatTuning
    {
        // ---------------------------------------------------------------------
        // Reacciones al golpe (hitstun)
        // ---------------------------------------------------------------------
        // El hitstun NO es un numero suelto: es la ventana en la que el rival esta
        // indefenso. Cada ataque declara el suyo en AttackSpec.hitStun (frame data
        // por golpe, como en un juego de pelea real). Estos dos son el respaldo
        // cuando un ataque no lo declara.
        public const float HitLightDuration = 0.50f;
        public const float HitHeavyDuration = 0.70f;

        // ---------------------------------------------------------------------
        // Agacharse
        // ---------------------------------------------------------------------
        // El clip `agacharse` dura 0.567 s: a 1x el luchador tardaba medio segundo en
        // bajar, el triple que un golpe rapido, y el agacharse se sentia roto. El estado
        // se hornea a esta duracion (speed = clip.length / 0.22) y la pose final se
        // mantiene mientras se siga pulsando.
        public const float CrouchStateDuration = 0.22f;
        public const float FadeCrouch = 0.10f;

        // ---------------------------------------------------------------------
        // Salto: la fisica sigue al clip
        // ---------------------------------------------------------------------
        // El clip `Jumping` (1.9 s) tiene tres tramos medidos con la hoja lateral
        // (Preview/6): carga hasta el 25 %, aire del 25 % al 66 %, aterrizaje hasta el
        // 100 %. Antes el impulso se aplicaba en el frame de la pulsacion, asi que el
        // cuerpo subia mientras las piernas aun estaban flexionando ("se eleva antes de
        // que las piernas salten"). Ahora el impulso sale al terminar la carga y la
        // reproduccion se reparte por tramos para que el clip y la parabola coincidan.
        public const float JumpAnticipation = 0.13f;
        public const float JumpAirtime = 0.52f;
        public const float JumpLanding = 0.24f;
        public const float JumpTotal = JumpAnticipation + JumpAirtime + JumpLanding;
        // Duracion con la que se hornea el estado: clip.length / JumpTotal.
        public const float JumpStateDuration = JumpTotal;
        // Fracciones del clip (medidas, no estimadas).
        public const float JumpTakeoffPhase = 0.25f;
        public const float JumpLandPhase = 0.66f;

        // Velocidad de reproduccion de las reacciones. Los clips de Mixamo son
        // flinches largos (1.3-3.5 s) con una cola muerta; a 1x el golpe se lee
        // lento y el hitstun corta el clip a la mitad. Reproducidos rapidos, el
        // gesto de impacto ocupa el hitstun completo y la salida se funde.
        public const float HitReactionSpeed = 2.00f;
        public const float HitHeavyReactionSpeed = 1.75f;
        public const float GroinReactionSpeed = 2.60f;
        public const float StunnedReactionSpeed = 1.90f;

        // ---------------------------------------------------------------------
        // Locomocion
        // ---------------------------------------------------------------------
        // Las cadencias naturales REALES de los clips de locomocion, medidas como
        // recorrido de cadera / duracion antes de hornearlos in-place: Walk 1.34 m/s,
        // Run 3.24 m/s. KomSetup mide estos valores por personaje; aqui quedan como
        // respaldo.
        public const float NaturalWalkSpeed = 0.25f;
        public const float NaturalRunSpeed = 3.24f;

        // El Animator reproduce la locomocion multiplicada por el MISMO factor con el
        // que se mueve el cuerpo, asi que subir la velocidad NO introduce
        // deslizamiento de pies: sube el ritmo del paso a la vez que el avance.
        // 0.55 m/s (dato original) era demasiado lento para cerrar 4.2 m de arena.
        public const float WalkSpeedScale = 2.00f;
        public const float DashSpeedScale = 1.30f;

        // Rampa de velocidad. Sin esto el luchador pasa de 0 a tope en un frame, que
        // es la mitad de la sensacion de "tosco": el cuerpo no tiene peso.
        public const float MoveAccel = 34f;
        public const float MoveDecel = 46f;

        public const float SprintDuration = 0.65f;
        public const float SprintCooldown = 1.0f;

        // ---------------------------------------------------------------------
        // Entrada
        // ---------------------------------------------------------------------
        // 0.18 s = 11 frames a 60 Hz, el buffer estandar de un juego de pelea
        // (Street Fighter 6 usa 10-12). La ventana se mide DESDE LA PULSACION y solo se
        // honra si el primer frame accionable cae dentro. Es la diferencia entre un buffer
        // y una cola: si la tecla no puede salir en 0.18 s, se DESCARTA. Un golpe que
        // aparece medio segundo despues de pulsarlo no se lee como propio.
        public const float AttackBufferTime = 0.18f;
        // Pulsacion MANTENIDA (modelo de Smash Ultimate): si el jugador sigue apretando el
        // boton, su intencion sobrevive hasta este tope. No se siente como cola porque el
        // dedo sigue ahi; al soltar, vuelve a mandar AttackBufferTime.
        public const float AttackBufferHold = 0.40f;
        // Prioridad de entrada: un movimiento NUEVO del jugador cancela la orden guardada
        // (el ultimo input gana). Se detecta como flanco: el eje pasa de reposo a pulsado.
        public const float MoveCancelThreshold = 0.40f;
        public const float MoveCancelRelease = 0.20f;
        public const float ComboWindow = 0.40f;
        // Tiempo maximo entre dos pulsaciones de una secuencia de combo
        // (G+G+G, H+H+H, J+J+J+J). Si pasa mas, la secuencia se reinicia.
        // 0.40 s = 24 frames: una mano que "machaca" encadena tres toques en ~0.25 s, asi
        // que la secuencia sigue saliendo sola; 0.55 s dejaba que un mash disperso cerrara
        // un combo medio segundo despues y el jugador ya estaba en otra cosa.
        public const float ComboChainStep = 0.40f;
        // Vida del combo YA RECONOCIDO que espera al primer frame accionable. Caduca: una
        // secuencia que no entra en 0.34 s es una cola, no una intencion. Antes no caducaba
        // y un G+G+G pulsado durante un golpe se ejecutaba un segundo despues, quitandole
        // el control al jugador.
        public const float ComboPendingMax = 0.34f;
        // Cancelacion en cadena: a partir de este porcentaje del ataque (y solo cuando
        // TODOS sus golpes ya se resolvieron) una pulsacion guardada en el buffer corta
        // la recuperacion y arranca el golpe siguiente. Es lo que separa "se me encolo
        // la tecla" de "encadeno": la orden no espera al ultimo frame del clip.
        public const float AttackCancelAt = 0.62f;
        // Eslabones maximos de una cadena de cancelaciones antes de que el golpe tenga que
        // terminar. Con 2, la cuarta pulsacion de un mash se quedaba sin salida y el golpe
        // en curso se comia la tecla: el jugador pedia otro golpe y no habia respuesta.
        public const int MaxCancelChain = 4;
        // Al acabar un ataque nadie puede levantar guardia en el mismo frame. El clip
        // termina y la pose necesita un frame para asentarse; sin este bloqueo, la IA
        // (que decide cada frame) sacaba la guardia al instante y parecia que se comia
        // la recuperacion del golpe. Es simetrico: tambien vale para el jugador.
        public const float AttackRecoveryLock = 0.10f;

        // ---------------------------------------------------------------------
        // Bloqueo: pose sostenida + escudo
        // ---------------------------------------------------------------------
        // El clip de guardia es un boxing de 1.53 s con desplazamientos de brazos: a 1x
        // parecia que el luchador hacia sombra en vez de bloquear. Bloqueando quieto se
        // reproduce casi congelado (micro-respiracion) y el feedback lo da el escudo.
        public const float BlockHoldSpeed = 0.10f;
        // El escudo es un aura pegada al cuerpo (cascara sobre la malla), no un cartel
        // delante de la camara: se enciende del lado que mira al rival.
        public const float ShieldFadeIn = 0.09f;
        public const float ShieldFadeOut = 0.13f;
        // Recuperacion desde el suelo: GroinHit y Stunned acaban tumbados, asi que tras
        // el hitstun hay que levantarse. Sin esto el clip se cortaba y el luchador
        // aparecia de pie de un frame a otro (el "cancela animaciones" que se veia).
        public const float DownRecoveryDuration = 0.62f;
        public const float DownRecoverySpeed = 5.5f;
        public const float DownRecoveryStartPhase = 0.52f;

        // ---------------------------------------------------------------------
        // Empuje (knockback)
        // ---------------------------------------------------------------------
        // El empuje es una VELOCIDAD que decae, no un teletransporte. Recorrido
        // total = KnockbackImpulse / KnockbackDecay por unidad de knockback
        // (~0.54 m por unidad), repartido en ~0.25 s.
        public const float KnockbackImpulse = 6.5f;
        public const float KnockbackDecay = 12f;
        // El bloqueo empuja mucho menos y ademas aturde (blockstun).
        public const float BlockPushScale = 0.28f;
        public const float BlockStunLight = 0.16f;
        public const float BlockStunHeavy = 0.26f;

        // ---------------------------------------------------------------------
        // Barra de guardia
        // ---------------------------------------------------------------------
        public const float GuardRegenPerSecond = 0.32f;
        public const float GuardRegenDelay = 0.55f;
        public const float GuardBreakStun = 0.95f;
        public const float GuardBreakPushback = 0.45f;
        public const float GuardMeterAfterBreak = 0.5f;

        // ---------------------------------------------------------------------
        // Medidor de super
        // ---------------------------------------------------------------------
        public const float SuperGainDealtPerDamage = 0.012f;
        public const float SuperGainTakenPerDamage = 0.016f;
        public const float SuperGainBlockedPerDamage = 0.010f;
        public const float SuperFull = 1f;

        public const float DefaultHurtboxRadius = 0.34f;
        public const float DefaultHurtboxHeight = 1.50f;
        public const float DefaultCrouchHurtboxHeight = 0.85f;

        // Capas: 0 Base (locomocion discreta + estados full-body) y 1 ReservedSpecials
        // (peso 0). Sin mascaras: en este rig una mascara de upper body desgarra la cabeza
        // aunque el clip sea el mismo.
        public const int BaseLayer = 0;
        public const int ReservedLayer = 1;

        // ---------------------------------------------------------------------
        // Mezcla entre estados
        // ---------------------------------------------------------------------
        // SIN MEZCLA de poses simultaneas: la locomocion son estados discretos a
        // peso 1. Los fundidos cortos de ENTRADA/SALIDA no mezclan dos poses de
        // locomocion: solo suavizan el salto entre la ultima pose de un estado y la
        // primera del siguiente (0.02-0.12 s, menos de 8 frames).
        public const float FadeLocomotion = 0f;
        public const float FadeGuard = 0f;
        // Entrada al ataque: casi instantanea (el golpe debe salir ya) pero sin el
        // corte seco que hacia saltar la pose.
        public const float FadeAttack = 0.02f;
        // SALIDA del ataque a locomocion: el clip termina en guardia, no en Idle.
        // Sin este fundido hay un salto visible al acabar CADA golpe.
        public const float FadeAttackExit = 0.12f;
        public const float FadeHit = 0f;
        // Salida del hitstun. El clip de reaccion se corta antes de su cola; con
        // este fundido el corte no se ve.
        public const float FadeHitOut = 0.10f;
        public const float FadeKO = 0f;
        // Unico fade de locomocion permitido: Idle<->Walk. Evita el salto de
        // pose de ~4.7 cm al empezar/parar de andar sin mezclar dos poses de caminata.
        public const float FadeIdleWalk = 0.06f;
        // Walk<->Run y Idle<->Run: mismo rig, mismos huesos, ambos completos.
        public const float FadeRun = 0.12f;

        // ---------------------------------------------------------------------
        // Recolectables de arena (concha / refresco)
        // ---------------------------------------------------------------------
        // Un asalto de 60 s da para tres caidas: la primera pronto (para que la mecanica
        // entre en la pelea), luego cada 13-18 s. Nunca mas de dos a la vez, y nunca al
        // lado de un luchador: el que lo coge tiene que MOVERSE.
        public const float PickupFirstDelayMin = 7f;
        public const float PickupFirstDelayMax = 10f;
        public const float PickupIntervalMin = 13f;
        public const float PickupIntervalMax = 18f;
        public const int PickupPerRound = 3;
        public const int PickupMaxAlive = 2;
        // Distancia minima a cualquier luchador en el momento de soltarlo.
        public const float PickupMinFighterDistance = 1.2f;
        // Margen desde el borde de la arena.
        public const float PickupArenaMargin = 1.1f;
        // La linea de combate VISIBLE (el anillo del escenario) es mas estrecha que el
        // limite de movimiento. Con el margen de arena solo, la concha y el refresco
        // caian junto a las paredes y parecian inalcanzables: el carril se acota aqui.
        // 2.15 y no 2.6: en La Madrugadora el telon esta a metro y medio del plano de
        // pelea, asi que un recolectable en el borde del carril se recortaba contra el
        // zocalo y se leia "detras" del ring aunque el luchador llegase a el.
        public const float PickupLaneHalfWidth = 2.15f;
        public const float PickupCollectRadius = 0.62f;
        public const float PickupLifetime = 20f;
        // La concha cura el 18 % de la vida maxima (en Juan: ~19 de 105, un golpe fuerte).
        public const float PickupConchaHealFraction = 0.18f;
        // El refresco devuelve el 35 % de la barra de estamina.
        // El refresco llena el SUPER (la barra que se ve). De paso devuelve estamina,
        // que sigue gastandose al bloquear aunque ya no tenga barra propia.
        public const float PickupSodaSuperFraction = 0.35f;
        public const float PickupSodaGuardFraction = 0.35f;

        // ---------------------------------------------------------------------
        // Camara de impacto
        // ---------------------------------------------------------------------
        // Presentacion de apertura: la camara entra desde el encuadre panoramico. Dura
        // lo que el cartel de RONDA/PELEA para que llegue al encuadre de pelea justo
        // cuando el jugador recupera el control (0.2 + 1.30 + 0.45 = 1.95 s en
        // CombatController.RoundIntroRoutine / BeginRoundRoutine).
        public const float CameraIntroTime = 1.95f;
        public const float CameraFollowTime = 0.16f;
        public const float CameraRotateSpeed = 12f;

        public static bool DebugHitboxes;
    }
}
