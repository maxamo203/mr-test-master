using Scanner;   // UIScale
using UnityEngine;
using UnityEngine.SceneManagement;
using T = MortuoriumTheme;

namespace Gameplay
{
    // Práctica PILAS Y RELIQUIAS, en dos partes:
    //  1. PILAS: juntar MetaPilas. La PRIMERA no carga al toque: se congela el drenaje y
    //     aparece un aviso encima de la barra (Rediseno.Menus.AvisoPrimeraPila2) con lo que
    //     suma; al tocar CONTINUAR la barra se llena animada y el drenaje sigue.
    //  2. RELIQUIAS: recién ahí se sueltan (CollectibleSpawnManager.Retenidas) y hay que
    //     juntar MetaReliquias.
    // Al terminar, la noche se gana como un amanecer (ServerNightSurvived) y FinDeNoche2 la
    // muestra como PRÁCTICA COMPLETA. Con NightIndex = -1 no mueve progresión ni récords.
    //
    // El avance va en las filas 1-2 del HUD de la noche (la cordura acá no se usa) y una
    // pista arriba dice qué hacer. Se crea sola en SampleScene sólo en esta práctica y
    // muere con la escena. Un jugador: el host es el único dispositivo.
    public class TutorialPilasReliquias : MonoBehaviour
    {
        public const int MetaPilas = 3, MetaReliquias = 3;
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

        private enum Fase { Pilas, Reliquias }

        private Fase _fase;
        private int _pilas;
        private bool _enPartida;
        private float _finEn = -1f;
        private Flashlight _linterna;

        // Primera pila: la carga que se retuvo hasta CONTINUAR.
        private bool _primeraHecha, _esperandoAnimacion;
        private float _cargaRetenida;

        public bool AvisoAbierto { get; private set; }
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
            new GameObject("AvisoPrimeraPila2").AddComponent<Rediseno.Menus.AvisoPrimeraPila2>();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Flashlight.PilaRecogida -= AlRecogerPila;
            if (Flashlight.RetenerCarga == RetenerPrimeraCarga) Flashlight.RetenerCarga = null;
            Collectibles.CollectibleSpawnManager.Retenidas = false;
            CerrarAviso();
            Instance = null;
        }

        private void AlRecogerPila()
        {
            if (_enPartida) _pilas++;
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

        // CONTINUAR del aviso: ahora sí la barra sube (animada); el drenaje vuelve cuando
        // termina de llenarse.
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
            Bateries.ContextActionController.AvisoAbierto = false;
        }

        private void Update()
        {
            var net = NetworkManager.Instance;
            bool enPartida = net != null && net.GameStarted;
            // Flanco de subida: arranque en frío y cada REPETIR (no recarga la escena; la
            // linterna ya se reinició sola en OnGameStarted).
            if (enPartida && !_enPartida) Reiniciar();
            _enPartida = enPartida;
            if (!enPartida || !net.IsServer) return;

            if (_esperandoAnimacion && Linterna != null && !Linterna.CargandoAnimado)
            {
                _esperandoAnimacion = false;
                Linterna.DrenajePausado = false;
            }

            if (_fase == Fase.Pilas && _pilas >= MetaPilas && !AvisoAbierto)
            {
                _fase = Fase.Reliquias;
                Collectibles.CollectibleSpawnManager.Retenidas = false;
            }

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

        private void Reiniciar()
        {
            _fase = Fase.Pilas;
            _pilas = 0;
            _finEn = -1f;
            _primeraHecha = false;
            _esperandoAnimacion = false;
            _cargaRetenida = 0f;
            CerrarAviso();
            Collectibles.CollectibleSpawnManager.Retenidas = true;
        }

        private void OnGUI()
        {
            if (!_enPartida || Gamepad.PauseMenuController.IsOpen) return;
            if (LocalDeath.Instance != null && LocalDeath.Instance.IsDead) return;

            UIScale.Begin();
            float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;
            // Filas 1 y 2: las que usan cordura y amanecer en una noche normal (acá no hay).
            // Las reliquias aparecen recién en su parte.
            Fila(vw, vh, 1, "PILAS", Mathf.Min(_pilas, MetaPilas), MetaPilas);
            if (_fase == Fase.Reliquias)
                Fila(vw, vh, 2, "RELIQUIAS", Mathf.Min(Reliquias, MetaReliquias), MetaReliquias);
            if (!AvisoAbierto) PistaTutorial.Dibujar(vw, Texto());
        }

        private static void Fila(float vw, float vh, int fila, string nombre, int n, int meta) =>
            T.HudBarra(vw, vh, fila, (float)n / meta, n >= meta ? T.Green : T.Tan, nombre, $"{n}/{meta}");

        private string Texto()
        {
            if (Completa) return "¡Práctica completa!";
            if (_fase == Fase.Reliquias)
                return "Ahora las reliquias del ritual: aparecen de a una. Buscala, apuntale y recogela.";
            if (_pilas == 0)
                return "La linterna se gasta. Buscá una pila en el piso: apuntale y recogela.";
            int faltan = MetaPilas - _pilas;
            return faltan == 1 ? "Bien. Juntá una pila más." : $"Bien. Juntá {faltan} pilas más.";
        }
    }
}
