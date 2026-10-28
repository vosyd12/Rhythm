using TMPro;
using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class CampConstructionSlotView : MonoBehaviour
{
    [Header("기존 시스템")]
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private NightDefenseTester nightDefenseTester;
    [Header("표시 대상")]
    [SerializeField] private SpriteRenderer marker;
    [SerializeField] private TMP_Text promptText;
    [Header("건설 자리")]
    [SerializeField, Min(1)] private int slotId = 1;
    [SerializeField] private CampBuildingKind buildingKind = CampBuildingKind.Wall;
    [SerializeField] private Vector2 footprintSize = Vector2.one;
    [Header("착공 비용")]
    [SerializeField, Min(1)] private int woodCost = 5;
    [Header("접근 거리")]
    [SerializeField, Min(0.1f)] private float interactionRange = 1.2f;
    [Header("표시 색상")]
    [SerializeField] private Color idleColor = new Color(0.55f, 0.65f, 0.75f, 0.5f);
    [SerializeField] private Color nearbyColor = new Color(1f, 0.85f, 0.3f, 1f);
    [SerializeField] private Color startedColor = new Color(0.3f, 0.8f, 1f, 1f);
    [Header("건설 작업량")]
    [SerializeField, Min(1)] private int totalWork = 6;
    [SerializeField, Min(1)] private int workPerAPWithOne = 2;
    [Header("완공 표시")]
    [SerializeField] private SpriteRenderer completedWall;
    [SerializeField] private Color completedColor = new Color(0.4f, 1f, 0.5f, 1f);

    private static readonly Dictionary<int, CampConstructionSlotView> activeSlots =
        new Dictionary<int, CampConstructionSlotView>();
    private Color originalCompletedColor;
    private float lastHitTime = float.NegativeInfinity;
    private bool isInitialized;
    private bool isDynamic;

    public static IEnumerable<CampConstructionSlotView> All => activeSlots.Values;
    public int SlotId => slotId;
    public int WoodCost => woodCost;
    public int TotalWork => totalWork;
    public int WorkPerStep => workPerAPWithOne;
    public CampBuildingKind BuildingKind => buildingKind;
    public Vector2 FootprintSize => footprintSize;
    public bool IsDynamic => isDynamic;
    public bool IsReady => isInitialized && isActiveAndEnabled;
    public bool CanReceiveNpcCommand => IsReady && CanInteract(true) &&
        (nightDefenseTester.IsPreparing || nightDefenseTester.IsRunning);
    public Rect Footprint => new Rect(
        (Vector2)transform.position - footprintSize * 0.5f, footprintSize);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveSlots() => activeSlots.Clear();

    // 반드시 비활성 프리팹을 복제한 뒤, 활성화하기 전에 호출한다.
    public void ConfigureDynamic(CampPlacedBuildingData data,
        PlayerMovement player, NightDefenseTester battle)
    {
        if (gameObject.activeInHierarchy || isInitialized)
            throw new System.InvalidOperationException("비활성 건설 자리에서 설정해 주세요.");

        isDynamic = true;
        playerMovement = player;
        nightDefenseTester = battle;
        promptText = null;
        slotId = data.SlotId;
        buildingKind = data.Kind;
        footprintSize = data.Size;
        woodCost = data.WoodCost;
        totalWork = data.TotalWork;
        workPerAPWithOne = data.WorkPerStep;
        transform.position = data.Position;
        if (buildingKind == CampBuildingKind.Tower)
        {
            var area = new GameObject("TowerDefenseArea");
            area.SetActive(false);
            area.transform.SetParent(transform, false);
            area.AddComponent<BoxCollider2D>();
            area.AddComponent<CampDefensePost>().ConfigureDynamic(this);
            area.SetActive(true);
        }
    }

    private void Awake()
    {
        if (playerMovement == null || nightDefenseTester == null ||
            marker == null || completedWall == null || marker == completedWall ||
            (!isDynamic && promptText == null) || slotId < 1 || woodCost < 1 ||
            totalWork < 1 || workPerAPWithOne < 1 || interactionRange <= 0f ||
            footprintSize.x <= 0f || footprintSize.y <= 0f)
        {
            Debug.LogError("건설 자리 참조, ID, 비용과 범위를 확인해 주세요.", this);
            enabled = false;
            return;
        }

        originalCompletedColor = completedWall.color;
        completedWall.enabled = false;
        if (promptText != null)
        {
            promptText.raycastTarget = false;
            promptText.enabled = false;
        }
        isInitialized = true;
    }

    private void OnEnable()
    {
        if (!isInitialized) return;
        if (activeSlots.TryGetValue(slotId, out CampConstructionSlotView existing) &&
            existing != null && existing != this)
        {
            Debug.LogError($"건설 슬롯 ID {slotId}가 중복되었습니다.", this);
            enabled = false;
            return;
        }

        activeSlots[slotId] = this;
        // 기존 고정 자리만 예전 메뉴 이벤트를 지원한다.
        if (!isDynamic)
            playerMovement.InteractionRequested += HandleInteractionRequested;
        RefreshView();
    }

    private void OnDisable()
    {
        if (playerMovement != null)
            playerMovement.InteractionRequested -= HandleInteractionRequested;
        if (activeSlots.TryGetValue(slotId, out CampConstructionSlotView registered) &&
            object.ReferenceEquals(registered, this))
            activeSlots.Remove(slotId);
        if (promptText != null) promptText.enabled = false;
    }

    private void LateUpdate()
    {
        if (isInitialized) RefreshView();
    }

    private bool CanInteract(bool allowDuringNight = false)
    {
        if (PlayerCombatStats.ActionsBlocked || !playerMovement.isActiveAndEnabled || !nightDefenseTester.isActiveAndEnabled ||
            !(nightDefenseTester.IsPreparing || (allowDuringNight && nightDefenseTester.IsRunning)) ||
            nightDefenseTester.IsChangingScene)
            return false;

        return ((Vector2)playerMovement.transform.position -
                (Vector2)transform.position).sqrMagnitude <= interactionRange * interactionRange;
    }

    private void HandleInteractionRequested()
    {
        if (!CanInteract(true) ||
            FindNearestCommandSlot(playerMovement.transform.position) != this) return;
        RunCampConstruction.TryStart(slotId, woodCost, totalWork, workPerAPWithOne);
        RefreshView();
    }

    private void RefreshView()
    {
        bool started = RunCampConstruction.TryGetConstruction(slotId, out CampConstructionData data);
        bool completed = started && data.IsCompleted;
        completedWall.enabled = completed;
        if (buildingKind == CampBuildingKind.Tower)
            completedWall.color = Time.time - lastHitTime < 0.18f
                ? new Color(1f, 0.35f, 0.4f, originalCompletedColor.a) : originalCompletedColor;
        bool nearby = CanReceiveNpcCommand &&
            FindNearestCommandSlot(playerMovement.transform.position) == this;
        marker.color = completed ? completedColor :
            started ? startedColor : nearby ? nearbyColor : idleColor;

        // 동적 자리들은 각자 TMP를 만들지 않는다. 공통 UI가 GetStatusText를 사용한다.
        if (promptText == null) return;
        promptText.enabled = nearby && !NightScreenUI.HasContextPanel;
        if (nearby) promptText.text = GetStatusText();
    }

    public void ShowHit() { lastHitTime = Time.time; }

    public string GetStatusText()
    {
        if (!RunCampConstruction.TryGetConstruction(slotId, out CampConstructionData data))
            return isDynamic ? "착공 데이터 없음 · B에서 건물을 선택해 다시 배치" :
                $"미착공 | 목재 {woodCost}\n보유 목재 {RunResources.StoredWood}\nR : 착공 | B : 건설창 열기";
        if (data.IsCompleted) return RunCampConstruction.NeedsRepair(slotId)
            ? "손상됨 · 백수/공병을 데려와 Space 수리 · 2초/목재 1 · HP/방어 20%"
            : "건설 완료";

        int count = RunCampConstruction.GetGroupWorkerCount(slotId);
        int arrived = 0;
        foreach (CampConstructionData member in data.Group.Members)
            arrived += RunCampNpcs.GetWorkingConstructionWorkerCount(member.SlotId);
        string worker = count == 0
            ? "작업자 배정 대기 · F로 백수/공병 호출 후 Space로 배치"
            : arrived == 0 ? $"작업자 이동 중 · {arrived}/{count}명 도착"
            : $"건설 작업 중 · {arrived}/{count}명 도착 · Space로 추가 배치";
        if (nightDefenseTester != null &&
            !(nightDefenseTester.IsPreparing || nightDefenseTester.IsRunning))
            worker = "건설 일시 중단 · 전투 결과 상태";
        else if (Time.deltaTime <= 0f)
            worker = "건설 일시 중단 · 게임 시간 정지";
        return $"착공됨 · 건설 {data.Progress01 * 100f:F1}% · 작업량 {data.CompletedWork:F2}/{data.TotalWork}\n{data.GroupSize}칸 · 배정 {count}명 · 도착 {arrived}명\n{worker}";
    }

    public static bool TryGetActiveSlot(int id, out CampConstructionSlotView slot)
    {
        slot = null;
        if (!activeSlots.TryGetValue(id, out CampConstructionSlotView found) ||
            found == null || !found.IsReady) return false;
        slot = found;
        return true;
    }

    public static CampConstructionSlotView FindNearestCommandSlot(Vector2 playerPosition)
    {
        CampConstructionSlotView nearest = null;
        float best = float.PositiveInfinity;
        foreach (CampConstructionSlotView slot in activeSlots.Values)
        {
            if (slot == null || !slot.CanReceiveNpcCommand) continue;
            float distance = ((Vector2)slot.transform.position - playerPosition).sqrMagnitude;
            if (distance > best || (nearest != null && distance == best && slot.SlotId >= nearest.SlotId))
                continue;
            nearest = slot;
            best = distance;
        }
        return nearest;
    }
}
