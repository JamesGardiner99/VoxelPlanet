using Unity.Entities;
using UnityEngine;

namespace VoxelPlanet
{
    public class VoxelPlanetBaker : Baker<VoxelPlanetAuthoring>
    {
        public override void Bake(VoxelPlanetAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new VoxelPlanetSettings
            {
                CellsPerChunk = authoring.cellsPerChunk,
                LayersPerChunk = authoring.layersPerChunk,
                ViewDistance = authoring.viewDistance,
                OceanLevel = authoring.oceanLevel,
                PlanetRadius = authoring.planetRadius,
                Seed = authoring.seed
            });
        }
    }
}
