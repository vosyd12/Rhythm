using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class CampNpcIdleView : MonoBehaviour
{
    [Header("연결할 NPC")]
    [SerializeField, Min(1)]
    private int npcId = 1;

    [Header("대기 위치와 이동 경계")]
    [SerializeField] private Transform idleCenter;
    [SerializeField] private Transform leftBoundary;
    [SerializeField] private Transform rightBoundary;

    [Header("NPC 외형")]
    [SerializeField] private SpriteRenderer visual;

    [Header("대기 중 이동")]
    [SerializeField, Min(0.0f)]
    private float roamRadius = 1.5f;

    [SerializeField, Min(0.01f)]
    private float moveSpeed = 0.8f;

    [SerializeField, Min(0.0f)]
    private float boundaryPadding = 0.3f;

    [SerializeField]
    private Vector2 waitSecondsRange = new Vector2(1.0f, 3.0f);

    [SerializeField, Min(0.01f)]
    private float workMoveSpeed = 3.0f;

    // 건물 중심에서 작업자까지의 간격. 부호 대신 배치 순서로 좌우를 정한다.
    [SerializeField]
    private float workOffsetX = -0.45f;

    [Header("플레이어 추종")]
    [SerializeField]
    private PlayerMovement playerMovement;

    [SerializeField, Min(0.1f)]
    private float followSpacing = 1.0f;

    [SerializeField, Min(0.0f)]
    private float followCatchUpSpeed = 1.5f;

    [SerializeField, Min(0.01f)]
    private float followAcceleration = 10.0f;

    [SerializeField, Min(0.01f)]
    private float followSlowdown = 4.0f;

    [SerializeField, Min(0.01f)]
    private float followRestartDistance = 0.35f;

    [SerializeField] private Transform bottomBoundary;
    [SerializeField] private Transform topBoundary;

    [SerializeField]
    private string npcName = "";

    [SerializeField]
    private CampNpcProfession profession =
        CampNpcProfession.Unemployed;

    [SerializeField, Min(1.0f)]
    private float engineerBuildMultiplier = 1.5f;

    [Tooltip("비워 두면 기존 밤 전투의 기본 무기를 사용합니다.")]
    [SerializeField] private RangedWeaponData defenseWeapon;

    [SerializeField, Min(1)] private int maximumHp = 10;
    [SerializeField, Min(0)] private int maximumDefense = 3;
    private float nextExitSearchTime;
    private bool exitWarningShown;

    private Color originalVisualColor;
    private CampNpcData boundNpc;
    private bool isInitialized;
    private System.Random random;
    private float mapMinimumX;
    private float mapMaximumX;
    private float fixedZ;
    private float observedIdleCenterX = float.NaN;
    private int observedSlotId;
    private float minimumX;
    private float maximumX;
    private float mapMinimumY;
    private float mapMaximumY;
    private float targetX;
    private float waitRemaining;
    private bool isMoving;
    private int observedFollowerIndex = -1;
    private float currentFollowSpeed;
    private bool isFollowingPlayer;

    private readonly List<Vector2> wallPath =
    new List<Vector2>();

    private int wallPathIndex;
    private Vector2 wallPathDestination;
    private float nextWallPathTime;
    private bool hasWallPathDestination;

    public RangedWeaponData RecruitWeapon => defenseWeapon;
    public int RecruitTemplateId => npcId;
    public CampNpcProfession RecruitProfession => profession;
    public CampNpcData CreateRecruitPrototype(NightDefenseTester battle)
    {
        var npc = new CampNpcData(npcId, profession);
        npc.RecruitTemplateId = npcId;
        npc.MaximumHp = npc.CurrentHp = maximumHp;
        npc.MaximumDefense = npc.CurrentDefense = maximumDefense;
        npc.EngineerBuildMultiplier = engineerBuildMultiplier;
        npc.DefenseWeapon = defenseWeapon;
        npc.BaseAttackDamage = defenseWeapon != null ? defenseWeapon.Damage : battle.NpcFallbackDamage;
        npc.BaseAttackRange = defenseWeapon != null ? defenseWeapon.Range : battle.NpcFallbackRange;
        npc.BaseAttackRate = defenseWeapon != null ? defenseWeapon.AttacksPerSecond : battle.NpcFallbackAttackRate;
        return npc;
    }
    private static List<CampNpcIdleView> RecruitTemplates()
    {
        var result = new List<CampNpcIdleView>();
        foreach (var view in FindObjectsByType<CampNpcIdleView>())
            if (!RunCampNpcs.TryGetNpc(view.npcId, out var data) || !data.IsRecruited) result.Add(view);
        result.Sort((a, b) => a.npcId.CompareTo(b.npcId));
        return result;
    }
    public static bool TryChooseRecruitTemplate(CampNpcProfession profession, out CampNpcIdleView template)
    {
        var options = RecruitTemplates().FindAll(view => view.profession == profession);
        // 현재 씬에 공병 원형이 없으면 기존 작업자 외형을 재사용한다.
        if (options.Count == 0 && profession == CampNpcProfession.Engineer)
            options = RecruitTemplates().FindAll(view => view.profession == CampNpcProfession.Unemployed);
        template = options.Count > 0 ? options[UnityEngine.Random.Range(0, options.Count)] : null;
        return template != null;
    }
    public void CreateRecruitedView(CampNpcData npc)
    {
        CampNpcIdleView copy = Instantiate(this, npc.SavedPosition, transform.rotation, transform.parent);
        copy.name = "RecruitedNPC_" + npc.Id;
        copy.npcId = npc.Id; copy.npcName = npc.Name; copy.profession = npc.Profession;
        copy.defenseWeapon = npc.DefenseWeapon;
        copy.maximumHp = npc.MaximumHp; copy.maximumDefense = npc.MaximumDefense;
        copy.engineerBuildMultiplier = npc.EngineerBuildMultiplier;
    }
    public static void RestoreRecruitedViews()
    {
        var templates = RecruitTemplates();
        foreach (var npc in RunCampNpcs.All)
        {
            if (!npc.IsRecruited || !npc.IsAlive || npc.IsInCampScene) continue;
            var template = templates.Find(view => view.npcId == npc.RecruitTemplateId);
            if (template == null) template = templates.Find(view => view.profession == npc.Profession);
            if (template != null) template.CreateRecruitedView(npc);
        }
    }

    private void Start()
    {
        // 원형 오브젝트는 모집에 재사용하되, 새 지역의 비동행 병력은 생성하지 않는다.
        if (RunCampaign.RosterOwned && !RunCampNpcs.TryGetNpc(npcId, out _))
        { if (visual != null) visual.enabled = false; return; }
        if (idleCenter == null ||
            leftBoundary == null ||
            rightBoundary == null ||
            bottomBoundary == null ||
            topBoundary == null ||
            visual == null ||
            playerMovement == null)
        {
            FailInitialization(
                "대기 지점, 좌우·상하 경계, NPC 외형과 플레이어를 연결해 주세요.");
            return; 
        }

        if (idleCenter == transform || 
            idleCenter.IsChildOf(transform))
        { 
            FailInitialization("대기 지점은 NPC 바깥의 독립된 오브젝트로 만들어 주세요."); 
            return; 
        }

        float padding = Mathf.Max(0.0f, boundaryPadding);

        mapMinimumX = leftBoundary.position.x + padding;
        mapMaximumX = rightBoundary.position.x - padding;

        mapMinimumY = bottomBoundary.position.y + padding;
        mapMaximumY = topBoundary.position.y - padding;

        if (!(mapMinimumX <= mapMaximumX) ||
            !(mapMinimumY <= mapMaximumY))
        { 
            FailInitialization("좌우 경계와 Boundary Padding을 확인해 주세요."); 
            return; 
        }

        CampNpcData npc = RunCampNpcs.RegisterNpc(
                    npcId,
                    profession,
                    npcName,
                    engineerBuildMultiplier,
                    defenseWeapon, maximumHp, maximumDefense);

        if (npc == null || npc.IsInCampScene)
        { 
            FailInitialization("NPC ID는 1 이상이며 각 NPC마다 달라야 합니다."); 
            return; 
        }

        fixedZ = idleCenter.position.z;

        if (!npc.HasCampPosition)
        {
            Vector3 camp = idleCenter.position;

            camp.x = Mathf.Clamp(camp.x, mapMinimumX, mapMaximumX);
            camp.y = Mathf.Clamp(camp.y, mapMinimumY, mapMaximumY);

            npc.CampPosition = camp;
            npc.HasCampPosition = true;
            npc.IdleCenterX = camp.x;
        }

        Vector3 position = npc.HasSavedPosition ? npc.SavedPosition : npc.CampPosition;

        position.x = Mathf.Clamp(position.x, mapMinimumX, mapMaximumX);
        position.y = Mathf.Clamp(position.y, mapMinimumY, mapMaximumY);

        position.z = fixedZ;

        transform.position = position;
        if (npc.HasSavedPosition) { visual.flipX = npc.SavedFlipX; }

        originalVisualColor = visual.color;
        boundNpc = npc;
        isInitialized = true;
        boundNpc.IsInCampScene = boundNpc.IsAlive;
        visual.enabled = boundNpc.IsAlive;

        random = new System.Random(npcId);
        observedSlotId = npc.AssignedConstructionSlotId;

        RefreshIdleRange();
        SaveCurrentPosition();
    }

    private void OnEnable()
    {
        if (!IsBoundToCurrentNpc() || !boundNpc.IsAlive) { return; }
        boundNpc.IsInCampScene = true;
        SaveCurrentPosition();
    }

    private void OnDisable()
    {
        if (!IsBoundToCurrentNpc()) { return; }

        SaveCurrentPosition();

        boundNpc.WorkingConstructionSlotId = 0;
        boundNpc.EnteredDefensePostId = 0;
        boundNpc.IsInCampScene = false;
    }

    private bool IsBoundToCurrentNpc()
    {
        return isInitialized && boundNpc != null &&
            RunCampNpcs.TryGetNpc(npcId, out CampNpcData current) &&
            object.ReferenceEquals(boundNpc, current);
    }

    private void SaveCurrentPosition()
    {
        if (!IsBoundToCurrentNpc()) { return; }
        boundNpc.SavedPosition = transform.position;
        boundNpc.SavedFlipX = visual.flipX;
        boundNpc.HasSavedPosition = true;
    }

    private void LateUpdate()
    {
        if (GameExitUI.IsWorldPaused) return;
        if (IsBoundToCurrentNpc())
            visual.color = Time.time - boundNpc.LastHitTime < 0.18f
                ? new Color(1f, 0.35f, 0.4f, originalVisualColor.a) : originalVisualColor;
        if (IsBoundToCurrentNpc() && !boundNpc.IsAlive)
        {
            visual.enabled = false;
            boundNpc.IsInCampScene = false;
            return;
        }
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0.0f || !Application.isFocused || !IsBoundToCurrentNpc()) { return; }

        boundNpc.WorkingConstructionSlotId = 0;
        if (boundNpc.HasPendingTowerExit)
        {
            if (Time.time < nextExitSearchTime) return;
            nextExitSearchTime = Time.time + 0.5f;
            if (!TryFindTowerExit(out Vector2 exit))
            {
                if (!exitWarningShown) Debug.LogWarning("타워 퇴거 위치를 찾지 못했습니다. 안전한 위치를 재탐색합니다.", this);
                exitWarningShown = true;
                return;
            }
            transform.position = new Vector3(exit.x, exit.y, fixedZ);
            boundNpc.IdleCenterX = exit.x;
            boundNpc.HasPendingTowerExit = false;
            hasWallPathDestination = false;
            wallPath.Clear();
            SaveCurrentPosition();
            exitWarningShown = false;
        }

        if (boundNpc.HasAdvanceOrder)
        {
            if (MoveToPosition(boundNpc.AdvanceDestination, workMoveSpeed, deltaTime))
            {
                boundNpc.HasAdvanceOrder = false;
                boundNpc.IsHoldingAfterCollapse = true;
                boundNpc.IdleCenterX = transform.position.x;
            }
            return;
        }

        int followerIndex = RunCampNpcs.GetFollowerIndex(npcId);
        if (followerIndex >= 0)
        {
            isMoving = false;
            FollowPlayer(followerIndex, deltaTime);
            return;
        }

        if (observedFollowerIndex >= 0)
        {
            observedFollowerIndex = -1;
            currentFollowSpeed = 0.0f;
            isFollowingPlayer = false;
            BeginWaiting();
        }

        if (boundNpc.AssignedDefensePostId != 0)
        {
            isMoving = false;
            boundNpc.EnteredDefensePostId = 0;

            if (CampDefensePost.TryGetActive(
                    boundNpc.AssignedDefensePostId,
                    out CampDefensePost post))
            {
                // 내부에 들어오면 이동을 멈춘다.
                // 정확히 중앙에 도착할 필요는 없다.
                if (!post.Contains(transform.position))
                {
                    MoveToPosition(
                        post.GetEntryPosition(boundNpc.DefenseSeatIndex),
                        workMoveSpeed,
                        deltaTime);
                }
                if (post.Contains(transform.position))
                    boundNpc.EnteredDefensePostId = post.PostId;
            }

            return;
        }

        if (observedSlotId != boundNpc.AssignedConstructionSlotId)
        {
            observedSlotId = boundNpc.AssignedConstructionSlotId;
            BeginWaiting();
        }

        if (observedSlotId != 0)
        {
            if (TryGetWorkPosition(
                    observedSlotId,
                    out Vector2 workPosition))
            {
                bool hasArrived = MoveToPosition(
                    workPosition,
                    workMoveSpeed,
                    deltaTime);

                if (hasArrived)
                {
                    boundNpc.WorkingConstructionSlotId = observedSlotId;
                }
            }

            return;
        }

        if (boundNpc.IsReturningToCamp)
        {
            Vector2 campPosition = boundNpc.CampPosition;

            if (MoveToPosition(
                    campPosition,
                    workMoveSpeed,
                    deltaTime))
            {
                boundNpc.IsReturningToCamp = false;
                boundNpc.IdleCenterX = transform.position.x;
                RefreshIdleRange();
            }

            return;
        }

        if (boundNpc.IsHoldingAfterCollapse) return;

        if (observedIdleCenterX != boundNpc.IdleCenterX) { RefreshIdleRange(); }

        if (!isMoving)
        {
            waitRemaining -= deltaTime;
            if (waitRemaining > 0.0f) { return; }
            targetX = RandomRange(minimumX, maximumX);
            isMoving = true;
        }
        if (MoveToX(targetX, moveSpeed, deltaTime)) { BeginWaiting(); }
    }

    private bool IsSafeTowerExit(Vector2 point, float radius)
    {
        float extraPadding = Mathf.Max(0f, radius - Mathf.Max(0f, boundaryPadding));
        if (point.x < mapMinimumX + extraPadding || point.x > mapMaximumX - extraPadding ||
            point.y < mapMinimumY + extraPadding || point.y > mapMaximumY - extraPadding) return false;
        Rect old = boundNpc.CollapsedTowerFootprint;
        if (point.x >= old.xMin - radius && point.x <= old.xMax + radius &&
            point.y >= old.yMin - radius && point.y <= old.yMax + radius) return false;
        foreach (CampConstructionSlotView slot in CampConstructionSlotView.All)
        {
            if (!RunCampConstruction.HasStarted(slot.SlotId)) continue;
            Rect area = slot.Footprint;
            if (point.x >= area.xMin - radius && point.x <= area.xMax + radius &&
                point.y >= area.yMin - radius && point.y <= area.yMax + radius) return false;
        }
        foreach (Collider2D obstacle in Physics2D.OverlapCircleAll(point, radius))
            if (!obstacle.isTrigger && obstacle.transform != transform &&
                !obstacle.transform.IsChildOf(transform)) return false;
        return true;
    }

    private bool TryFindTowerExit(out Vector2 exit)
    {
        exit = transform.position;
        float radius = CampWallNavigation.Instance != null ? CampWallNavigation.Instance.AllyRadius : 0.2f;
        radius = Mathf.Max(0.2f, radius);
        float step = radius * 2f + 0.05f;
        Vector2 center = boundNpc.CollapsedTowerFootprint.center;
        int seat = Mathf.Abs(npcId % 8);
        // 주변부터 탐색하고 타워가 둘러싸여 있으면 맵 안쪽으로 범위를 넓힌다.
        float maximumDistance = Vector2.Distance(new Vector2(mapMinimumX, mapMinimumY),
            new Vector2(mapMaximumX, mapMaximumY));
        for (float distance = step; distance <= maximumDistance + step; distance += step)
        {
            int samples = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * distance / step));
            for (int i = 0; i < samples; i++)
            {
                float angle = (i + seat) * 2f * Mathf.PI / samples;
                Vector2 candidate = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                if (!IsSafeTowerExit(candidate, radius)) continue;
                bool overlapsNpc = false;
                foreach (CampNpcData other in RunCampNpcs.All)
                    if (other.Id != npcId && other.IsAlive && other.IsInCampScene && other.HasSavedPosition &&
                        !other.HasPendingTowerExit && ((Vector2)other.SavedPosition - candidate).sqrMagnitude < step * step)
                    { overlapsNpc = true; break; }
                if (overlapsNpc) continue;
                exit = candidate;
                return true;
            }
        }
        return false;
    }

