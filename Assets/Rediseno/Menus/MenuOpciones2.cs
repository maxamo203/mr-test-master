using Gameplay;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // OPCIONES del menú principal, Rediseño 2.0 (UI Toolkit). Se muestra en la pantalla
    // Opciones de NightMenuUI y funciona con el visor puesto: con mando ↑↓ recorre, ←/→
    // ajustan los volúmenes, → activa; con la mirada se miran los interruptores o los
    // botones − / + de cada volumen.
    //
    // CONTROL (MANDO) lleva al visualizador del mando, que sigue en IMGUI (NightMenuUI).
    public class MenuOpciones2 : PantallaUI
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            AlCargar(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static MenuOpciones2 _instancia;

        private static void AlCargar(Scene s, LoadSceneMode m)
        {
            if (s.name != SceneFlow.EscenaMenu || _instancia != null) return;
            new GameObject("MenuOpciones2").AddComponent<MenuOpciones2>();
        }

        private Texture2D _arte;
        private Label _estadoMando, _progreso;
        private float _refresco;

        protected override void Awake()
        {
            if (_instancia != null && _instancia != this) { Destroy(gameObject); return; }
            _instancia = this;
            base.Awake();
            _arte = Resources.Load<Texture2D>("Rediseno/fondo_veleth");
        }

        protected override void OnDestroy()
        {
            if (_instancia == this) _instancia = null;
            base.OnDestroy();
        }

        protected override bool EsVisible()
        {
            var menu = NightMenuUI.Instance;
            return menu != null && menu.Mostrando(NightMenuUI.Pantalla.Opciones);
        }

        protected override Texture FondoVisor => _arte;

        private VisualElement _fondo;
        protected override void AlCambiarVisor(bool visor)
        {
            if (_fondo != null) _fondo.style.display = visor ? DisplayStyle.None : DisplayStyle.Flex;
        }

        protected override void Construir(VisualElement raiz, MenuNavegable nav)
        {
            _fondo = PiezasUI.Fondo(_arte);
            raiz.Add(_fondo);

            var col = PiezasUI.Columna();
            raiz.Add(col);
            col.Add(PiezasUI.Encabezado("OPCIONES", out var volver));

            var lista = PiezasUI.Lista();
            Desplazable = lista;
            col.Add(lista);

            lista.Add(PiezasUI.Seccion("SONIDO"));
            lista.Add(PiezasUI.Deslizador(nav, "VOLUMEN", () => GameOptions.Volumen, v => GameOptions.Volumen = v));
            lista.Add(PiezasUI.Deslizador(nav, "MÚSICA", () => GameOptions.VolumenMusica, v => GameOptions.VolumenMusica = v));
            lista.Add(PiezasUI.Deslizador(nav, "EFECTOS", () => GameOptions.VolumenEfectos, v => GameOptions.VolumenEfectos = v));

            lista.Add(PiezasUI.Seccion("JUEGO"));
            lista.Add(PiezasUI.Interruptor(nav, "PUNTOS DE ANCLAJE",
                "Colocá anclas en tu cuarto antes de empezar (menos deriva)",
                () => GameOptions.PuntosAncla, v => GameOptions.PuntosAncla = v));
            if (GameOptions.AutoWallScanBetaEnabled)
                lista.Add(PiezasUI.Interruptor(nav, "PARED AUTOMÁTICA (BETA)",
                    "En el escáner, sugiere paredes detectadas por la cámara",
                    () => GameOptions.EscaneoAutoBeta, v => GameOptions.EscaneoAutoBeta = v));
            lista.Add(PiezasUI.Interruptor(nav, "FILTRO VHS",
                "Grano, líneas y distorsión de cámara (no afecta la oscuridad)",
                () => GameOptions.VhsActivo, v => GameOptions.VhsActivo = v));

            // Calibración del visor: el menú también se usa con el Cardboard puesto, y sin
            // esto las dos imágenes podían no fusionarse. Mismas claves que la calibración
            // de la partida (CardboardCalibrationUI), así vale para las dos.
            lista.Add(PiezasUI.Seccion("VISOR (CARDBOARD)"));
            lista.Add(PiezasUI.Interruptor(nav, "USAR EL VISOR",
                "Todo se ve en estéreo, una imagen por ojo",
                () => ModoVisor.Activo, v => ModoVisor.Set(v)));
            lista.Add(PiezasUI.Interruptor(nav, "MENÚS QUIETOS EN EL CUARTO",
                "Apagado: los menús quedan fijos delante de cada ojo y se usan con el joystick. " +
                "Prendido: quedan quietos en el cuarto y se eligen mirando (sirve sin joystick)",
                () => GameOptions.MenusEnElCuarto, v => GameOptions.MenusEnElCuarto = v));
            lista.Add(PiezasUI.Deslizador(nav, "TAMAÑO DE LA IMAGEN",
                () => Mathf.InverseLerp(CardboardCalibrationUI.ScaleMin, CardboardCalibrationUI.ScaleMax, CalibracionVisor.Escala),
                v => CalibracionVisor.Escala = Mathf.Lerp(CardboardCalibrationUI.ScaleMin, CardboardCalibrationUI.ScaleMax, v),
                0.05f));
            lista.Add(PiezasUI.Deslizador(nav, "POSICIÓN OJO IZQUIERDO",
                () => CalibracionVisor.MaxOjo > 0f ? CalibracionVisor.OjoIzq / CalibracionVisor.MaxOjo : 0f,
                v => CalibracionVisor.OjoIzq = v * CalibracionVisor.MaxOjo, 0.04f));
            lista.Add(PiezasUI.Deslizador(nav, "POSICIÓN OJO DERECHO",
                () => CalibracionVisor.MaxOjo > 0f ? CalibracionVisor.OjoDer / CalibracionVisor.MaxOjo : 0f,
                v => CalibracionVisor.OjoDer = v * CalibracionVisor.MaxOjo, 0.04f));

            lista.Add(PiezasUI.Seccion("MANDO"));
            _estadoMando = PiezasUI.Info(lista, "");
            lista.Add(PiezasUI.Boton(nav, "CONTROL (MANDO)", () => NightMenuUI.Instance?.AbrirControl()));

            // Herramientas de desarrollo: sólo en development build (ver CLAUDE.md, tiers).
            if (Debug.isDebugBuild)
            {
                lista.Add(PiezasUI.Seccion("DESARROLLO"));
                // Cada catálogo tiene su propio progreso, así que el contador de abajo
                // cambia con él.
                lista.Add(PiezasUI.Interruptor(nav, "NOCHES DE PROD",
                    "Prendido: Nights/prod (datos reales). Apagado: Nights/dev. Rige desde la próxima partida",
                    () => PlayDataProfile.UseProduction,
                    v => { if (NightMenuUI.Instance != null) NightMenuUI.Instance.UsarNochesProd(v);
                           else PlayDataProfile.UseProduction = v;
                           Refrescar(); }));
                lista.Add(PiezasUI.Interruptor(nav, "PISO AUTOMÁTICO",
                    "Además del PISO del escáner, reparte pilas y reliquias alrededor tuyo al empezar " +
                    "la noche (altura del PISO, o la detectada si no hay)",
                    () => Gameplay.Spawning.SpawnDev.PisoAutomatico,
                    v => Gameplay.Spawning.SpawnDev.PisoAutomatico = v));
                _progreso = PiezasUI.Info(lista, "");
                // El total se lee al tocar: cambia con el catálogo (NOCHES DE PROD).
                lista.Add(PiezasUI.Boton(nav, "DESBLOQUEAR TODAS", () =>
                    NightProgress.DesbloquearTodas(NightMenuUI.Instance != null ? NightMenuUI.Instance.TotalNoches : 6)));
                lista.Add(PiezasUI.Boton(nav, "BORRAR PROGRESO", NightProgress.BorrarProgreso, peligro: true));
            }

            // "Volver" al final del orden de navegación: el foco inicial cae en el contenido.
            nav.Agregar(volver, () => NightMenuUI.Instance?.VolverPantalla());
            nav.Volver = () => NightMenuUI.Instance?.VolverPantalla();

            Refrescar();
        }

        protected override void Animar(float dt)
        {
            _refresco -= dt;
            if (_refresco > 0f) return;
            _refresco = 0.5f;
            Refrescar();
        }

        private void Refrescar()
        {
            var gm = Gamepad.GamepadManager.Instance;
            bool hay = gm != null && gm.IsConnected;
            if (_estadoMando != null)
            {
                _estadoMando.text = hay
                    ? $"Joystick: {gm.DisplayName} · conectado"
                    : "Joystick: ninguno. Conectá un mando por Bluetooth desde el sistema";
                _estadoMando.style.color = hay ? TemaUI.Green : TemaUI.Muted;
            }
            if (_progreso != null)
            {
                int total = NightMenuUI.Instance != null ? NightMenuUI.Instance.TotalNoches : 6;
                _progreso.text = $"Progreso: {NightProgress.Desbloqueadas} de {total} noches desbloqueadas";
            }
        }
    }
}
