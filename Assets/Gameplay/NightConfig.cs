using System;
using UnityEngine;

namespace Gameplay
{
    // Configuracion de dificultad de UNA noche. Es un asset editable desde el editor
    // (Create > Gameplay > Night Config). El menu de inicio lista estos assets y el
    // server aplica el elegido al arrancar la partida.
    //
    // Toda la logica de gameplay lee sus numeros de aca — nada hardcodeado. Pensado
    // para crecer: agregar una variable de dificultad = agregar un campo.
    [CreateAssetMenu(fileName = "NightConfig", menuName = "Gameplay/Night Config")]
    public class NightConfig : ScriptableObject
    {
        [Header("Identidad")]
        public string displayName = "Noche";
        [Tooltip("Texto del briefing que se muestra al arrancar la noche (ARLobbyUI).")]
        [TextArea] public string briefing = "";

        [Header("Entradas del Sorken")]
        [Tooltip("Segundos entre intentos de entrada (rango, se elige al azar).")]
        public float attemptIntervalMin = 20f;
        public float attemptIntervalMax = 35f;
        [Tooltip("Segundos que hay que iluminar el punto para repeler el intento.")]
        public float entryRepelSeconds = 3f;
        [Tooltip("Segundos de gracia antes de que el Sorken entre si no se repele.")]
        public float entryGraceSeconds = 5f;
        [Tooltip("Distancia (m) a la que el jugador dispara la ventana de entrada.")]
        public float entryTriggerDistance = 3f;

        [Header("Persecucion")]
        [Tooltip("Segundos que hay que iluminar al Sorken para ahuyentarlo en el chase.")]
        public float chaseRepelSeconds = 5f;
        public float sorkenChaseSpeed = 2.5f;
        [Tooltip("Velocidad al retirarse tras ser repelido (sale corriendo).")]
        public float sorkenRetreatSpeed = 3.5f;
        [Tooltip("Distancia (m) a la que el Sorken atrapa al jugador (grab).")]
        public float grabRange = 1.2f;

        [Header("Linterna")]
        public float flashlightMaxCharge = 100f;
        public float flashlightDrainPerSecond = 2f;

        [Header("Cordura")]
        public float sanityMax = 100f;
        [Tooltip("Segundos con la linterna apagada antes de que empiece a drenar cordura.")]
        public float flashlightOffThreshold = 5f;
        public float sanityDrainPerSecond = 4f;

        [Header("Cono de linterna (repeler — server-authoritative)")]
        [Tooltip("Semi-angulo del cono (grados) para contar que la linterna ilumina un objetivo.")]
        public float flashlightConeAngleDeg = 30f;
        [Tooltip("Alcance (m) de la linterna para el repel.")]
        public float flashlightRange = 8f;

        [Header("Director / tiempos")]
        [Tooltip("Demora (s) del primer intento tras arrancar la partida.")]
        public float initialAttemptDelay = 4f;
        [Tooltip("Si nadie se acerca ni repele, el intento se cancela tras esto (s).")]
        public float emergeTimeoutSeconds = 15f;
        [Tooltip("Cuanto dura la retirada (s) antes de despawnear al Sorken.")]
        public float retreatSeconds = 1.5f;
        [Tooltip("Cuanto se queda el Sorken en 'grab' (s) tras atrapar, antes de irse.")]
        public float grabHoldSeconds = 1.5f;

        [Header("Feedback de deteccion")]
        [Tooltip("Angulo (grados) dentro del cual apuntar al punto activa titileo/ruido.")]
        public float flickerAimAngle = 20f;
        [Tooltip("Distancia (m) a la que empieza el titileo/ruido al apuntar al punto.")]
        public float flickerDistance = 6f;

        [Header("Baterias — modifican la base del BatteryRaritySet")]
        [Tooltip("Multiplicador global de frecuencia de aparicion de baterias (1 = base).")]
        public float batterySpawnRateMultiplier = 1f;
        [Tooltip("Ajuste por rareza sobre la probabilidad base. Las no listadas quedan igual.")]
        public BatteryChanceMod[] batteryChanceMods;

        // Multiplicador de probabilidad para una rareza (1 si no esta listada). Lo usa
        // el spawner de baterias via el overload BatteryRaritySet.WeightedPick(scale).
        public float BatteryChanceScale(byte rarityIndex)
        {
            if (batteryChanceMods != null)
                foreach (var m in batteryChanceMods)
                    if (m != null && m.rarityIndex == rarityIndex)
                        return Mathf.Max(0f, m.chanceMultiplier);
            return 1f;
        }
    }

    // Ajuste de una rareza de bateria para esta noche (multiplica su probabilidad base).
    [Serializable]
    public class BatteryChanceMod
    {
        [Tooltip("rarityIndex de la rareza en el BatteryRaritySet.")]
        public byte rarityIndex;
        [Tooltip("Multiplicador sobre la probabilidad base (1 = igual, <1 mas escasa).")]
        public float chanceMultiplier = 1f;
    }
}
