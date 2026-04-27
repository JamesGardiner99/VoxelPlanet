using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace VoxelPlanet
{
    [BurstCompile]
    public partial struct VoxelChunkGenerationSystem : ISystem
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

            int size = settings.CellsPerChunk;
            int layers = settings.LayersPerChunk;
            int expectedBlockCount = size * size * layers;

            foreach (var (chunk, blocks) in
                     SystemAPI.Query<RefRW<VoxelChunk>, DynamicBuffer<VoxelBlock>>())
            {
                if (chunk.ValueRO.NeedsGeneration == 0)
                    continue;

                // Safety guard: make sure the buffer is the correct size
                if (blocks.Length != expectedBlockCount)
                {
                    blocks.ResizeUninitialized(expectedBlockCount);
                }

                for (int z = 0; z < size; z++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int worldX = chunk.ValueRO.Coord.x * size + x;
                        int worldZ = chunk.ValueRO.Coord.y * size + z;

                        float heightNoise = noise.cnoise(
                            new float2(worldX, worldZ) * 0.05f
                        );

                        int surfaceLayer = (int)math.round(heightNoise * 8f);

                        for (int y = 0; y < layers; y++)
                        {
                            int layer = y - layers / 2;

                            // Correct flat array index:
                            // x range: 0 to size - 1
                            // z range: 0 to size - 1
                            // y range: 0 to layers - 1
                            int index = x + (z * size) + (y * size * size);

                            BlockType block = BlockType.Air;

                            if (layer == surfaceLayer)
                            {
                                block = surfaceLayer < settings.OceanLevel
                                    ? BlockType.Dirt
                                    : BlockType.Grass;
                            }
                            else if (layer < surfaceLayer && layer >= surfaceLayer - 2)
                            {
                                block = BlockType.Dirt;
                            }
                            else if (layer < surfaceLayer)
                            {
                                block = BlockType.Stone;
                            }
                            else if (layer <= settings.OceanLevel)
                            {
                                block = BlockType.Water;
                            }

                            blocks.ElementAt(index).Value = (byte)block;
                        }
                    }
                }

                chunk.ValueRW.NeedsGeneration = 0;
                chunk.ValueRW.NeedsMeshBuild = 1;
            }
        }
    }
}