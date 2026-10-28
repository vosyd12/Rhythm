using UnityEngine;

public enum CampBuildingKind
{
    Wall,
    Tower
}

// 건물 종류의 공통 설정이다. 개별 배치 위치와 진행률은 여기에 저장하지 않는다.
[CreateAssetMenu(menuName = "Camp/건설 정의", fileName = "WallDefinition")]
public class CampBuildingDefinition : ScriptableObject
{
    [SerializeField] private CampBuildingKind kind = CampBuildingKind.Wall;
    [SerializeField] private CampConstructionSlotView sitePrefab;
    [SerializeField] private Vector2 footprintSize = Vector2.one;
    [SerializeField, Min(1)] private int woodCost = 5;
    [SerializeField, Min(1)] private int totalWork = 6;
    [SerializeField, Min(1)] private int workPerStep = 2;

    public CampBuildingKind Kind => kind;
    public CampConstructionSlotView SitePrefab => sitePrefab;
    public Vector2 FootprintSize => footprintSize;
    public int WoodCost => woodCost;
    public int TotalWork => totalWork;
    public int WorkPerStep => workPerStep;

    public bool IsValid => sitePrefab != null &&
        !sitePrefab.gameObject.activeSelf &&
        footprintSize.x > 0f && footprintSize.y > 0f &&
        woodCost > 0 && totalWork > 0 && workPerStep > 0;
}
