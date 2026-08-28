using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;
using Gameplay;   // VHSSettings, CameraFXOverlay
using T = MortuoriumTheme;

// US-11.1 — capa IMGUI del filtro VHS / cámara antigua. Complementa a CameraFXOverlay:
//
//   PARTIDA  → el filtro lo hace el shader (Assets/Resources/CameraFX.shader) sobre la
//              imagen 3D+cámara; acá sólo se dibuja el "REC + fecha" de camcorder, que
//              tiene que quedar NÍTIDO (si pasara por el shader se vería distorsionado).
//   MENÚS    → la IMGUI se dibuja DESPUÉS del render 3D, así que ningún GrabPass puede
//              alcanzarla: el filtro sobre los menús se compone acá, encima de todo
//              (scanlines, grano, banda de tracking, viñeta, tinte y REC). Lo único que
//              no se puede replicar sin shader es el warp de píxeles (jitter de línea).
//
// Es una opción de PRODUCCIÓN (GameOptions.VhsEnMenus): el jugador puede apagar el filtro
// sobre los menús sin tocar el de la partida. Los ingredientes se prenden/apagan por
// separado sólo en development build (ver VHSSettings).
//
// Se auto-crea al arrancar la app y sobrevive a los cambios de escena. El
// DefaultExecutionOrder alto hace que su OnGUI corra ÚLTIMO, así el filtro queda por
// encima de los menús en vez de debajo.
[DefaultExecutionOrder(10000)]
public class VHSOverlayUI : MonoBehaviour
{
    private static VHSOverlayUI _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("VHSOverlayUI");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<VHSOverlayUI>();
    }

    // Nombre de la escena activa: comparar Scene.name en cada OnGUI generaría basura por
    // frame, así que se cachea al cargar la escena.
    private bool _enMenu;
    // El Cardboard parte la pantalla en dos ojos: un "REC" mono cruzando el medio se ve
    // roto, así que ahí no se dibuja. Se resuelve una vez por escena.
    private MRCardboardController _cardboard;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        ResolverEscena(SceneManager.GetActiveScene());
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_instance == this) _instance = null;
    }

    private void OnSceneLoaded(Scene s, LoadSceneMode m) => ResolverEscena(s);

    private void ResolverEscena(Scene s)
    {
        _enMenu = s.name == SceneFlow.EscenaMenu;
        _cardboard = FindFirstObjectByType<MRCardboardController>();
    }

    private void OnGUI()
    {
        // Sólo pintar: en los eventos de layout/input no hay nada que hacer.
        if (Event.current.type != EventType.Repaint) return;

        // El menú de pausa es un menú aunque esté sobre la partida.
        bool menu = _enMenu || Gamepad.PauseMenuController.IsOpen;

        if (menu)
        {
            if (GameOptions.VhsEnMenus) DibujarCompuesto();
        }
        else if (CameraFXOverlay.EnPartida)
        {
            // El resto del filtro ya lo aplicó el shader sobre la imagen de la cámara.
            DibujarRec();
        }

        // El tema ya pinta scanlines en las pantallas opacas (MortuoriumTheme.FillScreen):
        // el flag evita dibujarlas dos veces. Se limpia acá porque este OnGUI corre último.
        T.LimpiarFlagScanlines();
    }

    // ── Composición sobre los menús (px crudos, sin la matriz de UIScale) ─────
    private void DibujarCompuesto()
    {
        var m = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;
        var full = new Rect(0f, 0f, Screen.width, Screen.height);
        var prevColor = GUI.color;

        float tinte = VHSSettings.AmtTinte;
        if (tinte > 0f)
        {
            var c = VHSSettings.ColorTinte;
            GUI.color = new Color(c.r, c.g, c.b, tinte * 0.5f);
            GUI.DrawTexture(full, Blanco());
        }

        // Las pantallas opacas del menú ya traen las scanlines del tema.
        float scan = VHSSettings.AmtScanlines;
        if (scan > 0f && !T.ScanlinesYaPintadas)
        {
            GUI.color = new Color(1f, 1f, 1f, scan);
            GUI.DrawTextureWithTexCoords(full, Scanlines(),
                                         new Rect(0f, 0f, 1f, Screen.height / 3f));
        }

        float grano = VHSSettings.AmtGrano;
        if (grano > 0f)
        {
            // Animación sin Random (no toca el estado global del RNG del juego): el
            // desplazamiento del tiling salta con el número de frame.
            float ox = Frac(Time.frameCount * 0.6180339f);
            float oy = Frac(Time.frameCount * 0.4142135f);
            GUI.color = new Color(1f, 1f, 1f, grano * 0.28f);
            GUI.DrawTextureWithTexCoords(full, Ruido(),
                new Rect(ox, oy, Screen.width / (float)RuidoLado, Screen.height / (float)RuidoLado));
        }

        float bandas = VHSSettings.AmtBandas;
        if (bandas > 0f)
        {
            // Banda de tracking subiendo. Sin GrabPass no se puede arrastrar la imagen:
            // se simula con el brillo de la banda + una línea de corte marcada.
            float h  = Screen.height * 0.11f;
            float y  = Screen.height * (1f - Frac(Time.unscaledTime * 0.11f)) - h * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, bandas * 0.10f);
            GUI.DrawTexture(new Rect(0f, y, Screen.width, h), Banda(), ScaleMode.StretchToFill);
            GUI.color = new Color(0f, 0f, 0f, bandas * 0.35f);
            GUI.DrawTexture(new Rect(0f, y + h * 0.5f, Screen.width, 2f), Blanco());
        }

        float vineta = VHSSettings.AmtVineta;
        if (vineta > 0f)
        {
            GUI.color = new Color(0f, 0f, 0f, vineta);
            GUI.DrawTexture(full, Vineta(), ScaleMode.StretchToFill);
        }

        GUI.color = prevColor;
        GUI.matrix = m;

        DibujarRec();
    }

    // ── "● REC" + fecha/hora de camcorder ─────────────────────────────────────
    private string _stamp;
    private float  _stampAt = -1f;

    private void DibujarRec()
    {
        if (!VHSSettings.Rec) return;
        if (_cardboard != null && _cardboard.CardboardActive) return;

        var m = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;

        // Dentro del área segura: en iPhone el notch se come la esquina superior.
        var safe = Scanner.SafeArea.GuiRect;
        float pad = Mathf.Max(12f, Screen.width * 0.03f);
        int   fs  = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.018f, 11f, 26f));

        // Parpadeo del punto rojo (~1 s), como una cámara grabando.
        bool on = Frac(Time.unscaledTime) < 0.65f;
        var prevColor = GUI.color;
        if (on)
        {
            GUI.color = new Color(0.85f, 0.12f, 0.12f, 0.9f);
            float d = fs * 0.7f;
            GUI.DrawTexture(new Rect(safe.x + pad, safe.y + pad + fs * 0.2f, d, d), Blanco());
        }
        GUI.color = prevColor;

        var st = T.Estilo(T.FMono, fs, new Color(0.90f, 0.88f, 0.82f, 0.75f));
        GUI.Label(new Rect(safe.x + pad + fs, safe.y + pad, 200f, fs * 1.6f), "REC", st);

        if (Time.unscaledTime - _stampAt > 0.5f)
        {
            _stampAt = Time.unscaledTime;
            _stamp = System.DateTime.Now.ToString("MMM dd yyyy  HH:mm:ss",
                                                 CultureInfo.InvariantCulture).ToUpperInvariant();
        }
        var stR = T.Estilo(T.FMono, fs, new Color(0.90f, 0.88f, 0.82f, 0.70f),
                           TextAnchor.MiddleRight);
        GUI.Label(new Rect(safe.xMax - pad - 320f, safe.yMax - pad - fs * 1.6f, 320f, fs * 1.6f),
                  _stamp, stR);

        GUI.matrix = m;
    }

    private static float Frac(float v) => v - Mathf.Floor(v);

    // ── Texturas procedurales (una sola vez, sin assets) ──────────────────────
    private const int RuidoLado = 128;
    private static Texture2D _blanco, _scanlines, _ruido, _vineta, _banda;

    private static Texture2D Blanco()
    {
        if (_blanco == null) _blanco = Solida(Color.white);
        return _blanco;
    }

    private static Texture2D Solida(Color c)
    {
        var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    private static Texture2D Scanlines()
    {
        if (_scanlines == null)
        {
            _scanlines = new Texture2D(1, 3, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode   = TextureWrapMode.Repeat,
                hideFlags  = HideFlags.HideAndDontSave,
            };
            _scanlines.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
            _scanlines.SetPixel(0, 1, new Color(0f, 0f, 0f, 0f));
            _scanlines.SetPixel(0, 2, new Color(0f, 0f, 0f, 0.55f));
            _scanlines.Apply();
        }
        return _scanlines;
    }

    private static Texture2D Ruido()
    {
        if (_ruido == null)
        {
            _ruido = new Texture2D(RuidoLado, RuidoLado, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode   = TextureWrapMode.Repeat,
                hideFlags  = HideFlags.HideAndDontSave,
            };
            // Estado propio del RNG: sembrarlo acá no puede alterar la secuencia que use
            // el gameplay (spawns, variaciones de audio...).
            var estado = Random.state;
            Random.InitState(9174);
            var px = new Color32[RuidoLado * RuidoLado];
            for (int i = 0; i < px.Length; i++)
            {
                byte v = (byte)Random.Range(0, 256);
                px[i] = new Color32(v, v, v, (byte)(v > 128 ? 255 : 90));
            }
            _ruido.SetPixels32(px);
            _ruido.Apply();
            Random.state = estado;
        }
        return _ruido;
    }

    private static Texture2D Vineta()
    {
        if (_vineta == null)
        {
            const int L = 64;
            _vineta = new Texture2D(L, L, TextureFormat.RGBA32, false)
            {
                wrapMode  = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color[L * L];
            for (int y = 0; y < L; y++)
            for (int x = 0; x < L; x++)
            {
                // Radio normalizado (1 = esquina). Arranca a oscurecer recién pasada la
                // mitad, si no la viñeta se come el contenido del menú.
                float dx = (x + 0.5f) / L - 0.5f, dy = (y + 0.5f) / L - 0.5f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy) / 0.7071f;
                float a  = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d));
                px[y * L + x] = new Color(0f, 0f, 0f, a * 0.75f);
            }
            _vineta.SetPixels(px);
            _vineta.Apply();
        }
        return _vineta;
    }

    private static Texture2D Banda()
    {
        if (_banda == null)
        {
            const int H = 32;
            _banda = new Texture2D(1, H, TextureFormat.RGBA32, false)
            {
                wrapMode  = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            for (int y = 0; y < H; y++)
            {
                float t = (float)y / (H - 1);
                float a = Mathf.Sin(t * Mathf.PI);   // 0 en los bordes, 1 en el centro
                _banda.SetPixel(0, y, new Color(1f, 1f, 1f, a * a));
            }
            _banda.Apply();
        }
        return _banda;
    }
}
