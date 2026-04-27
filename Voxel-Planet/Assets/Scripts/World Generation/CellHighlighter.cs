using UnityEngine;

namespace VoxelPlanet
{
    [RequireComponent(typeof(LineRenderer))]
    public class CellHighlighter : MonoBehaviour
    {
        public GoldbergPlanet planet;

        [Header("Style")]
        public float lineWidth = 0.05f;
        public float surfaceOffset = 0.05f;

        private LineRenderer lineRenderer;

        private void Awake()
        {
            lineRenderer = GetComponent<LineRenderer>();

            lineRenderer.loop = true;
            lineRenderer.useWorldSpace = true;
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;
            lineRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = Color.black;
            lineRenderer.endColor = Color.black;
        }

        public void HighlightCell(int cellIndex)
        {
            if (planet == null || planet.voxelWorld == null ||
                cellIndex < 0 || cellIndex >= planet.planetCells.Count)
            {
                lineRenderer.positionCount = 0;
                return;
            }

            var cell = planet.planetCells[cellIndex];

            int highestLayer = planet.voxelWorld.GetHighestSolidLayer(cellIndex);

            if (highestLayer == int.MinValue)
            {
                lineRenderer.positionCount = 0;
                return;
            }

            float topOffset = (highestLayer + 1) * planet.blockHeight;

            lineRenderer.positionCount = cell.corners.Count;

            for (int i = 0; i < cell.corners.Count; i++)
            {
                Vector3 normal = cell.corners[i].normalized;
                Vector3 point = cell.corners[i] + normal * (topOffset + surfaceOffset);

                lineRenderer.SetPosition(i, point);
            }
        }
    }
}