using System;
using UnityEngine;

// CATALOGO UNICO de todo el audio del juego. Es un ASSET: se llena arrastrando los
// clips en el Inspector, sin tocar codigo. Cada slot (Pista) trae su propio volumen
// para calibrar en desarrollo cuando un clip queda mas fuerte que otro.
//
// Vive en Assets/Resources/AudioCatalog.asset porque el AudioManager se auto-crea sin
// wiring de escena y lo levanta con Resources.Load (mismo criterio que las fuentes de
// MortuoriumTheme y el prefab del libro ritual). Crearlo con el menu
// "Mortuorium > Crear catalogo de audio", que lo deja en la ruta y con el nombre exactos.
//
// TODOS los slots pueden quedar vacios: el AudioManager los ignora en silencio, sin
// logs. Hoy el proyecto no tiene ni un archivo de audio, asi que ese es el estado inicial.
//
// Los volumenes de MUSICA y EFECTOS que el jugador regula en Opciones NO se guardan aca
// (viven en GameOptions); aca solo esta la calibracion de mezcla que hace el desarrollador.
[CreateAssetMenu(fileName = "AudioCatalog", menuName = "Audio/Catalogo")]
public class AudioCatalog : ScriptableObject
{
    // Nombre del asset dentro de Resources/ (sin extension). No cambiarlo sin cambiar
    // tambien el Resources.Load del AudioManager y el item de menu que lo crea.
    public const string ResourceName = "AudioCatalog";

    // Un sonido del juego. Es la unidad que se arrastra y se calibra.
    [Serializable]
    public class Pista
    {
        [Tooltip("Uno o varios clips. Si hay mas de uno se elige al azar en cada disparo, " +
                 "asi el sonido no se vuelve repetitivo.")]
        public AudioClip[] clips;

        [Tooltip("Calibracion de ESTE sonido respecto de los demas. Si un clip quedo mas " +
                 "fuerte que el resto, bajalo aca.")]
        [Range(0f, 1f)] public float volumen = 1f;

        [Tooltip("Variacion aleatoria de tono en cada disparo (0 = siempre igual). Un poco " +
                 "de variacion evita que se note la repeticion en sonidos frecuentes (pasos).")]
        [Range(0f, 0.5f)] public float variacionTono = 0f;

        [Tooltip("Variacion aleatoria de intensidad por reproduccion. El volumen configurado " +
                 "es el maximo y cada disparo puede sonar hasta este porcentaje mas suave.")]
        [Range(0f, 0.5f)] public float variacionIntensidad = 0f;

        [Header("Espacial")]
        [Tooltip("0 = plano, suena igual en los dos oidos (musica, UI). " +
                 "1 = posicional, el jugador puede ubicar de donde viene.")]
        [Range(0f, 1f)] public float espacial = 1f;

        [Tooltip("Distancia (m) hasta la que suena a volumen pleno.")]
        [Min(0.1f)] public float distanciaMin = 1f;

        [Tooltip("Distancia (m) a la que ya no se oye.")]
        [Min(0.2f)] public float distanciaMax = 15f;

        [Tooltip("0 = maxima separacion entre oidos (lo mas facil de ubicar con auriculares). " +
                 "Subilo solo para ambiente envolvente que no haga falta localizar.")]
        [Range(0f, 180f)] public float apertura = 0f;

        // Sin clips utiles no hay nada que reproducir: el AudioManager corta ahi.
        public bool Vacia
        {
            get
            {
                if (clips == null) return true;
                for (int i = 0; i < clips.Length; i++)
                    if (clips[i] != null) return false;
                return true;
            }
        }

        // Elige un clip al azar entre los NO nulos (el Inspector deja huecos vacios al
        // agrandar el array). Devuelve null si no hay ninguno servible.
        public AudioClip Elegir()
        {
            if (clips == null || clips.Length == 0) return null;

            // Camino rapido y sin allocs para el caso normal (un solo clip).
            if (clips.Length == 1) return clips[0];

            int validos = 0;
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] != null) validos++;
            if (validos == 0) return null;

