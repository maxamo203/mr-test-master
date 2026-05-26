using System;
using System.IO;
using System.Net.Sockets;

// Frame format: [ushort type (2 bytes)][int bodyLen (4 bytes)][body (N bytes)]
public static class MessageFramer
{
    public static (MessageType type, byte[] body) ReadOne(NetworkStream stream)
    {
        var header = ReadExact(stream, 6);
        var type   = (MessageType)BitConverter.ToUInt16(header, 0);
        int len    = BitConverter.ToInt32(header, 2);
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
