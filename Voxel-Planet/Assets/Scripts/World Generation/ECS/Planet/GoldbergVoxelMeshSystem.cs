using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelPlanet
{
    [UpdateAfter(typeof(GoldbergColumnGenerationSystem))]
    [UpdateAfter(typeof(GoldbergVoxelChunkCreationSystem))]
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

            using Unity.Collections.NativeArray<Entity> chunkEntities =
                query.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < chunkEntities.Length; i++)
            {
                Entity chunkEntity = chunkEntities[i];

                GoldbergVoxelChunk chunk =
                    entityManager.GetComponentData<GoldbergVoxelChunk>(chunkEntity);

                if (chunk.NeedsMeshBuild == 0)
                    continue;

                if (!entityManager.Exists(chunk.PlanetEntity))
                    continue;

                RemoveOldPlanetRenderComponents(entityManager, chunk.PlanetEntity);

                GoldbergPlanetSettings settings =
                    entityManager.GetComponentData<GoldbergPlanetSettings>(chunk.PlanetEntity);

                DynamicBuffer<GoldbergCell> cells =
                    entityManager.GetBuffer<GoldbergCell>(chunk.PlanetEntity);

                DynamicBuffer<GoldbergCellVertex> cellVertices =
                    entityManager.GetBuffer<GoldbergCellVertex>(chunk.PlanetEntity);

                DynamicBuffer<VoxelColumn> columns =
                    entityManager.GetBuffer<VoxelColumn>(chunk.PlanetEntity);

                Mesh mesh = BuildVoxelChunkMesh(
                    settings,
                    cells,
                    cellVertices,
                    columns,
                    chunk.StartColumnIndex,
                    chunk.ColumnCount
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
                    $"Goldberg chunk {chunk.ChunkIndex} mesh built. Vertices: {vertexCount}"
                );

                // Build only one chunk per frame.
                // This avoids DOTS buffer handles being invalidated by structural changes.
                return;
            }
        }

        private void RemoveOldPlanetRenderComponents(EntityManager entityManager, Entity planetEntity)
        {
            if (entityManager.HasComponent<MaterialMeshInfo>(planetEntity))
                entityManager.RemoveComponent<MaterialMeshInfo>(planetEntity);

            if (entityManager.HasComponent<RenderBounds>(planetEntity))
                entityManager.RemoveComponent<RenderBounds>(planetEntity);

            if (entityManager.HasComponent<WorldRenderBounds>(planetEntity))
                entityManager.RemoveComponent<WorldRenderBounds>(planetEntity);
        }

        private Mesh BuildVoxelChunkMesh(
            GoldbergPlanetSettings settings,
            DynamicBuffer<GoldbergCell> cells,
            DynamicBuffer<GoldbergCellVertex> cellVertices,
            DynamicBuffer<VoxelColumn> columns,
            int startColumnIndex,
            int columnCount)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector3> normals = new List<Vector3>();

            Dictionary<int, int> surfaceLayersByCell = new Dictionary<int, int>();

            for (int i = 0; i < columns.Length; i++)
            {
                surfaceLayersByCell[columns[i].CellIndex] = columns[i].SurfaceLayer;
            }

            Dictionary<EdgeKey, List<int>> edgeToCells =
                BuildEdgeLookup(cells, cellVertices);

            int endColumnIndex =
                Mathf.Min(startColumnIndex + columnCount, columns.Length);

            for (int c = startColumnIndex; c < endColumnIndex; c++)
            {
                VoxelColumn column = columns[c];
                GoldbergCell cell = cells[column.CellIndex];

                int surfaceLayer = column.SurfaceLayer;

                float surfaceRadius =
                    settings.Radius +
                    ((surfaceLayer - settings.Layers / 2) * settings.CellHeight);

                List<Vector3> topPolygon = GetCellPolygonAtRadius(
                    cell,
                    cellVertices,
                    surfaceRadius
                );

                Vector3 normal =
                    new Vector3(cell.Normal.x, cell.Normal.y, cell.Normal.z).normalized;

                AddPolygonFace(
                    topPolygon,
                    normal,
                    vertices,
                    triangles,
                    normals
                );

                AddNeighbourAwareSideWalls(
                    settings,
                    column.CellIndex,
                    cell,
                    cellVertices,
                    surfaceLayer,
                    surfaceLayersByCell,
                    edgeToCells,
                    vertices,
                    triangles,
                    normals
                );
            }

            Mesh mesh = new Mesh();
            mesh.name = "Goldberg Voxel Chunk Mesh";

            if (vertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);
            mesh.RecalculateBounds();

            return mesh;
        }

        private List<Vector3> GetCellPolygonAtRadius(
            GoldbergCell cell,
            DynamicBuffer<GoldbergCellVertex> cellVertices,
            float radius)
        {
            List<Vector3> polygon = new List<Vector3>();

            for (int i = 0; i < cell.VertexCount; i++)
            {
                Vector3 p = cellVertices[cell.FirstVertexIndex + i].Position;
                polygon.Add(p.normalized * radius);
            }

            return polygon;
        }

        private void AddPolygonFace(
            List<Vector3> polygon,
            Vector3 expectedNormal,
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector3> normals)
        {
            if (polygon.Count < 3)
                return;

            int centerIndex = vertices.Count;
            Vector3 center = Vector3.zero;

            for (int i = 0; i < polygon.Count; i++)
                center += polygon[i];

            center /= polygon.Count;

            vertices.Add(center);
            normals.Add(expectedNormal);

            int first = vertices.Count;

            for (int i = 0; i < polygon.Count; i++)
            {
                vertices.Add(polygon[i]);
                normals.Add(expectedNormal);
            }

            for (int i = 0; i < polygon.Count; i++)
            {
                int next = (i + 1) % polygon.Count;

                Vector3 a = vertices[first + i];
                Vector3 b = vertices[first + next];

                Vector3 triNormal =
                    Vector3.Cross(a - center, b - center).normalized;

                if (Vector3.Dot(triNormal, expectedNormal) >= 0f)
                {
                    triangles.Add(centerIndex);
                    triangles.Add(first + i);
                    triangles.Add(first + next);
                }
                else
                {
                    triangles.Add(centerIndex);
                    triangles.Add(first + next);
                    triangles.Add(first + i);
                }
            }
        }

        private void AddNeighbourAwareSideWalls(
            GoldbergPlanetSettings settings,
            int currentCellIndex,
            GoldbergCell cell,
            DynamicBuffer<GoldbergCellVertex> cellVertices,
            int surfaceLayer,
            Dictionary<int, int> surfaceLayersByCell,
            Dictionary<EdgeKey, List<int>> edgeToCells,
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector3> normals)
        {
            float currentRadius =
                settings.Radius +
                ((surfaceLayer - settings.Layers / 2) * settings.CellHeight);

            for (int i = 0; i < cell.VertexCount; i++)
            {
                int next = (i + 1) % cell.VertexCount;

                Vector3 aBase =
                    cellVertices[cell.FirstVertexIndex + i].Position;

                Vector3 bBase =
                    cellVertices[cell.FirstVertexIndex + next].Position;

                EdgeKey edge = new EdgeKey(aBase, bBase);

                int neighbourCellIndex =
                    FindNeighbourForEdge(edge, currentCellIndex, edgeToCells);

                if (neighbourCellIndex < 0)
                    continue;

                if (!surfaceLayersByCell.TryGetValue(
                        neighbourCellIndex,
                        out int neighbourSurfaceLayer))
                    continue;

                if (neighbourSurfaceLayer >= surfaceLayer)
                    continue;

                float neighbourRadius =
                    settings.Radius +
                    ((neighbourSurfaceLayer - settings.Layers / 2) * settings.CellHeight);

                Vector3 aDir = aBase.normalized;
                Vector3 bDir = bBase.normalized;

                Vector3 aTop = aDir * currentRadius;
                Vector3 bTop = bDir * currentRadius;

                Vector3 aBottom = aDir * neighbourRadius;
                Vector3 bBottom = bDir * neighbourRadius;

                AddQuadFace(
                    aBottom,
                    bBottom,
                    bTop,
                    aTop,
                    vertices,
                    triangles,
                    normals
                );
            }
        }

        private Dictionary<EdgeKey, List<int>> BuildEdgeLookup(
            DynamicBuffer<GoldbergCell> cells,
            DynamicBuffer<GoldbergCellVertex> cellVertices)
        {
            Dictionary<EdgeKey, List<int>> edgeToCells =
                new Dictionary<EdgeKey, List<int>>();

            for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                GoldbergCell cell = cells[cellIndex];

                for (int i = 0; i < cell.VertexCount; i++)
                {
                    int next = (i + 1) % cell.VertexCount;

                    Vector3 a =
                        cellVertices[cell.FirstVertexIndex + i].Position;

                    Vector3 b =
                        cellVertices[cell.FirstVertexIndex + next].Position;

                    EdgeKey key = new EdgeKey(a, b);

                    if (!edgeToCells.TryGetValue(key, out List<int> list))
                    {
                        list = new List<int>();
                        edgeToCells[key] = list;
                    }

                    list.Add(cellIndex);
                }
            }

            return edgeToCells;
        }

        private int FindNeighbourForEdge(
            EdgeKey edge,
            int currentCellIndex,
            Dictionary<EdgeKey, List<int>> edgeToCells)
        {
            if (!edgeToCells.TryGetValue(edge, out List<int> cells))
                return -1;

            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i] != currentCellIndex)
                    return cells[i];
            }

            return -1;
        }

        private void AddQuadFace(
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            Vector3 v3,
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector3> normals)
        {
            Vector3 normal =
                Vector3.Cross(v1 - v0, v2 - v0).normalized;

            int start = vertices.Count;

            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);

            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);

            triangles.Add(start + 0);
            triangles.Add(start + 1);
            triangles.Add(start + 2);

            triangles.Add(start + 0);
            triangles.Add(start + 2);
            triangles.Add(start + 3);

            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start + 0);

            triangles.Add(start + 3);
            triangles.Add(start + 2);
            triangles.Add(start + 0);
        }

        private readonly struct EdgeKey
        {
            private readonly int ax;
            private readonly int ay;
            private readonly int az;
            private readonly int bx;
            private readonly int by;
            private readonly int bz;

            public EdgeKey(Vector3 a, Vector3 b)
            {
                Vector3 an = a.normalized;
                Vector3 bn = b.normalized;

                int qax = Mathf.RoundToInt(an.x * 100000);
                int qay = Mathf.RoundToInt(an.y * 100000);
                int qaz = Mathf.RoundToInt(an.z * 100000);

                int qbx = Mathf.RoundToInt(bn.x * 100000);
                int qby = Mathf.RoundToInt(bn.y * 100000);
                int qbz = Mathf.RoundToInt(bn.z * 100000);

                bool swap =
                    qax > qbx ||
                    qax == qbx && qay > qby ||
                    qax == qbx && qay == qby && qaz > qbz;

                if (!swap)
                {
                    ax = qax;
                    ay = qay;
                    az = qaz;

                    bx = qbx;
                    by = qby;
                    bz = qbz;
                }
                else
                {
                    ax = qbx;
                    ay = qby;
                    az = qbz;

                    bx = qax;
                    by = qay;
                    bz = qaz;
                }
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = ax;
                    hash = hash * 397 ^ ay;
                    hash = hash * 397 ^ az;
                    hash = hash * 397 ^ bx;
                    hash = hash * 397 ^ by;
                    hash = hash * 397 ^ bz;
                    return hash;
                }
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeKey other &&
                    ax == other.ax &&
                    ay == other.ay &&
                    az == other.az &&
                    bx == other.bx &&
                    by == other.by &&
                    bz == other.bz;
            }
        }
    }
}