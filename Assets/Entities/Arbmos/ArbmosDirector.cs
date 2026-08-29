using System.Collections.Generic;
using UnityEngine;
using Scanner;
using Gameplay;

// Director del Arbmos (SERVER-AUTHORITATIVE; solo corre en el host). A diferencia del
// GameDirector/Sorken —que spawnea UN enemigo compartido por todos— el Arbmos es una
// ALUCINACION INDIVIDUAL: el director lleva una maquina de estados POR JUGADOR y, para
// cada uno, decide cuando y donde aparece SU propia copia (spawn dirigido, ServerSpawnFor)
// que solo ese jugador ve. Toda la dificultad sale de la NightConfig.
//
// Reglas (doc): solo desde la noche 4; quedarse quieto lo invoca; con la linterna apagada
// es mas probable; drena cordura si el jugador se MUEVE mientras esta presente; y se
// vuelve LETAL solo si la cordura ya esta en cero y se vuelve a gatillar (queda inmovil
// unos segundos, sin aura, con distorsion de camara en escalada, y despues embiste).
//
// Se auto-crea (Ensure) desde GameDirector al arrancar la partida — no requiere ponerlo
// en la escena.
public class ArbmosDirector : MonoBehaviour
{
    public static ArbmosDirector Instance { get; private set; }

    private enum Phase { Dormant, Present, LethalStalk, LethalChase, Done }

    private class Haunt
    {
        public Phase phase = Phase.Dormant;
        public float cooldown;          // Dormant: espera hasta el proximo intento

        // Deteccion de movimiento POR FRAME (solo para el drenaje de cordura del Present).
        public Vector3 anchor;          // referencia que se recentra al detectar movimiento (PIVOTE, no camara)
        public float anchorYaw;         // rumbo al fijar el ancla (tolera el error del brazo al girar)
        public bool  hasAnchor;
        public float moveHold;          // >0 => "en movimiento" (con decaimiento anti-jitter)

        // Ventana de quietud (esfera) — gatillo de invocacion. Ver UpdateQuietud.
        public Vector3 sphereCenter;    // centro de la esfera vigente
        public float sphereYaw;         // rumbo al generarla (tolerancia por giro)
        public bool  hasSphere;
        public float windowLeft;        // lo que le queda a la ventana actual
        public float outsideTime;       // tiempo acumulado FUERA de la esfera en esta ventana
        public bool  quieto;            // veredicto de la ultima ventana cerrada (latcheado)
        public float lastDist;          // debug: distancia actual al centro
        public float lastTol;           // debug: radio efectivo (radio + tolerancia por giro)

        public uint  netId;             // copia de Arbmos spawneada para este jugador
        public ArbmosEntity ent;
        public float presentTimer;      // Present: cuanto le queda a la alucinacion
        public float stalkTimer;        // LethalStalk: inmovil antes de embestir
        public float stalkTotal;

        public readonly List<Vector3> path = new();
        public int   pathIndex;
        public float repathTimer;
    }

    private readonly Dictionary<uint, Haunt> _haunts = new();
    private NightConfig _night;
    private bool _running;

    public static ArbmosDirector Ensure()
    {
        if (Instance == null)
        {
            var go = new GameObject("ArbmosDirector");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ArbmosDirector>();
        }
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.OnClientLeft -= HandleClientLeft;
        if (Instance == this) Instance = null;
    }

    // Lo llama el GameDirector (server) al arrancar la partida. Se (re)suscribe al
    // NetworkManager ACTUAL (puede haberse recreado entre sesiones) y reinicia el estado.
    public void StartRun()
    {
        var net = NetworkManager.Instance;
        if (net == null || !net.IsServer) return;
        // La noche la manda el GameDirector (que ya resolvio el fallback si se entro a
        // SampleScene sin GameSession); GameSession queda de respaldo por si acaso.
        _night = GameDirector.Instance != null ? GameDirector.Instance.NocheActual : null;
        if (_night == null && GameSession.Instance != null) _night = GameSession.Instance.SelectedNight;
        _haunts.Clear();
        net.OnClientLeft -= HandleClientLeft;
        net.OnClientLeft += HandleClientLeft;
        _running = true;
    }

