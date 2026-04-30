using Unity.Entities;
using Unity.Mathematics;

namespace VoxelPlanet
{
    public struct GoldbergVoxelChunk : IComponentData
    {
        public Entity PlanetEntity;
        public int ChunkIndex;
        public byte NeedsMeshBuild;
        public byte IsMeshBuilt;
        public float3 Center;
    }

    public struct GoldbergChunkColumn : IBufferElementData
    {
        public int ColumnIndex;
    }

    public struct GoldbergVoxelChunkNeedsMeshBuild : IComponentData
    {
    }

    public struct GoldbergVoxelChunksCreated : IComponentData
    {
    }
}