using UnityEngine;

namespace Gameplay
{
    // El TUTORIAL como un camino de pasos encadenados: linterna → pilas y cordura → Sorken →
    // Arbmos → libro y Veleth → "listo para la Noche 1". Cada paso es una de las prácticas
    // (TutorialLinterna, TutorialPilasReliquias, …) con su NightConfig de Resources/Tutoriales.
    //
    // Se arranca desde el menú eligiendo un escenario (el escaneo ya tiene que existir), y
    // al superar un paso FinDeNoche2 ofrece CONTINUAR: el siguiente arranca en la MISMA
    // sesión AR (NightTransition, igual que pasar a la noche siguiente), sin volver a
    // sincronizar la imagen y sin la pantalla de INICIAR NOCHE en el medio.
    //
    // El progreso es por dispositivo (PlayerPrefs): cuántos pasos seguidos superó. Salir a
    // mitad del camino lo retoma desde el primer paso sin superar.
    public static class TutorialCamino
    {
        public static readonly TutorialTipo[] Pasos =
        {
            TutorialTipo.Linterna,
            TutorialTipo.PilasReliquias,
            TutorialTipo.Sorken,
            TutorialTipo.Arbmos,
            TutorialTipo.Libro,
        };

        private const string ClaveProgreso = "mortuorium_tutorial_paso";