    // Corta la corrida sin cerrar la sesión (ver Gameplay.NightTransition). Los Arbmos
    // vivos ya los despawnea NetworkManager.ServerResetNight.
    public void StopRun()
    {
        _running = false;
        _haunts.Clear();
    }

    private void HandleClientLeft(uint clientId)
    {
        if (_haunts.TryGetValue(clientId, out var h))
        {
            DespawnHaunt(h);
            _haunts.Remove(clientId);
        }
    }

    private void Update()
    {
        if (!_running) return;
        var net = NetworkManager.Instance;
        // GameStarted ademas de _running: este director es DontDestroyOnLoad y salir al
        // menu no lo frenaba, asi que se reanudaba —con la noche y los haunts viejos— en
        // cuanto la partida siguiente levantaba su server, todavia en la sala.
        if (net == null || !net.IsServer || !net.GameStarted) return;

        // Solo en las noches donde el diseñador activo al Arbmos (doc: noche 4 en adelante).
        if (_night == null || !_night.arbmosActive) return;

        float dt = Time.deltaTime;

        // Jugadores objetivo este tick: host (0) + clientes conectados, vivos y con pose.
        foreach (var cid in AlivePlayersWithPose())
        {
            if (!_haunts.TryGetValue(cid, out var h))
            {
                h = new Haunt { cooldown = Random.Range(_night.arbmosCooldownMin, _night.arbmosCooldownMax) };
                _haunts[cid] = h;
            }
            TickPlayer(cid, h, dt);
        }

        // Limpieza: jugadores que murieron o dejaron de reportar pose y tienen Arbmos vivo.
        _scratchRemove.Clear();
        foreach (var kv in _haunts)
        {
            if (ServerDeaths.IsDead(kv.Key))
            {
                DespawnHaunt(kv.Value);
                _scratchRemove.Add(kv.Key);
            }
        }
        foreach (var cid in _scratchRemove) _haunts.Remove(cid);
    }

    private readonly List<uint> _scratchRemove = new();

    // ── Maquina de estados por jugador ─────────────────────────────────────
    private void TickPlayer(uint cid, Haunt h, float dt)
    {
        if (!TryGetPlayerPos(cid, out var ppos)) return;

        // La quietud se mide sobre el PIVOTE del cuerpo, no sobre la camara (ver
        // UpdateQuietud). El resto de las fases sigue usando ppos: es donde esta el jugador.
        Vector3 fwd    = PlayerForward(cid);
        Vector3 pivote = Pivote(ppos, fwd);
        float   yaw    = Yaw(fwd);
        UpdateQuietud(h, pivote, yaw, dt);   // ventana/esfera → gatillo de invocacion
        UpdateMotion(h, pivote, yaw, dt);    // por frame → drenaje de cordura del Present

        // Wireframe de la esfera (solo host + development build + toggle prendido).
        if (cid == 0 && ArbmosDebug.Wireframe && h.hasSphere)
            ArbmosQuietudViz.Dibujar(h.sphereCenter, h.lastTol, pivote,
                                     FloorWorldY(ppos.y), h.lastDist <= h.lastTol);

        switch (h.phase)
        {
            case Phase.Dormant:     TickDormant(cid, h, dt, ppos);     break;
            case Phase.Present:     TickPresent(cid, h, dt, ppos);     break;
            case Phase.LethalStalk: TickLethalStalk(cid, h, dt, ppos); break;
            case Phase.LethalChase: TickLethalChase(cid, h, dt, ppos); break;
            case Phase.Done:        break;   // ya mato / termino: no reaparece
        }
    }

    private void TickDormant(uint cid, Haunt h, float dt, Vector3 ppos)
    {
        h.cooldown -= dt;
        if (h.cooldown > 0f) return;
        if (!h.quieto) return;   // gatillo: la ultima ventana de quietud se cumplio entera
        h.quieto = false;        // el veredicto se consume: para reintentar hay que cumplir otra

        bool zero = SanitySystem.Instance != null && SanitySystem.Instance.IsAtZero(cid);
        if (zero)
        {
            InvokeLethal(cid, h, ppos);
            return;
        }

        // No letal: probabilidad (linterna apagada la aumenta).
        float chance = _night.arbmosSpawnChancePerAttempt;
        if (IsFlashlightOff(cid)) chance *= _night.arbmosFlashlightOffChanceMul;
        if (Random.value <= Mathf.Clamp01(chance)) InvokePresent(cid, h, ppos);
        else h.cooldown = 3f;   // no salio: reintentar en cuanto se cumpla otra ventana
    }

