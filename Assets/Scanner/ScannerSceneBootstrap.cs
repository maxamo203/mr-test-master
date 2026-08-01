using UnityEngine;

namespace Scanner
{
    // Punto de entrada de la ScannerScene. Orquesta el orden de inicializacion:
    //   1) Asegura que existan WorldOrigin, ScanStateMachine, SceneRegistry,
    //      RaycastResolver y TransformGizmoController.
    //   2) Suscribe a ARImageAnchor.OnImageReacquired para pasar de Calibrating
    //      a Idle automaticamente.
    //   3) Arranca el image tracking.
    //
    // Pegalo en un GameObject "ScannerRoot" en la escena junto con:
    //   ScanStateMachine, SceneRegistry, RaycastResolver, TransformGizmoController,
    //   WallBuilder, DoorBuilder, CubeBuilder, MarkerBuilder, ReticleController,
    //   EditPanelUI, SaveLoadUI, RecalibrateButton, SelectionController.
    [DefaultExecutionOrder(-100)]
    public class ScannerSceneBootstrap : MonoBehaviour
    {
        [SerializeField] private ARImageAnchor _imageAnchor;

        private void Start()
        {
            // El proyecto tiene Physics.autoSyncTransforms = false en
            // DynamicsManager.asset. Para que los SphereColliders de las
            // esferas-handle se ubiquen donde el transform los puso (sin esperar
            // un FixedUpdate), forzamos autoSync = true en runtime.
            Physics.autoSyncTransforms = true;

            // WorldOrigin es singleton — si la escena no lo tiene, lo creamos.
            // El que tenia la SampleScene vivia en ARLobbyRoot (escena vieja del
            // multijugador), asi que en la ScannerScene puede no estar.
            if (WorldOrigin.Instance == null)
            {
                var go = new GameObject("WorldOrigin");
                go.AddComponent<WorldOrigin>();
                Debug.Log("[ScannerSceneBootstrap] WorldOrigin creado en runtime.");
            }

            // Receptor de archivos .MSCN ("abrir con"): importa y carga el escaneo.
            MscnReceiver.Ensure();

            if (_imageAnchor == null) _imageAnchor = FindFirstObjectByType<ARImageAnchor>();
            if (_imageAnchor == null)
            {
                Debug.LogError("[ScannerSceneBootstrap] No hay ARImageAnchor en la escena.");
                return;
            }

            _imageAnchor.OnImageReacquired += OnAnchorReady;

            // FSM arranca en Calibrating. NO arrancamos el tracking acá: primero el
            // usuario captura un fragmento con la cámara (ReferenceCaptureUI), o
            // carga un escaneo guardado. En ambos casos ARImageAnchor.AddReferenceImage
            // registra la imagen y arranca la detección. Mientras tanto se muestra
            // la UI de captura porque la FSM está en Calibrating.
            ScanStateMachine.Instance?.SetMode(ScannerMode.Calibrating);

            // Si el menú principal pidió EDITAR un escaneo guardado, cargarlo ya
            // (reconstruye la escena y re-registra la imagen de referencia; queda
            // en Calibrating hasta reencontrar la zona física).
            var editar = ScannerLaunchParams.ConsumeEditScanName();
            SaveLoadUI.NombreEdicion = null;
            if (!string.IsNullOrEmpty(editar) && ScanLoader.Load(editar, _imageAnchor))
                SaveLoadUI.NombreEdicion = editar;
        }

        private void OnAnchorReady()
        {
            var fsm = ScanStateMachine.Instance;
            if (fsm == null) return;
            if (fsm.Current != ScannerMode.Calibrating) return;

            // Con la opción de anchor points activada, apenas sincroniza con la imagen
            // se entra DIRECTO a colocarlas: es el momento en que la alineación está
            // fresca, que es justo lo que el ancla #1 tiene que capturar. Recién
            // después (LISTO / OMITIR) se habilita el resto del escáner.
            if (GameOptions.PuntosAncla)
            {
                var anclas = AnchorPointManager.Ensure();
                if (!anclas.Listo)
                {
                    anclas.AbrirColocacion();
                    fsm.SetMode(ScannerMode.Anchor_Place);
                    return;
                }
            }
            fsm.SetMode(ScannerMode.Idle);
        }

        private void OnDestroy()
        {
            if (_imageAnchor != null) _imageAnchor.OnImageReacquired -= OnAnchorReady;
        }
    }
}