        // Pasos superados en orden (0 = ninguno; Pasos.Length = tutorial completo).
        public static int Progreso
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(ClaveProgreso, 0), 0, Pasos.Length);
            private set { PlayerPrefs.SetInt(ClaveProgreso, Mathf.Clamp(value, 0, Pasos.Length)); PlayerPrefs.Save(); }
        }

        public static bool Completo => Progreso >= Pasos.Length;

        // Primer paso sin superar (el que retoma CONTINUAR); Ninguno si ya está completo.
        public static TutorialTipo PasoActual => Completo ? TutorialTipo.Ninguno : Pasos[Progreso];

        public static int Indice(TutorialTipo tipo) => System.Array.IndexOf(Pasos, tipo);

        // ¿Se puede jugar ese paso? Los superados (para repetirlos) y el actual.
        public static bool Disponible(TutorialTipo tipo)
        {
            int i = Indice(tipo);
            return i >= 0 && i <= Progreso;
        }

        // Paso siguiente a ése (Ninguno si era el último).
        public static TutorialTipo Siguiente(TutorialTipo tipo)
        {
            int i = Indice(tipo);
            return i >= 0 && i + 1 < Pasos.Length ? Pasos[i + 1] : TutorialTipo.Ninguno;
        }

        public static void ReiniciarProgreso() => Progreso = 0;

        // Se superó esa práctica (sea en el camino o suelta, al repetirla). Sólo avanza si
        // era el paso que tocaba: repetir uno anterior no salta nada.
        public static void MarcarSuperado(TutorialTipo tipo)
        {
            int i = Indice(tipo);
            if (i >= 0 && i == Progreso) Progreso = i + 1;
        }

        // CONTINUAR de FinDeNoche2 tras superar un paso: arranca el siguiente sin recargar
        // la escena. Sólo el host (un jugador: siempre).
        public static void IrAlSiguiente()
        {
            var s = GameSession.Instance;
            if (s == null || !NightTransition.PuedeReiniciar) return;
            var sig = Siguiente(s.Tutorial);
            if (sig == TutorialTipo.Ninguno) return;
            var config = Tutoriales.Config(sig);
            if (config == null)
            {
                Debug.LogError($"[TutorialCamino] Falta la NightConfig del paso {sig} en Resources/Tutoriales.");
                return;
            }
            s.Tutorial      = sig;
            s.SelectedNight = config;
            Tutoriales.Crear(sig);
            AutoIniciar.Pedir($"PASO {Indice(sig) + 1} · {Tutoriales.Titulo(sig)}");
            NightTransition.Reintentar();
        }

        // REINTENTAR / REPETIR una práctica (en el camino o suelta): igual que pasar de
        // paso, sin la pantalla de sincronización ni INICIAR NOCHE en el medio.
        public static void ReintentarPaso()
        {
            var s = GameSession.Instance;
            if (s == null || !s.EnTutorial || !NightTransition.PuedeReiniciar) return;
            AutoIniciar.Pedir(s.Camino ? $"PASO {Indice(s.Tutorial) + 1} · {Tutoriales.Titulo(s.Tutorial)}"
                                       : Tutoriales.Titulo(s.Tutorial));
            NightTransition.Reintentar();
        }

        // ¿Se está tapando el pase entre una noche del tutorial y la siguiente? Mientras
        // tanto no se vuelven a mostrar las paredes escaneadas (NightTransition.ResetLocal)
        // ni la pantalla de sincronización: duraban un instante y se veían como un parpadeo
        // del modo escaneo.
        public static bool EnTransicion => AutoIniciar.Tapando;

        // JUGAR NOCHE 1 de la tarjeta final: misma sesión AR, ya fuera del tutorial.
        public static bool JugarNoche1()
        {
            var s = GameSession.Instance;
            if (s == null || !s.HayCatalogoDeNoches) return false;
            s.Tutorial = TutorialTipo.Ninguno;
            s.Camino   = false;
            return NightTransition.IrANoche(0);
        }

        // Tras pasar de paso (o reintentar), la sala de sincronización no tiene nada que
        // decidir (un jugador, ya ubicado): arranca sola en cuanto se puede, detrás de una
        // tarjeta con el nombre del paso. Si en MaxTapado no se pudo (hay que colocar anclas
        // o re-ubicar la imagen), la tarjeta se va, vuelven las paredes y la sala para que el
        // jugador lo resuelva, y arranca sola después.
        private class AutoIniciar : MonoBehaviour
        {
            private const float Entrada = 0.15f, Salida = 0.45f, MinTapado = 0.8f, MaxTapado = 2.5f;

            private static AutoIniciar _instancia;

            // Vive en la escena: salir de la partida (al menú) cancela el pedido.
            public static void Pedir(string titulo)
            {
                if (_instancia == null) new GameObject("TutorialCamino.AutoIniciar").AddComponent<AutoIniciar>();
                _instancia._titulo = titulo;
            }

            public static bool Tapando => _instancia != null && !_instancia._soltado;

            private string _titulo;
            private float _desde, _iniciadoEn = -1f, _soltadoEn = -1f;
            private bool _soltado;

            private void Awake()
            {
                _instancia = this;
                _desde = Time.unscaledTime;
            }

            private void OnDestroy()
            {
                if (_instancia == this) _instancia = null;
            }

            private void Update()
            {
                float t = Time.unscaledTime;
                if (_iniciadoEn >= 0f)
                {
                    // Ya arrancó: la tarjeta se queda lo mínimo y se desvanece.
                    if (!_soltado && t - _desde >= MinTapado) Soltar(t);
                    if (_soltado && t - _soltadoEn >= Salida) Destroy(gameObject);
                    return;
                }

                if (!_soltado && t - _desde >= MaxTapado)
                {
                    Soltar(t);
                    // Hay que resolver algo en la sala: que se vea el mapa para eso.
                    SceneOccluderMode.Instance?.Restore();
                }

                var lobby = ARLobbyManager.Instance;
                // Un frame de gracia: el reinicio todavía está bajando la noche anterior.
                if (lobby == null || t - _desde < 0.2f || !lobby.CanStartGame) return;
                lobby.ServerStartGame();
                _iniciadoEn = t;
                if (_soltado) Destroy(gameObject);
            }

            private void Soltar(float t)
            {
                _soltado = true;
                _soltadoEn = t;
            }

            private float Opacidad
            {
                get
                {
                    float t = Time.unscaledTime;
                    if (_soltado) return 1f - Mathf.Clamp01((t - _soltadoEn) / Salida);
                    return Mathf.Clamp01((t - _desde) / Entrada);
                }
            }

            private void OnGUI()
            {
                if (Event.current.type != EventType.Repaint) return;
                float a = Opacidad;
                if (a <= 0f) return;
                GUI.depth = -1000;
                var prevM = GUI.matrix;
                var prevC = GUI.color;
                GUI.matrix = Matrix4x4.identity;
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture,
                                ScaleMode.StretchToFill, true, 0f, MortuoriumTheme.Bg, 0f, 0f);
                if (!string.IsNullOrEmpty(_titulo))
                {
                    float s = Screen.height / 1080f;
                    var estilo = MortuoriumTheme.Estilo(MortuoriumTheme.FBebas, Mathf.RoundToInt(64 * s),
                                                        MortuoriumTheme.Cream, TextAnchor.MiddleCenter);
                    if (MRCardboardController.Activo)
                    {
                        Rediseno.UI.OjosVisor.Centros(out var izq, out var der);
                        Titulo(izq, s, estilo);
                        Titulo(der, s, estilo);
                    }
                    else Titulo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), s * 1.2f, estilo);
                }
                GUI.color = prevC;
                GUI.matrix = prevM;
            }

            private void Titulo(Vector2 c, float s, GUIStyle estilo) =>
                GUI.Label(new Rect(c.x - 400f * s, c.y - 50f * s, 800f * s, 100f * s), _titulo, estilo);
        }
    }
}
