using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using T = MortuoriumTheme;

namespace Gamepad
{
    // Bloque de diagnóstico del mando para los modales de Control (pausa y menú principal).
    // Responde "¿por qué a veces no anda la B?": en qué modo quedó detectado el mando, qué
    // dispositivos ve Unity, cuántas pulsaciones de B llegaron, por qué vía, cuánto duró la
    // última y cómo la interpretó la linterna (toque / mantener).
    //
    // En development build suma un log en vivo de CADA botón que cambia en cualquier
    // dispositivo (InputSystem.onEvent) + toques de pantalla + conexiones: sirve para ver qué
    // manda el VR BOX en cada modo (@+A/B/C/D) sin adb. El hook sólo existe mientras el modal
    // se está dibujando (se suelta solo a los ~30 frames sin Draw), así que no cuesta nada en
    // partida.
    public static class ControllerDiagnostics
    {
        private const int MaxEventos = 8;
        private struct Evento { public float t; public string texto; }
        private static readonly List<Evento> _eventos = new();

        private static int  _ultimoDraw = -1000;
        private static bool _enganchado;
        private static readonly StringBuilder _sb = new();
        private static float _proximaConsulta;

        // Alto que ocupa Draw con el log incluido / sin él, para que los callers reserven lugar.
        public static float Alto => Debug.isDebugBuild ? 450f : 220f;

        internal static void Tick()
        {
            bool visible = Time.frameCount - _ultimoDraw < 30;
            if (_enganchado && !visible) Soltar();
            if (!_enganchado) return;

            // Toques de pantalla: el Touchscreen manda TouchState (no su formato de estado), así
            // que onEvent no sirve para leerlos; EnhancedTouch ya los tiene resueltos.
            foreach (var t in ETouch.activeTouches)
            {
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Began)
                    Agregar($"Pantalla · toque #{t.touchId} apretó ({t.screenPosition.x:0},{t.screenPosition.y:0})");
                else if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                         t.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                    Agregar($"Pantalla · toque #{t.touchId} soltó");
            }
        }

        public static void Draw(Rect area)
        {
            // Recién abierto: arrancar de cero para que los números sean de esta prueba.
            if (Time.frameCount - _ultimoDraw >= 30)
            {
                VRBoxInput.ReiniciarEstadisticas();
                _eventos.Clear();
            }
            _ultimoDraw = Time.frameCount;
            if (Debug.isDebugBuild && !_enganchado) Enganchar();

            var gm = GamepadManager.Instance;
            var lbl  = T.Estilo(T.FMono, 13, T.CreamDim);
            var dim  = T.Estilo(T.FMono, 12, T.Muted);
            var head = T.Estilo(T.FBebas, 20, T.Tan);
            float x = area.x, w = area.width, y = area.y;
            void Linea(string s, GUIStyle st, float h = 20f) { GUI.Label(new Rect(x, y, w, h), s, st); y += h; }

            Linea("DIAGNÓSTICO", head, 26f);

            string modo = gm == null || !gm.IsConnected ? "sin mando"
                        : gm.UsesMouseInput ? "VR BOX · modo mouse"
                        : "gamepad";
            Linea($"Modo: {modo}   ·   cambios de mando: {(gm != null ? gm.CambiosDeMando : 0)}", lbl);

            _sb.Clear();
            foreach (var d in InputSystem.devices)
            {
                if (d is Sensor) continue;   // acelerómetro & cía: ruido para esto
                if (_sb.Length > 0) _sb.Append(", ");
                _sb.Append(d.name);
            }
            GUI.Label(new Rect(x, y, w, 36f), "Dispositivos: " + _sb, dim); y += 38f;

            bool b = VRBoxInput.BVisible;
            Linea($"Botón B (prende/apaga): {(b ? "PULSO" : "-")}",
                  T.Estilo(T.FMono, 13, b ? T.Tan : T.CreamDim));
            Linea($"  pulsaciones {VRBoxInput.BPulsaciones} · flancos {VRBoxInput.BFlancos} · ignoradas {VRBoxInput.BIgnoradas}", lbl);
            Linea(VRBoxInput.BPulsaciones > 0 ? $"  vía {VRBoxInput.BFuente}" : "", lbl);

            string palanca = VRBoxInput.PalancaArriba ? "ARRIBA = luz intensa"
                           : VRBoxInput.PalancaAbajo  ? "ABAJO = apagada"
                           : "centro = normal";
            bool activa = VRBoxInput.PalancaArriba || VRBoxInput.PalancaAbajo;
            Linea($"Palanca: {palanca}", T.Estilo(T.FMono, 13, activa ? T.Tan : T.CreamDim));
            // Capturado = sin cursor del SO, la palanca no tiene bordes (sólo en partida).
            if (gm != null && Time.unscaledTime >= _proximaConsulta)
            {
                _proximaConsulta = Time.unscaledTime + 0.5f;
                gm.ConsultarCapturaAndroid();
            }
            string captura = gm == null || !gm.UsesMouseInput ? "no (sin modo mouse)"
                           : gm.PunteroCapturado ? "sí"
                           : gm.QuiereCaptura ? "NO: se pidió y Android no la dio"
                           : "no (sólo se captura en partida)";
            Linea($"  puntero capturado: {captura}", lbl);
            y += 4f;

            Linea(Pista(gm), T.Estilo(T.FMono, 12, T.Tan), 34f);

            if (!Debug.isDebugBuild) return;

            y += 6f;
            Linea("EVENTOS (todos los botones, en vivo)", head, 26f);
            if (_eventos.Count == 0) Linea("Apretá cualquier botón del mando…", dim);
            float ahora = Time.unscaledTime;
            for (int i = _eventos.Count - 1; i >= 0; i--)
                Linea($"-{ahora - _eventos[i].t,4:0.0}s  {_eventos[i].texto}", dim);
        }

