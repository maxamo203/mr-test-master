// Configuración de red compartida.
public static class NetworkConfig
{
    // Puerto TCP por defecto del juego. Un cliente que sólo escribe la IP se conecta
    // a este puerto, y el autodescubrimiento por LAN sólo anuncia/encuentra hosts
    // que usan este puerto (ver GameBootstrapper y LanDiscovery). Si el host cambia
    // el puerto en "Avanzado", deja de anunciarse: hay que unirse escribiendo la IP
    // con el puerto (ip:puerto) a mano.
    public const int DefaultPort = 7777;
}
