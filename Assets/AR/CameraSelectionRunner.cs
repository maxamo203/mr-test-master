using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

// Brazo ejecutor de CameraSelection: es el único que toca el ARCameraManager.
//
// Se auto-crea en cualquier escena (RuntimeInitializeOnLoadMethod + DontDestroyOnLoad),
// igual que AudioManager y GamepadManager: no hay wiring en el Editor y sobrevive a
// SceneFlow.GoTo. En las escenas sin AR (menú principal) simplemente no encuentra cámara
// y se queda quieto.
//
// Coste en release: CERO por frame en régimen. Lo único que corre es
//   - una búsqueda del ARCameraManager por escena cargada (con reintentos acotados
//     mientras el rig AR todavía no existe), y
//   - una suscripción a frameReceived que se DA DE BAJA apenas aplicó la selección y
//     midió el ángulo de lo que se ve.
// Nada de esto vive en un Update.
[DefaultExecutionOrder(-60)]
public class CameraSelectionRunner : MonoBehaviour
{
    private static CameraSelectionRunner _inst;

    private ARCameraManager _mgr;
    private bool     _suscripto;
    private bool     _aplicado;      // ya se aplicó la selección en esta cámara
    private bool     _medido;        // ya se midió el modo en uso
    private float    _medirDesde;    // no medir antes de esto (asentamiento del modo)
    private Coroutine _buscando, _barrido;

    // Cuántas veces y cada cuánto se busca el rig AR después de cargar una escena. El
    // ARCameraManager suele estar en la escena, pero el bootstrap puede crearlo tarde.
    private const float EsperaBusqueda = 1f;
    private const int   IntentosBusqueda = 8;

