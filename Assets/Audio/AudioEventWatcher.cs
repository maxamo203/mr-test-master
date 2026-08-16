using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Convierte el estado replicado de las entidades en sonidos, SIN tocar sus prefabs ni la
// red. Vive en el mismo GameObject que el AudioManager.
//
// Por que un watcher central y no un componente de audio por entidad:
//  - Los estados (SorkenState, VelethState, ArbmosState) ya viajan en WorldState, asi que
//    comparar el estado contra el del frame anterior funciona IGUAL en host y en cliente.
//    Los directores, en cambio, son server-only: un sonido puesto ahi no lo oye nadie mas.
//  - Es el mismo enfoque que ya usa TensionSystem para leer las entidades vivas.
//  - Cero cambios en prefabs y un solo lugar donde mirar cuando algo no suena.
//
// Con <=5 entidades vivas el costo del recorrido es despreciable, y si no hay ni un clip
// cargado el Update sale en la primera linea.
[DefaultExecutionOrder(-44)]
public class AudioEventWatcher : MonoBehaviour
{
    // Claves de los loops (ver AudioManager.Bucle).
    private const string BuclePasos      = "sorken_pasos";
    private const string BucleVeleth     = "veleth_persecucion";
    private const string BucleDrena      = "arbmos_drena";
    private const string BucleSusurros   = "arbmos_susurros";
    private const string BucleGritos     = "arbmos_gritos";

    // Foto del frame anterior de una entidad, para detectar transiciones.
    private struct Foto
    {
        public byte tipo;
        public int  estado;
        public bool letal;
        public bool jumpscareHecho;
    }

    private readonly Dictionary<uint, Foto> _previo  = new();
    private readonly HashSet<uint>          _vistos  = new();
    private readonly List<uint>             _muertos = new();

    private Camera _cam;
    private bool   _partidaAnterior;
    private string _escenaActual;

