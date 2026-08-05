using UnityEngine;

namespace Voice
{
    // Reproducción de la voz de UN jugador remoto.
    //
    // Los paquetes llegan por red (hilo principal) y se consumen en el hilo de audio, a
    // ritmos distintos, así que en el medio hay un buffer circular productor/consumidor:
    // el hilo de red sólo avanza _write y el de audio sólo _read (por eso no hace falta
    // lock). El AudioClip es de tipo streaming y tira del buffer vía PCMReaderCallback.
    //
    // Antes de empezar a sonar espera a juntar unos frames (jitter buffer): sin eso
    // cualquier micro-retraso de la red se escucha como un chasquido.
    //
    // La voz va en 2D (spatialBlend 0) a propósito: existe para ENTENDERSE en una sala
    // con auriculares y sonido ambiente, no para ubicar al compañero en el espacio.
    public class VoiceStream
    {
        // Potencia de 2 para poder indexar el anillo con máscara en vez de módulo.
        private const int Anillo = 16384;                        // ~1 s a 16 kHz
        private const int Cebado = VoiceCapture.MuestrasFrame * 3; // 90 ms de jitter buffer

        private readonly float[] _ring = new float[Anillo];
        private readonly short[] _pcm  = new short[1024];

        // volatile: los escribe un hilo y los lee el otro.
        private volatile int  _write;
        private volatile int  _read;
        private volatile bool _cebando = true;

        private GameObject  _go;
        private AudioSource _src;

        // Momento (unscaled) del último paquete recibido — la UI lo usa para el "está hablando".
        public float UltimoPaquete { get; private set; } = -99f;

        public VoiceStream(Transform padre, string nombre)
        {
            _go = new GameObject(nombre);
            _go.transform.SetParent(padre, false);

            _src = _go.AddComponent<AudioSource>();
            _src.spatialBlend      = 0f;      // 2D: inteligibilidad por encima de posición
            _src.loop              = true;
            _src.playOnAwake       = false;
            _src.bypassEffects     = true;    // que los filtros de terror no toquen la voz
            _src.bypassReverbZones = true;
            _src.clip = AudioClip.Create(nombre, Anillo, 1, VoiceCapture.FrecuenciaRed,
                                         true, LeerPCM);
            _src.Play();
        }

        // Volumen final aplicado a este hablante (volumen global de voz * el suyo).
        public void SetVolumen(float v)
        {
            if (_src != null) _src.volume = Mathf.Clamp01(v);
        }

        // Hilo principal: mete un frame recibido en el anillo.
        public void Escribir(byte[] adpcm, int largo)
        {
            int n = VoiceCodec.Decodificar(adpcm, largo, _pcm);
            if (n <= 0) return;

            // Si el anillo está lleno (deriva entre el reloj del micrófono del emisor y
            // el de salida de este device) descartamos el frame: preferimos un hueco de
            // 30 ms antes que pisar audio que todavía no se reprodujo.
            if (Anillo - (_write - _read) < n) return;

            int w = _write;
            for (int i = 0; i < n; i++) _ring[w++ & (Anillo - 1)] = _pcm[i] * (1f / 32768f);
            _write = w;

            UltimoPaquete = Time.unscaledTime;
        }

        // Hilo de AUDIO. Sin allocs, sin llamadas a la API de Unity.
        private void LeerPCM(float[] data)
        {
            int r = _read, w = _write;

            if (_cebando)
            {
                if (w - r < Cebado)
                {
                    System.Array.Clear(data, 0, data.Length);
                    return;
                }
                _cebando = false;
            }

            for (int i = 0; i < data.Length; i++)
            {
                if (r == w)
                {
                    // Nos quedamos sin audio: silencio y a re-cebar el jitter buffer.
                    _cebando = true;
                    while (i < data.Length) data[i++] = 0f;
                    break;
                }
                data[i] = _ring[r++ & (Anillo - 1)];
            }

            _read = r;
        }

        public void Destruir()
        {
            if (_src != null)
            {
                _src.Stop();
                if (_src.clip != null) Object.Destroy(_src.clip);
            }
            if (_go != null) Object.Destroy(_go);
            _go  = null;
            _src = null;
        }
    }
}
