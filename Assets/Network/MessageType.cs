public enum MessageType : ushort
{
    ClientConnected = 1,
    SpawnEntity     = 2,
    DespawnEntity   = 3,
    WorldState      = 4,
    PlayerInput     = 5,
    AnchorId        = 6,   // server → client: ID del cloud anchor para resolver
    AnchorResolved  = 7,   // client → server: el cliente resolvió el anchor
    StartGame       = 8,   // server → all: arranca la partida
}