    private void InvokePresent(uint cid, Haunt h, Vector3 ppos)
    {
        if (!SpawnArbmosFor(cid, h, ppos, aura: true, lethal: false, distort: 0.3f)) return;
        h.presentTimer = _night.arbmosPresentSeconds;
        h.phase = Phase.Present;
    }

    private void TickPresent(uint cid, Haunt h, float dt, Vector3 ppos)
    {
        if (h.ent == null) { EndHaunt(h); return; }
        FacePlayer(h, ppos);

        if (h.moveHold > 0f)   // el jugador se movio => drena cordura y el Arbmos "corre"
        {
            SanitySystem.Instance?.ServerDrain(cid, _night.arbmosSanityDrainPerSecond * dt);
            h.ent.SetState(ArbmosState.Running);
            // Deriva lento hacia el jugador para mantenerse en la alucinacion (sin nav:
            // es una alucinacion). No se acerca a menos de la distancia de aparicion.
            if (HorizDist(h.ent.Position, ppos) > _night.arbmosSpawnDistance)
                h.ent.MoveTo(ppos, _night.arbmosPresentFollowSpeed, dt);
        }
        else h.ent.SetState(ArbmosState.Idle);

        h.presentTimer -= dt;
        if (h.presentTimer <= 0f) EndHaunt(h);
    }

    private void InvokeLethal(uint cid, Haunt h, Vector3 ppos)
    {
        // La secuencia letal NO muestra aura y arranca con una distorsion brusca.
        if (!SpawnArbmosFor(cid, h, ppos, aura: false, lethal: true, distort: 1f)) return;
        h.stalkTotal = _night.arbmosLethalStalkSeconds;
        h.stalkTimer = h.stalkTotal;
        h.phase = Phase.LethalStalk;
    }

    private void TickLethalStalk(uint cid, Haunt h, float dt, Vector3 ppos)
    {
        if (h.ent == null) { h.phase = Phase.Done; return; }
        FacePlayer(h, ppos);
        h.ent.SetState(ArbmosState.Idle);
        h.ent.SetAura(false);

        // Escala gradual de la distorsion hacia el maximo a medida que se acerca el ataque.
        float k = 1f - Mathf.Clamp01(h.stalkTimer / Mathf.Max(0.01f, h.stalkTotal));
        h.ent.SetDistort(Mathf.Lerp(0.5f, 1f, k));

        h.stalkTimer -= dt;
        if (h.stalkTimer <= 0f)
        {
            h.ent.SetState(ArbmosState.Chasing);
            h.path.Clear(); h.pathIndex = 0; h.repathTimer = 0f;
            h.phase = Phase.LethalChase;
        }
    }

    private void TickLethalChase(uint cid, Haunt h, float dt, Vector3 ppos)
    {
        if (h.ent == null) { h.phase = Phase.Done; return; }
        h.ent.SetDistort(1f);

        if (HorizDist(h.ent.Position, ppos) <= _night.arbmosGrabRange)
        {
            ServerDeaths.Kill(cid);   // jumpscare / muerte
            DespawnHaunt(h);
            h.phase = Phase.Done;
            return;
        }

        // Path-following con SorkerNav (respeta paredes/puertas/cubos).
        h.repathTimer -= dt;
        if (h.repathTimer <= 0f || h.pathIndex >= h.path.Count)
        {
            h.repathTimer = 0.3f;
            if (SorkerNav.Instance != null && SorkerNav.Instance.TryGetPath(h.ent.Position, ppos, h.path))
                h.pathIndex = 0;
            else
                h.path.Clear();
        }
        Vector3 step = h.pathIndex < h.path.Count ? h.path[h.pathIndex] : ppos;
        h.ent.MoveTo(step, _night.arbmosLethalChaseSpeed, dt);
        if (h.pathIndex < h.path.Count && HorizDist(h.ent.Position, h.path[h.pathIndex]) <= 0.2f) h.pathIndex++;
    }

