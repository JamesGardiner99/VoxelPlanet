using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelPlanet
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class PlanetChunk : MonoBehaviour
    {
        public GoldbergPlanet planet;
        public int chunkIndex;
        public List<int> cellIndices = new List<int>();

        private MeshFilter meshFilter;
        private MeshCollider meshCollider;
        private MeshRenderer meshRenderer;

        private GameObject waterObject;
        private MeshFilter waterMeshFilter;
        private MeshRenderer waterMeshRenderer;

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshCollider = GetComponent<MeshCollider>();
            meshRenderer = GetComponent<MeshRenderer>();
        }

        public void Initialise(GoldbergPlanet owner)
        {
            planet = owner;

            meshFilter = GetComponent<MeshFilter>();
            meshCollider = GetComponent<MeshCollider>();
            meshRenderer = GetComponent<MeshRenderer>();

            meshRenderer.sharedMaterials = new Material[]
            {
                planet.grassMaterial,
                planet.dirtMaterial,
                planet.stoneMaterial
            };

            if (waterObject == null)
            {
                waterObject = new GameObject("Water Mesh");
                waterObject.transform.SetParent(transform, false);

                waterMeshFilter = waterObject.AddComponent<MeshFilter>();
                waterMeshRenderer = waterObject.AddComponent<MeshRenderer>();
            }

            waterMeshRenderer.sharedMaterial = planet.waterMaterial;
        }

        public void AssignChunk(int newChunkIndex, List<int> newCellIndices)
        {
            chunkIndex = newChunkIndex;

            cellIndices.Clear();
            cellIndices.AddRange(newCellIndices);

            gameObject.name = $"Planet Chunk {chunkIndex}";
            gameObject.SetActive(true);

            RebuildMesh();
        }

        public void ClearChunk()
        {
            cellIndices.Clear();

            if (meshFilter != null)
                meshFilter.sharedMesh = null;

            if (meshCollider != null)
                meshCollider.sharedMesh = null;

            if (waterMeshFilter != null)
                waterMeshFilter.sharedMesh = null;

            gameObject.SetActive(false);
        }

        public void RebuildMesh()
        {
            if (planet == null)
                return;

            List<Vector3> terrainVertices = new List<Vector3>();
            List<Vector3> terrainNormals = new List<Vector3>();

            List<int>[] terrainTriangles =
            {
                new List<int>(),
                new List<int>(),
                new List<int>()
            };

            List<Vector3> waterVertices = new List<Vector3>();
            List<int> waterTriangles = new List<int>();
            List<Vector3> waterNormals = new List<Vector3>();

            foreach (int cellIndex in cellIndices)
            {
                if (cellIndex < 0 || cellIndex >= planet.planetCells.Count)
                    continue;

                BuildCell(
                    cellIndex,
                    terrainVertices,
                    terrainTriangles,
                    terrainNormals,
                    waterVertices,
                    waterTriangles,
                    waterNormals
                );
            }

            BuildTerrainMesh(terrainVertices, terrainTriangles, terrainNormals);
            BuildWaterMesh(waterVertices, waterTriangles, waterNormals);
        }

        private void BuildTerrainMesh(
            List<Vector3> vertices,
            List<int>[] triangles,
            List<Vector3> normals)
        {
            Mesh terrainMesh = new Mesh();
            terrainMesh.name = $"Planet Chunk {chunkIndex}";
            terrainMesh.indexFormat = IndexFormat.UInt32;

            terrainMesh.SetVertices(vertices);
            terrainMesh.SetNormals(normals);
            terrainMesh.subMeshCount = 3;

            terrainMesh.SetTriangles(triangles[0], 0);
            terrainMesh.SetTriangles(triangles[1], 1);
            terrainMesh.SetTriangles(triangles[2], 2);

            terrainMesh.RecalculateBounds();

            meshFilter.sharedMesh = terrainMesh;

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = terrainMesh;
        }

        private void BuildWaterMesh(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector3> normals)
        {
            Mesh waterMesh = new Mesh();
            waterMesh.name = $"Water Chunk {chunkIndex}";
            waterMesh.indexFormat = IndexFormat.UInt32;

            waterMesh.SetVertices(vertices);
            waterMesh.SetTriangles(triangles, 0);
            waterMesh.SetNormals(normals);
            waterMesh.RecalculateBounds();

            waterMeshFilter.sharedMesh = waterMesh;
        }

        private void BuildCell(
            int cellIndex,
            List<Vector3> terrainVertices,
            List<int>[] terrainTriangles,
            List<Vector3> terrainNormals,
            List<Vector3> waterVertices,
            List<int> waterTriangles,
            List<Vector3> waterNormals)
        {
            GoldbergPlanet.PlanetCell cell = planet.planetCells[cellIndex];

            foreach (var blockPair in cell.blocks)
            {
                int layer = blockPair.Key;
                GoldbergPlanet.BlockType blockType = blockPair.Value;

                if (blockType == GoldbergPlanet.BlockType.Air)
                    continue;

                if (blockType == GoldbergPlanet.BlockType.Water)
                {
                    BuildWaterBlock(
                        cell,
                        layer,
                        waterVertices,
                        waterTriangles,
                        waterNormals
                    );
                }
                else
                {
                    BuildSolidBlock(
                        cellIndex,
                        cell,
                        layer,
                        blockType,
                        terrainVertices,
                        terrainTriangles,
                        terrainNormals
                    );
                }
            }
        }

        private void BuildSolidBlock(
            int cellIndex,
            GoldbergPlanet.PlanetCell cell,
            int layer,
            GoldbergPlanet.BlockType blockType,
            List<Vector3> vertices,
            List<int>[] triangles,
            List<Vector3> normals)
        {
            int materialIndex = planet.GetMaterialIndex(blockType);

            float bottomOffset = layer * planet.blockHeight;
            float topOffset = (layer + 1) * planet.blockHeight;

            List<Vector3> bottomCorners = new List<Vector3>();
            List<Vector3> topCorners = new List<Vector3>();

            for (int i = 0; i < cell.corners.Count; i++)
            {
                Vector3 cornerNormal = cell.corners[i].normalized;

                bottomCorners.Add(cell.corners[i] + cornerNormal * bottomOffset);
                topCorners.Add(cell.corners[i] + cornerNormal * topOffset);
            }

            Vector3 bottomCenter = cell.center + cell.normal * bottomOffset;
            Vector3 topCenter = cell.center + cell.normal * topOffset;

            if (!planet.HasSolidBlock(cellIndex, layer + 1))
            {
                AddTerrainPolygonFace(
                    topCenter,
                    topCorners,
                    cell.normal,
                    materialIndex,
                    vertices,
                    triangles,
                    normals
                );
            }

            if (!planet.HasSolidBlock(cellIndex, layer - 1))
            {
                List<Vector3> reversedBottomCorners = new List<Vector3>(bottomCorners);
                reversedBottomCorners.Reverse();

                AddTerrainPolygonFace(
                    bottomCenter,
                    reversedBottomCorners,
                    -cell.normal,
                    materialIndex,
                    vertices,
                    triangles,
                    normals
                );
            }

            for (int i = 0; i < cell.corners.Count; i++)
            {
                int neighbourIndex = cell.neighbours[i];

                bool neighbourHasSolidBlock =
                    neighbourIndex >= 0 &&
                    neighbourIndex < planet.planetCells.Count &&
                    planet.HasSolidBlock(neighbourIndex, layer);

                if (neighbourHasSolidBlock)
                    continue;

                Vector3 bottomA = bottomCorners[i];
                Vector3 bottomB = bottomCorners[(i + 1) % bottomCorners.Count];

                Vector3 topA = topCorners[i];
                Vector3 topB = topCorners[(i + 1) % topCorners.Count];

                AddTerrainQuadFace(
                    bottomA,
                    bottomB,
                    topA,
                    topB,
                    materialIndex,
                    vertices,
                    triangles,
                    normals
                );
            }
        }

        private void BuildWaterBlock(
            GoldbergPlanet.PlanetCell cell,
            int layer,
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector3> normals)
        {
            if (planet.GetBlock(cell.index, layer + 1) == GoldbergPlanet.BlockType.Water)
                return;

            float topOffset = (layer + 1) * planet.blockHeight;

            Vector3 topCenter = cell.center + cell.normal * topOffset;

            List<Vector3> topCorners = new List<Vector3>();

            for (int i = 0; i < cell.corners.Count; i++)
            {
                Vector3 cornerNormal = cell.corners[i].normalized;
                topCorners.Add(cell.corners[i] + cornerNormal * topOffset);
            }

            AddWaterPolygonFace(
                topCenter,
                topCorners,
                cell.normal,
                vertices,
                triangles,
                normals
            );
        }

        private void AddTerrainPolygonFace(
            Vector3 center,
            List<Vector3> corners,
            Vector3 normal,
            int materialIndex,
            List<Vector3> vertices,
            List<int>[] triangles,
            List<Vector3> normals)
        {
            int centerIndex = vertices.Count;

            vertices.Add(center);
            normals.Add(normal.normalized);

            int startIndex = vertices.Count;

            for (int i = 0; i < corners.Count; i++)
            {
                vertices.Add(corners[i]);
                normals.Add(normal.normalized);
            }

            for (int i = 0; i < corners.Count; i++)
            {
                int current = startIndex + i;
                int next = startIndex + ((i + 1) % corners.Count);

                triangles[materialIndex].Add(centerIndex);
                triangles[materialIndex].Add(current);
                triangles[materialIndex].Add(next);
            }
        }

        private void AddTerrainQuadFace(
            Vector3 bottomA,
            Vector3 bottomB,
            Vector3 topA,
            Vector3 topB,
            int materialIndex,
            List<Vector3> vertices,
            List<int>[] triangles,
            List<Vector3> normals)
        {
            int start = vertices.Count;

            vertices.Add(bottomA);
            vertices.Add(bottomB);
            vertices.Add(topA);
            vertices.Add(topB);

            Vector3 faceNormal = Vector3.Cross(bottomB - bottomA, topA - bottomA).normalized;

            normals.Add(faceNormal);
            normals.Add(faceNormal);
            normals.Add(faceNormal);
            normals.Add(faceNormal);

            triangles[materialIndex].Add(start + 0);
            triangles[materialIndex].Add(start + 1);
            triangles[materialIndex].Add(start + 2);

            triangles[materialIndex].Add(start + 1);
            triangles[materialIndex].Add(start + 3);
            triangles[materialIndex].Add(start + 2);
        }

        private void AddWaterPolygonFace(
            Vector3 center,
            List<Vector3> corners,
            Vector3 normal,
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector3> normals)
        {
            int centerIndex = vertices.Count;

            vertices.Add(center);
            normals.Add(normal.normalized);

            int startIndex = vertices.Count;

            for (int i = 0; i < corners.Count; i++)
            {
                vertices.Add(corners[i]);
                normals.Add(normal.normalized);
            }

            for (int i = 0; i < corners.Count; i++)
            {
                int current = startIndex + i;
                int next = startIndex + ((i + 1) % corners.Count);

                triangles.Add(centerIndex);
                triangles.Add(current);
                triangles.Add(next);
            }
        }
    }
}