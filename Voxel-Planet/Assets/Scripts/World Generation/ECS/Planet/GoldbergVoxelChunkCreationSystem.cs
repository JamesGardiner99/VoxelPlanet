using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace VoxelPlanet
{
    [UpdateAfter(typeof(GoldbergColumnGenerationSystem))]
    public partial class GoldbergVoxelChunkCreationSystem : SystemBase
    {
        private const int ColumnsPerChunk = 256;

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
                typeof(GoldbergCellVertex),
                typeof(VoxelColumn)
            );

            using NativeArray<Entity> planets =
                query.ToEntityArray(Allocator.Temp);

            EntityCommandBuffer ecb =
                new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < planets.Length; i++)
            {
                Entity planetEntity = planets[i];

                if (entityManager.HasComponent<GoldbergVoxelChunksCreated>(planetEntity))
                    continue;

                GoldbergPlanetSettings settings =
                    entityManager.GetComponentData<GoldbergPlanetSettings>(planetEntity);

                if (settings.NeedsVoxelMeshBuild == 0)
                    continue;

                DynamicBuffer<VoxelColumn> columns =
                    entityManager.GetBuffer<VoxelColumn>(planetEntity);

                int columnLength = columns.Length;

                if (columnLength == 0)
                    continue;

                int chunkCount =
                    Mathf.CeilToInt(columnLength / (float)ColumnsPerChunk);

                for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
                {
                    int startColumnIndex = chunkIndex * ColumnsPerChunk;
                    int remainingColumns = columnLength - startColumnIndex;

                    int columnCount =
                        Mathf.Min(ColumnsPerChunk, remainingColumns);

                    Entity chunkEntity = ecb.CreateEntity();

                    ecb.AddComponent(chunkEntity, new GoldbergVoxelChunk
                    {
                        PlanetEntity = planetEntity,
                        ChunkIndex = chunkIndex,
                        StartColumnIndex = startColumnIndex,
                        ColumnCount = columnCount,
                        NeedsMeshBuild = 1
                    });
                }

                ecb.AddComponent<GoldbergVoxelChunksCreated>(planetEntity);

                Debug.Log($"Created {chunkCount} Goldberg voxel chunks.");
            }

            ecb.Playback(entityManager);
            ecb.Dispose();
        }
    }
}