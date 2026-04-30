using System.Collections.Generic;
using UnityEngine;

namespace VoxelPlanet
{
    public static class GoldbergChunkMeshCache
    {
        private static readonly Dictionary<int, Mesh> cachedMeshes = new();

        public static void Set(int chunkIndex, Mesh mesh)
        {
            cachedMeshes[chunkIndex] = mesh;
        }

        public static bool TryGet(int chunkIndex, out Mesh mesh)
        {
            return cachedMeshes.TryGetValue(chunkIndex, out mesh);
        }

        public static void Clear()
        {
            cachedMeshes.Clear();
        }
    }
}