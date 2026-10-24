using Gameplay;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // Pantallas del flujo de juego del menú principal (Rediseño 2.0), usables con el visor:
    // ELEGÍ LA NOCHE → ¿DÓNDE JUGÁS? (escenario + JUGAR) y FALTA UN ESCENARIO. Reemplazan a
    // las IMGUI de NightMenuUI con el flag; la lógica (noche elegida, escenario, confirmar)
    // sigue en NightMenuUI. Se crean solas en NightMenuScene.
    public static class MenusFlujoJuego
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            AlCargar(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void AlCargar(Scene s, LoadSceneMode m)
        {
            if (s.name != SceneFlow.EscenaMenu) return;
            if (Object.FindAnyObjectByType<MenuNoches2>() == null) new GameObject("MenuNoches2").AddComponent<MenuNoches2>();
            if (Object.FindAnyObjectByType<MenuEscenario2>() == null) new GameObject("MenuEscenario2").AddComponent<MenuEscenario2>();
            if (Object.FindAnyObjectByType<MenuSinEscenario2>() == null) new GameObject("MenuSinEscenario2").AddComponent<MenuSinEscenario2>();
            if (Object.FindAnyObjectByType<MenuCrearUnirse2>() == null) new GameObject("MenuCrearUnirse2").AddComponent<MenuCrearUnirse2>();
            if (Object.FindAnyObjectByType<MenuEscenarios2>() == null) new GameObject("MenuEscenarios2").AddComponent<MenuEscenarios2>();
            if (Object.FindAnyObjectByType<MenuTutoriales2>() == null) new GameObject("MenuTutoriales2").AddComponent<MenuTutoriales2>();
            if (Object.FindAnyObjectByType<AvisoMenu2>() == null) new GameObject("AvisoMenu2").AddComponent<AvisoMenu2>();
        }
    }

    // Base: una pantalla de NightMenuUI con fondo, encabezado (volver) y lista desplazable.
    public abstract class PantallaMenu2 : PantallaUI
    {
        protected abstract NightMenuUI.Pantalla Cual { get; }
        protected abstract string Titulo { get; }
        protected virtual Texture2D ArteFondo => TarjetasNoche.Arte(0);

        protected VisualElement Lista;
        private VisualElement _fondo, _volver, _pie;

        // Botones fijos debajo de la lista (ArmarPie). La lista toma el alto de su
        // contenido y sólo se encoge —desplazándose— cuando no entra: el pie queda pegado
        // a la lista y, como mucho, abajo de la pantalla. Sin pie, la lista ocupa todo.
        protected virtual bool ConPie => false;
        protected virtual void ArmarPie(VisualElement pie, MenuNavegable nav) { }

        protected override bool EsVisible()
        {
            var menu = NightMenuUI.Instance;
            return Flag.Activo && menu != null && menu.Mostrando(Cual);
        }

        protected override Texture FondoVisor => ArteFondo;

        protected override void AlCambiarVisor(bool visor)
        {
            if (_fondo != null) _fondo.style.display = visor ? DisplayStyle.None : DisplayStyle.Flex;
        }

        protected override void Construir(VisualElement raiz, MenuNavegable nav)
        {
            _fondo = PiezasUI.Fondo(ArteFondo);
            raiz.Add(_fondo);
            var col = PiezasUI.Columna();
            raiz.Add(col);
            col.Add(PiezasUI.Encabezado(Titulo, out _volver));
            var sv = PiezasUI.Lista();
            Desplazable = sv;
            col.Add(sv);
            Lista = sv;
            if (ConPie)
            {
                sv.style.flexGrow = 0;
                sv.style.flexShrink = 1;
                _pie = PiezasUI.Ignorar(new VisualElement());
                _pie.style.flexShrink = 0;
                _pie.style.paddingTop = 10;
                col.Add(_pie);
            }
            nav.Volver = AlVolver;
            Rearmar();
        }

        // Rearma el contenido (al entrar a la pantalla, o cuando cambia algo elegido).
        protected void Rearmar()
        {
            if (Lista == null) return;
            Lista.Clear();
            _pie?.Clear();
            Nav.Limpiar();
            Armar(Lista, Nav);
            if (_pie != null) ArmarPie(_pie, Nav);
            // "Volver" al final del orden: el foco inicial cae en el contenido.
            Nav.Agregar(_volver, AlVolver);
        }

        // "Volver" (botón del encabezado, ← / B, atrás de Android). Una pantalla con un paso
        // interno (p. ej. confirmar un borrado) lo redefine para cerrar ese paso primero.
        protected virtual void AlVolver() => NightMenuUI.Instance?.VolverPantalla();

        protected abstract void Armar(VisualElement lista, MenuNavegable nav);

        // Al volver a mostrarse, los datos pudieron cambiar (otra noche, otro escenario).
        // Animar sólo corre mientras se ve: un hueco de frames = recién reaparece.
        private int _ultimoFrame = -10;
        protected override void Animar(float dt)
        {
            if (Time.frameCount - _ultimoFrame > 1) Rearmar();
            _ultimoFrame = Time.frameCount;
        }
    }

    public class MenuNoches2 : PantallaMenu2
    {
        protected override NightMenuUI.Pantalla Cual => NightMenuUI.Pantalla.Noches;
        protected override string Titulo => "ELEGÍ LA NOCHE";

        protected override void Armar(VisualElement lista, MenuNavegable nav)
        {
            var menu = NightMenuUI.Instance;
            var noches = menu != null ? menu.Catalogo : null;
            int total = Mathf.Max(6, noches != null ? noches.Length : 0);
            int actual = Mathf.Clamp(NightProgress.Desbloqueadas - 1, 0, total - 1);
            var grilla = TarjetasNoche.Grilla();
            for (int i = 0; i < total; i++)
            {
                int idx = i;
                bool existe = noches != null && i < noches.Length && noches[i] != null;
                TarjetasNoche.Estado est;
                if (existe && !BuildVariant.NocheHabilitada(i)) est = TarjetasNoche.Estado.SoloCompleta;
                else if (!existe || !NightProgress.Desbloqueada(i)) est = TarjetasNoche.Estado.Bloqueada;
                else if (i == actual) est = TarjetasNoche.Estado.Actual;
                else if (i < actual) est = TarjetasNoche.Estado.Superada;
                else est = TarjetasNoche.Estado.Disponible;

                string nombre = existe ? noches[i].displayName : null;
                string detalle = null;
                if (existe && noches[i].collectiblesActive && est != TarjetasNoche.Estado.Bloqueada)
                {
                    int max = noches[i].collectibleMaxPerNight;
                    detalle = $"Reliquias {CollectibleProgress.Record(i)}/{(max > 0 ? max.ToString() : "∞")}";
                }
                grilla.Add(TarjetasNoche.Tarjeta(nav, i, nombre, est, detalle,
                    () => NightMenuUI.Instance?.SeleccionarNoche(idx)));
            }
            lista.Add(grilla);
        }
    }

    public class MenuEscenario2 : PantallaMenu2
    {
        protected override NightMenuUI.Pantalla Cual => NightMenuUI.Pantalla.Entorno;
        protected override string Titulo => "¿DÓNDE JUGÁS?";
        protected override Texture2D ArteFondo
        {
            get
            {
                var menu = NightMenuUI.Instance;
                if (menu == null) return TarjetasNoche.Arte(0);
                return menu.TutorialPendiente != TutorialTipo.Ninguno
                    ? Tutoriales.Arte(menu.TutorialPendiente)
                    : TarjetasNoche.Arte(Mathf.Max(0, menu.NocheElegida));
            }
        }

        protected override void Armar(VisualElement lista, MenuNavegable nav)
        {
            var menu = NightMenuUI.Instance;
            if (menu == null) return;
            string esc = menu.EscenarioElegido;
            lista.Add(PiezasUI.Seccion("ESCENARIO"));
            foreach (var nombre in menu.Escenarios)
            {
                string e = nombre;
                bool ok = menu.EsJugable(nombre);
                bool sel = nombre == esc;
                var fila = PiezasUI.Tarjeta();
                fila.style.flexDirection = FlexDirection.Row;
                fila.style.alignItems = Align.Center;
                fila.style.justifyContent = Justify.SpaceBetween;
                if (sel)
                {
                    TemaUI.Borde(fila, 3, TemaUI.Red);
                    fila.style.backgroundColor = new Color(TemaUI.Red.r, TemaUI.Red.g, TemaUI.Red.b, 0.16f);
                }
                if (!ok) fila.style.opacity = 0.45f;
                var izq = PiezasUI.Ignorar(new VisualElement());
                izq.style.flexShrink = 1;
                izq.Add(TemaUI.Texto(nombre, TemaUI.Bebas, 46, TemaUI.Cream, 3f));
                string motivo = menu.MotivoNoJugable(nombre);
                var d = TemaUI.Texto(ok ? $"{menu.ElementosDe(nombre)} elementos" : motivo,
                                     TemaUI.Mono, 24, ok ? TemaUI.Muted : TemaUI.Tan);
                izq.Add(d);
                fila.Add(izq);
                if (sel) fila.Add(TemaUI.Texto("ELEGIDO", TemaUI.Mono, 24, TemaUI.Red, 2f, TextAnchor.MiddleRight));
                lista.Add(fila);
                nav.Agregar(fila, () =>
                {
                    NightMenuUI.Instance?.SeleccionarEscenario(e);
                    Rearmar();
                }, habilitada: ok);
            }

            // Práctica: escanear otro escenario sin volver al menú principal. Al guardar (o
            // salir) del escáner se vuelve acá con la misma práctica (NightMenuUI.Awake).
            if (menu.TutorialPendiente != TutorialTipo.Ninguno)
                lista.Add(PiezasUI.Boton(nav, "ESCANEAR UN ESCENARIO NUEVO", () => NightMenuUI.Instance?.EscanearNuevo()));
            // SORKEN / VELETH sin escaneo: sólo development build (NightMenuUI.PuedeArmarEnElMomento),
            // para testear el armado rápido (p.ej. en un iPhone con LiDAR).
            if (menu.PuedeArmarEnElMomento)
                lista.Add(PiezasUI.Opcion(nav, IconosMenu.Libro, "ARMAR EN EL MOMENTO (DEV)",
                    "Sin escaneo: ubicás el libro y la entrada ahí mismo. Oclusión sólo por profundidad",
                    () => NightMenuUI.Instance?.ArmarEnElMomento()));
        }

        // JUGAR y ELEGIR OTRA NOCHE van en el pie: con muchos escenarios se desplaza la lista
        // y los botones quedan siempre a la vista, abajo.
        protected override bool ConPie => true;

        protected override void ArmarPie(VisualElement pie, MenuNavegable nav)
        {
            var menu = NightMenuUI.Instance;
            if (menu == null) return;
            int n = menu.NocheElegida;
            var noches = menu.Catalogo;
            var tutorial = menu.TutorialPendiente;
            bool practica = tutorial != TutorialTipo.Ninguno;
            string nombreNoche = !practica && noches != null && n >= 0 && n < noches.Length && noches[n] != null &&
                                 !string.IsNullOrEmpty(noches[n].displayName)
                ? noches[n].displayName.ToUpperInvariant() : "";
            string esc = menu.EscenarioElegido;

            // JUGAR abajo de los escenarios (se elige dónde y después se juega), pero con el
            // foco puesto: con el escenario ya preseleccionado, basta una A para arrancar.
            var jugar = new VisualElement();
            TemaUI.Radio(jugar, 42);
            TemaUI.Relleno(jugar, 34, 46);
            TemaUI.Borde(jugar, 2, new Color(1f, 1f, 1f, 0.1f));
            jugar.style.backgroundImage = TemaUI.DegradeRojo;
            jugar.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            jugar.style.marginTop = 10;
            jugar.style.marginBottom = 18;
            jugar.Add(TemaUI.Texto(practica ? $"PRACTICAR · {Tutoriales.Titulo(tutorial)}" : $"JUGAR · NOCHE {n + 1}",
                                   TemaUI.Bebas, 70, TemaUI.Cream, 7f));
            var sub = TemaUI.Texto(string.IsNullOrEmpty(esc) ? "Elegí un escenario arriba"
                                   : $"{(nombreNoche.Length > 0 ? nombreNoche + " · " : "")}en {esc}",
                                   TemaUI.Mono, 28, new Color(TemaUI.Cream.r, TemaUI.Cream.g, TemaUI.Cream.b, 0.85f));
            sub.style.marginTop = 6;
            jugar.Add(sub);
            if (string.IsNullOrEmpty(esc)) jugar.style.opacity = 0.5f;
            pie.Add(jugar);
            var opJugar = nav.Agregar(jugar, () => NightMenuUI.Instance?.ConfirmarPartida(), habilitada: !string.IsNullOrEmpty(esc));
            if (opJugar.Habilitada) nav.Enfocar(opJugar);

            // Una práctica no tiene noche que cambiar: se vuelve a TUTORIALES con "volver".
            if (practica) return;
            var otra = PiezasUI.Boton(nav, "ELEGIR OTRA NOCHE", () => NightMenuUI.Instance?.ElegirOtraNoche());
            otra.style.marginBottom = 0;
            pie.Add(otra);

            if (menu.CreandoSala)
                pie.Add(PiezasUI.Seccion($"SALA EN EL PUERTO {NetworkConfig.DefaultPort} (para cambiarlo, apagá el rediseño)"));
        }
    }

    public class MenuSinEscenario2 : PantallaMenu2
    {
        protected override NightMenuUI.Pantalla Cual => NightMenuUI.Pantalla.SinEntorno;
        protected override string Titulo => "FALTA UN ESCENARIO";

        protected override void Armar(VisualElement lista, MenuNavegable nav)
        {
            var menu = NightMenuUI.Instance;
            var escenarios = menu != null ? menu.Escenarios : null;
            if (escenarios == null || escenarios.Count == 0)
            {
                PiezasUI.Info(lista, "Todavía no escaneaste ningún escenario. " +
                                     "El ritual necesita conocer tu espacio real antes de empezar.");
                lista.Add(PiezasUI.Boton(nav, "ESCANEAR MI ESCENARIO", () => NightMenuUI.Instance?.EscanearNuevo()));
                return;
            }

            // Práctica: hay escenarios con imagen pero ninguno le sirve (p.ej. SORKEN sin
            // puertas ni ventanas marcadas). Se dice por qué y se ofrece editarlos.
            string motivoPractica = null;
            if (menu.TutorialPendiente != TutorialTipo.Ninguno)
                foreach (var e in escenarios)
                {
                    string m = menu.MotivoNoJugable(e);
                    if (m != null && !m.StartsWith("Sin imagen")) { motivoPractica = m; break; }
                }
            if (motivoPractica != null)
            {
                PiezasUI.Info(lista, $"Ninguno de tus escenarios sirve para esta práctica. {motivoPractica}. " +
                                     "Abrí un escenario y agregalo.");
                foreach (var e in escenarios)
                {
                    string nombre = e;
                    lista.Add(PiezasUI.Boton(nav, $"EDITAR \"{nombre}\"",
                                             () => NightMenuUI.Instance?.EditarEscenario(nombre)));
                }
                lista.Add(PiezasUI.Boton(nav, "ESCANEAR UNO NUEVO", () => NightMenuUI.Instance?.EscanearNuevo()));
                return;
            }

            // Hay escaneos pero ninguno tiene la imagen de referencia (se guardaron con
            // "NO TENGO LA IMAGEN"): sin ella el juego no puede ubicarte en el cuarto.
            PiezasUI.Info(lista, "Tus escenarios no tienen imagen de referencia. Es la imagen fija " +
                                 "que la cámara reconoce para ubicarte en tu cuarto: sin ella no se puede jugar. " +
                                 "Abrí el escenario y capturala.");
            foreach (var e in escenarios)
            {
                string nombre = e;
                lista.Add(PiezasUI.Boton(nav, $"AGREGAR LA IMAGEN A \"{nombre}\"",
                                         () => NightMenuUI.Instance?.EditarEscenario(nombre)));
            }
            lista.Add(PiezasUI.Boton(nav, "ESCANEAR UNO NUEVO", () => NightMenuUI.Instance?.EscanearNuevo()));
        }
    }

    // JUGAR CON OTROS: crear una sala (host) o unirse a una (cliente).
    public class MenuCrearUnirse2 : PantallaMenu2
    {
        protected override NightMenuUI.Pantalla Cual => NightMenuUI.Pantalla.CrearUnirse;
        protected override string Titulo => "JUGAR CON OTROS";
        protected override Texture2D ArteFondo => TarjetasNoche.Arte(3);

        protected override void Armar(VisualElement lista, MenuNavegable nav)
        {
            PiezasUI.Info(lista, "Cada jugador con su celular, en la misma red wifi y en la misma casa.");
            lista.Add(PiezasUI.Opcion(nav, IconosMenu.Otros, "CREAR SALA",
                "Tu celular arma la sala y elegís la noche y el escenario",
                () => NightMenuUI.Instance?.CrearSala()));
            lista.Add(PiezasUI.Opcion(nav, IconosMenu.Escenarios, "UNIRSE A UNA SALA",
                "Buscá la sala de un amigo en tu red",
                () => NightMenuUI.Instance?.Unirse()));
        }
    }
}
