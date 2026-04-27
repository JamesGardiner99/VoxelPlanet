using System.Collections.Generic;
using UnityEngine;

namespace VoxelPlanet
{
    public class GoldbergPlanet : MonoBehaviour
    {
        public enum CellType
        {
            Pentagon,
            Hexagon
        }

        public enum BlockType
        {
            Air,
            Grass,
            Dirt,
            Stone,
            Water
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

            // Missing layer = Air
            public Dictionary<int, BlockType> blocks = new Dictionary<int, BlockType>();
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

        [Header("Streaming")]
        public Transform viewer;
        public float renderCheckInterval = 0.25f;
        public int initialChunkPoolSize = 64;
        public float loadDistance = 180f;
        public float unloadDistance = 220f;
        public int maxChunkLoadsPerTick = 4;

        private float renderCheckTimer;

        [Header("Chunking")]
        [Range(1, 10)]
        public int chunksPerRootFaceSide = 4;

        private int TotalChunkCount => 20 * chunksPerRootFaceSide * chunksPerRootFaceSide;

        [Header("Voxel Blocks")]
        public float blockHeight = 0.5f;

        [Header("Materials")]
        public Material grassMaterial;
        public Material dirtMaterial;
        public Material stoneMaterial;
        public Material waterMaterial;

        [Header("Voxel World")]
        public VoxelWorld voxelWorld;

        [Header("GPU Terrain Generation")]
        public ComputeShader terrainCompute;
        public float terrainHeightAmplitude = 4f;
        public float terrainNoiseScale = 3f;
        public float terrainSeed = 12345f;

        [Header("Terrain Shape")]
        public float continentScale = 1.2f;
        public float continentStrength = 8f;
        public float detailScale = 8f;
        public float detailStrength = 2f;

        [Header("Ocean")]
        public int oceanLevel = 0;

        public List<PlanetCell> planetCells = new List<PlanetCell>();

        private readonly Dictionary<int, PlanetChunk> activeChunks = new Dictionary<int, PlanetChunk>();
        private readonly Queue<PlanetChunk> chunkPool = new Queue<PlanetChunk>();
        private readonly Dictionary<int, List<int>> cellsByChunkIndex = new Dictionary<int, List<int>>();

        private List<RootFaceRegion> rootFaceRegions = new List<RootFaceRegion>();

        private struct CellInput
        {
            public Vector3 normal;
        }

        private struct TerrainOutput
        {
            public int surfaceLayer;
        }

        private void Awake()
        {
            if(voxelWorld == null)
            {
                voxelWorld = GetComponent<VoxelWorld>();
            }

            GeneratePlanet();
        }

        private void Update()
        {
            if (viewer == null)
                return;

            renderCheckTimer -= Time.deltaTime;

            if (renderCheckTimer > 0f)
                return;

            renderCheckTimer = renderCheckInterval;
            UpdateChunkStreaming(false);
        }

        private void GeneratePlanet()
        {
            List<Vector3> icoVertices = new List<Vector3>();
            List<int> icoTriangles = new List<int>();

            CreateIcosahedron(icoVertices, icoTriangles);

            List<int> rootTriangles = new List<int>(icoTriangles);
            rootFaceRegions = BuildRootFaceRegions(icoVertices, rootTriangles);

            for (int i = 0; i < subdivisions; i++)
                Subdivide(icoVertices, icoTriangles);

            BuildGoldbergCells(icoVertices, icoTriangles);
            GenerateInitialBlocksWithCompute();
            CreateChunks();
        }

