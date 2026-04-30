using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace VoxelPlanet
{
    [UpdateAfter(typeof(GoldbergVoxelGenerationSystem))]
    public partial class GoldbergChunkStreamingSystem : SystemBase
    {
        private const float LoadDistance = 100f;
        private const float UnloadDistance = LoadDistance + 50f;

        protected override void OnCreate()
        {
            RequireForUpdate<GoldbergVoxelChunk>();
        }

        protected override void OnUpdate()
        {
            EntityManager entityManager = EntityManager;

            if (!PlanetPlayerPositionBridge.HasPlayer)
                return;

            float3 playerPosition = PlanetPlayerPositionBridge.Position;

            Debug.Log($"Player position: {playerPosition}");

            Entities
                .WithAll<GoldbergVoxelChunk>()
                .WithStructuralChanges()
                .ForEach((Entity chunkEntity, ref GoldbergVoxelChunk chunk) =>
                {
                    float distance = math.distance(playerPosition, chunk.Center);

                    Debug.Log($"Player position: {playerPosition}");

                    Debug.Log(
                        $"Chunk {chunk.ChunkIndex} center: {chunk.Center}, distance: {distance}"
                    );

                    if (distance <= LoadDistance)
                    {
                        if (entityManager.HasComponent<DisableRendering>(chunkEntity))
                        {
                            entityManager.RemoveComponent<DisableRendering>(chunkEntity);
                        }

                        if (chunk.IsMeshBuilt == 1)
                        {
                            if (GoldbergChunkMeshCache.TryGet(chunk.ChunkIndex, out Mesh cachedMesh))
                            {
                                GoldbergChunkColliderBridge.SetChunkCollider(chunk.ChunkIndex, cachedMesh);
                            }
                            else
                            {
                                chunk.IsMeshBuilt = 0;
                            }

                            chunk.NeedsMeshBuild = 0;

                            if (entityManager.HasComponent<GoldbergVoxelChunkNeedsMeshBuild>(chunkEntity))
                            {
                                entityManager.RemoveComponent<GoldbergVoxelChunkNeedsMeshBuild>(chunkEntity);
                            }

                            return;
                        }

                        if (chunk.IsMeshBuilt == 0 && chunk.NeedsMeshBuild == 0)
                        {
                            chunk.NeedsMeshBuild = 1;

                            if (!entityManager.HasComponent<GoldbergVoxelChunkNeedsMeshBuild>(chunkEntity))
                            {
                                entityManager.AddComponent<GoldbergVoxelChunkNeedsMeshBuild>(chunkEntity);
                            }

                            Debug.Log($"[STREAMING] Queue build chunk {chunk.ChunkIndex}, distance: {distance}");
                        }
                    }
                    else if (distance >= UnloadDistance)
                    {
                        if (chunk.IsMeshBuilt == 1)
                        {
                            if (!entityManager.HasComponent<DisableRendering>(chunkEntity))
                            {
                                entityManager.AddComponent<DisableRendering>(chunkEntity);
                            }

                            GoldbergChunkColliderBridge.ClearChunkCollider(chunk.ChunkIndex);

                            // IMPORTANT:
                            // Keep IsMeshBuilt = 1 because the mesh is cached.
                            chunk.NeedsMeshBuild = 0;

                            if (entityManager.HasComponent<GoldbergVoxelChunkNeedsMeshBuild>(chunkEntity))
                            {
                                entityManager.RemoveComponent<GoldbergVoxelChunkNeedsMeshBuild>(chunkEntity);
                            }

                            Debug.Log($"[STREAMING] Hide cached chunk {chunk.ChunkIndex}, distance: {distance}");
                        }
                    }
                })
                .WithoutBurst()
                .Run();
        }
    }
}