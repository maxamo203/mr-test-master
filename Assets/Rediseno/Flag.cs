namespace Rediseno
{
    // Único punto de lectura del interruptor "Rediseño 2.0" (OPCIONES del menú principal y
    // de la pausa). Todo lo de esta carpeta se prende/apaga con esto; el código viejo sólo
    // tiene que consultar Flag.Activo en sus puntos de entrada.
    //
    // Apagado = la app tal como venía funcionando. Se lee en vivo (no se cachea): cambiarlo
    // desde la pausa en plena noche tiene efecto inmediato.
    public static class Flag
    {
        public static bool Activo => GameOptions.Rediseno2;
    }
}
