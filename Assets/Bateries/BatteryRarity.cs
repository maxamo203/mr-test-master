using System;
using UnityEngine;

namespace Bateries
{
    // Definicion de un tipo/rareza de pila. Es data pura: la editas en el asset
    // BatteryRaritySet desde el Inspector. rarityIndex fija el typeId de red de la
    // entidad (EntityTypeIds.BatteryBase + rarityIndex) y por lo tanto que prefab
    // instancia cada cliente.
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

    // El "archivo independiente" con los tipos de pila. Lo consume el
    // BatterySpawnManager. Crealo con Create > Bateries > RaritySet.
    [CreateAssetMenu(fileName = "BatteryRaritySet", menuName = "Bateries/RaritySet")]
    public class BatteryRaritySet : ScriptableObject
    {
        [SerializeField] private BatteryRarity[] rarities;

        public BatteryRarity[] Rarities => rarities;

        // Elige una rareza al azar ponderada por weight. Devuelve null si no hay
        // rarezas configuradas o todos los pesos son 0.
        public BatteryRarity WeightedPick()
        {
            if (rarities == null || rarities.Length == 0) return null;

            float total = 0f;
            foreach (var r in rarities) total += Mathf.Max(0f, r.weight);
            if (total <= 0f) return rarities[0];

            float pick = UnityEngine.Random.value * total;
            foreach (var r in rarities)
            {
                pick -= Mathf.Max(0f, r.weight);
                if (pick <= 0f) return r;
            }
            return rarities[rarities.Length - 1];
        }

        // Busca la rareza por su indice (para acreditar la carga al recoger, ya que
        // el mensaje de red solo lleva el rarityIndex).
        public BatteryRarity ByIndex(byte rarityIndex)
        {
            if (rarities == null) return null;
            foreach (var r in rarities)
                if (r.rarityIndex == rarityIndex) return r;
            return null;
        }
    }
}
