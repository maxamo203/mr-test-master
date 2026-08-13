using UnityEngine;

namespace Gameplay
{
    // US-11.1 — configuracion del filtro VHS / camara antigua. Traduce "que ingredientes
    // estan prendidos y con cuanta fuerza" a los uniforms globales que lee
    // Assets/Resources/CameraFX.shader (partida) y VHSOverlayUI (menus).
    //
    // La fuerza de cada ingrediente se regula por separado (0 = apagado) SOLO en
    // development build (pausa -> Opciones -> VHS (DEV)), para poder calibrar en el
    // celular cual mezcla queda mejor; en release las propiedades nunca se escriben, asi
    // que Publicar() corre una unica vez al arrancar y despues no hay costo por frame. Lo
    // unico que el jugador toca en release es GameOptions.VhsEnMenus (el filtro sobre los
    // menus, on/off).
    public static class VHSSettings
    {
        private static readonly int ID_AMOUNT = Shader.PropertyToID("_VhsAmount");
        private static readonly int ID_COLOR  = Shader.PropertyToID("_VhsColor");
        private static readonly int ID_TINT   = Shader.PropertyToID("_VhsTintColor");

        // Cuanto refuerza la tension (US-11.2) a los ingredientes VHS: 0 = VHS parejo,
        // 1 = al maximo de tension los ingredientes valen el doble.
        private const float RefuerzoTension = 1.0f;

        // Tinte de cinta vieja (verdoso apagado). Multiplica al luma, asi que valores
        // cerca de 1 = poco tinte.
        private static readonly Color ColorCinta = new Color(0.82f, 0.95f, 0.78f);

        // ── Estado (campos, no PlayerPrefs por frame) ─────────────────────────
        // Fuerza de cada ingrediente, 0..1 (0 = apagado). Los defaults son la calibracion
        // que se envia: el efecto se tiene que leer sin tapar la escena (juego a oscuras).
        private static float _scanlines  = 0.70f;
        private static float _grano      = 0.55f;
        private static float _bandas     = 0.60f;
        private static float _jitter     = 0.50f;
        private static float _vineta     = 0.65f;
        private static float _tinte      = 0.28f;
        private static float _intensidad = 1f;
        // El "REC + fecha" no tiene intensidad: es un cartel, esta o no esta.
        private static bool  _rec        = true;

        private const string KeyPrefijo = "vhs_dev_";

        static VHSSettings()
        {
            // La persistencia de la calibracion es una comodidad de desarrollo (el tester
            // reinicia la app y conserva la mezcla que estaba probando). En release nadie
            // la cambia: se queda en los defaults de arriba.
            if (Debug.isDebugBuild)
            {
                _scanlines  = Leer("scanlines",  _scanlines);
                _grano      = Leer("grano",      _grano);
                _bandas     = Leer("bandas",     _bandas);
                _jitter     = Leer("jitter",     _jitter);
                _vineta     = Leer("vineta",     _vineta);
                _tinte      = Leer("tinte",      _tinte);
                _intensidad = Leer("intensidad", _intensidad);
                _rec        = PlayerPrefs.GetInt(KeyPrefijo + "rec", _rec ? 1 : 0) == 1;
            }
            Publicar();
        }

        private static float Leer(string k, float def) => PlayerPrefs.GetFloat(KeyPrefijo + k, def);

        // Setter comun: clampea, persiste (solo en dev) y republica los uniforms.
        private static void Set(ref float campo, string key, float valor)
        {
            campo = Mathf.Clamp01(valor);
            if (Debug.isDebugBuild)
            {
                PlayerPrefs.SetFloat(KeyPrefijo + key, campo);
                PlayerPrefs.Save();
            }
            Publicar();
        }

        public static float Scanlines { get => _scanlines; set => Set(ref _scanlines, "scanlines", value); }
        public static float Grano     { get => _grano;     set => Set(ref _grano,     "grano",     value); }
        public static float Bandas    { get => _bandas;    set => Set(ref _bandas,    "bandas",    value); }
        public static float Jitter    { get => _jitter;    set => Set(ref _jitter,    "jitter",    value); }
        public static float Vineta    { get => _vineta;    set => Set(ref _vineta,    "vineta",    value); }
        public static float Tinte     { get => _tinte;     set => Set(ref _tinte,     "tinte",     value); }

        // Multiplicador global de todos los ingredientes (el REC no depende de esto):
        // permite subir/bajar el filtro entero sin perder la mezcla relativa.
        public static float Intensidad { get => _intensidad; set => Set(ref _intensidad, "intensidad", value); }

        // El "REC + fecha" no es parte del shader: lo dibuja VHSOverlayUI en IMGUI.
        public static bool Rec
        {
            get => _rec;
            set
            {
                _rec = value;
                if (!Debug.isDebugBuild) return;
                PlayerPrefs.SetInt(KeyPrefijo + "rec", value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        // ¿Hay algo que dibujar? Si no, CameraFXOverlay puede apagar el quad y ahorrarse
        // el GrabPass entero cuando ademas no hay tension.
        public static bool Activo =>
            _intensidad > 0.001f &&
            (_scanlines + _grano + _bandas + _jitter + _vineta + _tinte) > 0.001f;

        // Intensidad efectiva de cada ingrediente (0 = apagado), para el overlay IMGUI
        // de los menus, que no pasa por el shader.
        public static float AmtScanlines => _scanlines * _intensidad;
        public static float AmtGrano     => _grano     * _intensidad;
        public static float AmtBandas    => _bandas    * _intensidad;
        public static float AmtJitter    => _jitter    * _intensidad;
        public static float AmtVineta    => _vineta    * _intensidad;
        public static float AmtTinte     => _tinte     * _intensidad;
        public static Color ColorTinte   => ColorCinta;

        // Empuja el estado a los uniforms globales del shader. Se llama SOLO cuando algo
        // cambia (y una vez al arrancar), nunca por frame.
        public static void Publicar()
        {
            Shader.SetGlobalVector(ID_AMOUNT,
                new Vector4(AmtScanlines, AmtGrano, AmtBandas, AmtJitter));
            Shader.SetGlobalVector(ID_COLOR,
                new Vector4(AmtVineta, AmtTinte, RefuerzoTension, 0f));
            Shader.SetGlobalVector(ID_TINT,
                new Vector4(ColorCinta.r, ColorCinta.g, ColorCinta.b, 1f));
        }

        // Fuerza la inicializacion del static ctor (publica los uniforms) sin esperar a
        // que alguien lea una propiedad.
        public static void Ensure() => Publicar();
    }
}
