using UnityEngine;

namespace VoxelPlanet
{
    public static class GoldbergRegionBoundaryDebug
    {
        public static bool Enabled = true;

        public static void DrawEdge(Vector3 a, Vector3 b, Color color)
        {
            if (!Enabled)
                return;

            Debug.DrawLine(a, b, color, 5f);
        }
    }
}