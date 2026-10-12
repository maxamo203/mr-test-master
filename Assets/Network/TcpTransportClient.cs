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
    private readonly ConcurrentQueue<IncomingMsg> _inbox = new();
    private volatile bool _connected;

    public bool IsConnected => _connected;

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

    public void Connect(string host, int port)
    {
        _tcp = new TcpClient();
        // Ver TcpTransportServer: Nagle agruparía los frames de voz (30 ms) y sumaría
        // retardo perceptible a la conversación.
        _tcp.NoDelay = true;
        _tcp.Connect(host, port);
        _stream    = _tcp.GetStream();
        _connected = true;
        new Thread(ReadLoop) { IsBackground = true }.Start();
        Debug.Log($"[Client] Connected to {host}:{port}");
    }

    public void Disconnect()
    {
        _connected = false;
        _tcp?.Close();
    }

    public bool TryDequeue(out IncomingMsg msg) => _inbox.TryDequeue(out msg);

    public void Send(byte[] framed)
    {
        if (!_connected) return;
        try { MessageFramer.Write(_stream, framed); }
        catch (Exception e)
        {
            _connected = false;
            Debug.LogWarning($"[Client] Send failed: {e.Message}");
        }
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
