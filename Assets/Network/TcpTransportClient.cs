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

    public void Connect(string host, int port)
    {
        _tcp    = new TcpClient();
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
                var (type, body) = MessageFramer.ReadOne(_stream);
                _inbox.Enqueue(new IncomingMsg { Type = type, Body = body });
            }
        }
        catch { }
        finally { _connected = false; }
    }
}
