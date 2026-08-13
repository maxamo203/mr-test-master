using UnityEngine;

namespace Gameplay
{
    // Perillas de DESARROLLO del gatillo de quietud del Arbmos (ver ArbmosDirector.UpdateQuietud).
    //
    // El radio de la esfera de quietud es un numero FIJO de diseño (vive en la NightConfig),
    // pero es imposible de calibrar sin probarlo en el celular: depende del jitter del
    // tracking, del drift y de como cada persona sostiene el telefono. Asi que en
    // development build (pausa -> Opciones -> ARBMOS (DEV)) se puede pisar en vivo y ver el
    // wireframe de las esferas segun se van generando.
    //
    // Tiers (ver CLAUDE.md): en RELEASE nada de esto existe — los getters devuelven
    // directamente el valor de la noche (una comparacion contra una constante que el
    // compilador/JIT resuelve) y el wireframe es constante false, asi que no se dibuja ni
    // se crean las mallas. La persistencia en PlayerPrefs tambien es solo dev.
    public static class ArbmosDebug
    {
        private const string KeyPrefijo = "arbmos_dev_";

        // Si esta apagado, se usan los valores de la NightConfig (comportamiento de release).
        private static bool  _override;
        private static float _radio   = 0.5f;
        private static float _ventana = 5f;
        private static float _gracia  = 0.4f;
        private static bool  _wireframe;

        static ArbmosDebug()
        {
            if (!Debug.isDebugBuild) return;
            _override  = PlayerPrefs.GetInt(KeyPrefijo + "override", 0) == 1;
            _radio     = PlayerPrefs.GetFloat(KeyPrefijo + "radio",   _radio);
            _ventana   = PlayerPrefs.GetFloat(KeyPrefijo + "ventana", _ventana);
            _gracia    = PlayerPrefs.GetFloat(KeyPrefijo + "gracia",  _gracia);
            _wireframe = PlayerPrefs.GetInt(KeyPrefijo + "wire", 0) == 1;
        }

        // ── Valores efectivos que consume el director ─────────────────────────
        public static float Radio(NightConfig n) =>
            Activo ? _radio   : (n != null ? n.arbmosStillRadius         : 0.5f);

        public static float Ventana(NightConfig n) =>
            Activo ? _ventana : (n != null ? n.arbmosStillInvokeSeconds  : 5f);

        public static float Gracia(NightConfig n) =>
            Activo ? _gracia  : (n != null ? n.arbmosStillOutsideGrace   : 0.4f);

        // ── Perillas (solo se tocan desde el menu de pausa en development build) ──
        public static bool Activo
        {
            get => Debug.isDebugBuild && _override;
            set { _override = value; GuardarInt("override", value); }
        }

        public static float RadioDev   { get => _radio;   set { _radio   = Mathf.Clamp(value, 0.05f, 3f);  Guardar("radio",   _radio);   } }
        public static float VentanaDev { get => _ventana; set { _ventana = Mathf.Clamp(value, 0.5f, 30f);  Guardar("ventana", _ventana); } }
        public static float GraciaDev  { get => _gracia;  set { _gracia  = Mathf.Clamp(value, 0f,    3f);  Guardar("gracia",  _gracia);  } }

        // Wireframe de la esfera de quietud del jugador local (solo host: el director es
        // server-authoritative y solo el server sabe donde esta la esfera de cada uno).
        public static bool Wireframe
        {
            get => Debug.isDebugBuild && _wireframe;
            set { _wireframe = value; GuardarInt("wire", value); }
        }

        private static void Guardar(string k, float v)
        {
            if (!Debug.isDebugBuild) return;
            PlayerPrefs.SetFloat(KeyPrefijo + k, v);
            PlayerPrefs.Save();
        }

        private static void GuardarInt(string k, bool v)
        {
            if (!Debug.isDebugBuild) return;
            PlayerPrefs.SetInt(KeyPrefijo + k, v ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
