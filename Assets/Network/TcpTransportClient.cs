using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class TcpTransportClient
{
    public struct IncomingMsg
    {
        public MessageType Type;
        public byte[]      Body;
    }

    private TcpClient     _tcp;
    private NetworkStream _stream;
    private ColaSalida    _salida;
    private readonly ConcurrentQueue<IncomingMsg> _inbox = new();
    private volatile bool _connected;

    public bool IsConnected => _connected;

    // ── Conexión asíncrona ──
    // Antes Connect era sincrónico en el hilo principal y sin timeout propio: con una IP
    // a la que no se llega (otra red, el hotspot que no enruta, un host que no responde)
    // la app quedaba congelada hasta que el sistema se rendía (~20 s en Android, hasta
    // ~75 s en iOS), y la excepción que tiraba al final dejaba la sesión a medio armar.
    // Ahora el connect corre en un hilo con tope y el resultado se consulta por estado.
    public enum EstadoConexion { SinIniciar, Conectando, Conectado, Fallo }

    private volatile EstadoConexion _estado = EstadoConexion.SinIniciar;
    public EstadoConexion Estado => _estado;

    // Motivo de la falla (válido con Estado == Fallo). TimedOut si venció el tope.
    public SocketError ErrorConexion { get; private set; } = SocketError.Success;

    // Frame que el hilo de lectura está recibiendo AHORA. Se escribe desde ese hilo y
    // se lee desde el principal sólo para UI: que los tres valores no sean atómicos
    // entre sí a lo sumo muestra un porcentaje desfasado un frame.
    private volatile int _frameTipo = -1, _frameLeidos, _frameTotal;

    // Sube con cada chunk recibido, aunque el frame todavía no esté completo. Un
    // MapData grande en una red lenta puede tardar más que el timeout del host:
    // mientras esto se mueva, el host sigue vivo.
    private volatile int _actividad;
    public int Actividad => _actividad;

    // Cacheado: ReadOne lo invoca por chunk de cada frame (voz incluida).
    private readonly Action<MessageType, int, int> _onProgreso;

    public TcpTransportClient() => _onProgreso = OnProgreso;

    private void OnProgreso(MessageType tipo, int leidos, int total)
    {
        _frameTipo   = (int)tipo;
        _frameTotal  = total;
        _frameLeidos = leidos;
        _actividad++;   // un solo escritor: el hilo de lectura
    }

    // ¿Se está recibiendo (sin completar) un frame de este tipo? leídos / total del cuerpo.
    public bool Recibiendo(MessageType tipo, out int leidos, out int total)
    {
        leidos = _frameLeidos;
        total  = _frameTotal;
        return _frameTipo == (int)tipo && leidos < total;
    }

    // Arranca la conexión en segundo plano. No bloquea: consultar Estado cada frame.
    public void ConnectAsync(string host, int port, int timeoutMs)
    {
        if (_estado != EstadoConexion.SinIniciar) return;
        _estado = EstadoConexion.Conectando;
        var tcp = new TcpClient();
        // Ver TcpTransportServer: Nagle agruparía los frames de voz (30 ms) y sumaría
        // retardo perceptible a la conversación.
        tcp.NoDelay = true;
        _tcp = tcp;

        new Thread(() =>
        {
            try
            {
                var tarea = tcp.ConnectAsync(host, port);
                if (!tarea.Wait(timeoutMs))
                {
                    // Cerrar el socket aborta el connect pendiente.
                    tarea.ContinueWith(t => { var _ = t.Exception; },
                        System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                    tcp.Close();
                    Fallar(SocketError.TimedOut, host, port);
                    return;
                }
                if (_estado != EstadoConexion.Conectando) { tcp.Close(); return; }   // Disconnect() durante el intento

                _stream = tcp.GetStream();
                _salida = new ColaSalida(_stream,
                    alFallar: () => _connected = false,
                    alCerrar: null,
                    nombre:   "Client-Send");
                _connected = true;
                _estado    = EstadoConexion.Conectado;
                new Thread(ReadLoop) { IsBackground = true, Name = "Client-Read" }.Start();
                Debug.Log($"[Client] Connected to {host}:{port}");
            }
            catch (Exception e)
            {
                tcp.Close();
                Fallar(CodigoDe(e), host, port);
            }
        }) { IsBackground = true, Name = "Client-Connect" }.Start();
    }

    private void Fallar(SocketError codigo, string host, int port)
    {
        if (_estado != EstadoConexion.Conectando) return;
        ErrorConexion = codigo;
        _estado       = EstadoConexion.Fallo;
        Debug.LogWarning($"[Client] No se pudo conectar a {host}:{port} ({codigo})");
    }

    private static SocketError CodigoDe(Exception e)
    {
        for (var x = e; x != null; x = x.InnerException)
        {
            if (x is SocketException se) return se.SocketErrorCode;
            if (x is AggregateException ae && ae.InnerExceptions.Count > 0)
                return CodigoDe(ae.InnerExceptions[0]);
        }
        return SocketError.SocketError;
    }

    public void Disconnect()
    {
        _connected = false;
        if (_estado == EstadoConexion.Conectando) _estado = EstadoConexion.Fallo;
        _salida?.Detener();
        _tcp?.Close();
    }

    public bool TryDequeue(out IncomingMsg msg) => _inbox.TryDequeue(out msg);

    // No bloquea (ver ColaSalida). Si el socket muere escribiendo, IsConnected baja.
    public void Send(byte[] framed)
    {
        if (!_connected) return;
        _salida?.Encolar(framed);
    }

    private void ReadLoop()
    {
        try
        {
            while (_connected)
            {
                var (type, body) = MessageFramer.ReadOne(_stream, _onProgreso);
                _inbox.Enqueue(new IncomingMsg { Type = type, Body = body });
            }
        }
        catch { }
        finally { _connected = false; }
    }
}
