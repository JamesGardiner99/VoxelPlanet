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

    public struct GoldbergPlanetSettings : IComponentData
    {
        public float Radius;
        public float CellHeight;
        public int Subdivisions;
        public int Layers;
        public int OceanLevel;
        public uint Seed;
        public byte NeedsBuild;
        public byte NeedsColumnGeneration;
        public byte NeedsVoxelMeshBuild;
    }

    public struct GoldbergPlanetTag : IComponentData
    {
    }

    // TEMP: only used by the flat test world.
    public struct FlatVoxelChunk : IComponentData
    {
        public int2 Coord;
        public byte NeedsGeneration;
        public byte NeedsMeshBuild;
    }

    // REAL: used by the Goldberg planet.
    public struct GoldbergChunk : IComponentData
    {
        public int ChunkIndex;
        public int StartCellIndex;
        public int CellCount;
        public byte NeedsGeneration;
        public byte NeedsMeshBuild;
    }

    public struct VoxelBlock : IBufferElementData
    {
        public byte Value;
    }

    public struct GoldbergCell : IBufferElementData
    {
        public float3 Center;
        public float3 Normal;
        public int FirstVertexIndex;
        public int VertexCount;
    }

    public struct GoldbergCellVertex : IBufferElementData
    {
        public float3 Position;
    }

    public struct VoxelColumn : IBufferElementData
    {
        public int CellIndex;
        public int SurfaceLayer;
    }
}