using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class TcpTransportServer
{
    public struct IncomingMsg
    {
        public uint        ClientId;
        public MessageType Type;
        public byte[]      Body;
    }

    private TcpListener _listener;
    private readonly ConcurrentDictionary<uint, TcpClient>     _clients = new();
    // Una cola de salida (con su hilo escritor) por cliente: Send nunca bloquea el hilo
    // principal, por lenta que sea la red de ESE cliente (ver ColaSalida).
    private readonly ConcurrentDictionary<uint, ColaSalida>    _salidas = new();
    private readonly ConcurrentQueue<IncomingMsg>               _inbox   = new();
    private readonly ConcurrentQueue<uint>                      _newConns = new();
    private readonly ConcurrentQueue<uint>                      _lostConns = new();
    private uint _nextClientId = 1;
    private volatile bool _running;

    // Tope de espera en Stop para que el último aviso (SessionEnded) llegue a salir.
    private const int EsperaVaciadoAlCerrarMs = 300;

    public int Start(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _running  = true;
        new Thread(AcceptLoop) { IsBackground = true }.Start();
        int boundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Debug.Log($"[Server] Listening on :{boundPort}");
        return boundPort;
    }

    public void Stop()
    {
        _running = false;
        _listener?.Stop();
        _listener = null;

        // Dejar salir lo encolado (el SessionEnded que acaba de difundir Shutdown) con un
        // tope corto: un cliente con la red muerta no puede demorar el cambio de escena.
        int limite = Environment.TickCount + EsperaVaciadoAlCerrarMs;
        foreach (var s in _salidas.Values)
            s.EsperarVaciado(Math.Max(0, limite - Environment.TickCount));
        foreach (var s in _salidas.Values) s.Detener();
        foreach (var c in _clients.Values) c.Close();
        _clients.Clear();
        _salidas.Clear();
    }

    // Call from main thread
    public bool TryDequeueNewConnection(out uint clientId)    => _newConns.TryDequeue(out clientId);
    public bool TryDequeueDisconnection(out uint clientId)    => _lostConns.TryDequeue(out clientId);
    public bool TryDequeue(out IncomingMsg msg)               => _inbox.TryDequeue(out msg);

    // No bloquea: encola en la salida del cliente. Si el socket muere escribiendo, el
    // hilo escritor encola la baja (TryDequeueDisconnection), igual que antes el catch.
    public void Send(uint clientId, byte[] framed)
    {
        if (_salidas.TryGetValue(clientId, out var s)) s.Encolar(framed);
    }

    public void Broadcast(byte[] framed, uint exclude = 0)
    {
        foreach (var kv in _salidas)
            if (kv.Key != exclude) kv.Value.Encolar(framed);
    }

    // Saca al cliente y cierra su socket en el acto (lo pendiente se pierde).
    public void RemoveClient(uint clientId)
    {
        if (_salidas.TryRemove(clientId, out var s)) s.Detener();
        if (_clients.TryRemove(clientId, out var c)) c.Close();
    }

    // Saca al cliente pero deja salir lo que ya tenía encolado (p. ej. el RoomFull) antes
    // de cerrar. Con un tope: un socket que no avanza se cierra igual.
    public void RemoveClientTrasVaciar(uint clientId, int topeMs = 2000)
    {
        _salidas.TryRemove(clientId, out var s);
        if (!_clients.TryRemove(clientId, out var c)) { s?.Detener(); return; }
        if (s == null) { c.Close(); return; }
        s.CerrarTrasVaciar();   // al terminar de vaciar cierra el socket (alCerrar)
        ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(topeMs); s.Detener(); c.Close(); });
    }

    public IEnumerable<uint> ConnectedClientIds => _clients.Keys;

    // Mensajes descartados por red lenta hacia un cliente (diagnóstico).
    public int Descartados(uint clientId) =>
        _salidas.TryGetValue(clientId, out var s) ? s.Descartados : 0;

    // ── background threads ────────────────────────────────────────────────

    private void AcceptLoop()
    {
        while (_running)
        {
            try
            {
                var  tcp      = _listener.AcceptTcpClient();
                // Sin NoDelay, Nagle agrupa los paquetes chicos y le mete hasta ~200 ms
                // de retardo al chat de voz (frames de 30 ms) y al input.
                tcp.NoDelay   = true;
                uint clientId = _nextClientId++;
                var stream    = tcp.GetStream();
                _clients[clientId] = tcp;
                _salidas[clientId] = new ColaSalida(stream,
                    alFallar: () => _lostConns.Enqueue(clientId),
                    alCerrar: () => tcp.Close(),
                    nombre:   $"Server-Send-{clientId}");
                _newConns.Enqueue(clientId);
                new Thread(() => ReadLoop(clientId, tcp)) { IsBackground = true }.Start();
            }
            catch (SocketException) when (!_running) { break; }
            catch (ObjectDisposedException) when (!_running) { break; }
            catch (Exception e) { Debug.LogWarning($"[Server] Accept: {e.Message}"); }
        }
    }

    private void ReadLoop(uint clientId, TcpClient tcp)
    {
        try
        {
            var stream = tcp.GetStream();
            while (_running && tcp.Connected)
            {
                var (type, body) = MessageFramer.ReadOne(stream);
                _inbox.Enqueue(new IncomingMsg { ClientId = clientId, Type = type, Body = body });
            }
        }
        catch { }
        finally { _lostConns.Enqueue(clientId); }
    }
}
