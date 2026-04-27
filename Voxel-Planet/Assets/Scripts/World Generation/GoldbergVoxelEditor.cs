using UnityEngine;

namespace VoxelPlanet
{
    public class GoldbergVoxelEditor : MonoBehaviour
    {
        [Header("References")]
        public Camera playerCamera;
        public GoldbergPlanet planet;
        public CellHighlighter cellHighlighter;

        [Header("Ray Debug")]
        public LineRenderer rayRenderer;
        public bool showRay = true;
        public float rayWidth = 0.02f;

        [Header("Editing")]
        public float editRange = 20f;
        public LayerMask planetMask;

        private int currentHighlightedCell = -1;

        private void Awake()
        {
            if (rayRenderer != null)
            {
                rayRenderer.positionCount = 2;
                rayRenderer.startWidth = rayWidth;
                rayRenderer.endWidth = rayWidth;
                rayRenderer.useWorldSpace = true;
                rayRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            }
        }

        private void Update()
        {
            UpdateHighlightedCell();

            if (Input.GetMouseButtonDown(1))
                EditCell(true);
            else if (Input.GetMouseButtonDown(0))
                EditCell(false);
        }

        private void UpdateHighlightedCell()
        {
            if (playerCamera == null || planet == null)
                return;

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

            bool hasHit = Physics.Raycast(ray, out RaycastHit hit, editRange, planetMask);

            UpdateRayVisual(ray, hasHit, hit);

            if (!hasHit)
            {
                if (currentHighlightedCell != -1)
                {
                    currentHighlightedCell = -1;

                    if (cellHighlighter != null)
                        cellHighlighter.HighlightCell(-1);
                }

                return;
            }

            PlanetChunk chunk = hit.collider.GetComponent<PlanetChunk>();
            int cellIndex = chunk != null
                ? chunk.GetCellIndexFromTriangle(hit.triangleIndex)
                : -1;

            if (cellIndex == currentHighlightedCell)
                return;

            currentHighlightedCell = cellIndex;

            if (cellHighlighter != null)
                cellHighlighter.HighlightCell(cellIndex);
        }

        private void EditCell(bool isRaising)
        {
            if (playerCamera == null || planet == null)
                return;

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

            if (!Physics.Raycast(ray, out RaycastHit hit, editRange, planetMask))
                return;

            PlanetChunk chunk = hit.collider.GetComponent<PlanetChunk>();
            int cellIndex = chunk != null
                ? chunk.GetCellIndexFromTriangle(hit.triangleIndex)
                : -1;

            if (cellIndex == -1)
                return;

            if (isRaising)
                planet.RaiseCell(cellIndex);
            else
                planet.LowerCell(cellIndex);

            currentHighlightedCell = cellIndex;

            if (cellHighlighter != null)
                cellHighlighter.HighlightCell(cellIndex);
        }

        private void UpdateRayVisual(Ray ray, bool hasHit, RaycastHit hit)
        {
            Debug.DrawRay(
                ray.origin,
                ray.direction * (hasHit ? hit.distance : editRange),
                hasHit ? Color.green : Color.red
            );

            if (rayRenderer == null)
                return;

            rayRenderer.enabled = showRay;

            if (!showRay)
                return;

            Vector3 endPoint = hasHit
                ? hit.point
                : ray.origin + ray.direction * editRange;

            Vector3 offset = playerCamera.transform.right * 0.1f;

            rayRenderer.SetPosition(0, ray.origin + offset);
            rayRenderer.SetPosition(1, endPoint + offset);

            Color colour = hasHit ? Color.green : Color.red;
            rayRenderer.startColor = colour;
            rayRenderer.endColor = colour;
        }
    }
}