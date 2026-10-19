using UnityEngine;

namespace Gameplay
{
    // Valores del retrato del asesino al morir (LocalDeath): encuadre, distancias y tiempos.
    // Vive en Resources/RetratoAsesinoConfig.asset (menú Mortuorium > Retrato del asesino >
    // Crear / seleccionar config) para tunearlo en el Inspector sin recompilar; en el editor
    // los cambios se ven al instante con Mortuorium > Retrato del asesino > Probar en Play.
    // Sin el asset rigen los valores por defecto de acá.
    [CreateAssetMenu(fileName = "RetratoAsesinoConfig", menuName = "Mortuorium/Retrato del asesino")]
    public class RetratoAsesinoConfig : ScriptableObject
    {
        public const string RutaResources = "RetratoAsesinoConfig";

        [Header("Encuadre")]
        [Tooltip("Parte del alto del asesino que se encuadra, desde arriba. 0.2 ≈ la cabeza; 1 = el cuerpo entero.")]
        [Range(0.05f, 1f)] public float encuadreAlto = 0.2f;
        [Tooltip("Aire alrededor de lo encuadrado. 1 = justo; más = más lejos.")]
        [Range(1f, 3f)] public float margen = 1.3f;
        [Tooltip("Corrimiento vertical del encuadre, en fracción de lo encuadrado (+ sube la mira, el asesino baja en pantalla).")]
        [Range(-1f, 1f)] public float corrimientoVertical = 0f;
        [Tooltip("Corrimiento horizontal, en fracción del ancho de pantalla (+ = a la derecha, 0.05 = 5%). " +
                 "El centrado ya usa el centro real de la pantalla; esto es un ajuste fino encima.")]
        [Range(-0.5f, 0.5f)] public float corrimientoHorizontal = 0f;

        [Header("Distancia (m)")]
        [Range(0.1f, 2f)] public float distanciaMin = 0.3f;
        [Range(0.5f, 8f)] public float distanciaMax = 4f;
        [Tooltip("Aire entre el cuerpo y el plano de recorte cercano: evita que lo que sobresale (alas, brazos) se corte en polígonos.")]
        [Range(0f, 0.5f)] public float margenCorte = 0.1f;

        [Header("Tiempos (s)")]
        [Tooltip("Lo que tarda en deslizarse desde donde estaba el asesino hasta su lugar.")]
        [Range(0.1f, 4f)] public float revelar = 1.45f;
        [Tooltip("Cuánto se lo deja ver, ya quieto, antes de la pantalla de muerte.")]
        [Range(0f, 10f)] public float sostener = 2.5f;

        [Header("Look")]
        [Range(0f, 5f)] public float temblorGrados = 0.7f;
        [Range(0f, 8f)] public float luzIntensidad = 2f;

        private static RetratoAsesinoConfig _actual, _defaults;

        public static RetratoAsesinoConfig Actual
        {
            get
            {
                // Sin asset se usan los defaults, pero se sigue buscando el asset: así uno
                // creado con el menú en medio del Play se toma sin reiniciar.
                if (_actual == null) _actual = Resources.Load<RetratoAsesinoConfig>(RutaResources);
                if (_actual != null) return _actual;
                if (_defaults == null) _defaults = CreateInstance<RetratoAsesinoConfig>();
                return _defaults;
            }
        }
    }
}