    // ── Helpers de haunt ───────────────────────────────────────────────────
    private bool SpawnArbmosFor(uint cid, Haunt h, Vector3 ppos, bool aura, bool lethal, float distort)
    {
        Vector3 spawn = SpawnPositionNear(cid, ppos);
        uint netId = NetworkManager.Instance.ServerSpawnFor(cid, EntityTypeIds.Arbmos, spawn);
        var ent = GetArbmos(netId);
        if (ent == null) { h.cooldown = 2f; return false; }

        ent.SetPositionDirectly(spawn);
        FaceWorld(ent, ppos);
        ent.SetState(ArbmosState.Idle);
        ent.SetAura(aura);
        ent.SetLethal(lethal);
        ent.SetDistort(distort);

        h.netId = netId;
        h.ent   = ent;
        return true;
    }

    // Fin de una alucinacion NO letal: despawnea y agenda la proxima.
    private void EndHaunt(Haunt h)
    {
        DespawnHaunt(h);
        h.cooldown  = Random.Range(_night.arbmosCooldownMin, _night.arbmosCooldownMax);
        h.hasAnchor = false;
        h.hasSphere = false;   // la quietud se vuelve a medir desde cero tras la alucinacion
        h.quieto    = false;
        h.phase     = Phase.Dormant;
    }

    private void DespawnHaunt(Haunt h)
    {
        if (h.netId != 0 && NetworkManager.Instance != null)
            NetworkManager.Instance.ServerDespawn(h.netId);
        h.netId = 0; h.ent = null; h.path.Clear();
    }

    private void FacePlayer(Haunt h, Vector3 ppos) { if (h.ent != null) FaceWorld(h.ent, ppos); }

    private static void FaceWorld(ArbmosEntity ent, Vector3 target)
    {
        var dir = target - ent.Position; dir.y = 0f;
        if (dir.sqrMagnitude > 1e-4f) ent.FaceDirection(dir);
    }

    // Posicion de aparicion: delante del jugador (hacia donde mira) a la distancia
    // configurada, a ras del piso. Asi es mas probable que la vea. Fallback: direccion fija.
    private Vector3 SpawnPositionNear(uint cid, Vector3 ppos)
    {
        Vector3 fwd = PlayerForward(cid); fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 pos = ppos + fwd * _night.arbmosSpawnDistance;
        pos.y = FloorWorldY(ppos.y);
        return pos;
    }

    // ── Movimiento / quietud del jugador (robusto al jitter de la camara AR) ──
    //
    // "Quieto" es que el JUGADOR no se desplazo, no que la camara no se haya movido: la
    // camara AR es el TELEFONO, que se sostiene por delante del cuerpo. Girar en el lugar
    // —mirar alrededor, lo mas normal del mundo— lo pasea por un arco de medio metro con
    // los pies clavados: un giro de 90° con el brazo estirado mueve la camara ~55 cm.
    //
    // Dos correcciones sobre el mismo hecho fisico, que valen para las dos medidas de abajo:
    //  1) Medimos el PIVOTE (la camara corrida hacia atras un brazo, ~el eje del cuerpo)
    //     en vez de la camara: al girar en el lugar el pivote se queda donde esta.
    //  2) El brazo real cambia con la persona y con como agarre el telefono, asi que al
    //     pivote le queda un error proporcional a cuanto giro. El radio lleva una
    //     tolerancia extra que crece con el giro acumulado (con tope).
    //
    // Caminar de verdad mueve el pivote igual que la camara, asi que se sigue detectando.
    //
    // Son DOS medidas distintas porque responden dos preguntas distintas:
    //   UpdateQuietud → "¿estuvo quieto todo este rato?"  (gatillo de invocacion)
    //   UpdateMotion  → "¿se esta moviendo AHORA?"        (drenaje de cordura en Present)
    // Con una sola no alcanza: la ventana de quietud deja al jugador "fuera de la esfera"
    // de forma permanente en cuanto camino un poco, y eso drenaria cordura estando parado.

    // Distancia camara → eje de giro del cuerpo.
    private const float BrazoMetros = 0.4f;
    // Incertidumbre de ese brazo, por radian girado (residuo que deja la correccion 1).
    private const float BrazoError  = 0.25f;
    // Tope de la tolerancia extra: girar mucho no puede habilitar caminar.
    private const float TolMaxExtra = 0.35f;

