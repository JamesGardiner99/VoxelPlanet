using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelPlanet
{
    [UpdateAfter(typeof(GoldbergVoxelGenerationSystem))]
    public partial class GoldbergVoxelMeshSystem : SystemBase
    {
        private Material grassMaterial;

        protected override void OnCreate()
        {
            RequireForUpdate<GoldbergVoxelChunk>();

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            grassMaterial = new Material(shader);
            grassMaterial.name = "Goldberg Voxel Grass";
            grassMaterial.color = Color.green;
        }

        protected override void OnUpdate()
        {
            EntityManager entityManager = EntityManager;

            EntityQuery query = GetEntityQuery(typeof(GoldbergVoxelChunk));

            using NativeArray<Entity> chunkEntities =
                query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < chunkEntities.Length; i++)
            {
                Entity chunkEntity = chunkEntities[i];

                GoldbergVoxelChunk chunk =
                    entityManager.GetComponentData<GoldbergVoxelChunk>(chunkEntity);

                if (chunk.NeedsMeshBuild == 0)
                    continue;

                if (!entityManager.Exists(chunk.PlanetEntity))
                    continue;

                GoldbergPlanetSettings settings =
                    entityManager.GetComponentData<GoldbergPlanetSettings>(chunk.PlanetEntity);

                DynamicBuffer<GoldbergCell> cells =
                    entityManager.GetBuffer<GoldbergCell>(chunk.PlanetEntity);

                DynamicBuffer<GoldbergCellVertex> cellVertices =
                    entityManager.GetBuffer<GoldbergCellVertex>(chunk.PlanetEntity);

                DynamicBuffer<VoxelColumn> columns =
                    entityManager.GetBuffer<VoxelColumn>(chunk.PlanetEntity);

                DynamicBuffer<GoldbergChunkColumn> chunkColumns =
                    entityManager.GetBuffer<GoldbergChunkColumn>(chunkEntity);

                int chunkColumnCount = chunkColumns.Length;

                Mesh mesh = BuildVoxelChunkMesh(
                    settings,
                    cells,
                    cellVertices,
                    columns,
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

                Debug.Log(
                    $"Goldberg spatial chunk {chunk.ChunkIndex} mesh built. Vertices: {vertexCount}, Columns: {chunkColumnCount}"
                );

                return;
            }
        }

        private Mesh BuildVoxelChunkMesh(
            GoldbergPlanetSettings settings,
            DynamicBuffer<GoldbergCell> cells,
            DynamicBuffer<GoldbergCellVertex> cellVertices,
            DynamicBuffer<VoxelColumn> columns,
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

            NativeList<GoldbergMeshVertex> meshVertices =
                new NativeList<GoldbergMeshVertex>(
                    chunkColumns.Length * 96,
                    Allocator.TempJob
                );

            NativeList<int> meshTriangles =
                new NativeList<int>(
                    chunkColumns.Length * 192,
                    Allocator.TempJob
                );

            GoldbergVoxelMeshBuildJob job = new GoldbergVoxelMeshBuildJob
            {
                Settings = settings,

                Cells = cellsArray,
                CellVertices = cellVerticesArray,
                Columns = columnsArray,
                ChunkColumnIndices = chunkColumnIndices,

                Vertices = meshVertices,
                Triangles = meshTriangles
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

            meshTriangles.Dispose();
            meshVertices.Dispose();
            chunkColumnIndices.Dispose();
            columnsArray.Dispose();
            cellVerticesArray.Dispose();
            cellsArray.Dispose();

            return mesh;
        }
    }
}