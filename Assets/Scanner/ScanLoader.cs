using UnityEngine;

namespace Scanner
{
    // Carga un escaneo guardado a la escena: reconstruye walls/cubes/floor y
    // re-registra la imagen de referencia (entrando en Calibrating). Centraliza la
    // logica que antes vivia solo en SaveLoadUI.DoLoad, para que el import de un
    // archivo .MSCN (MscnReceiver) la reutilice.
    public static class ScanLoader
    {
        // Cuando es true, la reconstrucción NO crea las esferas-handle de edición de
        // paredes/cubos (WallObject/CubeObject lo consultan). Se usa al cargar un mapa
        // para gameplay multijugador, donde el mapa es solo visual (no editable).
        public static bool DisplayOnly { get; private set; }

        // Carga un escaneo "solo para mostrar": reconstruye paredes/cubos sin handles
        // de edición y registra la imagen de referencia para calibrar el anchor. Lo usa
        // el flujo multijugador (host y clientes) sobre el mapa compartido.
        public static bool LoadForDisplay(string name, ARImageAnchor imageAnchor = null)
        {
            DisplayOnly = true;
            try { return Load(name, imageAnchor); }
            finally { DisplayOnly = false; }
        }

        public static bool Load(string name, ARImageAnchor imageAnchor = null)
        {
            var data = ScanSerializer.Load(name);
            if (data == null) return false;
            if (SceneRegistry.Instance == null)
            {
                Debug.LogWarning("[ScanLoader] SceneRegistry no esta listo todavia.");
                return false;
            }

            SceneRegistry.Instance.ClearAll();
            if (data.walls != null) foreach (var w in data.walls) WallObject.FromData(w);
            if (data.cubes != null) foreach (var c in data.cubes) CubeObject.FromData(c);
            if (data.hasFloor && data.floorLocal != null)
                FloorPoint.Create(data.floorLocal.ToVector3());

            // Re-registrar la imagen de referencia y volver a calibrar: al enfocar la
            // zona fisica, ARKit/ARCore reposiciona el anchor. keepVisualPosition:true
            // => lo recien cargado se queda donde esta hasta que aparezca el anchor.
            if (imageAnchor == null) imageAnchor = Object.FindFirstObjectByType<ARImageAnchor>();
            var refTex = ScanSerializer.LoadRefImage(name);
            if (refTex != null && imageAnchor != null)
            {
                CapturedReference.Set(refTex, data.refImageWidthMeters);
                imageAnchor.AddReferenceImage(refTex, name, data.refImageWidthMeters, keepVisualPosition: true);
                ScanStateMachine.Instance?.SetMode(ScannerMode.Calibrating);
            }
            return true;
        }
    }
}
