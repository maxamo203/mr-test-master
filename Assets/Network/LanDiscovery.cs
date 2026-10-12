using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// Descubrimiento de host en LAN por broadcast UDP.
// - Host: llama StartAdvertising(gamePort, name) y emite un paquete cada segundo.
// - Cliente: llama StartListening() y recibe eventos OnHostDiscovered / OnHostLost
//   (siempre invocados en el hilo principal de Unity).
//
// Nota plataforma:
//   * Android: el broadcast a 255.255.255.255 funciona sin permisos extra.
//   * iOS 14+: requiere NSLocalNetworkUsageDescription en Info.plist; sin eso
//     el sistema bloquea silenciosamente el trafico de LAN. Ademas, ENVIAR un
//     broadcast exige el entitlement com.apple.developer.networking.multicast
//     (lo aprueba Apple a mano): sin el, send() falla con "No route to host" y
//     un host iPhone nunca aparecia en la lista de los demas. Por eso en iOS el
//     anuncio tambien sale por unicast a cada IP de la subred (ver Barrido), que
//     solo necesita el permiso de red local.
public class LanDiscovery : MonoBehaviour
{
    public const int DiscoveryPort = 47777;

    private static readonly byte[] Magic = { (byte)'M', (byte)'R', (byte)'T', (byte)'1' };

    public struct DiscoveredHost
    {
        public string Address;   // ip del host (la del paquete recibido)
        public int    GamePort;  // puerto TCP del juego anunciado por el host
        public string Name;      // nombre legible
        public float  LastSeen;  // Time.time del ultimo paquete
    }

    public event Action<DiscoveredHost> OnHostDiscovered;
    public event Action<DiscoveredHost> OnHostLost;

    public IReadOnlyDictionary<string, DiscoveredHost> Hosts => _hosts;

    [Tooltip("Segundos sin recibir paquete antes de considerar perdido un host.")]
    [SerializeField] private float _hostTtl = 5f;
    [Tooltip("Intervalo entre paquetes de anuncio (segundos).")]
    [SerializeField] private float _advertiseInterval = 1f;
    [Tooltip("Intervalo entre barridos unicast de la subred (solo iOS). Debe quedar " +
             "por debajo de Host Ttl para que un paquete perdido no haga parpadear la sala.")]
    [SerializeField] private float _sweepInterval = 2f;

    // Solo iOS: es el unico que no puede ENVIAR broadcast. Recibirlo si puede
    // (verificado: un iPhone cliente ve las salas de un host Android), asi que
    // los demas hosts no necesitan barrer.
#if UNITY_IOS && !UNITY_EDITOR
    private const bool BarridoUnicast = true;
#else
    private const bool BarridoUnicast = false;
#endif
    // Tope de destinos por barrido: una subred mas grande que /24 se recorta a la
    // /24 propia (donde esta casi todo dispositivo de una red domestica).
    private const int MaxDestinosBarrido = 254;

    private readonly Dictionary<string, DiscoveredHost> _hosts   = new();
    private readonly Dictionary<string, DiscoveredHost> _pending = new();

    private UdpClient _advertiser;
    private UdpClient _listener;
    private Thread    _advertiseThread;
    private Thread    _listenThread;
    private volatile bool _running;
    private byte[]    _advertisePacket;

    // ── API publica ───────────────────────────────────────────────────────

    public void StartAdvertising(int gamePort, string hostName)
    {
        StopAll();
        _advertisePacket = BuildPacket(gamePort, hostName);
        _running = true;
        _advertiseThread = new Thread(AdvertiseLoop)
        {
            IsBackground = true,
            Name         = "LanDiscovery-Advertise",
        };
        _advertiseThread.Start();
        Debug.Log($"[LanDiscovery] Anunciando host '{hostName}' :{gamePort} en udp:{DiscoveryPort}");
    }

