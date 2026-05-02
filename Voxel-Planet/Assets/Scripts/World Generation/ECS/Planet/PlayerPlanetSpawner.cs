using System.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace VoxelPlanet
{
    public class PlanetPlayerSpawner : MonoBehaviour
    {
        [Header("References")]
        public GameObject playerPrefab;
        public GoldbergPlanetAuthoring planet;
        public CanvasGroup loadingCanvasGroup;

        [Header("Spawn Settings")]
        public float spawnHeightAboveSurface = 30f;
        public float playerGroundOffset = 1.2f;
        public float maxGroundCheckDistance = 200f;
        public LayerMask groundMask = ~0;

        [Header("Loading")]
        public float minimumLoadingTime = 1f;
        public float maxWaitForTerrainSeconds = 5f;
        public float fadeOutDuration = 1f;

        private IEnumerator Start()
        {
            if (playerPrefab == null || planet == null)
            {
                Debug.LogError("[SPAWN] Missing playerPrefab or planet reference.");
                yield break;
            }

            ShowLoading();

            Vector3 spawnDirection = UnityEngine.Random.onUnitSphere.normalized;

            Vector3 preloadPosition =
                spawnDirection * (planet.Radius + spawnHeightAboveSurface);

            PlanetPlayerPositionBridge.SetPreloadPosition(preloadPosition);

            Debug.Log($"[SPAWN] Preload position: {preloadPosition}");

            float elapsed = 0f;
            float minimumElapsed = 0f;

            bool foundGround = false;
            RaycastHit groundHit = default;

            while (elapsed < maxWaitForTerrainSeconds)
            {
                Vector3 rayOrigin = preloadPosition;
                Vector3 rayDirection = -spawnDirection;

                if (Physics.Raycast(
                    rayOrigin,
                    rayDirection,
                    out groundHit,
                    maxGroundCheckDistance,
                    groundMask,
                    QueryTriggerInteraction.Ignore))
                {
                    foundGround = true;

                    if (minimumElapsed >= minimumLoadingTime)
                        break;
                }

                elapsed += Time.deltaTime;
                minimumElapsed += Time.deltaTime;

                yield return null;
            }

            Vector3 finalPosition;
            Vector3 groundUp;

            if (foundGround)
            {
                groundUp = spawnDirection;
                finalPosition = groundHit.point + groundUp * playerGroundOffset;

                Debug.Log($"[SPAWN] Ground found at {groundHit.point}, spawning at {finalPosition}");
            }
            else
            {
                groundUp = spawnDirection;
                finalPosition = spawnDirection * (planet.Radius + spawnHeightAboveSurface);

                Debug.LogWarning($"[SPAWN] Terrain not found. Fallback spawn at {finalPosition}");
            }

            GameObject playerInstance = Instantiate(playerPrefab);

            PlanetPlayerController controller =
                playerInstance.GetComponent<PlanetPlayerController>();

            Rigidbody rb =
                playerInstance.GetComponent<Rigidbody>();

            if (controller != null)
                controller.enabled = false;

            if (rb != null)
            {
                rb.isKinematic = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            playerInstance.transform.position = finalPosition;
            AlignObjectToPlanet(playerInstance.transform, groundUp);

            yield return null;

            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = false;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.WakeUp();
            }

            if (controller != null)
                controller.enabled = true;

            PlanetPlayerPositionBridge bridge = playerInstance.GetComponent<PlanetPlayerPositionBridge>();

            if (bridge != null)
            {
                bridge.enabled = true;
            }

            Debug.Log(
                $"[SPAWN] Player enabled. " +
                $"Controller: {(controller != null && controller.enabled)} | " +
                $"RB kinematic: {(rb != null && rb.isKinematic)} | " +
                $"Position: {playerInstance.transform.position}"
            );

            yield return FadeLoadingOut();
        }

        private void ShowLoading()
        {
            if (loadingCanvasGroup == null)
                return;

            loadingCanvasGroup.alpha = 1f;
            loadingCanvasGroup.blocksRaycasts = true;
            loadingCanvasGroup.interactable = true;
        }

        private IEnumerator FadeLoadingOut()
        {
            if (loadingCanvasGroup == null)
                yield break;

            float t = 0f;

            while (t < fadeOutDuration)
            {
                t += Time.deltaTime;
                loadingCanvasGroup.alpha = Mathf.Lerp(1f, 0f, t / fadeOutDuration);
                yield return null;
            }

            loadingCanvasGroup.alpha = 0f;
            loadingCanvasGroup.blocksRaycasts = false;
            loadingCanvasGroup.interactable = false;
        }

        private void AlignObjectToPlanet(Transform target, Vector3 gravityUp)
        {
            Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, gravityUp).normalized;

            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(Vector3.right, gravityUp).normalized;

            target.rotation = Quaternion.LookRotation(forward, gravityUp);
        }
    }
}