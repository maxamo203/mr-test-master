using System;
using System.Collections.Generic;
using Gamepad;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rediseno.UI
{
    // Lista de opciones de una pantalla de UI Toolkit con UN foco, para que la misma
    // pantalla se use con el dedo, con un gamepad, con la ruedita del VR BOX o con la mirada
    // (modo visor). Cada entrada sólo mueve el foco o activa; la pantalla no sabe cuál fue.
    //
    // VR BOX (modo mouse): ruedita ↑↓ mueve, A elige, B vuelve (prueba: con la captura de
    // puntero la A llega como clic y la B como pulso, ver VRBoxInput).
    // Gamepad: cruceta/stick ↑↓ mueve, abajo (A/✕) elige, derecha (B/○) vuelve.
    // ←/→: sólo pasan al botón de al lado en una fila horizontal (Izquierda/Derecha) y
    // ajustan los deslizadores; nunca eligen ni vuelven.
    public sealed class MenuNavegable
    {
        public sealed class Opcion
        {
            public VisualElement Elemento;
            public Action Accion;
            public bool Habilitada = true;
            // Deslizador: con el foco en esta opción, ←/→ del mando ajustan (±1 paso) en vez de
            // elegir/volver.
            public Action<int> Ajustar;
            // Sólo para el dedo o la mirada (los botones − / + de un deslizador): la
            // navegación con mando los saltea, porque para eso están ←/→ sobre la fila.
            public bool SoloPuntero;
            // Vecinos en una fila horizontal (ver FilaHorizontal): ←/→ pasan a ellos.
            public Opcion Izquierda, Derecha;
            internal Color BordeNormal;
            internal float AnchoBorde;
        }

        private readonly List<Opcion> _opciones = new();
        private int _foco = -1;

        // Qué hacer con "volver" (← de la ruedita, B/○, atrás de Android). Null = nada.
        public Action Volver;

        // Si el foco se dibuja. Con el dedo no hay "selección" persistente: sólo se muestra
        // con un mando conectado o en el visor (lo decide la pantalla cada frame).
        public bool MostrarFoco { get; set; }

        public IReadOnlyList<Opcion> Opciones => _opciones;
        public Opcion Enfocada => _foco >= 0 && _foco < _opciones.Count ? _opciones[_foco] : null;

        public Opcion Agregar(VisualElement e, Action accion, bool habilitada = true)
        {
            var o = new Opcion
            {
                Elemento = e, Accion = accion, Habilitada = habilitada,
                BordeNormal = e.style.borderTopColor.value,
                AnchoBorde = e.style.borderTopWidth.value,
            };
            _opciones.Add(o);
            if (_foco < 0 && habilitada) _foco = _opciones.Count - 1;
            return o;
        }

        // Fila de deslizador: la fila se recorre con el mando (←/→ ajustan) y los botones
        // menos/más quedan para el dedo y la mirada.
        public Opcion AgregarDeslizador(VisualElement fila, Action<int> ajustar,
                                        VisualElement menos, VisualElement mas)
        {
            var o = Agregar(fila, null);
            o.Ajustar = ajustar;
            Agregar(menos, () => ajustar(-1)).SoloPuntero = true;
            Agregar(mas, () => ajustar(+1)).SoloPuntero = true;
            return o;
        }

        // Encadena opciones que se ven una al lado de la otra, de izquierda a derecha.
        public void FilaHorizontal(params Opcion[] fila)
        {
            for (int i = 0; i < fila.Length; i++)
            {
                fila[i].Izquierda = i > 0 ? fila[i - 1] : null;
                fila[i].Derecha = i < fila.Length - 1 ? fila[i + 1] : null;
            }
        }

        public void Limpiar()
        {
            _opciones.Clear();
            _foco = -1;
        }

        // La opción a la que pertenece un elemento (o un hijo suyo) devuelto por panel.Pick.
        public Opcion OpcionDe(VisualElement e)
        {
            for (var v = e; v != null; v = v.parent)
                for (int i = 0; i < _opciones.Count; i++)
                    if (_opciones[i].Elemento == v) return _opciones[i];
            return null;
        }

        public void Enfocar(Opcion o)
        {
            int i = _opciones.IndexOf(o);
            if (i >= 0) _foco = i;
        }

        public void Activar(Opcion o)
        {
            if (o == null) return;
            Enfocar(o);
            if (!o.Habilitada) { AudioManager.Sonar(c => c.uiAlerta); return; }
            AudioManager.Sonar(c => c.uiConfirmar);
            o.Accion?.Invoke();
        }

        public void ActivarEnfocada() => Activar(Enfocada);

        public void PedirVolver()
        {
            if (Volver == null) return;
            AudioManager.Sonar(c => c.uiVolver);
            Volver();
        }

        // ↑↓: una fila horizontal cuenta como UN escalón de la pila vertical. Se saltean las
        // opciones de la fila en la que ya estás y, al entrar a una fila, se cae en su primer
        // botón (el de la izquierda), venga de arriba o de abajo.
        private void Mover(int dir)
        {
            int n = _opciones.Count;
            if (n == 0) return;
            if (_foco < 0) _foco = 0;
            var filaActual = CabezaDeFila(_opciones[_foco]);
            int i = _foco;
            for (int s = 0; s < n; s++)
            {
                i = (i + dir + n) % n;
                var o = _opciones[i];
                if (!o.Habilitada || o.SoloPuntero) continue;
                var fila = CabezaDeFila(o);
                if (fila == filaActual) continue;
                _foco = _opciones.IndexOf(fila.Habilitada ? fila : o);
                AudioManager.Sonar(c => c.uiNavegar);
                return;
            }
        }

        private static Opcion CabezaDeFila(Opcion o)
        {
            while (o.Izquierda != null) o = o.Izquierda;
            return o;
        }

        // ── Entrada de mandos (una vez por frame) ─────────────────────────────
        private readonly RepeticionNav _rep = new();
        private readonly RepeticionNav _repAjuste = new();

        // Último uso real del mando (palanca/ruedita o botón), compartido entre pantallas. La
        // mirada se apaga sólo mientras el mando se está usando: estar conectado no alcanza,
        // el VR BOX emparejado y quieto (o cualquier HID que Android registre como gamepad)
        // dejaba la selección por mirada muerta hasta que el mando se dormía.
        private const float SegundosMandoEnUso = 3f;
        private static float _ultimoUsoMando = float.NegativeInfinity;
        public static bool MandoEnUso => Time.unscaledTime - _ultimoUsoMando < SegundosMandoEnUso;

        public void TickMandos()
        {
            var gm = GamepadManager.Instance;
            if (gm == null || !gm.IsConnected) return;

            float dy, dx;
            bool elegir = false, volver = false;
            if (gm.UsesMouseInput)
            {
                // VR BOX: el eje suavizado (el crudo llega a pulsos y entre pulsos parece
                // soltado). Ver VRBoxInput.SmoothedStick.
                Vector2 d = VRBoxInput.SmoothedStick;
                dy = d.y; dx = d.x;
                elegir = VRBoxInput.APressedThisFrame;
                volver = VRBoxInput.CancelDown;
            }
            else
            {
                var gp = gm.Current;
                Vector2 v = gp.dpad.ReadValue();
                if (v.sqrMagnitude < 0.25f) v = gp.leftStick.ReadValue();
                dy = v.y; dx = v.x;
                elegir = gp.buttonSouth.wasPressedThisFrame;
                volver = gp.buttonEast.wasPressedThisFrame;
            }
            if (Mathf.Abs(dx) > 0.3f || Mathf.Abs(dy) > 0.3f || elegir || volver)
                _ultimoUsoMando = Time.unscaledTime;

            // ↑↓ con auto-repetición tipo teclado (RepeticionNav): un paso, pausa, ritmo fijo.
            int paso = _rep.Tick(Mathf.Abs(dy) >= Mathf.Abs(dx) ? dy : 0f, Time.unscaledDeltaTime);
            if (paso != 0) Mover(paso > 0 ? -1 : 1);

            // →/← de a un golpe, con el bloqueo compartido entre pantallas (BloqueoNav): hay
            // que soltar la palanca y esperar un instante antes de la próxima acción, así un
            // empuje no se arrastra a la pantalla siguiente. En la ruedita "soltar" es que
            // deje de llegar delta.
            bool libre = BloqueoNav.Libre(dx, dy);

            // Deslizador enfocado: ←/→ ajustan el valor (con repetición), no eligen ni vuelven.
            var enf = Enfocada;
            if (enf?.Ajustar != null)
            {
                int ajuste = _repAjuste.Tick(libre && Mathf.Abs(dx) > Mathf.Abs(dy) ? dx : 0f, Time.unscaledDeltaTime);
                if (ajuste != 0) { enf.Ajustar(ajuste); AudioManager.Sonar(c => c.uiNavegar); }
                if (volver) PedirVolver();
                else if (elegir) ActivarEnfocada();
                return;
            }

            if (libre && Mathf.Abs(dx) > 0.6f && Mathf.Abs(dx) > Mathf.Abs(dy))
            {
                var vecino = dx > 0f ? enf?.Derecha : enf?.Izquierda;
                if (vecino != null && vecino.Habilitada)
                {
                    BloqueoNav.TrasAccion();
                    Enfocar(vecino);
                    AudioManager.Sonar(c => c.uiNavegar);
                    return;
                }
                // Sin vecino de ese lado no hace nada: elegir es A y volver es B (antes → elegía
                // y ← volvía, y un ← de más en OPCIONES te sacaba al menú).
            }
            if (!libre) { elegir = false; volver = false; }

            if (elegir) { BloqueoNav.TrasAccion(); VRBoxInput.ConsumirA(); ActivarEnfocada(); }
            else if (volver) { BloqueoNav.TrasAccion(); PedirVolver(); }
        }

        // Pinta el foco: borde dorado sobre la opción enfocada, el borde original en el resto.
        public void PintarFoco()
        {
            for (int i = 0; i < _opciones.Count; i++)
            {
                var o = _opciones[i];
                bool f = MostrarFoco && i == _foco;
                float ancho = f ? Mathf.Max(4f, o.AnchoBorde) : o.AnchoBorde;
                var e = o.Elemento;
                e.style.borderTopWidth = ancho; e.style.borderBottomWidth = ancho;
                e.style.borderLeftWidth = ancho; e.style.borderRightWidth = ancho;
                TemaUI.BordeColor(e, f ? TemaUI.Tan : o.BordeNormal);
            }
        }
    }
}
