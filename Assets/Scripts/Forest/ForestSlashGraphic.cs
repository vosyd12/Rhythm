using UnityEngine;
using UnityEngine.UI;

// 참격의 모양을 만든다.
// 색상, 표시 시점과 사라지는 동작은 기존 연출기에서 제어한다.
public class ForestSlashGraphic : MaskableGraphic
{
    [SerializeField, Range(20.0f, 240.0f)]
    private float arcDegrees = 110.0f;

    [SerializeField, Range(0.01f, 0.5f)]
    private float thicknessRatio = 0.20f;

    [SerializeField, Range(8, 64)]
    private int segments = 32;

    [Header("외곽 발광 공간")]
    [SerializeField, Range(1.0f, 3.0f)]
    private float glowWidthMultiplier = 2.5f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = rectTransform.rect;

        float availableRadius =
            Mathf.Min(rect.width, rect.height) * 0.5f;

        if (availableRadius <= 0.0f)
        {
            return;
        }

        int count = Mathf.Clamp(segments, 8, 64);

        float arc = Mathf.Clamp(
            arcDegrees, 20.0f, 240.0f);

        float thickness = Mathf.Clamp(
            thicknessRatio, 0.01f, 0.5f);

        float glowWidth = Mathf.Clamp(
            glowWidthMultiplier, 1.0f, 3.0f);

        // 발광 영역까지 RectTransform 안에 들어오도록 한다.
        float radius = availableRadius /
            (1.0f + thickness * (glowWidth - 1.0f) * 0.5f);

        Vector2 center = rect.center;
        Color32 vertexColor = color;

        for (int i = 0; i <= count; i++)
        {
            float t = (float)i / count;

            float angle =
                Mathf.Lerp(-arc * 0.5f, arc * 0.5f, t) *
                Mathf.Deg2Rad;

            Vector2 direction = new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle));

            // 양 끝은 가늘고 가운데는 두껍게 만든다.
            float taper = Mathf.Max(
                0.0f, Mathf.Sin(Mathf.PI * t));

            float bladeWidth = radius * thickness * taper;

            float middleRadius =
                radius - bladeWidth * 0.5f;

            float halfWidth =
                bladeWidth * glowWidth * 0.5f;

            Vector2 outer = center +
                direction * (middleRadius + halfWidth);

            Vector2 inner = center +
                direction * (middleRadius - halfWidth);

            // UV의 X는 참격의 길이 방향,
            // Y는 참격의 두께 방향을 나타낸다.
            vh.AddVert(
                new Vector3(outer.x, outer.y, 0.0f),
                vertexColor,
                new Vector2(t, 1.0f));

            vh.AddVert(
                new Vector3(inner.x, inner.y, 0.0f),
                vertexColor,
                new Vector2(t, 0.0f));

            if (i == 0)
            {
                continue;
            }

            int previous = (i - 1) * 2;

            vh.AddTriangle(
                previous, previous + 2, previous + 1);

            vh.AddTriangle(
                previous + 1, previous + 2, previous + 3);
        }
    }
}