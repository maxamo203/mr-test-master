using Gameplay;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // Fin de la noche (SOBREVIVISTE / MORISTE) del Rediseño 2.0, usable con el visor.
    // Reemplaza a DeathScreenUI con el flag: mismas acciones (reintentar, siguiente noche,
    // elegir noche, espectar, volver al menú) y misma lógica de quién puede qué (sólo el
    // host reinicia). En Cardboard se dibuja una vez por ojo, delante tuyo.
    //
    // ESPECTAR deja de verse en el visor por ahora: su barra para volver todavía es vieja.
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
        public static bool Mostrando => _instancia != null && _instancia.EsVisible();

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
            if (!Flag.Activo) return false;
            var ld = LocalDeath.Instance;
            bool muerto = ld != null && ld.PresentationReady;
            bool sobrevivio = NightResult.Sobrevivio;
            if (!muerto && !sobrevivio) { _selector = false; return false; }
            if (!sobrevivio && ld != null && ld.Espectando) return false;   // barra vieja
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
            string firma = $"{_selector}|{sobrevivio}|{NightTransition.PuedeReiniciar}|" +
                           $"{(ld != null && ld.PuedeEspectar)}|{EnVisor}|{NightResult.NocheDesbloqueada}";
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
            if (sobrevivio && sesion != null && sesion.EnTutorial)
            {
                _cuerpo.Add(TemaUI.Texto("PRÁCTICA COMPLETA", TemaUI.Bebas, 130, TemaUI.Green, 10f, TextAnchor.MiddleCenter));
                var bien = TemaUI.Texto(sesion.Tutorial switch
                                        {
                                            TutorialTipo.Sorken => "Ya sabés echar al Sorken: en la puerta y cuando ya entró.",
                                            TutorialTipo.Arbmos => "Ya sabés ocultarte del Arbmos: a oscuras se va, con luz se enoja.",
                                            TutorialTipo.Libro  => "Ya sabés salvar el libro: luz intensa apenas se empieza a apagar.",
                                            TutorialTipo.Veleth => "Ya conocés a Veleth: la luz no la frena. Que el libro no se apague.",
                                            _                   => "Ya sabés recargar la linterna y juntar reliquias.",
                                        },
                                        TemaUI.Elite, 38, TemaUI.Muted, 0f, TextAnchor.MiddleCenter);
                bien.style.marginTop = 8; bien.style.marginBottom = 30;
                _cuerpo.Add(bien);
                var repetir = PiezasUI.Boton(Nav, "REPETIR PRÁCTICA", NightTransition.Reintentar);
                repetir.style.backgroundImage = TemaUI.DegradeRojo;
                repetir.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                _cuerpo.Add(repetir);
                _cuerpo.Add(PiezasUI.Boton(Nav, "VOLVER A TUTORIALES", () => SceneFlow.GoTo(SceneFlow.EscenaMenu)));
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
