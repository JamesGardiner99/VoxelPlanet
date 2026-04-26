using System.Collections;
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

        public void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshCollider = GetComponent<MeshCollider>();
            meshRenderer = GetComponent<MeshRenderer>();
        } 

        public void Initialise(GoldbergPlanet owner, int index, Material material)
        {
            planet = owner;
            chunkIndex = index;
            
            meshFilter = GetComponent<MeshFilter>();
            meshCollider = GetComponent<MeshCollider>();
            meshRenderer = GetComponent<MeshRenderer>();

            if(material != null)
            {
                meshRenderer.sharedMaterial = material;
            }
        }

        public void RebuildMesh()
        {
            if (planet == null)
            {
                Debug.LogError("Planet reference is missing for chunk " + chunkIndex);
                return;
            }

            planet.ClearTriangleMappings(chunkIndex);

            Mesh mesh = new Mesh();
            mesh.name = $"Planet Chunk {chunkIndex}";
            mesh.indexFormat = IndexFormat.UInt32;

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector3> normals = new List<Vector3>();

            foreach (int cellIndex in cellIndices)
            {
                if (cellIndex < 0 || cellIndex >= planet.planetCells.Count)
                    continue;

                BuildCell(cellIndex, vertices, triangles, normals);
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);
            mesh.RecalculateBounds();

            meshFilter.sharedMesh = mesh;

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }

        private void BuildCell(int cellIndex, List<Vector3> vertices, List<int> triangles, List<Vector3> normals)
        {
            GoldbergPlanet.PlanetCell cell = planet.planetCells[cellIndex];

            float heightOffset = cell.heightLevel * planet.cellHeightStep;

            Vector3 topCenter = cell.center + cell.normal * heightOffset;
            List<Vector3> topCorners = new List<Vector3>();

            for(int i = 0; i < cell.corners.Count; i++)
            {
                Vector3 cornerNormal = cell.corners[i].normalized;
                Vector3 topCorner = cell.corners[i] + cornerNormal * heightOffset;
                topCorners.Add(topCorner);
            }

            //Top face
            int topCenterIndex = vertices.Count;
            vertices.Add(topCenter);
            normals.Add(topCenter.normalized);

            int topStartIndex = vertices.Count;

            for(int i = 0; i < topCorners.Count; i++)
            {
                vertices.Add(topCorners[i]);
                normals.Add(topCorners[i].normalized);
            }

            for(int i = 0; i < topCorners.Count; i++)
            {
                int current = topStartIndex + i;
                int next = topStartIndex + ((i + 1) % topCorners.Count);

                triangles.Add(topCenterIndex);
                triangles.Add(current);
                triangles.Add(next);

                planet.RegisterTriangleCell(chunkIndex, cell.index);
            }

            //Smart side walls
            for(int i = 0; i < cell.corners.Count; i++)
            {
                int neighbourIndex = cell.neighbours[i];
                int neighbourHeightLevel = planet.minHeightLevel;

                if(neighbourIndex >= 0 && neighbourIndex < planet.planetCells.Count)
                {
                    neighbourHeightLevel = planet.planetCells[neighbourIndex].heightLevel;
                }

                if(cell.heightLevel <= neighbourHeightLevel)
                {
                    continue;
                }

                float lowerOffset = neighbourHeightLevel * planet.cellHeightStep;
                float upperOffset = cell.heightLevel * planet.cellHeightStep;

                Vector3 cornerA = cell.corners[i];
                Vector3 cornerB = cell.corners[(i + 1) % cell.corners.Count];

                Vector3 normalA = cornerA.normalized;
                Vector3 normalB = cornerB.normalized;

                Vector3 bottomA = cornerA + normalA * lowerOffset;
                Vector3 bottomB = cornerB + normalB * lowerOffset;

                Vector3 topA = cornerA + normalA * upperOffset;
                Vector3 topB = cornerB + normalB * upperOffset;

                int wallStart = vertices.Count;

                vertices.Add(bottomA);
                vertices.Add(bottomB);
                vertices.Add(topA);
                vertices.Add(topB);

                normals.Add(bottomA.normalized);
                normals.Add(bottomB.normalized);
                normals.Add(topA.normalized);
                normals.Add(topB.normalized);

                triangles.Add(wallStart + 0);
                triangles.Add(wallStart + 1);
                triangles.Add(wallStart + 2);
                planet.RegisterTriangleCell(chunkIndex, cell.index);

                triangles.Add(wallStart + 1);
                triangles.Add(wallStart + 3);
                triangles.Add(wallStart + 2);
                planet.RegisterTriangleCell(chunkIndex, cell.index);

                planet.RegisterTriangleCell(chunkIndex, cell.index);
            }
        }

        public Vector3 GetChunkCenterWorld()
        {
            if(cellIndices.Count == 0 || planet == null)
            {
                return transform.position;
            }

            Vector3 center = Vector3.zero;

            foreach(int cellIndex in cellIndices)
            {
                if(cellIndex < 0 || cellIndex >= planet.planetCells.Count)
                {
                    continue;
                }

                center += planet.planetCells[cellIndex].center;
            }

            center /= cellIndices.Count;
            return planet.transform.TransformPoint(center);
        }

        public void SetVisible(bool isVisible)
        {
            if(meshRenderer != null)
            {
                meshRenderer.enabled = isVisible;
            }

            if(meshCollider != null)
            {
                meshCollider.enabled = isVisible;
            }
        }

        public float GetDistanceSqrToPoint(Vector3 point)
        {
            if(meshRenderer == null || meshRenderer.sharedMaterial == null)
            {
                return float.MaxValue;
            }

            Vector3 closestPoint = meshRenderer.bounds.ClosestPoint(point);
            return (closestPoint - point).sqrMagnitude;
        }
    }
}