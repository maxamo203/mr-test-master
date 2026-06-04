using System.Collections.Generic;
using UnityEngine;

namespace Scanner
{
    // UI minimalista de guardar/cargar/borrar scans con nombre.
    // Se muestra en una esquina y se expande con un boton.
    public class SaveLoadUI : MonoBehaviour
    {
        [SerializeField] private ARImageAnchor _imageAnchor;

        private bool _expanded;
        private string _newName = "mi cuarto";
        private List<string> _saved = new();
        private Vector2 _scroll;
        private string _flash;
        private float  _flashUntil;

        private void OnEnable()
        {
            if (_imageAnchor == null) _imageAnchor = FindFirstObjectByType<ARImageAnchor>();
            RefreshList();
        }

        private void RefreshList() => _saved = ScanSerializer.ListSaved();

        private static Texture2D _bgTex;
        private static Texture2D BG()
        {
            if (_bgTex == null) { _bgTex = new Texture2D(1,1); _bgTex.SetPixel(0,0, new Color(0,0,0,0.85f)); _bgTex.Apply(); }
            return _bgTex;
        }

        private void OnGUI()
        {
            UIScale.Begin();

            float w = 320;
            float x = UIScale.VirtualWidth - w - 10;
            float y = 380;

            var bgStyle = new GUIStyle(GUI.skin.box) { normal = { background = BG() } };

            if (!_expanded)
            {
                if (GUI.Button(new Rect(x + w - 130, y, 130, 45), "Guardar/Cargar"))
                    _expanded = true;
                return;
            }

            GUILayout.BeginArea(new Rect(x, y, w, 420), GUIContent.none, bgStyle);

            var title = new GUIStyle { fontSize = 22, normal = { textColor = Color.white } };
            GUILayout.BeginHorizontal();
            GUILayout.Label("Guardar / Cargar", title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(40))) _expanded = false;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Nombre:");
            _newName = GUILayout.TextField(_newName, GUILayout.Width(w - 30));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Guardar", GUILayout.Height(40)))
            {
                var data = SceneRegistry.Instance.Capture(_newName);
                // Persistimos también la imagen de referencia capturada en esta
                // sesión (si hay), para reconocer la zona física al recargar.
                data.refImageWidthMeters = CapturedReference.WidthMeters;
                if (CapturedReference.HasImage)
                    ScanSerializer.SaveRefImage(_newName, CapturedReference.Texture);
                ScanSerializer.Save(_newName, data);
                Flash($"Guardado '{_newName}'");
                RefreshList();
            }
            if (GUILayout.Button("Refrescar lista", GUILayout.Height(40)))
                RefreshList();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Scans guardados:");
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(180));
            if (_saved.Count == 0) GUILayout.Label("  (vacio)");
            foreach (var name in _saved)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(name, GUILayout.Height(34))) DoLoad(name);
                if (GUILayout.Button("X", GUILayout.Width(40), GUILayout.Height(34)))
                {
                    ScanSerializer.Delete(name);
                    RefreshList();
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_flash) && Time.time < _flashUntil)
                GUILayout.Label(_flash);

            GUILayout.EndArea();
        }

        private void DoLoad(string name)
        {
            var data = ScanSerializer.Load(name);
            if (data == null) return;

            SceneRegistry.Instance.ClearAll();
            foreach (var w in data.walls) WallObject.FromData(w);
            foreach (var c in data.cubes) CubeObject.FromData(c);
            _newName = name;

            // Si el escaneo tiene imagen de referencia, la re-registramos en la
            // librería mutable y reiniciamos el tracking: al volver a enfocar la
            // zona física, ARKit/ARCore la reconoce y reposiciona el anchor.
            // keepVisualPosition: true → lo recién cargado se queda donde está y
            // solo se recalcula su pose relativa al nuevo anchor cuando aparece.
            var refTex = ScanSerializer.LoadRefImage(name);
            if (refTex != null && _imageAnchor != null)
            {
                CapturedReference.Set(refTex, data.refImageWidthMeters);
                _imageAnchor.AddReferenceImage(refTex, name, data.refImageWidthMeters, keepVisualPosition: true);
                ScanStateMachine.Instance?.SetMode(ScannerMode.Calibrating);
                Flash($"Cargado '{name}' — buscando la zona…");
            }
            else
            {
                Flash($"Cargado '{name}'");
            }
        }

        private void Flash(string msg) { _flash = msg; _flashUntil = Time.time + 2.5f; }
    }
}