#if UNITY_EDITOR
    [ContextMenu("테스트/M3 NPC 상태 출력")]
    private void LogVitals()
    {
        if (Application.isPlaying && IsBoundToCurrentNpc())
            Debug.Log($"{boundNpc.Name}: HP {boundNpc.CurrentHp}/{boundNpc.MaximumHp}, 방어 {boundNpc.CurrentDefense}/{boundNpc.MaximumDefense}, 생존 {boundNpc.IsAlive}", this);
    }
    [ContextMenu("테스트/M3 일반 피해 1")]
    private void TestNormalDamage() { if (Application.isPlaying) { RunCampNpcs.TryDamageNpc(npcId, 1); LogVitals(); } }
    [ContextMenu("테스트/M3 일반 피해 100")]
    private void TestLargeDamage() { if (Application.isPlaying) { RunCampNpcs.TryDamageNpc(npcId, 100); LogVitals(); } }
    [ContextMenu("테스트/M3 직접 HP 피해 1")]
    private void TestDirectDamage() { if (Application.isPlaying) { RunCampNpcs.TryDamageNpc(npcId, 1, true); LogVitals(); } }
#endif

    private void RefreshIdleRange()
    {
        // 대기 중심은 걸을 때마다 바꾸지 않는다. 해제 당시 위치를 계속 사용한다.
        boundNpc.IdleCenterX = Mathf.Clamp(boundNpc.IdleCenterX, mapMinimumX, mapMaximumX);
        observedIdleCenterX = boundNpc.IdleCenterX;
        float radius = Mathf.Max(0.0f, roamRadius);
        minimumX = Mathf.Max(mapMinimumX, observedIdleCenterX - radius);
        maximumX = Mathf.Min(mapMaximumX, observedIdleCenterX + radius);
        BeginWaiting();
    }

    private void BeginWaiting()
    {
        isMoving = false;
        float min = Mathf.Max(0.1f, waitSecondsRange.x);
        waitRemaining = RandomRange(min, Mathf.Max(min, waitSecondsRange.y));
    }

    private float RandomRange(float min, float max) =>
        Mathf.Lerp(min, max, (float)random.NextDouble());

    private void FailInitialization(string message)
    {
        Debug.LogError($"[NPC 이동] {message}", this);
        enabled = false;
    }

    private bool MoveToPosition(
     Vector2 destination,
     float speed,
     float deltaTime,
     bool allowDetour = true)
    {
        destination.x = Mathf.Clamp(
            destination.x, mapMinimumX, mapMaximumX);

        destination.y = Mathf.Clamp(
            destination.y, mapMinimumY, mapMaximumY);

        Vector2 currentPosition = transform.position;
        Vector2 movementTarget = destination;

        CampWallNavigation navigation = CampWallNavigation.Instance;

        Vector2 wallExit = currentPosition;
        if (navigation != null && navigation.TryGetAllyWallExit(currentPosition, out wallExit))
        {
            movementTarget = wallExit;
            hasWallPathDestination = false;
            wallPath.Clear();
        }
        else if (navigation != null && allowDetour)
        {
            bool destinationChanged =
                !hasWallPathDestination ||
                (destination - wallPathDestination).sqrMagnitude > 0.01f;

            if (destinationChanged || Time.time >= nextWallPathTime)
            {
                wallPathDestination = destination;
                hasWallPathDestination = true;
                nextWallPathTime = Time.time + 0.5f;

                navigation.TryFindPath(
                    currentPosition,
                    destination,
                    navigation.AllyRadius,
                    wallPath);

                wallPathIndex = 1;
            }

            // 중간 경유점은 가까워지면 넘기되 마지막 목적지는 남겨 둔다.
            // 작업 도착 오차 안으로 이동을 마쳐야 건설·수리가 시작된다.
            while (wallPathIndex < wallPath.Count - 1 &&
                   Vector2.Distance(
                       currentPosition,
                       wallPath[wallPathIndex]) <= 0.01f)
            {
                wallPathIndex++;
            }

            if (wallPathIndex < wallPath.Count)
            {
                movementTarget = wallPath[wallPathIndex];
            }
            else
            {
                // 통행 가능한 경로가 없으면 벽을 향해 계속 이동하지 않는다.
                movementTarget = currentPosition;
            }
        }
        else
        {
            hasWallPathDestination = false;
            wallPath.Clear();
        }

        float differenceX = movementTarget.x - currentPosition.x;

        if (Mathf.Abs(differenceX) > 0.001f)
        {
            visual.flipX = differenceX < 0.0f;
        }

        Vector2 nextPosition = Vector2.MoveTowards(
            currentPosition,
            movementTarget,
            Mathf.Max(0.0f, speed) * deltaTime);

        if (navigation != null)
        {
            nextPosition = navigation.MoveAlly(
                currentPosition,
                nextPosition);
        }

        transform.position = new Vector3(
            nextPosition.x,
            nextPosition.y,
            fixedZ);

        SaveCurrentPosition();

        return (nextPosition - destination).sqrMagnitude
            <= 0.001f * 0.001f;
    }

    private bool MoveToX(
        float destinationX,
        float speed,
        float deltaTime)
    {
        // 기존 좌우 배회는 현재 Y 위치를 유지한다.
        Vector2 destination = new Vector2(
            destinationX,
            transform.position.y);

        return MoveToPosition(destination, speed, deltaTime, false);
    }

    private bool TryGetWorkPosition(
      int slotId,
      out Vector2 workPosition)
    {
        workPosition = Vector2.zero;

        if (!CampConstructionSlotView.TryGetActiveSlot(
                slotId,
                out CampConstructionSlotView slot))
        {
            return false;
        }

        int workerIndex =
            RunCampNpcs.GetConstructionWorkerIndex(npcId);

        if (!RunCampNpcs.TryGetConstructionWorkplacePosition(
                slot.transform.position, workerIndex,
                RunCampConstruction.IsCompleted(slotId)
                    ? slot.FootprintSize.x * 0.5f + Mathf.Max(boundaryPadding, 0.3f) + 0.1f
                    : workOffsetX,
                Rect.MinMaxRect(mapMinimumX, mapMinimumY, mapMaximumX, mapMaximumY),
                out workPosition))
        {
            FailInitialization(
                $"건설 자리 {slotId}에 사용 가능한 작업 공간이 없습니다.");

            return false;
        }

        return true;
    }

    private void FollowPlayer(
       int followerIndex,
       float deltaTime)
    {
        if (observedFollowerIndex != followerIndex)
        {
            observedFollowerIndex = followerIndex;
            currentFollowSpeed = 0.0f;
            isFollowingPlayer = true;
        }

        float spacing = Mathf.Max(0.1f, followSpacing);

        Vector2 destination =
            (Vector2)playerMovement.transform.position -
            playerMovement.FacingDirection2D *
            spacing * (followerIndex + 1);

        destination.x = Mathf.Clamp(
            destination.x, mapMinimumX, mapMaximumX);

        destination.y = Mathf.Clamp(
            destination.y, mapMinimumY, mapMaximumY);

        float remaining = Vector2.Distance(
            transform.position,
            destination);

        if (remaining <= 0.03f)
        {
            currentFollowSpeed = 0.0f;
            isFollowingPlayer = false;
            return;
        }

        if (!isFollowingPlayer &&
            remaining <= Mathf.Max(0.01f, followRestartDistance))
        {
            return;
        }

        isFollowingPlayer = true;

        float maximumSpeed = Mathf.Max(
            playerMovement.WalkSpeed,
            playerMovement.CurrentMoveSpeed) +
            Mathf.Max(0.0f, followCatchUpSpeed);

        float desiredSpeed = Mathf.Min(
            maximumSpeed,
            remaining * Mathf.Max(0.01f, followSlowdown));

        currentFollowSpeed = Mathf.MoveTowards(
            currentFollowSpeed,
            desiredSpeed,
            Mathf.Max(0.01f, followAcceleration) * deltaTime);

        MoveToPosition(destination, currentFollowSpeed, deltaTime);
    }
}
