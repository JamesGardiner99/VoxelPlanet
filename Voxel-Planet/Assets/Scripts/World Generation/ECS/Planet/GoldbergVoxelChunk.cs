using Unity.Entities;

namespace VoxelPlanet
{
    public struct GoldbergVoxelChunk : IComponentData
    {
        public Entity PlanetEntity;
        public int ChunkIndex;
        public byte NeedsMeshBuild;
    }

    public struct GoldbergChunkColumn : IBufferElementData
    {
        public int ColumnIndex;
    }

    public struct GoldbergVoxelChunksCreated : IComponentData
    {
    }
}