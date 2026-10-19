using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Gameplay
{
    // PROVISORIO (diagnóstico del retrato del asesino): guarda en memoria las últimas
    // líneas relevantes del log para copiarlas desde la pantalla de muerte (botón COPIAR
    // LOG) en un dispositivo sin consola de Xcode a mano. Relevantes = las de LocalDeath
    // más warnings, errores y excepciones (el resto, como la telemetría, es ruido).
    //
    // TIER: development build. En release no se suscribe a nada ni guarda nada.
    public static class LogMuerteDev
    {
        private const int MaxLineas = 60;
        private static readonly Queue<string> _lineas = new();

        public static bool Disponible => Debug.isDebugBuild;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Iniciar()
        {
            _lineas.Clear();
            Application.logMessageReceived -= AlLoguear;
            if (Debug.isDebugBuild) Application.logMessageReceived += AlLoguear;
        }

        private static void AlLoguear(string mensaje, string stack, LogType tipo)
        {
            bool propio = mensaje.StartsWith("[LocalDeath]");
            if (!propio && tipo == LogType.Log) return;

            var linea = $"{Time.realtimeSinceStartup:0.0}s {tipo}: {mensaje}";
            if (tipo == LogType.Exception || tipo == LogType.Error)
            {
                // Las primeras líneas del stack alcanzan para ubicar el error.
                var partes = stack.Split('\n');
                for (int i = 0; i < Mathf.Min(4, partes.Length); i++)
                    if (!string.IsNullOrWhiteSpace(partes[i])) linea += "\n    " + partes[i].Trim();
            }

            _lineas.Enqueue(linea);
            while (_lineas.Count > MaxLineas) _lineas.Dequeue();
        }

        // Copia el log al portapapeles del sistema (en iOS, también al de la Mac por el
        // portapapeles universal). Devuelve cuántas líneas copió.
        public static int Copiar()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Mortuorium log de muerte · {Application.version} · {SystemInfo.deviceModel} · " +
                          $"Cardboard {MRCardboardController.Activo}");
            foreach (var l in _lineas) sb.AppendLine(l);
            GUIUtility.systemCopyBuffer = sb.ToString();
            return _lineas.Count;
        }
    }
}
