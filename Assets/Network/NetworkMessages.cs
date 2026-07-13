using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class MsgHelper
{
    public static byte[] Frame(MessageType type, byte[] body)
    {
        using var ms = new MemoryStream(6 + body.Length);
        using var w  = new BinaryWriter(ms);
        w.Write((ushort)type);
        w.Write(body.Length);
        w.Write(body);
        return ms.ToArray();
    }

    public static void WriteV3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    public static Vector3 ReadV3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}

public class ClientConnectedMsg
{
    public uint ClientId;
    public uint PlayerNetworkId;

    public byte[] Serialize()
    {
        using var ms = new MemoryStream(8);
        using var w  = new BinaryWriter(ms);
        w.Write(ClientId); w.Write(PlayerNetworkId);
        return ms.ToArray();
    }

    public static ClientConnectedMsg Deserialize(byte[] d)
    {
        using var r = new BinaryReader(new MemoryStream(d));
        return new() { ClientId = r.ReadUInt32(), PlayerNetworkId = r.ReadUInt32() };
    }
}

public class SpawnEntityMsg
{
    public uint    NetworkId;
    public byte    TypeId;
    public uint    OwnerClientId;
    public Vector3 Position;

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write(NetworkId); w.Write(TypeId); w.Write(OwnerClientId);
        MsgHelper.WriteV3(w, Position);
        return ms.ToArray();
    }

    public static SpawnEntityMsg Deserialize(byte[] d)
    {
        using var r = new BinaryReader(new MemoryStream(d));
        return new()
        {
            NetworkId     = r.ReadUInt32(),
            TypeId        = r.ReadByte(),
            OwnerClientId = r.ReadUInt32(),
            Position      = MsgHelper.ReadV3(r),
        };
    }
}

public class DespawnEntityMsg
{
    public uint NetworkId;

    public byte[] Serialize()
    {
        using var ms = new MemoryStream(4);
        using var w  = new BinaryWriter(ms);
        w.Write(NetworkId);
        return ms.ToArray();
    }

    public static DespawnEntityMsg Deserialize(byte[] d)
    {
        using var r = new BinaryReader(new MemoryStream(d));
        return new() { NetworkId = r.ReadUInt32() };
    }
}

public class WorldStateMsg
{
    public uint Tick;

    public struct Entry
    {
        public uint   NetworkId;
        public byte   TypeId;
        public byte[] Payload;
    }

    public List<Entry> Entries = new();

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write(Tick);
        w.Write((ushort)Entries.Count);
        foreach (var e in Entries)
        {
            w.Write(e.NetworkId);
            w.Write(e.TypeId);
            w.Write((ushort)e.Payload.Length);
            w.Write(e.Payload);
        }
        return ms.ToArray();
    }

    public static WorldStateMsg Deserialize(byte[] d)
    {
        using var r   = new BinaryReader(new MemoryStream(d));
        var       msg = new WorldStateMsg { Tick = r.ReadUInt32() };
        int       n   = r.ReadUInt16();
        for (int i = 0; i < n; i++)
        {
            uint netId  = r.ReadUInt32();
            byte typeId = r.ReadByte();
            int  len    = r.ReadUInt16();
            msg.Entries.Add(new Entry { NetworkId = netId, TypeId = typeId, Payload = r.ReadBytes(len) });
        }
        return msg;
    }
}

// En AR la posicion viene de la camara fisica, no de WASD.
// Position esta en espacio relativo al anchor (para que sea igual en todos los dispositivos).
public class PlayerInputMsg
{
    public uint    NetworkId;
    public uint    Tick;
    public Vector3 Position;  // posicion anchor-relativa del jugador (desde Camera.main)
    public bool    Attack;

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write(NetworkId); w.Write(Tick);
        MsgHelper.WriteV3(w, Position);
        w.Write(Attack);
        return ms.ToArray();
    }

    public static PlayerInputMsg Deserialize(byte[] d)
    {
        using var r = new BinaryReader(new MemoryStream(d));
        return new()
        {
            NetworkId = r.ReadUInt32(),
            Tick      = r.ReadUInt32(),
            Position  = MsgHelper.ReadV3(r),
            Attack    = r.ReadBoolean(),
        };
    }
}

// Cliente → servidor cada tick: la posicion de la camara AR del jugador, en espacio
// anchor-relativo (para que el server la interprete en su propio AR local). El server
// la usa para saber donde estan todos los jugadores (spawns de pilas, validar pickup).
// Sin esto el server solo conoce su propia camara (host) — no la de los clientes.
public class PlayerPoseMsg
{
    public Vector3 RelPos;
    // Estado de la linterna del jugador (para que el server, autoritativo, drene la
    // cordura y compute el repel cuando la linterna ilumina un objetivo).
    public bool FlashlightOn;
    // Direccion de apuntado (forward de la camara) en espacio anchor-relativo, para el
    // test de cono del repel en el server.
    public Vector3 Forward;

    public byte[] Serialize()
    {
        using var ms = new MemoryStream(25);
        using var w  = new BinaryWriter(ms);
        MsgHelper.WriteV3(w, RelPos);
        w.Write(FlashlightOn);
        MsgHelper.WriteV3(w, Forward);
        return ms.ToArray();
    }

    public static PlayerPoseMsg Deserialize(byte[] d)
    {
        using var r = new BinaryReader(new MemoryStream(d));
        return new()
        {
            RelPos       = MsgHelper.ReadV3(r),
            FlashlightOn = r.ReadBoolean(),
            Forward      = MsgHelper.ReadV3(r),
        };
    }
}

// server → client: la cordura autoritativa del jugador de ese cliente. El server la
// calcula (drena si su linterna esta apagada mas de lo permitido) y la envia; el
// cliente solo la muestra (barra + distorsion). No es recuperable.
public class PlayerSanityMsg
{
    public float Sanity;
    public float Max;

    public byte[] Serialize()
    {
        using var ms = new MemoryStream(8);
        using var w  = new BinaryWriter(ms);
        w.Write(Sanity); w.Write(Max);
        return ms.ToArray();
    }

    public static PlayerSanityMsg Deserialize(byte[] d)
    {
        using var r = new BinaryReader(new MemoryStream(d));
        return new() { Sanity = r.ReadSingle(), Max = r.ReadSingle() };
    }
}

public class AnchorIdMsg
{
    public string Id;

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write(Id ?? "");
        return ms.ToArray();
    }

    public static AnchorIdMsg Deserialize(byte[] d)
    {
        using var r = new BinaryReader(new MemoryStream(d));
        return new() { Id = r.ReadString() };
    }
}

// Lleva el .mscn completo del mapa (json + PNG de referencia) empaquetado por
// ScanPackage.Pack. El transporte enmarca el largo con int, así que entra como un
// solo frame aunque pese cientos de KB. server → client al conectarse.
public class MapDataMsg
{
    public byte[] Bytes;

    public byte[] Serialize()
    {
        var data = Bytes ?? System.Array.Empty<byte>();
        using var ms = new MemoryStream(4 + data.Length);
        using var w  = new BinaryWriter(ms);
        w.Write(data.Length);
        w.Write(data);
        return ms.ToArray();
    }

    public static MapDataMsg Deserialize(byte[] d)
    {
        using var r   = new BinaryReader(new MemoryStream(d));
        int       len = r.ReadInt32();
        return new() { Bytes = len > 0 ? r.ReadBytes(len) : System.Array.Empty<byte>() };
    }
}

// AnchorResolved y StartGame no llevan payload — body vacío es suficiente
