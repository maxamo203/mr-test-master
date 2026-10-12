using UnityEngine;

namespace Rediseno.UI
{
    // Dónde cae, en cada ojo, el punto que está justo al frente (píxeles de pantalla, origen
    // arriba-izq, convención IMGUI). NO es el centro de cada mitad: el Cardboard muestra en
    // cada ojo un recorte distinto según la calibración del visor (zoom + distancia de cada
    // ojo), así que el "frente" cae corrido. Dibujar en el centro de la mitad hacía que las
    // dos imágenes no se fusionaran ("se colapsan").
    //
    // En partida se lo pide al propio MRCardboardController (incluye la convergencia del
    // estéreo). En los menús no hay controlador: se calcula con la calibración guardada,
    // con la misma cuenta que MRCardboardController.UpdateEyes (RT del tamaño de la pantalla).
    public static class OjosVisor
    {
        private static MRCardboardController _mr;

        public static void Centros(out Vector2 izq, out Vector2 der)
        {
            if (MRCardboardController.Activo)
            {
                if (_mr == null) _mr = Object.FindAnyObjectByType<MRCardboardController>();
                if (_mr != null && _mr.MiraEnOjos(2f, out izq, out der)) return;
            }

            float sx = CalibracionVisor.Escala, offL = CalibracionVisor.OjoIzq, offR = CalibracionVisor.OjoDer;
            float W = Screen.width, H = Screen.height, halfW = W * 0.5f;
            float slice = sx * W / Mathf.Max(1f, H);
            float w = halfW, h = halfW / Mathf.Max(1e-4f, slice);
            if (h > H) { h = H; w = H * slice; }
            izq = new Vector2(W * 0.25f + ((0.5f - offL) / sx - 0.5f) * w, H * 0.5f);
            der = new Vector2(W * 0.75f + ((0.5f - offR) / sx - 0.5f) * w, H * 0.5f);
        }
    }

    // La calibración del visor, leída/escrita con las MISMAS claves que CardboardCalibrationUI
    // (que vive en la escena de juego): así se puede calibrar desde el menú y la partida la
    // toma al cargar.
    public static class CalibracionVisor
    {
        private const string KEscala = "cardboard_scale";
        private const string KIzq    = "cardboard_offsetL";
        private const string KDer    = "cardboard_offsetR";

        public static float Escala
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(KEscala, 0.77f), CardboardCalibrationUI.ScaleMin, CardboardCalibrationUI.ScaleMax);
            set
            {
                PlayerPrefs.SetFloat(KEscala, Mathf.Clamp(value, CardboardCalibrationUI.ScaleMin, CardboardCalibrationUI.ScaleMax));
                // Al cambiar el zoom, las distancias no pueden pasar del nuevo máximo.
                OjoIzq = OjoIzq; OjoDer = OjoDer;
                PlayerPrefs.Save();
            }
        }

        public static float MaxOjo => 1f - Escala;

        public static float OjoIzq
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(KIzq, 0.064f), 0f, MaxOjo);
            set { PlayerPrefs.SetFloat(KIzq, Mathf.Clamp(value, 0f, MaxOjo)); PlayerPrefs.Save(); }
        }

        public static float OjoDer
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(KDer, 0.183f), 0f, MaxOjo);
            set { PlayerPrefs.SetFloat(KDer, Mathf.Clamp(value, 0f, MaxOjo)); PlayerPrefs.Save(); }
        }
    }
}
