using UnityEngine;

namespace Scanner
{
    // Define un tipo de marcador (puerta, ventana, etc.) como ASSET editable desde
    // el editor de Unity. Para dar de alta uno nuevo NO hace falta tocar codigo ni
    // enums: Create > Scanner > Marker Type y sumarlo al MarkerCatalog.
    [CreateAssetMenu(fileName = "MarkerType", menuName = "Scanner/Marker Type")]
    public class MarkerType : ScriptableObject
    {
        [Tooltip("Id estable que se guarda en el escaneo. NO cambiarlo una vez usado " +
                 "(rompe los marcadores ya guardados). Ej: Door, Window.")]
        [SerializeField] private string id = "";

        [Tooltip("Nombre visible en la UI (ej: Puerta).")]
        [SerializeField] private string displayName = "";

        [Tooltip("Color de la esfera y la flecha del marcador.")]
        [SerializeField] private Color color = new Color(1f, 1f, 1f, 1f);

        // Si el id/displayName estan vacios, caemos al nombre del asset como fallback.
        public string Id          => string.IsNullOrEmpty(id) ? name : id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? Id : displayName;
        public Color  Color       => color;
    }
}
