using Gameplay;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // Fin de la noche (SOBREVIVISTE / MORISTE) del Rediseño 2.0, usable con el visor.
    // Acciones: reintentar, siguiente noche, elegir noche, espectar, volver al menú; sólo el
    // host reinicia. Las reglas de qué ofrecer viven en DeathScreenUI. En Cardboard se
    // dibuja una vez por ojo, delante tuyo. Espectando, la barra para volver es Espectando2.
    public class FinDeNoche2 : PantallaUI
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            AlCargar(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static FinDeNoche2 _instancia;

        private static void AlCargar(Scene s, LoadSceneMode m)
        {
            if (s.name != SceneFlow.EscenaJuego || _instancia != null) return;
            new GameObject("FinDeNoche2").AddComponent<FinDeNoche2>();
        }

        private VisualElement _velo, _cuerpo;
        private bool _selector;
        private string _firma;

        protected override void Awake()
        {
            if (_instancia != null && _instancia != this) { Destroy(gameObject); return; }
            _instancia = this;
            base.Awake();
        }

        protected override void OnDestroy()
        {
            if (_instancia == this) _instancia = null;
            base.OnDestroy();
        }

        protected override bool EsVisible()
        {
            var ld = LocalDeath.Instance;
            bool muerto = ld != null && ld.PresentationReady;
            bool sobrevivio = NightResult.Sobrevivio;
            if (!muerto && !sobrevivio) { _selector = false; _logCopiado = -1; return false; }
            if (!sobrevivio && ld != null && ld.Espectando) return false;   // Espectando2
            return true;
        }

        protected override bool EnVisor => MRCardboardController.Activo;
        protected override bool CabezaPorCamara => true;
        protected override Color VeloVisor => new(0.02f, 0.016f, 0.012f, 0.85f);

        protected override void AlCambiarVisor(bool visor)
        {
            if (_velo != null) _velo.style.display = visor ? DisplayStyle.None : DisplayStyle.Flex;
            _firma = null;
        }

        protected override void Construir(VisualElement raiz, MenuNavegable nav)
        {
            _velo = PiezasUI.Ignorar(new VisualElement());
            PiezasUI.Llenar(_velo);
            _velo.style.backgroundColor = new Color(TemaUI.Bg.r, TemaUI.Bg.g, TemaUI.Bg.b, 0.92f);
            raiz.Add(_velo);
            var col = PiezasUI.Columna();
            raiz.Add(col);
            var sv = PiezasUI.Lista();
            Desplazable = sv;
            col.Add(sv);
            _cuerpo = sv;
            nav.Volver = () => { if (_selector) { _selector = false; _firma = null; } };
        }

        // Se rearma cuando cambia lo que hay que ofrecer (host/cliente, ganar/perder, etc.).
        protected override void Animar(float dt)
        {
            var ld = LocalDeath.Instance;
            bool sobrevivio = NightResult.Sobrevivio;
            var ses = GameSession.Instance;
            string firma = $"{_selector}|{sobrevivio}|{NightTransition.PuedeReiniciar}|" +
                           $"{(ses != null ? ses.Tutorial : TutorialTipo.Ninguno)}|{(ses != null && ses.Camino)}|" +
                           $"{(TutorialLibro.Instance != null && TutorialLibro.Instance.CompletadaPorVeleth)}|" +
                           $"{(ld != null && ld.PuedeEspectar)}|{EnVisor}|{NightResult.NocheDesbloqueada}|{_logCopiado}";
            if (firma == _firma) return;
            _firma = firma;
            _cuerpo.Clear();
            Nav.Limpiar();
            if (_selector) ArmarSelector(); else ArmarResultado(ld, sobrevivio);
        }

        private void ArmarResultado(LocalDeath ld, bool sobrevivio)
        {
            var espacio = PiezasUI.Ignorar(new VisualElement());
            espacio.style.height = 200;
            _cuerpo.Add(espacio);

            // Práctica terminada: sin noches ni récords; repetirla o volver a la lista
            // (NightMenuUI abre TUTORIALES al volver de una práctica).
            var sesion = GameSession.Instance;
            // Paso del TUTORIAL superado: el siguiente (en la misma sesión) o la tarjeta final.
            if (sobrevivio && sesion != null && sesion.EnTutorial && sesion.Camino)
            {
                ArmarPasoSuperado(sesion.Tutorial);
                return;
            }
            if (sobrevivio && sesion != null && sesion.EnTutorial)
            {
                _cuerpo.Add(TemaUI.Texto("PRÁCTICA COMPLETA", TemaUI.Bebas, 130, TemaUI.Green, 10f, TextAnchor.MiddleCenter));
                var bien = TemaUI.Texto(QueAprendiste(sesion.Tutorial),
                                        TemaUI.Elite, 38, TemaUI.Muted, 0f, TextAnchor.MiddleCenter);
                bien.style.marginTop = 8; bien.style.marginBottom = 30;
                _cuerpo.Add(bien);
                var repetir = PiezasUI.Boton(Nav, "REPETIR PRÁCTICA", TutorialCamino.ReintentarPaso);
                repetir.style.backgroundImage = TemaUI.DegradeRojo;
                repetir.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                _cuerpo.Add(repetir);
                _cuerpo.Add(PiezasUI.Boton(Nav, "VOLVER AL TUTORIAL", () => SceneFlow.GoTo(SceneFlow.EscenaMenu)));
                return;
            }

            // Práctica perdida: te atraparon. Se muere igual que en una noche (retrato del
            // asesino y todo), pero acá sólo se ofrece reintentarla o volver a la lista.
            if (!sobrevivio && sesion != null && sesion.EnTutorial)
            {
                // Veleth te atrapó en la demostración del libro: el paso se superó igual.
                if (TutorialLibro.Instance != null && TutorialLibro.Instance.CompletadaPorVeleth)
                {
                    ArmarAtrapadoPorVeleth(sesion);
                    return;
                }
                _cuerpo.Add(TemaUI.Texto("MORISTE", TemaUI.Bebas, 150, TemaUI.Red, 10f, TextAnchor.MiddleCenter));
                var mal = TemaUI.Texto(sesion.Tutorial switch
                                       {
                                           TutorialTipo.Sorken => "El Sorken te atrapó. Alumbralo con la luz intensa antes de que llegue.",
                                           TutorialTipo.Arbmos => "Con la cordura en cero, el Arbmos mata. Apagá la luz para que se vaya.",
                                           TutorialTipo.Libro  => "Veleth te alcanzó. La luz no la frena: poné paredes o muebles entre ustedes.",
                                           _                   => "El ritual te reclamó… por ahora.",
                                       },
                                       TemaUI.Elite, 38, TemaUI.Muted, 0f, TextAnchor.MiddleCenter);
                mal.style.marginTop = 8; mal.style.marginBottom = 30;
                _cuerpo.Add(mal);
                var reintentar = PiezasUI.Boton(Nav, sesion.Camino ? "REINTENTAR PASO" : "REINTENTAR PRÁCTICA",
                                                TutorialCamino.ReintentarPaso);
                reintentar.style.backgroundImage = TemaUI.DegradeRojo;
                reintentar.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                _cuerpo.Add(reintentar);
                _cuerpo.Add(PiezasUI.Boton(Nav, sesion.Camino ? "SALIR DEL TUTORIAL" : "VOLVER AL TUTORIAL",
                                           () => SceneFlow.GoTo(SceneFlow.EscenaMenu)));
                return;
            }

            var tit = TemaUI.Texto(sobrevivio ? "SOBREVIVISTE" : "MORISTE", TemaUI.Bebas, 150,
                                   sobrevivio ? TemaUI.Green : TemaUI.Red, 10f, TextAnchor.MiddleCenter);
            _cuerpo.Add(tit);
            var sub = TemaUI.Texto(sobrevivio ? "Amaneció. El ritual sigue, pero no esta noche."
                                              : "El ritual te reclamó… por ahora.",
                                   TemaUI.Elite, 38, TemaUI.Muted, 0f, TextAnchor.MiddleCenter);
            sub.style.marginTop = 8; sub.style.marginBottom = 30;
            _cuerpo.Add(sub);

            if (sobrevivio && DeathScreenUI.FinDeLaDemo())
                PiezasUI.Info(_cuerpo, $"FIN DE LA DEMO. Podés seguir jugando estas {BuildVariant.NochesDemo} " +
                                       "noches todas las veces que quieras. La versión completa suma más noches y multijugador.");
            else if (sobrevivio && NightResult.NocheDesbloqueada > 0)
                PiezasUI.Info(_cuerpo, $"Noche {NightResult.NocheDesbloqueada} desbloqueada");

            bool espectable = !sobrevivio && ld != null && ld.PuedeEspectar;
            if (espectable) PiezasUI.Info(_cuerpo, "Tus compañeros siguen en pie");

            bool host = NightTransition.PuedeReiniciar;
            if (host)
            {
                VisualElement principal;
                if (sobrevivio && DeathScreenUI.SiguienteDesbloqueada(out int sig))
                    principal = PiezasUI.Boton(Nav, "SIGUIENTE NOCHE", () => NightTransition.IrANoche(sig));
                else
                    principal = PiezasUI.Boton(Nav, "REINTENTAR NOCHE", NightTransition.Reintentar);
                principal.style.backgroundImage = TemaUI.DegradeRojo;
                principal.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                _cuerpo.Add(principal);
                _cuerpo.Add(PiezasUI.Boton(Nav, "ELEGIR NOCHE", () => { _selector = true; _firma = null; },
                                           habilitado: NightTransition.HayCatalogoDeNoches));
            }
            else
            {
                PiezasUI.Info(_cuerpo, "Esperando a que el host reinicie la noche…");
            }

            if (espectable)
                _cuerpo.Add(PiezasUI.Boton(Nav, "ESPECTAR PARTIDA", ld.Espectar));

            _cuerpo.Add(PiezasUI.Boton(Nav, "VOLVER AL MENÚ", () => SceneFlow.GoTo(SceneFlow.EscenaMenu)));

            // PROVISORIO (dev build): copiar el log del retrato del asesino para pegarlo
            // afuera, sin consola de Xcode. Ver LogMuerteDev.
            if (LogMuerteDev.Disponible && !sobrevivio)
            {
                if (_logCopiado >= 0) PiezasUI.Info(_cuerpo, $"Log copiado al portapapeles ({_logCopiado} líneas)");
                _cuerpo.Add(PiezasUI.Boton(Nav, "COPIAR LOG (DEV)",
                    () => { _logCopiado = LogMuerteDev.Copiar(); _firma = null; }));
            }
        }

        private int _logCopiado = -1;

        private static string QueAprendiste(TutorialTipo tipo) => tipo switch
        {
            TutorialTipo.Linterna       => "Ya sabés prender, apagar y concentrar la luz.",
            TutorialTipo.PilasReliquias => "Ya sabés recargar la linterna con pilas y conocés tu cordura.",
            TutorialTipo.Sorken         => "Ya sabés echar al Sorken: en la puerta y cuando ya entró.",
            TutorialTipo.Arbmos         => "Ya sabés ocultarte del Arbmos: a oscuras se va, con luz se enoja.",
            TutorialTipo.Libro          => "Ya sabés salvar el libro y conocés a Veleth: la luz no la frena, que el libro no se apague.",
            _                           => "",
        };

        // TUTORIAL: paso superado. Lo que sigue va en la misma sesión AR (TutorialCamino), sin
        // volver al menú ni a sincronizar; tras el último, la tarjeta de "listo para la Noche 1".
        private void ArmarPasoSuperado(TutorialTipo tipo)
        {
            var sig = TutorialCamino.Siguiente(tipo);
            if (sig == TutorialTipo.Ninguno)
            {
                ArmarFinDelTutorial();
                return;
            }

            int total = TutorialCamino.Pasos.Length;
            int i = TutorialCamino.Indice(tipo);
            var paso = TemaUI.Texto($"PASO {i + 1} DE {total}", TemaUI.Mono, 28, TemaUI.Tan, 4f, TextAnchor.MiddleCenter);
            _cuerpo.Add(paso);
            _cuerpo.Add(TemaUI.Texto("SUPERADO", TemaUI.Bebas, 130, TemaUI.Green, 10f, TextAnchor.MiddleCenter));
            var bien = TemaUI.Texto(QueAprendiste(tipo), TemaUI.Elite, 38, TemaUI.Muted, 0f, TextAnchor.MiddleCenter);
            bien.style.marginTop = 8; bien.style.marginBottom = 30;
            _cuerpo.Add(bien);

            var siguiente = PiezasUI.Tarjeta();
            PiezasUI.Ignorar(siguiente);
            siguiente.Add(TemaUI.Texto($"SIGUIENTE · PASO {i + 2}", TemaUI.Mono, 24, TemaUI.Tan, 4f));
            siguiente.Add(TemaUI.Texto(Tutoriales.Titulo(sig), TemaUI.Bebas, 64, TemaUI.Cream, 5f));
            var desc = TemaUI.Texto(Tutoriales.Descripcion(sig), TemaUI.Mono, 26, TemaUI.Muted);
            desc.style.marginTop = 4;
            siguiente.Add(desc);
            _cuerpo.Add(siguiente);

            var continuar = PiezasUI.Boton(Nav, "CONTINUAR", TutorialCamino.IrAlSiguiente,
                                           habilitado: NightTransition.PuedeReiniciar);
            continuar.style.backgroundImage = TemaUI.DegradeRojo;
            continuar.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            _cuerpo.Add(continuar);
            _cuerpo.Add(PiezasUI.Boton(Nav, "REPETIR PASO", TutorialCamino.ReintentarPaso));
            _cuerpo.Add(PiezasUI.Boton(Nav, "SALIR DEL TUTORIAL", () => SceneFlow.GoTo(SceneFlow.EscenaMenu)));
        }

        // LIBRO Y VELETH: de Veleth no se escapa, así que la segunda parte es una demostración
        // y que te atrape también la completa (TutorialLibro.CompletadaPorVeleth).
        private void ArmarAtrapadoPorVeleth(GameSession sesion)
        {
            _cuerpo.Add(TemaUI.Texto("TE ATRAPÓ VELETH", TemaUI.Bebas, 120, TemaUI.Red, 10f, TextAnchor.MiddleCenter));
            var leccion = TemaUI.Texto("Si el libro se apaga, Veleth no perdona: la luz no la frena y no se cansa. " +
                                       "La única defensa es que el libro nunca se apague.",
                                       TemaUI.Elite, 36, TemaUI.Muted, 0f, TextAnchor.MiddleCenter);
            leccion.style.marginTop = 8; leccion.style.marginBottom = 30;
            _cuerpo.Add(leccion);

            if (sesion.Camino)
            {
                ArmarFinDelTutorial();
                return;
            }
            _cuerpo.Add(TemaUI.Texto("PRÁCTICA COMPLETA", TemaUI.Bebas, 80, TemaUI.Green, 8f, TextAnchor.MiddleCenter));
            var repetir = PiezasUI.Boton(Nav, "REPETIR PRÁCTICA", TutorialCamino.ReintentarPaso);
            repetir.style.backgroundImage = TemaUI.DegradeRojo;
            repetir.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            _cuerpo.Add(repetir);
            _cuerpo.Add(PiezasUI.Boton(Nav, "VOLVER AL TUTORIAL", () => SceneFlow.GoTo(SceneFlow.EscenaMenu)));
        }

        private void ArmarFinDelTutorial()
        {
            _cuerpo.Add(TemaUI.Texto("¡FINALIZASTE EL TUTORIAL!", TemaUI.Bebas, 110, TemaUI.Green, 8f, TextAnchor.MiddleCenter));
            var sub = TemaUI.Texto("Ya estás listo para jugar la primera noche. Estate atento.",
                                   TemaUI.Elite, 38, TemaUI.Cream, 0f, TextAnchor.MiddleCenter);
            sub.style.marginTop = 8; sub.style.marginBottom = 30;
            _cuerpo.Add(sub);

            var jugar = PiezasUI.Boton(Nav, "JUGAR NOCHE 1", () => TutorialCamino.JugarNoche1(),
                                       habilitado: NightTransition.HayCatalogoDeNoches);
            jugar.style.backgroundImage = TemaUI.DegradeRojo;
            jugar.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            _cuerpo.Add(jugar);
            _cuerpo.Add(PiezasUI.Boton(Nav, "VOLVER AL MENÚ", () => SceneFlow.GoTo(SceneFlow.EscenaMenu)));
        }

        private void ArmarSelector()
        {
            var s = GameSession.Instance;
            var noches = s != null ? s.Nights : null;
            _cuerpo.Add(TemaUI.Texto("ELEGÍ LA NOCHE", TemaUI.Bebas, 84, TemaUI.Cream, 8f));
            var nota = TemaUI.Texto("La sesión no se reinicia: tus anclas siguen puestas", TemaUI.Mono, 24, TemaUI.Dim);
            nota.style.marginBottom = 24;
            _cuerpo.Add(nota);

            int total = Mathf.Max(6, noches != null ? noches.Length : 0);
            var grilla = TarjetasNoche.Grilla();
            for (int i = 0; i < total; i++)
            {
                int idx = i;
                bool ok = s != null && s.NocheDisponible(i) && NightProgress.Desbloqueada(i);
                var est = !ok ? TarjetasNoche.Estado.Bloqueada
                        : s.NightIndex == i ? TarjetasNoche.Estado.Actual
                        : TarjetasNoche.Estado.Disponible;
                string nombre = ok && noches[i] != null ? noches[i].displayName : null;
                grilla.Add(TarjetasNoche.Tarjeta(Nav, i, nombre, est,
                    est == TarjetasNoche.Estado.Actual ? "Recién jugada" : null,
                    () => { _selector = false; NightTransition.IrANoche(idx); }));
            }
            _cuerpo.Add(grilla);
            _cuerpo.Add(PiezasUI.Boton(Nav, "VOLVER", () => { _selector = false; _firma = null; }));
        }
    }
}
