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
                GoldbergCell cell = Cells[column.CellIndex];

                int surfaceLayer = column.SurfaceLayer;

                float surfaceRadius =
                    Settings.Radius +
                    ((surfaceLayer - Settings.Layers / 2f) * Settings.CellHeight);

                float3 normal = math.normalize(cell.Normal);

                AddPolygonFace(cell, surfaceRadius, normal);
                AddFullColumnWalls(cell, surfaceLayer);
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

            Triangles.Add(start + 2);
            Triangles.Add(start + 1);
            Triangles.Add(start + 0);

            Triangles.Add(start + 3);
            Triangles.Add(start + 2);
            Triangles.Add(start + 0);
        }
    }
}