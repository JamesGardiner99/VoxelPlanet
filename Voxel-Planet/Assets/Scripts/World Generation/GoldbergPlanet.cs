using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelPlanet
{
    public class GoldbergPlanet : MonoBehaviour
    {
        public enum CellType
        {
            Pentagon,
            Hexagon
        }

        public class PlanetCell
        {
            public int index;
            public int chunkIndex;
            public CellType type;
            public Vector3 center;
            public Vector3 normal;
            public List<Vector3> corners = new List<Vector3>();
            public List<int> neighbours = new List<int>();
            public int heightLevel = 0;
        }

        private class CellCorner
        {
            public Vector3 position;
            public int faceIndex;

            public CellCorner(Vector3 position, int faceIndex)
            {
                this.position = position;
                this.faceIndex = faceIndex;
            }
        }

        private class RootFaceRegion
        {
            public Vector3 center;
            public Vector3 tangent;
            public Vector3 bitangent;
            public float minU;
            public float maxU;
            public float minV;
            public float maxV;
        }

        [Header("Planet Settings")]
        public float radius = 10f;

        [Range(0, 10)]
        public int subdivisions = 3;

        [Header("Render Distance")]
        public Transform viewer;
        public float renderDistance = 80f;
        public float renderCheckinterval = 0.25f;

        private float renderCheckTimer;

        [Header("Chunking")]
        [Range(1, 10)]
        public int chunksPerRootFaceSide = 4;

        private int TotalChunkCount => 20 * chunksPerRootFaceSide * chunksPerRootFaceSide;

        [Header("Voxel Editing")]
        public float cellHeightStep = 0.5f;
        public int minHeightLevel = 0;
        public int maxHeightLevel = 5;

        [Header("Material")]
        public Material planetMaterial;

        public List<PlanetCell> planetCells = new List<PlanetCell>();

        private readonly List<PlanetChunk> chunks = new List<PlanetChunk>();
        private readonly Dictionary<int, List<int>> triangleToCellByChunk = new Dictionary<int, List<int>>();

        private List<RootFaceRegion> rootFaceRegions = new List<RootFaceRegion>();
 
        private void Awake()
        {
            GeneratePlanet();
        }

        private void Update()
        {
            if(viewer == null)
                return;

            renderCheckTimer -= Time.deltaTime;

            if (renderCheckTimer > 0f)
                return;

            renderCheckTimer = renderCheckinterval;
            UpdateChunkVisibility();
        }

        private void GeneratePlanet()
        {
            List<Vector3> icoVertices = new List<Vector3>();
            List<int> icoTriangles = new List<int>();

            CreateIcosahedron(icoVertices, icoTriangles);

            rootFaceRegions = BuildRootFaceRegions(icoVertices, icoTriangles);

            for (int i = 0; i < subdivisions; i++)
                Subdivide(icoVertices, icoTriangles);

            BuildGoldbergCells(icoVertices, icoTriangles);
            CreateChunks();
        }

        private List<RootFaceRegion> BuildRootFaceRegions(List<Vector3> vertices, List<int> triangles)
        {
            List<RootFaceRegion> regions = new List<RootFaceRegion>();

            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 a = vertices[triangles[i]].normalized;
                Vector3 b = vertices[triangles[i + 1]].normalized;
                Vector3 c = vertices[triangles[i + 2]].normalized;

                Vector3 center = ((a + b + c) / 3f).normalized;

                Vector3 tangent = Vector3.ProjectOnPlane(a - center, center).normalized;
                Vector3 bitangent = Vector3.Cross(center, tangent).normalized;

                Vector2 uvA = ProjectToRootFace(a, center, tangent, bitangent);
                Vector2 uvB = ProjectToRootFace(b, center, tangent, bitangent);
                Vector2 uvC = ProjectToRootFace(c, center, tangent, bitangent);

                RootFaceRegion region = new RootFaceRegion
                {
                    center = center,
                    tangent = tangent,
                    bitangent = bitangent,

                    minU = Mathf.Min(uvA.x, uvB.x, uvC.x),
                    maxU = Mathf.Max(uvA.x, uvB.x, uvC.x),
                    minV = Mathf.Min(uvA.y, uvB.y, uvC.y),
                    maxV = Mathf.Max(uvA.y, uvB.y, uvC.y)
                };

                regions.Add(region);
            }

            return regions;
        }

        private Vector2 ProjectToRootFace(Vector3 point, Vector3 center, Vector3 tangent, Vector3 bitangent)
        {
            Vector3 offset = Vector3.ProjectOnPlane(point - center, center);

            return new Vector2(
                Vector3.Dot(offset, tangent),
                Vector3.Dot(offset, bitangent)
            );
        }

        private void CreateChunks()
        {
            foreach (PlanetChunk chunk in chunks)
            {
                if (chunk != null)
                    Destroy(chunk.gameObject);
            }

            chunks.Clear();
            triangleToCellByChunk.Clear();

            for (int i = 0; i < TotalChunkCount; i++)
            {
                GameObject chunkObject = new GameObject($"Planet Chunk {i}");
                chunkObject.transform.SetParent(transform, false);
                chunkObject.layer = gameObject.layer;

                PlanetChunk chunk = chunkObject.AddComponent<PlanetChunk>();
                chunk.Initialise(this, i, planetMaterial);

                chunks.Add(chunk);
                triangleToCellByChunk[i] = new List<int>();
            }

            foreach (PlanetCell cell in planetCells)
            {
                if (cell.chunkIndex >= 0 && cell.chunkIndex < chunks.Count)
                    chunks[cell.chunkIndex].cellIndices.Add(cell.index);
            }

            RebuildAllChunks();
        }

        private void RebuildAllChunks()
        {
            foreach (PlanetChunk chunk in chunks)
                chunk.RebuildMesh();
        }

        public void RebuildChunk(int chunkIndex)
        {
            if (chunkIndex < 0 || chunkIndex >= chunks.Count)
                return;

            chunks[chunkIndex].RebuildMesh();
        }

        private void RebuildCellAndNeighbourChunks(int cellIndex)
        {
            if (cellIndex < 0 || cellIndex >= planetCells.Count)
                return;

            HashSet<int> chunksToRebuild = new HashSet<int>();

            PlanetCell cell = planetCells[cellIndex];
            chunksToRebuild.Add(cell.chunkIndex);

            foreach (int neighbourIndex in cell.neighbours)
            {
                if (neighbourIndex >= 0 && neighbourIndex < planetCells.Count)
                    chunksToRebuild.Add(planetCells[neighbourIndex].chunkIndex);
            }

            foreach (int chunkIndex in chunksToRebuild)
                RebuildChunk(chunkIndex);
        }
        
        private void UpdateChunkVisibility()
        {
            float renderDistanceSqr = renderDistance * renderDistance;

            foreach(PlanetChunk chunk in chunks)
            {
                if (chunk == null)
                    continue;

                float distanceSqr =  chunk.GetDistanceSqrToPoint(viewer.position);
                bool shouldBeVisible = distanceSqr <= renderDistanceSqr;
                
                chunk.SetVisible(shouldBeVisible);
            }
        }

        public void ClearTriangleMappings(int chunkIndex)
        {
            if (!triangleToCellByChunk.ContainsKey(chunkIndex))
                triangleToCellByChunk[chunkIndex] = new List<int>();

            triangleToCellByChunk[chunkIndex].Clear();
        }

        public void RegisterTriangleCell(int chunkIndex, int cellIndex)
        {
            if (!triangleToCellByChunk.ContainsKey(chunkIndex))
                triangleToCellByChunk[chunkIndex] = new List<int>();

            triangleToCellByChunk[chunkIndex].Add(cellIndex);
        }

        public int GetCellIndexFromTriangle(PlanetChunk chunk, int triangleIndex)
        {
            if (chunk == null)
                return -1;

            if (!triangleToCellByChunk.TryGetValue(chunk.chunkIndex, out List<int> mappings))
                return -1;

            if (triangleIndex < 0 || triangleIndex >= mappings.Count)
                return -1;

            return mappings[triangleIndex];
        }

        public void RaiseCell(int cellIndex)
        {
            if (cellIndex < 0 || cellIndex >= planetCells.Count)
                return;

            planetCells[cellIndex].heightLevel = Mathf.Min(
                planetCells[cellIndex].heightLevel + 1,
                maxHeightLevel
            );

            RebuildCellAndNeighbourChunks(cellIndex);
        }

        public void LowerCell(int cellIndex)
        {
            if (cellIndex < 0 || cellIndex >= planetCells.Count)
                return;

            planetCells[cellIndex].heightLevel = Mathf.Max(
                planetCells[cellIndex].heightLevel - 1,
                minHeightLevel
            );

            RebuildCellAndNeighbourChunks(cellIndex);
        }

        private void BuildGoldbergCells(List<Vector3> vertices, List<int> triangles)
        {
            planetCells.Clear();

            List<Vector3> faceCenters = new List<Vector3>();
            List<int[]> faceVertices = new List<int[]>();
            List<int>[] adjacentFaces = new List<int>[vertices.Count];

            for (int i = 0; i < adjacentFaces.Length; i++)
                adjacentFaces[i] = new List<int>();

            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];

                Vector3 center = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                center = center.normalized * radius;

                int faceIndex = faceCenters.Count;

                faceCenters.Add(center);
                faceVertices.Add(new int[] { a, b, c });

                adjacentFaces[a].Add(faceIndex);
                adjacentFaces[b].Add(faceIndex);
                adjacentFaces[c].Add(faceIndex);
            }

            for (int vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex++)
            {
                PlanetCell cell = new PlanetCell();

                cell.index = vertexIndex;
                cell.center = vertices[vertexIndex].normalized * radius;
                cell.normal = cell.center.normalized;
                cell.chunkIndex = GetChunkIndexForCell(cell.normal);

                List<CellCorner> corners = new List<CellCorner>();

                foreach (int faceIndex in adjacentFaces[vertexIndex])
                    corners.Add(new CellCorner(faceCenters[faceIndex], faceIndex));

                SortCornersAroundCell(cell.center, cell.normal, corners);

                for (int i = 0; i < corners.Count; i++)
                    cell.corners.Add(corners[i].position);

                for (int i = 0; i < corners.Count; i++)
                {
                    int faceA = corners[i].faceIndex;
                    int faceB = corners[(i + 1) % corners.Count].faceIndex;

                    int neighbour = FindNeighbourVertex(vertexIndex, faceVertices[faceA], faceVertices[faceB]);
                    cell.neighbours.Add(neighbour);
                }

                cell.type = cell.corners.Count == 5 ? CellType.Pentagon : CellType.Hexagon;

                planetCells.Add(cell);
            }
        }

        private int GetChunkIndexForCell(Vector3 normal)
        {
            int rootFaceIndex = 0;
            float bestDot = -999f;

            for (int i = 0; i < rootFaceRegions.Count; i++)
            {
                float dot = Vector3.Dot(normal, rootFaceRegions[i].center);

                if (dot > bestDot)
                {
                    bestDot = dot;
                    rootFaceIndex = i;
                }
            }

            RootFaceRegion region = rootFaceRegions[rootFaceIndex];

            Vector2 uv = ProjectToRootFace(
                normal,
                region.center,
                region.tangent,
                region.bitangent
            );

            float u01 = Mathf.InverseLerp(region.minU, region.maxU, uv.x);
            float v01 = Mathf.InverseLerp(region.minV, region.maxV, uv.y);

            int x = Mathf.Clamp(
                Mathf.FloorToInt(u01 * chunksPerRootFaceSide),
                0,
                chunksPerRootFaceSide - 1
            );

            int y = Mathf.Clamp(
                Mathf.FloorToInt(v01 * chunksPerRootFaceSide),
                0,
                chunksPerRootFaceSide - 1
            );

            int localChunkIndex = y * chunksPerRootFaceSide + x;

            return rootFaceIndex * chunksPerRootFaceSide * chunksPerRootFaceSide + localChunkIndex;
        }

        private int FindNeighbourVertex(int currentVertex, int[] faceA, int[] faceB)
        {
            for (int i = 0; i < faceA.Length; i++)
            {
                int vertex = faceA[i];

                if (vertex == currentVertex)
                    continue;

                for (int j = 0; j < faceB.Length; j++)
                {
                    if (faceB[j] == vertex)
                        return vertex;
                }
            }

            return -1;
        }

        private void SortCornersAroundCell(Vector3 center, Vector3 normal, List<CellCorner> corners)
        {
            Vector3 tangent = Vector3.ProjectOnPlane(corners[0].position - center, normal).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent);

            corners.Sort((a, b) =>
            {
                Vector3 dirA = Vector3.ProjectOnPlane(a.position - center, normal).normalized;
                Vector3 dirB = Vector3.ProjectOnPlane(b.position - center, normal).normalized;

                float angleA = Mathf.Atan2(Vector3.Dot(dirA, bitangent), Vector3.Dot(dirA, tangent));
                float angleB = Mathf.Atan2(Vector3.Dot(dirB, bitangent), Vector3.Dot(dirB, tangent));

                return angleA.CompareTo(angleB);
            });
        }

        private void CreateIcosahedron(List<Vector3> vertices, List<int> triangles)
        {
            float goldenRatio = (1f + Mathf.Sqrt(5f)) / 2f;

            vertices.Add(new Vector3(-1, goldenRatio, 0).normalized * radius);
            vertices.Add(new Vector3(1, goldenRatio, 0).normalized * radius);
            vertices.Add(new Vector3(-1, -goldenRatio, 0).normalized * radius);
            vertices.Add(new Vector3(1, -goldenRatio, 0).normalized * radius);

            vertices.Add(new Vector3(0, -1, goldenRatio).normalized * radius);
            vertices.Add(new Vector3(0, 1, goldenRatio).normalized * radius);
            vertices.Add(new Vector3(0, -1, -goldenRatio).normalized * radius);
            vertices.Add(new Vector3(0, 1, -goldenRatio).normalized * radius);

            vertices.Add(new Vector3(goldenRatio, 0, -1).normalized * radius);
            vertices.Add(new Vector3(goldenRatio, 0, 1).normalized * radius);
            vertices.Add(new Vector3(-goldenRatio, 0, -1).normalized * radius);
            vertices.Add(new Vector3(-goldenRatio, 0, 1).normalized * radius);

            int[] tris =
            {
                0, 11, 5,
                0, 5, 1,
                0, 1, 7,
                0, 7, 10,
                0, 10, 11,

                1, 5, 9,
                5, 11, 4,
                11, 10, 2,
                10, 7, 6,
                7, 1, 8,

                3, 9, 4,
                3, 4, 2,
                3, 2, 6,
                3, 6, 8,
                3, 8, 9,

                4, 9, 5,
                2, 4, 11,
                6, 2, 10,
                8, 6, 7,
                9, 8, 1
            };

            triangles.AddRange(tris);
        }

        private void Subdivide(List<Vector3> vertices, List<int> triangles)
        {
            Dictionary<long, int> midpointCache = new Dictionary<long, int>();
            List<int> newTriangles = new List<int>();

            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];

                int ab = GetMidpoint(a, b, vertices, midpointCache);
                int bc = GetMidpoint(b, c, vertices, midpointCache);
                int ca = GetMidpoint(c, a, vertices, midpointCache);

                newTriangles.Add(a);
                newTriangles.Add(ab);
                newTriangles.Add(ca);

                newTriangles.Add(b);
                newTriangles.Add(bc);
                newTriangles.Add(ab);

                newTriangles.Add(c);
                newTriangles.Add(ca);
                newTriangles.Add(bc);

                newTriangles.Add(ab);
                newTriangles.Add(bc);
                newTriangles.Add(ca);
            }

            triangles.Clear();
            triangles.AddRange(newTriangles);
        }

        private int GetMidpoint(int indexA, int indexB, List<Vector3> vertices, Dictionary<long, int> cache)
        {
            long key = GetEdgeKey(indexA, indexB);

            if (cache.TryGetValue(key, out int midpointIndex))
                return midpointIndex;

            Vector3 midpoint = (vertices[indexA] + vertices[indexB]) * 0.5f;
            midpoint = midpoint.normalized * radius;

            midpointIndex = vertices.Count;
            vertices.Add(midpoint);

            cache.Add(key, midpointIndex);

            return midpointIndex;
        }

        private long GetEdgeKey(int indexA, int indexB)
        {
            int min = Mathf.Min(indexA, indexB);
            int max = Mathf.Max(indexA, indexB);

            return ((long)min << 32) | (uint)max;
        }

        public int GetClosestCellFromHit(PlanetChunk chunk, Vector3 worldHitPoint)
        {
            if (chunk == null)
                return -1;

            Vector3 localHitPoint = transform.InverseTransformPoint(worldHitPoint);

            int bestCellIndex = -1;
            float bestDistance = float.MaxValue;

            foreach (int cellIndex in chunk.cellIndices)
            {
                if (cellIndex < 0 || cellIndex >= planetCells.Count)
                    continue;

                PlanetCell cell = planetCells[cellIndex];

                float heightOffset = cell.heightLevel * cellHeightStep;
                Vector3 cellTopCenter = cell.center + cell.normal * heightOffset;

                float distance = Vector3.SqrMagnitude(localHitPoint - cellTopCenter);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestCellIndex = cellIndex;
                }
            }

            return bestCellIndex;
        }
    }
}