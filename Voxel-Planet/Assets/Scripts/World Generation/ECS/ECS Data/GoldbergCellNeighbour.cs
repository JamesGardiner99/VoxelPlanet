using Unity.Entities;

namespace VoxelPlanet
{
    public struct GoldbergCellNeighbour : IBufferElementData
    {
        public int CellIndex;
        public int NeighbourCellIndex;

        // Which edge of CellIndex this neighbour touches.
        public int EdgeIndex;
    }

    // Flattened lookup buffer.
    // Index = CellIndex * GoldbergNeighbourConstants.MaxEdgesPerCell + EdgeIndex.
    public struct GoldbergCellNeighbourLookup : IBufferElementData
    {
        public int NeighbourCellIndex;
    }

    public static class GoldbergNeighbourConstants
    {
        public const int MaxEdgesPerCell = 6;
    }

    public struct GoldbergNeighboursBuilt : IComponentData
    {
    }
}
