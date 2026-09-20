namespace Gameplay
{
    // De qué VARIANTE es este build: completa o demo. Es el cuarto eje, ortogonal a los
    // tres tiers de CLAUDE.md (editor-only / development build / release): una demo se
    // compila en release, sólo que con menos contenido habilitado.
    //
    // El interruptor es el define de compilación MORTUORIUM_DEMO, no una opción ni un
    // asset: así el recorte no se puede desactivar desde el dispositivo (ni con las
    // PlayerPrefs a mano) y en el build completo las ramas de demo ni siquiera existen
    // — `EsDemo` es const, o sea que el compilador se lleva puesto el código muerto.
    //
    // Se prende y apaga desde el menú del editor **Mortuorium > Variante de build**
    // (Assets/Editor/MortuoriumBuildVariant.cs), que además cambia el bundle id y el
    // nombre de la app para que la demo y la completa convivan instaladas en el mismo
    // teléfono.
    public static class BuildVariant
    {
#if MORTUORIUM_DEMO
        public const bool EsDemo = true;
#else
        public const bool EsDemo = false;
#endif

        // Cuántas noches del catálogo habilita la demo. El catálogo NO se recorta (los
        // NightConfig siguen en el build): las noches de más se dibujan bloqueadas, que
        // es justamente el gancho de una demo.
        public const int NochesDemo = 3;

        // Multijugador: fuera de la demo. La sesión compartida necesita que los dos
        // jugadores tengan el mismo entorno escaneado y la misma imagen física, que es
        // demasiado setup para una primera prueba del juego.
        public const bool Multijugador = !EsDemo;

        // ¿Esa posición del catálogo entra en esta variante? Es el único gate: lo
        // consultan GameSession.NocheDisponible (y con él la grilla del menú y la de
        // fin de noche) y el desbloqueo por progresión.
        public static bool NocheHabilitada(int index) => !EsDemo || index < NochesDemo;

        // Sello para la UI. Null en la build completa.
        public static string Etiqueta => EsDemo ? "DEMO" : null;
    }
}
