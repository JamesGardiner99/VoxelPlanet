using Unity.Entities;
using Unity.Mathematics;

namespace VoxelPlanet
{
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