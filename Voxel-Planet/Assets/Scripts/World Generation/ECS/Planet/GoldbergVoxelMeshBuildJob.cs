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
        [ReadOnly] public NativeArray<GoldbergCellNeighbour> Neighbours;
        [ReadOnly] public NativeArray<int> CellSurfaceLayers;
        [ReadOnly] public NativeArray<int> ChunkColumnIndices;

        public GoldbergPlanetSettings Settings;

        public NativeList<GoldbergMeshVertex> Vertices;
        public NativeList<int> Triangles;

        public void Execute()
        {
            for (int i = 0; i < ChunkColumnIndices.Length; i++)
            {
                int columnIndex = ChunkColumnIndices[i];

                if (columnIndex < 0 || columnIndex >= Columns.Length)
                    continue;

                VoxelColumn column = Columns[columnIndex];

                if (column.CellIndex < 0 || column.CellIndex >= Cells.Length)
                    continue;

                GoldbergCell cell = Cells[column.CellIndex];

                int surfaceLayer = column.SurfaceLayer;

                float surfaceRadius = GetRadiusForLayer(surfaceLayer);
                float3 normal = math.normalize(cell.Normal);

                AddPolygonFace(cell, surfaceRadius, normal);
                AddVisibleColumnWalls(column.CellIndex, cell, surfaceLayer);
            }
        }

        private void AddVisibleColumnWalls(
            int cellIndex,
            GoldbergCell cell,
            int surfaceLayer)
        {
            for (int edgeIndex = 0; edgeIndex < cell.VertexCount; edgeIndex++)
            {
                int neighbourCellIndex =
                    FindNeighbourCellIndex(cellIndex, edgeIndex);

                int neighbourSurfaceLayer = -1;

                if (
                    neighbourCellIndex >= 0 &&
                    neighbourCellIndex < CellSurfaceLayers.Length
                )
                {
                    neighbourSurfaceLayer =
                        CellSurfaceLayers[neighbourCellIndex];
                }

                if (neighbourSurfaceLayer >= surfaceLayer)
                    continue;

                float topRadius = GetRadiusForLayer(surfaceLayer);

                float bottomRadius =
                    neighbourSurfaceLayer >= 0
                        ? GetRadiusForLayer(neighbourSurfaceLayer)
                        : GetRadiusForLayer(0);

                AddWallForEdge(cell, edgeIndex, bottomRadius, topRadius);
            }
        }

        private int FindNeighbourCellIndex(
            int cellIndex,
            int edgeIndex)
        {
            for (int i = 0; i < Neighbours.Length; i++)
            {
                GoldbergCellNeighbour neighbour = Neighbours[i];

                if (
                    neighbour.CellIndex == cellIndex &&
                    neighbour.EdgeIndex == edgeIndex
                )
                {
                    return neighbour.NeighbourCellIndex;
                }
            }

            return -1;
        }

        private void AddWallForEdge(
            GoldbergCell cell,
            int edgeIndex,
            float bottomRadius,
            float topRadius)
        {
            int next = (edgeIndex + 1) % cell.VertexCount;

            float3 aBase =
                CellVertices[cell.FirstVertexIndex + edgeIndex].Position;

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

            Triangles.Add(start + 0);
            Triangles.Add(start + 1);
            Triangles.Add(start + 2);

            Triangles.Add(start + 0);
            Triangles.Add(start + 2);
            Triangles.Add(start + 3);
        }

        private float GetRadiusForLayer(int layer)
        {
            return Settings.Radius +
                   ((layer - Settings.Layers / 2f) * Settings.CellHeight);
        }
    }
}