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

        private void Update()
        {
            _nav.Update();
            // Teclado nativo abierto: el nombre es lo que tenga su caja de texto (que maneja el
            // cursor y el borrado mantenido). Al cerrarlo (listo, cancelar, tocar afuera) se suelta.
            if (_kb != null)
            {
                if (_kb.text != null) _nombre = _kb.text;
                if (_kb.status != TouchScreenKeyboard.Status.Visible) _kb = null;
            }
        }

        // ── Campo del nombre en el celular ─────────────────────────────────────
        // Con GUI.TextField el teclado le pasaba las teclas al IMGUI de a una: no se veía el
        // cursor y mantener BORRAR no repetía. Acá se abre el teclado nativo a mano, con su
        // propia caja de texto visible (cursor, borrado continuo, selección), y en el campo se
        // dibuja el texto con un cursor que parpadea mientras se edita. En el editor no hay
        // teclado nativo: sigue el TextField de siempre.
        private TouchScreenKeyboard _kb;
        private static bool TecladoNativo => TouchScreenKeyboard.isSupported && !Application.isEditor;
        private const string Ejemplo = "Ej: living de casa";

        private void CampoNombre(Rect r)
        {
            if (!TecladoNativo) { _nombre = T.CampoTexto2(r, _nombre, Ejemplo); return; }

            bool editando = _kb != null && _kb.status == TouchScreenKeyboard.Status.Visible;
            var texto = _nombre ?? "";
            var st = T.Estilo(T.FMono, 15, T.Cream);
            if (Event.current.type == EventType.Repaint)
            {
                T.Tarjeta2(r, T.BgField, editando ? T.Tan : null);
                if (editando && Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f)
                {
                    float x = r.x + 14f + Mathf.Min(st.CalcSize(new GUIContent(texto)).x, r.width - 30f);
                    T.FillR(new Rect(x + 1f, r.y + r.height * 0.25f, 2f, r.height * 0.5f), T.Cream, 1f);
                }
            }
            var dentro = new Rect(r.x + 14f, r.y, r.width - 28f, r.height);
            if (texto.Length > 0) GUI.Label(dentro, texto, st);
            else GUI.Label(dentro, Ejemplo, T.Estilo(T.FMono, 15, T.Dim));

            if (GUI.Button(r, GUIContent.none, GUIStyle.none) && !editando) AbrirTeclado();
        }

        private void AbrirTeclado()
        {
            TouchScreenKeyboard.hideInput = false;   // la caja nativa trae cursor y borrado continuo
            var texto = _nombre ?? "";
            _kb = TouchScreenKeyboard.Open(texto, TouchScreenKeyboardType.Default,
                                           autocorrection: false, multiline: false, secure: false,
                                           alert: false, textPlaceholder: Ejemplo);
            if (_kb != null) _kb.selection = new RangeInt(texto.Length, 0);   // cursor al final
        }

        private void CerrarTeclado()
        {
            if (_kb != null) { _kb.active = false; _kb = null; }
        }

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

            // Fondo opaco en toda la pantalla (incluye fuera del área segura).
            T.FillScreen(T.Bg);
            UIBlocker.AddVirtualRect(new Rect(0, 0, vw, vh));

            T.BotonVolver2(_nav, () => { CerrarTeclado(); _abierto = false; });

            GUI.Label(new Rect(Pad, 90f, vw - Pad * 2f, 40f), "GUARDAR ESCANEO",
                      T.Estilo(T.FBebas, 28, T.Cream));
            GUI.Label(new Rect(Pad, 132f, vw - Pad * 2f, 22f),
                      "Nombrá este entorno para encontrarlo luego",
                      T.Estilo(T.FElite, 12, T.Dim));

            CampoNombre(new Rect(Pad, 170f, vw - Pad * 2f, 52f));

            // Aviso si el escaneo no tiene imagen de referencia asociada (sin ella
            // no se puede sincronizar en multijugador).
            bool hayImagen = CapturedReference.HasImage ||
                             (!string.IsNullOrEmpty(NombreEdicion) && ScanSerializer.HasRefImage(NombreEdicion));
            if (!hayImagen)
                GUI.Label(new Rect(Pad, 234f, vw - Pad * 2f, 44f),
                          "Ojo: este escaneo no tiene imagen de referencia; sin ella no " +
                          "se va a poder jugar (ni solo ni en multijugador). Capturala antes de guardar.",
                          T.Estilo(T.FMono, 11, T.Tan, TextAnchor.UpperLeft, wrap: true));

            float y = 292f;
            T.Boton2(_nav, new Rect(Pad, y, vw - Pad * 2f, 56f), "GUARDAR", primario: true, Guardar);
            y += 68f;

            // Compartir (.mscn) solo si ya existe en disco.
            if (!string.IsNullOrEmpty(NombreEdicion))
                T.Boton2(_nav, new Rect(Pad, y, vw - Pad * 2f, 50f), "COMPARTIR (.MSCN)",
                        primario: false, Compartir, fontSize: 16);

            _nav.End();
        }

        private void Guardar()
        {
            if (_kb != null && _kb.text != null) _nombre = _kb.text;
            CerrarTeclado();
            var nombre = (_nombre ?? "").Trim();
            if (string.IsNullOrEmpty(nombre)) { Flash("Poné un nombre"); return; }
            if (SceneRegistry.Instance == null) { Flash("No hay nada que guardar"); return; }

            var data = SceneRegistry.Instance.Capture(nombre);
            // Persistimos también la imagen de referencia capturada en esta sesión
            // (si hay), para reconocer la zona física al recargar.
            data.refImageWidthMeters = CapturedReference.WidthMeters;
            data.refImageOrientation = (int)CapturedReference.Orientacion;
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
            if (string.IsNullOrEmpty(path)) { Flash("No se pudo exportar"); return; }
            MscnShare.Share(path);
            Flash($"Compartiendo '{NombreEdicion}'…");
        }

        private void Flash(string msg)
        {
            _flash = msg;
            _flashUntil = Time.time + 2.5f;
        }
    }
}
