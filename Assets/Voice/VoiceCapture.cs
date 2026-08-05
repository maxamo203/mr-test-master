using System;
using UnityEngine;

namespace Voice
{
    // Captura del micrófono y codificación en frames de 30 ms.
    //
    // El micrófono de Unity graba en un AudioClip CIRCULAR de 1 segundo; acá se va
    // leyendo lo nuevo desde la última posición, se remuestrea a 16 kHz (los teléfonos
    // suelen imponer 44.1/48 kHz), se mide el nivel y —si el jugador está hablando— se
    // codifica y se entrega para mandar por red.
    //
    // Detección de voz (VAD) por RMS con "cola" (hangover): el micro no transmite
    // silencio, así que en una sala de 4 jugadores el tráfico real es sólo el de quien
    // habla. Además se guarda el frame anterior y se manda al arrancar la frase, para
    // que no se coma la primera sílaba.
    public class VoiceCapture
    {
        public const int FrecuenciaRed  = 16000;   // Hz que viajan por la red
        public const int MuestrasFrame  = 480;     // 30 ms a 16 kHz

        // Segundos que se sigue transmitiendo después de que el nivel baja del umbral.
        private const float Cola = 0.45f;

        private string     _dispositivo;
        private AudioClip  _clip;
        private int        _muestrasClip;
        private int        _pos;            // última posición leída del clip circular

        private float[] _bufCaptura;        // frame crudo, a la frecuencia del device
        private short[] _pcm;               // frame remuestreado a 16 kHz
        private short[] _pcmPrev;           // el anterior (pre-roll al arrancar a hablar)
        private byte[]  _adpcm;

        private readonly VoiceCodec.Codificador _cod = new();

        private float _cola;
        private bool  _hablandoPrev;

        public bool  Activa   => _clip != null;
        public float Nivel    { get; private set; }   // RMS 0..1 del último frame (UI)
        public bool  Hablando { get; private set; }

        // Arranca el micrófono. False si el dispositivo no tiene o no lo deja abrir.
        public bool Iniciar()
        {
            if (_clip != null) return true;

            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                Debug.LogWarning("[Voz] No hay micrófono disponible.");
                return false;
            }

            _dispositivo = devices[0];

            // GetDeviceCaps devuelve (0,0) cuando el device acepta cualquier frecuencia.
            Microphone.GetDeviceCaps(_dispositivo, out int min, out int max);
            int frec = (min == 0 && max == 0) ? FrecuenciaRed : Mathf.Clamp(FrecuenciaRed, min, max);

            _clip = Microphone.Start(_dispositivo, true, 1, frec);
            if (_clip == null)
            {
                Debug.LogWarning($"[Voz] No se pudo abrir el micrófono '{_dispositivo}'.");
                return false;
            }

            // El device puede haber impuesto otra frecuencia: la que manda es la del clip.
            int frecReal    = _clip.frequency;
            _muestrasClip   = _clip.samples;
            int frameNativo = Mathf.Max(1, Mathf.RoundToInt(MuestrasFrame * (float)frecReal / FrecuenciaRed));

            _bufCaptura = new float[frameNativo];
            _pcm        = new short[MuestrasFrame];
            _pcmPrev    = new short[MuestrasFrame];
            _adpcm      = new byte[VoiceCodec.LargoCodificado(MuestrasFrame)];
            _pos        = 0;
            _cola       = 0f;
            Hablando    = false;
            _hablandoPrev = false;

            Debug.Log($"[Voz] Micrófono '{_dispositivo}' a {frecReal} Hz ({frameNativo} muestras/frame).");
            return true;
        }

        public void Detener()
        {
            if (_clip == null) return;
            Microphone.End(_dispositivo);
            UnityEngine.Object.Destroy(_clip);
            _clip     = null;
            Nivel     = 0f;
            Hablando  = false;
            _hablandoPrev = false;
        }

        // Consume todo el audio nuevo del micrófono. Por cada frame que supere el umbral
        // llama a 'enviar(buffer, largo)' — el buffer se REUTILIZA, así que el callback
        // tiene que copiarlo o mandarlo en el acto.
        public void Procesar(float umbral, Action<byte[], int> enviar)
        {
            if (_clip == null || _bufCaptura == null) return;

            int pos = Microphone.GetPosition(_dispositivo);
            if (pos < 0 || _muestrasClip <= 0) return;

            int disp = pos - _pos;
            if (disp < 0) disp += _muestrasClip;

            // Si nos atrasamos mucho (app en background, hitch de carga) saltamos al
            // presente: mandar el backlog sólo agregaría retardo a la conversación.
            if (disp > _bufCaptura.Length * 8)
            {
                _pos = pos;
                return;
            }

            while (disp >= _bufCaptura.Length)
            {
                // GetData envuelve solo si el rango pasa el final del clip circular.
                _clip.GetData(_bufCaptura, _pos);
                _pos  = (_pos + _bufCaptura.Length) % _muestrasClip;
                disp -= _bufCaptura.Length;
                ProcesarFrame(umbral, enviar);
            }
        }

        private void ProcesarFrame(float umbral, Action<byte[], int> enviar)
        {
            // Intercambia los buffers: el frame que era "actual" pasa a ser el anterior.
            var tmp = _pcmPrev; _pcmPrev = _pcm; _pcm = tmp;

            Remuestrear(_bufCaptura, _pcm);

            // RMS del frame, normalizado 0..1.
            double suma = 0d;
            for (int i = 0; i < _pcm.Length; i++)
            {
                double v = _pcm[i] * (1d / 32768d);
                suma += v * v;
            }
            Nivel = (float)System.Math.Sqrt(suma / _pcm.Length);

            float dt = (float)MuestrasFrame / FrecuenciaRed;
            if (Nivel >= umbral) _cola = Cola;
            else                 _cola -= dt;

            Hablando = _cola > 0f;
            if (!Hablando) { _hablandoPrev = false; return; }

            // Arranque de frase: mandamos primero el frame anterior para no cortar la
            // primera sílaba (el umbral siempre se cruza con la palabra ya empezada).
            if (!_hablandoPrev)
            {
                int largoPrev = _cod.Codificar(_pcmPrev, _pcmPrev.Length, _adpcm);
                enviar(_adpcm, largoPrev);
            }
            _hablandoPrev = true;

            int largo = _cod.Codificar(_pcm, _pcm.Length, _adpcm);
            enviar(_adpcm, largo);
        }

        // Remuestreo por promedio de ventana: además de bajar la frecuencia hace de
        // filtro anti-alias barato (decimar 48 kHz -> 16 kHz sin filtrar mete siseo).
        private static void Remuestrear(float[] src, short[] dst)
        {
            int n = src.Length, m = dst.Length;

            if (n == m)
            {
                for (int i = 0; i < m; i++) dst[i] = AShort(src[i]);
                return;
            }

            float paso = (float)n / m;
            for (int i = 0; i < m; i++)
            {
                int a = (int)(i * paso);
                int b = (int)((i + 1) * paso);
                if (b <= a) b = a + 1;
                if (b > n)  b = n;
                if (a >= n) a = n - 1;

                float s = 0f;
                for (int k = a; k < b; k++) s += src[k];
                dst[i] = AShort(s / (b - a));
            }
        }

        private static short AShort(float v)
        {
            float s = v * 32767f;
            if (s >  32767f) s =  32767f;
            if (s < -32768f) s = -32768f;
            return (short)s;
        }
    }
}