        private void GenerateInitialBlocksWithCompute()
        {
            if (terrainCompute == null)
            {
                foreach (PlanetCell cell in planetCells)
                    GenerateInitialBlocks(cell, 0);

                return;
            }

            int cellCount = planetCells.Count;

            CellInput[] inputs = new CellInput[cellCount];
            TerrainOutput[] outputs = new TerrainOutput[cellCount];

            for (int i = 0; i < cellCount; i++)
            {
                inputs[i] = new CellInput
                {
                    normal = planetCells[i].normal
                };
            }

            ComputeBuffer inputBuffer = new ComputeBuffer(cellCount, sizeof(float) * 3);
            ComputeBuffer outputBuffer = new ComputeBuffer(cellCount, sizeof(int));

            inputBuffer.SetData(inputs);

            int kernel = terrainCompute.FindKernel("CSMain");

            terrainCompute.SetInt("_CellCount", cellCount);
            terrainCompute.SetFloat("_HeightAmplitude", terrainHeightAmplitude);
            terrainCompute.SetFloat("_NoiseScale", terrainNoiseScale);
            terrainCompute.SetFloat("_Seed", terrainSeed);

            terrainCompute.SetFloat("_ContinentScale", continentScale);
            terrainCompute.SetFloat("_ContinentStrength", continentStrength);
            terrainCompute.SetFloat("_DetailScale", detailScale);
            terrainCompute.SetFloat("_DetailStrength", detailStrength);

            terrainCompute.SetBuffer(kernel, "_Cells", inputBuffer);
            terrainCompute.SetBuffer(kernel, "_Results", outputBuffer);

            int threadGroups = Mathf.CeilToInt(cellCount / 64f);
            terrainCompute.Dispatch(kernel, threadGroups, 1, 1);

            outputBuffer.GetData(outputs);

            inputBuffer.Release();
            outputBuffer.Release();

            for (int i = 0; i < cellCount; i++)
            {
                GenerateInitialBlocks(planetCells[i], outputs[i].surfaceLayer);
            }
        }

        private void CreateChunks()
        {
            activeChunks.Clear();
            chunkPool.Clear();
            cellsByChunkIndex.Clear();

            for (int i = 0; i < TotalChunkCount; i++)
            {
                cellsByChunkIndex[i] = new List<int>();
            }

            foreach (PlanetCell cell in planetCells)
            {
                if (cell.chunkIndex >= 0 && cell.chunkIndex < TotalChunkCount)
                    cellsByChunkIndex[cell.chunkIndex].Add(cell.index);
            }

            CreateChunkPool();
            UpdateChunkStreaming(true);
        }

        private void CreateChunkPool()
        {
            for (int i = 0; i < initialChunkPoolSize; i++)
            {
                PlanetChunk chunk = CreatePooledChunk();
                chunkPool.Enqueue(chunk);
            }
        }

        private PlanetChunk CreatePooledChunk()
        {
            GameObject chunkObject = new GameObject("Pooled Planet Chunk");
            chunkObject.transform.SetParent(transform, false);
            chunkObject.layer = gameObject.layer;

            PlanetChunk chunk = chunkObject.AddComponent<PlanetChunk>();
            chunk.Initialise(this);
            chunk.ClearChunk();

            return chunk;
        }

        private PlanetChunk GetChunkFromPool()
        {
            if (chunkPool.Count > 0)
                return chunkPool.Dequeue();

            return CreatePooledChunk();
        }

        private void ReturnChunkToPool(PlanetChunk chunk)
        {
            if (chunk == null)
                return;

            chunk.ClearChunk();
            chunkPool.Enqueue(chunk);
        }

        private void LoadChunk(int chunkIndex)
        {
            if (activeChunks.ContainsKey(chunkIndex))
                return;

            if (!cellsByChunkIndex.TryGetValue(chunkIndex, out List<int> cellIndices))
                return;

            if (cellIndices.Count == 0)
                return;

            PlanetChunk chunk = GetChunkFromPool();
            chunk.AssignChunk(chunkIndex, cellIndices);

            activeChunks.Add(chunkIndex, chunk);
        }

        private void UnloadChunk(int chunkIndex)
        {
            if (!activeChunks.TryGetValue(chunkIndex, out PlanetChunk chunk))
                return;

            activeChunks.Remove(chunkIndex);
            ReturnChunkToPool(chunk);
        }

