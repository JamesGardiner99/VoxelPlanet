using Unity.Entities;
using UnityEngine;

namespace VoxelPlanet
{
    public class VoxelPlanetAuthoring : MonoBehaviour
    {
        public int cellsPerChunk = 16;
        public int layersPerChunk = 64;
        public int viewDistance = 4;
        public int oceanLevel = 0;
        public float planetRadius = 64f;
        public uint seed = 12345;
    }
}