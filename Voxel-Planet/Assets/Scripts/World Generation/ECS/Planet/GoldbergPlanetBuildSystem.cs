using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace VoxelPlanet
{
    public partial class GoldbergPlanetBuildSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<GoldbergPlanetSettings>();
        }
        protected override void OnUpdate()
        {
            EntityManager entityManager = EntityManager;

            EntityQuery query = GetEntityQuery(
                typeof(GoldbergPlanetSettings),
                typeof(GoldbergCell),
                typeof(GoldbergCellVertex)
            );

            using Unity.Collections.NativeArray<Entity> entities =
                query.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];

                GoldbergPlanetSettings settings =
                    entityManager.GetComponentData<GoldbergPlanetSettings>(entity);

                if (settings.NeedsBuild == 0)
                    continue;

                DynamicBuffer<GoldbergCell> cells =
                    entityManager.GetBuffer<GoldbergCell>(entity);

                DynamicBuffer<GoldbergCellVertex> cellVertices =
                    entityManager.GetBuffer<GoldbergCellVertex>(entity);

                BuildGoldbergPlanet(
                    entity,
                    entityManager,
                    ref settings,
                    cells,
                    cellVertices
                );

                settings.NeedsBuild = 0;
                settings.NeedsColumnGeneration = 1;

                entityManager.SetComponentData(entity, settings);
            }
        }

        private void BuildGoldbergPlanet(
            Entity entity,
            EntityManager entityManager,
            ref GoldbergPlanetSettings settings,
            DynamicBuffer<GoldbergCell> cells,
            DynamicBuffer<GoldbergCellVertex> cellVertices)
        {
            cells.Clear();
            cellVertices.Clear();

            List<Vector3> sphereVertices = new List<Vector3>();
            List<int3> sphereTriangles = new List<int3>();

            BuildIcosphere(settings.Radius, settings.Subdivisions, sphereVertices, sphereTriangles);

            List<Vector3> meshVertices = new List<Vector3>();
            List<int> meshTriangles = new List<int>();
            List<Vector3> meshNormals = new List<Vector3>();

            Dictionary<int, List<int>> vertexToTriangles = new Dictionary<int, List<int>>();

            for (int i = 0; i < sphereTriangles.Count; i++)
            {
                int3 tri = sphereTriangles[i];

                AddTriangleReference(vertexToTriangles, tri.x, i);
                AddTriangleReference(vertexToTriangles, tri.y, i);
                AddTriangleReference(vertexToTriangles, tri.z, i);
            }

            List<Vector3> triangleCenters = new List<Vector3>();

            for (int i = 0; i < sphereTriangles.Count; i++)
            {
                int3 tri = sphereTriangles[i];

                Vector3 center =
                    (sphereVertices[tri.x] +
                     sphereVertices[tri.y] +
                     sphereVertices[tri.z]) / 3f;

                center = center.normalized * settings.Radius;
                triangleCenters.Add(center);
            }

            foreach (var pair in vertexToTriangles)
            {
                Vector3 cellCenter = sphereVertices[pair.Key].normalized * settings.Radius;
                Vector3 normal = cellCenter.normalized;

                List<Vector3> polygon = new List<Vector3>();

                foreach (int triIndex in pair.Value)
                {
                    polygon.Add(triangleCenters[triIndex]);
                }

                SortPolygonAroundNormal(polygon, cellCenter, normal);

                int firstVertexIndex = cellVertices.Length;

                for (int i = 0; i < polygon.Count; i++)
                {
                    cellVertices.Add(new GoldbergCellVertex
                    {
                        Position = polygon[i]
                    });
                }

                cells.Add(new GoldbergCell
                {
                    Center = cellCenter,
                    Normal = normal,
                    FirstVertexIndex = firstVertexIndex,
                    VertexCount = polygon.Count
                });

                AddCellTopFace(
                    polygon,
                    normal,
                    meshVertices,
                    meshTriangles,
                    meshNormals
                );
            }

            Mesh mesh = new Mesh();
        }

        private void BuildIcosphere(
            float radius,
            int subdivisions,
            List<Vector3> vertices,
            List<int3> triangles)
        {
            vertices.Clear();
            triangles.Clear();

            float t = (1f + Mathf.Sqrt(5f)) / 2f;

            AddVertex(new Vector3(-1, t, 0), radius, vertices);
            AddVertex(new Vector3(1, t, 0), radius, vertices);
            AddVertex(new Vector3(-1, -t, 0), radius, vertices);
            AddVertex(new Vector3(1, -t, 0), radius, vertices);

            AddVertex(new Vector3(0, -1, t), radius, vertices);
            AddVertex(new Vector3(0, 1, t), radius, vertices);
            AddVertex(new Vector3(0, -1, -t), radius, vertices);
            AddVertex(new Vector3(0, 1, -t), radius, vertices);

            AddVertex(new Vector3(t, 0, -1), radius, vertices);
            AddVertex(new Vector3(t, 0, 1), radius, vertices);
            AddVertex(new Vector3(-t, 0, -1), radius, vertices);
            AddVertex(new Vector3(-t, 0, 1), radius, vertices);

            triangles.Add(new int3(0, 11, 5));
            triangles.Add(new int3(0, 5, 1));
            triangles.Add(new int3(0, 1, 7));
            triangles.Add(new int3(0, 7, 10));
            triangles.Add(new int3(0, 10, 11));

            triangles.Add(new int3(1, 5, 9));
            triangles.Add(new int3(5, 11, 4));
            triangles.Add(new int3(11, 10, 2));
            triangles.Add(new int3(10, 7, 6));
            triangles.Add(new int3(7, 1, 8));

            triangles.Add(new int3(3, 9, 4));
            triangles.Add(new int3(3, 4, 2));
            triangles.Add(new int3(3, 2, 6));
            triangles.Add(new int3(3, 6, 8));
            triangles.Add(new int3(3, 8, 9));

            triangles.Add(new int3(4, 9, 5));
            triangles.Add(new int3(2, 4, 11));
            triangles.Add(new int3(6, 2, 10));
            triangles.Add(new int3(8, 6, 7));
            triangles.Add(new int3(9, 8, 1));

            Dictionary<EdgeKey, int> midpointCache = new Dictionary<EdgeKey, int>();

            for (int s = 0; s < subdivisions; s++)
            {
                List<int3> newTriangles = new List<int3>();

                foreach (int3 tri in triangles)
                {
                    int a = GetMidpoint(tri.x, tri.y, radius, vertices, midpointCache);
                    int b = GetMidpoint(tri.y, tri.z, radius, vertices, midpointCache);
                    int c = GetMidpoint(tri.z, tri.x, radius, vertices, midpointCache);

                    newTriangles.Add(new int3(tri.x, a, c));
                    newTriangles.Add(new int3(tri.y, b, a));
                    newTriangles.Add(new int3(tri.z, c, b));
                    newTriangles.Add(new int3(a, b, c));
                }

                triangles.Clear();
                triangles.AddRange(newTriangles);
            }
        }

        private void AddVertex(Vector3 position, float radius, List<Vector3> vertices)
        {
            vertices.Add(position.normalized * radius);
        }

        private int GetMidpoint(
            int a,
            int b,
            float radius,
            List<Vector3> vertices,
            Dictionary<EdgeKey, int> cache)
        {
            EdgeKey key = new EdgeKey(a, b);

            if (cache.TryGetValue(key, out int index))
                return index;

            Vector3 midpoint = ((vertices[a] + vertices[b]) * 0.5f).normalized * radius;

            index = vertices.Count;
            vertices.Add(midpoint);
            cache[key] = index;

            return index;
        }

        private void AddTriangleReference(Dictionary<int, List<int>> map, int vertexIndex, int triangleIndex)
        {
            if (!map.TryGetValue(vertexIndex, out List<int> list))
            {
                list = new List<int>();
                map[vertexIndex] = list;
            }

            list.Add(triangleIndex);
        }

        private void SortPolygonAroundNormal(List<Vector3> polygon, Vector3 center, Vector3 normal)
        {
            Vector3 tangent = Vector3.Cross(normal, Vector3.up);

            if (tangent.sqrMagnitude < 0.001f)
                tangent = Vector3.Cross(normal, Vector3.right);

            tangent.Normalize();

            Vector3 bitangent = Vector3.Cross(normal, tangent);

            polygon.Sort((a, b) =>
            {
                Vector3 da = a - center;
                Vector3 db = b - center;

                float angleA = Mathf.Atan2(Vector3.Dot(da, bitangent), Vector3.Dot(da, tangent));
                float angleB = Mathf.Atan2(Vector3.Dot(db, bitangent), Vector3.Dot(db, tangent));

                return angleA.CompareTo(angleB);
            });
        }

        private void AddCellTopFace(
            List<Vector3> polygon,
            Vector3 normal,
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
            normals.Add(normal);

            int first = vertices.Count;

            for (int i = 0; i < polygon.Count; i++)
            {
                vertices.Add(polygon[i]);
                normals.Add(normal);
            }

            for (int i = 0; i < polygon.Count; i++)
            {
                int next = (i + 1) % polygon.Count;

                triangles.Add(centerIndex);
                triangles.Add(first + i);
                triangles.Add(first + next);
            }
        }

        private readonly struct EdgeKey
        {
            private readonly int a;
            private readonly int b;

            public EdgeKey(int x, int y)
            {
                if (x < y)
                {
                    a = x;
                    b = y;
                }
                else
                {
                    a = y;
                    b = x;
                }
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (a * 397) ^ b;
                }
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeKey other &&
                       a == other.a &&
                       b == other.b;
            }
        }
    }
}