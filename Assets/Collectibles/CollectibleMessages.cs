using System.IO;

namespace Collectibles
{
    // Cliente → servidor: pedido de recoger la reliquia apuntada. El server valida que
    // sea la reliquia activa y que el jugador este lo bastante cerca antes de aceptarla.
    public class CollectiblePickupMsg
    {
        public uint NetworkId;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream(4);
            using var w  = new BinaryWriter(ms);
            w.Write(NetworkId);
            return ms.ToArray();
        }

        public static CollectiblePickupMsg Deserialize(byte[] d)
        {
            using var r = new BinaryReader(new MemoryStream(d));
            return new() { NetworkId = r.ReadUInt32() };
        }
    }

    // Servidor → TODOS: cuantas reliquias van recogidas esta noche en total. Es un
    // contador compartido (no por jugador), asi que se manda a todos por igual — cada
    // dispositivo usa el mismo numero para su propio resultado de fin de noche.
    public class CollectibleTotalMsg
    {
        public byte Total;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream(1);
            using var w  = new BinaryWriter(ms);
            w.Write(Total);
            return ms.ToArray();
        }

        public static CollectibleTotalMsg Deserialize(byte[] d)
        {
            using var r = new BinaryReader(new MemoryStream(d));
            return new() { Total = r.ReadByte() };
        }
    }
}
