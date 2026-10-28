using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 카메라 추적 뒤, 플레이어 공격보다 먼저 배치 입력을 처리한다.
[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
public class CampWallPlacementController : MonoBehaviour
{
    [SerializeField] private Camera worldCamera;
    [SerializeField] private PlayerMovement player;
    [SerializeField] private NightDefenseTester battle;
    [SerializeField] private CampWindowController windows;
    [SerializeField] private Transform campCenter;
    [SerializeField] private Transform buildingRoot;
    [SerializeField] private Transform leftBoundary;
    [SerializeField] private Transform rightBoundary;
    [SerializeField] private Transform bottomBoundary;
    [SerializeField] private Transform topBoundary;
    [SerializeField] private CampBuildingDefinition wallDefinition;

    [SerializeField] private CampBuildingDefinition towerDefinition;

    // 현재 배치하려는 건물의 설정.
    private CampBuildingDefinition selectedDefinition;

    private string SelectedBuildingName =>
        selectedDefinition != null &&
        selectedDefinition.Kind == CampBuildingKind.Tower
            ? "타워"
            : "벽";
    [SerializeField] private SpriteRenderer previewPrefab;
    [SerializeField] private TMP_Text placementText;
    [Header("배치 범위")]
    [SerializeField, Min(0.01f)] private float cellSize = 1f;
    [SerializeField] private Vector2 gridOrigin;
    [Tooltip("현재 단계의 캠프 중심 배치 제한. 0이면 맵 전체.")]
    [SerializeField, Min(0f)] private float campBuildRadius = 10f;
    [SerializeField] private float placementZ;
    [Header("미리보기")]
    [SerializeField] private Color validColor = new Color(0.1f, 1f, 0.1f, 0.7f);
    [SerializeField] private Color blockedColor = new Color(1f, 0.1f, 0.1f, 0.7f);
    [SerializeField] private Color noWoodColor = new Color(1f, 0.65f, 0f, 0.7f);

    public static CampWallPlacementController Instance { get; private set; }
    public static bool IsPlacing => Instance != null && Instance.isPlacing;

    private readonly List<Vector2Int> candidates = new List<Vector2Int>();
    private readonly List<SpriteRenderer> ghosts = new List<SpriteRenderer>();
    private Rect mapBounds;
    public string ContextMessage => ready && isActiveAndEnabled ? placementText.text : "";
    public bool CanBeginPlacement => CanBuild && !GameExitUI.IsWorldPaused;
    public int WallWoodCost => wallDefinition != null ? wallDefinition.WoodCost : 0;
    public int TowerWoodCost => towerDefinition != null ? towerDefinition.WoodCost : 0;
    public CampBuildingDefinition DefinitionFor(CampBuildingKind kind) => kind == CampBuildingKind.Wall ? wallDefinition : towerDefinition;
    private bool ready;
    private bool isPlacing;
    private bool dragging;
    private bool waitForRelease;
    private Vector2Int dragStart;
    private int dragAxis;
    private float messageUntil;
    private string lastMessage;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInstance() => Instance = null;

    private bool CanBuild => ready && battle.isActiveAndEnabled &&
        (battle.IsPreparing || battle.IsRunning) && !battle.IsChangingScene && !PlayerCombatStats.ActionsBlocked;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("벽 배치 컨트롤러는 씬에 하나만 두세요.", this);
            enabled = false;
            return;
        }
        if (worldCamera == null || player == null || battle == null || windows == null ||
            campCenter == null || buildingRoot == null || !buildingRoot.gameObject.activeInHierarchy ||
            leftBoundary == null || rightBoundary == null || bottomBoundary == null ||
            topBoundary == null || wallDefinition == null || !wallDefinition.IsValid ||
            wallDefinition.Kind != CampBuildingKind.Wall ||
            wallDefinition.SitePrefab.GetComponent<CampWall>() == null ||
            previewPrefab == null || previewPrefab.sprite == null || placementText == null ||
            cellSize <= 0f || wallDefinition.FootprintSize.x > cellSize ||
            wallDefinition.FootprintSize.y > cellSize)
        {
            Debug.LogError("벽 배치 참조, 비활성 벽 프리팹과 격자 크기를 확인해 주세요.", this);
            enabled = false;
            return;
        }
        mapBounds = Rect.MinMaxRect(leftBoundary.position.x, bottomBoundary.position.y,
            rightBoundary.position.x, topBoundary.position.y);
        if (mapBounds.width <= 0f || mapBounds.height <= 0f)
        {
            enabled = false;
            return;
        }
        placementText.raycastTarget = false;
        placementText.text = "";
        Instance = this;
        ready = true;
    }

    private void Start()
    {
        // 씬 복원에는 목재를 소비하거나 TryStart를 다시 호출하지 않는다.
        foreach (CampPlacedBuildingData data in RunCampPlacedBuildings.All)
        {
            if (!CampConstructionSlotView.TryGetActiveSlot(data.SlotId, out _))
                CreateSite(data);
        }
    }

    public void BeginWallPlacement()
    {
        BeginPlacement(wallDefinition);
    }

