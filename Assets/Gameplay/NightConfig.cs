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

        [Header("Duración")]
        [Tooltip("Segundos que hay que sobrevivir para que amanezca. Al llegar a 0 la " +
                 "noche se gana y se desbloquea la siguiente (ver NightProgress). " +
                 "Poner 0 = noche sin límite de tiempo (no se puede ganar).")]
        public float nightDurationSeconds = 300f;

        [Header("Entradas del Sorken")]
        [Tooltip("Master switch del Sorken. Desactivar (solo dev/testing) para probar SIN " +
                 "Sorken — util para aislar el Arbmos u otras mecanicas. En prod: siempre activo.")]
        public bool sorkenActive = true;
        [Tooltip("Segundos entre intentos de entrada (rango, se elige al azar).")]
        public float attemptIntervalMin = 20f;
        public float attemptIntervalMax = 35f;
        [Tooltip("Segundos que hay que iluminar el punto para repeler el intento.")]
        public float entryRepelSeconds = 3f;
        [Tooltip("Duracion total del intento de entrada antes de que el Sorken comience la persecucion.")]
        [Min(0.1f)] public float entryGraceSeconds = 8f;
        [Tooltip("Tramo final de la entrada reservado para la animacion de emergencia. " +
                 "El Sorken permanece quieto durante el resto de la ventana para dar tiempo a reaccionar.")]
        [Min(0f)] public float entryAnimationSeconds = 3f;
        [Tooltip("Distancia (m) a la que el jugador dispara la ventana de entrada.")]
        public float entryTriggerDistance = 3f;

        [Header("Persecucion")]
        [Tooltip("Tiempo TOTAL de luz intensa valida para ahuyentar al Sorken. El gesto " +
                 "inicial de cubrirse ya cuenta dentro de este tiempo.")]
        [Min(0.1f)] public float chaseRepelSeconds = 3.5f;
        [Tooltip("Pausa inmovil al terminar de entrar. Durante ella puede comenzar la defensa, " +
                 "pero nunca puede capturar al jugador.")]
        [Min(0f)] public float sorkenPostEntryPauseSeconds = 2.5f;
        [Tooltip("Velocidad normal dentro del ambiente, pensada para espacios pequenos.")]
        [Min(0f)] public float sorkenChaseSpeed = 0.5f;
        [Tooltip("Velocidad muy lenta mientras recibe luz intensa valida.")]
        [Min(0f)] public float sorkenIlluminatedSpeed = 0.1f;
        [Tooltip("Al perder el haz, conserva progreso y posicion durante esta ventana.")]
        [Min(0f)] public float sorkenAimToleranceSeconds = 2f;
        [Tooltip("Segundos de progreso que pierde por cada segundo sin luz, una vez agotada la tolerancia.")]
        [Min(0f)] public float sorkenRepelDecayPerSecond = 0.5f;
        [Tooltip("Intervalo entre reintentos de ruta cuando existe geometria pero no hay camino valido.")]
        [Min(0.05f)] public float sorkenBlockedRepathSeconds = 0.4f;
        [Tooltip("Duracion de la animacion inicial al cubrirse el rostro con la linterna.")]
        [Min(0f)] public float sorkenCoverStartSeconds = 3f;
        [Tooltip("Multiplicador adicional de la animacion cubierta. La velocidad iluminada " +
                 "sigue teniendo como techo sorkenIlluminatedSpeed.")]
        [Range(0.1f, 1f)] public float sorkenCoverWalkSpeedMultiplier = 1f;
        [Tooltip("Velocidad al retirarse tras ser repelido (sale corriendo).")]
        public float sorkenRetreatSpeed = 3.5f;
        [Tooltip("Distancia (m) a la que el Sorken atrapa al jugador (grab).")]
        public float grabRange = 1.2f;

        [Header("Linterna")]
        public float flashlightMaxCharge = 100f;
        [Tooltip("Consumo por segundo en el modo tenue.")]
        public float flashlightDrainPerSecond = 2f;
        [Tooltip("Consumo por segundo mientras se mantiene el modo intenso.")]
        public float flashlightBrightDrainPerSecond = 6f;

        [Header("Cordura")]
        public float sanityMax = 100f;

        [Header("Arbmos V2 (alucinacion individual por jugador)")]
        [Tooltip("Master switch: activar SOLO en las noches donde aparece el Arbmos (doc: noche 4+).")]
        public bool arbmosActive = false;
        [Tooltip("Solo DEV: invoca al terminar el cooldown sin tirar probabilidad.")]
        public bool arbmosForceSpawnAfterCooldown = false;
        [Tooltip("Espera (s) entre alucinaciones del MISMO jugador (rango, al azar).")]
        public float arbmosCooldownMin = 20f;
        public float arbmosCooldownMax = 40f;
        [Tooltip("A que distancia (m) del jugador, hacia donde mira, aparece el Arbmos.")]
        public float arbmosSpawnDistance = 2.5f;
        [Tooltip("Probabilidad base [0..1] de aparicion al terminar el cooldown.")]
        public float arbmosSpawnChancePerAttempt = 0.6f;
        [Tooltip("Segundos continuos con la linterna apagada para ocultar una aparicion normal.")]
        [Min(0.1f)] public float arbmosHideSeconds = 3f;
        [Tooltip("Segundos de luz directa sobre la cabeza necesarios para comprometer el ataque.")]
        [Min(0.05f)] public float arbmosExposureSeconds = 0.8f;
        [Tooltip("Demora breve entre comprometer el ataque normal y aplicar el daño.")]
        [Min(0f)] public float arbmosAttackCommitSeconds = 0.35f;
        [Tooltip("Techo de velocidad (m/s). La velocidad real también respeta la distancia por paso del clip.")]
        [Min(0.1f)] public float arbmosAttackChaseSpeed = 2.5f;
        [Tooltip("Limite de seguridad (s) del ataque normal si no consigue alcanzar al jugador.")]
        [Min(0.1f)] public float arbmosAttackMaxSeconds = 14f;
        [Tooltip("Distancia horizontal a la que ejecuta el susto y aplica el golpe de cordura.")]
        [Min(0.05f)] public float arbmosAttackGrabRange = 1f;
        [Tooltip("Cordura que quita un ataque normal. Nunca mata directamente.")]
        [Min(0f)] public float arbmosSanityDamage = 30f;
        [Tooltip("Altura de respaldo del punto de mirada si el prefab no tiene lookTarget.")]
        [Min(0f)] public float arbmosLookTargetHeight = 1.65f;
        [Tooltip("Radio tolerado alrededor de la cabeza para detectar el haz.")]
        [Min(0f)] public float arbmosLookTargetRadius = 0.25f;

        [Header("Arbmos letal (cordura en cero)")]
        [Tooltip("Segundos inmovil (sin aura, distorsion en escalada) antes de embestir.")]
        public float arbmosLethalStalkSeconds = 3f;
        [Tooltip("Velocidad (m/s) de la embestida letal.")]
        public float arbmosLethalChaseSpeed = 3.5f;
        [Tooltip("Limite de seguridad de la persecucion final. Al vencer, completa la muerte " +
                 "aunque la navegacion no encuentre una ruta, para que la secuencia no quede trabada.")]
        [Min(0.1f)] public float arbmosLethalMaxSeconds = 8f;
        [Tooltip("Frecuencia con la que recalcula la ruta durante la persecucion final.")]
        [Min(0.05f)] public float arbmosLethalRepathSeconds = 0.3f;
        [Tooltip("Distancia (m) a la que el Arbmos letal atrapa al jugador.")]
        public float arbmosGrabRange = 1.2f;

        [Header("Libro ritual (oscuridad desde el centro)")]
        [Tooltip("Master switch del libro. Desactivar (dev/testing) para probar SIN la " +
                 "mecánica del libro. En prod: siempre activo.")]
        public bool bookActive = true;
        [Tooltip("Espera aleatoria minima (s) antes de que la oscuridad ataque el libro.")]
        [Min(0f)] public float bookEventDelayMin = 30f;
        [Tooltip("Espera aleatoria maxima (s) antes de que la oscuridad ataque el libro.")]
        [Min(0f)] public float bookEventDelayMax = 50f;
        [Tooltip("Ventana completa (s) desde que empieza la oscuridad hasta que consume el libro.")]
        [Min(0.1f)] public float bookConsumeSeconds = 6f;
        [Tooltip("Segundos CONTINUOS de linterna sobre el libro necesarios para salvarlo.")]
        [Min(0.1f)] public float bookDefenseSeconds = 4f;

        public float RandomBookEventDelay() =>
            UnityEngine.Random.Range(Mathf.Min(bookEventDelayMin, bookEventDelayMax),
                                     Mathf.Max(bookEventDelayMin, bookEventDelayMax));

        [Header("Veleth (invocada al perder el libro)")]
        [Tooltip("Velocidad de persecucion. Veleth no puede ser repelida con la linterna.")]
        [Min(0.1f)] public float velethChaseSpeed = 3.2f;
        [Tooltip("Distancia horizontal a la que Veleth atrapa al jugador.")]
        [Min(0.05f)] public float velethGrabRange = 1.1f;
        [Tooltip("Frecuencia con la que recalcula su ruta hacia el jugador que se mueve.")]
        [Min(0.05f)] public float velethRepathSeconds = 0.2f;
        [Tooltip("Pausa del jumpscare antes de continuar hacia otro jugador vivo.")]
        [Min(0f)] public float velethGrabHoldSeconds = 0.8f;

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

        [Header("Coleccionables (reliquias del ritual)")]
        [Tooltip("Master switch. Desactivado por defecto: ninguna noche existente " +
                 "cambia de comportamiento sola.")]
        public bool collectiblesActive = false;
        [Tooltip("Demora (s) antes de que aparezca la PRIMERA reliquia de la noche.")]
        public float collectibleInitialDelaySeconds = 5f;
        [Tooltip("Espera aleatoria (s), medida desde que se RECOGIÓ la última reliquia " +
                 "(no desde que apareció), antes de que aparezca la siguiente.")]
        public float collectibleIntervalMin = 40f;
        public float collectibleIntervalMax = 80f;
        [Tooltip("Cuantas reliquias como maximo aparecen en TODA la noche (recogidas o no: " +
                 "cuenta apariciones, no pickups). Al llegar al limite no aparecen mas, aunque " +
                 "falte tiempo para el amanecer. 0 = sin limite.")]
        public int collectibleMaxPerNight = 5;
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
