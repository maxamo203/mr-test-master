using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gamepad
{
    // Navegación con gamepad de menús IMGUI, con UN foco GLOBAL que abarca los botones de TODOS
    // los menús visibles en el frame. Así, p. ej., en el lobby del host se navega entre el toggle
    // "Modo Cardboard" (GameBootstrapper) y "Empezar Partida" (ARLobbyUI) como una sola lista.
    //
    // Cada menú tiene un campo `ImguiGamepadMenu _nav` y dibuja sus botones con `_nav.Button(...)`
    // en su OnGUI. El estado de navegación es ESTÁTICO (compartido); GamepadManager.Update llama
    // Tick() una vez por frame. Begin/End/Update de instancia quedaron no-op (compat con el
    // call-site de los menús).
    //
    // La lista de botones se arma SÓLO en el Repaint y se reinicia con el PRIMER botón dibujado
    // en el Repaint de cada frame (comparando Time.frameCount). Antes la reiniciaba el OnGUI de
    // GamepadManager asumiendo que corría antes que los menús, pero Unity no garantiza el orden
    // de OnGUI entre scripts (ordena por GUI.depth y con empates queda indefinido): al volver al
    // menú principal el NightMenuUI nuevo podía correr primero, sus botones se borraban apenas
    // registrados y el mando quedaba muerto hasta reiniciar la app.
    //
    // Arriba/abajo recorren los botones en orden VISUAL (de arriba a abajo y, en la misma fila,
    // de izquierda a derecha), no en el orden en que se dibujaron: hay pantallas que apilan los
    // botones desde el borde inferior hacia arriba (colocar anclas, ubicar el entorno) y la
    // navegación quedaba invertida. El foco se guarda como índice de DIBUJO (es lo que conoce el
    // botón al pintarse); el orden visual sólo se usa para moverlo.
    //
    // Click de mouse/touch invoca la Action al toque; el gamepad (dpad/stick vertical + South) la
    // invoca en Tick(). El foco se resalta solo si hay mando conectado. Ítems con GUI.enabled=false
    // se saltean al navegar y no se activan. Con el menú de pausa abierto (PauseMenuController
    // tiene su propia navegación) acá no se navega ni se activa nada.
    //
    // Rediseño 2.0 (sólo con Rediseno.Flag prendido; apagado, todo queda como arriba): ruedita
    // del VR BOX suavizada con repetición tipo teclado, → elige y ← vuelve (con el bloqueo
    // compartido entre pantallas, Rediseno.BloqueoNav), B/○ del gamepad vuelve, y el foco
    // inicial de una pantalla nueva saltea el botón "volver".
    public class ImguiGamepadMenu
    {
        // ── API de instancia (los menús la usan; el estado real es estático/global) ──
        public void Begin()  { }
        public void End()    { }
        public void Update() { }

        public bool Button(string label, Action onClick, params GUILayoutOption[] options)
            => DoButtonLayout(label, onClick, options);

        public bool Button(Rect rect, string label, GUIStyle style, Action onClick)
            => DoButtonRect(rect, label, style, onClick);

        // ── Estado global compartido entre todos los menús ──
        // pos = centro del botón en coords de pantalla GUI (y hacia abajo), ya con el scroll
        // y los clips aplicados: sirve para comparar botones de distintos menús/scrollviews.
        private struct Item { public Action action; public bool enabled; public Vector2 pos; }
        private static readonly List<Item> _frame = new();   // botones del último Repaint
        private static readonly List<int>  _orden = new();   // índices de _frame en orden visual
        private static int   _ordenFrame = -1;
        private static int   _repaintFrame = -1;              // frame cuyo Repaint armó _frame
        private static int   _drawIndex;                      // índice corriente dentro del Repaint
        private static int   _focus;
        private static int   _lastCount = -1;
        private static float _navCd;
        private static readonly Rediseno.RepeticionNav _rep = new();

        // Rediseño 2.0: "volver" pedido con el mando (← de la ruedita del VR BOX, ← o B/○ del
        // gamepad). Cada pantalla vieja que tenga a dónde volver se suscribe. Sólo se dispara
        // con Rediseno.Flag prendido.
        public static event Action PedidoVolver;

        // El botón "volver" (MortuoriumTheme.BotonVolver) que se dibujó en el Repaint de este
        // frame. Si nadie está suscripto a PedidoVolver, la ← lo acciona: así toda pantalla
        // vieja con botón de volver lo hereda sin tocarla.
        private static Action _volverDelPase;
        public static void RegistrarVolver(Action volver)
        {
            SyncPass();   // por si es lo primero que se dibuja en el Repaint de este frame
            _volverDelPase = volver;
        }

        // Dos botones con centros a menos de esto (px) en vertical son de la misma fila.
        private const float ToleranciaFila = 10f;

        // Foco pedido por un menú (Enfocar / EnfocarMarcado). Se resuelve DURANTE el Repaint,
        // en el momento en que se dibuja el botón destino, así ese mismo frame ya sale bien
        // resaltado. Resolverlo en el Tick siguiente dejaba un frame con el foco viejo (la
        // flecha volver parpadeaba antes de saltar al botón correcto). Mientras el pedido está
        // pendiente en el Repaint no se resalta nada: los botones anteriores al destino no
        // deben encenderse.
        private static bool _hayPedido;
        private static bool _pedidoMarca;     // true = el destino es el próximo Marcar()
        private static int  _pedidoIndice;    // si no: primer botón habilitado desde este índice
        private static int  _pedidoFrame = -1;
        private static int  _resueltoFrame = -1;
        private static bool _marcado;         // Marcar() → el próximo botón es el destino

        private static readonly Color Highlight = new(0.95f, 0.85f, 0.20f);

        // Con la pausa abierta el foco de los menús de atrás no se muestra: la navegación es
        // de la pausa y un resaltado debajo del overlay confunde.
        private static bool ShowFocus =>
            GamepadManager.Instance != null && GamepadManager.Instance.IsConnected &&
            !PauseMenuController.IsOpen;

        private static bool EsRepaint => Event.current != null && Event.current.type == EventType.Repaint;

        private static bool PedidoEnCurso => _hayPedido && _repaintFrame >= _pedidoFrame;

        // ¿El PRÓXIMO botón que se dibuje tiene el foco del gamepad? Para menús con
        // dibujo custom (MortuoriumTheme) que pintan su propio resaltado (borde
        // dorado) en vez del tint de backgroundColor. Sólo dibuja algo en Repaint.
        // Los widgets del tema no la consultan para botones deshabilitados.
        public static bool NextHasFocus
        {
            get
            {
                SyncPass();
                if (!EsRepaint) return false;
                ResolverPedido();
                return ShowFocus && !PedidoEnCurso && _drawIndex == _focus;
            }
        }

        // Al entrar a una pantalla: foco en el primer botón habilitado con índice de dibujo
        // >= `indice` (p. ej. 1 = lo primero después de la flecha volver).
        public static void Enfocar(int indice)
        {
            _hayPedido    = true;
            _pedidoMarca  = false;
            _pedidoIndice = Mathf.Max(0, indice);
            _pedidoFrame  = Time.frameCount;
        }

        // "Siguiente paso": el foco va al botón que la pantalla marque con Marcar() justo antes
        // de dibujarlo (p. ej. CONTINUAR apenas se elige la noche). Si ese botón no llega a
        // dibujarse, el pedido se descarta y el foco queda donde estaba.
        public static void EnfocarMarcado()
        {
            _hayPedido   = true;
            _pedidoMarca = true;
            _pedidoFrame = Time.frameCount;
        }

        // Marca el PRÓXIMO botón que se dibuje como destino de EnfocarMarcado().
        public static void Marcar() => _marcado = true;

        // Al cambiar de escena la lista quedó con Actions de objetos destruidos: no deben
        // poder invocarse antes del primer Repaint de la escena nueva. Lo llama GamepadManager.
        internal static void ResetEscena()
        {
            _frame.Clear();
            _orden.Clear();
            _ordenFrame   = -1;
            _repaintFrame = -1;
            _lastCount    = -1;
            _focus        = 0;
            _hayPedido    = false;
            _marcado      = false;
        }

        // Primer botón del Repaint de este frame → arranca la lista de cero. Independiente
        // del orden en que Unity llame los OnGUI de cada script.
        private static void SyncPass()
        {
            if (!EsRepaint || Time.frameCount == _repaintFrame) return;
            _repaintFrame = Time.frameCount;
            _frame.Clear();
            _drawIndex = 0;
            _volverDelPase = null;   // lo vuelve a registrar el BotonVolver de este Repaint
        }

        private static void ResolverPedido()
        {
            if (!PedidoEnCurso) return;
            bool esDestino = _pedidoMarca ? _marcado
                                          : _drawIndex >= _pedidoIndice && GUI.enabled;
            if (!esDestino) return;
            _focus         = _drawIndex;
            _hayPedido     = false;
            _resueltoFrame = _repaintFrame;
        }

        // Para controles dibujados a mano que leen su propio puntero (p. ej. ReferenceCaptureUI,
        // que no usa GUI.Button): sólo los anota para la navegación con mando, sin un botón
        // invisible encima (un toque contaría dos veces). Devuelve si tiene el foco, para que
        // el control pinte su borde. Llamar en el mismo orden en todos los eventos.
        public static bool RegistrarSinBoton(Rect rect, Action onClick)
        {
            bool foco = NextHasFocus;
            Registrar(rect, onClick);
            return foco;
        }

        // Registra el botón recién dibujado. Sólo cuenta en Repaint: los pases de Layout / input
        // de cada script se intercalan y correrían el índice.
        private static void Registrar(Rect rect, Action onClick)
        {
            if (!EsRepaint) { _marcado = false; return; }
            ResolverPedido();   // por si el destino no consultó NextHasFocus antes
            _marcado = false;
            _frame.Add(new Item
            {
                action  = onClick,
                enabled = GUI.enabled,
                pos     = GUIUtility.GUIToScreenPoint(rect.center),
            });
            _drawIndex++;
        }

        private static bool DoButtonLayout(string label, Action onClick, GUILayoutOption[] options)
        {
            bool focused = NextHasFocus;
            var prev = GUI.backgroundColor;
            if (focused) GUI.backgroundColor = Highlight;
            bool clicked = GUILayout.Button(label, options);
            GUI.backgroundColor = prev;
            if (clicked) onClick?.Invoke();
            Registrar(EsRepaint ? GUILayoutUtility.GetLastRect() : default, onClick);
            return clicked;
        }

        private static bool DoButtonRect(Rect rect, string label, GUIStyle style, Action onClick)
        {
            bool focused = NextHasFocus;
            var prev = GUI.backgroundColor;
            if (focused) GUI.backgroundColor = Highlight;
            bool clicked = GUI.Button(rect, label, style);
            GUI.backgroundColor = prev;
            if (clicked) onClick?.Invoke();
            Registrar(rect, onClick);
            return clicked;
        }

        // Driver: 1 vez por frame (GamepadManager.Update). Navega y activa; usa la lista que quedó
        // armada en el Repaint del frame anterior.
        public static void Tick()
        {
            if (_frame.Count != _lastCount)
            {
                // Cambió el set de botones (otra pantalla): foco al primero, salvo que un
                // pedido ya lo haya ubicado en ese mismo Repaint.
                if (_resueltoFrame != _repaintFrame) _focus = PrimerFoco();
                _lastCount = _frame.Count;
            }
            // El Repaint ya pasó y el destino nunca se dibujó: se descarta el pedido.
            if (PedidoEnCurso) _hayPedido = false;
            if (_frame.Count > 0) _focus = Mathf.Clamp(_focus, 0, _frame.Count - 1);

            // La pausa tiene su propia navegación: sin esto el mismo dpad/South movía y
            // activaba a la vez la pausa y el menú de atrás (p. ej. COLOCAR ANCLA al
            // confirmar SALIR AL MENÚ).
            if (PauseMenuController.IsOpen) { _navCd = 0f; return; }

            var gm = GamepadManager.Instance;
            var gp = gm != null ? gm.Current : null;
            bool useMouse = gm != null && gm.UsesMouseInput;

            if ((gp == null && !useMouse) || _frame.Count == 0) return;

            float dy;
            float dx = 0f;
            bool confirm;
            bool volver = false;

            if (useMouse)
            {
                // VR BOX en modo mouse: delta.y del stick → navegación vertical.
                // Umbral 3 px/frame, escala 12 px/frame = eje completo.
                float rawY = VRBoxInput.Delta.y;
                dy      = Mathf.Abs(rawY) > 3f ? Mathf.Clamp(rawY / 12f, -1f, 1f) : 0f;
                confirm = VRBoxInput.ConfirmDown;
                if (Rediseno.Flag.Activo)
                {
                    // Eje suavizado: el crudo llega a pulsos y entre pulsos parece soltado, lo que
                    // hacía que mantener la ruedita recorriera todo el menú.
                    dy = VRBoxInput.SmoothedStick.y; dx = VRBoxInput.SmoothedStick.x;
                    // La A llega como toque de pantalla en los menús viejos: elige la ruedita →.
                    confirm = false;
                }
            }
            else
            {
                dy = gp.dpad.ReadValue().y;
                if (Mathf.Abs(dy) < 0.5f) dy = gp.leftStick.ReadValue().y;
                confirm = gp.buttonSouth.wasPressedThisFrame;
                if (Rediseno.Flag.Activo)
                {
                    Vector2 h = gp.dpad.ReadValue();
                    dx = Mathf.Abs(h.x) >= 0.5f ? h.x : gp.leftStick.ReadValue().x;
                    volver = gp.buttonEast.wasPressedThisFrame;
                }
            }

            if (Rediseno.Flag.Activo)
            {
                // → elige y ← vuelve, de a un golpe. Después de cada acción hay que soltar la
                // palanca y esperar un instante (BloqueoNav), así el empuje que eligió no se lee
                // otra vez en la pantalla siguiente.
                bool libre = Rediseno.BloqueoNav.Libre(dx, dy);
                if (libre && Mathf.Abs(dx) > 0.6f && Mathf.Abs(dx) > Mathf.Abs(dy))
                {
                    if (dx > 0f) confirm = true; else volver = true;
                }
                if (!libre) { confirm = false; volver = false; }

                if (volver)
                {
                    Rediseno.BloqueoNav.TrasAccion();
                    if (PedidoVolver != null) { AudioManager.Sonar(c => c.uiVolver); PedidoVolver(); return; }
                    if (_volverDelPase != null) { _volverDelPase(); return; }   // ya suena solo
                }
                if (confirm) Rediseno.BloqueoNav.TrasAccion();

                // Repetición tipo teclado: un paso, pausa, ritmo fijo.
                int paso = _rep.Tick(Mathf.Abs(dy) >= Mathf.Abs(dx) ? dy : 0f, Time.unscaledDeltaTime);
                if (paso != 0) MoveFocus(paso > 0 ? -1 : 1);
            }
            else
            {
                _navCd -= Time.unscaledDeltaTime;
                if (Mathf.Abs(dy) > 0.5f)
                {
                    if (_navCd <= 0f) { MoveFocus(dy < 0 ? 1 : -1); _navCd = 0.18f; }
                }
                else _navCd = 0f;
            }

            if (confirm && _focus >= 0 && _focus < _frame.Count && _frame[_focus].enabled)
                _frame[_focus].action?.Invoke();
        }

        // Foco inicial de una pantalla nueva. Sin el rediseño, el primer botón dibujado (como
        // siempre). Con el rediseño, el primero en pantalla que NO sea "volver": está arriba a
        // la izquierda y con el foco ahí un → de más te devolvía a la pantalla anterior.
        private static int PrimerFoco()
        {
            if (!Rediseno.Flag.Activo) return 0;
            foreach (int i in OrdenVisual())
                if (_frame[i].enabled && _frame[i].action != _volverDelPase) return i;
            return 0;
        }

        // Índices de _frame ordenados de arriba a abajo y, en la misma fila, de izq. a der.
        // Se recalcula una vez por lista (por Repaint), no en cada movimiento.
        private static List<int> OrdenVisual()
        {
            if (_ordenFrame == _repaintFrame && _orden.Count == _frame.Count) return _orden;
            _ordenFrame = _repaintFrame;
            _orden.Clear();
            for (int i = 0; i < _frame.Count; i++) _orden.Add(i);
            _orden.Sort((a, b) =>
            {
                Vector2 pa = _frame[a].pos, pb = _frame[b].pos;
                if (Mathf.Abs(pa.y - pb.y) > ToleranciaFila) return pa.y.CompareTo(pb.y);
                int cx = pa.x.CompareTo(pb.x);
                return cx != 0 ? cx : a.CompareTo(b);
            });
            return _orden;
        }

        // dir = +1 baja (siguiente en pantalla), -1 sube.
        private static void MoveFocus(int dir)
        {
            var orden = OrdenVisual();
            int n = orden.Count;
            int p = orden.IndexOf(_focus);
            if (p < 0) p = 0;
            for (int s = 0; s < n; s++)
            {
                p = (p + dir + n) % n;
                // El tic va sólo cuando el foco ATERRIZA en un ítem, no en cada vuelta del
                // bucle que saltea los deshabilitados. Corre desde Tick() (una vez por
                // frame, fuera de OnGUI) y con el cooldown de _navCd, así que no se repite.
                if (_frame[orden[p]].enabled)
                {
                    _focus = orden[p];
                    AudioManager.Sonar(c => c.uiNavegar);
                    return;
                }
            }
        }
    }
}
