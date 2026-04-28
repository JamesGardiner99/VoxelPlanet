using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxelPlanet
{
    public struct GoldbergMeshVertex
    {
        public float3 Position;
        public float3 Normal;
    }

    [BurstCompile]
    public struct GoldbergVoxelMeshBuildJob : IJob
    {
        [ReadOnly] public NativeArray<GoldbergCell> Cells;
        [ReadOnly] public NativeArray<GoldbergCellVertex> CellVertices;
        [ReadOnly] public NativeArray<VoxelColumn> Columns;
        [ReadOnly] public NativeArray<int> ChunkColumnIndices;

        [ReadOnly] public NativeParallelMultiHashMap<EdgeKey, int> EdgeToCells;
        [ReadOnly] public NativeParallelMultiHashMap<VertexKey, int> VertexToCells;

        public GoldbergPlanetSettings Settings;

        public NativeList<GoldbergMeshVertex> Vertices;
        public NativeList<int> Triangles;

        public NativeReference<int> DebugEdgesChecked;
        public NativeReference<int> DebugNeighboursFound;
        public NativeReference<int> DebugDifferentHeightEdges;
        public NativeReference<int> DebugWallsAdded;

        public void Execute()
        {
            for (int i = 0; i < ChunkColumnIndices.Length; i++)
            {
                int columnIndex = ChunkColumnIndices[i];

                if (columnIndex < 0 || columnIndex >= Columns.Length)
                    continue;

                VoxelColumn column = Columns[columnIndex];
                GoldbergCell cell = Cells[column.CellIndex];

                int surfaceLayer = column.SurfaceLayer;

                float surfaceRadius =
                    Settings.Radius +
                    ((surfaceLayer - Settings.Layers / 2f) * Settings.CellHeight);

                float3 normal = math.normalize(cell.Normal);

                AddPolygonFace(cell, surfaceRadius, normal);
                AddFullColumnWalls(cell, surfaceLayer);

                /*AddNeighbourAwareSideWalls(
                    column.CellIndex,
                    cell,
                    surfaceLayer
                );

                AddCornerFillers(
                    column.CellIndex,
                    cell
                );*/

                
            }
        }

        private void AddPolygonFace(
            GoldbergCell cell,
            float radius,
            float3 expectedNormal)
        {
            if (cell.VertexCount < 3)
                return;

            float3 center = float3.zero;

            for (int i = 0; i < cell.VertexCount; i++)
            {
                float3 p = CellVertices[cell.FirstVertexIndex + i].Position;
                center += math.normalize(p) * radius;
            }

            center /= cell.VertexCount;

            int centerIndex = Vertices.Length;

            Vertices.Add(new GoldbergMeshVertex
            {
                Position = center,
                Normal = expectedNormal
            });

            int first = Vertices.Length;

            for (int i = 0; i < cell.VertexCount; i++)
            {
                float3 p = CellVertices[cell.FirstVertexIndex + i].Position;

                Vertices.Add(new GoldbergMeshVertex
                {
                    Position = math.normalize(p) * radius,
                    Normal = expectedNormal
                });
            }

            for (int i = 0; i < cell.VertexCount; i++)
            {
                int next = (i + 1) % cell.VertexCount;

                float3 a = Vertices[first + i].Position;
                float3 b = Vertices[first + next].Position;

                float3 triNormal =
                    math.normalize(math.cross(a - center, b - center));

                if (math.dot(triNormal, expectedNormal) >= 0f)
                {
                    Triangles.Add(centerIndex);
                    Triangles.Add(first + i);
                    Triangles.Add(first + next);
                }
                else
                {
                    Triangles.Add(centerIndex);
                    Triangles.Add(first + next);
                    Triangles.Add(first + i);
                }
            }
        }

        private void AddNeighbourAwareSideWalls(
            int currentCellIndex,
            GoldbergCell cell,
            int surfaceLayer)
        {
            for (int i = 0; i < cell.VertexCount; i++)
            {
                DebugEdgesChecked.Value++;

                int next = (i + 1) % cell.VertexCount;

                float3 aBase = CellVertices[cell.FirstVertexIndex + i].Position;
                float3 bBase = CellVertices[cell.FirstVertexIndex + next].Position;

                EdgeKey edge = new EdgeKey(aBase, bBase);

                NativeParallelMultiHashMapIterator<EdgeKey> iterator;

                if (!EdgeToCells.TryGetFirstValue(edge, out int neighbourCellIndex, out iterator))
                    continue;

                do
                {
                    if (neighbourCellIndex == currentCellIndex)
                        continue;

                    DebugNeighboursFound.Value++;

                    int neighbourSurfaceLayer = FindSurfaceLayerForCell(neighbourCellIndex);

                    if (neighbourSurfaceLayer < 0)
                        continue;

                    if (neighbourSurfaceLayer >= surfaceLayer)
                        continue;

                    DebugDifferentHeightEdges.Value++;

                    float topRadius =
                        Settings.Radius +
                        ((surfaceLayer - Settings.Layers / 2f) * Settings.CellHeight);

                    float bottomRadius =
                        Settings.Radius +
                        ((neighbourSurfaceLayer - Settings.Layers / 2f) * Settings.CellHeight);

                    float3 aDir = math.normalize(aBase);
                    float3 bDir = math.normalize(bBase);

                    AddQuadFace(
                        aDir * bottomRadius,
                        bDir * bottomRadius,
                        bDir * topRadius,
                        aDir * topRadius
                    );

                    DebugWallsAdded.Value++;
                }
                while (EdgeToCells.TryGetNextValue(out neighbourCellIndex, ref iterator));
            }
        }

        private void AddCornerFillers(
            int currentCellIndex,
            GoldbergCell cell)
        {
            for (int i = 0; i < cell.VertexCount; i++)
            {
                float3 vertex =
                    CellVertices[cell.FirstVertexIndex + i].Position;

                VertexKey key = new VertexKey(vertex);

                NativeParallelMultiHashMapIterator<VertexKey> iterator;

                if (!VertexToCells.TryGetFirstValue(key, out int sharedCellIndex, out iterator))
                    continue;

                int minCellIndex = currentCellIndex;
                int minLayer = int.MaxValue;
                int maxLayer = int.MinValue;

                do
                {
                    int layer = FindSurfaceLayerForCell(sharedCellIndex);

                    if (layer < 0)
                        continue;

                    minLayer = math.min(minLayer, layer);
                    maxLayer = math.max(maxLayer, layer);
                    minCellIndex = math.min(minCellIndex, sharedCellIndex);
                }
                while (VertexToCells.TryGetNextValue(out sharedCellIndex, ref iterator));

                if (minLayer == int.MaxValue || maxLayer == int.MinValue)
                    continue;

                if (minLayer == maxLayer)
                    continue;

                // Only one cell sharing this vertex creates the filler.
                if (currentCellIndex != minCellIndex)
                    continue;

                float bottomRadius =
                    Settings.Radius +
                    ((minLayer - Settings.Layers / 2f) * Settings.CellHeight);

                float topRadius =
                    Settings.Radius +
                    ((maxLayer - Settings.Layers / 2f) * Settings.CellHeight);

                float3 dir = math.normalize(vertex);

                float3 tangentA = math.cross(dir, new float3(0f, 1f, 0f));

                if (math.lengthsq(tangentA) < 0.0001f)
                    tangentA = math.cross(dir, new float3(1f, 0f, 0f));

                tangentA = math.normalize(tangentA);

                float3 tangentB =
                    math.normalize(math.cross(dir, tangentA));

                float fillerRadius =
                    Settings.CellHeight * 0.05f;

                float3 bottomCenter = dir * bottomRadius;
                float3 topCenter = dir * topRadius;

                float3 b0 = bottomCenter + tangentA * fillerRadius;
                float3 b1 = bottomCenter + tangentB * fillerRadius;
                float3 b2 = bottomCenter - tangentA * fillerRadius;
                float3 b3 = bottomCenter - tangentB * fillerRadius;

                float3 t0 = topCenter + tangentA * fillerRadius;
                float3 t1 = topCenter + tangentB * fillerRadius;
                float3 t2 = topCenter - tangentA * fillerRadius;
                float3 t3 = topCenter - tangentB * fillerRadius;

                AddQuadFace(b0, b1, t1, t0);
                AddQuadFace(b1, b2, t2, t1);
                AddQuadFace(b2, b3, t3, t2);
                AddQuadFace(b3, b0, t0, t3);
            }
        }

        private int FindSurfaceLayerForCell(int cellIndex)
        {
            for (int i = 0; i < Columns.Length; i++)
            {
                if (Columns[i].CellIndex == cellIndex)
                    return Columns[i].SurfaceLayer;
            }

            return -1;
        }

        private void AddQuadFace(
            float3 v0,
            float3 v1,
            float3 v2,
            float3 v3)
        {
            float3 normal =
                math.normalize(math.cross(v1 - v0, v2 - v0));

            int start = Vertices.Length;

            Vertices.Add(new GoldbergMeshVertex { Position = v0, Normal = normal });
            Vertices.Add(new GoldbergMeshVertex { Position = v1, Normal = normal });
            Vertices.Add(new GoldbergMeshVertex { Position = v2, Normal = normal });
            Vertices.Add(new GoldbergMeshVertex { Position = v3, Normal = normal });

            // Front side
            Triangles.Add(start + 0);
            Triangles.Add(start + 1);
            Triangles.Add(start + 2);

            Triangles.Add(start + 0);
            Triangles.Add(start + 2);
            Triangles.Add(start + 3);

            // Back side
            Triangles.Add(start + 2);
            Triangles.Add(start + 1);
            Triangles.Add(start + 0);

            Triangles.Add(start + 3);
            Triangles.Add(start + 2);
            Triangles.Add(start + 0);
        }

        private void AddFullColumnWalls(
            GoldbergCell cell,
            int surfaceLayer)
        {
            float topRadius =
                Settings.Radius +
                ((surfaceLayer - Settings.Layers / 2f) * Settings.CellHeight);

            float bottomRadius =
                Settings.Radius +
                ((0 - Settings.Layers / 2f) * Settings.CellHeight);

            for (int i = 0; i < cell.VertexCount; i++)
            {
                int next = (i + 1) % cell.VertexCount;

                float3 aBase =
                    CellVertices[cell.FirstVertexIndex + i].Position;

                float3 bBase =
                    CellVertices[cell.FirstVertexIndex + next].Position;

                float3 aDir = math.normalize(aBase);
                float3 bDir = math.normalize(bBase);

                AddQuadFace(
                    aDir * bottomRadius,
                    bDir * bottomRadius,
                    bDir * topRadius,
                    aDir * topRadius
                );
            }
        }
    }
}