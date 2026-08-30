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

        [Header("Arbmos (alucinacion de cordura — individual por jugador)")]
        [Tooltip("Master switch: activar SOLO en las noches donde aparece el Arbmos (doc: noche 4+).")]
        public bool arbmosActive = false;
        [Tooltip("Duracion (s) de la VENTANA de quietud. Cada X segundos se abre una esfera nueva " +
                 "centrada en el jugador; al cerrarse, si no se salio de ella se lo considera quieto " +
                 "y se intenta invocar al Arbmos. Ver ArbmosDirector.UpdateQuietud.")]
        public float arbmosStillInvokeSeconds = 5f;
        [Tooltip("Radio (m) de esa esfera: cuanto se puede desplazar el jugador dentro de la ventana " +
                 "sin dejar de estar 'quieto'. Se mide sobre el eje del CUERPO, no sobre la camara, asi " +
                 "girar en el lugar para mirar alrededor no cuenta como caminar; ademas se tolera mas " +
                 "cuanto mas gire. En dev se puede pisar desde pausa -> ARBMOS (DEV).")]
        public float arbmosStillRadius = 0.5f;
        [Tooltip("Segundos que el jugador puede estar FUERA de la esfera sin invalidar la ventana. " +
                 "Absorbe los saltos de una correccion de tracking (un frame malo no tiene por que " +
                 "costar la ventana entera); salir de verdad supera esta gracia enseguida.")]
        public float arbmosStillOutsideGrace = 0.4f;
        [Tooltip("Cuanto permanece la alucinacion no letal (s).")]
        public float arbmosPresentSeconds = 6f;
        [Tooltip("Velocidad (m/s) con la que el Arbmos deriva hacia el jugador mientras drena (anim running).")]
        public float arbmosPresentFollowSpeed = 1.2f;
        [Tooltip("Espera (s) entre alucinaciones del MISMO jugador (rango, al azar).")]
        public float arbmosCooldownMin = 20f;
        public float arbmosCooldownMax = 40f;
        [Tooltip("A que distancia (m) del jugador, hacia donde mira, aparece el Arbmos.")]
        public float arbmosSpawnDistance = 2.5f;
        [Tooltip("Cordura por segundo que drena si el jugador se MUEVE mientras el Arbmos esta presente.")]
        public float arbmosSanityDrainPerSecond = 6f;
        [Tooltip("Probabilidad base [0..1] de que aparezca al cumplirse el gatillo de quietud.")]
        public float arbmosSpawnChancePerAttempt = 0.6f;
        [Tooltip("Multiplicador de esa probabilidad cuando la linterna esta apagada.")]
        public float arbmosFlashlightOffChanceMul = 2f;

        [Header("Arbmos letal (cordura en cero)")]
        [Tooltip("Segundos inmovil (sin aura, distorsion en escalada) antes de embestir.")]
        public float arbmosLethalStalkSeconds = 3f;
        [Tooltip("Velocidad (m/s) de la embestida letal.")]
        public float arbmosLethalChaseSpeed = 3.5f;
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