    // Cuánto se espera antes de creerle a las intrínsecas después de cambiar de modo:
    // el cambio reinicia la captura y los primeros frames todavía son del modo viejo, así
    // que sin esto se guardaría el ángulo del anterior a nombre del nuevo — justo el dato
    // con el que después se ordena la lista y se resuelve "la más amplia".
    private const float Asentamiento = 0.6f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_inst != null) return;
        var go = new GameObject("CameraSelection");
        _inst = go.AddComponent<CameraSelectionRunner>();
        DontDestroyOnLoad(go);
    }

    private void OnEnable()
    {
        _inst = this;   // Bootstrap sólo corre al arrancar la app
        SceneManager.sceneLoaded += AlCargarEscena;
        Rebuscar();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= AlCargarEscena;
        Desuscribir();
        if (_inst == this) _inst = null;
    }

    private void AlCargarEscena(Scene s, LoadSceneMode m) => Rebuscar();

    // ── Resolución del ARCameraManager ────────────────────────────────────

    private void Rebuscar()
    {
        if (_barrido != null) return;   // en medio de un barrido no se re-resuelve nada
        if (_buscando != null) { StopCoroutine(_buscando); _buscando = null; }
        Desuscribir();
        if (_mgr != null) { CameraSelection.CamaraMuerta(_mgr); _mgr = null; }
        _aplicado = _medido = false;
        _buscando = StartCoroutine(BuscarCamara());
    }

    private IEnumerator BuscarCamara()
    {
        for (int i = 0; i < IntentosBusqueda; i++)
        {
            var mgr = FindFirstObjectByType<ARCameraManager>();
            if (mgr != null) { Enganchar(mgr); _buscando = null; yield break; }
            yield return new WaitForSecondsRealtime(EsperaBusqueda);
        }
        _buscando = null;   // escena sin AR (menú): se vuelve a intentar al cargar otra
    }

    private void Enganchar(ARCameraManager mgr)
    {
        _mgr = mgr;
        // La cámara FRONTAL nunca se usa: el juego mira al cuarto. Forzarlo acá además
        // garantiza que las configuraciones que enumeramos sean las de la trasera.
        _mgr.requestedFacingDirection = CameraFacingDirection.World;
        Suscribir();
    }

    private void Suscribir()
    {
        if (_suscripto || _mgr == null) return;
        _mgr.frameReceived += AlRecibirFrame;
        _suscripto = true;
    }

    private void Desuscribir()
    {
        if (!_suscripto) return;
        if (_mgr != null) _mgr.frameReceived -= AlRecibirFrame;
        _suscripto = false;
    }

    // ── Aplicar + medir (y después, silencio) ─────────────────────────────

    private void AlRecibirFrame(ARCameraFrameEventArgs args)
    {
        if (_mgr == null || _barrido != null) return;

        if (!_aplicado)
        {
            // Enumera los modos del equipo y pone el elegido. Ojo al orden: esto puede
            // cambiar la configuración, con lo que la medición de abajo tiene que esperar
            // a los frames del modo NUEVO — si no, se guardaría el ángulo del viejo a
            // nombre del nuevo, que es justo el dato con el que después se ordena la lista.
            CameraSelection.CamaraLista(_mgr);
            _aplicado   = true;
            _medirDesde = Time.realtimeSinceStartup + Asentamiento;
            return;
        }

        if (_medido) { Desuscribir(); return; }
        if (Time.realtimeSinceStartup < _medirDesde) return;

        if (!CameraSelection.TryFov(_mgr, out float h, out float v, out var res)) return;

        // El aspecto de las intrínsecas es el detector de "ya cambió el modo". Es
        // best-effort: pasado el plazo se acepta lo que haya, porque no todos los
        // backends reportan la resolución de la captura (ARKit reporta la del video).
        if (Time.realtimeSinceStartup < _medirDesde + 4f &&
            !AspectoCoincide(CameraSelection.ClaveActiva, res)) return;

        CameraSelection.RegistrarMedicion(CameraSelection.ClaveActiva, h, v);
        _medido = true;
        Desuscribir();
    }

    // Algo cambió la configuración por fuera: hay que volver a medir lo que se ve.
    internal static void PedirMedicion()
    {
        if (_inst == null || _inst._barrido != null) return;   // el barrido mide por su cuenta
        _inst._medido     = false;
        _inst._medirDesde = Time.realtimeSinceStartup + Asentamiento;
        _inst.Suscribir();
    }

    // ── Barrido: medir TODOS los modos ────────────────────────────────────
    // Aplicar cada configuración reinicia la captura (y puede sacudir el tracking), así
    // que esto no se hace solo nunca: lo dispara el jugador desde Opciones, y sólo fuera
    // de una noche en curso (ver CameraSelection.PuedeMedir).

    internal static void IniciarBarrido()
    {
        if (_inst == null || !CameraSelection.PuedeMedir) return;
        _inst._barrido = _inst.StartCoroutine(_inst.Barrer());
    }

    private IEnumerator Barrer()
    {
        Desuscribir();
        CameraSelection.Midiendo    = true;
        CameraSelection.MedidoN     = 0;
        CameraSelection.MedidoTotal = CameraSelection.Modos.Count;
        CameraSelection.Notificar();

        // Copia de las claves: la lista se reordena con cada medición.
        int n = CameraSelection.Modos.Count;
        var claves = new string[n];
        for (int i = 0; i < n; i++) claves[i] = CameraSelection.Modos[i].clave;

        foreach (var clave in claves)
        {
            if (_mgr == null) break;
            if (!PonerPorClave(clave)) { CameraSelection.MedidoN++; continue; }
            yield return MedirActivo(clave);
            CameraSelection.MedidoN++;
            CameraSelection.Notificar();
        }

        // Dejar la cámara como la quiere el jugador (el barrido la fue moviendo).
        CameraSelection.Midiendo = false;
        CameraSelection.AplicarSeleccion();
        _barrido = null;
        CameraSelection.Notificar();

        // Si la escena cambió en el medio, Rebuscar() se salteó (no se re-resuelve durante
        // un barrido): hay que resolverla ahora o el runner se queda sin cámara.
        if (_mgr == null) { Rebuscar(); yield break; }

        _medido     = false;
        _medirDesde = Time.realtimeSinceStartup + Asentamiento;
        Suscribir();
    }

    private bool PonerPorClave(string clave)
    {
        if (_mgr == null) return false;
        bool ok = false;
        using (var cfgs = _mgr.GetConfigurations(Allocator.Temp))
        {
            for (int i = 0; i < cfgs.Length; i++)
            {
                if (CameraSelection.ClaveDe(cfgs[i]) != clave) continue;
                ok = CameraSelection.Poner(cfgs[i]);
                break;
            }
        }
        return ok;
    }

    // Espera a que la cámara entregue frames del modo recién puesto y anota su ángulo.
    private IEnumerator MedirActivo(string clave)
    {
        float desde  = Time.realtimeSinceStartup;
        float limite = desde + 3f;
        while (Time.realtimeSinceStartup < limite)
        {
            yield return null;
            if (_mgr == null) yield break;
            if (Time.realtimeSinceStartup - desde < Asentamiento) continue;
            if (!CameraSelection.TryFov(_mgr, out float h, out float v, out var res)) continue;
            if (!AspectoCoincide(clave, res)) continue;

            CameraSelection.RegistrarMedicion(clave, h, v);
            yield break;
        }
    }

    // ¿Las intrínsecas ya son del modo `clave`? Se compara por ASPECTO, no por resolución:
    // la resolución de las intrínsecas no siempre es la de la captura, pero el recorte del
    // sensor —que es justo lo que estamos midiendo— sí se ve en el aspecto.
    private static bool AspectoCoincide(string clave, Vector2Int res)
    {
        float esperado = AspectoDe(clave);
        if (esperado <= 0f || res.y <= 0) return true;   // sin dato con qué comparar
        return Mathf.Abs((float)res.x / res.y - esperado) <= 0.02f;
    }

    // "1600x1200@30" -> 1.333
    private static float AspectoDe(string clave)
    {
        if (string.IsNullOrEmpty(clave)) return 0f;
        int x = clave.IndexOf('x'), a = clave.IndexOf('@');
        if (x <= 0 || a <= x) return 0f;
        if (!int.TryParse(clave.Substring(0, x), out int w)) return 0f;
        if (!int.TryParse(clave.Substring(x + 1, a - x - 1), out int hgt) || hgt <= 0) return 0f;
        return (float)w / hgt;
    }
}
