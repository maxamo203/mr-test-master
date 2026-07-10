using System.Collections.Generic;
using UnityEngine;

namespace Scanner
{
    // Catalogo de tipos de marcador disponibles. Es un ASSET: agregar/quitar/ordenar
    // tipos se hace desde el editor arrastrando MarkerType a la lista. El
    // MarkerBuilder lo referencia (campo serializado) y lo publica en Active, para que
    // la carga de escaneos pueda resolver el tipo por id sin depender de la escena.
    [CreateAssetMenu(fileName = "MarkerCatalog", menuName = "Scanner/Marker Catalog")]
    public class MarkerCatalog : ScriptableObject
    {
        [SerializeField] private List<MarkerType> types = new List<MarkerType>();

        public IReadOnlyList<MarkerType> Types => types;

        // Catalogo activo en runtime (lo setea MarkerBuilder en Awake).
        public static MarkerCatalog Active { get; set; }

        public MarkerType GetById(string id)
        {
            if (string.IsNullOrEmpty(id) || types == null) return null;
            foreach (var t in types)
                if (t != null && t.Id == id) return t;
            return null;
        }

        public MarkerType First => (types != null && types.Count > 0) ? types[0] : null;
    }
}
