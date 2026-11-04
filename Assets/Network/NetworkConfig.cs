// Configuración de red compartida.
public static class NetworkConfig
{
    // Puerto TCP por defecto del juego. Un cliente que sólo escribe la IP se conecta
    // a este puerto, y el autodescubrimiento por LAN sólo anuncia/encuentra hosts
    // que usan este puerto (ver GameBootstrapper y LanDiscovery). Si el host cambia
    // el puerto en "Avanzado", deja de anunciarse: hay que unirse escribiendo la IP
    // con el puerto (ip:puerto) a mano.
    public const int DefaultPort = 7777;

    // Tope de la sala, host incluido. El host rechaza al que llega con la sala llena
    // (MessageType.RoomFull) y deja de anunciarse por LAN mientras no haya lugar.
    public const int MaxPlayers = 4;

    // El host manda un Heartbeat cada HeartbeatInterval segundos (también en la sala,
    // antes de GameStarted). Si el cliente pasa HostTimeoutSeconds sin recibir NADA del
    // host, lo da por caído: cubre las caídas sin cierre limpio del socket (Wi-Fi que
    // se corta, app del host matada o suspendida), en las que nunca llega el EOF. El
    // margen es amplio para no cortar por un MapData grande o un hipo de la red: en una
    // red saturada (exposición, hotspot con mala señal) TCP puede pasar varios segundos
    // retransmitiendo sin entregar nada, y cortar ahí es peor que tardar en detectar.
    public const float HeartbeatInterval  = 1f;
    public const float HostTimeoutSeconds = 15f;

    // Lo mismo en el otro sentido: cada cliente le manda un Heartbeat al host y el host
    // da de baja al que pasa ClientTimeoutSeconds sin mandar nada. En la sala el cliente
    // no manda nada más, así que sin esto un celular que se bloqueó, se quedó sin Wi-Fi
    // o mató la app seguía contando como conectado para siempre: nunca llegaba a listo,
    // el contador de la sala quedaba trabado y INICIAR NOCHE bloqueado.
    public const float ClientTimeoutSeconds = 15f;

    // Tope del intento de conexión del cliente (TcpTransportClient.ConnectAsync). TCP
    // reintenta el SYN solo dentro de este plazo; pasado, la pantalla UNIRSE muestra
    // "no responde" en vez de dejar la app congelada esperando al sistema.
    public const int ConnectTimeoutMs = 8000;
}
