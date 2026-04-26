using UnityEngine;

namespace VoxelPlanet
{
    [RequireComponent(typeof(LineRenderer))]
    public class CellHighlighter : MonoBehaviour
    {
        public GoldbergPlanet planet;

        [Header("Style")]
        public float lineWidth = 0.05f;
        public float surfaceOffset = 0.2f;

        private LineRenderer lineRenderer;

        private void Awake()
        {
            lineRenderer = GetComponent<LineRenderer>();

            lineRenderer.loop = true;
            lineRenderer.useWorldSpace = true;
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;

            // Better: no runtime material leak
            lineRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));

            lineRenderer.startColor = Color.black;
            lineRenderer.endColor = Color.black;
        }

        public void HighlightCell(int cellIndex)
        {
            if (planet == null || cellIndex < 0 || cellIndex >= planet.planetCells.Count)
            {
                lineRenderer.positionCount = 0;
                return;
            }

            var cell = planet.planetCells[cellIndex];

            if (cell.corners == null || cell.corners.Count == 0)
            {
                lineRenderer.positionCount = 0;
                return;
            }

            float heightOffset = cell.heightLevel * planet.cellHeightStep;

            lineRenderer.positionCount = cell.corners.Count;

            for (int i = 0; i < cell.corners.Count; i++)
            {
                Vector3 normal = cell.corners[i].normalized;
                Vector3 point = cell.corners[i] + normal * (heightOffset + surfaceOffset);

                lineRenderer.SetPosition(i, point);
            }
        }
    }
}