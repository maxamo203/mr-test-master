using UnityEngine;

namespace Bateries
{
    // El "archivo independiente" con los tipos de pila. Lo consume el
    // BatterySpawnManager. Crealo con Create > Bateries > RaritySet.
    //
    // IMPORTANTE: un ScriptableObject debe vivir en un archivo con el mismo nombre que
    // la clase (BatteryRaritySet.cs). Por eso está separado de BatteryRarity.
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

            float pick = Random.value * total;
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
