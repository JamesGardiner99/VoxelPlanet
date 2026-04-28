using Unity.Entities;

namespace VoxelPlanet
{
    public struct GoldbergCellNeighbour : IBufferElementData
    {
        public int CellIndex;
        public int NeighbourCellIndex;

        //Which edge of CellIndex this neighbour touches
        public int EdgeIndex;
    }

    public struct GoldbergNeighboursBuilt : IComponentData
    {
    }
}