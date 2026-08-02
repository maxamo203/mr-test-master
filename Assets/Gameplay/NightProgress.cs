using UnityEngine;

namespace Gameplay
{
    // Progresión de noches desbloqueadas (persistente, por dispositivo).
    //
    // Se guarda una sola cosa: CUÁNTAS noches están desbloqueadas. La noche 1 siempre
    // lo está; cada noche superada desbloquea la siguiente. No se guarda un set porque
    // el desbloqueo es estrictamente secuencial.
    //
    // La clave se parte entre dev y prod a propósito: el menú usa `_devNights` en
    // development build y `_nights` en release (ver NightMenuUI.Nights), así que son
    // dos catálogos distintos y compartir el contador daría índices desbloqueados que
    // no corresponden. Las PlayerPrefs son de la app, no del build.
    public static class NightProgress
    {
        private static string Key => Debug.isDebugBuild ? "prog_noches_dev" : "prog_noches";

        // Cantidad de noches desbloqueadas (siempre >= 1: la primera es de arranque).
        public static int Desbloqueadas => Mathf.Max(1, PlayerPrefs.GetInt(Key, 1));

        public static bool Desbloqueada(int index) => index >= 0 && index < Desbloqueadas;

        // Superaste la noche `index` → queda desbloqueada la siguiente. Devuelve true
        // sólo si REALMENTE desbloqueó algo nuevo (rejugar una noche vieja no cuenta).
        public static bool RegistrarNocheSuperada(int index)
        {
            if (index < 0) return false;
            int nuevo = index + 2;
            if (nuevo <= Desbloqueadas) return false;
            Guardar(nuevo);
            return true;
        }

        // ── Herramientas de desarrollo (ver NightMenuUI → OPCIONES, dev-only) ──

        public static void DesbloquearTodas(int total) => Guardar(Mathf.Max(1, total));

        public static void BorrarProgreso() => Guardar(1);

        private static void Guardar(int cantidad)
        {
            PlayerPrefs.SetInt(Key, Mathf.Max(1, cantidad));
            PlayerPrefs.Save();
        }
    }
}
