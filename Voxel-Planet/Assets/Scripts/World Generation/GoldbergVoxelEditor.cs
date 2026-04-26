using UnityEngine;

namespace VoxelPlanet
{
    public class GoldbergVoxelEditor : MonoBehaviour
    {
        [Header("References")]
        public Camera playerCamera;
        public GoldbergPlanet planet;
        public CellHighlighter cellHighlighter;

        [Header("Editing")]
        public float editRange = 20f;
        public LayerMask planetMask;

        private int currentHighlightedCell = -1;

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

            if (!Physics.Raycast(ray, out RaycastHit hit, editRange, planetMask))
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
            int cellIndex = planet.GetClosestCellFromHit(chunk, hit.point);

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
            int cellIndex = planet.GetClosestCellFromHit(chunk, hit.point);

            if (cellIndex == -1)
                return;

            if (isRaising)
                planet.RaiseCell(cellIndex);
            else
                planet.LowerCell(cellIndex);

            currentHighlightedCell = cellIndex;
            
            if(cellHighlighter != null)
                cellHighlighter.HighlightCell(cellIndex);

            UpdateHighlightedCell();
        }
    }
}