    // Los huesos del Sorken son un one-shot suelto encima del loop de pasos, no un loop:
    // se dispara cada tanto para que el "movimiento quebrado" no suene mecanico.
    private float _proximoHueso;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += AlCargarEscena;
        _escenaActual = SceneManager.GetActiveScene().name;
        AplicarMusicaDeEscena();
    }

    private void OnDisable() => SceneManager.sceneLoaded -= AlCargarEscena;

    private void AlCargarEscena(Scene s, LoadSceneMode modo)
    {
        _escenaActual = s.name;
        _previo.Clear();          // las entidades de la escena anterior ya no existen
        _partidaAnterior = false;
        AplicarMusicaDeEscena();
    }

    // Musica de fondo segun donde estemos. En la escena de juego la musica de partida la
    // decide ActualizarMusicaPartida (depende de si arranco la noche), no esto.
    private void AplicarMusicaDeEscena()
    {
        if (_escenaActual == SceneFlow.EscenaMenu) AudioManager.Musica(c => c.musicaMenu);
        else if (_escenaActual == SceneFlow.EscenaEscaner) AudioManager.PararMusica();
    }

    private Camera Cam
    {
        get
        {
            if (_cam == null) _cam = Camera.main;
            return _cam;
        }
    }

    private void Update()
    {
        if (!AudioManager.Activo) return;

        ActualizarMusicaPartida();
        ActualizarTension();
        RecorrerEntidades();
    }

    // US-11.3: la capa ambiental sube y baja con el nivel de peligro que ya calcula
    // TensionSystem (cordura + Arbmos presente + proximidad del Sorken).
    private void ActualizarTension()
    {
        var t = Gameplay.TensionSystem.Instance;
        AudioManager.CapaTension(t != null ? t.Value01 : 0f);
    }

    private void ActualizarMusicaPartida()
    {
        if (_escenaActual != SceneFlow.EscenaJuego) return;

        bool enPartida = NetworkManager.Instance != null && NetworkManager.Instance.GameStarted;
        if (enPartida == _partidaAnterior) return;

        _partidaAnterior = enPartida;
        if (enPartida) AudioManager.Musica(c => c.ambienteNoche);
        else           AudioManager.PararMusica();
    }

    private void RecorrerEntidades()
    {
        var reg = EntityRegistry.Instance;
        if (reg == null)
        {
            if (_previo.Count > 0) LimpiarTodo();
            return;
        }

        _vistos.Clear();
        bool sorkenPersiguiendo = false;
        bool velethCazando      = false;

        foreach (var e in reg.All)
        {
            if (e == null) continue;
            _vistos.Add(e.NetworkId);

            switch (e.EntityTypeId)
            {
                case EntityTypeIds.Sorken:
                    if (Sorken(e, out bool persiguiendo)) sorkenPersiguiendo |= persiguiendo;
                    break;
                case EntityTypeIds.Veleth:
                    if (Veleth(e, out bool cazando)) velethCazando |= cazando;
                    break;
                case EntityTypeIds.Arbmos:
                    Arbmos(e);
                    break;
                default:
                    if (e.EntityTypeId >= EntityTypeIds.BatteryBase) Bateria(e);
                    break;
            }
        }

        // Loops que dependen de que la entidad siga existiendo y en el estado correcto.
        if (!sorkenPersiguiendo) AudioManager.PararBucle(BuclePasos);
        if (!velethCazando)      AudioManager.PararBucle(BucleVeleth);

        BarrerDespawns();
    }

    // ─────────────────────────────────────────────────────────── Sorken
    private bool Sorken(NetworkEntity e, out bool persiguiendo)
    {
        persiguiendo = false;
        var s = e.GetComponent<SorkenEntity>();
        if (s == null) return false;

        bool nuevo = !_previo.TryGetValue(e.NetworkId, out var antes);
        int  estado = (int)s.State;

        if (nuevo)
        {
            // Aparece asomando por un punto de entrada: US-4.1, el ruido depende del TIPO
            // de punto (puerta/ventana/...) y varia al azar entre los clips de ese tipo.
            byte idx = s.MarkerTypeIndex;
            AudioManager.Sonar(c => c.EntradaPorIndice(idx), s.Position);
        }
        else if (antes.estado != estado)
        {
            switch (s.State)
            {
                case SorkenState.Chasing:    AudioManager.Sonar(c => c.sorkenEntra,    s.Position); break;
                case SorkenState.Grabbing:   AudioManager.Sonar(c => c.sorkenGrab,     s.Position); break;
                case SorkenState.Retreating: AudioManager.Sonar(c => c.sorkenRepelido, s.Position); break;
            }
        }

        if (s.State == SorkenState.Chasing)
        {
            persiguiendo = true;
            // Doc: "pasos cuyo volumen aumenta exponencialmente al acercarse". La curva
            // cuadratica va ENCIMA de la atenuacion 3D, asi los ultimos metros pegan fuerte.
            float cerca = Cercania(s.Position, 12f);
            AudioManager.Bucle(BuclePasos, c => c.sorkenPasos, s.Position, cerca * cerca);

            // Huesos: cada 0.6-1.4 s, no por frame. Mas seguido cuanto mas cerca.
            if (Time.time >= _proximoHueso)
            {
                AudioManager.Sonar(c => c.sorkenHuesos, s.Position, Mathf.Max(0.25f, cerca));
                _proximoHueso = Time.time + Random.Range(0.6f, 1.4f) * (1.4f - cerca * 0.5f);
            }
        }

        _previo[e.NetworkId] = new Foto { tipo = e.EntityTypeId, estado = estado };
        return true;
    }

    // ─────────────────────────────────────────────────────────── Veleth
    private bool Veleth(NetworkEntity e, out bool cazando)
    {
        cazando = false;
        var v = e.GetComponent<VelethEntity>();
        if (v == null) return false;

        bool nuevo = !_previo.TryGetValue(e.NetworkId, out var antes);
        int  estado = (int)v.State;

        // La invocacion NO se dispara aca: ya la toca VelethNetwork.OnNetworkSpawn, que es
        // el hook que el juego tenia desde antes (y que ahora sale del catalogo).
        if (!nuevo && antes.estado != estado && v.State == VelethState.Grabbing)
            AudioManager.Sonar(c => c.velethGrab, v.Position);

        if (v.State == VelethState.Hunting)
        {
            cazando = true;
            AudioManager.Bucle(BucleVeleth, c => c.velethPersecucion, v.Position,
                               Cercania(v.Position, 20f));
        }

        _previo[e.NetworkId] = new Foto { tipo = e.EntityTypeId, estado = estado };
        return true;
    }

    // ─────────────────────────────────────────────────────────── Arbmos
    private void Arbmos(NetworkEntity e)
    {
        var a = e.GetComponent<ArbmosEntity>();
        if (a == null) return;

        // En el host conviven copias OCULTAS del Arbmos de los otros jugadores (la
        // alucinacion es individual). Sin este filtro el host escucharia las ajenas.
        if (!a.Rendered)
        {
            _previo.Remove(e.NetworkId);
            return;
        }

        bool nuevo  = !_previo.TryGetValue(e.NetworkId, out var antes);
        int  estado = (int)a.State;

        if (nuevo)
        {
            AudioManager.Sonar(c => c.arbmosAparece, a.Position);
        }
        else
        {
            if (!antes.letal && a.Lethal)
                AudioManager.Sonar(c => c.arbmosAparece, a.Position);
            if (antes.estado != estado && a.State == ArbmosState.Chasing && a.Lethal)
                AudioManager.Sonar(c => c.arbmosEmbestida, a.Position);
        }

        // Drenaje de cordura: suena mientras te movés con el Arbmos presente.
        if (!a.Lethal && a.State == ArbmosState.Running)
            AudioManager.Bucle(BucleDrena, c => c.arbmosDrena, a.Position);
        else
            AudioManager.PararBucle(BucleDrena);

        // US-6.4: susurros y gritos ESCALABLES. Distort01 es la misma rampa que ya usa la
        // distorsion de lente, asi que imagen y sonido escalan juntos: arranca en susurros
        // y termina en gritos justo antes del jumpscare.
        bool jumpscareHecho = !nuevo && antes.jumpscareHecho;
        if (a.Lethal)
        {
            float d = Mathf.Clamp01(a.Distort01);
            AudioManager.Bucle(BucleSusurros, c => c.arbmosSusurros, a.Position, 1f - d * 0.5f);
            AudioManager.Bucle(BucleGritos,   c => c.arbmosGritos,   a.Position, d * d);

            if (!jumpscareHecho && d >= 0.99f)
            {
                AudioManager.Sonar(c => c.arbmosJumpscare);
                jumpscareHecho = true;
            }
        }
        else
        {
            AudioManager.PararBucle(BucleSusurros);
            AudioManager.PararBucle(BucleGritos);
        }

        _previo[e.NetworkId] = new Foto
        {
            tipo = e.EntityTypeId, estado = estado,
            letal = a.Lethal, jumpscareHecho = jumpscareHecho,
        };
    }

    // ─────────────────────────────────────────────────────────── Pilas
    private void Bateria(NetworkEntity e)
    {
        if (_previo.ContainsKey(e.NetworkId)) return;   // ya la conocíamos
        AudioManager.Sonar(c => c.pilaAparece, e.transform.position);
        _previo[e.NetworkId] = new Foto { tipo = e.EntityTypeId, estado = 0 };
    }

    // ─────────────────────────────────────────────────────── Despawns
    private void BarrerDespawns()
    {
        _muertos.Clear();
        foreach (var kv in _previo)
            if (!_vistos.Contains(kv.Key)) _muertos.Add(kv.Key);

        for (int i = 0; i < _muertos.Count; i++)
        {
            var foto = _previo[_muertos[i]];
            if (foto.tipo == EntityTypeIds.Arbmos)
            {
                AudioManager.PararBucle(BucleDrena);
                AudioManager.PararBucle(BucleSusurros);
                AudioManager.PararBucle(BucleGritos);
            }
            _previo.Remove(_muertos[i]);
        }
    }

    private void LimpiarTodo()
    {
        _previo.Clear();
        AudioManager.PararBucle(BuclePasos);
        AudioManager.PararBucle(BucleVeleth);
        AudioManager.PararBucle(BucleDrena);
        AudioManager.PararBucle(BucleSusurros);
        AudioManager.PararBucle(BucleGritos);
    }

    // 0 lejos .. 1 encima del jugador. Se usa para modular loops por distancia por encima
    // de la atenuacion 3D del motor.
    private float Cercania(Vector3 pos, float alcance)
    {
        var cam = Cam;
        if (cam == null) return 1f;
        float d = Vector3.Distance(cam.transform.position, pos);
        return Mathf.Clamp01(1f - d / Mathf.Max(0.1f, alcance));
    }
}
