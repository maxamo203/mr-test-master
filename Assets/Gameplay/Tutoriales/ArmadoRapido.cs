using Scanner;
using UnityEngine;

namespace Gameplay
{
    // Armado rápido de las prácticas SORKEN, LIBRO RITUAL y VELETH: en vez de un escaneo
    // guardado con imagen de referencia, el jugador arma en el momento lo mínimo que la
    // práctica necesita, dentro de SampleScene:
    //   - el LIBRO (el 0,0 del mapa), apuntando al piso — ManualCalibration lo ancla ahí;
    //   - para el Sorken, una o más ENTRADAS (puertas/ventanas reales): cada una es un
    //     tramo de pared de referencia (WallObject.SoloReferencia, ignorado por SorkerNav)
    //     con un MarkerObject en la cara que mira al jugador.
    // No se guarda a disco: vive en SceneRegistry y sobrevive a REPETIR porque esa
    // transición no recarga la escena (NightTransition).
    public static class ArmadoRapido
    {
        public const float LargoPared = 2.0f, AlturaPared = 2.4f, AnchoPared = 0.1f;

        // ¿Esa práctica se arma en el momento (sin escaneo)? Pilas y Arbmos necesitan un
        // ambiente cerrado de paredes, así que siguen jugándose sobre un escaneo real.
        public static bool Usa(TutorialTipo t) =>
            t == TutorialTipo.Sorken || t == TutorialTipo.Libro || t == TutorialTipo.Veleth;

        public static bool LibroUbicado => ManualCalibration.Calibrado;
        public static int  Entradas => SceneRegistry.Instance != null ? SceneRegistry.Instance.Markers.Count : 0;
        public static bool PideEntradas(TutorialTipo t) => t == TutorialTipo.Sorken;
        public static bool Completo(TutorialTipo t) => LibroUbicado && (!PideEntradas(t) || Entradas > 0);

        // Pone el libro (el 0,0) sobre el piso bajo la mira. Devuelve null si salió bien,
        // o el motivo en español si no.
        public static string UbicarLibro()
        {
            var cam = Camera.main;
            if (cam == null) return "Sin cámara.";
            var hit = RaycastResolver.Ensure().ResolveFromScreenCenter();
#if !UNITY_EDITOR
            // En el editor no hay superficies AR y el resolver siempre cae en Fallback:
            // sin esta excepción el flujo no se podría probar.
            if (!hit.Hit || hit.Source == RaycastSource.Fallback)
                return "No encuentro el piso: mové el celular despacio apuntando al piso.";
            if (hit.Position.y > cam.transform.position.y - 0.6f)
                return "Eso no parece el piso: apuntá más abajo.";
#endif

            // Hace su propio raycast en este mismo frame: cae en el mismo punto.
            if (!ManualCalibration.Ensure().CentrarBajoLaMira(out var error)) return error;
            // CentrarBajoLaMira abre la fase de ajuste: la cerramos para que el libro
            // quede donde se apuntó.
            ManualCalibration.Instance.Listo();

            // Las entradas anteriores cuelgan del 0,0: al mover el libro quedarían corridas
            // de las puertas reales. ClearAll también borra el FloorPoint.
            SceneRegistry.Instance?.ClearAll();
            // El 0,0 está sobre el piso real: da la Y de piso a Sorken, Veleth y SorkerNav.
            FloorPoint.Create(Vector3.zero);
            return null;
        }

        // Marca una puerta (o ventana) real bajo la mira como entrada del Sorken.
        // Devuelve null si salió bien, o el motivo en español si no.
        public static string MarcarEntrada(bool ventana)
        {
            if (!LibroUbicado) return "Primero ubicá el libro.";
            var wo = WorldOrigin.Instance;
            var cam = Camera.main;
            if (wo == null || cam == null) return "Sin cámara.";

            var hit = RaycastResolver.Ensure().ResolveFromScreenCenter();
#if !UNITY_EDITOR
            if (!hit.Hit || hit.Source == RaycastSource.Fallback)
                return "No encuentro la puerta: acercate y apuntá al centro de la puerta o ventana.";
#endif

            // Todo en coordenadas locales de WorldOrigin (upright: la Y local es la del piso).
            Vector3 p = wo.ToRelative(hit.Position), c = wo.ToRelative(cam.transform.position);
            Vector3 f = p - c; f.y = 0f;
            if (f.sqrMagnitude < 0.04f) return "Alejate un poco de la puerta o ventana.";
            f.Normalize();
            Vector3 lado = Vector3.Cross(Vector3.up, f);
            Vector3 centro = new(p.x, 0f, p.z);
            Vector3 a = centro - lado * (LargoPared * 0.5f), b = centro + lado * (LargoPared * 0.5f);

            WallObject pared = null;
            MarkerObject marcador = null;
            ScanLoader.ComoVisual(() =>
            {
                // side:-1 → la caja crece alejándose del jugador, detrás del punto apuntado.
                pared = WallObject.Create(a, b, AlturaPared, AnchoPared, side: -1);
                if (pared == null) return;
                pared.SoloReferencia = true;
                // Cara que mira al jugador: el forward del marcador apunta hacia adentro,
                // que es por donde el Sorken entra (GameDirector.EmergePosition).
                int cara = Vector3.Dot(c - pared.ALocal, pared.Normal) >= 0f ? 1 : -1;
                marcador = MarkerObject.Create(null, pared, pared.Length * 0.5f,
                    Mathf.Clamp(p.y, 0.1f, AlturaPared - 0.1f), cara,
                    kindId: ventana ? "Window" : "Door");
            });

            if (marcador == null)
            {
                if (pared != null) pared.Delete();
                return "No se pudo marcar la entrada.";
            }
            return null;
        }
    }
}
