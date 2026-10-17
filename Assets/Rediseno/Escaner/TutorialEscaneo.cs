using System;
using System.Collections;
using System.Collections.Generic;
using Gamepad;
using Rediseno.UI;
using Scanner;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using T = MortuoriumTheme;

namespace Rediseno.Escaner
{
    // Tutorial del primer escaneo (ScannerScene), con la estética del Rediseño 2.0. Siete
    // pasos: imagen de referencia → piso → paredes → apoyarlas en el piso → identificadores
    // → huecos de puerta (opcional) → muebles/cubos (opcional), y un cierre con GUARDAR.
    //
    // Cada paso tiene dos momentos:
    //   - Hoja: explicación abajo (qué hacer y por qué, con un dibujo). Tapa el HUD del
    //     escáner, que mientras tanto no dibuja ni recibe toques (TapaEscaner).
    //   - Guía: un banner arriba que dice el sub-paso, y el botón del escáner que hay que
    //     tocar latiendo en dorado (Resaltar, lo llama el HUD IMGUI al dibujar cada botón).
    //
    // Ningún paso tiene SIGUIENTE: el sub-paso y el "ya está" se deducen cada frame del
    // estado real (modo del FSM, SceneRegistry, FloorPoint). Si el jugador hace las cosas en
    // otro orden el tutorial lo acompaña igual, y un paso obligatorio que ya está hecho se
    // saltea solo. Los pasos que admiten varios objetos (identificadores, huecos, muebles)
    // se cierran con LISTO en el banner.
    //
    // Se abre solo la primera vez que se entra a un escaneo NUEVO (PlayerPrefs); después,
    // con el botón "?" de la barra superior del escáner (ReticleController).
    //
    // TIER: release. Vive sólo en ScannerScene y sin el tutorial abierto no hace nada por
    // frame más que EsVisible().
    public class TutorialEscaneo : PantallaUI
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
            if (s.name != SceneFlow.EscenaEscaner || _inst != null) return;
            // En la escena (no DontDestroyOnLoad): se va con el escáner.
            new GameObject("TutorialEscaneo").AddComponent<TutorialEscaneo>();
        }

        private enum Paso { Intro, Imagen, Piso, Paredes, Alinear, Identificar, Huecos, Muebles, Fin }

        private const int    TotalPasos     = 7;
        private const string KeyVisto       = "mortuorium_tutorial_escaneo_visto";
        private const float  ToleranciaPiso = 0.02f;   // m: base de la pared "apoyada"
        private const float  SegundosToast  = 1.4f;

        private static TutorialEscaneo _inst;

        private bool  _activo;            // tutorial en curso (aunque esté oculto por el modo)
        private bool  _mostrando;         // EsVisible() de este frame
        private Paso  _paso;
        private bool  _hoja = true;       // hoja de explicación (true) o guía con banner (false)
        private bool  _listoPaso;         // LISTO del banner en los pasos de varios objetos
        private bool  _salteoHuecos, _salteoMuebles;
        private float _avanzarEn = -1f;   // fin del toast de "paso hecho" → siguiente paso
        private float _toastHasta = -1f;
        private float _toquesBannerDesde;
        private int   _paredesAlEmpezar;  // paredes que había al empezar la polilínea actual

        // Guía del frame (la calcula CalcularGuia, la leen el banner y el HUD viejo).
        private string _guiaTexto = "", _guiaCuenta = "";
        private string _guiaBoton;        // LISTO / SALTAR del banner (null = sin botón)
        private Action _guiaAccion;
        private readonly HashSet<string> _objetivos = new();

        private ReferenceCaptureUI _captura;
        private ReticleController  _reticula;
        private ARImageAnchor      _ancla;

        // ── API para el HUD viejo (IMGUI) ─────────────────────────────────────
        // Hoja / intro / cierre a la vista: el HUD del escáner no se dibuja ni toma toques.
        public static bool TapaEscaner => _inst != null && _inst._mostrando && _inst._hoja;
        // Banner de guía a la vista: el HUD no muestra su propio hint (dirían lo mismo).
        public static bool GuiaVisible => _inst != null && _inst._mostrando && !_inst._hoja;

        public static bool EsObjetivo(string clave) =>
            GuiaVisible && _inst._avanzarEn < 0f && _inst._objetivos.Contains(clave);

        // Borde dorado que late alrededor de r si 'clave' es lo que hay que tocar ahora.
        // Mismo dorado que el foco de MenuNavegable. r en el espacio de GUI actual.
        // 'radio' = el de las esquinas del botón, para que el borde lo siga.
        public static void Resaltar(string clave, Rect r, float radio = T.Radio2)
        {
            if (Event.current.type != EventType.Repaint || !EsObjetivo(clave)) return;
            float t = (Time.unscaledTime % 1.3f) / 1.3f;
            T.BordeR(r, T.Tan, radio, 3f);
            float g = 3f + 10f * t;
            T.BordeR(new Rect(r.x - g, r.y - g, r.width + g * 2f, r.height + g * 2f),
                     new Color(T.Tan.r, T.Tan.g, T.Tan.b, 0.85f * (1f - t)), radio + g, 2f);
        }

        // Botón "?" del escáner: retoma desde el primer paso que falta.
        public static void Abrir()
        {
            if (_inst == null) return;
            _inst._activo = true;
            _inst.IrA(_inst.PrimerPendiente());
        }

        // ── Ciclo ─────────────────────────────────────────────────────────────
        protected override void Awake()
        {
            if (_inst != null && _inst != this) { Destroy(gameObject); return; }
            _inst = this;
            base.Awake();
        }

        protected override void OnDestroy()
        {
            if (_inst == this) _inst = null;
            base.OnDestroy();
        }

        protected override bool EnVisor => false;   // al escáner se entra saliendo del visor

        protected override void Start()
        {
            base.Start();
            StartCoroutine(DecidirSiAbrir());
        }

        // ScannerSceneBootstrap define en su Start si es un escaneo nuevo o una edición
        // (SaveLoadUI.NombreEdicion): se decide un par de frames después.
        private IEnumerator DecidirSiAbrir()
        {
            yield return null;
            yield return null;
            if (!string.IsNullOrEmpty(SaveLoadUI.NombreEdicion)) yield break;
            if (PlayerPrefs.GetInt(KeyVisto, 0) == 1) yield break;
            _activo = true;
            IrA(Paso.Intro);
        }

        protected override bool EsVisible()
        {
            if (!_activo || PauseMenuController.IsOpen || SaveLoadUI.EstaAbierto) return false;
            var fsm = ScanStateMachine.Instance;
            return fsm != null && ModoPermitido(fsm.Current);
        }

        // En qué modos del escáner se ve el tutorial. En los que no son del paso (anclas,
        // ajuste del origen, mover un objeto, otra herramienta) se esconde y vuelve solo.
        private bool ModoPermitido(ScannerMode m)
        {
            if (m == ScannerMode.Calibrating) return _paso == Paso.Intro || _paso == Paso.Imagen;
            if (m == ScannerMode.Idle || m == ScannerMode.Selected) return true;
            return _paso switch
            {
                Paso.Piso or Paso.Alinear => m == ScannerMode.Floor_Place,
                Paso.Paredes     => m is ScannerMode.Wall_V1 or ScannerMode.Wall_Height or ScannerMode.Wall_Vn,
                Paso.Identificar => m == ScannerMode.Marker_Place,
                Paso.Huecos      => m is ScannerMode.DoorPickWall or ScannerMode.Door_V1 or ScannerMode.Door_V2,
                Paso.Muebles     => m is ScannerMode.Cube_V1 or ScannerMode.Cube_V2 or ScannerMode.Cube_V3,
                _ => false,
            };
        }

        protected override void Update()
        {
            _mostrando = EsVisible();
            if (_activo)
            {
                CalcularGuia();
                if (!_hoja && _avanzarEn < 0f && Hecho(_paso)) Completar();
                if (_avanzarEn >= 0f && Time.unscaledTime >= _avanzarEn) { _avanzarEn = -1f; IrA(Siguiente(_paso)); }
            }
            RegistrarBloqueos();
            base.Update();
            if (_mostrando && !_hoja) TickToquesBanner();
        }

        // ── Estado del escaneo ────────────────────────────────────────────────
        private static ScannerMode Modo => ScanStateMachine.Instance != null
            ? ScanStateMachine.Instance.Current : ScannerMode.Calibrating;

        private static int Paredes => SceneRegistry.Instance != null ? SceneRegistry.Instance.Walls.Count : 0;
        private static int Marcadores => SceneRegistry.Instance != null ? SceneRegistry.Instance.Markers.Count : 0;
        private static int Cubos => SceneRegistry.Instance != null ? SceneRegistry.Instance.Cubes.Count : 0;

        private static int Huecos
        {
            get
            {
                var reg = SceneRegistry.Instance;
                if (reg == null) return 0;
                int n = 0;
                foreach (var w in reg.Walls) if (w != null) n += w.Doors.Count;
                return n;
            }
        }

        // Paredes con las dos esquinas de la base a ToleranciaPiso del piso marcado.
        private static int ParedesApoyadas(out int total)
        {
            total = 0;
            var reg = SceneRegistry.Instance;
            var piso = FloorPoint.Instance;
            if (reg == null) return 0;
            int ok = 0;
            foreach (var w in reg.Walls)
            {
                if (w == null) continue;
                total++;
                if (piso != null && Apoyada(w, piso.LocalY)) ok++;
            }
            return ok;
        }

        private static bool Apoyada(WallObject w, float pisoY) =>
            Mathf.Abs(w.ALocal.y - pisoY) <= ToleranciaPiso && Mathf.Abs(w.BLocal.y - pisoY) <= ToleranciaPiso;

        private static bool OrigenListo => WorldOrigin.Instance != null && WorldOrigin.Instance.IsReady;

        private bool Hecho(Paso p)
        {
            var m = Modo;
            switch (p)
            {
                case Paso.Imagen:
                    return OrigenListo && m != ScannerMode.Calibrating && m != ScannerMode.Origin_Adjust;
                case Paso.Piso:
                    return FloorPoint.Instance != null && m == ScannerMode.Idle;
                case Paso.Paredes:
                    return Paredes > 0 && m == ScannerMode.Idle;
                case Paso.Alinear:
                    int ok = ParedesApoyadas(out int total);
                    return total > 0 && ok == total && m == ScannerMode.Idle;
                case Paso.Identificar:
                case Paso.Huecos:
                case Paso.Muebles:
                    return _listoPaso;
                default:
                    return false;
            }
        }

        // Para retomar con "?": los pasos de varios objetos cuentan hechos si ya hay alguno.
        private Paso PrimerPendiente()
        {
            for (var p = Paso.Imagen; p < Paso.Fin; p++)
            {
                bool hecho = p switch
                {
                    Paso.Identificar => Marcadores > 0,
                    Paso.Huecos      => Huecos > 0 || _salteoHuecos,
                    Paso.Muebles     => Cubos > 0 || _salteoMuebles,
                    Paso.Imagen      => OrigenListo,
                    Paso.Piso        => FloorPoint.Instance != null,
                    Paso.Paredes     => Paredes > 0,
                    _                => Hecho(p),
                };
                if (!hecho) return p;
            }
            return Paso.Fin;
        }

        private static bool Opcional(Paso p) => p == Paso.Huecos || p == Paso.Muebles;

        // Siguiente paso, salteando los obligatorios que ya están hechos (las paredes
        // marcadas justo a la altura del piso no necesitan el paso de apoyarlas).
        private Paso Siguiente(Paso p)
        {
            var n = p + 1;
            while (n < Paso.Fin && n != Paso.Identificar && !Opcional(n) && Hecho(n)) n++;
            return n;
        }

        private void IrA(Paso p)
        {
            _paso = p;
            _hoja = true;
            _listoPaso = false;
            _avanzarEn = -1f;
            Reconstruir();
        }

        private void Completar()
        {
            _avanzarEn = Time.unscaledTime + SegundosToast;
            _toastHasta = _avanzarEn;
            _toastTexto.text = TextoHecho(_paso);
            AudioManager.Sonar(c => c.uiConfirmar);
        }

        private void Cerrar()
        {
            _activo = false;
            _objetivos.Clear();
            PlayerPrefs.SetInt(KeyVisto, 1);
            PlayerPrefs.Save();
        }

        private void Saltear()
        {
            if (_paso == Paso.Huecos) _salteoHuecos = true;
            if (_paso == Paso.Muebles) _salteoMuebles = true;
            IrA(Siguiente(_paso));
        }

        private string TextoHecho(Paso p) => p switch
        {
            Paso.Imagen      => ManualCalibration.Calibrado ? "CUARTO UBICADO" : "IMAGEN GUARDADA",
            Paso.Piso        => "PISO MARCADO",
            Paso.Paredes     => Paredes == 1 ? "1 PARED" : $"{Paredes} PAREDES",
            Paso.Alinear     => "PAREDES APOYADAS",
            Paso.Identificar => Marcadores == 1 ? "1 IDENTIFICADOR" : $"{Marcadores} IDENTIFICADORES",
            Paso.Huecos      => Huecos == 1 ? "HUECO ABIERTO" : $"{Huecos} HUECOS",
            Paso.Muebles     => Cubos == 1 ? "1 MUEBLE" : $"{Cubos} MUEBLES",
            _ => "LISTO",
        };

        // ── Guía del momento ──────────────────────────────────────────────────
        private void CalcularGuia()
        {
            _objetivos.Clear();
            _guiaCuenta = "";
            _guiaBoton = null;
            _guiaAccion = null;
            if (_hoja) { _guiaTexto = ""; return; }

            var m = Modo;
            var sel = ScanStateMachine.Instance != null ? ScanStateMachine.Instance.CurrentSelection : null;

            // Algo seleccionado fuera del paso de apoyar paredes: soltarlo para seguir.
            if (m == ScannerMode.Selected && _paso != Paso.Alinear)
            {
                Guia("Tenés algo seleccionado. Tocá DESELECCIONAR o LISTO para seguir.", "deseleccionar", "listo-edicion");
                return;
            }

            switch (_paso)
            {
                case Paso.Imagen:
                    if (_captura == null) _captura = FindFirstObjectByType<ReferenceCaptureUI>();
                    if (_captura != null && _captura.EnConfirmar)
                        Guia("Medí el ancho real de la imagen, cargalo en cm y tocá CONFIRMAR.", "confirmar");
                    else if (_captura != null && _captura.Esperando)
                    {
                        if (_ancla == null) _ancla = FindFirstObjectByType<ARImageAnchor>();
                        Guia(_ancla != null && _ancla.MuestrasActuales > 0
                            ? "Imagen encontrada, afinando… no muevas el celular."
                            : "Apuntá a la imagen, bien de frente. Ya la está buscando.");
                    }
                    else
                        Guia("Encuadrá la imagen de frente, bien adentro del recuadro, y tocá CAPTURAR.", "capturar");
                    break;

                case Paso.Piso:
                    if (m == ScannerMode.Floor_Place) Guia("Apuntá la mira al piso real y tocá COLOCAR.", "colocar");
                    else Guia("Abrí la herramienta PISO en la barra de abajo.", "piso");
                    break;

                case Paso.Paredes:
                    _guiaCuenta = Paredes > 0 ? $"PAREDES: {Paredes}" : "";
                    switch (m)
                    {
                        case ScannerMode.Wall_V1:
                            _paredesAlEmpezar = Paredes;
                            Guia("1 · Apuntá a una esquina, abajo, donde la pared toca el piso, y tocá COLOCAR.", "colocar");
                            break;
                        case ScannerMode.Wall_Height:
                            Guia("2 · Levantá la mira hasta el techo, justo arriba de esa esquina, y tocá COLOCAR.", "colocar");
                            break;
                        case ScannerMode.Wall_Vn when Paredes <= _paredesAlEmpezar:
                            Guia("3 · Apuntá a la otra esquina de la pared, en el piso, y tocá COLOCAR.", "colocar");
                            break;
                        case ScannerMode.Wall_Vn:
                            Guia("Seguí esquina por esquina con COLOCAR. En la última, tocá TERMINAR POLILÍNEA.",
                                 "colocar", "terminar");
                            break;
                        default:
                            Guia("Tocá PARED en la barra de abajo.", "pared");
                            break;
                    }
                    break;

                case Paso.Alinear:
                {
                    int ok = ParedesApoyadas(out int total);
                    _guiaCuenta = total > 0 ? $"APOYADAS: {ok} / {total}" : "";
                    if (FloorPoint.Instance == null)
                    {
                        if (m == ScannerMode.Floor_Place) Guia("Apuntá la mira al piso real y tocá COLOCAR.", "colocar");
                        else Guia("Primero hace falta el piso: abrí la herramienta PISO.", "piso");
                    }
                    else if (m == ScannerMode.Selected && sel is WallObject w)
                    {
                        if (Apoyada(w, FloorPoint.Instance.LocalY))
                            Guia("Quedó apoyada. Tocá DESELECCIONAR para cerrar el panel.", "deseleccionar");
                        else
                            Guia("Tocá MOVER AL PISO.", "mover-al-piso");
                    }
                    else if (m == ScannerMode.Selected)
                        Guia("Eso no es una pared. Tocá DESELECCIONAR o LISTO y elegí una pared.", "deseleccionar", "listo-edicion");
                    else
                        Guia("Tocá una de las paredes para elegirla.");
                    break;
                }

                case Paso.Identificar:
                    _guiaCuenta = Marcadores > 0 ? $"IDENTIFICADORES: {Marcadores}" : "";
                    if (m == ScannerMode.Marker_Place)
                    {
                        var mb = FindMarkerBuilder();
                        string tipo = mb != null && mb.PendingType != null ? mb.PendingType.DisplayName.ToLowerInvariant() : "puerta";
                        Guia($"Apuntá a la pared, justo donde está la {tipo}, y tocá COLOCAR.", "colocar");
                    }
                    else if (SubmenuIdentificarAbierto())
                        Guia("Elegí qué hay en esa pared: PUERTA o VENTANA.", "tipo-marcador");
                    else if (Marcadores > 0)
                    {
                        Guia("¿Hay más puertas o ventanas? Marcalas con IDENTIFICAR. Si no, tocá LISTO.", "identificar");
                        Boton("LISTO, YA ESTÁN", () => _listoPaso = true);
                    }
                    else
                        Guia("Tocá IDENTIFICAR.", "identificar");
                    break;

                case Paso.Huecos:
                    _guiaCuenta = Huecos > 0 ? $"HUECOS: {Huecos}" : "";
                    switch (m)
                    {
                        case ScannerMode.DoorPickWall: Guia("Tocá la pared donde está la puerta."); break;
                        case ScannerMode.Door_V1:
                            Guia("Apuntá a la esquina de abajo de la puerta y tocá COLOCAR.", "colocar"); break;
                        case ScannerMode.Door_V2:
                            Guia("Ahora la esquina de arriba, del lado opuesto, y COLOCAR.", "colocar"); break;
                        default:
                            Guia(Huecos > 0 ? "¿Otra puerta abierta? Repetí con AGUJERO PUERTA. Si no, tocá LISTO."
                                            : "Tocá AGUJERO PUERTA.", "puerta");
                            Boton(Huecos > 0 ? "LISTO" : "SALTAR ESTE PASO",
                                  () => { if (Huecos > 0) _listoPaso = true; else Saltear(); });
                            break;
                    }
                    break;

                case Paso.Muebles:
                    _guiaCuenta = Cubos > 0 ? $"MUEBLES: {Cubos}" : "";
                    switch (m)
                    {
                        case ScannerMode.Cube_V1:
                            Guia("Apuntá a una esquina de abajo del mueble y tocá COLOCAR.", "colocar"); break;
                        case ScannerMode.Cube_V2:
                            Guia("Ahora la esquina opuesta, arriba y en diagonal, y COLOCAR.", "colocar"); break;
                        case ScannerMode.Cube_V3:
                            Guia("Apuntá a un punto del lado largo para fijar el giro y COLOCAR, o tocá CONFIRMAR SIN ROTAR.",
                                 "colocar", "sin-rotar");
                            break;
                        default:
                            Guia(Cubos > 0 ? "¿Otro mueble grande? Repetí con CUBO. Si no, tocá LISTO."
                                           : "Tocá CUBO en la barra de abajo.", "cubo");
                            Boton(Cubos > 0 ? "LISTO" : "SALTAR ESTE PASO",
                                  () => { if (Cubos > 0) _listoPaso = true; else Saltear(); });
                            break;
                    }
                    break;
            }
        }

        private void Guia(string texto, params string[] objetivos)
        {
            _guiaTexto = texto;
            foreach (var o in objetivos) _objetivos.Add(o);
        }

        private void Boton(string texto, Action accion) { _guiaBoton = texto; _guiaAccion = accion; }

        private MarkerBuilder _markerBuilder;
        private MarkerBuilder FindMarkerBuilder() =>
            _markerBuilder != null ? _markerBuilder : (_markerBuilder = FindFirstObjectByType<MarkerBuilder>());

        private bool SubmenuIdentificarAbierto()
        {
            if (_reticula == null) _reticula = FindFirstObjectByType<ReticleController>();
            return _reticula != null && _reticula.SubmenuIdentificarAbierto;
        }

        // ── Contenido de cada paso ────────────────────────────────────────────
        private struct Contenido
        {
            public string Titulo, Cuerpo, Primario, Secundario, SubSecundario;
            public string[] Consejos;
        }

        private static Contenido De(Paso p) => p switch
        {
            Paso.Imagen => new Contenido
            {
                Titulo = "ELEGÍ Y FOTOGRAFIÁ LA IMAGEN",
                Cuerpo = "Es el punto fijo de tu cuarto. Cada vez que juegues, el celular la busca para saber dónde quedó todo lo que escaneaste.",
                Consejos = new[]
                {
                    "Con mucho detalle y nada simétrica: si se ve igual dada vuelta, el cuarto puede aparecer girado.",
                    "Sin reflejos: mejor papel mate que un cuadro con vidrio.",
                    "Que no se mueva nunca. Si la sacás, vas a tener que ubicar el cuarto a mano.",
                    "Tené una regla a mano: te va a pedir el ancho real en cm.",
                },
            },
            Paso.Piso => new Contenido
            {
                Titulo = "MARCÁ EL PISO",
                Cuerpo = "Un solo punto sobre el piso real. Es el nivel en el que después vas a apoyar las paredes.",
                Consejos = new[]
                {
                    "Apuntá a una parte del piso despejada, sin alfombras gruesas.",
                    "Si quedó corrido, tocalo y arrastralo hasta donde va.",
                },
            },
            Paso.Paredes => new Contenido
            {
                Titulo = "LEVANTÁ LAS PAREDES",
                Cuerpo = "La primera pared sale con tres puntos: 1 una esquina en el piso, 2 arriba de esa esquina en el techo y 3 la otra esquina en el piso. Después seguís esquina por esquina.",
                Consejos = new[]
                {
                    "Las esquinas se marcan abajo, en la línea donde la pared toca el piso.",
                    "El punto 2 fija la altura de todas las paredes: apuntá justo arriba del 1.",
                    "Recorré el cuarto en un solo sentido y cerrá con TERMINAR POLILÍNEA.",
                },
            },
            Paso.Alinear => new Contenido
            {
                Titulo = "APOYALAS EN EL PISO",
                Cuerpo = "Marcando esquinas a mano, las paredes casi nunca quedan a la altura justa: flotan un poco o se hunden. Con un toque las bajás al piso que marcaste.",
                Consejos = new[]
                {
                    "Se acomoda la pared entera, con todas sus esquinas a la vez.",
                    "Por eso el piso va primero: sin piso marcado, este botón no está.",
                },
            },
            Paso.Identificar => new Contenido
            {
                Titulo = "MARCÁ PUERTAS Y VENTANAS",
                Cuerpo = "Los identificadores le dicen al juego por dónde se entra y se sale del cuarto. No cambian las paredes: son marcas sobre ellas.",
                Consejos = new[]
                {
                    "Marcá cada puerta y cada ventana real del cuarto.",
                    "Quedan del lado de la pared que estás mirando: marcalas parado adentro del cuarto.",
                },
            },
            Paso.Huecos => new Contenido
            {
                Titulo = "ABRÍ LOS HUECOS DE PUERTA",
                Cuerpo = "Si una puerta suele quedar abierta, recortá el hueco en la pared virtual para poder ver y moverte a través de ella.",
                Consejos = new[]
                {
                    "Es opcional: si tus puertas quedan cerradas, saltealo.",
                    "Se marca con dos esquinas: la de abajo y la de arriba del lado opuesto.",
                },
                Primario = "ABRIR UN HUECO",
                Secundario = "SALTAR ESTE PASO", SubSecundario = "Mis puertas quedan cerradas",
            },
            Paso.Muebles => new Contenido
            {
                Titulo = "MARCÁ LOS MUEBLES",
                Cuerpo = "Los muebles grandes (cama, mesa, ropero) se marcan como cubos. Lo que acecha los esquiva, y encima de ellos pueden aparecer pilas.",
                Consejos = new[]
                {
                    "Tres puntos: una esquina de abajo, la opuesta arriba en diagonal y uno más sobre el lado largo para el giro.",
                    "Si el mueble está alineado con las paredes, CONFIRMAR SIN ROTAR te ahorra el tercer punto.",
                    "Es opcional, pero con los muebles marcados la noche se juega mejor.",
                },
                Primario = "MARCAR UN MUEBLE",
                Secundario = "SALTAR ESTE PASO", SubSecundario = "No hay muebles grandes",
            },
            _ => default,
        };

        private static int Numero(Paso p) => (int)p;   // Imagen = 1 … Muebles = 7

        // ── Construcción ──────────────────────────────────────────────────────
        private VisualElement _contenedor;
        private VisualElement _bloque;     // hoja o banner actual (para UIBlocker)
        private VisualElement _toast;
        private Label _toastTexto;
        private Label _bannerTexto, _bannerCuenta;
        private VisualElement _bannerBoton;
        private Label _bannerBotonTexto;
        private readonly MenuNavegable _navBanner = new();

        protected override void Construir(VisualElement raiz, MenuNavegable nav)
        {
            _contenedor = PiezasUI.Ignorar(new VisualElement());
            PiezasUI.Llenar(_contenedor);
            raiz.Add(_contenedor);

            // Toast "paso hecho": pastilla con tilde verde, centrada, sobre todo lo demás.
            _toast = PiezasUI.Ignorar(new VisualElement());
            _toast.style.position = Position.Absolute;
            _toast.style.left = 0; _toast.style.right = 0;
            _toast.style.top = Length.Percent(40);
            _toast.style.flexDirection = FlexDirection.Row;
            _toast.style.justifyContent = Justify.Center;
            var pastilla = PiezasUI.Ignorar(new VisualElement());
            pastilla.style.flexDirection = FlexDirection.Row;
            pastilla.style.alignItems = Align.Center;
            pastilla.style.backgroundColor = new Color(0.047f, 0.039f, 0.035f, 0.94f);
            TemaUI.Radio(pastilla, 50);
            TemaUI.Borde(pastilla, 2, new Color(TemaUI.Green.r, TemaUI.Green.g, TemaUI.Green.b, 0.6f));
            pastilla.style.paddingTop = 20; pastilla.style.paddingBottom = 20;
            pastilla.style.paddingLeft = 22; pastilla.style.paddingRight = 40;
            var tilde = PiezasUI.Ignorar(new VisualElement());
            tilde.style.width = 60; tilde.style.height = 60;
            tilde.style.marginRight = 22;
            tilde.generateVisualContent += DibujosTutorial.Tilde;
            pastilla.Add(tilde);
            _toastTexto = TemaUI.Texto("", TemaUI.Bebas, 54, TemaUI.Cream, 6f);
            _toastTexto.style.whiteSpace = WhiteSpace.NoWrap;
            pastilla.Add(_toastTexto);
            _toast.Add(pastilla);
            _toast.style.display = DisplayStyle.None;
            raiz.Add(_toast);
        }

        private void Reconstruir()
        {
            if (_contenedor == null) return;
            _contenedor.Clear();
            Nav.Limpiar();
            _navBanner.Limpiar();
            Nav.Volver = null;
            Desplazable = null;
            _bloque = null;
            _bannerTexto = _bannerCuenta = null;
            _bannerBoton = null;
            _toquesBannerDesde = Time.unscaledTime + 0.3f;

            if (_paso == Paso.Intro) ConstruirIntro();
            else if (_paso == Paso.Fin) ConstruirFin();
            else if (_hoja) ConstruirHoja();
            else ConstruirBanner();
        }

        // Pantalla completa (intro / cierre): velo sobre la cámara y contenido abajo.
        private VisualElement PantallaCompleta()
        {
            var velo = PiezasUI.Ignorar(new VisualElement());
            PiezasUI.Llenar(velo);
            velo.style.backgroundColor = new Color(TemaUI.Bg.r, TemaUI.Bg.g, TemaUI.Bg.b, 0.35f);
            var sombra = PiezasUI.Ignorar(new VisualElement());
            PiezasUI.Llenar(sombra);
            sombra.style.backgroundImage = TemaUI.SombraAbajo;
            sombra.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            velo.Add(sombra);
            _contenedor.Add(velo);

            var col = PiezasUI.Columna();
            col.style.justifyContent = Justify.FlexEnd;
            _contenedor.Add(col);
            _bloque = col;
            return col;
        }

        private void ConstruirIntro()
        {
            var col = PantallaCompleta();
            col.Add(Ceja("ANTES DE EMPEZAR"));
            col.Add(Titulo("TU PRIMER ESCANEO", 104f));
            var frase = TemaUI.Texto("El cuarto entra al juego una sola vez.", TemaUI.Elite, 38, TemaUI.Muted);
            frase.style.marginBottom = 22;
            col.Add(frase);
            var medio = Medio(col);
            medio.Add(Cuerpo("Vas a copiar tu cuarto dentro de Mortuorium. Lleva unos minutos y se hace una vez: después, cada noche usa este mismo escaneo."));
            medio.Add(Necesitas("UNA IMAGEN PLANA", "Un póster, un cuadro o una tapa de revista que no vayas a mover."));
            medio.Add(Necesitas("BUENA LUZ", "Prendé las luces del cuarto mientras escaneás."));
            medio.Add(Necesitas("UNOS 5 MINUTOS", "Y lugar para caminar hasta cada pared."));

            var cta = Primario("EMPEZAR", $"{TotalPasos} pasos · los dos últimos son opcionales");
            cta.style.marginTop = 16;
            col.Add(cta);
            Nav.Enfocar(Nav.Agregar(cta, () => IrA(Siguiente(Paso.Intro))));
            col.Add(PiezasUI.Boton(Nav, "YA SÉ ESCANEAR", Cerrar));
            SoloElMedioCede(col);
            AjustarPie(col);
        }

        private void ConstruirFin()
        {
            var col = PantallaCompleta();
            col.Add(Ceja("ESCANEO COMPLETO"));
            col.Add(Titulo("TU CUARTO ESTÁ LISTO", 96f));

            var resumen = PiezasUI.Ignorar(PiezasUI.Tarjeta());
            resumen.style.paddingTop = 8; resumen.style.paddingBottom = 8;
            int ok = ParedesApoyadas(out int total);
            string imagen = CapturedReference.HasImage ? $"{CapturedReference.WidthMeters * 100f:0} cm"
                          : ManualCalibration.Calibrado ? "ubicado a mano" : "falta";
            Fila(resumen, "IMAGEN DE REFERENCIA", imagen, imagen != "falta");
            Fila(resumen, "PISO", FloorPoint.Instance != null ? "marcado" : "falta", FloorPoint.Instance != null);
            Fila(resumen, "PAREDES", total == 0 ? "faltan" : ok == total ? $"{total} · apoyadas" : $"{total} · {ok} apoyadas", total > 0);
            Fila(resumen, "IDENTIFICADORES", Marcadores > 0 ? Marcadores.ToString() : "faltan", Marcadores > 0);
            Fila(resumen, "HUECOS DE PUERTA", Huecos > 0 ? Huecos.ToString() : "salteado", Huecos > 0, opcional: true);
            Fila(resumen, "MUEBLES", Cubos > 0 ? Cubos.ToString() : "salteado", Cubos > 0, opcional: true, ultima: true);
            var medio = Medio(col);
            medio.Add(resumen);
            medio.Add(Cuerpo("Dejá la imagen donde está. Cada noche el juego la busca para volver a ubicar el cuarto."));

            var cta = Primario("GUARDAR ESCANEO", "Después le ponés un nombre");
            cta.style.marginTop = 10;
            col.Add(cta);
            Nav.Enfocar(Nav.Agregar(cta, () => { Cerrar(); SaveLoadUI.Abrir(); }));
            col.Add(PiezasUI.Boton(Nav, "REVISAR ANTES", Cerrar));
            SoloElMedioCede(col);
            AjustarPie(col);
        }

        private void ConstruirHoja()
        {
            var c = De(_paso);
            var hoja = new VisualElement();
            hoja.style.position = Position.Absolute;
            hoja.style.left = 0; hoja.style.right = 0; hoja.style.bottom = 0;
            hoja.style.backgroundColor = new Color(0.047f, 0.039f, 0.035f, 0.94f);
            TemaUI.Radio(hoja, 44);
            hoja.style.borderBottomLeftRadius = 0; hoja.style.borderBottomRightRadius = 0;
            TemaUI.Borde(hoja, 2, TemaUI.Linea);
            hoja.style.borderBottomWidth = 0;
            TemaUI.Relleno(hoja, 44, 48);
            hoja.style.maxHeight = Length.Percent(94);
            _contenedor.Add(hoja);
            _bloque = hoja;

            hoja.Add(Progreso($"PASO {Numero(_paso)} DE {TotalPasos}"));

            var t = Titulo(c.Titulo, 76f);
            t.style.marginTop = 26;
            if (Opcional(_paso))
            {
                var fila = PiezasUI.Ignorar(new VisualElement());
                fila.style.flexDirection = FlexDirection.Row;
                fila.style.alignItems = Align.Center;
                fila.style.flexWrap = Wrap.Wrap;
                fila.style.marginTop = 26;
                t.style.marginTop = 0;
                t.style.marginRight = 20;
                t.style.flexShrink = 1;   // en la fila: que el título ceda ancho a la etiqueta
                fila.Add(t);
                fila.Add(Etiqueta("OPCIONAL"));
                hoja.Add(fila);
            }
            else hoja.Add(t);

            var dibujo = PiezasUI.Ignorar(new VisualElement());
            dibujo.style.height = 200;
            dibujo.style.marginTop = 24; dibujo.style.marginBottom = 22;
            TemaUI.Radio(dibujo, 26);
            TemaUI.Borde(dibujo, 2, TemaUI.Linea);
            dibujo.style.backgroundColor = new Color(1f, 1f, 1f, 0.025f);
            var paso = _paso;
            dibujo.generateVisualContent += ctx => DibujosTutorial.Dibujar(Numero(paso), ctx);
            NumerosDelDibujo(dibujo, Numero(paso));
            // Dibujo + texto + consejos: lo que se desplaza si la pantalla es baja.
            var medio = Medio(hoja);
            medio.Add(dibujo);
            medio.Add(Cuerpo(c.Cuerpo));
            foreach (var consejo in c.Consejos) medio.Add(Consejo(consejo));

            var cta = Primario(c.Primario ?? "ENTENDIDO", "Te muestro dónde tocar");
            cta.style.marginTop = 24;
            hoja.Add(cta);
            Nav.Enfocar(Nav.Agregar(cta, () => { _hoja = false; Reconstruir(); }));

            if (c.Secundario != null)
            {
                var b = PiezasUI.Boton(Nav, c.Secundario, Saltear);
                var sub = TemaUI.Texto(c.SubSecundario, TemaUI.Mono, 24, TemaUI.Muted, 0f, TextAnchor.MiddleCenter);
                sub.style.marginTop = 4;
                b.Add(sub);
                hoja.Add(b);
            }
            else
            {
                hoja.Add(PiezasUI.Boton(Nav, "ATRÁS", Atras));
            }
            hoja.Add(Enlace("SALIR DEL TUTORIAL", Cerrar));
            Nav.Volver = Atras;
            SoloElMedioCede(hoja);
            AjustarPie(hoja);
        }

        // El número adentro de cada círculo del dibujo (Painter2D no dibuja texto). Se
        // reubican cuando el dibujo cambia de tamaño, con la misma escala que el trazo.
        private static void NumerosDelDibujo(VisualElement dibujo, int paso)
        {
            var numeros = DibujosTutorial.Numeros(paso);
            if (numeros.Length == 0) return;
            var etiquetas = new Label[numeros.Length];
            for (int i = 0; i < numeros.Length; i++)
            {
                var l = TemaUI.Texto(numeros[i].Texto, TemaUI.Bebas, 30, TemaUI.Bg, 0f, TextAnchor.MiddleCenter);
                l.style.position = Position.Absolute;
                dibujo.Add(l);
                etiquetas[i] = l;
            }
            dibujo.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var r = dibujo.contentRect;
                if (r.width <= 0f || r.height <= 0f) return;
                DibujosTutorial.Mapear(r, out float s, out Vector2 o);
                float lado = DibujosTutorial.RadioNumero * 2f * s;
                for (int i = 0; i < numeros.Length; i++)
                {
                    var l = etiquetas[i];
                    l.style.width = lado; l.style.height = lado;
                    l.style.left = o.x + numeros[i].X * s - lado * 0.5f;
                    l.style.top  = o.y + numeros[i].Y * s - lado * 0.5f;
                    l.style.fontSize = lado * 0.82f;
                }
            });
        }

        private void Atras() => IrA(_paso <= Paso.Imagen ? Paso.Intro : _paso - 1);

        // Banner de guía, arriba (debajo del título del escáner y del botón de pausa).
        private void ConstruirBanner()
        {
            var b = new VisualElement();
            b.style.position = Position.Absolute;
            b.style.left = 32; b.style.right = 32;
            b.style.backgroundColor = new Color(0.047f, 0.039f, 0.035f, 0.92f);
            TemaUI.Radio(b, 30);
            TemaUI.Borde(b, 2, TemaUI.Linea);
            TemaUI.Relleno(b, 24, 30);
            _contenedor.Add(b);
            _bloque = b;

            b.Add(Progreso($"PASO {Numero(_paso)} / {TotalPasos}"));

            var fila = PiezasUI.Ignorar(new VisualElement());
            fila.style.flexDirection = FlexDirection.Row;
            fila.style.alignItems = Align.FlexStart;
            fila.style.marginTop = 16;
            b.Add(fila);

            var textos = PiezasUI.Ignorar(new VisualElement());
            textos.style.flexGrow = 1; textos.style.flexShrink = 1;
            textos.style.marginRight = 20;
            textos.Add(TemaUI.Texto(De(_paso).Titulo, TemaUI.Bebas, 46, TemaUI.Cream, 4f));
            _bannerTexto = TemaUI.Texto("", TemaUI.Mono, 28, TemaUI.CreamDim);
            _bannerTexto.style.marginTop = 8;
            textos.Add(_bannerTexto);
            _bannerCuenta = TemaUI.Texto("", TemaUI.Mono, 24, TemaUI.Tan, 3f);
            _bannerCuenta.style.marginTop = 8;
            textos.Add(_bannerCuenta);
            fila.Add(textos);

            var ayuda = new VisualElement();
            ayuda.style.width = 84; ayuda.style.height = 84;
            TemaUI.Radio(ayuda, 42);
            TemaUI.Borde(ayuda, 2, new Color(1f, 1f, 1f, 0.25f));
            ayuda.style.backgroundColor = new Color(0.024f, 0.02f, 0.016f, 0.45f);
            ayuda.style.alignItems = Align.Center; ayuda.style.justifyContent = Justify.Center;
            ayuda.style.flexShrink = 0;
            ayuda.Add(TemaUI.Texto("?", TemaUI.Bebas, 52, TemaUI.Cream, 0f, TextAnchor.MiddleCenter));
            fila.Add(ayuda);
            _navBanner.Agregar(ayuda, () => { _hoja = true; Reconstruir(); });

            _bannerBoton = PiezasUI.Tarjeta();
            _bannerBoton.style.marginTop = 18; _bannerBoton.style.marginBottom = 0;
            _bannerBoton.style.paddingTop = 18; _bannerBoton.style.paddingBottom = 18;
            _bannerBoton.style.alignItems = Align.Center;
            _bannerBotonTexto = TemaUI.Texto("", TemaUI.Bebas, 40, TemaUI.Cream, 4f, TextAnchor.MiddleCenter);
            _bannerBoton.Add(_bannerBotonTexto);
            _bannerBoton.style.display = DisplayStyle.None;
            b.Add(_bannerBoton);
            _navBanner.Agregar(_bannerBoton, () => _guiaAccion?.Invoke());
        }

        // ── Piezas ────────────────────────────────────────────────────────────
        // Lista desplazable que ocupa su contenido y se encoge (desplazándose) sólo si no
        // entra: título y botones quedan siempre a la vista.
        private VisualElement Medio(VisualElement padre)
        {
            var sv = PiezasUI.Lista();
            sv.style.flexGrow = 0;
            sv.style.flexShrink = 1;
            padre.Add(sv);
            Desplazable = sv;
            return sv.contentContainer;
        }

        private static Label Ceja(string s)
        {
            var l = TemaUI.Texto(s, TemaUI.Mono, 26, TemaUI.Red, 7f);
            l.style.marginBottom = 10;
            return l;
        }

        private static Label Titulo(string s, float size)
        {
            var l = TemaUI.Texto(s, TemaUI.Bebas, size, TemaUI.Cream, 6f);
            l.style.flexShrink = 0;   // en columna no se aplasta (se desplaza el medio)
            return l;
        }

        private static Label Cuerpo(string s)
        {
            var l = TemaUI.Texto(s, TemaUI.Mono, 29, TemaUI.CreamDim);
            l.style.marginBottom = 16;
            return l;
        }

        private static VisualElement Consejo(string s)
        {
            var fila = PiezasUI.Ignorar(new VisualElement());
            fila.style.flexDirection = FlexDirection.Row;
            fila.style.alignItems = Align.FlexStart;
            fila.style.marginBottom = 10;
            var rombo = PiezasUI.Ignorar(new VisualElement());
            rombo.style.width = 12; rombo.style.height = 12;
            rombo.style.marginTop = 14; rombo.style.marginRight = 22; rombo.style.marginLeft = 4;
            rombo.style.flexShrink = 0;
            rombo.style.backgroundColor = TemaUI.Tan;
            rombo.style.rotate = new Rotate(new Angle(45f, AngleUnit.Degree));
            fila.Add(rombo);
            var l = TemaUI.Texto(s, TemaUI.Mono, 27, TemaUI.Muted);
            l.style.flexShrink = 1;
            fila.Add(l);
            return fila;
        }

        private static VisualElement Etiqueta(string s)
        {
            var e = PiezasUI.Ignorar(new VisualElement());
            TemaUI.Radio(e, 22);
            TemaUI.Borde(e, 2, TemaUI.Tan);
            e.style.paddingLeft = 18; e.style.paddingRight = 18;
            e.style.paddingTop = 4; e.style.paddingBottom = 4;
            e.Add(TemaUI.Texto(s, TemaUI.Mono, 22, TemaUI.Tan, 5f));
            return e;
        }

        // Fila de segmentos: hechos en crema, el actual en rojo; los opcionales sin relleno.
        private VisualElement Progreso(string etiqueta)
        {
            var fila = PiezasUI.Ignorar(new VisualElement());
            fila.style.flexDirection = FlexDirection.Row;
            fila.style.alignItems = Align.Center;
            var l = TemaUI.Texto(etiqueta, TemaUI.Mono, 24, TemaUI.Red, 7f);
            l.style.marginRight = 24;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            fila.Add(l);
            var segs = PiezasUI.Ignorar(new VisualElement());
            segs.style.flexDirection = FlexDirection.Row;
            segs.style.flexGrow = 1;
            for (var p = Paso.Imagen; p < Paso.Fin; p++)
            {
                var s = PiezasUI.Ignorar(new VisualElement());
                s.style.flexGrow = 1;
                s.style.height = 8;
                s.style.marginLeft = 10;
                TemaUI.Radio(s, 4);
                if (p < _paso) s.style.backgroundColor = TemaUI.CreamDim;
                else if (p == _paso) s.style.backgroundColor = TemaUI.Red;
                else if (Opcional(p)) TemaUI.Borde(s, 2, new Color(1f, 1f, 1f, 0.28f));
                else s.style.backgroundColor = new Color(1f, 1f, 1f, 0.14f);
                segs.Add(s);
            }
            fila.Add(segs);
            return fila;
        }

        private static VisualElement Primario(string titulo, string sub)
        {
            var cta = new VisualElement();
            TemaUI.Radio(cta, 42);
            TemaUI.Relleno(cta, 30, 46);
            TemaUI.Borde(cta, 2, new Color(1f, 1f, 1f, 0.10f));
            cta.style.backgroundImage = TemaUI.DegradeRojo;
            cta.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            cta.style.marginBottom = 18;
            cta.Add(TemaUI.Texto(titulo, TemaUI.Bebas, 64, TemaUI.Cream, 7f));
            if (!string.IsNullOrEmpty(sub))
            {
                var s = TemaUI.Texto(sub, TemaUI.Mono, 26, new Color(TemaUI.Cream.r, TemaUI.Cream.g, TemaUI.Cream.b, 0.82f));
                s.style.marginTop = 6;
                cta.Add(s);
            }
            return cta;
        }

        private VisualElement Enlace(string texto, Action accion)
        {
            var e = new VisualElement();
            e.style.alignItems = Align.Center;
            e.style.paddingTop = 16; e.style.paddingBottom = 8;
            TemaUI.Radio(e, 20);
            TemaUI.Borde(e, 2, Color.clear);
            e.Add(TemaUI.Texto(texto, TemaUI.Mono, 24, TemaUI.Dim, 5f, TextAnchor.MiddleCenter));
            Nav.Agregar(e, accion);
            return e;
        }

        private static VisualElement Necesitas(string titulo, string texto)
        {
            var t = PiezasUI.Ignorar(PiezasUI.Tarjeta());
            t.style.paddingTop = 20; t.style.paddingBottom = 20;
            t.style.marginBottom = 14;
            t.Add(TemaUI.Texto(titulo, TemaUI.Bebas, 42, TemaUI.Cream, 4f));
            var s = TemaUI.Texto(texto, TemaUI.Mono, 26, TemaUI.CreamDim);
            s.style.marginTop = 4;
            t.Add(s);
            return t;
        }

        private static void Fila(VisualElement padre, string nombre, string valor, bool ok,
                                 bool opcional = false, bool ultima = false)
        {
            var f = PiezasUI.Ignorar(new VisualElement());
            f.style.flexDirection = FlexDirection.Row;
            f.style.justifyContent = Justify.SpaceBetween;
            f.style.alignItems = Align.Center;
            f.style.paddingTop = 18; f.style.paddingBottom = 18;
            if (!ultima) { f.style.borderBottomWidth = 2; f.style.borderBottomColor = TemaUI.Linea; }
            f.Add(TemaUI.Texto(nombre, TemaUI.Bebas, 40, TemaUI.Cream, 4f));
            Color c = ok ? TemaUI.Green : opcional ? TemaUI.Dim : TemaUI.Red;
            f.Add(TemaUI.Texto(valor, TemaUI.Mono, 27, c, 0f, TextAnchor.MiddleRight));
            padre.Add(f);
        }

        // En UI Toolkit todo hijo se encoge por defecto: si la pantalla es baja se aplastaban
        // también los botones. Sólo cede la lista del medio, que se desplaza.
        private void SoloElMedioCede(VisualElement padre)
        {
            foreach (var hijo in padre.Children())
                hijo.style.flexShrink = hijo == Desplazable ? 1f : 0f;
        }

        // Margen de abajo = área segura (home indicator) en unidades del panel.
        private void AjustarPie(VisualElement e)
        {
            float k = PxAPanel();
            e.style.paddingBottom = 40f + (Screen.height - SafeArea.Bottom) * k;
        }

        // ── Por frame ─────────────────────────────────────────────────────────
        private float PxAPanel()
        {
            float w = Raiz != null ? Raiz.layout.width : float.NaN;
            return w > 0f && !float.IsNaN(w) ? w / Mathf.Max(1f, Screen.width) : AnchoRef / (float)Mathf.Max(1, Screen.width);
        }

        protected override void Animar(float dt)
        {
            bool toast = Time.unscaledTime < _toastHasta;
            _toast.style.display = toast ? DisplayStyle.Flex : DisplayStyle.None;

            if (_hoja || _bloque == null || _bannerTexto == null) return;

            // Debajo de la barra del escáner (título + botón de pausa, 96 px virtuales).
            _bloque.style.top = (SafeArea.Top + 96f * UIScale.Factor) * PxAPanel();

            if (_bannerTexto.text != _guiaTexto) _bannerTexto.text = _guiaTexto;
            if (_bannerCuenta.text != _guiaCuenta) _bannerCuenta.text = _guiaCuenta;
            _bannerCuenta.style.display = string.IsNullOrEmpty(_guiaCuenta) ? DisplayStyle.None : DisplayStyle.Flex;
            bool boton = _guiaBoton != null && _avanzarEn < 0f;
            _bannerBoton.style.display = boton ? DisplayStyle.Flex : DisplayStyle.None;
            if (boton && _bannerBotonTexto.text != _guiaBoton) _bannerBotonTexto.text = _guiaBoton;
        }

        // Lo que tapa el tutorial no es de la cámara ni de la selección: los sistemas que
        // leen toques crudos (SelectionController, EditorPlayerControls) consultan UIBlocker.
        private void RegistrarBloqueos()
        {
            if (!_mostrando || _bloque == null) return;
            // La hoja es modal: un toque en la cámara de arriba tampoco selecciona nada.
            if (_hoja)
            {
                UIBlocker.AddScreenRect(new Rect(0f, 0f, Screen.width, Screen.height));
                return;
            }
            var r = _bloque.worldBound;
            if (float.IsNaN(r.width) || r.width <= 0f) return;
            float s = 1f / PxAPanel();
            UIBlocker.AddScreenRect(new Rect(r.x * s, r.y * s, r.width * s, r.height * s));
        }

        // El banner no va en Nav: así el mando sigue manejando el escáner mientras guía.
        // Sólo se toca con el dedo (o el mouse en el editor).
        private void TickToquesBanner()
        {
            if (Time.unscaledTime < _toquesBannerDesde || Raiz?.panel == null) return;
            if (!TapSoltado(out var px)) return;
            var p = RuntimePanelUtils.ScreenToPanel(Raiz.panel, new Vector2(px.x, Screen.height - px.y));
            var op = _navBanner.OpcionDe(Raiz.panel.Pick(p));
            if (op != null) _navBanner.Activar(op);
        }

        private static bool TapSoltado(out Vector2 pos)
        {
            pos = default;
            foreach (var t in ETouch.activeTouches)
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended &&
                    (t.screenPosition - t.startScreenPosition).sqrMagnitude <= 60f * 60f)
                {
                    pos = t.screenPosition;
                    return true;
                }
#if UNITY_EDITOR
            var m = Mouse.current;
            if (m != null && m.leftButton.wasReleasedThisFrame) { pos = m.position.ReadValue(); return true; }
#endif
            return false;
        }
    }
}
