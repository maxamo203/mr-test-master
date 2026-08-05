namespace Voice
{
    // Códec IMA ADPCM 4:1 (PCM 16 bit -> 4 bits por muestra), escrito a mano para no
    // depender de plugins externos (ver "Límites / No incluido" del doc de proyecto:
    // sin plugins no auditados). Es el códec clásico de voz sobre LAN: una tabla y
    // cuatro shifts por muestra, sin allocs y sin coste de licencia.
    //
    // A 16 kHz deja la voz en ~8 KB/s por hablante, que sobre LAN es despreciable.
    //
    // Formato del frame:  [short predictor][byte índice][nibbles...]  (little endian)
    // El nibble BAJO de cada byte es la muestra par, el ALTO la impar.
    //
    // Cada frame lleva su propio predictor en la cabecera, así que el decodificador NO
    // arrastra estado entre paquetes: si la detección de voz (VAD) corta y reanuda, o
    // si un jugador entra a mitad de una frase, el audio sigue saliendo limpio.
    public static class VoiceCodec
    {
        public const int Cabecera = 3;   // bytes de estado al inicio de cada frame

        // Tamaño exacto que ocupa un frame de N muestras ya codificado.
        public static int LargoCodificado(int muestras) => Cabecera + (muestras + 1) / 2;

        private static readonly int[] Pasos =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
            50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230,
            253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963,
            1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327,
            3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442,
            11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794,
            32767,
        };

        private static readonly int[] Indices =
            { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };

        // El codificador SÍ mantiene estado: el índice de paso se adapta al volumen de
        // la voz y arrancar de cero en cada frame metería distorsión en los primeros
        // milisegundos. Como el índice viaja en la cabecera, el decodificador no
        // necesita seguir la misma secuencia de frames.
        public sealed class Codificador
        {
            private int _index;

            public int Codificar(short[] src, int cantidad, byte[] dst)
            {
                int pred = cantidad > 0 ? src[0] : 0;
                dst[0] = (byte)(pred & 0xFF);
                dst[1] = (byte)((pred >> 8) & 0xFF);
                dst[2] = (byte)_index;

                int  o         = Cabecera;
                byte pendiente = 0;

                for (int i = 0; i < cantidad; i++)
                {
                    int paso  = Pasos[_index];
                    int diff  = src[i] - pred;
                    int signo = 0;
                    if (diff < 0) { signo = 8; diff = -diff; }

                    int delta = 0, vpdiff = paso >> 3;
                    if (diff >= paso) { delta  = 4; diff -= paso; vpdiff += paso; }
                    paso >>= 1;
                    if (diff >= paso) { delta |= 2; diff -= paso; vpdiff += paso; }
                    paso >>= 1;
                    if (diff >= paso) { delta |= 1; vpdiff += paso; }

                    pred = signo != 0 ? pred - vpdiff : pred + vpdiff;
                    if (pred >  32767) pred =  32767;
                    if (pred < -32768) pred = -32768;

                    int code = delta | signo;
                    _index += Indices[code];
                    if (_index < 0)       _index = 0;
                    else if (_index > 88) _index = 88;

                    if ((i & 1) == 0) pendiente = (byte)code;
                    else              dst[o++]  = (byte)(pendiente | (code << 4));
                }

                if ((cantidad & 1) != 0) dst[o++] = pendiente;
                return o;
            }
        }

        // Decodifica un frame completo a PCM 16 bit. Devuelve cuántas muestras escribió.
        public static int Decodificar(byte[] src, int largo, short[] dst)
        {
            if (src == null || largo <= Cabecera) return 0;

            int pred  = (short)(src[0] | (src[1] << 8));
            int index = src[2];
            if (index > 88) index = 88;

            int n = (largo - Cabecera) * 2;
            if (n > dst.Length) n = dst.Length;

            for (int i = 0; i < n; i++)
            {
                byte b    = src[Cabecera + (i >> 1)];
                int  code = (i & 1) == 0 ? b & 0x0F : b >> 4;

                int paso   = Pasos[index];
                int vpdiff = paso >> 3;
                if ((code & 4) != 0) vpdiff += paso;
                if ((code & 2) != 0) vpdiff += paso >> 1;
                if ((code & 1) != 0) vpdiff += paso >> 2;

                pred = (code & 8) != 0 ? pred - vpdiff : pred + vpdiff;
                if (pred >  32767) pred =  32767;
                if (pred < -32768) pred = -32768;

                index += Indices[code];
                if (index < 0)       index = 0;
                else if (index > 88) index = 88;

                dst[i] = (short)pred;
            }

            return n;
        }
    }
}
