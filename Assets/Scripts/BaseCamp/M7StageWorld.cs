using UnityEngine;

// 기존 씬·에셋을 보존하고 지역마다 다른 통로를 런타임에 구성한다.
public static class M7StageWorld
{
    public static void Build(int stage, NightDefenseTester battle)
    {
        if (stage <= 1 || CampWallPlacementController.Instance == null) return;
        var definition = CampWallPlacementController.Instance.DefinitionFor(CampBuildingKind.Wall);
        var source = definition.SitePrefab.GetComponentInChildren<SpriteRenderer>(true);
        if (source == null || source.sprite == null) return;
        var root = new GameObject("M7_" + RunCampaign.StageName(stage));
        Vector2[] centers = stage == 2
            ? new[] { new Vector2(-18, 13), new Vector2(18, -13) }
            : stage == 3
                ? new[] { new Vector2(-19, 17), new Vector2(19, 17), new Vector2(-19, -17), new Vector2(19, -17) }
                : new[] { new Vector2(-25, 17), new Vector2(25, -17), new Vector2(-17, -25), new Vector2(17, 25) };
        for (int i = 0; i < centers.Length; i++)
        {
            var obstacle = new GameObject("지역 장애물_" + i, typeof(SpriteRenderer), typeof(BoxCollider2D), typeof(CampPlacementBlocker));
            obstacle.transform.SetParent(root.transform, false);
            obstacle.transform.position = centers[i];
            Vector2 size = stage == 2 ? new Vector2(3, 14) : stage == 3 ? new Vector2(14, 3) : (i < 2 ? new Vector2(3, 16) : new Vector2(16, 3));
            var visual = obstacle.GetComponent<SpriteRenderer>(); visual.sprite = source.sprite;
            visual.drawMode = SpriteDrawMode.Sliced; visual.size = Vector2.one;
            visual.color = stage == 2 ? new Color(0.45f, 0.5f, 0.48f) : stage == 3 ? new Color(0.48f, 0.43f, 0.37f) : new Color(0.35f, 0.39f, 0.47f);
            visual.sortingLayerID = source.sortingLayerID; visual.sortingOrder = source.sortingOrder;
            obstacle.transform.localScale = new Vector3(size.x, size.y, 1);
            obstacle.GetComponent<BoxCollider2D>().size = Vector2.one;
        }
        Physics2D.SyncTransforms();
    }
}
