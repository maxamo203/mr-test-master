using System.Collections.Generic;
using UnityEngine;

namespace Scanner
{
    // Registro por-frame de las zonas ocupadas por la UI IMGUI (paneles, botones).
    // Toda la UI del scanner es OnGUI, que corre en un canal de input separado de
    // los sistemas que leen input crudo (SelectionController, RecalibrateButton).
    // Sin coordinacion, un tap sobre un boton tambien seleccionaba/recalibraba lo
    // que estaba debajo. Cada panel registra su rect aca y esos sistemas consultan
    // IsPointerOver antes de actuar.
    //
    // Los rects de un OnGUI valen para el frame siguiente (OnGUI corre despues de
    // Update); por eso aceptamos entradas de hasta 1 frame de antiguedad.
    public static class UIBlocker
    {
        private struct Entry { public Rect rect; public int frame; }
        private static readonly List<Entry> _entries = new();

        // Registra un rect en coordenadas VIRTUALES (las de UIScale, pre-escala).
        public static void AddVirtualRect(Rect virtualRect)
        {
            float f = UIScale.Factor;
            AddScreenRect(new Rect(virtualRect.x * f, virtualRect.y * f,
                                   virtualRect.width * f, virtualRect.height * f));
        }

        // Registra un rect en pixeles reales (origen arriba-izquierda, como GUI).
        public static void AddScreenRect(Rect screenRect)
        {
            int frame = Time.frameCount;
            _entries.RemoveAll(e => frame - e.frame > 1);
            _entries.Add(new Entry { rect = screenRect, frame = frame });
        }

        // screenPos en pixeles con origen ABAJO-izquierda (Input System / touch).
        public static bool IsPointerOver(Vector2 screenPosBottomLeft)
        {
            int frame = Time.frameCount;
            var p = new Vector2(screenPosBottomLeft.x, Screen.height - screenPosBottomLeft.y);
            foreach (var e in _entries)
                if (frame - e.frame <= 1 && e.rect.Contains(p)) return true;
            return false;
        }
    }
}