            int elegido = UnityEngine.Random.Range(0, validos);
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null) continue;
                if (elegido == 0) return clips[i];
                elegido--;
            }
            return null;
        }

        public int CantidadClipsValidos
        {
            get
            {
                if (clips == null) return 0;
                int cantidad = 0;
                for (int i = 0; i < clips.Length; i++)
                    if (clips[i] != null) cantidad++;
                return cantidad;
            }
        }

        // Indice compacto entre los clips no nulos. Permite que el host replique una
        // variante exacta sin depender del orden de llamadas a Random en cada cliente.
        public AudioClip ClipValido(int indice)
        {
            if (indice < 0 || clips == null) return null;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null) continue;
                if (indice-- == 0) return clips[i];
            }
            return null;
        }

        // Devuelve un indice compacto y evita repetir inmediatamente cuando hay mas de
        // una variante. El llamador conserva `anterior` por entidad/evento.
        public int ElegirIndiceSinRepetir(int anterior)
        {
            int cantidad = CantidadClipsValidos;
            if (cantidad <= 0) return -1;
            if (cantidad == 1) return 0;

            int elegido = UnityEngine.Random.Range(0, cantidad - 1);
            if (elegido >= anterior && anterior >= 0 && anterior < cantidad) elegido++;
            return elegido;
        }
    }

    // US-4.1: "ruido unico y reconocible por cada punto de entrada". Los tipos de punto
    // son MarkerType (assets en Assets/Scanner/Markers/), data-driven: se agregan sin
    // tocar codigo. Por eso esto es una LISTA por id y no campos fijos.
    //
    // La eleccion es al azar DENTRO del tipo: la puerta de la cocina solo puede sonar a
    // puerta, pero varia entre los clips de puerta.
    [Serializable]
    public class PistaPorTipo
    {
        [Tooltip("Id del MarkerType tal como figura en su asset (ej: Door, Window).")]
        public string markerTypeId = "";

        public Pista pista = new Pista();
    }

    // ─────────────────────────────────────────────────────────── MUSICA
    [Header("MUSICA  (bus de musica en Opciones)")]

    [Tooltip("Loop del menu principal.")]
    public Pista musicaMenu = new Pista { espacial = 0f, volumen = 0.7f };

    [Tooltip("Cama ambiental de fondo durante la noche. US-11.3.")]
    public Pista ambienteNoche = new Pista { espacial = 0f, volumen = 0.5f };

    [Tooltip("Capa que se superpone al ambiente y sube con la tension (cordura baja, " +
             "Arbmos presente, Sorken cerca). US-11.3: 'banda sonora acorde al nivel de peligro'.")]
    public Pista capaTension = new Pista { espacial = 0f, volumen = 0.6f };

    [Tooltip("Musica de persecucion: entra cuando una entidad esta activamente cazando.")]
    public Pista musicaPersecucion = new Pista { espacial = 0f };

    [Tooltip("Amanecer: sobreviviste la noche.")]
    public Pista victoriaAmanecer = new Pista { espacial = 0f };

    [Tooltip("Coro breve que marca una muerte local sin reemplazar ni reiniciar el fondo.")]
    public Pista gameOverStinger = new Pista { espacial = 0f, volumen = 0.85f };

    // ─────────────────────────────────────────────────────────── SORKEN
    [Header("SORKEN")]

    [Tooltip("US-4.1: un sonido por TIPO de punto de entrada. Agrega una entrada por cada " +
             "MarkerType (Door, Window, ...) con varios clips para que varie.")]
    public PistaPorTipo[] entradasPorTipo;

    [Tooltip("Fallback si el tipo del punto no esta en la lista de arriba.")]
    public Pista entradaGenerica = new Pista { distanciaMax = 20f };

    [Tooltip("Loop de pasos mientras persigue. El volumen ademas crece de forma exponencial " +
             "al acercarse (doc: 'pasos cuyo volumen aumenta exponencialmente').")]
    public Pista sorkenPasos = new Pista { variacionTono = 0.1f, distanciaMax = 12f };

    [Tooltip("Crujido real al comenzar una entrada de Sorken por puerta. No se usa para ventanas.")]
    public Pista sorkenEntradaPuertaCrujido = new Pista { distanciaMax = 20f };

    [Tooltip("Huesos rotos al desplazarse (doc: 'movimiento quebrado y erratico').")]
    public Pista sorkenHuesos = new Pista { variacionTono = 0.15f, distanciaMax = 12f };

    [Tooltip("El Sorken logro entrar al ambiente y arranca la persecucion.")]
    public Pista sorkenEntra = new Pista { distanciaMax = 20f };

    [Tooltip("Repelido con la linterna: se retira.")]
    public Pista sorkenRepelido = new Pista { distanciaMax = 20f };

    [Tooltip("US-4.4: apuntas al punto por el que esta entrando (feedback de que lo ubicaste).")]
    public Pista sorkenApuntado = new Pista { distanciaMax = 20f };

    [Tooltip("Dos reacciones alternativas al comenzar a cubrirse por primera vez.")]
    public Pista sorkenCubrirse = new Pista { distanciaMax = 20f, volumen = 0.85f };

    [Tooltip("Vocalizaciones espaciadas que acompañan solamente la marcha cubierta.")]
    public Pista sorkenMarchaCubierta = new Pista { distanciaMax = 20f, volumen = 0.72f };

    [Tooltip("Grito local de la victima cuando Sorken resuelve una captura fatal.")]
    public Pista sorkenCapturaFatal = new Pista { espacial = 0f };

    // ─────────────────────────────────────────────────── AMBIENTE DE TENSION
    [Header("AMBIENTE DE TENSION")]

    [Tooltip("Variantes independientes de golpes falsos en una puerta real del mapa.")]
    public Pista ambienteGolpesPuerta = new Pista
    {
        variacionTono = 0.025f, distanciaMin = 0.8f, distanciaMax = 18f
    };

    [Tooltip("Falsa alarma independiente: movimiento de manija en una puerta real.")]
    public Pista ambienteManijaPuerta = new Pista
    {
        variacionTono = 0.02f, distanciaMin = 0.8f, distanciaMax = 18f
    };

    // ─────────────────────────────────────────────────────────── ARBMOS
    [Header("ARBMOS  (alucinacion individual: solo la oye el jugador afectado)")]

    [Tooltip("Exhalacion intermitente y espacial de la presencia individual.")]
    public Pista arbmosExhalacion = new Pista { volumen = 0.72f, distanciaMin = 0.7f, distanciaMax = 12f };

    [Tooltip("Entrada de la resonancia al encender la luz con Arbmos presente.")]
    public Pista arbmosResonanciaEntrada = new Pista { volumen = 0.55f, distanciaMax = 12f };

    [Tooltip("Sostenido preparado para loop mientras la luz permanece encendida.")]
    public Pista arbmosResonanciaSostenida = new Pista { volumen = 0.46f, distanciaMax = 12f };

    [Tooltip("Caida natural al apagar la luz; cruza con el sostenido.")]
    public Pista arbmosResonanciaSalida = new Pista { volumen = 0.5f, distanciaMax = 12f };

    [Tooltip("Tres variantes de enfado al comprometer la persecucion normal por luz.")]
    public Pista arbmosEnfado = new Pista { volumen = 0.85f, distanciaMax = 12f };

    [Tooltip("Banco independiente: un clip por apoyo real de la persecucion.")]
    public Pista arbmosPasos = new Pista
    {
        volumen = 0.9f, variacionTono = 0.035f, variacionIntensidad = 0.18f,
        distanciaMin = 0.7f, distanciaMax = 12f
    };

    [Tooltip("Jadeo local al recibir un agarre que deja cordura restante.")]
    public Pista arbmosAgarreNoLetal = new Pista { espacial = 0f, volumen = 0.8f };

    [Tooltip("Primera onda local al morir por una captura letal de Arbmos.")]
    public Pista arbmosAgarreLetal = new Pista { espacial = 0f };

    // ─────────────────────────────────────────────────────────── VELETH
    [Header("VELETH  (invocada al perder el libro)")]

    [Tooltip("Capa organica de 8,3 s durante la emergencia efectiva del libro.")]
    public Pista velethSalidaLibro = new Pista { volumen = 0.9f, distanciaMax = 20f };

    [Tooltip("Contactos breves de apoyo, sincronizados con emergencia y locomocion.")]
    public Pista velethApoyos = new Pista
    {
        volumen = 0.88f, variacionTono = 0.035f, variacionIntensidad = 0.15f,
        distanciaMin = 0.7f, distanciaMax = 18f
    };

    [Tooltip("Roces prolongados alternados, sincronizados con el arrastre.")]
    public Pista velethRoces = new Pista
    {
        volumen = 0.72f, variacionTono = 0.025f, variacionIntensidad = 0.12f,
        distanciaMin = 0.7f, distanciaMax = 18f
    };

    [Tooltip("Capa espectral secundaria, en loop solo durante la persecucion.")]
    public Pista velethPersecucionEspectral = new Pista { volumen = 0.62f, distanciaMax = 20f };

    [Tooltip("Grito local de la victima al resolver una captura fatal de Veleth.")]
    public Pista velethCapturaFatal = new Pista { espacial = 0f };

    // ─────────────────────────────────────────────────── LIBRO RITUAL
    [Header("LIBRO RITUAL")]

    [Tooltip("Loop 3D de susurros durante TODO el consumo, incluso mientras se defiende.")]
    public Pista libroSusurrosConsumo = new Pista
    {
        volumen = 0.75f, distanciaMin = 0.6f, distanciaMax = 15f
    };

    [Tooltip("Loop mientras lo estas defendiendo con la linterna (la oscuridad retrocede).")]
    public Pista libroDefendiendo = new Pista { distanciaMax = 15f };

    [Tooltip("US-7: LO PERDISTE. Es el sonido de alerta que invoca a Veleth; " +
             "deberia ser el mas reconocible del juego.")]
    public Pista libroPerdido = new Pista { espacial = 0f };

    // ─────────────────────────────────────────────── LINTERNA Y PILAS
    [Header("LINTERNA Y PILAS  (siempre cerca del jugador)")]

    [Tooltip("Encendes la linterna.")]
    public Pista linternaOn = new Pista { espacial = 0f, volumen = 0.8f };

    [Tooltip("Apagas la linterna.")]
    public Pista linternaOff = new Pista { espacial = 0f, volumen = 0.8f };

    [Tooltip("Click en falso: intentas encenderla sin bateria.")]
    public Pista linternaVacia = new Pista { espacial = 0f, volumen = 0.8f };

    [Tooltip("Se te agoto la bateria y se apago sola.")]
    public Pista linternaSeAgota = new Pista { espacial = 0f };

    [Tooltip("Aviso de bateria baja (se dispara una sola vez al cruzar el umbral).")]
    public Pista bateriaBaja = new Pista { espacial = 0f };

    [Tooltip("Recogiste una pila.")]
    public Pista pilaRecogida = new Pista { espacial = 0f };

    [Tooltip("Aparecio una pila en el entorno.")]
    public Pista pilaAparece = new Pista { distanciaMax = 10f, volumen = 0.6f };

    // ──────────────────────────────────────────────── CORDURA Y NOCHE
    [Header("CORDURA Y NOCHE")]

    [Tooltip("Tu cordura cruzo hacia abajo el umbral de 'baja'.")]
    public Pista corduraBaja = new Pista { espacial = 0f };

    [Tooltip("Tu cordura llego a CERO: a partir de aca el Arbmos es letal.")]
    public Pista corduraCero = new Pista { espacial = 0f };

    [Tooltip("Tic de los ultimos segundos antes del amanecer.")]
    public Pista relojFinal = new Pista { espacial = 0f, volumen = 0.7f };

    [Tooltip("Desbloqueaste una noche nueva.")]
    public Pista nocheDesbloqueada = new Pista { espacial = 0f };

    // ─────────────────────────────────────────────────────────────── UI
    [Header("UI  (bus de efectos)")]

    public Pista uiNavegar  = new Pista { espacial = 0f, volumen = 0.5f };
    public Pista uiConfirmar = new Pista { espacial = 0f, volumen = 0.6f };
    public Pista uiVolver   = new Pista { espacial = 0f, volumen = 0.5f };
    public Pista uiAlerta   = new Pista { espacial = 0f };

    // Marca de "tipo no resuelto": se usa la pista generica.
    public const byte IndiceDesconocido = 255;

    // US-4.1. El tipo del punto de entrada viaja por red como UN BYTE que es el indice
    // dentro de entradasPorTipo. Se indexa contra ESTE catalogo (no contra el
    // MarkerCatalog) a proposito: el AudioCatalog esta en Resources y por lo tanto existe
    // en TODOS los peers, mientras que MarkerCatalog.Active es null en SampleScene
    // (no hay MarkerBuilder fuera del scanner). Asi el cliente resuelve el sonido sin
    // depender de datos del escaneo que no tiene.
    public byte IndiceEntrada(string markerTypeId)
    {
        if (string.IsNullOrEmpty(markerTypeId) || entradasPorTipo == null) return IndiceDesconocido;

        int tope = Mathf.Min(entradasPorTipo.Length, IndiceDesconocido);
        for (int i = 0; i < tope; i++)
        {
            var e = entradasPorTipo[i];
            if (e != null &&
                string.Equals(e.markerTypeId, markerTypeId, StringComparison.OrdinalIgnoreCase))
                return (byte)i;
        }
        return IndiceDesconocido;
    }

    // Pista del punto de entrada a partir del indice recibido. Cae en la generica si el
    // indice es desconocido, quedo fuera de rango (catalogo editado) o el slot esta vacio.
    public Pista EntradaPorIndice(byte indice)
    {
        if (indice != IndiceDesconocido && entradasPorTipo != null && indice < entradasPorTipo.Length)
        {
            var e = entradasPorTipo[indice];
            if (e != null && e.pista != null && !e.pista.Vacia) return e.pista;
        }
        return entradaGenerica;
    }

    // ¿Hay al menos un clip cargado en todo el catalogo? Si no, el AudioManager se apaga
    // entero y no gasta nada (ver "Performance & build tiers" en CLAUDE.md). Se recorre
    // una sola vez y se cachea, porque son ~35 campos por reflexion.
    [NonSerialized] private int _tieneAlgo = -1;

    public bool TieneAlgunClip()
    {
        if (_tieneAlgo >= 0) return _tieneAlgo == 1;

        bool algo = false;
        var campos = typeof(AudioCatalog).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var f in campos)
        {
            if (f.FieldType == typeof(Pista))
            {
                if (f.GetValue(this) is Pista p && !p.Vacia) { algo = true; break; }
            }
            else if (f.FieldType == typeof(PistaPorTipo[]))
            {
                if (f.GetValue(this) is PistaPorTipo[] arr)
                {
                    foreach (var e in arr)
                        if (e != null && e.pista != null && !e.pista.Vacia) { algo = true; break; }
                    if (algo) break;
                }
            }
        }

        _tieneAlgo = algo ? 1 : 0;
        return algo;
    }

#if UNITY_EDITOR
    // Calibrar consiste justamente en arrastrar clips con el juego corriendo: sin esto el
    // primer clip que agregues en Play no encenderia el sistema (quedaria cacheado el "no
    // hay nada" del arranque). Solo en el editor: en una build el asset no cambia.
    private void OnValidate() => _tieneAlgo = -1;
#endif
}
