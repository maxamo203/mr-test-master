using Scanner;
using UnityEngine;

namespace Gameplay
{
    // Armado rápido de las prácticas SORKEN y VELETH: en vez de un escaneo
    // guardado con imagen de referencia, el jugador arma en el momento lo mínimo que la
    // práctica necesita, dentro de SampleScene:
    //   - el LIBRO (el 0,0 del mapa), apuntando al piso — ManualCalibration lo ancla ahí;
    //   - para el Sorken, hasta UNA PUERTA y UNA VENTANA reales (marcar otra del mismo tipo
    //     reemplaza a la anterior): cada entrada es un tramo de pared de referencia
    //     (WallObject.SoloReferencia: sin collider, ignorado por SorkerNav) del largo de la
    //     pared real medida, con el hueco de la abertura recortado, un MarkerObject en la cara
    //     que mira al jugador y un MarcoEntrada que muestra qué se marcó. En partida esa
    //     pared es un oclusor invisible: tapa al Sorken alrededor del marco aunque el equipo
    //     no mida profundidad. Plano, orientación, largo y (si se ve el hueco) ancho salen de
    //     la pared real medida con profundidad (EntradaReal).
    // No se guarda a disco: vive en SceneRegistry y sobrevive a REPETIR porque esa
    // transición no recarga la escena (NightTransition).
    public static class ArmadoRapido
    {
        public const float AlturaPared = 2.4f, AnchoPared = 0.1f;
        // Pared de cada lado de la abertura: mínimo alrededor del marco y tope (lo que no se
        // midió puede no existir: una esquina, un pasillo).
        public const float MargenPared = 0.6f, MaxLado = 3f;
        // Medidas del marco cuando no se pudo medir el hueco (puerta/ventana cerrada, pared lisa).
        public const float AnchoPuerta = 0.9f, AltoPuerta = 2.05f, AnchoVentana = 1.0f, AltoVentana = 1.1f;
        // Fila de muestras de profundidad a la altura de la mira (fracción del ancho de pantalla).
        private const int Muestras = 25;
        private const float FilaDesde = 0.04f, FilaHasta = 0.96f;
        private static readonly System.Collections.Generic.List<Vector2> _fila = new(Muestras);

        // ¿Esa práctica se arma en el momento (sin escaneo)? Pilas y Arbmos necesitan un
        // ambiente cerrado de paredes, así que siguen jugándose sobre un escaneo real.
        public static bool Usa(TutorialTipo t) =>
            t == TutorialTipo.Sorken || t == TutorialTipo.Libro;

        public static bool LibroUbicado => ManualCalibration.Calibrado;
        public static int  Entradas => SceneRegistry.Instance != null ? SceneRegistry.Instance.Markers.Count : 0;
        public static bool HayEntrada(bool ventana)
        {
            if (SceneRegistry.Instance == null) return false;
            string tipo = ventana ? "Window" : "Door";
            foreach (var m in SceneRegistry.Instance.Markers)
                if (m != null && m.KindId == tipo) return true;
            return false;
        }
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
            // Como en un mapa cargado para jugar: invisible y sin collider (si no, la esfera
            // azul quedaba a la vista debajo del libro).
            ScanLoader.ComoVisual(() => FloorPoint.Create(Vector3.zero));
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

            // Todo en coordenadas locales de WorldOrigin (upright: la Y local es la del piso).
            var resolver = RaycastResolver.Ensure();
            Vector3 c = wo.ToRelative(cam.transform.position);
            Vector3 dir = wo.transform.InverseTransformDirection(cam.transform.forward);
            var rayo = new Vector2(dir.x, dir.z);
            if (rayo.sqrMagnitude < 0.01f) return "Mirá de frente a la puerta o ventana, no al piso ni al techo.";
            rayo.Normalize();

            // Pared real alrededor de la mira, medida con profundidad.
            _fila.Clear();
            float y = Screen.height * 0.5f;
            for (int i = 0; i < Muestras; i++)
            {
                float x = Screen.width * Mathf.Lerp(FilaDesde, FilaHasta, i / (Muestras - 1f));
                var h = resolver.ResolveFromScreenPoint(new Vector2(x, y));
                if (!h.Hit || h.Source == RaycastSource.Fallback) continue;
                Vector3 q = wo.ToRelative(h.Position);
                if (q.y < 0.25f) continue;   // piso o zócalo: no es la pared
                _fila.Add(new Vector2(q.x, q.z));
            }
            var ajuste = EntradaReal.Ajustar(new Vector2(c.x, c.z), rayo, _fila);

