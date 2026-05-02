using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Rendering;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace VoxelPlanet
{
    [UpdateAfter(typeof(GoldbergVoxelGenerationSystem))]
    public partial class GoldbergVoxelMeshSystem : SystemBase
    {
        private const int MaxChunkBuildsPerFrame = 1;
        private const int MaxColliderUpdatesPerFrame = 1;

        private readonly Queue<PendingChunkCollider> pendingColliders = new();
        private readonly List<PendingChunkBuild> pendingBuilds = new();

        private struct PendingChunkCollider
        {
            public Entity ChunkEntity;
            public int ChunkIndex;
            public Mesh ColliderMesh;
        }

        private struct PendingChunkBuild
        {
            public Entity ChunkEntity;
            public int ChunkIndex;
            public Mesh ColliderMesh;

            public JobHandle Handle;

            public NativeList<GoldbergMeshVertex> Vertices;
            public NativeList<int> Triangles;
            public NativeParallelHashMap<GoldbergMeshVertexKey, int> VertexLookup;

            public NativeArray<GoldbergCell> Cells;
            public NativeArray<GoldbergCellVertex> CellVertices;
            public NativeArray<VoxelColumn> Columns;
            public NativeArray<int> CellSurfaceLayers;
            public NativeArray<int> NeighbourLookup;
            public NativeArray<int> ChunkColumnIndices;
        }

        private struct Edge
        {
            public Vector3 A;
            public Vector3 B;
        }

        private Material grassMaterial;

        protected override void OnCreate()
        {
            RequireForUpdate<GoldbergVoxelChunk>();

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            grassMaterial = new Material(shader);
            grassMaterial.name = "Goldberg Voxel Grass";
            grassMaterial.color = Color.green;
            grassMaterial.enableInstancing = true;
        }

        protected override void OnUpdate()
        {
            EntityManager entityManager = EntityManager;

            CompletePendingBuilds(entityManager);
            ProcessPendingColliders(entityManager);

            EntityQuery query = GetEntityQuery(
                ComponentType.ReadWrite<GoldbergVoxelChunk>(),
                ComponentType.ReadOnly<GoldbergVoxelChunkNeedsMeshBuild>()
            );

            using NativeArray<Entity> chunkEntities =
                query.ToEntityArray(Allocator.Temp);

            int scheduledThisFrame = 0;

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

                if (IsChunkAlreadyPending(chunkEntity))
                    continue;

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

                DrawRegionBoundaryDebug(
                    cells,
                    cellVertices,
                    columns,
                    neighbourLookup,
                    chunkColumns
                );

                ScheduleVoxelChunkMeshBuild(
                    chunkEntity,
                    chunk,
                    settings,
                    cells,
                    cellVertices,
                    columns,
                    neighbourLookup,
                    chunkColumns
                );

                scheduledThisFrame++;

                if (scheduledThisFrame >= MaxChunkBuildsPerFrame)
                    break;
            }
        }

        private bool IsChunkAlreadyPending(Entity chunkEntity)
        {
            for (int i = 0; i < pendingBuilds.Count; i++)
            {
                if (pendingBuilds[i].ChunkEntity == chunkEntity)
                    return true;
            }

            return false;
        }

        private void ScheduleVoxelChunkMeshBuild(
            Entity chunkEntity,
            GoldbergVoxelChunk chunk,
            GoldbergPlanetSettings settings,
            DynamicBuffer<GoldbergCell> cells,
            DynamicBuffer<GoldbergCellVertex> cellVertices,
            DynamicBuffer<VoxelColumn> columns,
            DynamicBuffer<GoldbergCellNeighbourLookup> neighbourLookupBuffer,
            DynamicBuffer<GoldbergChunkColumn> chunkColumns)
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

            AddMergedTopFaces(
                settings,
                cells,
                cellVertices,
                columns,
                neighbourLookupBuffer,
                chunkColumns,
                meshVertices,
                meshTriangles,
                vertexLookup
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

            pendingBuilds.Add(new PendingChunkBuild
            {
                ChunkEntity = chunkEntity,
                ChunkIndex = chunk.ChunkIndex,
                Handle = handle,

                Vertices = meshVertices,
                Triangles = meshTriangles,
                VertexLookup = vertexLookup,

                Cells = cellsArray,
                CellVertices = cellVerticesArray,
                Columns = columnsArray,
                CellSurfaceLayers = cellSurfaceLayers,
                NeighbourLookup = neighbourLookup,
                ChunkColumnIndices = chunkColumnIndices
            });
        }

        private void CompletePendingBuilds(EntityManager entityManager)
        {
            for (int i = pendingBuilds.Count - 1; i >= 0; i--)
            {
                PendingChunkBuild pending = pendingBuilds[i];

                if (!pending.Handle.IsCompleted)
                    continue;

                pending.Handle.Complete();

                if (!entityManager.Exists(pending.ChunkEntity))
                {
                    DisposePendingBuild(pending);
                    pendingBuilds.RemoveAt(i);
                    continue;
                }

                Mesh mesh = new Mesh();
                Mesh colliderMesh = BuildColliderMesh(
                    pending.Vertices,
                    pending.Triangles,
                    pending.ChunkIndex
                );
                mesh.name = $"Goldberg Voxel Spatial Chunk {pending.ChunkIndex}";
                mesh.indexFormat = IndexFormat.UInt32;

                mesh.SetVertexBufferParams(
                    pending.Vertices.Length,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3)
                );

                mesh.SetVertexBufferData(
                    pending.Vertices.AsArray(),
                    0,
                    0,
                    pending.Vertices.Length
                );

                mesh.SetIndexBufferParams(
                    pending.Triangles.Length,
                    mesh.indexFormat
                );

                mesh.SetIndexBufferData(
                    pending.Triangles.AsArray(),
                    0,
                    0,
                    pending.Triangles.Length
                );

                mesh.SetSubMesh(
                    0,
                    new SubMeshDescriptor(0, pending.Triangles.Length)
                );

                mesh.RecalculateBounds();
                mesh.UploadMeshData(false);

                GoldbergChunkMeshCache.Set(pending.ChunkIndex, mesh);

                RenderMeshArray renderMeshArray = new RenderMeshArray(
                    new[] { grassMaterial },
                    new[] { mesh }
                );

                RenderMeshDescription desc = new RenderMeshDescription(
                    shadowCastingMode: ShadowCastingMode.On,
                    receiveShadows: true
                );

                if (entityManager.HasComponent<MaterialMeshInfo>(pending.ChunkEntity))
                {
                    entityManager.RemoveComponent<MaterialMeshInfo>(pending.ChunkEntity);
                }

                RenderMeshUtility.AddComponents(
                    pending.ChunkEntity,
                    entityManager,
                    desc,
                    renderMeshArray,
                    MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
                );

                pendingColliders.Enqueue(new PendingChunkCollider
                {
                    ChunkEntity = pending.ChunkEntity,
                    ChunkIndex = pending.ChunkIndex,
                    ColliderMesh = colliderMesh
                });

                GoldbergVoxelChunk chunk =
                    entityManager.GetComponentData<GoldbergVoxelChunk>(pending.ChunkEntity);

                chunk.NeedsMeshBuild = 0;
                chunk.IsMeshBuilt = 1;

                if (entityManager.HasComponent<DisableRendering>(pending.ChunkEntity))
                {
                    entityManager.RemoveComponent<DisableRendering>(pending.ChunkEntity);
                }

                entityManager.SetComponentData(pending.ChunkEntity, chunk);

                if (entityManager.HasComponent<GoldbergVoxelChunkNeedsMeshBuild>(pending.ChunkEntity))
                {
                    entityManager.RemoveComponent<GoldbergVoxelChunkNeedsMeshBuild>(pending.ChunkEntity);
                }

                Debug.Log(
                    $"[ASYNC MESH] Chunk {pending.ChunkIndex} vertices: {mesh.vertexCount}, " +
                    $"bounds center: {mesh.bounds.center}, size: {mesh.bounds.size}"
                );

                DisposePendingBuild(pending);
                pendingBuilds.RemoveAt(i);
            }
        }

        private void DisposePendingBuild(PendingChunkBuild pending)
        {
            if (pending.VertexLookup.IsCreated)
                pending.VertexLookup.Dispose();

            if (pending.Triangles.IsCreated)
                pending.Triangles.Dispose();

            if (pending.Vertices.IsCreated)
                pending.Vertices.Dispose();

            if (pending.NeighbourLookup.IsCreated)
                pending.NeighbourLookup.Dispose();

            if (pending.CellSurfaceLayers.IsCreated)
                pending.CellSurfaceLayers.Dispose();

            if (pending.ChunkColumnIndices.IsCreated)
                pending.ChunkColumnIndices.Dispose();

            if (pending.Columns.IsCreated)
                pending.Columns.Dispose();

            if (pending.CellVertices.IsCreated)
                pending.CellVertices.Dispose();

            if (pending.Cells.IsCreated)
                pending.Cells.Dispose();
        }

        private void ProcessPendingColliders(EntityManager entityManager)
        {
            int processed = 0;

            while (pendingColliders.Count > 0 && processed < MaxColliderUpdatesPerFrame)
            {
                PendingChunkCollider pending = pendingColliders.Dequeue();

                if (!entityManager.Exists(pending.ChunkEntity))
                    continue;

                GoldbergVoxelChunk chunk =
                    entityManager.GetComponentData<GoldbergVoxelChunk>(pending.ChunkEntity);

                if (chunk.IsMeshBuilt == 0)
                    continue;

                if (entityManager.HasComponent<DisableRendering>(pending.ChunkEntity))
                {
                    pendingColliders.Enqueue(pending);
                    break;
                }

                GoldbergChunkColliderBridge.SetChunkCollider(
                    pending.ChunkIndex,
                    pending.ColliderMesh
                );

                processed++;
            }
        }

        private void DrawRegionBoundaryDebug(
            DynamicBuffer<GoldbergCell> cells,
            DynamicBuffer<GoldbergCellVertex> cellVertices,
            DynamicBuffer<VoxelColumn> columns,
            DynamicBuffer<GoldbergCellNeighbourLookup> neighbourLookupBuffer,
            DynamicBuffer<GoldbergChunkColumn> chunkColumns)
        {
            int maxEdgesPerCell = GoldbergNeighbourConstants.MaxEdgesPerCell;

            int[] cellSurfaceLayers = new int[cells.Length];

            for (int i = 0; i < cellSurfaceLayers.Length; i++)
                cellSurfaceLayers[i] = -1;

            for (int i = 0; i < columns.Length; i++)
            {
                VoxelColumn column = columns[i];

                if (column.CellIndex >= 0 && column.CellIndex < cellSurfaceLayers.Length)
                    cellSurfaceLayers[column.CellIndex] = column.SurfaceLayer;
            }

            for (int i = 0; i < chunkColumns.Length; i++)
            {
                int columnIndex = chunkColumns[i].ColumnIndex;

                if (columnIndex < 0 || columnIndex >= columns.Length)
                    continue;

                VoxelColumn column = columns[columnIndex];

                if (column.CellIndex < 0 || column.CellIndex >= cells.Length)
                    continue;

                GoldbergCell cell = cells[column.CellIndex];
                int surfaceLayer = column.SurfaceLayer;

                for (int edgeIndex = 0; edgeIndex < cell.VertexCount; edgeIndex++)
                {
                    int lookupIndex = column.CellIndex * maxEdgesPerCell + edgeIndex;

                    int neighbourCellIndex = -1;

                    if (lookupIndex >= 0 && lookupIndex < neighbourLookupBuffer.Length)
                        neighbourCellIndex = neighbourLookupBuffer[lookupIndex].NeighbourCellIndex;

                    int neighbourSurfaceLayer = -1;

                    if (neighbourCellIndex >= 0 && neighbourCellIndex < cellSurfaceLayers.Length)
                        neighbourSurfaceLayer = cellSurfaceLayers[neighbourCellIndex];

                    if (neighbourSurfaceLayer == surfaceLayer)
                        continue;

                    int next = (edgeIndex + 1) % cell.VertexCount;

                    Vector3 a = cellVertices[cell.FirstVertexIndex + edgeIndex].Position;
                    Vector3 b = cellVertices[cell.FirstVertexIndex + next].Position;

                    Debug.DrawLine(a, b, Color.yellow, 10f);
                }
            }
        }

        private void AddMergedTopFaces(
            GoldbergPlanetSettings settings,
            DynamicBuffer<GoldbergCell> cells,
            DynamicBuffer<GoldbergCellVertex> cellVertices,
            DynamicBuffer<VoxelColumn> columns,
            DynamicBuffer<GoldbergCellNeighbourLookup> neighbourLookupBuffer,
            DynamicBuffer<GoldbergChunkColumn> chunkColumns,
            NativeList<GoldbergMeshVertex> meshVertices,
            NativeList<int> meshTriangles,
            NativeParallelHashMap<GoldbergMeshVertexKey, int> vertexLookup)
        {
            int maxEdgesPerCell = GoldbergNeighbourConstants.MaxEdgesPerCell;

            int[] cellSurfaceLayers = new int[cells.Length];
            bool[] cellInChunk = new bool[cells.Length];
            bool[] visited = new bool[cells.Length];

            for (int i = 0; i < cellSurfaceLayers.Length; i++)
                cellSurfaceLayers[i] = -1;

            for (int i = 0; i < columns.Length; i++)
            {
                VoxelColumn column = columns[i];

                if (column.CellIndex >= 0 && column.CellIndex < cellSurfaceLayers.Length)
                    cellSurfaceLayers[column.CellIndex] = column.SurfaceLayer;
            }

            for (int i = 0; i < chunkColumns.Length; i++)
            {
                int columnIndex = chunkColumns[i].ColumnIndex;

                if (columnIndex < 0 || columnIndex >= columns.Length)
                    continue;

                int cellIndex = columns[columnIndex].CellIndex;

                if (cellIndex >= 0 && cellIndex < cellInChunk.Length)
                    cellInChunk[cellIndex] = true;
            }

            for (int i = 0; i < chunkColumns.Length; i++)
            {
                int columnIndex = chunkColumns[i].ColumnIndex;

                if (columnIndex < 0 || columnIndex >= columns.Length)
                    continue;

                int startCellIndex = columns[columnIndex].CellIndex;

                if (startCellIndex < 0 || startCellIndex >= cells.Length)
                    continue;

                if (visited[startCellIndex])
                    continue;

                int surfaceLayer = cellSurfaceLayers[startCellIndex];

                if (surfaceLayer < 0)
                    continue;

                List<int> regionCells = BuildSameHeightRegion(
                    startCellIndex,
                    surfaceLayer,
                    cellInChunk,
                    visited,
                    cellSurfaceLayers,
                    neighbourLookupBuffer,
                    maxEdgesPerCell
                );

                if (regionCells.Count == 0)
                    continue;

                bool success = TryAddMergedRegionTopFace(
                    settings,
                    cells,
                    cellVertices,
                    cellSurfaceLayers,
                    neighbourLookupBuffer,
                    regionCells,
                    surfaceLayer,
                    maxEdgesPerCell,
                    meshVertices,
                    meshTriangles,
                    vertexLookup
                );

                if (!success)
                {
                    for (int r = 0; r < regionCells.Count; r++)
                    {
                        int cellIndex = regionCells[r];
                        GoldbergCell cell = cells[cellIndex];

                        float radius = GetRadiusForLayer(settings, surfaceLayer);
                        Vector3 normal = ((Vector3)cell.Normal).normalized;

                        AddFallbackCellTopFace(
                            cell,
                            radius,
                            normal,
                            cellVertices,
                            meshVertices,
                            meshTriangles,
                            vertexLookup
                        );
                    }
                }
            }
        }

        private List<int> BuildSameHeightRegion(
            int startCellIndex,
            int surfaceLayer,
            bool[] cellInChunk,
            bool[] visited,
            int[] cellSurfaceLayers,
            DynamicBuffer<GoldbergCellNeighbourLookup> neighbourLookupBuffer,
            int maxEdgesPerCell)
        {
            List<int> region = new();
            Queue<int> queue = new();

            visited[startCellIndex] = true;
            queue.Enqueue(startCellIndex);

            while (queue.Count > 0)
            {
                int cellIndex = queue.Dequeue();
                region.Add(cellIndex);

                for (int edgeIndex = 0; edgeIndex < maxEdgesPerCell; edgeIndex++)
                {
                    int lookupIndex = cellIndex * maxEdgesPerCell + edgeIndex;

                    if (lookupIndex < 0 || lookupIndex >= neighbourLookupBuffer.Length)
                        continue;

                    int neighbour = neighbourLookupBuffer[lookupIndex].NeighbourCellIndex;

                    if (neighbour < 0 || neighbour >= cellSurfaceLayers.Length)
                        continue;

                    if (!cellInChunk[neighbour])
                        continue;

                    if (visited[neighbour])
                        continue;

                    if (cellSurfaceLayers[neighbour] != surfaceLayer)
                        continue;

                    visited[neighbour] = true;
                    queue.Enqueue(neighbour);
                }
            }

            return region;
        }

        private bool TryAddMergedRegionTopFace(
            GoldbergPlanetSettings settings,
            DynamicBuffer<GoldbergCell> cells,
            DynamicBuffer<GoldbergCellVertex> cellVertices,
            int[] cellSurfaceLayers,
            DynamicBuffer<GoldbergCellNeighbourLookup> neighbourLookupBuffer,
            List<int> regionCells,
            int surfaceLayer,
            int maxEdgesPerCell,
            NativeList<GoldbergMeshVertex> meshVertices,
            NativeList<int> meshTriangles,
            NativeParallelHashMap<GoldbergMeshVertexKey, int> vertexLookup)
        {
            HashSet<int> regionSet = new(regionCells);

            List<Vector3> boundaryPoints = ExtractOrderedBoundaryPoints(
                cells,
                cellVertices,
                neighbourLookupBuffer,
                regionSet,
                maxEdgesPerCell
            );

            if (boundaryPoints.Count < 3)
                return false;

            float radius = GetRadiusForLayer(settings, surfaceLayer);

            Vector3 center = Vector3.zero;

            for (int i = 0; i < regionCells.Count; i++)
            {
                center += (Vector3)math.normalize(cells[regionCells[i]].Center);
            }

            center = (center / regionCells.Count).normalized * radius;

            Vector3 normal = center.normalized;

            List<Vector2> projected =
                ProjectBoundaryTo2D(boundaryPoints, center, normal, out _, out _);

            List<int> triangulatedIndices = TriangulateEarClipping(projected);

            if (triangulatedIndices.Count < 3)
                return false;

            List<int> meshPointIndices = new();

            for (int i = 0; i < boundaryPoints.Count; i++)
            {
                Vector3 worldPoint = boundaryPoints[i].normalized * radius;

                int vertexIndex = GetOrAddManagedVertex(
                    new GoldbergMeshVertex
                    {
                        Position = worldPoint,
                        Normal = normal
                    },
                    meshVertices,
                    vertexLookup
                );

                meshPointIndices.Add(vertexIndex);
            }

            for (int i = 0; i < triangulatedIndices.Count; i += 3)
            {
                int a = meshPointIndices[triangulatedIndices[i]];
                int b = meshPointIndices[triangulatedIndices[i + 1]];
                int c = meshPointIndices[triangulatedIndices[i + 2]];

                Vector3 pa = meshVertices[a].Position;
                Vector3 pb = meshVertices[b].Position;
                Vector3 pc = meshVertices[c].Position;

                Vector3 triNormal = Vector3.Cross(pb - pa, pc - pa).normalized;

                if (Vector3.Dot(triNormal, normal) >= 0f)
                {
                    meshTriangles.Add(a);
                    meshTriangles.Add(b);
                    meshTriangles.Add(c);
                }
                else
                {
                    meshTriangles.Add(a);
                    meshTriangles.Add(c);
                    meshTriangles.Add(b);
                }
            }

            return true;
        }

        private List<Vector3> ExtractOrderedBoundaryPoints(
            DynamicBuffer<GoldbergCell> cells,
            DynamicBuffer<GoldbergCellVertex> cellVertices,
            DynamicBuffer<GoldbergCellNeighbourLookup> neighbourLookupBuffer,
            HashSet<int> regionSet,
            int maxEdgesPerCell)
        {
            List<Edge> boundaryEdges = new();

            foreach (int cellIndex in regionSet)
            {
                GoldbergCell cell = cells[cellIndex];

                for (int edgeIndex = 0; edgeIndex < cell.VertexCount; edgeIndex++)
                {
                    int lookupIndex = cellIndex * maxEdgesPerCell + edgeIndex;

                    int neighbour = -1;

                    if (lookupIndex >= 0 && lookupIndex < neighbourLookupBuffer.Length)
                        neighbour = neighbourLookupBuffer[lookupIndex].NeighbourCellIndex;

                    if (neighbour >= 0 && regionSet.Contains(neighbour))
                        continue;

                    int next = (edgeIndex + 1) % cell.VertexCount;

                    Vector3 a = cellVertices[cell.FirstVertexIndex + edgeIndex].Position;
                    Vector3 b = cellVertices[cell.FirstVertexIndex + next].Position;

                    boundaryEdges.Add(new Edge { A = a, B = b });
                }
            }

            if (boundaryEdges.Count == 0)
                return new List<Vector3>();

            List<Vector3> orderedPoints = new();

            Edge current = boundaryEdges[0];
            boundaryEdges.RemoveAt(0);

            orderedPoints.Add(current.A);
            orderedPoints.Add(current.B);

            int guard = 0;

            while (boundaryEdges.Count > 0 && guard < 10000)
            {
                bool foundNext = false;

                for (int i = 0; i < boundaryEdges.Count; i++)
                {
                    Edge e = boundaryEdges[i];

                    if ((orderedPoints[^1] - e.A).sqrMagnitude < 0.00001f)
                    {
                        orderedPoints.Add(e.B);
                        boundaryEdges.RemoveAt(i);
                        foundNext = true;
                        break;
                    }

                    if ((orderedPoints[^1] - e.B).sqrMagnitude < 0.00001f)
                    {
                        orderedPoints.Add(e.A);
                        boundaryEdges.RemoveAt(i);
                        foundNext = true;
                        break;
                    }
                }

                if (!foundNext)
                    break;

                guard++;
            }

            return orderedPoints;
        }

        private List<Vector2> ProjectBoundaryTo2D(
            List<Vector3> boundaryPoints,
            Vector3 center,
            Vector3 normal,
            out Vector3 tangent,
            out Vector3 bitangent)
        {
            tangent = Vector3.Cross(normal, Vector3.up);

            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.Cross(normal, Vector3.right);

            tangent.Normalize();
            bitangent = Vector3.Cross(normal, tangent).normalized;

            List<Vector2> result = new();

            for (int i = 0; i < boundaryPoints.Count; i++)
            {
                Vector3 p = boundaryPoints[i] - center;

                result.Add(new Vector2(
                    Vector3.Dot(p, tangent),
                    Vector3.Dot(p, bitangent)
                ));
            }

            return result;
        }

        private List<int> TriangulateEarClipping(List<Vector2> points)
        {
            List<int> result = new();

            if (points.Count < 3)
                return result;

            List<int> indices = new();

            for (int i = 0; i < points.Count; i++)
                indices.Add(i);

            if (SignedArea(points) < 0f)
                indices.Reverse();

            int guard = 0;

            while (indices.Count > 3 && guard < 10000)
            {
                bool earFound = false;

                for (int i = 0; i < indices.Count; i++)
                {
                    int prevIndex = indices[(i - 1 + indices.Count) % indices.Count];
                    int currentIndex = indices[i];
                    int nextIndex = indices[(i + 1) % indices.Count];

                    Vector2 prev = points[prevIndex];
                    Vector2 current = points[currentIndex];
                    Vector2 next = points[nextIndex];

                    if (!IsConvex(prev, current, next))
                        continue;

                    bool containsPoint = false;

                    for (int p = 0; p < indices.Count; p++)
                    {
                        int testIndex = indices[p];

                        if (testIndex == prevIndex ||
                            testIndex == currentIndex ||
                            testIndex == nextIndex)
                            continue;

                        if (PointInTriangle(points[testIndex], prev, current, next))
                        {
                            containsPoint = true;
                            break;
                        }
                    }

                    if (containsPoint)
                        continue;

                    result.Add(prevIndex);
                    result.Add(currentIndex);
                    result.Add(nextIndex);

                    indices.RemoveAt(i);
                    earFound = true;
                    break;
                }

                if (!earFound)
                    return new List<int>();

                guard++;
            }

            if (indices.Count == 3)
            {
                result.Add(indices[0]);
                result.Add(indices[1]);
                result.Add(indices[2]);
            }

            return result;
        }

        private float SignedArea(List<Vector2> points)
        {
            float area = 0f;

            for (int i = 0; i < points.Count; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % points.Count];

                area += (a.x * b.y) - (b.x * a.y);
            }

            return area * 0.5f;
        }

        private bool IsConvex(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 ab = b - a;
            Vector2 bc = c - b;

            float cross = ab.x * bc.y - ab.y * bc.x;

            return cross > 0f;
        }

        private bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float area = TriangleArea(a, b, c);
            float area1 = TriangleArea(p, b, c);
            float area2 = TriangleArea(a, p, c);
            float area3 = TriangleArea(a, b, p);

            return Mathf.Abs(area - (area1 + area2 + area3)) <= 0.0001f;
        }

        private float TriangleArea(Vector2 a, Vector2 b, Vector2 c)
        {
            return Mathf.Abs(
                (a.x * (b.y - c.y) +
                 b.x * (c.y - a.y) +
                 c.x * (a.y - b.y)) * 0.5f
            );
        }

        private void AddFallbackCellTopFace(
            GoldbergCell cell,
            float radius,
            Vector3 normal,
            DynamicBuffer<GoldbergCellVertex> cellVertices,
            NativeList<GoldbergMeshVertex> meshVertices,
            NativeList<int> meshTriangles,
            NativeParallelHashMap<GoldbergMeshVertexKey, int> vertexLookup)
        {
            if (cell.VertexCount < 3)
                return;

            Vector3 center = Vector3.zero;

            for (int i = 0; i < cell.VertexCount; i++)
            {
                Vector3 p = cellVertices[cell.FirstVertexIndex + i].Position;
                center += p.normalized * radius;
            }

            center /= cell.VertexCount;

            int centerIndex = GetOrAddManagedVertex(
                new GoldbergMeshVertex
                {
                    Position = center,
                    Normal = normal
                },
                meshVertices,
                vertexLookup
            );

            List<int> ringIndices = new();

            for (int i = 0; i < cell.VertexCount; i++)
            {
                Vector3 p = cellVertices[cell.FirstVertexIndex + i].Position;
                Vector3 position = p.normalized * radius;

                int index = GetOrAddManagedVertex(
                    new GoldbergMeshVertex
                    {
                        Position = position,
                        Normal = normal
                    },
                    meshVertices,
                    vertexLookup
                );

                ringIndices.Add(index);
            }

            for (int i = 0; i < cell.VertexCount; i++)
            {
                int next = (i + 1) % cell.VertexCount;

                meshTriangles.Add(centerIndex);
                meshTriangles.Add(ringIndices[i]);
                meshTriangles.Add(ringIndices[next]);
            }
        }

        private int GetOrAddManagedVertex(
            GoldbergMeshVertex vertex,
            NativeList<GoldbergMeshVertex> meshVertices,
            NativeParallelHashMap<GoldbergMeshVertexKey, int> vertexLookup)
        {
            GoldbergMeshVertexKey key = new GoldbergMeshVertexKey(vertex);

            if (vertexLookup.TryGetValue(key, out int existingIndex))
                return existingIndex;

            int newIndex = meshVertices.Length;
            meshVertices.Add(vertex);
            vertexLookup.TryAdd(key, newIndex);

            return newIndex;
        }

        private float GetRadiusForLayer(GoldbergPlanetSettings settings, int layer)
        {
            return settings.Radius +
                   ((layer - settings.Layers / 2f) * settings.CellHeight);
        }

        private Mesh BuildColliderMesh(
            NativeList<GoldbergMeshVertex> renderVertices,
            NativeList<int> renderTriangles,
            int chunkIndex)
        {
            Dictionary<Vector3Int, int> vertexLookup = new();
            List<Vector3> colliderVertices = new();
            List<int> colliderTriangles = new();

            const float precision = 10000f;

            for (int i = 0; i < renderTriangles.Length; i += 3)
            {
                int r0 = renderTriangles[i];
                int r1 = renderTriangles[i + 1];
                int r2 = renderTriangles[i + 2];

                Vector3 p0 = renderVertices[r0].Position;
                Vector3 p1 = renderVertices[r1].Position;
                Vector3 p2 = renderVertices[r2].Position;

                int c0 = GetOrAddColliderVertex(p0, precision, vertexLookup, colliderVertices);
                int c1 = GetOrAddColliderVertex(p1, precision, vertexLookup, colliderVertices);
                int c2 = GetOrAddColliderVertex(p2, precision, vertexLookup, colliderVertices);

                if (c0 == c1 || c1 == c2 || c2 == c0)
                    continue;

                colliderTriangles.Add(c0);
                colliderTriangles.Add(c1);
                colliderTriangles.Add(c2);
            }

            Mesh colliderMesh = new Mesh();
            colliderMesh.name = $"Goldberg Collider Mesh {chunkIndex}";
            colliderMesh.indexFormat = IndexFormat.UInt32;

            colliderMesh.SetVertices(colliderVertices);
            colliderMesh.SetTriangles(colliderTriangles, 0);
            colliderMesh.RecalculateBounds();

            Debug.Log(
                $"[COLLIDER MESH] Chunk {chunkIndex} render verts: {renderVertices.Length}, " +
                $"collider verts: {colliderVertices.Count}, tris: {colliderTriangles.Count / 3}"
            );

            return colliderMesh;
        }

        private int GetOrAddColliderVertex(
            Vector3 position,
            float precision,
            Dictionary<Vector3Int, int> lookup,
            List<Vector3> vertices)
        {
            Vector3Int key = new Vector3Int(
                Mathf.RoundToInt(position.x * precision),
                Mathf.RoundToInt(position.y * precision),
                Mathf.RoundToInt(position.z * precision)
            );

            if (lookup.TryGetValue(key, out int index))
                return index;

            index = vertices.Count;
            vertices.Add(position);
            lookup.Add(key, index);

            return index;
        }
    }
}
