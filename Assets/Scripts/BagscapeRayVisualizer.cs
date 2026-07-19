using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public sealed class BagscapeRayVisualizer : MonoBehaviour
{
    [Header("레이")]
    [SerializeField] private Transform rayOrigin;
    [SerializeField, Min(0.1f)] private float maxDistance = 4f;
    [SerializeField] private LayerMask hitLayers = ~0;

    [Header("표시")]
    [SerializeField, Min(0.001f)] private float lineWidth = 0.004f;

    private LineRenderer lineRenderer;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();

        if (rayOrigin == null)
        {
            rayOrigin = transform;
        }

        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
    }

    private void LateUpdate()
    {
        if (rayOrigin == null)
        {
            lineRenderer.enabled = false;
            return;
        }

        lineRenderer.enabled = true;

        Vector3 start = rayOrigin.position;
        Vector3 end = start + rayOrigin.forward * maxDistance;

        if (Physics.Raycast(
                start,
                rayOrigin.forward,
                out RaycastHit hit,
                maxDistance,
                hitLayers,
                QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
        }

        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
    }
}