        // Una línea que dice qué hacer con lo que se ve arriba.
        private static string Pista(GamepadManager gm)
        {
            if (gm == null || !gm.IsConnected)
                return "Unity no ve ningún gamepad. Revisá que el mando esté emparejado\ny en modo mouse (@+D).";
            if (!gm.UsesMouseInput)
                return "El mando no está en modo mouse: la linterna usa los botones\ndel gamepad. Para la B del VR BOX, poné @+D.";
            if (VRBoxInput.BIgnoradas > 0)
                return "Llegaron B con el mando sin detectar en modo mouse: se ignoraron.";
            if (VRBoxInput.BPulsaciones == 0)
                return "Apretá la B: tiene que sumar pulsaciones. Si no suma, no llega a Unity.";
            if (VRBoxInput.BFuente.Contains("escape"))
                return "La B llegó como la tecla \"atrás\" de Android (escape).";
            return "La B llega. Probá también mantener la palanca arriba 5 s:\ntiene que quedar en ARRIBA todo el tiempo.";
        }

        private static void Enganchar()
        {
            InputSystem.onEvent += OnEvento;
            InputSystem.onDeviceChange += OnCambioDispositivo;
            _enganchado = true;
        }

        private static void Soltar()
        {
            InputSystem.onEvent -= OnEvento;
            InputSystem.onDeviceChange -= OnCambioDispositivo;
            _enganchado = false;
            _eventos.Clear();
        }

        private static void OnCambioDispositivo(InputDevice d, InputDeviceChange c)
        {
            if (d is Sensor) return;
            Agregar($"{d.name} ({d.layout}) {c}");
        }

        // Se llama ANTES de aplicar el evento al dispositivo: el valor actual del control es
        // el previo y el del evento es el nuevo. Sólo se loguean cambios de apretado/suelto.
        private static void OnEvento(InputEventPtr ev, InputDevice device)
        {
            if (device is Touchscreen || device is Sensor) return;
            if (!ev.IsA<StateEvent>() && !ev.IsA<DeltaStateEvent>()) return;

            foreach (var c in ev.EnumerateChangedControls(device))
            {
                if (!(c is ButtonControl btn) || btn.synthetic) continue;
                if (!btn.ReadValueFromEvent(ev, out float nuevo)) continue;
                bool ahora = btn.IsValueConsideredPressed(nuevo);
                bool antes = btn.IsValueConsideredPressed(btn.ReadValue());
                if (ahora == antes) continue;
                Agregar($"{device.name} · {btn.name} {(ahora ? "apretó" : "soltó")}");
            }
        }

        private static void Agregar(string texto)
        {
            _eventos.Add(new Evento { t = Time.unscaledTime, texto = texto });
            if (_eventos.Count > MaxEventos) _eventos.RemoveAt(0);
        }
    }
}
