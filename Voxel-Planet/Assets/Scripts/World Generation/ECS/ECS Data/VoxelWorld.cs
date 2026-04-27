using Unity.Entities;
using Unity.Mathematics;

namespace VoxelPlanet
{
    public enum BlockType : byte
    {
        Air,
        Grass,
        Dirt,
        Stone,
        Water
    }

    public struct VoxelPlanetSettings : IComponentData
    {
        public int CellsPerChunk;
        public int LayersPerChunk;
        public int ViewDistance;
        public int OceanLevel;
        public float PlanetRadius;
        public uint Seed;
    }

    public struct VoxelChunk : IComponentData
    {
        public int2 Coord;
        public byte NeedsGeneration;
        public byte NeedsMeshBuild;
    }

    public struct VoxelBlock : IBufferElementData
    {
        public byte Value;
    }
}
