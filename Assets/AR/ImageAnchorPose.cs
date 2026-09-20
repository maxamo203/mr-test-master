using System.Collections.Generic;
using UnityEngine;

// Matemática PURA del anclaje por imagen: cómo se pasa de la pose que reporta
// ARKit/ARCore para la imagen de referencia a la pose del anchor (Y-up, sólo rumbo)
// y cómo se decide que las muestras ya convergieron. Sin MonoBehaviour ni subsistemas
// para poder cubrirlo con tests de editor (Assets/Tests/Editor/ImageAnchorPoseTests).
//
// Convención de ejes de ARFoundation para un ARTrackedImage (ARKit y ARCore la
// comparten): +X = ancho de la imagen, +Z = alto, +Y = NORMAL (sale de la imagen).
// Es la misma que usa el sample oficial, que apoya un Plane de Unity (XZ) sobre la
// imagen con localScale = (size.x, 1, size.y).
//
// INVARIANTE: el mundo siempre queda Y-up. La imagen sólo aporta la POSICIÓN y la
// ROTACIÓN SOBRE Y (rumbo). Nunca se copia su inclinación.
public static class ImageAnchorPose
{
    // Cómo está pegada la imagen física. Se decide al CAPTURARLA (por la inclinación
    // del celular) y se confirma con la primera detección real; viaja guardada en el
    // escaneo (ScanData.refImageOrientation) para que todas las sesiones — y todos
    // los jugadores en multijugador — deriven el rumbo con la misma regla.
    public enum Orientacion
    {
        Desconocida = 0,   // escaneos viejos: se infiere de la pose detectada
        Horizontal  = 1,   // apoyada en piso / mesa (normal vertical)
        Vertical    = 2,   // pegada en una pared (normal horizontal)
    }

    // Umbral de |normal.y| para separar horizontal de vertical. 0.5 = 60° de
    // inclinación: por debajo se toma como pared, por encima como piso/mesa.
    private const float UmbralNormalY = 0.5f;

    // Orientación a partir de hacia dónde miraba la cámara al capturar el recorte:
    // mirando al piso (o al techo) la imagen es horizontal; mirando de frente, es
    // una pared. Es sólo la estimación inicial: ConfirmarConPose la corrige con la
    // detección real.
    public static Orientacion DesdeCamara(Vector3 camaraForward)
    {
        float f = camaraForward.sqrMagnitude > 1e-6f ? camaraForward.normalized.y : 0f;
        return Mathf.Abs(f) >= UmbralNormalY ? Orientacion.Horizontal : Orientacion.Vertical;
    }

    // Orientación según la NORMAL detectada (img.up). Determinista: sólo depende de
    // la geometría real de la imagen, no de la convención de captura.
    public static Orientacion DesdePose(Vector3 normal)
    {
        return Mathf.Abs(normal.y) >= UmbralNormalY ? Orientacion.Horizontal : Orientacion.Vertical;
    }

    // Orientación EFECTIVA para derivar el rumbo: la guardada con el escaneo manda
    // (así todas las sesiones y todos los jugadores aplican la misma regla), salvo
    // que la pose detectada la contradiga de forma clara — la imagen se pegó en otro
    // lado, o la estimación de captura (por la inclinación del celular) estuvo mal.
    // En ese caso gana la geometría real; la zona gris del medio respeta el flag.
    public static Orientacion Resolver(Orientacion guardada, Vector3 normal)
    {
        float ny = Mathf.Abs(normal.y);
        switch (guardada)
        {
            case Orientacion.Horizontal when ny < 0.25f: return Orientacion.Vertical;
            case Orientacion.Vertical   when ny > 0.75f: return Orientacion.Horizontal;
            case Orientacion.Desconocida:                return DesdePose(normal);
            default:                                     return guardada;
        }
    }

