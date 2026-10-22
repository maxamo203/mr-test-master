using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rediseno.UI
{
    // Piezas comunes de las pantallas del rediseño, ya registradas en el MenuNavegable:
    // encabezado con "volver", filas de interruptor y de deslizador, botones y cajas de
    // texto. Mismo lenguaje que el menú principal (TemaUI): esquinas redondeadas, Bebas con
    // espacio entre letras, textos de ayuda en mono.
    public static class PiezasUI
    {
        public static VisualElement Ignorar(VisualElement e) { e.pickingMode = PickingMode.Ignore; return e; }

        public static void Llenar(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.right = 0; e.style.top = 0; e.style.bottom = 0;
        }

        // Fondo de pantalla secundaria: arte oscuro, quieto, con sombra hacia abajo.
        public static VisualElement Fondo(Texture2D arte)
        {
            var f = Ignorar(new VisualElement());
            Llenar(f);
            f.style.backgroundColor = TemaUI.Bg;
            if (arte != null)
            {
                var a = Ignorar(new VisualElement());
                Llenar(a);
                a.style.backgroundImage = arte;
                a.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
                a.style.unityBackgroundImageTintColor = new Color(0.32f, 0.3f, 0.29f, 1f);
                f.Add(a);
            }
            var s = Ignorar(new VisualElement());
            Llenar(s);
            s.style.backgroundImage = TemaUI.SombraAbajo;
            s.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            f.Add(s);
            return f;
        }

        // Columna principal con márgenes de pantalla. Los de arriba/abajo los fija PantallaUI
        // (MargenesSeguros) según la zona segura del celular: notch / barra de estado arriba,
        // barra de gestos abajo.
        public const string ClaseColumna = "rediseno-columna";
        public const float AireArriba = 56f, AireAbajo = 48f;
        // Arriba: justo debajo de lo que tape el celular (notch / barra de estado) con este
        // margen, pero nunca más arriba que AireArriba.
        public const float MargenBajoBarra = 12f;
        public static float Arriba(float tapadoArriba) => Mathf.Max(AireArriba, tapadoArriba + MargenBajoBarra);

        public static VisualElement Columna()
        {
            var c = Ignorar(new VisualElement());
            Llenar(c);
            c.AddToClassList(ClaseColumna);
            c.style.flexDirection = FlexDirection.Column;
            c.style.paddingLeft = 48; c.style.paddingRight = 48;
            // Ya nace con la zona segura (pantallas que rearman su columna después de aparecer,
            // como las hojas del tutorial); PantallaUI la corrige al entrar o salir del visor.
            bool visor = ModoVisor.Activo || MRCardboardController.Activo;
            float k = Screen.width > 0 ? 1080f / Screen.width : 1f;
            var seguro = Screen.safeArea;
            c.style.paddingTop = Arriba(visor ? 0f : Mathf.Max(0f, Screen.height - seguro.yMax) * k);
            c.style.paddingBottom = AireAbajo + (visor ? 0f : Mathf.Max(0f, seguro.yMin) * k);
            return c;
        }

        // Encabezado: botón "volver" redondo + título. El botón se registra AL FINAL por la
        // pantalla (ver Registrar...) para que el foco inicial caiga en el contenido.
        public static VisualElement Encabezado(string titulo, out VisualElement botonVolver)
        {
            var fila = Ignorar(new VisualElement());
            fila.style.flexDirection = FlexDirection.Row;
            fila.style.alignItems = Align.Center;
            fila.style.marginBottom = 34;

            botonVolver = new VisualElement();
            botonVolver.style.width = 96; botonVolver.style.height = 96;
            TemaUI.Radio(botonVolver, 48);
            TemaUI.Borde(botonVolver, 2, new Color(1f, 1f, 1f, 0.25f));
            botonVolver.style.backgroundColor = new Color(0.024f, 0.02f, 0.016f, 0.45f);
            botonVolver.style.alignItems = Align.Center;
            botonVolver.style.justifyContent = Justify.Center;
            botonVolver.style.marginRight = 30;
            botonVolver.style.flexShrink = 0;
            botonVolver.Add(TemaUI.Texto("<", TemaUI.Bebas, 58, TemaUI.Cream, 0f, TextAnchor.MiddleCenter));
            fila.Add(botonVolver);

            fila.Add(TemaUI.Texto(titulo, TemaUI.Bebas, 84, TemaUI.Cream, 8f));
            return fila;
        }

        // Lista desplazable para el contenido de la pantalla.
        public static ScrollView Lista()
        {
            var sv = new ScrollView(ScrollViewMode.Vertical);
            sv.style.flexGrow = 1;
            sv.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            // Clamped: no se pasa del principio ni del final. Con Elastic se podía seguir
            // arrastrando más allá del borde y, en el visor, la lista quedaba corrida.
            sv.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            // Lo que normalmente pone el tema por defecto (acá no hay tema): el visor de la
            // lista ocupa el lugar y recorta, y el contenido no se aplasta.
            sv.contentViewport.style.flexGrow = 1;
            sv.contentViewport.style.overflow = Overflow.Hidden;
            sv.contentContainer.style.flexShrink = 0;
            return sv;
        }

        public static VisualElement Tarjeta()
        {
            var t = new VisualElement();
            TemaUI.Radio(t, 30);
            TemaUI.Relleno(t, 26, 32);
            TemaUI.Borde(t, 2, TemaUI.Linea);
            t.style.backgroundColor = TemaUI.Panel;
            t.style.marginBottom = 18;
            return t;
        }

        public static Label Seccion(string texto)
        {
            var l = TemaUI.Texto(texto, TemaUI.Mono, 26, TemaUI.Red, 4f);
            l.style.marginTop = 22; l.style.marginBottom = 12;
            return l;
        }

        // ── Interruptor ──────────────────────────────────────────────────────
        public static VisualElement Interruptor(MenuNavegable nav, string titulo, string sub,
                                                Func<bool> leer, Action<bool> escribir)
        {
            var t = Tarjeta();
            t.style.flexDirection = FlexDirection.Row;
            t.style.alignItems = Align.Center;

            var textos = Ignorar(new VisualElement());
            textos.style.flexGrow = 1; textos.style.flexShrink = 1;
            textos.style.marginRight = 24;
            textos.Add(TemaUI.Texto(titulo, TemaUI.Bebas, 44, TemaUI.Cream, 4f));
            if (!string.IsNullOrEmpty(sub))
            {
                var s = TemaUI.Texto(sub, TemaUI.Mono, 24, TemaUI.Muted);
                s.style.marginTop = 4;
                textos.Add(s);
            }
            t.Add(textos);

            var pastilla = Ignorar(new VisualElement());
            pastilla.style.width = 128; pastilla.style.height = 62;
            TemaUI.Radio(pastilla, 31);
            pastilla.style.alignItems = Align.Center;
            pastilla.style.justifyContent = Justify.Center;
            pastilla.style.flexShrink = 0;
            var txt = TemaUI.Texto("", TemaUI.Mono, 28, TemaUI.Dim, 2f, TextAnchor.MiddleCenter);
            pastilla.Add(txt);
            t.Add(pastilla);

            void Pintar()
            {
                bool on = leer();
                txt.text = on ? "ON" : "OFF";
                txt.style.color = on ? TemaUI.Tan : TemaUI.Dim;
                TemaUI.Borde(pastilla, 2, on ? TemaUI.Tan : TemaUI.Dim);
                pastilla.style.backgroundColor = on
                    ? new Color(TemaUI.Tan.r, TemaUI.Tan.g, TemaUI.Tan.b, 0.14f) : Color.clear;
            }
            Pintar();
            // El valor también puede cambiar desde afuera (p. ej. el visor se prende desde el
            // menú principal, y la pantalla ya estaba armada de antes): repintar seguido. El
            // scheduler de UI Toolkit no corre mientras el panel está oculto.
            t.schedule.Execute(Pintar).Every(250);
            nav.Agregar(t, () => { escribir(!leer()); Pintar(); });
            return t;
        }

        // ── Deslizador 0..1 ──────────────────────────────────────────────────
        // Con mando: ←/→ sobre la fila (pasos de 'paso'). Con el dedo o la mirada: − y +.
        public static VisualElement Deslizador(MenuNavegable nav, string titulo,
                                               Func<float> leer, Action<float> escribir, float paso = 0.1f)
        {
            var t = Tarjeta();

            var arriba = Ignorar(new VisualElement());
            arriba.style.flexDirection = FlexDirection.Row;
            arriba.style.justifyContent = Justify.SpaceBetween;
            arriba.style.alignItems = Align.Center;
            arriba.style.marginBottom = 16;
            arriba.Add(TemaUI.Texto(titulo, TemaUI.Bebas, 44, TemaUI.Cream, 4f));
            var valor = TemaUI.Texto("", TemaUI.Mono, 30, TemaUI.Tan, 0f, TextAnchor.MiddleRight);
            arriba.Add(valor);
            t.Add(arriba);

            var abajo = Ignorar(new VisualElement());
            abajo.style.flexDirection = FlexDirection.Row;
            abajo.style.alignItems = Align.Center;

            var menos = BotonChico("-");
            var mas = BotonChico("+");
            var pista = Ignorar(new VisualElement());
            pista.style.flexGrow = 1;
            pista.style.height = 16;
            pista.style.marginLeft = 26; pista.style.marginRight = 26;
            TemaUI.Radio(pista, 8);
            pista.style.backgroundColor = new Color(1f, 1f, 1f, 0.10f);
            var relleno = Ignorar(new VisualElement());
            relleno.style.height = Length.Percent(100);
            TemaUI.Radio(relleno, 8);
            relleno.style.backgroundColor = TemaUI.Tan;
            pista.Add(relleno);

            abajo.Add(menos);
            abajo.Add(pista);
            abajo.Add(mas);
            t.Add(abajo);

            void Pintar()
            {
                float v = Mathf.Clamp01(leer());
                valor.text = $"{Mathf.RoundToInt(v * 100f)}%";
                relleno.style.width = Length.Percent(v * 100f);
            }
            Pintar();
            nav.AgregarDeslizador(t, dir => { escribir(Mathf.Clamp01(leer() + dir * paso)); Pintar(); }, menos, mas);
            return t;
        }

        private static VisualElement BotonChico(string simbolo)
        {
            var b = new VisualElement();
            b.style.width = 88; b.style.height = 88;
            TemaUI.Radio(b, 44);
            TemaUI.Borde(b, 2, TemaUI.Linea);
            b.style.backgroundColor = new Color(1f, 1f, 1f, 0.04f);
            b.style.alignItems = Align.Center;
            b.style.justifyContent = Justify.Center;
            b.style.flexShrink = 0;
            b.Add(TemaUI.Texto(simbolo, TemaUI.Bebas, 56, TemaUI.Cream, 0f, TextAnchor.MiddleCenter));
            return b;
        }

        // ── Botón y textos ───────────────────────────────────────────────────
        public static VisualElement Boton(MenuNavegable nav, string texto, Action accion,
                                          bool peligro = false, bool habilitado = true)
        {
            var b = Tarjeta();
            b.style.alignItems = Align.Center;
            b.style.paddingTop = 30; b.style.paddingBottom = 30;
            if (!habilitado) b.style.opacity = 0.45f;
            b.Add(TemaUI.Texto(texto, TemaUI.Bebas, 46, peligro ? TemaUI.Red : TemaUI.Cream, 5f, TextAnchor.MiddleCenter));
            nav.Agregar(b, accion, habilitado);
            return b;
        }

        public static Label Info(VisualElement padre, string texto)
        {
            var t = Ignorar(Tarjeta());
            var l = TemaUI.Texto(texto, TemaUI.Mono, 28, TemaUI.CreamDim);
            t.Add(l);
            padre.Add(t);
            return l;
        }
    }
}
