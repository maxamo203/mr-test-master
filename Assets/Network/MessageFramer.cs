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

    public static (MessageType type, byte[] body) ReadOne(NetworkStream stream)
    {
        var header = ReadExact(stream, 6);
        var type   = (MessageType)BitConverter.ToUInt16(header, 0);
        int len    = BitConverter.ToInt32(header, 2);
        if (len < 0 || len > MaxBodyBytes)
            throw new IOException($"Frame inválido ({len} bytes): stream desincronizado");
        var body   = len > 0 ? ReadExact(stream, len) : Array.Empty<byte>();
        return (type, body);
    }

    public static void Write(NetworkStream stream, byte[] framed)
        => stream.Write(framed, 0, framed.Length);

    private static byte[] ReadExact(NetworkStream s, int count)
    {
        var buf    = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int n = s.Read(buf, offset, count - offset);
            if (n == 0) throw new IOException("Connection closed");
            offset += n;
        }
        return buf;
    }
}
