using UnityEngine;

namespace Gameplay
{
    // Prácticas del menú TUTORIALES: qué NightConfig usa cada una. SORKEN y VELETH (que
    // incluye salvar el libro ritual) se arman en el momento, sin escaneo (ArmadoRapido); PILAS y ARBMOS se juegan
    // sobre un escenario escaneado del jugador (como una noche).
    public static class Tutoriales
    {
        // Las configs viven en Resources/Tutoriales (no en el catálogo de noches: no
        // se eligen en la grilla ni cuentan para la progresión).
        public static NightConfig Config(TutorialTipo tipo) => tipo switch
        {
            TutorialTipo.PilasReliquias => Resources.Load<NightConfig>("Tutoriales/PilasReliquias"),
            TutorialTipo.Sorken         => Resources.Load<NightConfig>("Tutoriales/Sorken"),
            TutorialTipo.Arbmos         => Resources.Load<NightConfig>("Tutoriales/Arbmos"),
            TutorialTipo.Libro          => Resources.Load<NightConfig>("Tutoriales/Libro"),
            _                           => null,
        };

        public static string Titulo(TutorialTipo tipo) => tipo switch
        {
            TutorialTipo.PilasReliquias => "PILAS Y RELIQUIAS",
            TutorialTipo.Sorken         => "SORKEN",
            TutorialTipo.Arbmos         => "ARBMOS",
            TutorialTipo.Libro          => "VELETH",
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
    }
}
