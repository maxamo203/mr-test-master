using System.Collections.Generic;
using System.Text;
using Gamepad;
using Rediseno.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Elemento = Gamepad.PauseMenuController.ElementoPausa;
using Tipo = Gamepad.PauseMenuController.TipoElementoPausa;

namespace Rediseno.Menus
{
    // Menú de pausa del Rediseño 2.0 (UI Toolkit). No duplica las páginas: lee el MODELO
    // que arma PauseMenuController (títulos, textos, botones, interruptores, deslizadores,
    // con sus ids) y lo dibuja con el estilo nuevo; elegir algo llama a la misma acción de
    // la pausa vieja. Así todas sus páginas (audio, Cardboard, calidad AR, voz, las de
    // desarrollo…) salen migradas de una vez, y cualquier opción nueva aparece sola.
    //
    // En Cardboard (MRCardboardController) se dibuja una vez por ojo, como una pantalla
    // quieta en el cuarto delante tuyo, con la cámara visible detrás; se navega con el
    // joystick (↑↓, → elige, ← vuelve; ←/→ ajustan los deslizadores) o con la mirada.
    // CONTROL (el mando virtual) todavía es la página vieja: con el visor queda deshabilitada.
    public class MenuPausa2 : PantallaUI
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instancia != null) return;
            var go = new GameObject("MenuPausa2");
            DontDestroyOnLoad(go);
            go.AddComponent<MenuPausa2>();
        }

        private static MenuPausa2 _instancia;
        public static bool Mostrando => _instancia != null && _instancia.EsVisible();

        // Ids de páginas que todavía son viejas: no se pueden usar con el visor puesto.
        private static readonly HashSet<string> SinVisor = new() { "control" };

        private VisualElement _velo, _tarjeta;
        private Label _titulo;
        private ScrollView _lista;
        private string _firma;

        // Vínculo elemento del modelo → piezas a refrescar cada frame.
        private struct Vinculo
        {
            public Label Texto, Valor;
            public VisualElement Relleno, Pastilla, Marca;
            public Label PastillaTxt;
        }
        private readonly List<Vinculo> _vinculos = new();
        private readonly StringBuilder _sb = new();

        protected override void Awake()
        {
            if (_instancia != null && _instancia != this) { Destroy(gameObject); return; }
            _instancia = this;
            base.Awake();
        }

        protected override void OnDestroy()
        {
            if (_instancia == this) _instancia = null;
            base.OnDestroy();
        }

        protected override bool EsVisible()
        {
            var p = PauseMenuController.Instance;
            return p != null && PauseMenuController.IsOpen && p.PaginaMigrable &&
                   SceneManager.GetActiveScene().name != SceneFlow.EscenaMenu;
        }

        protected override bool EnVisor => ModoVisor.Activo || MRCardboardController.Activo;
        protected override bool CabezaPorCamara => MRCardboardController.Activo;
        protected override Color VeloVisor =>
            MRCardboardController.Activo ? new Color(0.02f, 0.016f, 0.012f, 0.62f) : Color.black;

        protected override void AlCambiarVisor(bool visor)
        {
            // En el visor el velo lo pone la composición estéreo; en pantalla, el árbol.
            if (_velo != null) _velo.style.display = visor ? DisplayStyle.None : DisplayStyle.Flex;
            if (_tarjeta != null)
            {
                _tarjeta.style.height = visor ? Length.Percent(100) : Length.Percent(80);
                _tarjeta.style.width = visor ? Length.Percent(100) : Length.Percent(90);
            }
            _firma = null;   // reconstruir: cambian los habilitados (CONTROL)
        }

        protected override void Construir(VisualElement raiz, MenuNavegable nav)
        {
            raiz.style.alignItems = Align.Center;
            raiz.style.justifyContent = Justify.Center;

            _velo = PiezasUI.Ignorar(new VisualElement());
            PiezasUI.Llenar(_velo);
            _velo.style.backgroundColor = new Color(TemaUI.Bg.r, TemaUI.Bg.g, TemaUI.Bg.b, 0.82f);
            raiz.Add(_velo);

            _tarjeta = PiezasUI.Ignorar(new VisualElement());
            // Alto FIJO: la lista desplazable no aporta alto propio, así que con un alto
            // "según contenido" el área visible quedaba de un solo ítem (se veía sólo
            // REANUDAR). La lista ocupa todo lo que queda debajo del título.
            _tarjeta.style.width = Length.Percent(90);
            _tarjeta.style.height = Length.Percent(80);
            TemaUI.Radio(_tarjeta, 44);
            TemaUI.Borde(_tarjeta, 2, TemaUI.Linea);
            _tarjeta.style.backgroundColor = new Color(0.078f, 0.067f, 0.063f, 0.97f);
            TemaUI.Relleno(_tarjeta, 44, 40);
            raiz.Add(_tarjeta);

            _titulo = TemaUI.Texto("PAUSA", TemaUI.Bebas, 84, TemaUI.Cream, 8f);
            _titulo.style.marginBottom = 26;
            _tarjeta.Add(_titulo);

            _lista = PiezasUI.Lista();
            _lista.style.flexGrow = 1;
            _lista.style.flexShrink = 1;
            Desplazable = _lista;
            _tarjeta.Add(_lista);

            nav.Volver = () => PauseMenuController.Instance?.Volver();
        }

        protected override void Animar(float dt)
        {
            var modelo = PauseMenuController.Modelo;
            string firma = Firma(modelo);
            if (firma != _firma)
            {
                _firma = firma;
                Reconstruir(modelo);
            }
            else Refrescar(modelo);
        }

        // Estructura de la página (sin los valores): si cambia, se rearma el árbol.
        private string Firma(IReadOnlyList<Elemento> modelo)
        {
            _sb.Clear();
            _sb.Append(PauseMenuController.Instance != null ? PauseMenuController.Instance.Pagina : "");
            _sb.Append(EnVisor ? "|v" : "|p");
            for (int i = 0; i < modelo.Count; i++)
                _sb.Append('|').Append((int)modelo[i].Tipo).Append(':').Append(modelo[i].Id);
            return _sb.ToString();
        }

        private void Reconstruir(IReadOnlyList<Elemento> modelo)
        {
            _lista.Clear();
            _lista.scrollOffset = Vector2.zero;   // página nueva: desde arriba
            Nav.Limpiar();
            _vinculos.Clear();
            var pausa = PauseMenuController.Instance;

            for (int i = 0; i < modelo.Count; i++)
            {
                var m = modelo[i];
                var v = new Vinculo();
                switch (m.Tipo)
                {
                    case Tipo.Titulo:
                        _titulo.text = Mayus(m.Texto);
                        break;

                    case Tipo.Texto:
                    {
                        var l = TemaUI.Texto(m.Texto, TemaUI.Mono, 26, TemaUI.Muted);
                        l.style.marginBottom = 16; l.style.marginLeft = 8; l.style.marginRight = 8;
                        _lista.Add(l);
                        v.Texto = l;
                        break;
                    }

                    case Tipo.Boton:
                    {
                        string id = m.Id;
                        bool hab = !(EnVisor && SinVisor.Contains(id));
                        bool peligro = id == "salir";
                        var b = PiezasUI.Tarjeta();
                        b.style.alignItems = Align.Center;
                        b.style.paddingTop = 28; b.style.paddingBottom = 28;
                        if (!hab) b.style.opacity = 0.4f;
                        var l = TemaUI.Texto(Mayus(m.Texto), TemaUI.Bebas, 46,
                                             peligro ? TemaUI.Red : TemaUI.Cream, 5f, TextAnchor.MiddleCenter);
                        b.Add(l);
                        if (!hab)
                        {
                            var nota = TemaUI.Texto("No disponible con el visor", TemaUI.Mono, 22, TemaUI.Dim, 0f, TextAnchor.MiddleCenter);
                            nota.style.marginTop = 4;
                            b.Add(nota);
                        }
                        _lista.Add(b);
                        Nav.Agregar(b, () => pausa?.Accionar(id), hab);
                        v.Texto = l;
                        break;
                    }

                    case Tipo.Interruptor:
                    {
                        string id = m.Id;
                        var t = PiezasUI.Tarjeta();
                        t.style.flexDirection = FlexDirection.Row;
                        t.style.alignItems = Align.Center;
                        var l = TemaUI.Texto(Mayus(m.Texto), TemaUI.Bebas, 42, TemaUI.Cream, 4f);
                        l.style.flexGrow = 1; l.style.flexShrink = 1; l.style.marginRight = 20;
                        t.Add(l);
                        var pas = PiezasUI.Ignorar(new VisualElement());
                        pas.style.width = 128; pas.style.height = 62;
                        TemaUI.Radio(pas, 31);
                        pas.style.alignItems = Align.Center; pas.style.justifyContent = Justify.Center;
                        pas.style.flexShrink = 0;
                        var pt = TemaUI.Texto("", TemaUI.Mono, 28, TemaUI.Dim, 2f, TextAnchor.MiddleCenter);
                        pas.Add(pt);
                        t.Add(pas);
                        _lista.Add(t);
                        Nav.Agregar(t, () => pausa?.Accionar(id));
                        v.Texto = l; v.Pastilla = pas; v.PastillaTxt = pt;
                        break;
                    }

                    case Tipo.Deslizador:
                    {
                        string id = m.Id;
                        var t = PiezasUI.Tarjeta();
                        var arriba = PiezasUI.Ignorar(new VisualElement());
                        arriba.style.flexDirection = FlexDirection.Row;
                        arriba.style.justifyContent = Justify.SpaceBetween;
                        arriba.style.alignItems = Align.Center;
                        arriba.style.marginBottom = 16;
                        var l = TemaUI.Texto(Mayus(m.Texto), TemaUI.Bebas, 42, TemaUI.Cream, 4f);
                        l.style.flexShrink = 1;
                        arriba.Add(l);
                        var val = TemaUI.Texto("", TemaUI.Mono, 30, TemaUI.Tan, 0f, TextAnchor.MiddleRight);
                        arriba.Add(val);
                        t.Add(arriba);

                        var abajo = PiezasUI.Ignorar(new VisualElement());
                        abajo.style.flexDirection = FlexDirection.Row;
                        abajo.style.alignItems = Align.Center;
                        var menos = BotonChico("-");
                        var mas = BotonChico("+");
                        var pista = PiezasUI.Ignorar(new VisualElement());
                        pista.style.flexGrow = 1; pista.style.height = 16;
                        pista.style.marginLeft = 26; pista.style.marginRight = 26;
                        TemaUI.Radio(pista, 8);
                        pista.style.backgroundColor = new Color(1f, 1f, 1f, 0.10f);
                        var rel = PiezasUI.Ignorar(new VisualElement());
                        rel.style.height = Length.Percent(100);
                        TemaUI.Radio(rel, 8);
                        rel.style.backgroundColor = TemaUI.Tan;
                        pista.Add(rel);
                        abajo.Add(menos); abajo.Add(pista); abajo.Add(mas);
                        t.Add(abajo);
                        _lista.Add(t);
                        Nav.AgregarDeslizador(t, dir => pausa?.Ajustar(id, dir), menos, mas);
                        v.Texto = l; v.Valor = val; v.Relleno = rel;
                        break;
                    }

                    case Tipo.Medidor:
                    {
                        // Sólo lectura (no entra en la navegación): barra en vivo + marca roja.
                        var t = PiezasUI.Ignorar(PiezasUI.Tarjeta());
                        var arriba = PiezasUI.Ignorar(new VisualElement());
                        arriba.style.flexDirection = FlexDirection.Row;
                        arriba.style.justifyContent = Justify.SpaceBetween;
                        arriba.style.alignItems = Align.Center;
                        arriba.style.marginBottom = 16;
                        var l = TemaUI.Texto(Mayus(m.Texto), TemaUI.Bebas, 42, TemaUI.Cream, 4f);
                        l.style.flexShrink = 1;
                        arriba.Add(l);
                        var val = TemaUI.Texto("", TemaUI.Mono, 26, TemaUI.Dim, 0f, TextAnchor.MiddleRight);
                        arriba.Add(val);
                        t.Add(arriba);

                        var pista = PiezasUI.Ignorar(new VisualElement());
                        pista.style.height = 16;
                        TemaUI.Radio(pista, 8);
                        pista.style.backgroundColor = new Color(1f, 1f, 1f, 0.10f);
                        var rel = PiezasUI.Ignorar(new VisualElement());
                        rel.style.height = Length.Percent(100);
                        TemaUI.Radio(rel, 8);
                        pista.Add(rel);
                        var marca = PiezasUI.Ignorar(new VisualElement());
                        marca.style.position = Position.Absolute;
                        marca.style.top = -6; marca.style.bottom = -6;
                        marca.style.width = 4; marca.style.marginLeft = -2;
                        marca.style.backgroundColor = TemaUI.Red;
                        pista.Add(marca);
                        t.Add(pista);
                        _lista.Add(t);
                        v.Texto = l; v.Valor = val; v.Relleno = rel; v.Marca = marca;
                        break;
                    }
                }
                _vinculos.Add(v);
            }
            Refrescar(modelo);
        }

        private void Refrescar(IReadOnlyList<Elemento> modelo)
        {
            int n = Mathf.Min(modelo.Count, _vinculos.Count);
            for (int i = 0; i < n; i++)
            {
                var m = modelo[i];
                var v = _vinculos[i];
                switch (m.Tipo)
                {
                    case Tipo.Titulo:
                        _titulo.text = Mayus(m.Texto);
                        break;
                    case Tipo.Texto:
                        if (v.Texto != null) v.Texto.text = m.Texto;
                        break;
                    case Tipo.Boton:
                        // "SALIR AL MENÚ" pasa a "¿SEGURO? TOCÁ DE NUEVO", etc.
                        if (v.Texto != null) v.Texto.text = Mayus(m.Texto);
                        break;
                    case Tipo.Interruptor:
                        if (v.PastillaTxt != null)
                        {
                            v.PastillaTxt.text = m.On ? "ON" : "OFF";
                            v.PastillaTxt.style.color = m.On ? TemaUI.Tan : TemaUI.Dim;
                            TemaUI.Borde(v.Pastilla, 2, m.On ? TemaUI.Tan : TemaUI.Dim);
                            v.Pastilla.style.backgroundColor = m.On
                                ? new Color(TemaUI.Tan.r, TemaUI.Tan.g, TemaUI.Tan.b, 0.14f) : Color.clear;
                        }
                        break;
                    case Tipo.Deslizador:
                        if (v.Valor != null)
                        {
                            string txt;
                            try { txt = string.Format(m.Formato ?? "{0:0.00}", m.Valor); }
                            catch (System.FormatException) { txt = m.Valor.ToString("0.00"); }
                            v.Valor.text = txt;
                            float k = m.Max > m.Min ? Mathf.Clamp01((m.Valor - m.Min) / (m.Max - m.Min)) : 0f;
                            v.Relleno.style.width = Length.Percent(k * 100f);
                            if (v.Texto != null) v.Texto.text = Mayus(m.Texto);
                        }
                        break;
                    case Tipo.Medidor:
                        if (v.Relleno != null)
                        {
                            bool apagado = m.Min < 0f;
                            v.Relleno.style.width = Length.Percent(Mathf.Clamp01(m.Valor) * 100f);
                            v.Relleno.style.backgroundColor = m.On ? TemaUI.Tan : new Color(1f, 1f, 1f, 0.35f);
                            v.Marca.style.display = apagado ? DisplayStyle.None : DisplayStyle.Flex;
                            v.Marca.style.left = Length.Percent(Mathf.Clamp01(m.Max) * 100f);
                            v.Valor.text = apagado ? "APAGADO" : m.On ? "TRANSMITE" : "";
                            v.Valor.style.color = m.On ? TemaUI.Tan : TemaUI.Dim;
                        }
                        break;
                }
            }
        }

        private static string Mayus(string s) =>
            string.IsNullOrEmpty(s) ? s : s.ToUpper(System.Globalization.CultureInfo.InvariantCulture);

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
    }
}
