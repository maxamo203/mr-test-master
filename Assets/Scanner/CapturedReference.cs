using UnityEngine;

namespace Scanner
{
    // Holder en memoria de la imagen de referencia (el fragmento capturado con la
    // cámara o el cargado de disco) usada en la sesión actual. SaveLoadUI lo lee
    // al guardar para persistir la imagen junto al escaneo, y lo actualiza al
    // capturar o al cargar.
    public static class CapturedReference
    {
        public static Texture2D Texture { get; private set; }
        public static float WidthMeters { get; private set; }
        public static bool HasImage => Texture != null;

        // Cómo está pegada la imagen física (piso/mesa o pared). Al capturar se
        // estima por la inclinación del celular; ARImageAnchor la confirma con la
        // primera detección real (ConfirmarOrientacion). Se guarda con el escaneo.
        public static ImageAnchorPose.Orientacion Orientacion { get; private set; }

        public static void Set(Texture2D tex, float widthMeters,
                               ImageAnchorPose.Orientacion orientacion = ImageAnchorPose.Orientacion.Desconocida)
        {
            Texture = tex;
            WidthMeters = widthMeters;
            Orientacion = orientacion;
        }

        // La detección real manda sobre la estimación de captura: es la geometría de
        // la imagen tal como está pegada, no cómo se sostenía el celular.
        public static void ConfirmarOrientacion(ImageAnchorPose.Orientacion detectada)
        {
            if (!HasImage || detectada == ImageAnchorPose.Orientacion.Desconocida) return;
            Orientacion = detectada;
        }

        public static void Clear()
        {
            Texture = null;
            WidthMeters = 0f;
            Orientacion = ImageAnchorPose.Orientacion.Desconocida;
        }
    }
}
