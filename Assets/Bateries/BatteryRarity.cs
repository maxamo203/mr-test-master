using System;
using UnityEngine;

namespace Bateries
{
    // Definicion de un tipo/rareza de pila. Es data pura (clase [Serializable], NO un
    // ScriptableObject): se edita dentro del asset BatteryRaritySet desde el Inspector.
    // rarityIndex fija el typeId de red de la entidad (EntityTypeIds.BatteryBase +
    // rarityIndex) y por lo tanto que prefab instancia cada cliente.
    [Serializable]
    public class BatteryRarity
    {
        public string     displayName = "Comun";
        [Tooltip("0/1/2. Fija el typeId de red (BatteryBase + este indice) y el prefab.")]
        [Range(0, 2)] public byte rarityIndex = 0;
        [Tooltip("Carga que suma a la linterna al recogerla.")]
        public float      charge = 30f;
        [Tooltip("Peso relativo de aparicion. Mas alto = mas frecuente. Las rarezas " +
                 "que dan mas carga deberian tener menos peso.")]
        public float      weight = 1f;
        [Tooltip("Prefab de la pila en el mundo (con BatteryEntity). Tambien va " +
                 "registrado en el PrefabRegistry con TypeId = BatteryBase + rarityIndex.")]
        public GameObject prefab;
        [Tooltip("Color opcional, solo informativo/para HUD.")]
        public Color      tint = Color.yellow;
    }
}
