using System.Collections.Generic;
using UnityEngine;

namespace Bateries
{
    // Una pila en el mundo. Es una NetworkEntity estatica: no se mueve ni tiene IA,
    // asi que no aporta estado de simulacion. La spawnea el servidor (BatterySpawnManager)
    // via NetworkManager.ServerSpawn; el typeId encodea la rareza.
    //
    // rarityIndex se setea en el PREFAB (uno por rareza). En Awake se traduce al typeId
    // de red correspondiente, que debe coincidir con el TypeId registrado en el
    // PrefabRegistry para ese prefab (BatteryBase + rarityIndex).
    public class BatteryEntity : NetworkEntity
    {
        // Registro estatico de pilas vivas: evita que el controlador de pickup tenga que
        // escanear todas las entidades con GetComponent cada frame.
        public static readonly List<BatteryEntity> Active = new();

        [Tooltip("0/1/2. Debe coincidir con la rareza y con el TypeId del PrefabRegistry.")]
        [Range(0, 2)] public byte rarityIndex = 0;

        public byte RarityIndex => rarityIndex;

        // Posicion anchor-relativa: la guardamos al spawnear y reconvertimos a world
        // cada frame, para seguir las recalibraciones del anchor (igual que SorkerNetwork).
        private Vector3 _relPos;
        private bool    _hasRel;

        private void Awake()
        {
            EntityTypeId = (byte)(EntityTypeIds.BatteryBase + rarityIndex);
        }

        private void OnEnable()  { if (!Active.Contains(this)) Active.Add(this); }
        private void OnDisable() { Active.Remove(this); }

        // Estatica: sin estado que sincronizar por tick.
        public override byte[] SerializeState(uint tick) => System.Array.Empty<byte>();
        public override void    ApplyState(uint tick, byte[] data) { }

        public override void OnNetworkSpawn()
        {
            if (WorldOrigin.Instance != null && WorldOrigin.Instance.IsReady)
            {
                _relPos = WorldOrigin.Instance.ToRelative(transform.position);
                _hasRel = true;
            }
        }

        private void Update()
        {
            // Re-anclar a WorldOrigin: si el AR corrige la pose del anchor, la pila
            // sigue pegada a su posicion relativa. Solo tocamos el transform si de verdad
            // cambió (evita ensuciar el transform de un objeto estatico cada frame).
            if (!_hasRel || WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return;

            Vector3 target = WorldOrigin.Instance.ToWorld(_relPos);
            if ((transform.position - target).sqrMagnitude > 1e-8f)
                transform.position = target;
        }
    }
}
