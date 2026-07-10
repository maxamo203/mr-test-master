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
        // Referencia activa para que cualquiera (incluidas las pilas en clientes) pueda
        // leer los colores/rarezas sin cablear el asset en cada objeto. La setea el
        // BatterySpawnManager en Awake (existe en la escena en todos los dispositivos).
        public static BatteryRaritySet Current { get; set; }

        [SerializeField] private BatteryRarity[] rarities;

        public BatteryRarity[] Rarities => rarities;

        // Elige una rareza al azar segun spawnChance (probabilidad relativa, se normaliza
        // sola con la suma). Devuelve null si no hay rarezas o todas tienen chance 0.
        public BatteryRarity WeightedPick() => WeightedPick(null);

        // Igual que WeightedPick pero escalando la probabilidad base de cada rareza por
        // chanceScale(rarityIndex) — asi cada noche modifica la distribucion base sin
        // redefinirla (ver Gameplay.NightConfig.BatteryChanceScale). null => sin escala.
        public BatteryRarity WeightedPick(System.Func<byte, float> chanceScale)
        {
            if (rarities == null || rarities.Length == 0) return null;

            float total = 0f;
            foreach (var r in rarities)
                total += Mathf.Max(0f, r.spawnChance) * Scale(chanceScale, r.rarityIndex);
            if (total <= 0f) return rarities[0];

            float pick = Random.value * total;
            foreach (var r in rarities)
            {
                pick -= Mathf.Max(0f, r.spawnChance) * Scale(chanceScale, r.rarityIndex);
                if (pick <= 0f) return r;
            }
            return rarities[rarities.Length - 1];
        }

        private static float Scale(System.Func<byte, float> chanceScale, byte idx)
            => chanceScale != null ? Mathf.Max(0f, chanceScale(idx)) : 1f;

        // Busca la rareza por su indice (para acreditar la carga al recoger, ya que
        // el mensaje de red solo lleva el rarityIndex).
        public BatteryRarity ByIndex(byte rarityIndex)
        {
            if (rarities == null) return null;
            foreach (var r in rarities)
                if (r.rarityIndex == rarityIndex) return r;
            return null;
        }

        // Color (tint) de una rareza por indice; blanco si no la encuentra.
        public Color TintFor(byte rarityIndex)
        {
            var r = ByIndex(rarityIndex);
            return r != null ? r.tint : Color.white;
        }
    }
}
