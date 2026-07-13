using UnityEngine;

namespace Scanner
{
    // Area segura de la pantalla en coordenadas de GUI (IMGUI: pixeles, origen
    // arriba-izquierda), excluyendo notch / Dynamic Island / home indicator.
    //
    // Screen.safeArea viene con origen ABAJO-izquierda (como Input/Screen), asi que lo
    // convertimos a coords de GUI. Toda la UI OnGUI deberia posicionarse dentro de este
    // rect — directamente (Left/Top/Right/Bottom) o via UIScale, que ya lo usa.
    public static class SafeArea
    {
        // El area segura en coords de GUI (pixeles, origen arriba-izquierda).
        public static Rect GuiRect
        {
            get
            {
                var sa = Screen.safeArea;                 // origen abajo-izquierda
                float top = Screen.height - sa.yMax;      // borde superior en coords GUI
                return new Rect(sa.x, top, sa.width, sa.height);
            }
        }

        public static float Left   => GuiRect.x;      // borde izquierdo seguro
        public static float Top    => GuiRect.y;      // borde superior seguro
        public static float Right  => GuiRect.xMax;   // borde derecho seguro
        public static float Bottom => GuiRect.yMax;   // borde inferior seguro
        public static float Width  => GuiRect.width;
        public static float Height => GuiRect.height;
    }
}
