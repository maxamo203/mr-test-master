using UnityEngine;

namespace Gameplay
{
    // Perillas de DESARROLLO del encuentro de luz de Arbmos V2. Permiten calibrar en el
    // dispositivo los tres umbrales principales sin modificar los assets de las noches.
    //
    // Tiers (ver CLAUDE.md): en RELEASE nada de esto existe — los getters devuelven
    // directamente el valor de la noche. La persistencia en PlayerPrefs tambien es solo dev.
    public static class ArbmosDebug
    {
        private const string KeyPrefijo = "arbmos_dev_";

        // Si esta apagado, se usan los valores de la NightConfig (comportamiento de release).
        private static bool  _override;
        private static float _hide = 3f;
        private static float _exposure = 0.8f;
        private static float _attackCommit = 0.35f;

        static ArbmosDebug()
        {
            if (!Debug.isDebugBuild) return;
            _override  = PlayerPrefs.GetInt(KeyPrefijo + "override", 0) == 1;
            _hide         = PlayerPrefs.GetFloat(KeyPrefijo + "hide", _hide);
            _exposure     = PlayerPrefs.GetFloat(KeyPrefijo + "exposure", _exposure);
            _attackCommit = PlayerPrefs.GetFloat(KeyPrefijo + "attack_commit", _attackCommit);
        }

        // ── Valores efectivos que consume el director ─────────────────────────
        public static float HideSeconds(NightConfig n) => Mathf.Max(0.1f,
            Activo ? _hide : (n != null ? n.arbmosHideSeconds : 3f));

        public static float ExposureSeconds(NightConfig n) => Mathf.Max(0.05f,
            Activo ? _exposure : (n != null ? n.arbmosExposureSeconds : 0.8f));

        public static float AttackCommitSeconds(NightConfig n) => Mathf.Max(0f,
            Activo ? _attackCommit : (n != null ? n.arbmosAttackCommitSeconds : 0.35f));

        // ── Perillas (solo se tocan desde el menu de pausa en development build) ──
        public static bool Activo
        {
            get => Debug.isDebugBuild && _override;
            set { _override = value; GuardarInt("override", value); }
        }

        public static float HideDev { get => _hide; set { _hide = Mathf.Clamp(value, 0.1f, 10f); Guardar("hide", _hide); } }
        public static float ExposureDev { get => _exposure; set { _exposure = Mathf.Clamp(value, 0.05f, 5f); Guardar("exposure", _exposure); } }
        public static float AttackCommitDev { get => _attackCommit; set { _attackCommit = Mathf.Clamp(value, 0f, 3f); Guardar("attack_commit", _attackCommit); } }

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
