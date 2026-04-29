using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelPlanet
{
    [UpdateAfter(typeof(GoldbergVoxelGenerationSystem))]
    public partial class GoldbergVoxelMeshSystem : SystemBase
    {
        private const int MaxChunkBuildsPerFrame = 4;

        private Material grassMaterial;

        protected override void OnCreate()
        {
            RequireForUpdate<GoldbergVoxelChunkNeedsMeshBuild>();

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            grassMaterial = new Material(shader);
            grassMaterial.name = "Goldberg Voxel Grass";
            grassMaterial.color = Color.green;
        }

        protected override void OnUpdate()
        {
            EntityManager entityManager = EntityManager;

            EntityQuery query = GetEntityQuery(
                ComponentType.ReadWrite<GoldbergVoxelChunk>(),
                ComponentType.ReadOnly<GoldbergVoxelChunkNeedsMeshBuild>()
            );

            using NativeArray<Entity> chunkEntities =
                query.ToEntityArray(Allocator.Temp);

            int builtThisFrame = 0;

            for (int i = 0; i < chunkEntities.Length; i++)
            {
                Entity chunkEntity = chunkEntities[i];

                GoldbergVoxelChunk chunk =
                    entityManager.GetComponentData<GoldbergVoxelChunk>(chunkEntity);

                if (chunk.NeedsMeshBuild == 0)
                {
                    entityManager.RemoveComponent<GoldbergVoxelChunkNeedsMeshBuild>(chunkEntity);
                    continue;
                }

                if (!entityManager.Exists(chunk.PlanetEntity))
                {
                    entityManager.RemoveComponent<GoldbergVoxelChunkNeedsMeshBuild>(chunkEntity);
                    continue;
                }

                GoldbergPlanetSettings settings =
                    entityManager.GetComponentData<GoldbergPlanetSettings>(chunk.PlanetEntity);

                DynamicBuffer<GoldbergCell> cells =
                    entityManager.GetBuffer<GoldbergCell>(chunk.PlanetEntity);

                DynamicBuffer<GoldbergCellVertex> cellVertices =
                    entityManager.GetBuffer<GoldbergCellVertex>(chunk.PlanetEntity);

                DynamicBuffer<VoxelColumn> columns =
                    entityManager.GetBuffer<VoxelColumn>(chunk.PlanetEntity);

                if (!entityManager.HasBuffer<GoldbergCellNeighbourLookup>(chunk.PlanetEntity))
                    continue;

                DynamicBuffer<GoldbergCellNeighbourLookup> neighbourLookup =
                    entityManager.GetBuffer<GoldbergCellNeighbourLookup>(chunk.PlanetEntity);

                DynamicBuffer<GoldbergChunkColumn> chunkColumns =
                    entityManager.GetBuffer<GoldbergChunkColumn>(chunkEntity);

                int chunkColumnCount = chunkColumns.Length;

                Mesh mesh = BuildVoxelChunkMesh(
                    settings,
                    cells,
                    cellVertices,
                    columns,
                    neighbourLookup,
                    chunkColumns,
                    chunk.ChunkIndex
                );

                GoldbergChunkColliderBridge.SetChunkCollider(chunk.ChunkIndex, mesh);

                int vertexCount = mesh.vertexCount;

                RenderMeshArray renderMeshArray = new RenderMeshArray(
                    new[] { grassMaterial },
                    new[] { mesh }
                );

                RenderMeshDescription desc = new RenderMeshDescription(
                    shadowCastingMode: ShadowCastingMode.On,
                    receiveShadows: true
                );

                if (!entityManager.HasComponent<LocalTransform>(chunkEntity))
                {
                    entityManager.AddComponentData(chunkEntity, LocalTransform.Identity);
                }

                if (!entityManager.HasComponent<MaterialMeshInfo>(chunkEntity))
                {
                    RenderMeshUtility.AddComponents(
                        chunkEntity,
                        entityManager,
                        desc,
                        renderMeshArray,
                        MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
                    );
                }
                else
                {
                    entityManager.SetSharedComponentManaged(chunkEntity, renderMeshArray);

                    entityManager.SetComponentData(
                        chunkEntity,
                        MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
                    );
                }

                chunk.NeedsMeshBuild = 0;
                entityManager.SetComponentData(chunkEntity, chunk);

                entityManager.RemoveComponent<GoldbergVoxelChunkNeedsMeshBuild>(chunkEntity);

                Debug.Log(
                    $"Goldberg spatial chunk {chunk.ChunkIndex} mesh built. Vertices: {vertexCount}, Columns: {chunkColumnCount}"
                );

                builtThisFrame++;

                if (builtThisFrame >= MaxChunkBuildsPerFrame)
                    break;
            }
        }

        private Mesh BuildVoxelChunkMesh(
            GoldbergPlanetSettings settings,
            DynamicBuffer<GoldbergCell> cells,
            DynamicBuffer<GoldbergCellVertex> cellVertices,
            DynamicBuffer<VoxelColumn> columns,
            DynamicBuffer<GoldbergCellNeighbourLookup> neighbourLookupBuffer,
            DynamicBuffer<GoldbergChunkColumn> chunkColumns,
            int chunkIndex)
        {
            NativeArray<GoldbergCell> cellsArray =
                new NativeArray<GoldbergCell>(cells.Length, Allocator.TempJob);
            cellsArray.CopyFrom(cells.AsNativeArray());

            NativeArray<GoldbergCellVertex> cellVerticesArray =
                new NativeArray<GoldbergCellVertex>(cellVertices.Length, Allocator.TempJob);
            cellVerticesArray.CopyFrom(cellVertices.AsNativeArray());

            NativeArray<VoxelColumn> columnsArray =
                new NativeArray<VoxelColumn>(columns.Length, Allocator.TempJob);
            columnsArray.CopyFrom(columns.AsNativeArray());

            NativeArray<int> chunkColumnIndices =
                new NativeArray<int>(chunkColumns.Length, Allocator.TempJob);

            for (int i = 0; i < chunkColumns.Length; i++)
            {
                chunkColumnIndices[i] = chunkColumns[i].ColumnIndex;
            }

            NativeArray<int> cellSurfaceLayers =
                new NativeArray<int>(cells.Length, Allocator.TempJob);

            for (int i = 0; i < cellSurfaceLayers.Length; i++)
            {
                cellSurfaceLayers[i] = -1;
            }

            for (int i = 0; i < columnsArray.Length; i++)
            {
                VoxelColumn column = columnsArray[i];

                if (column.CellIndex < 0 || column.CellIndex >= cellSurfaceLayers.Length)
                    continue;

                cellSurfaceLayers[column.CellIndex] = column.SurfaceLayer;
            }

            NativeArray<int> neighbourLookup =
                new NativeArray<int>(
                    neighbourLookupBuffer.Length,
                    Allocator.TempJob
                );

            for (int i = 0; i < neighbourLookupBuffer.Length; i++)
            {
                neighbourLookup[i] = neighbourLookupBuffer[i].NeighbourCellIndex;
            }

            NativeList<GoldbergMeshVertex> meshVertices =
                new NativeList<GoldbergMeshVertex>(
                    chunkColumns.Length * 64,
                    Allocator.TempJob
                );

            NativeList<int> meshTriangles =
                new NativeList<int>(
                    chunkColumns.Length * 128,
                    Allocator.TempJob
                );

            NativeParallelHashMap<GoldbergMeshVertexKey, int> vertexLookup =
                new NativeParallelHashMap<GoldbergMeshVertexKey, int>(
                    chunkColumns.Length * 64,
                    Allocator.TempJob
                );

            GoldbergVoxelMeshBuildJob job = new GoldbergVoxelMeshBuildJob
            {
                Settings = settings,

                Cells = cellsArray,
                CellVertices = cellVerticesArray,
                Columns = columnsArray,
                CellSurfaceLayers = cellSurfaceLayers,
                NeighbourLookup = neighbourLookup,
                MaxEdgesPerCell = GoldbergNeighbourConstants.MaxEdgesPerCell,
                ChunkColumnIndices = chunkColumnIndices,

                Vertices = meshVertices,
                Triangles = meshTriangles,
                VertexLookup = vertexLookup
            };

            JobHandle handle = job.Schedule();
            handle.Complete();

            Mesh mesh = new Mesh();
            mesh.name = $"Goldberg Voxel Spatial Chunk {chunkIndex}";
            mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertexBufferParams(
                meshVertices.Length,
                new VertexAttributeDescriptor(
                    VertexAttribute.Position,
                    VertexAttributeFormat.Float32,
                    3
                ),
                new VertexAttributeDescriptor(
                    VertexAttribute.Normal,
                    VertexAttributeFormat.Float32,
                    3
                )
            );

            mesh.SetVertexBufferData(
                meshVertices.AsArray(),
                0,
                0,
                meshVertices.Length
            );

            mesh.SetIndexBufferParams(
                meshTriangles.Length,
                mesh.indexFormat
            );

            mesh.SetIndexBufferData(
                meshTriangles.AsArray(),
                0,
                0,
                meshTriangles.Length
            );

            mesh.SetSubMesh(
                0,
                new SubMeshDescriptor(0, meshTriangles.Length)
            );

            mesh.RecalculateBounds();

            vertexLookup.Dispose();
            meshTriangles.Dispose();
            meshVertices.Dispose();
            neighbourLookup.Dispose();
            cellSurfaceLayers.Dispose();
            chunkColumnIndices.Dispose();
            columnsArray.Dispose();
            cellVerticesArray.Dispose();
            cellsArray.Dispose();

            return mesh;
        }
    }
}