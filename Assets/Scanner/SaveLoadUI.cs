using UnityEngine;
using T = MortuoriumTheme;

namespace Scanner
{
    // Overlay "GUARDAR ESCANEO" (estilo prototipo): nombre + GUARDAR (+ COMPARTIR
    // si el escaneo ya existe en disco). Lo abre el botón GUARDAR ESCANEO de la
    // botonera del escáner (ReticleController) vía SaveLoadUI.Abrir().
    //
    // La carga/borrado de escaneos ya NO vive acá: se hace desde el menú principal
    // (NightMenuUI -> pantalla "ESCANEO DE ENTORNO"), que entra a la ScannerScene
    // con ScannerLaunchParams.EditScanName seteado.
    public class SaveLoadUI : MonoBehaviour
    {
        [SerializeField] private ARImageAnchor _imageAnchor;

        // Nombre del escaneo cargado para editar en esta sesión (o null si es uno
        // nuevo). Lo setea ScannerSceneBootstrap al cargar; lo usa esta UI como
        // nombre por defecto y ReticleController para el título "EDITANDO ESCANEO".
        public static string NombreEdicion;

        private static SaveLoadUI _inst;

        private bool _abierto;
        private string _nombre = "";
        private string _flash;
        private float _flashUntil;

        private readonly Gamepad.ImguiGamepadMenu _nav = new();

        private const float Pad = 28f;

        private void OnEnable()
        {
            _inst = this;
            if (_imageAnchor == null) _imageAnchor = FindFirstObjectByType<ARImageAnchor>();
        }

        private void OnDisable()
        {
            if (_inst == this) _inst = null;
        }

        private void Update() => _nav.Update();

        // Abre el overlay con el nombre por defecto (el del escaneo en edición, o
        // "Escaneo N" si es nuevo).
        public static void Abrir()
        {
            if (_inst == null) return;
            _inst._abierto = true;
            _inst._nombre = !string.IsNullOrEmpty(NombreEdicion)
                ? NombreEdicion
                : $"Escaneo {ScanSerializer.ListSaved().Count + 1}";
        }

        public static bool EstaAbierto => _inst != null && _inst._abierto;

        private void OnGUI()
        {
            UIScale.Begin();
            float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;

            // Mensaje flash (guardado/compartido) visible aún con el overlay cerrado.
            if (!string.IsNullOrEmpty(_flash) && Time.time < _flashUntil)
                GUI.Label(new Rect(0, vh * 0.30f, vw, 30f), _flash,
                          T.Estilo(T.FMono, 14, T.Tan, TextAnchor.MiddleCenter));

            if (!_abierto) return;

            _nav.Begin();

            var full = new Rect(0, 0, vw, vh);
            T.Fill(full, new Color(T.Bg.r, T.Bg.g, T.Bg.b, 0.94f));
            UIBlocker.AddVirtualRect(full);

            T.BotonVolver(_nav, () => _abierto = false);

            GUI.Label(new Rect(Pad, 90f, vw - Pad * 2f, 40f), "GUARDAR ESCANEO",
                      T.Estilo(T.FBebas, 28, T.Cream));
            GUI.Label(new Rect(Pad, 132f, vw - Pad * 2f, 22f),
                      "nombrá este entorno para encontrarlo luego",
                      T.Estilo(T.FElite, 12, T.Dim));

            _nombre = T.CampoTexto(new Rect(Pad, 170f, vw - Pad * 2f, 52f),
                                   _nombre, "ej: living de casa");

            // Aviso si el escaneo no tiene imagen de referencia asociada (sin ella
            // no se puede sincronizar en multijugador).
            bool hayImagen = CapturedReference.HasImage ||
                             (!string.IsNullOrEmpty(NombreEdicion) && ScanSerializer.HasRefImage(NombreEdicion));
            if (!hayImagen)
                GUI.Label(new Rect(Pad, 234f, vw - Pad * 2f, 44f),
                          "ojo: este escaneo no tiene imagen de referencia; sin ella no " +
                          "se puede usar en multijugador.",
                          T.Estilo(T.FMono, 11, T.Tan, TextAnchor.UpperLeft, wrap: true));

            float y = 292f;
            T.Boton(_nav, new Rect(Pad, y, vw - Pad * 2f, 56f), "GUARDAR", primario: true, Guardar);
            y += 68f;

            // Compartir (.mscn) solo si ya existe en disco.
            if (!string.IsNullOrEmpty(NombreEdicion))
                T.Boton(_nav, new Rect(Pad, y, vw - Pad * 2f, 50f), "COMPARTIR (.MSCN)",
                        primario: false, Compartir, fontSize: 16);

            _nav.End();
        }

        private void Guardar()
        {
            var nombre = (_nombre ?? "").Trim();
            if (string.IsNullOrEmpty(nombre)) { Flash("poné un nombre"); return; }
            if (SceneRegistry.Instance == null) { Flash("no hay nada que guardar"); return; }

            var data = SceneRegistry.Instance.Capture(nombre);
            // Persistimos también la imagen de referencia capturada en esta sesión
            // (si hay), para reconocer la zona física al recargar.
            data.refImageWidthMeters = CapturedReference.WidthMeters;
            if (CapturedReference.HasImage)
                ScanSerializer.SaveRefImage(nombre, CapturedReference.Texture);
            ScanSerializer.Save(nombre, data);
            NombreEdicion = nombre;

            // Flujo del prototipo: guardar cierra el escaneo y vuelve al menú.
            SceneFlow.GoTo(SceneFlow.EscenaMenu);
        }

        // Exporta el escaneo guardado a un .MSCN y abre la hoja de compartir.
        private void Compartir()
        {
            var path = ScanPackage.WriteTempFile(NombreEdicion);
            if (string.IsNullOrEmpty(path)) { Flash("no se pudo exportar"); return; }
            MscnShare.Share(path);
            Flash($"compartiendo '{NombreEdicion}'…");
        }

        private void Flash(string msg)
        {
            _flash = msg;
            _flashUntil = Time.time + 2.5f;
        }
    }
}