    // Eje horizontal unitario que define el RUMBO del anchor, según la orientación.
    //
    //   Horizontal: el ancho (+X) de la imagen. Está en el plano de la imagen, así
    //               que es horizontal casi puro; determinista y estable entre
    //               calibraciones de la misma imagen. Es exactamente lo que se venía
    //               usando, así que los escaneos existentes no cambian de rumbo.
    //   Vertical:   de los DOS ejes del plano (+X ancho, +Z alto) el más horizontal.
    //               En una pared uno queda vertical (|y|≈1) y el otro horizontal
    //               (|y|≈0): no hay empate. NUNCA se usa la normal (+Y): es el eje
    //               que el tracking estima con más ruido y era lo que hacía saltar
    //               el rumbo ~90° entre calibraciones en la versión vieja.
    //
    // Devuelve un vector en el plano XZ, unitario (o forward si degenerado).
    public static Vector3 EjeRumbo(Vector3 right, Vector3 up, Vector3 forward, Orientacion orientacion)
    {
        if (orientacion == Orientacion.Desconocida) orientacion = DesdePose(up);

        Vector3 eje = right;
        if (orientacion == Orientacion.Vertical)
        {
            // Empate (imagen girada ~45° sobre la pared): se queda con el ancho, que
            // es la misma elección que en horizontal. Lo importante es que la
            // decisión sea la MISMA en todas las sesiones, y lo es.
            if (Mathf.Abs(forward.y) + 0.05f < Mathf.Abs(right.y)) eje = forward;
        }

        return Aplanar(eje);
    }

    // Proyecta al plano horizontal y normaliza. Forward si el vector no tiene
    // componente horizontal (imagen mirada exactamente de canto).
    public static Vector3 Aplanar(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
    }

    // Rotación con Y = up del mundo y forward = eje de rumbo.
    public static Quaternion UprightDesdeRumbo(Vector3 ejeHorizontal)
    {
        return Quaternion.LookRotation(Aplanar(ejeHorizontal), Vector3.up);
    }

    // Alinea el signo de un eje con el de una referencia (para que un flip de 180°
    // entre muestras no cancele el promedio).
    public static Vector3 AlinearSigno(Vector3 eje, Vector3 referencia)
    {
        return Vector3.Dot(eje, referencia) < 0f ? -eje : eje;
    }

    // ── Convergencia de las muestras ─────────────────────────────────────────
    //
    // ARKit/ARCore refinan la pose de la imagen durante los primeros frames de
    // tracking: las muestras viejas son las peores. Por eso se mira sólo la VENTANA
    // de las últimas N y se ancla cuando esa ventana ya no se mueve, en vez de
    // promediar todo lo visto desde el principio.

    public readonly struct Muestra
    {
        public readonly Vector3 Rumbo;     // eje horizontal unitario, ya alineado en signo
        public readonly Vector3 Posicion;
        public Muestra(Vector3 rumbo, Vector3 posicion) { Rumbo = rumbo; Posicion = posicion; }
    }

    // Promedio de la ventana: rumbo = media de los ejes (re-aplanada y normalizada),
    // posición = media aritmética. Devuelve false si la lista está vacía.
    public static bool Promediar(IReadOnlyList<Muestra> ventana, out Vector3 rumbo, out Vector3 posicion)
    {
        rumbo = Vector3.forward; posicion = Vector3.zero;
        if (ventana == null || ventana.Count == 0) return false;

        Vector3 sumR = Vector3.zero, sumP = Vector3.zero;
        for (int i = 0; i < ventana.Count; i++)
        {
            sumR += ventana[i].Rumbo;
            sumP += ventana[i].Posicion;
        }
        rumbo    = Aplanar(sumR);
        posicion = sumP / ventana.Count;
        return true;
    }

    // Dispersión de la ventana: el mayor ángulo (grados) entre una muestra y el
    // rumbo medio, y la mayor distancia (m) entre una muestra y la posición media.
    public static void Dispersion(IReadOnlyList<Muestra> ventana, out float maxGrados, out float maxMetros)
    {
        maxGrados = 0f; maxMetros = 0f;
        if (!Promediar(ventana, out var rumbo, out var pos)) return;

        for (int i = 0; i < ventana.Count; i++)
        {
            float ang = Vector3.Angle(ventana[i].Rumbo, rumbo);
            if (ang > maxGrados) maxGrados = ang;
            float d = Vector3.Distance(ventana[i].Posicion, pos);
            if (d > maxMetros) maxMetros = d;
        }
    }

    // ¿La ventana ya está estable? Requiere que esté llena (minMuestras) y que ni el
    // rumbo ni la posición se hayan movido más que la tolerancia dentro de ella.
    public static bool Convergio(IReadOnlyList<Muestra> ventana, int minMuestras,
                                 float tolGrados, float tolMetros)
    {
        if (ventana == null || ventana.Count < minMuestras) return false;
        Dispersion(ventana, out float g, out float m);
        return g <= tolGrados && m <= tolMetros;
    }
}
