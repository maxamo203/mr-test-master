using System.IO;

namespace Bateries
{
    // Cliente → servidor: pedido de recoger la pila apuntada. El server valida que
    // exista, sea pila y que el jugador este lo bastante cerca antes de aceptarla.
    public class BatteryPickupMsg
    {
        public uint NetworkId;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream(4);
            using var w  = new BinaryWriter(ms);
            w.Write(NetworkId);
            return ms.ToArray();
        }

        public static BatteryPickupMsg Deserialize(byte[] d)
        {
            using var r = new BinaryReader(new MemoryStream(d));
            return new() { NetworkId = r.ReadUInt32() };
        }
    }

    // Servidor → cliente que recogio: cuanto carga sumar a la linterna (y que rareza,
    // por si el HUD quiere mostrarla).
    public class BatteryCollectedMsg
    {
        public byte  RarityIndex;
        public float Charge;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream(5);
            using var w  = new BinaryWriter(ms);
            w.Write(RarityIndex);
            w.Write(Charge);
            return ms.ToArray();
        }

        public static BatteryCollectedMsg Deserialize(byte[] d)
        {
            using var r = new BinaryReader(new MemoryStream(d));
            return new() { RarityIndex = r.ReadByte(), Charge = r.ReadSingle() };
        }
    }
}
