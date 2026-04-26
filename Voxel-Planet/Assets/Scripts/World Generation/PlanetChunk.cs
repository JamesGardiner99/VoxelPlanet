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

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshCollider = GetComponent<MeshCollider>();
            meshRenderer = GetComponent<MeshRenderer>();
        }

        public void Initialise(GoldbergPlanet owner, int index)
        {
            planet = owner;
            chunkIndex = index;

            meshFilter = GetComponent<MeshFilter>();
            meshCollider = GetComponent<MeshCollider>();
            meshRenderer = GetComponent<MeshRenderer>();

            meshRenderer.sharedMaterials = new Material[]
            {
                planet.grassMaterial,
                planet.dirtMaterial,
                planet.stoneMaterial
            };
        }

        public void RebuildMesh()
        {
            if (planet == null)
                return;

            planet.ClearTriangleMappings(chunkIndex);

            Mesh mesh = new Mesh();
            mesh.name = $"Planet Chunk {chunkIndex}";
            mesh.indexFormat = IndexFormat.UInt32;

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();

            List<int>[] trianglesByMaterial =
            {
                new List<int>(),
                new List<int>(),
                new List<int>()
            };

            foreach (int cellIndex in cellIndices)
            {
                if (cellIndex < 0 || cellIndex >= planet.planetCells.Count)
                    continue;

                BuildCell(cellIndex, vertices, trianglesByMaterial, normals);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.subMeshCount = 3;

            mesh.SetTriangles(trianglesByMaterial[0], 0);
            mesh.SetTriangles(trianglesByMaterial[1], 1);
            mesh.SetTriangles(trianglesByMaterial[2], 2);

            mesh.RecalculateBounds();

            meshFilter.sharedMesh = mesh;

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }

        private void BuildCell(
            int cellIndex,
            List<Vector3> vertices,
            List<int>[] trianglesByMaterial,
            List<Vector3> normals)
        {
            GoldbergPlanet.PlanetCell cell = planet.planetCells[cellIndex];

            foreach (var blockPair in cell.blocks)
            {
                int layer = blockPair.Key;
                GoldbergPlanet.BlockType blockType = blockPair.Value;

                if (blockType == GoldbergPlanet.BlockType.Air)
                    continue;

                BuildBlock(
                    cellIndex,
                    cell,
                    layer,
                    blockType,
                    vertices,
                    trianglesByMaterial,
                    normals
                );
            }
        }

        private void BuildBlock(
            int cellIndex,
            GoldbergPlanet.PlanetCell cell,
            int layer,
            GoldbergPlanet.BlockType blockType,
            List<Vector3> vertices,
            List<int>[] trianglesByMaterial,
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

            if (!planet.HasBlock(cellIndex, layer + 1))
            {
                AddPolygonFace(
                    topCenter,
                    topCorners,
                    cell.normal,
                    cellIndex,
                    materialIndex,
                    vertices,
                    trianglesByMaterial,
                    normals
                );
            }

            if (!planet.HasBlock(cellIndex, layer - 1))
            {
                List<Vector3> reversedBottomCorners = new List<Vector3>(bottomCorners);
                reversedBottomCorners.Reverse();

                AddPolygonFace(
                    bottomCenter,
                    reversedBottomCorners,
                    -cell.normal,
                    cellIndex,
                    materialIndex,
                    vertices,
                    trianglesByMaterial,
                    normals
                );
            }

            for (int i = 0; i < cell.corners.Count; i++)
            {
                int neighbourIndex = cell.neighbours[i];

                bool neighbourHasBlock =
                    neighbourIndex >= 0 &&
                    neighbourIndex < planet.planetCells.Count &&
                    planet.HasBlock(neighbourIndex, layer);

                if (neighbourHasBlock)
                    continue;

                Vector3 bottomA = bottomCorners[i];
                Vector3 bottomB = bottomCorners[(i + 1) % bottomCorners.Count];

                Vector3 topA = topCorners[i];
                Vector3 topB = topCorners[(i + 1) % topCorners.Count];

                AddQuadFace(
                    bottomA,
                    bottomB,
                    topA,
                    topB,
                    cellIndex,
                    materialIndex,
                    vertices,
                    trianglesByMaterial,
                    normals
                );
            }
        }

        private void AddPolygonFace(
            Vector3 center,
            List<Vector3> corners,
            Vector3 normal,
            int cellIndex,
            int materialIndex,
            List<Vector3> vertices,
            List<int>[] trianglesByMaterial,
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

                trianglesByMaterial[materialIndex].Add(centerIndex);
                trianglesByMaterial[materialIndex].Add(current);
                trianglesByMaterial[materialIndex].Add(next);

                planet.RegisterTriangleCell(chunkIndex, cellIndex);
            }
        }

        private void AddQuadFace(
            Vector3 bottomA,
            Vector3 bottomB,
            Vector3 topA,
            Vector3 topB,
            int cellIndex,
            int materialIndex,
            List<Vector3> vertices,
            List<int>[] trianglesByMaterial,
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

            trianglesByMaterial[materialIndex].Add(start + 0);
            trianglesByMaterial[materialIndex].Add(start + 1);
            trianglesByMaterial[materialIndex].Add(start + 2);
            planet.RegisterTriangleCell(chunkIndex, cellIndex);

            trianglesByMaterial[materialIndex].Add(start + 1);
            trianglesByMaterial[materialIndex].Add(start + 3);
            trianglesByMaterial[materialIndex].Add(start + 2);
            planet.RegisterTriangleCell(chunkIndex, cellIndex);
        }

        public Vector3 GetChunkCenterWorld()
        {
            if (cellIndices.Count == 0 || planet == null)
                return transform.position;

            Vector3 center = Vector3.zero;
            int validCount = 0;

            foreach (int cellIndex in cellIndices)
            {
                if (cellIndex < 0 || cellIndex >= planet.planetCells.Count)
                    continue;

                center += planet.planetCells[cellIndex].center;
                validCount++;
            }

            if (validCount == 0)
                return transform.position;

            center /= validCount;
            return planet.transform.TransformPoint(center);
        }

        public void SetVisible(bool isVisible)
        {
            if (meshRenderer != null)
                meshRenderer.enabled = isVisible;

            if (meshCollider != null)
                meshCollider.enabled = isVisible;
        }

        public float GetDistanceSqrToPoint(Vector3 point)
        {
            if (meshRenderer == null)
                return float.MaxValue;

            Vector3 closestPoint = meshRenderer.bounds.ClosestPoint(point);
            return (closestPoint - point).sqrMagnitude;
        }
    }
}