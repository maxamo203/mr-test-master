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
    private readonly ConcurrentDictionary<uint, NetworkStream>  _streams = new();
    private readonly ConcurrentQueue<IncomingMsg>               _inbox   = new();
    private readonly ConcurrentQueue<uint>                      _newConns = new();
    private readonly ConcurrentQueue<uint>                      _lostConns = new();
    private uint _nextClientId = 1;
    private volatile bool _running;

    public void Start(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _running  = true;
        new Thread(AcceptLoop) { IsBackground = true }.Start();
        Debug.Log($"[Server] Listening on :{port}");
    }

    public void Stop()
    {
        _running = false;
        _listener?.Stop();
        foreach (var c in _clients.Values) c.Close();
    }

    // Call from main thread
    public bool TryDequeueNewConnection(out uint clientId)    => _newConns.TryDequeue(out clientId);
    public bool TryDequeueDisconnection(out uint clientId)    => _lostConns.TryDequeue(out clientId);
    public bool TryDequeue(out IncomingMsg msg)               => _inbox.TryDequeue(out msg);

    public void Send(uint clientId, byte[] framed)
    {
        if (_streams.TryGetValue(clientId, out var s))
        {
            try { MessageFramer.Write(s, framed); }
            catch { _lostConns.Enqueue(clientId); }
        }
    }

    public void Broadcast(byte[] framed, uint exclude = 0)
    {
        foreach (var kv in _clients)
            if (kv.Key != exclude) Send(kv.Key, framed);
    }

    public void RemoveClient(uint clientId)
    {
        if (_clients.TryRemove(clientId, out var c)) c.Close();
        _streams.TryRemove(clientId, out _);
    }

    public IEnumerable<uint> ConnectedClientIds => _clients.Keys;

    // ── background threads ────────────────────────────────────────────────

    private void AcceptLoop()
    {
        while (_running)
        {
            try
            {
                var  tcp      = _listener.AcceptTcpClient();
                uint clientId = _nextClientId++;
                _clients[clientId] = tcp;
                _streams[clientId] = tcp.GetStream();
                _newConns.Enqueue(clientId);
                new Thread(() => ReadLoop(clientId, tcp)) { IsBackground = true }.Start();
            }
            catch (SocketException) when (!_running) { break; }
            catch (Exception e) { Debug.LogWarning($"[Server] Accept: {e.Message}"); }
        }
    }

    private void ReadLoop(uint clientId, TcpClient tcp)
    {
        var stream = tcp.GetStream();
        try
        {
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
