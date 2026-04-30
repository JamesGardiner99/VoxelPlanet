using Unity.Mathematics;
using UnityEngine;

namespace VoxelPlanet
{
    public class PlanetPlayerPositionBridge : MonoBehaviour
    {
        public static bool HasPlayer;
        public static float3 Position;

        private void LateUpdate()
        {
            Position = transform.position;
            HasPlayer = true;

            Debug.Log($"[BRIDGE] Player world position: {Position}");
        }

        private void OnDisable()
        {
            HasPlayer = false;
        }
    }
}