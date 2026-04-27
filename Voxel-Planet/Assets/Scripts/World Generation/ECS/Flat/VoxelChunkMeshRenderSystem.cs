using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelPlanet
{
    public partial class VoxelChunkMeshRenderSystem : SystemBase
    {
        private Material chunkMaterial;

        protected override void OnCreate()
        {
            RequireForUpdate<VoxelPlanetSettings>();

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
                shader = Shader.Find("Standard");

            chunkMaterial = new Material(shader);
            chunkMaterial.name = "ECS Voxel Chunk Material";
            chunkMaterial.color = Color.green;
        }

        protected override void OnUpdate()
        {
            VoxelPlanetSettings settings =
                SystemAPI.GetSingleton<VoxelPlanetSettings>();

            EntityManager entityManager = EntityManager;

            Entities
                .WithoutBurst()
                .WithStructuralChanges()
                .ForEach((Entity entity, ref FlatVoxelChunk chunk, in DynamicBuffer<VoxelBlock> blocks) =>
                {
                    if (chunk.NeedsMeshBuild == 0)
                        return;

                    Mesh mesh = BuildChunkMesh(chunk, blocks, settings);

                    Debug.Log($"Chunk {chunk.Coord} verts: {mesh.vertexCount}");

                    if (mesh.vertexCount == 0)
                    {
                        chunk.NeedsMeshBuild = 0;
                        Debug.LogWarning($"Empty mesh for chunk {chunk.Coord.x},{chunk.Coord.y}");
                        return;
                    }

                    var renderMeshArray = new RenderMeshArray(
                        new[] { chunkMaterial },
                        new[] { mesh }
                    );

                    var desc = new RenderMeshDescription(
                        shadowCastingMode: ShadowCastingMode.On,
                        receiveShadows: true
                    );

                    if (!entityManager.HasComponent<LocalTransform>(entity))
                    {
                        float3 position = new float3(
                            chunk.Coord.x * settings.CellsPerChunk,
                            0,
                            chunk.Coord.y * settings.CellsPerChunk
                        );

                        entityManager.AddComponentData(
                            entity,
                            LocalTransform.FromPosition(position)
                        );
                    }

                    RenderMeshUtility.AddComponents(
                        entity,
                        entityManager,
                        desc,
                        renderMeshArray,
                        MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
                    );

                    entityManager.SetComponentData(
                        entity,
                        MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
                    );

                    chunk.NeedsMeshBuild = 0;

                }).Run();
        }

        private Mesh BuildChunkMesh(
            FlatVoxelChunk chunk,
            DynamicBuffer<VoxelBlock> blocks,
            VoxelPlanetSettings settings)
        {
            int size = settings.CellsPerChunk;
            int layers = settings.LayersPerChunk;

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector3> normals = new List<Vector3>();

            for (int y = 0; y < layers; y++)
            {
                for (int z = 0; z < size; z++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int index = GetIndex(x, y, z, size);

                        BlockType block = (BlockType)blocks[index].Value;

                        if (!IsSolid(block))
                            continue;

                        Vector3 voxelPos = new Vector3(
                            x, y - layers / 2, z
                        );

                        AddVisibleFaces(
                            x,
                            y,
                            z,
                            voxelPos,
                            size,
                            layers,
                            blocks,
                            vertices,
                            triangles,
                            normals
                        );
                    }
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = $"Chunk_{chunk.Coord.x}_{chunk.Coord.y}";

            if (vertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);
            mesh.RecalculateBounds();

            return mesh;
        }

        private void AddVisibleFaces(
            int x,
            int y,
            int z,
            Vector3 voxelPos,
            int size,
            int layers,
            DynamicBuffer<VoxelBlock> blocks,
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector3> normals)
        {
            if (!IsSolid(GetBlock(x + 1, y, z, size, layers, blocks)))
                AddFace(voxelPos, Vector3.right, vertices, triangles, normals);

            if (!IsSolid(GetBlock(x - 1, y, z, size, layers, blocks)))
                AddFace(voxelPos, Vector3.left, vertices, triangles, normals);

            if (!IsSolid(GetBlock(x, y + 1, z, size, layers, blocks)))
                AddFace(voxelPos, Vector3.up, vertices, triangles, normals);

            if (!IsSolid(GetBlock(x, y - 1, z, size, layers, blocks)))
                AddFace(voxelPos, Vector3.down, vertices, triangles, normals);

            if (!IsSolid(GetBlock(x, y, z + 1, size, layers, blocks)))
                AddFace(voxelPos, Vector3.forward, vertices, triangles, normals);

            if (!IsSolid(GetBlock(x, y, z - 1, size, layers, blocks)))
                AddFace(voxelPos, Vector3.back, vertices, triangles, normals);
        }

        private BlockType GetBlock(
            int x,
            int y,
            int z,
            int size,
            int layers,
            DynamicBuffer<VoxelBlock> blocks)
        {
            if (x < 0 || x >= size ||
                y < 0 || y >= layers ||
                z < 0 || z >= size)
            {
                return BlockType.Air;
            }

            int index = GetIndex(x, y, z, size);
            return (BlockType)blocks[index].Value;
        }

        private int GetIndex(int x, int y, int z, int size)
        {
            return x + (z * size) + (y * size * size);
        }

        private bool IsSolid(BlockType block)
        {
            return block != BlockType.Air &&
                   block != BlockType.Water;
        }

        private void AddFace(
            Vector3 pos,
            Vector3 normal,
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector3> normals)
        {
            int startIndex = vertices.Count;

            Vector3 v0;
            Vector3 v1;
            Vector3 v2;
            Vector3 v3;

            if (normal == Vector3.up)
            {
                v0 = pos + new Vector3(0, 1, 0);
                v1 = pos + new Vector3(0, 1, 1);
                v2 = pos + new Vector3(1, 1, 1);
                v3 = pos + new Vector3(1, 1, 0);
            }
            else if (normal == Vector3.down)
            {
                v0 = pos + new Vector3(0, 0, 0);
                v1 = pos + new Vector3(1, 0, 0);
                v2 = pos + new Vector3(1, 0, 1);
                v3 = pos + new Vector3(0, 0, 1);
            }
            else if (normal == Vector3.forward)
            {
                v0 = pos + new Vector3(0, 0, 1);
                v1 = pos + new Vector3(1, 0, 1);
                v2 = pos + new Vector3(1, 1, 1);
                v3 = pos + new Vector3(0, 1, 1);
            }
            else if (normal == Vector3.back)
            {
                v0 = pos + new Vector3(1, 0, 0);
                v1 = pos + new Vector3(0, 0, 0);
                v2 = pos + new Vector3(0, 1, 0);
                v3 = pos + new Vector3(1, 1, 0);
            }
            else if (normal == Vector3.right)
            {
                v0 = pos + new Vector3(1, 0, 1);
                v1 = pos + new Vector3(1, 0, 0);
                v2 = pos + new Vector3(1, 1, 0);
                v3 = pos + new Vector3(1, 1, 1);
            }
            else
            {
                v0 = pos + new Vector3(0, 0, 0);
                v1 = pos + new Vector3(0, 0, 1);
                v2 = pos + new Vector3(0, 1, 1);
                v3 = pos + new Vector3(0, 1, 0);
            }

            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);

            triangles.Add(startIndex + 0);
            triangles.Add(startIndex + 1);
            triangles.Add(startIndex + 2);

            triangles.Add(startIndex + 0);
            triangles.Add(startIndex + 2);
            triangles.Add(startIndex + 3);

            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
        }
    }
}