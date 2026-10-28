using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public class CampDefensePost : MonoBehaviour
{
    [SerializeField, Min(1)] private int postId = 1;
    [SerializeField] private string postName = "캠프";
    [SerializeField, Min(0.1f)] private float commandRange = 2.0f;
    // 캠프는 비워 두고 타워는 자기 건설 자리를 연결한다.
    [SerializeField] private CampConstructionSlotView requiredConstruction;
    [Tooltip("시작 레벨입니다. Editor Play 중 변경하면 실제 타워 레벨과 정원에 자동 적용됩니다.")]
    [SerializeField, Range(1, 9)] private int initialTowerLevel = 1;
#if UNITY_EDITOR
    private int observedInspectorTowerLevel;
#endif

    [SerializeField, Min(1)] private int maximumTowerHp = 20;
    [SerializeField, Min(0)] private int maximumTowerDefense = 5;
    public int MaximumHp => TryGetVitals(out CampConstructionData data) ? data.MaximumTowerHp : 0;
    public int MaximumDefense => TryGetVitals(out CampConstructionData data) ? data.MaximumTowerDefense : 0;
    public int CurrentHp => TryGetVitals(out CampConstructionData data) ? data.RemainingTowerHp : 0;
    public int CurrentDefense => TryGetVitals(out CampConstructionData data) ? data.RemainingTowerDefense : 0;

    private bool TryGetVitals(out CampConstructionData data)
    {
        data = null;
        return IsTower && IsAvailable &&
            RunCampConstruction.EnsureTowerVitals(requiredConstruction.SlotId, maximumTowerHp, maximumTowerDefense) &&
            RunCampConstruction.TryGetConstruction(requiredConstruction.SlotId, out data);
    }

    public bool TakeDamage(int damage, bool directHp = false)
    {
        bool damaged = TryGetVitals(out _) && RunCampConstruction.TryDamageTower(requiredConstruction.SlotId, damage, directHp);
        if (damaged) requiredConstruction.ShowHit();
        return damaged;
    }

    // 씬 전환·비활성화와 실제 건설 데이터 파괴를 구분한다.
    public static void NotifyConstructionDestroyed(int slotId)
    {
        if (RunCampConstruction.HasStarted(slotId)) return;
        foreach (CampDefensePost post in activePosts.Values)
            if (post != null && post.IsTower && post.requiredConstruction.SlotId == slotId)
                RunCampNpcs.HandleTowerDestroyed(post.postId, post.requiredConstruction.Footprint);
    }

    private BoxCollider2D interiorArea;
    private static readonly Dictionary<int, CampDefensePost> activePosts =
        new Dictionary<int, CampDefensePost>();

    public static IEnumerable<CampDefensePost> All => activePosts.Values;
    public Rect Footprint => requiredConstruction != null ? requiredConstruction.Footprint : new Rect(Position, Vector2.zero);
    public int PostId => postId;
    public string PostName => postName;
    public bool IsTower => requiredConstruction != null &&
        requiredConstruction.BuildingKind == CampBuildingKind.Tower;
    public Vector2 Position => IsTower
        ? (Vector2)requiredConstruction.transform.position : (Vector2)interiorArea.bounds.center;
    public int TowerLevel
    {
        get
        {
            if (IsTower && RunCampConstruction.TryGetConstruction(
                    requiredConstruction.SlotId, out CampConstructionData data))
            {
                if (data.TowerLevel == 0) data.TowerLevel = Mathf.Clamp(initialTowerLevel, 1, 9);
                return data.TowerLevel;
            }
            return Mathf.Clamp(initialTowerLevel, 1, 9);
        }
    }
    public bool TryUpgrade()
    {
        var settings = M6Balance.Snapshot();
        if (!IsTower || !IsAvailable || TowerLevel >= settings.towerMaximum ||
            !RunCampConstruction.TryGetConstruction(requiredConstruction.SlotId, out var data) ||
            !RunResources.TrySpendWood(TowerLevel * settings.towerWoodPerLevel)) return false;
        data.TowerLevel++; return true;
    }

    public int Capacity => IsTower ? RunCampNpcs.GetTowerCapacity(TowerLevel) : int.MaxValue;
    public int ReservedCount => RunCampNpcs.GetDefenseWorkerCount(postId);
    public int EnteredCount
    {
        get
        {
            int count = 0;
            foreach (CampNpcData npc in RunCampNpcs.All)
                if (npc.IsAlive && npc.IsInCampScene && npc.HasSavedPosition &&
                    npc.AssignedDefensePostId == postId && npc.EnteredDefensePostId == postId &&
                    Contains(npc.SavedPosition)) count++;
            return count;
        }
    }
    public bool IsAvailable => isActiveAndEnabled && interiorArea != null && interiorArea.enabled &&
        (requiredConstruction == null || (requiredConstruction.IsReady &&
         RunCampConstruction.IsCompleted(requiredConstruction.SlotId)));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActivePosts() => activePosts.Clear();

    // 비활성 부모 아래에서 설정한 뒤 부모와 함께 활성화한다.
    public void ConfigureDynamic(CampConstructionSlotView construction)
    {
        if (gameObject.activeInHierarchy)
            throw new System.InvalidOperationException("비활성 방어 지점에서 설정해 주세요.");
        requiredConstruction = construction;
        postId = construction.SlotId;
        postName = "타워 " + construction.SlotId;
        GetComponent<BoxCollider2D>().size = construction.FootprintSize;
    }

    private void Awake()
    {
#if UNITY_EDITOR
        observedInspectorTowerLevel = initialTowerLevel;
#endif
        interiorArea = GetComponent<BoxCollider2D>();
        interiorArea.isTrigger = true;
        if (IsTower)
        {
            // 기존 고정 지점도 건설 자리 중심을 사용한다.
            transform.position = requiredConstruction.transform.position;
            interiorArea.offset = Vector2.zero;
        }
    }

    private void OnEnable()
    {
        if (postId < 1 || (activePosts.TryGetValue(postId, out CampDefensePost existing) && existing != this))
        {
            Debug.LogError("방어 위치 ID는 1 이상이며 서로 달라야 합니다.", this);
            enabled = false;
            return;
        }
        activePosts[postId] = this;
    }

    private void OnDisable()
    {
        if (activePosts.TryGetValue(postId, out CampDefensePost registered) && ReferenceEquals(registered, this))
            activePosts.Remove(postId);
    }

    public bool Contains(Vector2 position) => IsAvailable && interiorArea.OverlapPoint(position);

    public Vector2 GetEntryPosition(int seatIndex)
    {
        if (!IsTower) return Position;
        float x = ((seatIndex % 3) - 1) * interiorArea.bounds.size.x * 0.2f;
        return Position + new Vector2(Capacity == 1 ? 0f : x, 0f);
    }

    public static bool TryGetActive(int id, out CampDefensePost post) =>
        activePosts.TryGetValue(id, out post) && post.IsAvailable;

    public static CampDefensePost FindNearest(Vector2 position, bool towerOnly = false)
    {
        CampDefensePost nearest = null;
        float nearestDistance = float.PositiveInfinity;
        foreach (CampDefensePost post in activePosts.Values)
        {
            if (!post.IsAvailable || (towerOnly && !post.IsTower)) continue;
            float distance = (post.Position - position).sqrMagnitude;
            if (distance > post.commandRange * post.commandRange || distance > nearestDistance ||
                (nearest != null && distance == nearestDistance && post.PostId > nearest.PostId)) continue;
            nearest = post;
            nearestDistance = distance;
        }
        return nearest;
    }

#if UNITY_EDITOR
    [ContextMenu("테스트/M3 상태 출력")]
    private void LogVitals()
    {
        if (Application.isPlaying) Debug.Log($"{PostName}: HP {CurrentHp}, 방어 {CurrentDefense}, 예약 {ReservedCount}", this);
    }
    [ContextMenu("테스트/M3 일반 피해 1")]
    private void TestNormalDamage() { if (Application.isPlaying) { TakeDamage(1); LogVitals(); } }
    [ContextMenu("테스트/M3 직접 HP 피해 1")]
    private void TestDirectDamage() { if (Application.isPlaying) { TakeDamage(1, true); LogVitals(); } }
    [ContextMenu("테스트/M3 타워 붕괴")]
    private void TestCollapse() { if (Application.isPlaying) { TakeDamage(int.MaxValue, true); LogVitals(); } }

    private void Update()
    {
        if (!Application.isPlaying || !IsTower ||
            !RunCampConstruction.TryGetConstruction(requiredConstruction.SlotId, out _)) return;

        if (initialTowerLevel != observedInspectorTowerLevel)
        {
            ApplyTestTowerLevel();
        }
        else
        {
            // 씬 재진입 시 Inspector 기본값으로 저장된 런 레벨을 덮어쓰지 않는다.
            initialTowerLevel = TowerLevel;
            observedInspectorTowerLevel = initialTowerLevel;
        }
    }

    [ContextMenu("테스트/타워 레벨을 Inspector 초기값으로 적용")]
    private void ApplyTestTowerLevel()
    {
        if (!Application.isPlaying || !IsTower || !RunCampConstruction.TryGetConstruction(
                requiredConstruction.SlotId, out CampConstructionData data)) return;
        int currentLevel = TowerLevel;
        int level = Mathf.Clamp(initialTowerLevel, 1, 9);
        if (ReservedCount > RunCampNpcs.GetTowerCapacity(level))
        {
            Debug.LogWarning("새 정원을 초과하는 예약 인원을 먼저 회수해 주세요.", this);
            initialTowerLevel = currentLevel;
            observedInspectorTowerLevel = currentLevel;
            return;
        }
        data.TowerLevel = level;
        initialTowerLevel = level;
        observedInspectorTowerLevel = level;
    }
#endif
}