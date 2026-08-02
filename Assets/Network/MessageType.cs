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
    MapData         = 9,   // server → client: el .mscn del mapa elegido por el host
    PlayerPose      = 10,  // client → server: pose anchor-relativa de la camara AR (por tick)
    BatteryPickup   = 11,  // client → server: pedido de recoger una pila apuntada
    BatteryCollected = 12, // server → client: la pila se recogio, sumar carga a la linterna
    PlayerSanity    = 13,  // server → client: cordura autoritativa del jugador (por dispositivo)
    PlayerDied      = 14,  // server → client: este jugador fue atrapado (pantalla de muerte)
    AnchorPointsStatus = 15, // client → server: estado de los anchor points extra de ese dispositivo
    ResetNight      = 16,  // server → all: se corta la noche y se vuelve a sincronizar (misma sesión AR)
    NightSurvived   = 17,  // server → all: amaneció, la noche está ganada
    NightClock      = 18,  // server → all: segundos que faltan para el amanecer (1 Hz)
}
