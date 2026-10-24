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
            AutoIniciar.Pedir();
            NightTransition.Reintentar();
        }

        // JUGAR NOCHE 1 de la tarjeta final: misma sesión AR, ya fuera del tutorial.
        public static bool JugarNoche1()
        {
            var s = GameSession.Instance;
            if (s == null || !s.HayCatalogoDeNoches) return false;
            s.Tutorial = TutorialTipo.Ninguno;
            s.Camino   = false;
            return NightTransition.IrANoche(0);
        }

        // Tras pasar de paso, la sala de sincronización no tiene nada que decidir (un
        // jugador, ya ubicado): arranca sola en cuanto se puede. Si hay que colocar anclas o
        // re-ubicar la imagen, espera a que el jugador lo resuelva y arranca después.
        private class AutoIniciar : MonoBehaviour
        {
            private static AutoIniciar _instancia;

            public static void Pedir()
            {
                if (_instancia == null) new GameObject("TutorialCamino.AutoIniciar").AddComponent<AutoIniciar>();
            }

            private float _desde;

            // Vive en la escena: salir de la partida (al menú) cancela el pedido.
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
                var lobby = ARLobbyManager.Instance;
                // Un frame de gracia: el reinicio todavía está bajando la noche anterior.
                if (lobby == null || Time.unscaledTime - _desde < 0.2f) return;
                if (!lobby.CanStartGame) return;
                lobby.ServerStartGame();
                Destroy(gameObject);
            }
        }
    }
}
