using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rediseno.UI
{
    // Mantiene el visor puesto entre escenas (Rediseño 2.0). En los menús el visor es
    // ModoVisor; en la partida, MRCardboardController. Este puente:
    //   - al entrar a la partida con el visor puesto, prende Cardboard solo;
    //   - en la partida, refleja en ModoVisor si el jugador entra o sale de Cardboard;
    //   - al volver al menú con el visor puesto, vuelve a poner la pantalla apaisada.
    public class PuenteVisor : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<PuenteVisor>() != null) return;
            var go = new GameObject("PuenteVisor");
            DontDestroyOnLoad(go);
            go.AddComponent<PuenteVisor>();
        }

        private MRCardboardController _mr;
        private float _intentarHasta;

        private void OnEnable()  => SceneManager.sceneLoaded += AlCargar;
        private void OnDisable() => SceneManager.sceneLoaded -= AlCargar;

        private void AlCargar(Scene s, LoadSceneMode m)
        {
            _mr = null;
            if (!Flag.Activo) return;
            if (s.name == SceneFlow.EscenaJuego && ModoVisor.Activo) _intentarHasta = Time.unscaledTime + 6f;
            else _intentarHasta = 0f;
            if (s.name == SceneFlow.EscenaMenu) ModoVisor.ReaplicarOrientacion();
        }

        private void Update()
        {
            if (!Flag.Activo) return;
            if (SceneManager.GetActiveScene().name != SceneFlow.EscenaJuego) return;
            if (_mr == null) _mr = FindAnyObjectByType<MRCardboardController>();
            if (_mr == null) return;

            // Entrar solo a Cardboard (reintenta unos segundos: la cámara AR tarda en estar).
            if (_intentarHasta > 0f)
            {
                if (MRCardboardController.Activo || Time.unscaledTime > _intentarHasta) _intentarHasta = 0f;
                else { _mr.SetCardboard(true); return; }
            }

            // De ahí en más, el visor de los menús sigue a lo que el jugador haga en partida.
            // Sólo con el controlador vivo: al salir de la partida (SALIR AL MENÚ) se apaga
            // solo en el OnDisable de la descarga, y eso NO es "el jugador salió del visor".
            if (_mr.isActiveAndEnabled) ModoVisor.Reflejar(MRCardboardController.Activo);
        }
    }
}