#if UNITY_EDITOR
    [ContextMenu("테스트/M4 타워 배치 연결 상태 출력")]
    private void LogTowerPlacementState()
    {
        Debug.Log($"[타워 배치] 초기화 {ready}, 착공 가능 {CanBuild}, " +
            $"전투 상태 {(battle != null ? battle.State.ToString() : "연결 없음")}, " +
            $"치료 {PlayerCombatStats.ActionsBlocked}, 목재 {RunResources.StoredWood}, " +
            $"타워 정의 {(towerDefinition != null ? towerDefinition.name : "없음")}, " +
            $"정의 유효 {(towerDefinition != null && towerDefinition.IsValid)}, " +
            $"배치 모드 {isPlacing}", this);
    }
#endif

    public void BeginTowerPlacement()
    {
        if (towerDefinition == null ||
            towerDefinition.Kind != CampBuildingKind.Tower)
        {
            Debug.LogError(
                "Tower Definition에 타워 건설 정의를 연결해 주세요.",
                this);
            return;
        }

        BeginPlacement(towerDefinition);
    }

    private void BeginPlacement(CampBuildingDefinition definition)
    {
        if (!CanBuild)
        {
            return;
        }

        if (definition == null ||
            !definition.IsValid ||
            definition.FootprintSize.x > cellSize ||
            definition.FootprintSize.y > cellSize)
        {
            Debug.LogError(
                "비활성 건설 프리팹과 건물 크기·격자 크기를 확인해 주세요.",
                this);
            return;
        }

        selectedDefinition = definition;

        windows.CloseAll();

        isPlacing = true;
        dragging = false;
        waitForRelease = true;
        messageUntil = 0f;

        HideGhosts();
    }

    public void CancelPlacement()
    {
        isPlacing = false;
        dragging = false;
        waitForRelease = true;
        HideGhosts();
        if (placementText != null) placementText.text = "";
    }

    private void OnDisable() => CancelPlacement();
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void LateUpdate()
    {
        if (!ready) return;
        placementText.enabled = !NightScreenUI.HasContextPanel;
        if (!isPlacing)
        {
            CampConstructionSlotView nearest =
                CampConstructionSlotView.FindNearestCommandSlot(player.transform.position);
            placementText.text = CanBuild && !CampWindowController.WorldInputBlocked && nearest != null
                ? nearest.GetStatusText() : "";
            return;
        }

        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;
        if (!CanBuild || CampWindowController.WorldInputBlocked)
        {
            CancelPlacement();
            return;
        }
        if ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
            (mouse != null && mouse.rightButton.wasPressedThisFrame))
        {
            CancelPlacement();
            return;
        }
        if (!Application.isFocused || mouse == null || Time.deltaTime <= 0f)
        {
            dragging = false;
            waitForRelease = true;
            HideGhosts();
            return;
        }

        if (waitForRelease)
        {
            HideGhosts();
            placementText.text =
               $"{SelectedBuildingName} 배치 · 왼쪽 버튼을 놓은 뒤 위치를 선택해 주세요.";
            if (!mouse.leftButton.isPressed) waitForRelease = false;
            return;
        }

        bool overUI = EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject();
        if (overUI || !TryGetCursorCell(mouse.position.ReadValue(), out Vector2Int cell))
        {
            HideGhosts();
            // UI 위나 Game 영역 밖에서 놓으면 이번 드래그는 확정하지 않는다.
            if (!mouse.leftButton.isPressed) dragging = false;
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            dragging = true;
            dragStart = cell;
            dragAxis = 0;
        }
        BuildCandidateList(cell);
        ShowPreview();

        if (dragging && mouse.leftButton.wasReleasedThisFrame)
        {
            CommitCandidates();
            dragging = false;
        }
        else if (dragging && !mouse.leftButton.isPressed)
        {
            dragging = false;
        }
    }

    private bool TryGetCursorCell(Vector2 screen, out Vector2Int cell)
    {
        cell = default;
        if (!worldCamera.pixelRect.Contains(screen)) return false;
        Plane plane = new Plane(Vector3.forward, new Vector3(0f, 0f, placementZ));
        Ray ray = worldCamera.ScreenPointToRay(screen);
        if (!plane.Raycast(ray, out float distance)) return false;
        Vector2 position = ray.GetPoint(distance);
        Vector2 relative = (position - gridOrigin) / cellSize;
        cell = new Vector2Int(Mathf.RoundToInt(relative.x), Mathf.RoundToInt(relative.y));
        return true;
    }

    private Vector3 CellPosition(Vector2Int cell)
    {
        Vector2 position = gridOrigin + new Vector2(cell.x, cell.y) * cellSize;
        return new Vector3(position.x, position.y, placementZ);
    }

    private void BuildCandidateList(Vector2Int cursor)
    {
        candidates.Clear();

        if (!dragging ||
            selectedDefinition.Kind == CampBuildingKind.Tower)
        {
            candidates.Add(cursor);
            return;
        }

        Vector2Int difference = cursor - dragStart;
        if (dragAxis == 0 && difference != Vector2Int.zero)
            dragAxis = Mathf.Abs(difference.x) >= Mathf.Abs(difference.y) ? 1 : 2;

        int amount = dragAxis == 2 ? difference.y : difference.x;
        int direction = amount < 0 ? -1 : 1;
        // 미리보기 길이는 맵 크기로 제한한다. 전체 건물 수 제한은 아니다.
        int maximumSteps = Mathf.CeilToInt(
            Mathf.Max(mapBounds.width, mapBounds.height) / cellSize) + 2;
        int steps = Mathf.Min(Mathf.Abs(amount), maximumSteps);
        for (int i = 0; i <= steps; i++)
            candidates.Add(dragStart + (dragAxis == 2
                ? new Vector2Int(0, i * direction) : new Vector2Int(i * direction, 0)));
    }

    private bool CanPlace(Vector3 position, out CampConstructionSlotView reusable)
    {
        reusable = null;
        Vector2 size = selectedDefinition.FootprintSize;
        Rect footprint = new Rect((Vector2)position - size * 0.5f, size);
        if (footprint.xMin < mapBounds.xMin || footprint.xMax > mapBounds.xMax ||
            footprint.yMin < mapBounds.yMin || footprint.yMax > mapBounds.yMax)
            return false;

        if (campBuildRadius > 0f)
        {
            Vector2 offset = (Vector2)position - (Vector2)campCenter.position;
            Vector2 farthest = new Vector2(Mathf.Abs(offset.x) + size.x * 0.5f,
                Mathf.Abs(offset.y) + size.y * 0.5f);
            if (farthest.sqrMagnitude > campBuildRadius * campBuildRadius) return false;
        }

        if (battle.BlocksRiftConstruction(footprint)) return false;

        foreach (CampPlacementBlocker blocker in CampPlacementBlocker.All)
            if (blocker != null && blocker.Overlaps(footprint)) return false;

        foreach (CampConstructionSlotView slot in CampConstructionSlotView.All)
        {
            if (slot == null || !slot.IsReady || !slot.Footprint.Overlaps(footprint)) continue;
            bool sameBuilding =
                slot.BuildingKind == selectedDefinition.Kind &&
                ((Vector2)slot.transform.position - (Vector2)position).sqrMagnitude < 0.000001f &&
                (slot.FootprintSize - size).sqrMagnitude < 0.000001f;

            if (sameBuilding && !RunCampConstruction.HasStarted(slot.SlotId))
                reusable = slot;

            else
            {
                // 벽/타워 종류를 구분해 둔다. 결합 기능 전에는 단순 겹침을 허용하지 않는다.
                return false;
            }
        }
        return true;
    }

    private void ShowPreview()
    {
        int budget = RunResources.StoredWood;
        int tickets = RunCampaign.Tickets(selectedDefinition.Kind);
        int available = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            while (ghosts.Count <= i)
            {
                SpriteRenderer ghost = Instantiate(previewPrefab, transform);
                ghost.gameObject.SetActive(false);
                ghosts.Add(ghost);
            }
            SpriteRenderer visual = ghosts[i];
            Vector3 position = CellPosition(candidates[i]);
            bool valid = CanPlace(position, out CampConstructionSlotView reusable);
            int cost = reusable != null ? reusable.WoodCost : selectedDefinition.WoodCost;
            bool affordable = valid && (tickets > 0 || budget >= cost);
            if (affordable)
            {
                if (tickets > 0) tickets--; else budget -= cost;
                available++;
            }
            visual.transform.position = position;
            visual.transform.rotation = Quaternion.identity;
            Vector2 spriteSize = visual.sprite.bounds.size;
            Vector2 size = selectedDefinition.FootprintSize;
            Vector3 parentScale = transform.lossyScale;
            visual.transform.localScale = new Vector3(
                size.x / spriteSize.x / Mathf.Abs(parentScale.x),
                size.y / spriteSize.y / Mathf.Abs(parentScale.y), 1f);
            visual.color = !valid ? blockedColor : affordable ? validColor : noWoodColor;
            visual.gameObject.SetActive(true);
        }
        for (int i = candidates.Count; i < ghosts.Count; i++)
            ghosts[i].gameObject.SetActive(false);

        placementText.text = Time.time < messageUntil ? lastMessage :
            $"벽 {candidates.Count}자리 중 {available}개 착공 가능 · 목재 {RunResources.StoredWood - budget}\n" +
            "초록: 착공 가능 / 빨강: 배치 불가 / 주황: 목재 부족\n" +
            "클릭: 한 개 · 드래그: 가로/세로 · 우클릭/Esc: 취소";

        string controls =
            selectedDefinition.Kind == CampBuildingKind.Wall
                ? "클릭: 한 개 · 드래그: 가로/세로 · 우클릭/Esc: 취소"
                : "클릭: 타워 한 개 · 우클릭/Esc: 취소";

                placementText.text = Time.time < messageUntil ? lastMessage :
                    $"{SelectedBuildingName} {candidates.Count}자리 중 {available}개 착공 가능 · 목재 {RunResources.StoredWood - budget}\n" +
                    "초록: 착공 가능 / 빨강: 배치 불가 / 주황: 목재 부족\n" +
                    controls;
    }

    private void CommitCandidates()
    {
        int created = 0;
        int blocked = 0;
        int noWood = 0;
        int groupSlotId = 0;
        foreach (Vector2Int cell in candidates)
        {
            Vector3 position = CellPosition(cell);
            if (!CanPlace(position, out CampConstructionSlotView reusable))
            {
                blocked++;
                continue;
            }
            int cost = reusable != null ? reusable.WoodCost : selectedDefinition.WoodCost;
            bool useTicket = RunCampaign.Tickets(selectedDefinition.Kind) > 0;
            if (!useTicket && RunResources.StoredWood < cost)
            {
                noWood++;
                continue;
            }

            CampPlacedBuildingData newData = null;
            CampConstructionSlotView site = reusable;
            if (site == null)
            {
                newData = new CampPlacedBuildingData(
                  RunCampPlacedBuildings.AllocateId(), selectedDefinition, position);
                site = CreateSite(newData);
                if (site == null)
                {
                    blocked++;
                    continue;
                }
            }

            if (useTicket
                ? RunCampConstruction.CompleteWithTicket(site.SlotId, site.TotalWork, site.WorkPerStep)
                : RunCampConstruction.TryStart(site.SlotId, site.WoodCost, site.TotalWork, site.WorkPerStep, groupSlotId))
            {
                if (useTicket) RunCampaign.ConsumeTicket(selectedDefinition.Kind);
                else if (groupSlotId == 0) groupSlotId = site.SlotId;
                if (newData != null) RunCampPlacedBuildings.Add(newData);
                created++;
            }
            else if (newData != null)
            {
                site.gameObject.SetActive(false);
                Destroy(site.gameObject);
            }
        }
        lastMessage = $"{created}개 착공 · 배치 불가 {blocked}개 · 목재 부족 {noWood}개 제외";
        messageUntil = Time.time + 1.5f;
        placementText.text = lastMessage;
    }

    private CampConstructionSlotView CreateSite(CampPlacedBuildingData data)
    {
        if (data.Definition == null || data.Definition.SitePrefab == null ||
            data.Definition.SitePrefab.gameObject.activeSelf)
        {
            Debug.LogError("복원할 건설 프리팹이 없거나 활성 상태입니다.", this);
            return null;
        }
        CampConstructionSlotView site = Instantiate(data.Definition.SitePrefab, buildingRoot);
        site.name = $"{data.Kind}_{data.SlotId}";
        site.ConfigureDynamic(data, player, battle);
        site.gameObject.SetActive(true);
        if (site.IsReady) return site;
        site.gameObject.SetActive(false);
        Destroy(site.gameObject);
        return null;
    }

    private void HideGhosts()
    {
        foreach (SpriteRenderer ghost in ghosts)
            if (ghost != null) ghost.gameObject.SetActive(false);
    }
}
