using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Bateries
{
    // Definicion de un tipo/rareza de pila. Es data pura (clase [Serializable], NO un
    // ScriptableObject): se edita dentro del asset BatteryRaritySet desde el Inspector.
    [Serializable]
    public class BatteryRarity
    {
        public string displayName = "Comun";

        [Tooltip("Identificador del tipo de pila. Fija el TypeId de red (BatteryBase + " +
                 "este numero) y por lo tanto que prefab instancia cada cliente. Poné un " +
                 "numero distinto por cada tipo (0,1,2,3…). Debe coincidir con el " +
                 "rarityIndex del BatteryEntity del prefab y con el TypeId en el PrefabRegistry.")]
        public byte rarityIndex = 0;

        [Tooltip("Carga que suma a la linterna al recogerla.")]
        public float charge = 30f;

        [Tooltip("Probabilidad de aparicion, RELATIVA al resto. Se normaliza con la suma " +
                 "de todas: si tenés 60, 30 y 10 => 60%, 30% y 10%. No hace falta que sumen " +
                 "100. Cuanto mas alto, mas seguido aparece este tipo.")]
        [FormerlySerializedAs("weight")]
        public float spawnChance = 33f;

        [Tooltip("Prefab de la pila en el mundo (con BatteryEntity y el mismo rarityIndex). " +
                 "Tambien va registrado en el PrefabRegistry con TypeId = BatteryBase + rarityIndex.")]
        public GameObject prefab;

        [Tooltip("Color de la pila y de su luz/glow en el mundo.")]
        public Color tint = Color.yellow;
    }
}
