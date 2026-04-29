using System;
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

    public struct GoldbergMeshVertexKey : IEquatable<GoldbergMeshVertexKey>
    {
        private const float Precision = 100000f;

        private int px;
        private int py;
        private int pz;

        private int nx;
        private int ny;
        private int nz;

        public GoldbergMeshVertexKey(GoldbergMeshVertex vertex)
        {
            px = (int)math.round(vertex.Position.x * Precision);
            py = (int)math.round(vertex.Position.y * Precision);
            pz = (int)math.round(vertex.Position.z * Precision);

            nx = (int)math.round(vertex.Normal.x * Precision);
            ny = (int)math.round(vertex.Normal.y * Precision);
            nz = (int)math.round(vertex.Normal.z * Precision);
        }

        public bool Equals(GoldbergMeshVertexKey other)
        {
            return px == other.px &&
                   py == other.py &&
                   pz == other.pz &&
                   nx == other.nx &&
                   ny == other.ny &&
                   nz == other.nz;
        }

        public override bool Equals(object obj)
        {
            return obj is GoldbergMeshVertexKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + px;
                hash = hash * 31 + py;
                hash = hash * 31 + pz;
                hash = hash * 31 + nx;
                hash = hash * 31 + ny;
                hash = hash * 31 + nz;
                return hash;
            }
        }
    }

    [BurstCompile]
    public struct GoldbergVoxelMeshBuildJob : IJob
    {
        [ReadOnly] public NativeArray<GoldbergCell> Cells;
        [ReadOnly] public NativeArray<GoldbergCellVertex> CellVertices;
        [ReadOnly] public NativeArray<VoxelColumn> Columns;
        [ReadOnly] public NativeArray<int> CellSurfaceLayers;
        [ReadOnly] public NativeArray<int> NeighbourLookup;
        [ReadOnly] public NativeArray<int> ChunkColumnIndices;

        [ReadOnly] public int MaxEdgesPerCell;

        public GoldbergPlanetSettings Settings;

        public NativeList<GoldbergMeshVertex> Vertices;
        public NativeList<int> Triangles;
        public NativeParallelHashMap<GoldbergMeshVertexKey, int> VertexLookup;

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
            if (cellIndex < 0 || edgeIndex < 0)
                return -1;

            if (edgeIndex >= MaxEdgesPerCell)
                return -1;

            int lookupIndex = cellIndex * MaxEdgesPerCell + edgeIndex;

            if (lookupIndex < 0 || lookupIndex >= NeighbourLookup.Length)
                return -1;

            return NeighbourLookup[lookupIndex];
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

            int centerIndex = GetOrAddVertex(new GoldbergMeshVertex
            {
                Position = center,
                Normal = expectedNormal
            });

            FixedList128Bytes<int> ringIndices = default;
            FixedList512Bytes<float3> ringPositions = default;

            for (int i = 0; i < cell.VertexCount; i++)
            {
                float3 p = CellVertices[cell.FirstVertexIndex + i].Position;
                float3 position = math.normalize(p) * radius;

                int index = GetOrAddVertex(new GoldbergMeshVertex
                {
                    Position = position,
                    Normal = expectedNormal
                });

                ringIndices.Add(index);
                ringPositions.Add(position);
            }

            for (int i = 0; i < cell.VertexCount; i++)
            {
                int next = (i + 1) % cell.VertexCount;

                float3 a = ringPositions[i];
                float3 b = ringPositions[next];

                float3 triNormal =
                    math.normalize(math.cross(a - center, b - center));

                if (math.dot(triNormal, expectedNormal) >= 0f)
                {
                    Triangles.Add(centerIndex);
                    Triangles.Add(ringIndices[i]);
                    Triangles.Add(ringIndices[next]);
                }
                else
                {
                    Triangles.Add(centerIndex);
                    Triangles.Add(ringIndices[next]);
                    Triangles.Add(ringIndices[i]);
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

            int i0 = GetOrAddVertex(new GoldbergMeshVertex { Position = v0, Normal = normal });
            int i1 = GetOrAddVertex(new GoldbergMeshVertex { Position = v1, Normal = normal });
            int i2 = GetOrAddVertex(new GoldbergMeshVertex { Position = v2, Normal = normal });
            int i3 = GetOrAddVertex(new GoldbergMeshVertex { Position = v3, Normal = normal });

            Triangles.Add(i0);
            Triangles.Add(i1);
            Triangles.Add(i2);

            Triangles.Add(i0);
            Triangles.Add(i2);
            Triangles.Add(i3);
        }

        private int GetOrAddVertex(GoldbergMeshVertex vertex)
        {
            GoldbergMeshVertexKey key = new GoldbergMeshVertexKey(vertex);

            if (VertexLookup.TryGetValue(key, out int existingIndex))
                return existingIndex;

            int newIndex = Vertices.Length;
            Vertices.Add(vertex);
            VertexLookup.TryAdd(key, newIndex);

            return newIndex;
        }

        private float GetRadiusForLayer(int layer)
        {
            return Settings.Radius +
                   ((layer - Settings.Layers / 2f) * Settings.CellHeight);
        }
    }
}
