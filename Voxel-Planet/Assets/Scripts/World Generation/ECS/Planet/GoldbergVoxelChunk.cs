using Unity.Entities;

namespace VoxelPlanet
{
    public struct GoldbergVoxelChunk : IComponentData
    {
        public Entity PlanetEntity;

        public int ChunkIndex;
        public int StartColumnIndex;
        public int ColumnCount;

        public byte NeedsMeshBuild;
    }

    public struct GoldbergVoxelChunksCreated : IComponentData
    {
    }
}