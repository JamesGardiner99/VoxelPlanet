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
        private const float LoadDistance = 115f;
        private const float PreloadMultiplier = 1.4f;
        private const float UnloadMultiplier = 1.9f;

        private const float PreloadDistance = LoadDistance * PreloadMultiplier;
        private const float UnloadDistance = LoadDistance * UnloadMultiplier;

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

// 1. PRELOAD / BUILD CACHE
if (distance <= PreloadDistance)
{
    if (chunk.IsMeshBuilt == 0 && chunk.NeedsMeshBuild == 0)
    {
        chunk.NeedsMeshBuild = 1;

        if (!entityManager.HasComponent<GoldbergVoxelChunkNeedsMeshBuild>(chunkEntity))
        {
            entityManager.AddComponent<GoldbergVoxelChunkNeedsMeshBuild>(chunkEntity);
        }

        Debug.Log($"[STREAMING] Preload chunk {chunk.ChunkIndex}, distance: {distance}");
    }
}

                    // 2. SHOW
                    if (distance <= LoadDistance)
                    {
                        if (entityManager.HasComponent<DisableRendering>(chunkEntity))
                        {
                            entityManager.RemoveComponent<DisableRendering>(chunkEntity);
                        }

                        if (chunk.IsMeshBuilt == 1 &&
                            GoldbergChunkMeshCache.TryGet(chunk.ChunkIndex, out Mesh cachedMesh))
                        {
                            GoldbergChunkColliderBridge.SetChunkCollider(chunk.ChunkIndex, cachedMesh);
                        }
                    }

                    // 3. HIDE
                    else if (distance >= UnloadDistance)
                    {
                        if (chunk.IsMeshBuilt == 1)
                        {
                            if (!entityManager.HasComponent<DisableRendering>(chunkEntity))
                            {
                                entityManager.AddComponent<DisableRendering>(chunkEntity);
                            }

                            GoldbergChunkColliderBridge.ClearChunkCollider(chunk.ChunkIndex);

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