using Unity.Entities;
using UnityEngine;

namespace VoxelPlanet
{
    public class GoldbergPlanetAuthoring : MonoBehaviour
    {
        [Header("Planet Shape")]
        public float Radius;
        public float CellHeight = 1f;
        public int Subdivisions = 4;
        public int Layers = 16;

        [Header("Voxel Generation")]
        public int OceanLevel = 8;
        public uint Seed = 12345;

        [Header("Build Flags")]
        public bool GenerateOnStart = true;

        private class Baker : Baker<GoldbergPlanetAuthoring>
        {
            public override void Bake(GoldbergPlanetAuthoring authoring)
            {
                AddComponent<PlanetPlayerTag>(GetEntity(TransformUsageFlags.Dynamic));
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new GoldbergPlanetSettings
                {
                    Radius = authoring.Radius,
                    CellHeight = authoring.CellHeight,
                    Subdivisions = authoring.Subdivisions,
                    Layers = authoring.Layers,
                    OceanLevel = authoring.OceanLevel,
                    Seed = authoring.Seed,

                    NeedsGoldbergBuild = authoring.GenerateOnStart ? (byte)1 : (byte)0,
                    NeedsColumnGeneration = 0,
                    NeedsVoxelMeshBuild = 0
                });

                AddComponent<GoldbergPlanetTag>(entity);

                AddBuffer<GoldbergCell>(entity);
                AddBuffer<GoldbergCellVertex>(entity);
                AddBuffer<VoxelColumn>(entity);
                AddBuffer<GoldbergCellNeighbour>(entity);
                AddBuffer<GoldbergCellNeighbourLookup>(entity);
            }
        }
    }
}