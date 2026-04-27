using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace VoxelPlanet
{
    [BurstCompile]
    public partial struct VoxelChunkRequestSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VoxelPlanetSettings>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            VoxelPlanetSettings settings =
                SystemAPI.GetSingleton<VoxelPlanetSettings>();

            EntityCommandBuffer ecb =
                new EntityCommandBuffer(Allocator.Temp);

            int view = settings.ViewDistance;

            for (int x = -view; x <= view; x++)
            {
                for (int z = -view; z <= view; z++)
                {
                    Entity chunkEntity = ecb.CreateEntity();

                    ecb.AddComponent(chunkEntity, new FlatVoxelChunk
                    {
                        Coord = new int2(x, z),
                        NeedsGeneration = 1,
                        NeedsMeshBuild = 0
                    });

                    DynamicBuffer<VoxelBlock> blocks =
                        ecb.AddBuffer<VoxelBlock>(chunkEntity);

                    int blockCount =
                        settings.CellsPerChunk *
                        settings.CellsPerChunk *
                        settings.LayersPerChunk;

                    blocks.ResizeUninitialized(blockCount);
                }
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            state.Enabled = false;
        }
    }
}