            Vector3 centro, normal;   // normal: hacia el jugador
            float altura;             // de la mira sobre el plano de la entrada
            if (ajuste.Ok)
            {
                centro = new Vector3(ajuste.Centro.x, 0f, ajuste.Centro.y);
                normal = new Vector3(ajuste.Normal.x, 0f, ajuste.Normal.y);
                float horiz = new Vector2(dir.x, dir.z).magnitude;
                altura = c.y + dir.y / horiz * ajuste.Distancia;
            }
            else
            {
                // Sin pared medible (poca profundidad, pared lisa sin textura): el punto bajo
                // la mira y la pared perpendicular a la mirada, como antes.
                var hit = resolver.ResolveFromScreenCenter();
#if !UNITY_EDITOR
                if (!hit.Hit || hit.Source == RaycastSource.Fallback)
                    return "No encuentro la puerta: acercate y apuntá al centro de la puerta o ventana, " +
                           "con algo de pared a los costados.";
#endif
                Vector3 p = wo.ToRelative(hit.Position);
                Vector3 f = p - c; f.y = 0f;
                if (f.sqrMagnitude < 0.04f) return "Alejate un poco de la puerta o ventana.";
                centro = new Vector3(p.x, 0f, p.z);
                normal = -f.normalized;
                altura = p.y;
            }

            float ancho = ajuste.Ancho > 0f ? ajuste.Ancho : ventana ? AnchoVentana : AnchoPuerta;
            Vector3 lado = Vector3.Cross(Vector3.up, -normal);
            // Tramo de pared a cada lado de la abertura: lo medido de la pared real, con un
            // mínimo alrededor del marco (fuera de cuadro la pared no se midió) y un tope.
            float izq = ancho * 0.5f + MargenPared, der = izq;
            if (ajuste.Ok)
            {
                float signo = Vector2.Dot(ajuste.Tangente, new Vector2(lado.x, lado.z)) >= 0f ? 1f : -1f;
                float l0 = signo * ajuste.Desde, l1 = signo * ajuste.Hasta;
                izq = Mathf.Max(izq, -Mathf.Min(l0, l1));
                der = Mathf.Max(der,  Mathf.Max(l0, l1));
            }
            izq = Mathf.Min(izq, MaxLado); der = Mathf.Min(der, MaxLado);
            Vector3 a = centro - lado * izq, b = centro + lado * der;
            float v = Mathf.Clamp(altura, 0.1f, AlturaPared - 0.1f);
            // Hueco de la abertura en la pared: en partida la pared es un oclusor invisible
            // (SceneOccluderMode) que tapa al Sorken alrededor del marco, no dentro de él.
            var hueco = new DoorData
            {
                id   = "entrada",
                uMin = izq - ancho * 0.5f, uMax = izq + ancho * 0.5f,
                vMin = ventana ? Mathf.Max(0f, v - AltoVentana * 0.5f) : 0f,
                vMax = ventana ? Mathf.Min(AlturaPared, v + AltoVentana * 0.5f) : Mathf.Min(AlturaPared, AltoPuerta),
            };

            // Una entrada por tipo: una puerta nueva reemplaza a la puerta anterior y una
            // ventana a la ventana; puerta y ventana conviven. La vieja se borra recién cuando
            // la nueva quedó creada, para no dejar la práctica sin entrada si esta falla.
            string tipo = ventana ? "Window" : "Door";
            var viejas = new System.Collections.Generic.List<WallObject>();
            if (SceneRegistry.Instance != null)
                foreach (var m in SceneRegistry.Instance.Markers)
                    if (m != null && m.KindId == tipo && m.Wall != null && m.Wall.SoloReferencia)
                        viejas.Add(m.Wall);

            WallObject pared = null;
            MarkerObject marcador = null;
            ScanLoader.ComoVisual(() =>
            {
                // side:-1 → la caja crece alejándose del jugador, detrás del plano real.
                pared = WallObject.Create(a, b, AlturaPared, AnchoPared, side: -1);
                if (pared == null) return;
                pared.SoloReferencia = true;
                pared.AddDoor(hueco);
                // Cara que mira al jugador: el forward del marcador apunta hacia adentro,
                // que es por donde el Sorken entra (GameDirector.EmergePosition).
                int cara = Vector3.Dot(c - pared.ALocal, pared.Normal) >= 0f ? 1 : -1;
                marcador = MarkerObject.Create(null, pared, izq, v, cara, kindId: tipo);
            });

            if (marcador == null)
            {
                if (pared != null) pared.Delete();
                return "No se pudo marcar la entrada.";
            }

            // Puerta: del piso a su altura. Ventana: centrada en la mira.
            float abajo  = ventana ? -AltoVentana * 0.5f : -marcador.V;
            float arriba = ventana ?  AltoVentana * 0.5f : AltoPuerta - marcador.V;
            MarcoEntrada.Crear(marcador.transform, ancho, abajo, arriba, pared);
            foreach (var w in viejas) w.Delete();   // también borra su marcador y su marco
            return null;
        }
    }
}
