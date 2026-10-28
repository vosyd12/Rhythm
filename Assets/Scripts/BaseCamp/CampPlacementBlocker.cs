using System.Collections.Generic;
using UnityEngine;

public enum CampPlacementBlockerShape { Box, Circle }

// 캠프, 자원 오브젝트, 웜홀 주변 등 건설을 금지할 범위다.
// 적의 이동 장애물이나 전투 충돌을 생성하는 컴포넌트는 아니다.
[DisallowMultipleComponent]
public class CampPlacementBlocker : MonoBehaviour
{
    [SerializeField] private CampPlacementBlockerShape shape;
    [SerializeField] private Vector2 localCenter;
    [SerializeField] private Vector2 size = Vector2.one;
    [SerializeField, Min(0.01f)] private float radius = 3f;

    private static readonly HashSet<CampPlacementBlocker> active =
        new HashSet<CampPlacementBlocker>();
    public static IEnumerable<CampPlacementBlocker> All => active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => active.Clear();

    private void OnEnable() => active.Add(this);
    private void OnDisable() => active.Remove(this);

    private Vector2 Center => transform.TransformPoint(localCenter);
    private float WorldRadius => radius * Mathf.Max(
        Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));

    private Rect WorldBox
    {
        get
        {
            Vector2 half = size * 0.5f;
            Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                {
                    Vector2 point = transform.TransformPoint(
                        localCenter + new Vector2(half.x * x, half.y * y));
                    minimum = Vector2.Min(minimum, point);
                    maximum = Vector2.Max(maximum, point);
                }
            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }
    }

    public bool Overlaps(Rect footprint)
    {
        if (shape == CampPlacementBlockerShape.Box)
            return WorldBox.Overlaps(footprint);

        Vector2 center = Center;
        Vector2 nearest = new Vector2(
            Mathf.Clamp(center.x, footprint.xMin, footprint.xMax),
            Mathf.Clamp(center.y, footprint.yMin, footprint.yMax));
        return (nearest - center).sqrMagnitude <= WorldRadius * WorldRadius;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.8f);
        if (shape == CampPlacementBlockerShape.Circle)
            Gizmos.DrawWireSphere(Center, WorldRadius);
        else
        {
            Rect box = WorldBox;
            Gizmos.DrawWireCube(box.center, new Vector3(box.width, box.height, 0f));
        }
    }
}
