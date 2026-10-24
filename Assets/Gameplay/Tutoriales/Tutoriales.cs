using UnityEngine;

namespace Gameplay
{
    // Prácticas del TUTORIAL: qué NightConfig usa cada una. Todas se juegan sobre un
    // escenario escaneado del jugador (como una noche); sólo en development build SORKEN y
    // VELETH se pueden armar además en el momento, sin escaneo (ArmadoRapido).
    public static class Tutoriales
    {
        // Las configs viven en Resources/Tutoriales (no en el catálogo de noches: no
        // se eligen en la grilla ni cuentan para la progresión).
        public static NightConfig Config(TutorialTipo tipo) => tipo switch
        {
            TutorialTipo.Linterna       => Resources.Load<NightConfig>("Tutoriales/Linterna"),
            TutorialTipo.PilasReliquias => Resources.Load<NightConfig>("Tutoriales/PilasReliquias"),
            TutorialTipo.Sorken         => Resources.Load<NightConfig>("Tutoriales/Sorken"),
            TutorialTipo.Arbmos         => Resources.Load<NightConfig>("Tutoriales/Arbmos"),
            TutorialTipo.Libro          => Resources.Load<NightConfig>("Tutoriales/Libro"),
            _                           => null,
        };

        public static string Titulo(TutorialTipo tipo) => tipo switch
        {
            TutorialTipo.Linterna       => "LA LINTERNA",
            TutorialTipo.PilasReliquias => "PILAS Y CORDURA",
            TutorialTipo.Sorken         => "SORKEN",
            TutorialTipo.Arbmos         => "ARBMOS",
            TutorialTipo.Libro          => "LIBRO Y VELETH",
            _                           => "",
        };

        // Qué se aprende en cada paso (el mapa del TUTORIAL y la tarjeta de "siguiente").
        public static string Descripcion(TutorialTipo tipo) => tipo switch
        {
            TutorialTipo.Linterna       => "Prenderla, apagarla y concentrar la luz",
            TutorialTipo.PilasReliquias => "La batería se gasta: recargala con pilas y conocé tu cordura",
            TutorialTipo.Sorken         => "Entra por puertas y ventanas. La luz intensa lo echa",
            TutorialTipo.Arbmos         => "Con éste es al revés: apagá la luz para que se vaya",
            TutorialTipo.Libro          => "Salvá el libro con luz. Si se apaga, escapale a Veleth",
            _                           => "",
        };

        // Arte de fondo / retrato de cada práctica (mismo criterio que las noches).
        public static Texture2D Arte(TutorialTipo tipo) =>
            Rediseno.Menus.TarjetasNoche.Arte(tipo switch
            {
                TutorialTipo.Arbmos => 3,
                TutorialTipo.Libro  => 5,
                _                   => 0,
            });

        // ¿Ese escaneo sirve para esa práctica? null = sí; si no, el motivo para mostrar.
        // Lo general (imagen de referencia) lo decide el menú; acá sólo lo propio de cada una.
        public static string MotivoNoSirve(TutorialTipo tipo, Scanner.ScanData data)
        {
            if (data == null) return "No se pudo leer el escaneo";
            // El Sorken entra por puertas y ventanas marcadas: sin ninguna no aparece.
            if (tipo == TutorialTipo.Sorken && (data.markers == null || data.markers.Count == 0))
                return "Sin puertas ni ventanas marcadas";
            return null;
        }

        // Crea el componente de esa práctica si todavía no existe. Cada uno se crea solo al
        // cargar SampleScene; esto es para pasar de un paso del TUTORIAL al siguiente sin
        // recargarla (TutorialCamino). El del paso anterior se destruye solo al ver que
        // GameSession.Tutorial ya no es el suyo.
        public static void Crear(TutorialTipo tipo)
        {
            switch (tipo)
            {
                case TutorialTipo.Linterna:
                    if (TutorialLinterna.Instance == null) new GameObject("TutorialLinterna").AddComponent<TutorialLinterna>();
                    break;
                case TutorialTipo.PilasReliquias:
                    if (TutorialPilasReliquias.Instance == null) new GameObject("TutorialPilasReliquias").AddComponent<TutorialPilasReliquias>();
                    break;
                case TutorialTipo.Sorken:
                    if (TutorialSorken.Instance == null) new GameObject("TutorialSorken").AddComponent<TutorialSorken>();
                    break;
                case TutorialTipo.Arbmos:
                    if (TutorialArbmos.Instance == null) new GameObject("TutorialArbmos").AddComponent<TutorialArbmos>();
                    break;
                case TutorialTipo.Libro:
                    if (TutorialLibro.Instance == null) new GameObject("TutorialLibro").AddComponent<TutorialLibro>();
                    break;
            }
        }

        // ¿Sigue vigente la práctica de ese tipo? Cada componente lo consulta en su Update
        // y se destruye cuando el TUTORIAL pasó al paso siguiente (o a la Noche 1).
        public static bool Vigente(TutorialTipo tipo) =>
            GameSession.Instance != null && GameSession.Instance.Tutorial == tipo;
    }
}
