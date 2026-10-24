using Scanner;   // UIScale
using UnityEngine;
using UnityEngine.SceneManagement;
using T = MortuoriumTheme;

namespace Gameplay
{
    // Práctica PILAS Y CORDURA (segundo paso del TUTORIAL), en cuatro partes:
    //  1. GASTAR: la noche arranca con la batería al CargaInicial. La pista pide usar la luz
    //     intensa y una barra muestra el gasto por segundo, para que se vea cuánto más
    //     consume.
    //  2. LA PILA: con la batería vacía aparece UNA pila al toque, cerca
    //     (BatterySpawnManager.ServerSpawnUnaAhora), y una flecha lleva hasta ella
    //     (GuiaTutorial). No carga al toque: se congela el drenaje y aparece un aviso encima
    //     de la barra (Rediseno.Menus.AvisoPrimeraPila2) con lo que suma; al tocar
    //     CONTINUAR la barra se llena animada.
    //  3. CORDURA: aparece su barra (en las prácticas está oculta) con un aviso que la
    //     explica (Rediseno.Menus.AvisoCordura2): la bajan las criaturas, no la oscuridad.
    //  4. LA RELIQUIA: igual que la pila, una sola y al toque, con la flecha.
    // Las apariciones normales de pilas y reliquias quedan frenadas todo el paso
    // (Retenidas): en el tutorial no se espera ni se juntan de a muchas.
    // Al terminar, la noche se gana como un amanecer (ServerNightSurvived) y FinDeNoche2 la
    // muestra como paso superado. Con NightIndex = -1 no mueve progresión ni récords.
    //
    // El avance va en las filas 1-2 del HUD de la noche y una pista arriba dice qué hacer.
    // Se crea sola en SampleScene sólo en esta práctica y muere con la escena. Un jugador:
    // el host es el único dispositivo.
    public class TutorialPilasReliquias : MonoBehaviour
    {
        public const int MetaPilas = 1, MetaReliquias = 1;
        // Cada cuánto se vuelve a intentar dejar la pieza si todavía no hay dónde.
        private const float SegundosReintento = 0.5f;
        // Tras cuánto sin lugar se le explica al jugador por qué no aparece.
        private const float SegundosSinLugar = 4f;
        // Con la batería llena, vaciarla con la luz normal tardaría medio minuto.
        private const float CargaInicial = 0.3f;
        // Respiro entre la última pieza y el cartel de fin, para que se oiga el pickup.
        private const float DemoraFin = 1.5f;
        // Lo que tarda la barra en llenarse con la primera pila, tras CONTINUAR.
        private const float SegundosAnimacionCarga = 1.2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            AlCargar(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void AlCargar(Scene s, LoadSceneMode m)
        {
            var sesion = GameSession.Instance;
            if (s.name != SceneFlow.EscenaJuego || sesion == null ||
                sesion.Tutorial != TutorialTipo.PilasReliquias || Instance != null) return;
            new GameObject("TutorialPilasReliquias").AddComponent<TutorialPilasReliquias>();
        }

        public static TutorialPilasReliquias Instance { get; private set; }

        private enum Fase { Gastar, Pilas, Cordura, Reliquias }

        private Fase _fase;
        private int _pilas;
        private bool _enPartida;
        private bool _cargaInicialPuesta;
        private float _finEn = -1f;
        private Flashlight _linterna;

        // La pieza que hay que ir a buscar (la pila o la reliquia) y su guía.
        private uint _pieza;
        private bool _hayPieza;
        private float _reintentoEn, _buscandoLugarDesde = -1f;

        // Primera pila: la carga que se retuvo hasta CONTINUAR.
        private bool _primeraHecha, _esperandoAnimacion;
        private float _cargaRetenida;

        // Aviso de la primera pila (AvisoPrimeraPila2).
        public bool AvisoAbierto { get; private set; }
        // Aviso de la cordura (AvisoCordura2).
        public bool AvisoCorduraAbierto { get; private set; }
        public int PorcentajeRetenido =>
            Linterna != null && Linterna.maxCharge > 0f
                ? Mathf.RoundToInt(100f * Mathf.Min(_cargaRetenida, Linterna.maxCharge) / Linterna.maxCharge) : 0;

        private Flashlight Linterna => _linterna != null ? _linterna : _linterna = FindFirstObjectByType<Flashlight>();
        private static int Reliquias => NightLoot.Total;
        private bool Completa => _fase == Fase.Reliquias && Reliquias >= MetaReliquias;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Flashlight.PilaRecogida += AlRecogerPila;
            Flashlight.RetenerCarga = RetenerPrimeraCarga;
            Collectibles.CollectibleSpawnManager.Retenidas = true;
            Bateries.BatterySpawnManager.Retenidas = true;
            new GameObject("AvisoPrimeraPila2").AddComponent<Rediseno.Menus.AvisoPrimeraPila2>();
            new GameObject("AvisoCordura2").AddComponent<Rediseno.Menus.AvisoCordura2>();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Flashlight.PilaRecogida -= AlRecogerPila;
            if (Flashlight.RetenerCarga == RetenerPrimeraCarga) Flashlight.RetenerCarga = null;
            Collectibles.CollectibleSpawnManager.Retenidas = false;
            Bateries.BatterySpawnManager.Retenidas = false;
            SanityHUD.MostrarEnPractica = false;
            Rediseno.ControlMirada.ObjetivoIntensa = null;
            CerrarAviso();
            CerrarAvisoCordura();
            Instance = null;
        }

