using Unity.Entities;
using UnityEngine;

namespace VoxelPlanet
{
    public class GoldbergPlanetAuthoring : MonoBehaviour
    {
        [Header("Goldberg Planet")]
        public float radius = 128f;
        public float cellHeight = 1f;

        [Range(0, 4)]
        public int subdivisions = 4;

        public int layers = 32;
        public int oceanLevel = 0;
        public uint seed = 12345;
    }

    public class GoldbergPlanetBaker : Baker<GoldbergPlanetAuthoring>
    {
        public override void Bake(GoldbergPlanetAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<GoldbergPlanetTag>(entity);

            AddComponent(entity, new GoldbergPlanetSettings
            {
                Radius = authoring.radius,
                CellHeight = authoring.cellHeight,
                Subdivisions = authoring.subdivisions,
                Layers = authoring.layers,
                OceanLevel = authoring.oceanLevel,
                Seed = authoring.seed,
                NeedsBuild = 1,
                NeedsColumnGeneration = 0,
                NeedsVoxelMeshBuild = 0
            });

            AddBuffer<GoldbergCell>(entity);
            AddBuffer<GoldbergCellVertex>(entity);
            AddBuffer<VoxelColumn>(entity);
        }
    }
}