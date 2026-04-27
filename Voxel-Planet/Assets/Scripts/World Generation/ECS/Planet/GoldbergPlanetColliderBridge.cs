using UnityEngine;

namespace VoxelPlanet
{
    public class GoldbergPlanetColliderBridge : MonoBehaviour
    {
        private static GameObject colliderObject;
        private static MeshCollider meshCollider;

        public static void SetColliderMesh(Mesh mesh)
        {
            if (colliderObject == null)
            {
                colliderObject = new GameObject("Goldberg Planet Collider");

                meshCollider = colliderObject.AddComponent<MeshCollider>();
                meshCollider.convex = false;

                colliderObject.layer = LayerMask.NameToLayer("Default");
            }

            meshCollider.sharedMesh = null;
            mesh.RecalculateBounds();
            meshCollider.sharedMesh = mesh;
        }

        public static void Clear()
        {
            if (colliderObject != null)
            {
                Destroy(colliderObject);
                colliderObject = null;
                meshCollider = null;
            }
        }
    }
}