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

        public static void Set(Texture2D tex, float widthMeters)
        {
            Texture = tex;
            WidthMeters = widthMeters;
        }

        public static void Clear()
        {
            Texture = null;
            WidthMeters = 0f;
        }
    }
}