    // Gatillo de invocacion — VENTANA DE QUIETUD.
    //
    // Cada X segundos (arbmosStillInvokeSeconds) se genera una esfera de radio R
    // (arbmosStillRadius) centrada en el jugador. Si llega al final de la ventana sin
    // haberse salido de ella, se lo considera quieto; el veredicto queda latcheado hasta la
    // ventana siguiente, para que el cooldown lo pueda consumir cuando vence sin perder una
    // ventana entera por desfase.
    //
    // Salirse de la esfera cuenta como MOVERSE EN EL ACTO, no recien al cerrar la ventana:
    // apenas se agota la gracia se invalida el veredicto (aunque venga latcheado en true de
    // la ventana anterior — si esta caminando no puede gatillar) y se abre una esfera nueva
    // ahi mismo, asi el reloj arranca de donde esta y no queda una ventana muerta corriendo.
    //
    // Reemplaza al contador continuo anterior, que se reseteaba a cero en cuanto UN frame
    // se pasaba del radio: en el celular, entre jitter del tracking y correcciones de drift,
    // eso pasaba seguido y el contador no llegaba nunca al umbral — el Arbmos no aparecia.
    // Aca un pico aislado no tira la ventana: hay un presupuesto de tiempo fuera
    // (arbmosStillOutsideGrace) que un frame malo no alcanza a agotar, pero caminar si.
    private void UpdateQuietud(Haunt h, Vector3 pivote, float yaw, float dt)
    {
        if (!h.hasSphere) { AbrirVentana(h, pivote, yaw); return; }

        float giroRad = Mathf.Abs(Mathf.DeltaAngle(yaw, h.sphereYaw)) * Mathf.Deg2Rad;
        h.lastTol  = Radio() + Mathf.Min(giroRad * BrazoError, TolMaxExtra);
        h.lastDist = HorizDist(pivote, h.sphereCenter);

        if (h.lastDist > h.lastTol)
        {
            h.outsideTime += dt;
            if (h.outsideTime > Gracia())     // se fue de la esfera: se movio, punto
            {
                h.quieto = false;
                AbrirVentana(h, pivote, yaw);
                return;
            }
        }

        h.windowLeft -= dt;
        if (h.windowLeft > 0f) return;

        h.quieto = true;                      // ventana completa sin salirse
        AbrirVentana(h, pivote, yaw);         // la proxima esfera arranca donde esta ahora
    }

    private void AbrirVentana(Haunt h, Vector3 pivote, float yaw)
    {
        h.sphereCenter = pivote;
        h.sphereYaw    = yaw;
        h.hasSphere    = true;
        h.windowLeft   = Ventana();
        h.outsideTime  = 0f;
        h.lastDist     = 0f;
        h.lastTol      = Radio();
    }

    // Parametros del gatillo: los de la noche, salvo que dev los este pisando (ArbmosDebug).
    private float Radio()   => ArbmosDebug.Radio(_night);
    private float Ventana() => Mathf.Max(0.5f, ArbmosDebug.Ventana(_night));
    private float Gracia()  => ArbmosDebug.Gracia(_night);

    // "¿Se esta moviendo ahora?" — el ancla se recentra en cuanto se detecta movimiento,
    // asi que mide desplazamiento RECIENTE, no acumulado. Solo alimenta a moveHold.
    private void UpdateMotion(Haunt h, Vector3 pivote, float yaw, float dt)
    {
        if (!h.hasAnchor)
        {
            h.anchor   = pivote; h.anchorYaw = yaw; h.hasAnchor = true;
            h.moveHold = 0f;
            return;
        }

        float giroRad = Mathf.Abs(Mathf.DeltaAngle(yaw, h.anchorYaw)) * Mathf.Deg2Rad;
        float tol     = Radio() + Mathf.Min(giroRad * BrazoError, TolMaxExtra);

        if (HorizDist(pivote, h.anchor) > tol)
        {
            h.anchor    = pivote;
            h.anchorYaw = yaw;
            h.moveHold  = 0.35f;   // se lo considera "en movimiento" un ratito
        }

        h.moveHold = Mathf.Max(0f, h.moveHold - dt);
    }

