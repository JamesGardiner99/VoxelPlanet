using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace VoxelPlanet
{
    public partial class GoldbergColumnGenerationSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<GoldbergPlanetSettings>();
        }

        protected override void OnUpdate()
        {
            EntityManager entityManager = EntityManager;

            EntityQuery query = GetEntityQuery(
                typeof(GoldbergPlanetSettings),
                typeof(GoldbergCell),
                typeof(VoxelColumn)
            );

            using Unity.Collections.NativeArray<Entity> entities =
                query.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int e = 0; e < entities.Length; e++)
            {
                Entity entity = entities[e];

                GoldbergPlanetSettings settings =
                    entityManager.GetComponentData<GoldbergPlanetSettings>(entity);

                if (settings.NeedsColumnGeneration == 0)
                    continue;

                DynamicBuffer<GoldbergCell> cells =
                    entityManager.GetBuffer<GoldbergCell>(entity);

                DynamicBuffer<VoxelColumn> columns =
                    entityManager.GetBuffer<VoxelColumn>(entity);

                columns.Clear();

                for (int i = 0; i < cells.Length; i++)
                {
                    GoldbergCell cell = cells[i];

                    float3 n = math.normalize(cell.Normal);

                    float heightNoise =
                        noise.cnoise(new float3(
                            n.x * 2.5f + settings.Seed * 0.001f,
                            n.y * 2.5f,
                            n.z * 2.5f + settings.Seed * 0.001f
                        ));

                    int middleLayer = settings.Layers / 2;

                    int surfaceLayer =
                        middleLayer + (int)math.round(heightNoise * 6f);

                    surfaceLayer = math.clamp(
                        surfaceLayer,
                        1,
                        settings.Layers - 2
                    );

                    columns.Add(new VoxelColumn
                    {
                        CellIndex = i,
                        SurfaceLayer = surfaceLayer
                    });
                }

                settings.NeedsColumnGeneration = 0;
                settings.NeedsVoxelMeshBuild = 1;

                entityManager.SetComponentData(entity, settings);

                Debug.Log($"Generated Goldberg voxel columns: {columns.Length}");
            }
        }
    }
}