        private void AlRecogerPila()
        {
            if (_enPartida && _fase == Fase.Pilas) _pilas++;
        }

        // Flashlight.AddCharge pregunta antes de cargar: la primera pila se queda acá.
        private bool RetenerPrimeraCarga(float carga)
        {
            if (!_enPartida || _primeraHecha) return false;
            _primeraHecha = true;
            _cargaRetenida = carga;
            if (Linterna != null) Linterna.DrenajePausado = true;
            AvisoAbierto = true;
            Bateries.ContextActionController.AvisoAbierto = true;
            return true;
        }

        // CONTINUAR del aviso de la primera pila: ahora sí la barra sube (animada); el
        // drenaje vuelve cuando termina de llenarse.
        public void Continuar()
        {
            if (!AvisoAbierto) return;
            CerrarAviso();
            if (Linterna == null) return;
            Linterna.CargarAnimado(_cargaRetenida, SegundosAnimacionCarga);
            _esperandoAnimacion = true;
        }

        private void CerrarAviso()
        {
            AvisoAbierto = false;
            Bateries.ContextActionController.AvisoAbierto = AvisoCorduraAbierto;
        }

        // CONTINUAR del aviso de la cordura: siguen las reliquias.
        public void ContinuarCordura()
        {
            if (!AvisoCorduraAbierto) return;
            CerrarAvisoCordura();
            _fase = Fase.Reliquias;
            SoltarPieza();
        }

        private void CerrarAvisoCordura()
        {
            AvisoCorduraAbierto = false;
            Bateries.ContextActionController.AvisoAbierto = AvisoAbierto;
        }

        private void Update()
        {
            // El TUTORIAL pasó al paso siguiente (o a la Noche 1) sin recargar la escena.
            if (!Tutoriales.Vigente(TutorialTipo.PilasReliquias)) { Destroy(gameObject); return; }

            var net = NetworkManager.Instance;
            bool enPartida = net != null && net.GameStarted;
            // Flanco de subida: arranque en frío y cada REPETIR (no recarga la escena; la
            // linterna ya se reinició sola en OnGameStarted).
            if (enPartida && !_enPartida) Reiniciar();
            _enPartida = enPartida;
            if (!enPartida || !net.IsServer) return;

            // La linterna se llena en OnGameStarted; recién después se la deja baja.
            if (!_cargaInicialPuesta && Linterna != null)
            {
                _cargaInicialPuesta = true;
                Linterna.currentCharge = Linterna.maxCharge * CargaInicial;
            }

            if (_esperandoAnimacion && Linterna != null && !Linterna.CargandoAnimado)
            {
                _esperandoAnimacion = false;
                Linterna.DrenajePausado = false;
            }

            switch (_fase)
            {
                case Fase.Gastar:
                    // En el visor sin mando la intensa sólo sale mirando un objetivo: el libro.
                    var libro = RitualBookView.Active;
                    Rediseno.ControlMirada.ObjetivoIntensa = libro != null ? libro.PuntoDeLuz : (Vector3?)null;
                    if (libro != null) Rediseno.ControlMirada.RadioObjetivoIntensa = libro.RadioAproximado;
                    if (Linterna != null && Linterna.IsEmpty)
                    {
                        _fase = Fase.Pilas;
                        Rediseno.ControlMirada.ObjetivoIntensa = null;
                        SoltarPieza();
                    }
                    break;
                case Fase.Pilas:
                    if (_pilas < MetaPilas) DejarPieza(reliquia: false);
                    if (_pilas >= MetaPilas && !AvisoAbierto && !_esperandoAnimacion)
                    {
                        _fase = Fase.Cordura;
                        SanityHUD.MostrarEnPractica = true;
                        AvisoCorduraAbierto = true;
                        Bateries.ContextActionController.AvisoAbierto = true;
                    }
                    break;
            }

            if (_fase == Fase.Reliquias && Reliquias < MetaReliquias) DejarPieza(reliquia: true);

            if (_finEn < 0f)
            {
                if (Completa) _finEn = Time.time + DemoraFin;
            }
            else if (Time.time >= _finEn)
            {
                _finEn = -1f;
                net.ServerNightSurvived();
            }
        }