        private void UpdateChunkStreaming(bool force)
        {
            if (viewer == null)
                return;

            int loadedThisTick = 0;

            for (int chunkIndex = 0; chunkIndex < TotalChunkCount; chunkIndex++)
            {
                float distanceSqr = GetChunkDistanceSqrToViewer(chunkIndex);
                bool isLoaded = activeChunks.ContainsKey(chunkIndex);

                if (!isLoaded && distanceSqr <= loadDistance * loadDistance)
                {
                    if (!force && loadedThisTick >= maxChunkLoadsPerTick)
                        continue;

                    LoadChunk(chunkIndex);
                    loadedThisTick++;
                }
            }

            List<int> chunksToUnload = new List<int>();

            foreach (var pair in activeChunks)
            {
                int chunkIndex = pair.Key;
                float distanceSqr = GetChunkDistanceSqrToViewer(chunkIndex);

                if (distanceSqr > unloadDistance * unloadDistance)
                    chunksToUnload.Add(chunkIndex);
            }

            foreach (int chunkIndex in chunksToUnload)
                UnloadChunk(chunkIndex);
        }

        public void RebuildChunk(int chunkIndex)
        {
            if (!activeChunks.TryGetValue(chunkIndex, out PlanetChunk chunk))
                return;

            chunk.RebuildMesh();
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

        private float GetChunkDistanceSqrToViewer(int chunkIndex)
        {
            if (viewer == null)
                return float.MaxValue;

            if (!cellsByChunkIndex.TryGetValue(chunkIndex, out List<int> cellIndices))
                return float.MaxValue;

            if (cellIndices.Count == 0)
                return float.MaxValue;

            Vector3 center = Vector3.zero;
            int validCount = 0;

            foreach (int cellIndex in cellIndices)
            {
                if (cellIndex < 0 || cellIndex >= planetCells.Count)
                    continue;

                center += planetCells[cellIndex].center;
                validCount++;
            }

            if (validCount == 0)
                return float.MaxValue;

            center /= validCount;

            Vector3 worldCenter = transform.TransformPoint(center);
            return (worldCenter - viewer.position).sqrMagnitude;
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

        private void GenerateInitialBlocks(PlanetCell cell, int surfaceLayer)
        {
            voxelWorld.GenerateBasicColumn(cell.index, surfaceLayer, oceanLevel);
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

        private int GetMidpoint(
            int indexA,
            int indexB,
            List<Vector3> vertices,
            Dictionary<long, int> cache)
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

        public bool HasBlock(int cellIndex, int layer)
        {
            if (cellIndex < 0 || cellIndex >= planetCells.Count)
                return false;

            return planetCells[cellIndex].blocks.ContainsKey(layer);
        }

        public BlockType GetBlock(int cellIndex, int layer)
        {
            if (!HasBlock(cellIndex, layer))
                return BlockType.Air;

            return planetCells[cellIndex].blocks[layer];
        }

        public bool HasSolidBlock(int cellIndex, int layer)
        {
            BlockType block = GetBlock(cellIndex, layer);
            return block != BlockType.Air && block != BlockType.Water;
        }

        public int GetHighestSolidLayer(int cellIndex)
        {
            if (cellIndex < 0 || cellIndex >= planetCells.Count)
                return int.MinValue;

            int highest = int.MinValue;

            foreach (var pair in planetCells[cellIndex].blocks)
            {
                if (pair.Value == BlockType.Water)
                    continue;

                if (pair.Key > highest)
                    highest = pair.Key;
            }

            return highest;
        }

        public void RaiseCell(int cellIndex)
        {
            if (cellIndex < 0 || cellIndex >= planetCells.Count)
                return;

            voxelWorld.AddBlockAboveTop(cellIndex, VoxelWorld.BlockType.Grass);

            RebuildCellAndNeighbourChunks(cellIndex);
        }

        public void LowerCell(int cellIndex)
        {
            if (cellIndex < 0 || cellIndex >= planetCells.Count)
                return;

            voxelWorld.RemoveTopSolidBlock(cellIndex);

            RebuildCellAndNeighbourChunks(cellIndex);
        }
        public int GetMaterialIndex(BlockType blockType)
        {
            switch (blockType)
            {
                case BlockType.Grass:
                    return 0;
                case BlockType.Dirt:
                    return 1;
                case BlockType.Stone:
                    return 2;
                default:
                    return 0;
            }
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

                int highestLayer = GetHighestSolidLayer(cellIndex);
                float heightOffset = highestLayer == int.MinValue
                    ? 0f
                    : (highestLayer + 1) * blockHeight;

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