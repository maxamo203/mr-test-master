using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

// Cola de salida de UN socket, con su propio hilo escritor.
//
// Antes cada Send escribía en el NetworkStream desde el hilo principal. Mientras la red
// anda bien, Write vuelve enseguida (copia al buffer del kernel); pero en una red
// congestionada (una exposición con decenas de Wi-Fi, un hotspot con mala señal) el
// buffer se llena y Write BLOQUEA hasta que el otro lado confirme datos:
//   * un MapData de algunos MB a un cliente lento congelaba el juego del host;
//   * un celular que se alejaba del Wi-Fi congelaba al host para TODOS (voz + WorldState
//     llenaban el buffer y el hilo principal se quedaba trabado en ese Write, sin llegar
//     nunca al chequeo de timeout que lo hubiera dado de baja);
//   * con el host congelado no salían los Heartbeat y los demás clientes se iban por
//     timeout ("SE PERDIÓ LA CONEXIÓN").
// Ahora Encolar no bloquea nunca: el hilo escritor vacía la cola en orden.
//
// Bajo presión (cola con más de LimiteDescartables bytes esperando) se descartan los
// mensajes que el siguiente reemplaza solo — voz, WorldState, PlayerPose, PlayerInput —
// en vez de acumular segundos de retraso. Lo demás (spawns, StartGame, mapa...) nunca
// se descarta. Además los frames chicos que esperan juntos salen en un solo Write:
// con NoDelay cada frame era un segmento TCP propio, y en un canal saturado menos
// paquetes es menos contención.
public sealed class ColaSalida
{
    // Bytes esperando en la cola por encima de los cuales se descartan los mensajes
    // "de estado" (ver Descartable). ~32 KB ≈ 2-3 s de voz ADPCM: más que eso ya no es
    // conversación, es eco viejo.
    public const int LimiteDescartables = 32 * 1024;

    // Tamaño de cada Write: los frames chicos se agrupan hasta acá y los grandes (el
    // MapData) se parten en trozos de este tamaño.
    private const int Trozo = 16 * 1024;

    private readonly NetworkStream _stream;
    private readonly Action        _alFallar;   // el socket murió escribiendo
    private readonly Action        _alCerrar;   // se terminó de vaciar tras CerrarTrasVaciar
    private readonly object        _lock = new();
    private readonly Queue<byte[]> _cola = new();
    private readonly byte[]        _agrupado = new byte[Trozo];
    private int  _bytesEnCola;
    private bool _cerrando;   // vaciar lo que queda y cerrar
    private bool _detenida;   // cortar ya, sin vaciar

    // Mensajes descartados por la red lenta (sólo diagnóstico).
    public int Descartados { get; private set; }

    public ColaSalida(NetworkStream stream, Action alFallar, Action alCerrar, string nombre)
    {
        _stream   = stream;
        _alFallar = alFallar;
        _alCerrar = alCerrar;
        new Thread(Bucle) { IsBackground = true, Name = nombre }.Start();
    }

    // Mensajes que el siguiente del mismo tipo reemplaza: perder uno sólo se nota como
    // un salto, nunca como un estado roto.
    private static bool Descartable(MessageType t) =>
        t == MessageType.VoiceData || t == MessageType.WorldState ||
        t == MessageType.PlayerPose || t == MessageType.PlayerInput;

    private static MessageType Tipo(byte[] framed) =>
        framed.Length >= 2 ? (MessageType)BitConverter.ToUInt16(framed, 0) : 0;

    // No bloquea. El arreglo no se copia: quien llama no debe modificarlo después (todos
    // los frames salen de MsgHelper.Frame, que crea uno nuevo cada vez).
    public void Encolar(byte[] framed)
    {
        if (framed == null || framed.Length == 0) return;
        lock (_lock)
        {
            if (_detenida || _cerrando) return;
            if (_bytesEnCola > LimiteDescartables && Descartable(Tipo(framed)))
            {
                Descartados++;
                return;
            }
            _cola.Enqueue(framed);
            _bytesEnCola += framed.Length;
            Monitor.PulseAll(_lock);
        }
    }

    // Termina de mandar lo encolado y después invoca alCerrar (que cierra el socket).
    // Lo que se encole después se ignora.
    public void CerrarTrasVaciar()
    {
        lock (_lock)
        {
            _cerrando = true;
            Monitor.PulseAll(_lock);
        }
    }

    // Corta el hilo sin vaciar. Si está trabado en un Write, lo destraba cerrar el socket
    // (lo hace quien llama).
    public void Detener()
    {
        lock (_lock)
        {
            _detenida = true;
            _cola.Clear();
            _bytesEnCola = 0;
            Monitor.PulseAll(_lock);
        }
    }

    // Espera (bloqueando, con tope) a que la cola quede vacía. Para el cierre del host:
    // que el SessionEnded llegue a salir antes de cerrar los sockets.
    public bool EsperarVaciado(int ms)
    {
        int limite = Environment.TickCount + ms;
        lock (_lock)
        {
            while (_cola.Count > 0 && !_detenida)
            {
                int resta = limite - Environment.TickCount;
                if (resta <= 0) return false;
                Monitor.Wait(_lock, resta);
            }
            return true;
        }
    }

    private void Bucle()
    {
        bool cerrarAlFinal = false;
        try
        {
            while (true)
            {
                byte[] primero;
                lock (_lock)
                {
                    while (_cola.Count == 0 && !_cerrando && !_detenida)
                        Monitor.Wait(_lock);
                    if (_detenida) return;
                    if (_cola.Count == 0) { cerrarAlFinal = true; return; }   // _cerrando y vacía
                    primero = _cola.Peek();
                }

                int escritos;
                if (primero.Length >= Trozo)
                {
                    // Frame grande: en trozos, así el hilo nunca queda adentro de un
                    // único Write de varios MB.
                    for (int o = 0; o < primero.Length; o += Trozo)
                        _stream.Write(primero, o, Math.Min(Trozo, primero.Length - o));
                    escritos = 1;
                }
                else
                {
                    // Frames chicos: juntar los que ya esperan en un solo Write.
                    int largo = 0;
                    escritos = 0;
                    lock (_lock)
                    {
                        foreach (var f in _cola)
                        {
                            if (f.Length >= Trozo || largo + f.Length > Trozo) break;
                            Buffer.BlockCopy(f, 0, _agrupado, largo, f.Length);
                            largo += f.Length;
                            escritos++;
                        }
                    }
                    _stream.Write(_agrupado, 0, largo);
                }

                lock (_lock)
                {
                    if (_detenida) return;
                    for (int i = 0; i < escritos && _cola.Count > 0; i++)
                        _bytesEnCola -= _cola.Dequeue().Length;
                    Monitor.PulseAll(_lock);   // EsperarVaciado
                }
            }
        }
        catch (Exception)
        {
            bool avisar;
            lock (_lock)
            {
                avisar    = !_detenida;
                _detenida = true;
                _cola.Clear();
                _bytesEnCola = 0;
                Monitor.PulseAll(_lock);
            }
            if (avisar) _alFallar?.Invoke();
        }
        finally
        {
            if (cerrarAlFinal) _alCerrar?.Invoke();
        }
    }
}
