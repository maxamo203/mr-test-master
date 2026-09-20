using UnityEngine;

namespace Gameplay
{
    // Récord persistente (por dispositivo) de reliquias recogidas, POR NOCHE — cada
    // noche tiene su propio mejor resultado, no hay un número global. La clave usa el
    // índice de la noche (base 0, el mismo que GameSession.NightIndex / _nocheSel en
    // NightMenuUI) para no depender de un catálogo con nombres propios.
    public static class CollectibleProgress
    {
        private const string KeyPrefix = "reliquias_record_noche_";

        private static string Key(int nightIndex) => KeyPrefix + nightIndex;

        // 0 si nunca se registró un intento para esa noche (o si nightIndex es inválido,
        // p. ej. un cliente LAN que no pasó por el menú de noches).
        public static int Record(int nightIndex) =>
            nightIndex < 0 ? 0 : PlayerPrefs.GetInt(Key(nightIndex), 0);

        // Devuelve true sólo si `cantidad` superó el récord anterior de ESA noche (y lo
        // guarda). Rejugar peor que tu mejor intento de esa noche no toca el récord. Sin
        // noche conocida (nightIndex < 0) no hay dónde guardarlo: no-op.
        public static bool RegistrarIntento(int nightIndex, int cantidad)
        {
            if (nightIndex < 0) return false;
            if (cantidad <= Record(nightIndex)) return false;
            PlayerPrefs.SetInt(Key(nightIndex), cantidad);
            PlayerPrefs.Save();
            return true;
        }
    }
}
