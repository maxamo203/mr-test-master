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
    // Claves de los loops (ver AudioManager.Bucle). Las pisadas son contactos discretos
    // emitidos por SorkenAnimator y consumidos por este watcher.
    private const string BucleVeleth     = "veleth_persecucion_espectral";
    private const string FaseVelethSalida = "veleth_salida_libro";
    private const string FaseSorkenCover = "sorken_cubrirse_";
    private const string FaseSorkenMarcha = "sorken_marcha_cubierta_";
    private const float GananciaPasosSorken = 3f;

    // Foto del frame anterior de una entidad, para detectar transiciones.
    private struct Foto
    {
        public byte tipo;
        public int  estado;
        public bool letal;
        public bool entradaSonada;
        public bool emergiendo;
    }

    private readonly Dictionary<uint, Foto> _previo  = new();
    private readonly HashSet<uint>          _vistos  = new();
    private readonly List<uint>             _muertos = new();
    private readonly Dictionary<uint, int>  _ultimaPisada = new();
    private readonly Dictionary<uint, int>  _ultimaPisadaArbmos = new();
    private readonly Dictionary<uint, int>  _ultimoApoyoVeleth = new();
    private readonly Dictionary<uint, int>  _ultimoRoceVeleth = new();
    private readonly Dictionary<uint, int>  _ultimaVozCubierta = new();
    private readonly Dictionary<uint, float> _proximaVozCubierta = new();

    private Camera _cam;
    private bool   _partidaAnterior;
    private string _escenaActual;

    private float _proximoHueso;
    private float _proximaExhalacionArbmos;
    private int _ultimoEnfadoArbmos = -1;
    private int _alternanciaCubrirse;

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

        // Si el jugador local murio, el coro de la noche conserva exactamente la misma
        // fuente y posicion temporal durante game over y espectador.
        bool enPartida = (NetworkManager.Instance != null && NetworkManager.Instance.GameStarted) ||
                         (Gameplay.LocalDeath.Instance != null && Gameplay.LocalDeath.Instance.IsDead);
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
        bool velethCazando = false;

        foreach (var e in reg.All)
        {
            if (e == null) continue;
            _vistos.Add(e.NetworkId);

            switch (e.EntityTypeId)
            {
                case EntityTypeIds.Sorken:
                    Sorken(e);
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
        if (!velethCazando) AudioManager.PararBucle(BucleVeleth);

        BarrerDespawns();
    }

    // ─────────────────────────────────────────────────────────── Sorken
    private void Sorken(NetworkEntity e)
    {
        var s = e.GetComponent<SorkenEntity>();
        if (s == null) return;

        bool nuevo = !_previo.TryGetValue(e.NetworkId, out var antes);
        int  estado = (int)s.State;

        bool entradaSonada = !nuevo && antes.entradaSonada;
        if (!entradaSonada)
        {
            if (s.State == SorkenState.EmergingDoor)
            {
                // El crujido pertenece al inicio real de la animacion, no al spawn en
                // espera ni a la posterior transicion a persecucion.
                AudioManager.Sonar(c => c.sorkenEntradaPuertaCrujido, s.Position);
                entradaSonada = true;
            }
            else if (s.State == SorkenState.EmergingWindow)
            {
                byte idx = s.MarkerTypeIndex;
                AudioManager.Sonar(c => c.EntradaPorIndice(idx), s.Position);
                entradaSonada = true;
            }
        }

        if (nuevo && s.State == SorkenState.CoverStarting)
        {
            int ignored;
            AudioManager.TransitorioSinRepetir(
                FaseSorkenCover + e.NetworkId, c => c.sorkenCubrirse, s.Position,
                _alternanciaCubrirse == 0 ? 1 : 0, out ignored);
            _alternanciaCubrirse = ignored == 0 ? 1 : 0;
        }
        else if (!nuevo && antes.estado != estado)
        {
            string coverKey = FaseSorkenCover + e.NetworkId;
            string marchKey = FaseSorkenMarcha + e.NetworkId;
            if ((SorkenState)antes.estado == SorkenState.CoverStarting)
                AudioManager.PararTransitorio(coverKey);
            if ((SorkenState)antes.estado == SorkenState.CoverWalking)
            {
                AudioManager.PararTransitorio(marchKey);
                _proximaVozCubierta.Remove(e.NetworkId);
            }

            switch (s.State)
            {
                case SorkenState.Retreating: AudioManager.Sonar(c => c.sorkenRepelido, s.Position); break;
                case SorkenState.CoverStarting:
                    int ignored;
                    AudioManager.TransitorioSinRepetir(
                        coverKey, c => c.sorkenCubrirse, s.Position,
                        _alternanciaCubrirse == 0 ? 1 : 0, out ignored);
                    _alternanciaCubrirse = ignored == 0 ? 1 : 0;
                    break;
                case SorkenState.CoverWalking:
                    _proximaVozCubierta[e.NetworkId] = Time.time + Random.Range(1f, 2f);
                    break;
            }
        }

        if (s.State == SorkenState.CoverWalking)
        {
            string key = FaseSorkenMarcha + e.NetworkId;
            AudioManager.PosicionarTransitorio(key, s.Position);
            if (!_proximaVozCubierta.TryGetValue(e.NetworkId, out float next))
                next = _proximaVozCubierta[e.NetworkId] = Time.time + Random.Range(1f, 2f);
            if (Time.time >= next && !AudioManager.TransitorioActivo(key))
            {
                _ultimaVozCubierta.TryGetValue(e.NetworkId, out int previous);
                if (!_ultimaVozCubierta.ContainsKey(e.NetworkId)) previous = -1;
                float length = AudioManager.TransitorioSinRepetir(
                    key, c => c.sorkenMarchaCubierta, s.Position, previous, out int chosen);
                if (length > 0f)
                {
                    _ultimaVozCubierta[e.NetworkId] = chosen;
                    _proximaVozCubierta[e.NetworkId] = Time.time + length + Random.Range(1.5f, 3f);
                }
            }
        }
        else if (s.State == SorkenState.CoverStarting)
            AudioManager.PosicionarTransitorio(FaseSorkenCover + e.NetworkId, s.Position);

        var animator = e.GetComponent<SorkenAnimator>();
        if (animator != null && animator.TryConsumeFootstep(out Vector3 footPosition,
                                                             out float intensity))
        {
            _ultimaPisada.TryGetValue(e.NetworkId, out int anterior);
            if (!_ultimaPisada.ContainsKey(e.NetworkId)) anterior = -1;
            _ultimaPisada[e.NetworkId] = AudioManager.SonarSinRepetir(
                c => c.sorkenPasos, footPosition, anterior, intensity,
                ganancia: GananciaPasosSorken);
        }

        if (s.State == SorkenState.Chasing)
        {
            // Doc: "pasos cuyo volumen aumenta exponencialmente al acercarse". La curva
            // cuadratica va ENCIMA de la atenuacion 3D, asi los ultimos metros pegan fuerte.
            float cerca = Cercania(s.Position, 12f);

            // Huesos: mas espaciados que los pasos, encima de ellos.
            if (Time.time >= _proximoHueso)
            {
                AudioManager.Sonar(c => c.sorkenHuesos, s.Position, Mathf.Max(0.25f, cerca));
                _proximoHueso = Time.time + Random.Range(0.9f, 2.2f) * (1.4f - cerca * 0.5f);
            }
        }

        _previo[e.NetworkId] = new Foto
        {
            tipo = e.EntityTypeId,
            estado = estado,
            entradaSonada = entradaSonada,
        };
    }

    // ─────────────────────────────────────────────────────────── Veleth
    private bool Veleth(NetworkEntity e, out bool cazando)
    {
        cazando = false;
        var v = e.GetComponent<VelethEntity>();
        if (v == null) return false;

        bool nuevo = !_previo.TryGetValue(e.NetworkId, out var antes);
        int  estado = (int)v.State;

        string emergenceKey = FaseVelethSalida + e.NetworkId;
        if (v.IsEmerging)
        {
            // Disparo por flanco: si el WAV termina unas milésimas antes que la fase no
            // debe volver a arrancar desde cero en el último frame de emergencia.
            if (nuevo || !antes.emergiendo)
            {
                int ignored;
                AudioManager.TransitorioSinRepetir(
                    emergenceKey, c => c.velethSalidaLibro, v.Position, -1, out ignored);
            }
            AudioManager.PosicionarTransitorio(emergenceKey, v.Position);
        }
        else if (nuevo || antes.emergiendo)
            AudioManager.PararTransitorio(emergenceKey, 0.2f);

        while (v.TryConsumeAudioContact(out bool support, out Vector3 contactPosition))
        {
            var memory = support ? _ultimoApoyoVeleth : _ultimoRoceVeleth;
            memory.TryGetValue(e.NetworkId, out int previous);
            if (!memory.ContainsKey(e.NetworkId)) previous = -1;
            memory[e.NetworkId] = AudioManager.SonarSinRepetir(
                support ? (System.Func<AudioCatalog, AudioCatalog.Pista>)(c => c.velethApoyos)
                        : c => c.velethRoces,
                contactPosition, previous, support ? 1f : 0.82f);
        }

        if (v.State == VelethState.Hunting && !v.IsEmerging)
        {
            cazando = true;
            AudioManager.Bucle(BucleVeleth, c => c.velethPersecucionEspectral, v.Position,
                               Cercania(v.Position, 20f));
        }

        _previo[e.NetworkId] = new Foto
        {
            tipo = e.EntityTypeId, estado = estado, emergiendo = v.IsEmerging
        };
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

        if (!nuevo)
        {
            if (antes.estado != estado && a.State == ArbmosState.Attacking)
                _ultimoEnfadoArbmos = AudioManager.SonarSinRepetir(
                    c => c.arbmosEnfado, a.Position, _ultimoEnfadoArbmos);
        }

        // La resonancia acompaña presencia + luz local. Apagarla dispara su caída natural
        // sin modificar desaparición, daño ni locomoción.
        var net = NetworkManager.Instance;
        uint localId = net != null && !net.IsServer ? net.LocalClientId : 0;
        AudioManager.ResonanciaArbmos(a.Position,
            Gameplay.PlayerLights.ModeFor(localId) != FlashlightMode.Off);

        if (nuevo) _proximaExhalacionArbmos = Time.time + Random.Range(0.4f, 1.4f);
        if (Time.time >= _proximaExhalacionArbmos)
        {
            AudioManager.Sonar(c => c.arbmosExhalacion, a.Position);
            _proximaExhalacionArbmos = Time.time + Random.Range(8f, 13f);
        }

        var animator = e.GetComponent<ArbmosAnimator>();
        if (animator != null && animator.TryConsumeFootstep(out Vector3 footPosition,
                                                             out float intensity))
        {
            _ultimaPisadaArbmos.TryGetValue(e.NetworkId, out int previous);
            if (!_ultimaPisadaArbmos.ContainsKey(e.NetworkId)) previous = -1;
            _ultimaPisadaArbmos[e.NetworkId] = AudioManager.SonarSinRepetir(
                c => c.arbmosPasos, footPosition, previous, intensity);
        }

        _previo[e.NetworkId] = new Foto
        {
            tipo = e.EntityTypeId, estado = estado, letal = a.Lethal,
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
                AudioManager.DetenerResonanciaArbmos(salidaNatural: false);
                _ultimaPisadaArbmos.Remove(_muertos[i]);
            }
            if (foto.tipo == EntityTypeIds.Sorken)
            {
                _ultimaPisada.Remove(_muertos[i]);
                _ultimaVozCubierta.Remove(_muertos[i]);
                _proximaVozCubierta.Remove(_muertos[i]);
                AudioManager.PararTransitorio(FaseSorkenCover + _muertos[i]);
                AudioManager.PararTransitorio(FaseSorkenMarcha + _muertos[i]);
            }
            if (foto.tipo == EntityTypeIds.Veleth)
            {
                _ultimoApoyoVeleth.Remove(_muertos[i]);
                _ultimoRoceVeleth.Remove(_muertos[i]);
                AudioManager.PararTransitorio(FaseVelethSalida + _muertos[i]);
            }
            _previo.Remove(_muertos[i]);
        }
    }

    private void LimpiarTodo()
    {
        _previo.Clear();
        _ultimaPisada.Clear();
        _ultimaPisadaArbmos.Clear();
        _ultimoApoyoVeleth.Clear();
        _ultimoRoceVeleth.Clear();
        _ultimaVozCubierta.Clear();
        _proximaVozCubierta.Clear();
        AudioManager.PararBucle(BucleVeleth);
        AudioManager.DetenerResonanciaArbmos(salidaNatural: false);
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
