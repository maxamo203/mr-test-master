using System;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // Grilla de noches del rediseño (opción 4A): cada noche es una tarjeta con la entidad
    // que la domina, su estado y el récord de reliquias. La usan ELEGÍ LA NOCHE (menú) y el
    // selector del fin de noche. Las bloqueadas se ven apagadas y no se eligen.
    public static class TarjetasNoche
    {
        public enum Estado { Disponible, Actual, Superada, Bloqueada, SoloCompleta }

        private static Texture2D _sorken, _arbmos, _veleth;

        // Qué entidad "domina" cada noche (decorativo): Sorken las primeras, Arbmos desde la
        // 4 (cuando aparece en el juego), Veleth la última.
        public static Texture2D Arte(int i)
        {
            if (_sorken == null) _sorken = Resources.Load<Texture2D>("Rediseno/fondo_sorken");
            if (_arbmos == null) _arbmos = Resources.Load<Texture2D>("Rediseno/fondo_arbmos");
            if (_veleth == null) _veleth = Resources.Load<Texture2D>("Rediseno/fondo_veleth");
            return i < 3 ? _sorken : i < 5 ? _arbmos : _veleth;
        }

        public static VisualElement Grilla()
        {
            var g = PiezasUI.Ignorar(new VisualElement());
            g.style.flexDirection = FlexDirection.Row;
            g.style.flexWrap = Wrap.Wrap;
            g.style.justifyContent = Justify.SpaceBetween;
            return g;
        }

        public static VisualElement Tarjeta(MenuNavegable nav, int i, string nombre, Estado estado,
                                            string detalle, Action elegir)
        {
            bool bloqueada = estado == Estado.Bloqueada || estado == Estado.SoloCompleta;
            var t = new VisualElement();
            t.style.width = Length.Percent(48.5f);
            t.style.height = 330;
            t.style.marginBottom = 22;
            TemaUI.Radio(t, 30);
            t.style.overflow = Overflow.Hidden;
            Color borde = estado == Estado.Actual ? TemaUI.Red
                        : estado == Estado.Superada ? new Color(TemaUI.Green.r, TemaUI.Green.g, TemaUI.Green.b, 0.7f)
                        : TemaUI.Linea;
            TemaUI.Borde(t, estado == Estado.Actual ? 4 : 2, borde);
            t.style.backgroundColor = TemaUI.Bg;

            var arte = PiezasUI.Ignorar(new VisualElement());
            PiezasUI.Llenar(arte);
            arte.style.backgroundImage = Arte(i);
            arte.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            arte.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top);
            arte.style.unityBackgroundImageTintColor = bloqueada
                ? new Color(0.16f, 0.15f, 0.15f, 1f) : new Color(0.6f, 0.57f, 0.55f, 1f);
            t.Add(arte);

            var sombra = PiezasUI.Ignorar(new VisualElement());
            PiezasUI.Llenar(sombra);
            sombra.style.backgroundImage = TemaUI.SombraAbajo;
            sombra.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            t.Add(sombra);

            var textos = PiezasUI.Ignorar(new VisualElement());
            textos.style.position = Position.Absolute;
            textos.style.left = 26; textos.style.right = 26; textos.style.bottom = 22;
            textos.Add(TemaUI.Texto($"NOCHE {i + 1}", TemaUI.Bebas, 56, bloqueada ? TemaUI.Dim : TemaUI.Cream, 5f));
            if (!string.IsNullOrEmpty(nombre) && !bloqueada)
            {
                var n = TemaUI.Texto(nombre.ToUpperInvariant(), TemaUI.Mono, 22, TemaUI.CreamDim, 1f);
                n.style.marginTop = 2;
                textos.Add(n);
            }
            string est = estado switch
            {
                Estado.Actual       => "Te toca",
                Estado.Superada     => "Superada",
                Estado.Bloqueada    => "Bloqueada",
                Estado.SoloCompleta => "Versión completa",
                _                   => "Disponible",
            };
            Color ce = estado switch
            {
                Estado.Actual   => new Color(0.94f, 0.63f, 0.65f, 1f),
                Estado.Superada => TemaUI.Green,
                Estado.SoloCompleta => TemaUI.Tan,
                _ => TemaUI.Muted,
            };
            var e = TemaUI.Texto(est, TemaUI.Mono, 24, ce);
            e.style.marginTop = 6;
            textos.Add(e);
            if (!string.IsNullOrEmpty(detalle))
            {
                var d = TemaUI.Texto(detalle, TemaUI.Mono, 22, TemaUI.Tan);
                d.style.marginTop = 2;
                textos.Add(d);
            }
            t.Add(textos);

            nav.Agregar(t, elegir, habilitada: !bloqueada);
            return t;
        }
    }
}
