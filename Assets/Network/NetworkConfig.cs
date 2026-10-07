// Configuración de red compartida.
public static class NetworkConfig
{
    // Puerto TCP por defecto del juego. Un cliente que sólo escribe la IP se conecta
    // a este puerto, y el autodescubrimiento por LAN sólo anuncia/encuentra hosts
    // que usan este puerto (ver GameBootstrapper y LanDiscovery). Si el host cambia
    // el puerto en "Avanzado", deja de anunciarse: hay que unirse escribiendo la IP
    // con el puerto (ip:puerto) a mano.
    public const int DefaultPort = 7777;

    // El host manda un Heartbeat cada HeartbeatInterval segundos (también en la sala,
    // antes de GameStarted). Si el cliente pasa HostTimeoutSeconds sin recibir NADA del
    // host, lo da por caído: cubre las caídas sin cierre limpio del socket (Wi-Fi que
    // se corta, app del host matada o suspendida), en las que nunca llega el EOF. El
    // margen es amplio para no cortar por un MapData grande o un hipo de la red.
    public const float HeartbeatInterval  = 1f;
    public const float HostTimeoutSeconds = 10f;
}
