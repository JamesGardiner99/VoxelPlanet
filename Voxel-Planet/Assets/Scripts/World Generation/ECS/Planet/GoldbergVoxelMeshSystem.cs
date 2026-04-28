using System.Collections.Generic;
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

        private readonly Dictionary<Entity, NativeParallelMultiHashMap<EdgeKey, int>>
            edgeLookupCache = new();

        private readonly Dictionary<Entity, NativeParallelMultiHashMap<VertexKey, int>>
            vertexLookupCache = new();

        protected override void OnCreate()
        {
            RequireForUpdate<GoldbergVoxelChunk>();

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            grassMaterial = new Material(shader);
            grassMaterial.name = "Goldberg Voxel Grass";
            grassMaterial.color = Color.green;
        }

        protected override void OnDestroy()
        {
            foreach (NativeParallelMultiHashMap<EdgeKey, int> lookup in edgeLookupCache.Values)
            {
                if (lookup.IsCreated)
                    lookup.Dispose();
            }

            edgeLookupCache.Clear();

            foreach (NativeParallelMultiHashMap<VertexKey, int> lookup in vertexLookupCache.Values)
            {
                if (lookup.IsCreated)
                    lookup.Dispose();
            }

            vertexLookupCache.Clear();
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

                NativeParallelMultiHashMap<EdgeKey, int> edgeToCells =
                    GetOrCreateEdgeLookup(
                        chunk.PlanetEntity,
                        cells,
                        cellVertices
                    );

                NativeParallelMultiHashMap<VertexKey, int> vertexToCells =
                    GetOrCreateVertexLookup(
                        chunk.PlanetEntity,
                        cells,
                        cellVertices
                    );

                Mesh mesh = BuildVoxelChunkMesh(
                    settings,
                    cells,
                    cellVertices,
                    columns,
                    chunkColumns,
                    edgeToCells,
                    vertexToCells,
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

        private void RemoveOldPlanetRenderComponents(
            EntityManager entityManager,
            Entity planetEntity)
        {
            if (entityManager.HasComponent<MaterialMeshInfo>(planetEntity))
                entityManager.RemoveComponent<MaterialMeshInfo>(planetEntity);

            if (entityManager.HasComponent<RenderBounds>(planetEntity))
                entityManager.RemoveComponent<RenderBounds>(planetEntity);

            if (entityManager.HasComponent<WorldRenderBounds>(planetEntity))
                entityManager.RemoveComponent<WorldRenderBounds>(planetEntity);
        }

        private NativeParallelMultiHashMap<EdgeKey, int> GetOrCreateEdgeLookup(
            Entity planetEntity,
            DynamicBuffer<GoldbergCell> cells,
            DynamicBuffer<GoldbergCellVertex> cellVertices)
        {
            if (edgeLookupCache.TryGetValue(
                    planetEntity,
                    out NativeParallelMultiHashMap<EdgeKey, int> cachedLookup))
            {
                if (cachedLookup.IsCreated)
                    return cachedLookup;
            }

            NativeParallelMultiHashMap<EdgeKey, int> edgeToCells =
                new NativeParallelMultiHashMap<EdgeKey, int>(
                    cells.Length * 6,
                    Allocator.Persistent
                );

            for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                GoldbergCell cell = cells[cellIndex];

                for (int edgeIndex = 0; edgeIndex < cell.VertexCount; edgeIndex++)
                {
                    int next = (edgeIndex + 1) % cell.VertexCount;

                    float3 a =
                        cellVertices[cell.FirstVertexIndex + edgeIndex].Position;

                    float3 b =
                        cellVertices[cell.FirstVertexIndex + next].Position;

                    edgeToCells.Add(new EdgeKey(a, b), cellIndex);
                }
            }

            edgeLookupCache[planetEntity] = edgeToCells;

            Debug.Log($"Cached Goldberg edge lookup. Cells: {cells.Length}");

            return edgeToCells;
        }

        private NativeParallelMultiHashMap<VertexKey, int> GetOrCreateVertexLookup(
            Entity planetEntity,
            DynamicBuffer<GoldbergCell> cells,
            DynamicBuffer<GoldbergCellVertex> cellVertices)
        {
            if (vertexLookupCache.TryGetValue(
                    planetEntity,
                    out NativeParallelMultiHashMap<VertexKey, int> cachedLookup))
            {
                if (cachedLookup.IsCreated)
                    return cachedLookup;
            }

            NativeParallelMultiHashMap<VertexKey, int> vertexToCells =
                new NativeParallelMultiHashMap<VertexKey, int>(
                    cells.Length * 6,
                    Allocator.Persistent
                );

            for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                GoldbergCell cell = cells[cellIndex];

                for (int i = 0; i < cell.VertexCount; i++)
                {
                    float3 p =
                        cellVertices[cell.FirstVertexIndex + i].Position;

                    vertexToCells.Add(new VertexKey(p), cellIndex);
                }
            }

            vertexLookupCache[planetEntity] = vertexToCells;

            Debug.Log($"Cached Goldberg vertex lookup. Cells: {cells.Length}");

            return vertexToCells;
        }

        private Mesh BuildVoxelChunkMesh(
            GoldbergPlanetSettings settings,
            DynamicBuffer<GoldbergCell> cells,
            DynamicBuffer<GoldbergCellVertex> cellVertices,
            DynamicBuffer<VoxelColumn> columns,
            DynamicBuffer<GoldbergChunkColumn> chunkColumns,
            NativeParallelMultiHashMap<EdgeKey, int> edgeToCells,
            NativeParallelMultiHashMap<VertexKey, int> vertexToCells,
            int chunkIndex)
        {
            NativeArray<GoldbergCell> cellsArray =
                cells.ToNativeArray(Allocator.TempJob);

            NativeArray<GoldbergCellVertex> cellVerticesArray =
                cellVertices.ToNativeArray(Allocator.TempJob);

            NativeArray<VoxelColumn> columnsArray =
                columns.ToNativeArray(Allocator.TempJob);

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

            NativeReference<int> debugEdgesChecked =
                new NativeReference<int>(Allocator.TempJob);

            NativeReference<int> debugNeighboursFound =
                new NativeReference<int>(Allocator.TempJob);

            NativeReference<int> debugDifferentHeightEdges =
                new NativeReference<int>(Allocator.TempJob);

            NativeReference<int> debugWallsAdded =
                new NativeReference<int>(Allocator.TempJob);

            GoldbergVoxelMeshBuildJob job = new GoldbergVoxelMeshBuildJob
            {
                Settings = settings,

                Cells = cellsArray,
                CellVertices = cellVerticesArray,
                Columns = columnsArray,
                ChunkColumnIndices = chunkColumnIndices,

                EdgeToCells = edgeToCells,
                VertexToCells = vertexToCells,

                Vertices = meshVertices,
                Triangles = meshTriangles,

                DebugEdgesChecked = debugEdgesChecked,
                DebugNeighboursFound = debugNeighboursFound,
                DebugDifferentHeightEdges = debugDifferentHeightEdges,
                DebugWallsAdded = debugWallsAdded
            };

            JobHandle handle = job.Schedule();
            handle.Complete();

            Debug.Log(
                $"Chunk debug {chunkIndex}: " +
                $"columns={chunkColumnIndices.Length}, " +
                $"edges={debugEdgesChecked.Value}, " +
                $"neighbours={debugNeighboursFound.Value}, " +
                $"heightDiffs={debugDifferentHeightEdges.Value}, " +
                $"walls={debugWallsAdded.Value}"
            );

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

            debugWallsAdded.Dispose();
            debugDifferentHeightEdges.Dispose();
            debugNeighboursFound.Dispose();
            debugEdgesChecked.Dispose();

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