    public void StartListening()
    {
        StopAll();
        _running = true;
        _listenThread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name         = "LanDiscovery-Listen",
        };
        _listenThread.Start();
        Debug.Log($"[LanDiscovery] Escuchando hosts en udp:{DiscoveryPort}");
    }

    public void StopAll()
    {
        _running = false;
        try { _advertiser?.Close(); } catch { }
        try { _listener?.Close();   } catch { }
        _advertiser = null;
        _listener   = null;

        if (_advertiseThread != null && _advertiseThread.IsAlive) _advertiseThread.Join(500);
        if (_listenThread    != null && _listenThread.IsAlive)    _listenThread.Join(500);
        _advertiseThread = null;
        _listenThread    = null;

        lock (_pending) _pending.Clear();
        _hosts.Clear();
    }

    // ── Hilos ─────────────────────────────────────────────────────────────

    private void AdvertiseLoop()
    {
        try
        {
            _advertiser = new UdpClient { EnableBroadcast = true };
            var ep      = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
            var sleepMs = Mathf.Max(100, Mathf.RoundToInt(_advertiseInterval * 1000f));
            var sweepMs = Mathf.Max(sleepMs, Mathf.RoundToInt(_sweepInterval * 1000f));

            List<IPEndPoint> destinos = BarridoUnicast ? DestinosBarrido() : null;
            if (destinos != null)
                Debug.Log($"[LanDiscovery] Barrido unicast a {destinos.Count} direcciones de la subred");
            int  proximoBarrido   = Environment.TickCount;
            bool avisoBroadcast   = false;

            while (_running)
            {
                try
                {
                    _advertiser.Send(_advertisePacket, _advertisePacket.Length, ep);
                }
                catch (Exception e)
                {
                    // En iOS sin entitlement de multicast falla siempre: avisar una
                    // sola vez, el barrido unicast cubre el anuncio.
                    if (_running && !(BarridoUnicast && avisoBroadcast))
                        Debug.LogWarning($"[LanDiscovery] send: {e.Message}");
                    avisoBroadcast = true;
                }

                if (destinos != null && Environment.TickCount - proximoBarrido >= 0)
                {
                    proximoBarrido = Environment.TickCount + sweepMs;
                    foreach (var d in destinos)
                    {
                        if (!_running) break;
                        // IPs sin nadie detras fallan (EHOSTDOWN/EHOSTUNREACH tras
                        // el ARP): es lo esperado en un barrido, se ignora.
                        try { _advertiser.Send(_advertisePacket, _advertisePacket.Length, d); }
                        catch { }
                    }
                }
                Thread.Sleep(sleepMs);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[LanDiscovery] advertiser fatal: {e}");
        }
    }

    private void ListenLoop()
    {
        try
        {
            _listener = new UdpClient();
            _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

            var from = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                byte[] data;
                try
                {
                    data = _listener.Receive(ref from);
                }
                catch (SocketException)
                {
                    if (!_running) break;
                    continue;
                }
                catch (ObjectDisposedException) { break; }

                if (!TryParsePacket(data, out int gamePort, out string name)) continue;

                var key = from.Address.ToString();
                var h   = new DiscoveredHost
                {
                    Address  = key,
                    GamePort = gamePort,
                    Name     = name,
                    LastSeen = 0f, // se asigna en main thread
                };
                lock (_pending) _pending[key] = h;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[LanDiscovery] listener fatal: {e}");
        }
    }

    // ── Barrido unicast (iOS) ─────────────────────────────────────────────
    // Todas las IPs de host de las subredes privadas en las que esta el
    // dispositivo (Wi-Fi en0, hotspot propio bridge*), sin la propia ni las de
    // red/broadcast. Se calcula una vez al empezar a anunciar.

    private static readonly string[] InterfacesExcluidas = { "pdp_ip", "utun", "ipsec", "awdl", "llw" };

    private static List<IPEndPoint> DestinosBarrido()
    {
        var destinos = new List<IPEndPoint>();
        var vistos   = new HashSet<uint>();
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                string nombre = (ni.Name ?? "").ToLowerInvariant();
                bool excluida = false;
                foreach (var pre in InterfacesExcluidas)
                    if (nombre.StartsWith(pre)) { excluida = true; break; }
                if (excluida) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var b = ua.Address.GetAddressBytes();
                    bool privada = b[0] == 10 || (b[0] == 192 && b[1] == 168) ||
                                   (b[0] == 172 && b[1] >= 16 && b[1] <= 31);
                    if (!privada) continue;

                    // Mascara real si la plataforma la da; si no, /24. Mas ancha que
                    // /24 se recorta a la /24 propia (ver MaxDestinosBarrido).
                    int prefijo = 24;
                    try
                    {
                        var m = ua.IPv4Mask;
                        if (m != null)
                        {
                            uint mascara = ToUInt(m.GetAddressBytes());
                            if (mascara != 0) prefijo = CountBits(mascara);
                        }
                    }
                    catch { /* IPv4Mask no implementado en la plataforma */ }
                    if (prefijo < 24) prefijo = 24;
                    if (prefijo > 30) continue; // /31 y /32: no hay otros hosts

                    uint propia = ToUInt(b);
                    uint masc   = 0xFFFFFFFFu << (32 - prefijo);
                    uint red    = propia & masc;
                    uint bcast  = red | ~masc;
                    for (uint ip = red + 1; ip < bcast && destinos.Count < MaxDestinosBarrido * 2; ip++)
                    {
                        if (ip == propia || !vistos.Add(ip)) continue;
                        destinos.Add(new IPEndPoint(new IPAddress(new[]
                        {
                            (byte)(ip >> 24), (byte)(ip >> 16), (byte)(ip >> 8), (byte)ip,
                        }), DiscoveryPort));
                    }
                }
            }
        }
        catch (Exception e) { Debug.LogWarning($"[LanDiscovery] barrido: {e.Message}"); }
        return destinos;
    }

    private static uint ToUInt(byte[] b) =>
        ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];

    private static int CountBits(uint v)
    {
        int n = 0;
        while (v != 0) { n += (int)(v & 1); v >>= 1; }
        return n;
    }

    // ── Main-thread bookkeeping ───────────────────────────────────────────

    private void Update()
    {
        lock (_pending)
        {
            if (_pending.Count > 0)
            {
                foreach (var kv in _pending)
                {
                    var h = kv.Value;
                    h.LastSeen = Time.time;
                    bool isNew = !_hosts.ContainsKey(kv.Key);
                    _hosts[kv.Key] = h;
                    if (isNew) OnHostDiscovered?.Invoke(h);
                }
                _pending.Clear();
            }
        }

        List<string> toRemove = null;
        foreach (var kv in _hosts)
        {
            if (Time.time - kv.Value.LastSeen > _hostTtl)
                (toRemove ??= new List<string>()).Add(kv.Key);
        }
        if (toRemove != null)
        {
            foreach (var k in toRemove)
            {
                var lost = _hosts[k];
                _hosts.Remove(k);
                OnHostLost?.Invoke(lost);
            }
        }
    }

    private void OnDestroy()        => StopAll();
    private void OnApplicationQuit() => StopAll();

    // ── Formato de paquete ────────────────────────────────────────────────
    // [4 magic 'MRT1'][2 port big-endian][1 nameLen][name utf8 (<=64)]

    private static byte[] BuildPacket(int gamePort, string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name ?? "");
        if (nameBytes.Length > 64) Array.Resize(ref nameBytes, 64);

        var buf = new byte[Magic.Length + 2 + 1 + nameBytes.Length];
        Buffer.BlockCopy(Magic, 0, buf, 0, Magic.Length);
        buf[Magic.Length]     = (byte)((gamePort >> 8) & 0xFF);
        buf[Magic.Length + 1] = (byte)(gamePort & 0xFF);
        buf[Magic.Length + 2] = (byte)nameBytes.Length;
        Buffer.BlockCopy(nameBytes, 0, buf, Magic.Length + 3, nameBytes.Length);
        return buf;
    }

    private static bool TryParsePacket(byte[] data, out int gamePort, out string name)
    {
        gamePort = 0;
        name     = null;
        if (data == null || data.Length < Magic.Length + 3) return false;
        for (int i = 0; i < Magic.Length; i++)
            if (data[i] != Magic[i]) return false;

        gamePort   = (data[Magic.Length] << 8) | data[Magic.Length + 1];
        int nameLn = data[Magic.Length + 2];
        if (Magic.Length + 3 + nameLn > data.Length) return false;
        name = Encoding.UTF8.GetString(data, Magic.Length + 3, nameLn);
        return true;
    }
}
