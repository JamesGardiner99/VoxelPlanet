using System.Collections.Generic;
using UnityEngine;

namespace VoxelPlanet
{
    public class GoldbergChunkColliderBridge : MonoBehaviour
    {
        public static GoldbergChunkColliderBridge Instance;

        private readonly Dictionary<int, MeshCollider> chunkColliders = new();

        private void Awake()
        {
            Instance = this;
            Debug.Log("GoldbergChunkColliderBridge active");
        }

        public static void SetChunkCollider(int chunkIndex, Mesh mesh)
        {
            if (Instance == null)
            {
                Debug.LogWarning("GoldbergChunkColliderBridge Instance is null.");
                return;
            }

            Instance.SetChunkColliderInternal(chunkIndex, mesh);
        }

        private void SetChunkColliderInternal(int chunkIndex, Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount == 0)
            {
                Debug.LogWarning($"Chunk {chunkIndex} collider mesh is null or empty.");
                return;
            }

            if (!chunkColliders.TryGetValue(chunkIndex, out MeshCollider meshCollider))
            {
                GameObject chunkObject = new GameObject($"Goldberg Chunk Collider {chunkIndex}");
                chunkObject.transform.SetParent(transform, false);

                chunkObject.transform.localPosition = Vector3.zero;
                chunkObject.transform.localRotation = Quaternion.identity;
                chunkObject.transform.localScale = Vector3.one;

                // Make sure this matches your player's groundMask
                chunkObject.layer = LayerMask.NameToLayer("Default");

                meshCollider = chunkObject.AddComponent<MeshCollider>();
                meshCollider.convex = false;

                chunkColliders.Add(chunkIndex, meshCollider);
            }

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;

            Debug.Log($"Collider set for chunk {chunkIndex}. Vertices: {mesh.vertexCount}");
        }
    }
}