using System;
using System.IO;
using System.Net.Sockets;

// Frame format: [ushort type (2 bytes)][int bodyLen (4 bytes)][body (N bytes)]
public static class MessageFramer
{
    // Tope de un frame. El mensaje más grande legítimo es el .mscn del mapa (json +
    // PNG de la imagen de referencia: unos pocos MB). El largo llega sin validar por la
    // red: un stream desincronizado o un paquete basura podía pedir un new byte[] de
    // cientos de MB desde el hilo de lectura y hacer que iOS matara la app por memoria.
    public const int MaxBodyBytes = 32 * 1024 * 1024;

    // progreso (opcional): (tipo, leídos, total) del cuerpo a medida que llega, desde
    // el hilo de lectura. Sirve para mostrar el avance de un frame grande (el MapData)
    // antes de que se complete. Pasar un delegado cacheado: se invoca por chunk.
    public static (MessageType type, byte[] body) ReadOne(NetworkStream stream,
        Action<MessageType, int, int> progreso = null)
    {
        var header = ReadExact(stream, 6, default, null);
        var type   = (MessageType)BitConverter.ToUInt16(header, 0);
        int len    = BitConverter.ToInt32(header, 2);
        if (len < 0 || len > MaxBodyBytes)
            throw new IOException($"Frame inválido ({len} bytes): stream desincronizado");
        progreso?.Invoke(type, 0, len);
        var body   = len > 0 ? ReadExact(stream, len, type, progreso) : Array.Empty<byte>();
        return (type, body);
    }

    public static void Write(NetworkStream stream, byte[] framed)
        => stream.Write(framed, 0, framed.Length);

    private static byte[] ReadExact(NetworkStream s, int count, MessageType type,
        Action<MessageType, int, int> progreso)
    {
        var buf    = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int n = s.Read(buf, offset, count - offset);
            if (n == 0) throw new IOException("Connection closed");
            offset += n;
            progreso?.Invoke(type, offset, count);
        }
        return buf;
    }
}