    // Eje de giro aproximado del jugador: la camara llevada hacia atras un brazo.
    private static Vector3 Pivote(Vector3 camPos, Vector3 camFwd)
    {
        camFwd.y = 0f;
        if (camFwd.sqrMagnitude < 1e-6f) return camPos;   // mirando a pique: sin rumbo util
        return camPos - camFwd.normalized * BrazoMetros;
    }

    // Rumbo horizontal (grados) de una direccion de mirada.
    private static float Yaw(Vector3 fwd)
    {
        fwd.y = 0f;
        return fwd.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
    }

    // ── Consultas de jugadores ─────────────────────────────────────────────
    private IEnumerable<uint> AlivePlayersWithPose()
    {
        var net = NetworkManager.Instance;
        if (Camera.main != null && ServerDeaths.IsAlive(0)) yield return 0;   // host
        foreach (var cid in net.ConnectedClients)
            if (ServerDeaths.IsAlive(cid) && net.TryGetClientWorldPosition(cid, out _)) yield return cid;
    }

    private bool TryGetPlayerPos(uint cid, out Vector3 pos)
    {
        if (cid == 0)
        {
            if (Camera.main != null) { pos = Camera.main.transform.position; return true; }
            pos = Vector3.zero; return false;
        }
        return NetworkManager.Instance.TryGetClientWorldPosition(cid, out pos);
    }

    private Vector3 PlayerForward(uint cid)
    {
        if (cid == 0) return Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
        return NetworkManager.Instance.TryGetClientForward(cid, out var f) ? f : Vector3.forward;
    }

    private bool IsFlashlightOff(uint cid)
    {
        var net = NetworkManager.Instance;
        if (cid == 0) return !net.LocalFlashlightOn();
        // Desconocida => se asume encendida (no penaliza).
        return net.TryGetClientFlashlightOn(cid, out var on) && !on;
    }

    private ArbmosEntity GetArbmos(uint netId)
    {
        if (EntityRegistry.Instance != null && EntityRegistry.Instance.TryGet(netId, out var ne))
            return ne.GetComponent<ArbmosEntity>();
        return null;
    }

    private float FloorWorldY(float fallback)
    {
        var wo = WorldOrigin.Instance;
        if (FloorPoint.Instance != null && wo != null)
            return wo.ToWorld(FloorPoint.Instance.LocalPosition).y;
        return fallback;
    }

    private static float HorizDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    // Snapshot para el DebugHud (solo host).
    //
    // Cuando el director NO esta corriendo dice POR QUE: si no, "no aparece el Arbmos" y
    // "no se ve el wireframe de la esfera" son la misma pantalla en blanco y no hay forma
    // de distinguir un bug del gatillo de una noche sin Arbmos o de una partida sin arrancar.
    public string DebugSnapshot()
    {
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer)
            return "[Arbmos]  solo corre en el HOST (sos cliente)\n";
        if (!_running)
            return "[Arbmos]  director parado: la partida todavia no arranco\n";
        if (_night == null)
            return "[Arbmos]  sin NightConfig: entraste a SampleScene sin pasar por el menu\n" +
                   "          (GameSession.SelectedNight == null => el director no tickea)\n";
        if (!_night.arbmosActive)
            return $"[Arbmos]  arbmosActive=0 en '{_night.displayName}': esta noche no tiene Arbmos\n";

        var sb = new System.Text.StringBuilder();
        sb.Append($"[Arbmos]  noche='{_night.displayName}'  jugadores={_haunts.Count}\n");
        sb.Append($"  quietud: R={Radio():F2}m  ventana={Ventana():F1}s  gracia={Gracia():F2}s" +
                  $"{(ArbmosDebug.Activo ? "  (DEV)" : "")}\n");
        foreach (var kv in _haunts)
        {
            var h = kv.Value;
            sb.Append($"  p{kv.Key}: {h.phase} cd={h.cooldown:F1} quieto={(h.quieto ? "SI" : "no")}" +
                      $" d={h.lastDist:F2}/{h.lastTol:F2} t={h.windowLeft:F1} fuera={h.outsideTime:F2}" +
                      $"{(h.ent != null ? $" ent={h.ent.State}" : "")}\n");
        }
        return sb.ToString();
    }
}