        // Deja la pieza de esta parte si todavía no está (reintenta mientras no haya dónde).
        private void DejarPieza(bool reliquia)
        {
            if (_hayPieza || Time.time < _reintentoEn) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (_buscandoLugarDesde < 0f) _buscandoLugarDesde = Time.time;
            Vector3 jugador = cam.transform.position;
            if (reliquia)
            {
                var m = Collectibles.CollectibleSpawnManager.Instance;
                _hayPieza = m != null && m.ServerSpawnUnaAhora(jugador, out _pieza);
            }
            else
            {
                var m = Bateries.BatterySpawnManager.Instance;
                _hayPieza = m != null && m.ServerSpawnUnaAhora(jugador, out _pieza);
            }
            if (_hayPieza) _buscandoLugarDesde = -1f;
            else _reintentoEn = Time.time + SegundosReintento;
        }

        private void SoltarPieza()
        {
            _hayPieza = false;
            _pieza = 0;
            _reintentoEn = 0f;
            _buscandoLugarDesde = -1f;
        }

        // ¿Dónde está la pieza ahora? False si ya no está (la recogió).
        private bool PosicionPieza(out Vector3 pos)
        {
            pos = default;
            var reg = EntityRegistry.Instance;
            if (!_hayPieza || reg == null || !reg.TryGet(_pieza, out var e) || e == null) return false;
            pos = e.transform.position;
            return true;
        }

        private bool SinLugar => !_hayPieza && _buscandoLugarDesde >= 0f &&
                                 Time.time - _buscandoLugarDesde > SegundosSinLugar;

        private void Reiniciar()
        {
            SoltarPieza();
            _fase = Fase.Gastar;
            _pilas = 0;
            _finEn = -1f;
            _cargaInicialPuesta = false;
            _primeraHecha = false;
            _esperandoAnimacion = false;
            _cargaRetenida = 0f;
            CerrarAviso();
            CerrarAvisoCordura();
            SanityHUD.MostrarEnPractica = false;
            Collectibles.CollectibleSpawnManager.Retenidas = true;
            Bateries.BatterySpawnManager.Retenidas = true;
        }

        private void OnGUI()
        {
            if (!_enPartida || Gamepad.PauseMenuController.IsOpen) return;
            if (LocalDeath.Instance != null && LocalDeath.Instance.IsDead) return;

            UIScale.Begin();
            float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;
            // Filas 1 y 2: las que usan cordura y amanecer en una noche normal. Desde la
            // parte de la cordura, la fila 1 es suya (SanityHUD).
            switch (_fase)
            {
                case Fase.Gastar:
                    float gasto = Linterna != null ? Linterna.CurrentDrainPerSecond : 0f;
                    float maxGasto = Linterna != null ? Mathf.Max(0.01f, Linterna.brightDrainPerSecond) : 1f;
                    bool intensa = Linterna != null && Linterna.Mode == FlashlightMode.Bright;
                    T.HudBarra(vw, vh, 1, gasto / maxGasto, intensa ? T.Red : T.Tan, "GASTO", $"-{gasto:0}/s");
                    break;
                case Fase.Pilas:
                    Fila(vw, vh, 1, "PILA", Mathf.Min(_pilas, MetaPilas), MetaPilas);
                    break;
                case Fase.Reliquias:
                    Fila(vw, vh, 2, "RELIQUIA", Mathf.Min(Reliquias, MetaReliquias), MetaReliquias);
                    break;
            }
            if (AvisoAbierto || AvisoCorduraAbierto) return;
            PistaTutorial.Dibujar(vw, Texto());
            if ((_fase == Fase.Pilas || _fase == Fase.Reliquias) && PosicionPieza(out var pos))
                GuiaTutorial.Dibujar(pos, _fase == Fase.Pilas ? "PILA" : "RELIQUIA");
        }

        private static string NoHayLugar(string que) =>
            $"No hay dónde dejar {que}: faltan piso o paredes que cierren el cuarto.";

        private static void Fila(float vw, float vh, int fila, string nombre, int n, int meta) =>
            T.HudBarra(vw, vh, fila, (float)n / meta, n >= meta ? T.Green : T.Tan, nombre, $"{n}/{meta}");

        private string Texto()
        {
            if (Completa) return "¡Paso completo!";
            switch (_fase)
            {
                case Fase.Gastar:
                    bool intensa = Linterna != null && Linterna.Mode == FlashlightMode.Bright;
                    if (intensa) return "La intensa gasta 4 veces más. Seguí hasta vaciarla.";
                    return Rediseno.ControlMirada.Activo
                        ? "Batería baja. Mirá el libro de frente para gastarla."
                        : $"Gastá la batería con luz intensa ({PistaTutorial.ComoLuzIntensa()}).";
                case Fase.Reliquias:
                    if (SinLugar) return NoHayLugar("la reliquia");
                    return "Seguí la flecha y recogé la reliquia.";
            }
            if (SinLugar) return NoHayLugar("la pila");
            return "Sin luz. Seguí la flecha y recogé la pila.";
        }
    }
}
