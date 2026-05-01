using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Unity.Transforms;

namespace VoxelPlanet
{
    [UpdateAfter(typeof(GoldbergVoxelNeighbourSystem))]
    public partial class GoldbergVoxelGenerationSystem : SystemBase
    {
        private const int ChunksPerCubeFace = 4;

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

            using NativeArray<Entity> planets =
                query.ToEntityArray(Allocator.Temp);

            EntityCommandBuffer ecb =
                new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < planets.Length; i++)
            {
                Entity planetEntity = planets[i];

                GoldbergPlanetSettings settings =
                    entityManager.GetComponentData<GoldbergPlanetSettings>(planetEntity);

                if (settings.NeedsColumnGeneration == 0)
                    continue;

                if (entityManager.HasComponent<GoldbergVoxelChunksCreated>(planetEntity))
                    continue;

                DynamicBuffer<GoldbergCell> cells =
                    entityManager.GetBuffer<GoldbergCell>(planetEntity);

                DynamicBuffer<VoxelColumn> columns =
                    entityManager.GetBuffer<VoxelColumn>(planetEntity);

                columns.Clear();

                Dictionary<int, List<int>> chunkToColumns =
                    new Dictionary<int, List<int>>();

                for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
                {
                    GoldbergCell cell = cells[cellIndex];

                    float3 normal = math.normalize(cell.Normal);

                    float heightNoise =
                        noise.cnoise(new float3(
                            normal.x * 2.5f + settings.Seed * 0.001f,
                            normal.y * 2.5f,
                            normal.z * 2.5f + settings.Seed * 0.001f
                        ));

                    int middleLayer = settings.Layers / 2;

                    int surfaceLayer =
                        middleLayer + (int)math.round(heightNoise * 6f);

                    surfaceLayer = math.clamp(
                        surfaceLayer,
                        1,
                        settings.Layers - 2
                    );

                    int columnIndex = columns.Length;

                    columns.Add(new VoxelColumn
                    {
                        CellIndex = cellIndex,
                        SurfaceLayer = surfaceLayer
                    });

                    int chunkIndex = GetSpatialChunkIndex(normal);

                    if (!chunkToColumns.TryGetValue(chunkIndex, out List<int> list))
                    {
                        list = new List<int>();
                        chunkToColumns[chunkIndex] = list;
                    }

                    list.Add(columnIndex);
                }

                foreach (KeyValuePair<int, List<int>> pair in chunkToColumns)
                {
                    Entity chunkEntity = ecb.CreateEntity();

                    float3 chunkCenter = float3.zero;
                    int count = 0;

                    for (int c = 0; c < pair.Value.Count; c++)
                    {
                        int columnIndex = pair.Value[c];

                        if (columnIndex < 0 || columnIndex >= columns.Length)
                            continue;

                        int cellIndex = columns[columnIndex].CellIndex;

                        if (cellIndex < 0 || cellIndex >= cells.Length)
                            continue;

                        chunkCenter += cells[cellIndex].Center;
                        count++;
                    }

                    if (count > 0)
                    {
                        chunkCenter /= count;
                    }

                    ecb.AddComponent(chunkEntity, LocalTransform.Identity);
                    ecb.AddComponent(chunkEntity, new LocalToWorld
                    {
                        Value = float4x4.identity
                    });

                    ecb.AddComponent(chunkEntity, new GoldbergVoxelChunk
                    {
                        PlanetEntity = planetEntity,
                        ChunkIndex = pair.Key,
                        NeedsMeshBuild = 0,
                        IsMeshBuilt = 0,
                        Center = chunkCenter
                    });

                    DynamicBuffer<GoldbergChunkColumn> chunkColumns =
                        ecb.AddBuffer<GoldbergChunkColumn>(chunkEntity);

                    for (int c = 0; c < pair.Value.Count; c++)
                    {
                        chunkColumns.Add(new GoldbergChunkColumn
                        {
                            ColumnIndex = pair.Value[c]
                        });
                    }
                }

                settings.NeedsColumnGeneration = 0;
                settings.NeedsVoxelMeshBuild = 1;

                entityManager.SetComponentData(planetEntity, settings);

                ecb.AddComponent<GoldbergVoxelChunksCreated>(planetEntity);

                /*Debug.Log(
                    $"Generated Goldberg voxel columns: {columns.Length}. Created chunks: {chunkToColumns.Count}"
                );*/
            }

            ecb.Playback(entityManager);
            ecb.Dispose();
        }

        private int GetSpatialChunkIndex(float3 normal)
        {
            float ax = math.abs(normal.x);
            float ay = math.abs(normal.y);
            float az = math.abs(normal.z);

            int face;
            float u;
            float v;

            if (ax >= ay && ax >= az)
            {
                if (normal.x >= 0f)
                {
                    face = 0;
                    u = -normal.z / ax;
                    v = normal.y / ax;
                }
                else
                {
                    face = 1;
                    u = normal.z / ax;
                    v = normal.y / ax;
                }
            }
            else if (ay >= ax && ay >= az)
            {
                if (normal.y >= 0f)
                {
                    face = 2;
                    u = normal.x / ay;
                    v = -normal.z / ay;
                }
                else
                {
                    face = 3;
                    u = normal.x / ay;
                    v = normal.z / ay;
                }
            }
            else
            {
                if (normal.z >= 0f)
                {
                    face = 4;
                    u = normal.x / az;
                    v = normal.y / az;
                }
                else
                {
                    face = 5;
                    u = -normal.x / az;
                    v = normal.y / az;
                }
            }

            int x = Mathf.Clamp(
                Mathf.FloorToInt((u * 0.5f + 0.5f) * ChunksPerCubeFace),
                0,
                ChunksPerCubeFace - 1
            );

            int y = Mathf.Clamp(
                Mathf.FloorToInt((v * 0.5f + 0.5f) * ChunksPerCubeFace),
                0,
                ChunksPerCubeFace - 1
            );

            return face * ChunksPerCubeFace * ChunksPerCubeFace +
                   y * ChunksPerCubeFace +
                   x;
        }
    }
}
