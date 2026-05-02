using Unity.Mathematics;
using UnityEngine;

namespace VoxelPlanet
{
    public class PlanetPlayerPositionBridge : MonoBehaviour
    {
        public static bool HasPlayer;
        public static float3 Position;

        public static void SetPreloadPosition(float3 position)
        {
            Position = position;
            HasPlayer = true;
        }

        private void LateUpdate()
        {
            Position = transform.position;
            HasPlayer = true;
        }

        private void OnDisable()
        {
            HasPlayer = false;
        }
    }
}