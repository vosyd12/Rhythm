using System.Collections.Generic;
using UnityEngine;

public enum CampNpcProfession
{
    Unemployed = 0,
    Engineer = 1,
    Ranged = 2
}

// 런 데이터만 보관한다. 씬 오브젝트와 참조를 섞지 않는다.
public sealed class CampNpcData
{
    public int Id { get; }
    public CampNpcProfession Profession { get; internal set; }
    public int AssignedConstructionSlotId { get; internal set; }

    // 실제 작업 위치에 도착한 건물 ID. 이동 중이거나 작업하지 않으면 0.
    public int WorkingConstructionSlotId { get; internal set; }
    public long ConstructionAssignmentOrder { get; internal set; }
    public int ConstructionWorkplaceIndex { get; internal set; } = -1;
    public bool IsFollowing { get; internal set; }
    public bool IsInCampScene { get; internal set; }
    public bool HasSavedPosition { get; internal set; }
    public Vector3 SavedPosition { get; internal set; }
    public bool SavedFlipX { get; internal set; }
    public bool HasCampPosition { get; internal set; }
    public Vector3 CampPosition { get; internal set; }
    public float IdleCenterX { get; internal set; }
    public bool IsReturningToCamp { get; internal set; }

    public string Name { get; internal set; }

    public float EngineerBuildMultiplier { get; internal set; } = 1.5f;

    public int MaximumHp { get; internal set; } = 10;
    public int CurrentHp { get; internal set; } = 10;
    public int MaximumDefense { get; internal set; } = 3;
    public int CurrentDefense { get; internal set; } = 3;
    public bool IsAlive => CurrentHp > 0;
    public float LastHitTime { get; internal set; } = float.NegativeInfinity;
    public bool IsHoldingAfterCollapse { get; internal set; }
    public bool HasPendingTowerExit { get; internal set; }
    public Rect CollapsedTowerFootprint { get; internal set; }

    public int Level { get; internal set; } = 1;
    public int Experience { get; internal set; }
    public int Strength { get; internal set; }
    public int Vitality { get; internal set; }
    public int Agility { get; internal set; }
    public int Tenacity { get; internal set; }
    public float GrowthAttackRate { get; internal set; } = 1f;
    public bool IsRecruited { get; internal set; }
    public int RecruitTemplateId { get; internal set; }
    public int BaseAttackDamage { get; internal set; } = -1;
    public float BaseAttackRange { get; internal set; } = -1f;
    public float BaseAttackRate { get; internal set; } = -1f;
    public bool HasAdvanceOrder { get; internal set; }
    public Vector3 AdvanceDestination { get; internal set; }

    public bool CanBuild => IsAlive &&
        (Profession == CampNpcProfession.Unemployed ||
         Profession == CampNpcProfession.Engineer);

    public int AssignedDefensePostId { get; internal set; }
    public int EnteredDefensePostId { get; internal set; }
    public int DefenseSeatIndex { get; internal set; } = -1;
    public long DefenseAssignmentOrder { get; internal set; }
    public float NextDefenseAttackTime { get; internal set; }
    public RangedWeaponData DefenseWeapon { get; internal set; }

    internal CampNpcData(int id, CampNpcProfession profession)
    {
        Id = id;
        Profession = profession;
        Name = $"NPC {id}";
    }
}

public static partial class RunCampNpcs
{
    private static readonly Dictionary<int, CampNpcData> npcs =
        new Dictionary<int, CampNpcData>();

    private static readonly List<int> followingNpcIds = new List<int>();
    private static long nextAssignmentOrder;

    public static int Count => npcs.Count;
    public static int FollowingCount => followingNpcIds.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize() { ResetForNewRun(); }

    public static IEnumerable<CampNpcData> All => npcs.Values;

    public static void ResetForNewRun()
    {
        npcs.Clear();
        followingNpcIds.Clear();
        nextAssignmentOrder = 0;
    }

    public static CampNpcData RegisterNpc(
      int id,
      CampNpcProfession profession,
      string npcName = "",
      float engineerBuildMultiplier = 1.5f,
      RangedWeaponData defenseWeapon = null,
      int maximumHp = 10, int maximumDefense = 3)
    {
        if (id < 1)
        {
            return null;
        }

        if (!npcs.TryGetValue(id, out CampNpcData npc))
        {
            npc = new CampNpcData(id, profession);

            npc.Name = M6Balance.PickNpcName($"대원 {id}");

            npc.EngineerBuildMultiplier =
                Mathf.Max(1.0f, engineerBuildMultiplier);
            npc.DefenseWeapon = defenseWeapon;
            npc.MaximumHp = npc.CurrentHp = Mathf.Max(1, maximumHp);
            npc.MaximumDefense = npc.CurrentDefense = Mathf.Max(0, maximumDefense);

            npcs.Add(id, npc);
        }

        // 씬을 다시 방문해도 기존 병과와 배정 상태를 유지한다.
        return npc;
    }

    public static bool TryGetNpc(int id, out CampNpcData npc) =>
        npcs.TryGetValue(id, out npc);

    public static int GetFollowerIndex(int npcId) => followingNpcIds.IndexOf(npcId);

    public static bool TryGetLeadingFollower(out CampNpcData npc)
    {
        npc = null;
        return followingNpcIds.Count > 0 && TryGetNpc(followingNpcIds[0], out npc);
    }

    // 선두를 뒤로 보내 다음 추종자를 실제 명령 대상으로 선택한다.
    public static bool TrySelectNextFollower(out CampNpcData selected)
    {
        selected = null;
        if (followingNpcIds.Count < 2) { return false; }

        int previousLeader = followingNpcIds[0];
        followingNpcIds.RemoveAt(0);
        followingNpcIds.Add(previousLeader);
        return TryGetLeadingFollower(out selected);
    }

    public static bool TryGetNearestIdleNpc(Vector2 position, float range, out CampNpcData nearest)
    {
        nearest = null;
        float nearestDistance = range * range;
        foreach (CampNpcData npc in npcs.Values)
        {
            if (!npc.IsAlive || !npc.IsInCampScene ||
                !npc.HasSavedPosition ||
                npc.IsFollowing ||
                npc.AssignedConstructionSlotId != 0 ||
                npc.AssignedDefensePostId != 0) 
            {
                continue;
            }
            float distance = ((Vector2)npc.SavedPosition - position).sqrMagnitude;
            if (distance > nearestDistance ||
                (nearest != null && distance == nearestDistance && npc.Id > nearest.Id)) { continue; }
            nearest = npc;
            nearestDistance = distance;
        }
        return nearest != null;
    }

    private static void AddFollower(CampNpcData npc)
    {
        if (!npc.IsAlive || npc.IsFollowing) { return; }
        npc.HasAdvanceOrder = false;
        npc.IsHoldingAfterCollapse = false;
        npc.IsReturningToCamp = false;
        npc.IsFollowing = true;
        followingNpcIds.Add(npc.Id); // 다시 호출해도 항상 대열 끝에 합류한다.
    }

    public static bool TryStartFollowing(int npcId)
    {
        if (!TryGetNpc(npcId, out CampNpcData npc) ||
            !npc.IsAlive || !npc.IsInCampScene ||
            npc.IsFollowing ||
            npc.AssignedConstructionSlotId != 0 ||
            npc.AssignedDefensePostId != 0) 
        { 
            return false; 
        }

        AddFollower(npc);
        return true;
    }

    // F 짧게: 대열 맨 앞 한 명을 현재 NPC 위치에 남긴다.
    public static bool TryDismissLeadingFollower(out int npcId)
    {
        npcId = 0;
        if (!TryGetLeadingFollower(out CampNpcData npc)) { return false; }
        followingNpcIds.RemoveAt(0);
        npc.IsFollowing = false;
        SetLocalIdle(npc);
        npcId = npc.Id;
        return true;
    }

    private static void SetLocalIdle(CampNpcData npc)
    {
        npc.IsReturningToCamp = false;
        npc.IdleCenterX = npc.HasSavedPosition ? npc.SavedPosition.x : npc.CampPosition.x;
    }

    // F 길게: 현재 추종자만 캠프로 복귀. 현장 대기자와 작업자는 제외한다.
    public static void ReturnAllFollowersToCamp()
    {
        foreach (int id in followingNpcIds)
        {
            if (!TryGetNpc(id, out CampNpcData npc)) { continue; }
            npc.IsFollowing = false;
            npc.IsReturningToCamp = true;
            npc.IdleCenterX = npc.CampPosition.x;
        }
        followingNpcIds.Clear();
    }

    public static int GetConstructionWorkerCount(int slotId)
    {
        if (slotId < 1) { return 0; }
        int count = 0;
        foreach (CampNpcData npc in npcs.Values)
        {
            if (npc.AssignedConstructionSlotId == slotId) { count++; }
        }
        return count;
    }

    public static int GetConstructionWorkerIndex(int npcId)
    {
        if (!TryGetNpc(npcId, out CampNpcData npc) || npc.AssignedConstructionSlotId == 0) { return -1; }
        return npc.ConstructionWorkplaceIndex;
    }

    private static int FindFreeConstructionWorkplace(int slotId)
    {
        // 회수한 자리는 재사용하되 다른 작업자의 위치를 재정렬하지 않는다.
        for (int index = 0; ; index++)
        {
            bool occupied = false;
            foreach (CampNpcData other in npcs.Values)
            {
                if (other.AssignedConstructionSlotId == slotId &&
                    other.ConstructionWorkplaceIndex == index)
                { occupied = true; break; }
            }
            if (!occupied) return index;
        }
    }

    // 각 건설 자리 양옆으로 작업 공간을 늘린다. 경계 밖 후보는 순서에서 제외한다.
    public static bool TryGetConstructionWorkplacePosition(
        Vector2 center, int index, float offset, Rect bounds, out Vector2 position)
    {
        position = center;
        if (index < 0 || bounds.width <= 0f || bounds.height <= 0f) return false;
        float distance = Mathf.Max(0.1f, Mathf.Abs(offset));
        float spacing = distance * 2f;
        int maximumRows = Mathf.CeilToInt(bounds.height / spacing) * 2 + 1;
        int available = 0;
        for (int row = 0; row <= maximumRows; row++)
        {
            int rowOffset = row == 0 ? 0 : (row + 1) / 2 * (row % 2 == 1 ? 1 : -1);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 candidate = center + new Vector2(side * distance, rowOffset * spacing);
                if (candidate.x < bounds.xMin || candidate.x > bounds.xMax ||
                    candidate.y < bounds.yMin || candidate.y > bounds.yMax) continue;
                if (available++ != index) continue;
                position = candidate;
                return true;
            }
        }
        return false;
    }

    public static bool TryReserveNearestBuilder(
      int slotId,
      Vector2 constructionPosition)
    {
        if (!RunCampConstruction.TryGetConstruction(
                slotId,
                out CampConstructionData construction) ||
            construction.IsCompleted)
        {
            return false;
        }

        // 묶음에 한 명이라도 배정되어 있으면 자동 증원하지 않는다.
        if (RunCampConstruction.GetGroupWorkerCount(slotId) > 0)
        {
            return false;
        }

        CampNpcData nearest = null;
        float nearestDistance = float.PositiveInfinity;

        foreach (CampNpcData npc in npcs.Values)
        {
            if (npc.HasPendingTowerExit || !npc.CanBuild ||
                !npc.IsInCampScene ||
                !npc.HasSavedPosition ||
                npc.IsFollowing ||
                npc.IsReturningToCamp ||
                npc.AssignedConstructionSlotId != 0 ||
                npc.AssignedDefensePostId != 0)
            {
                continue;
            }

            float distance =
                ((Vector2)npc.SavedPosition -
                 constructionPosition).sqrMagnitude;

            if (distance > nearestDistance ||
                (nearest != null &&
                 distance == nearestDistance &&
                 npc.Id > nearest.Id))
            {
                continue;
            }

            nearest = npc;
            nearestDistance = distance;
        }

        if (nearest == null)
        {
            return false;
        }

        // 이동하기 전에 예약을 확정한다.
        // 이후 검색에서는 이 NPC가 다른 건설에 선택되지 않는다.
        nearest.ConstructionWorkplaceIndex = FindFreeConstructionWorkplace(slotId);
        nearest.AssignedConstructionSlotId = slotId;
        nearest.WorkingConstructionSlotId = 0;
        nearest.ConstructionAssignmentOrder = ++nextAssignmentOrder;

        return true;
    }

    public static bool TryAssignLeadingBuilder(int slotId, out string failureReason)
    {
        failureReason = "";
        if (!TryGetLeadingFollower(out CampNpcData npc) || !npc.IsInCampScene)
        { failureReason = "F로 작업할 NPC를 데려와 주세요."; return false; }
        if (!npc.CanBuild)
        { failureReason = "백수나 공병만 건설 작업에 배치할 수 있습니다."; return false; }
        if (npc.HasPendingTowerExit || npc.AssignedConstructionSlotId != 0 || npc.AssignedDefensePostId != 0)
        { failureReason = "기존 배정을 먼저 회수해 주세요."; return false; }
        if (!RunCampConstruction.TryGetWorkSlot(slotId, out int workSlotId))
        { failureReason = "진행 중인 건설 자리 가까이에서 지시해 주세요."; return false; }

        followingNpcIds.RemoveAt(0);
        npc.IsFollowing = false;
        npc.IsReturningToCamp = false;
        npc.ConstructionWorkplaceIndex = FindFreeConstructionWorkplace(workSlotId);
        npc.AssignedConstructionSlotId = workSlotId;
        npc.WorkingConstructionSlotId = 0;
        npc.ConstructionAssignmentOrder = ++nextAssignmentOrder;
        return true;
    }

    // 거리/ID가 아니라 해당 건물에 가장 나중에 배치한 순서로 찾는다.
    // IsInCampScene은 순서 비교에 사용하지 않는다. 씬 이동으로 순서를 바꾸지 않는다.
    public static bool TryGetLastConstructionWorker(int slotId, out CampNpcData worker)
    {
        worker = null;
        if (slotId < 1) { return false; }
        foreach (CampNpcData npc in npcs.Values)
        {
            if (npc.AssignedConstructionSlotId != slotId) { continue; }
            if (worker == null || npc.ConstructionAssignmentOrder > worker.ConstructionAssignmentOrder)
            { worker = npc; }
        }
        return worker != null;
    }

    public static bool TryRecallLastConstructionWorker(int slotId, out int npcId, out string failureReason)
    {
        npcId = 0;
        failureReason = "";
        if (!TryGetLastConstructionWorker(slotId, out CampNpcData npc))
        { failureReason = "이 건물에 배치된 작업자가 없습니다."; return false; }
        if (!npc.IsInCampScene)
        { failureReason = "마지막 작업자의 캠프 오브젝트를 확인해 주세요."; return false; }
        npc.AssignedConstructionSlotId = 0;
        npc.ConstructionWorkplaceIndex = -1;
        npc.WorkingConstructionSlotId = 0;
        npc.ConstructionAssignmentOrder = 0;
        AddFollower(npc);
        npcId = npc.Id;
        return true;
    }

    public static void ReleaseConstructionWorkers(int slotId)
    {
        if (slotId < 1) { return; }
        foreach (CampNpcData npc in npcs.Values)
        {
            if (npc.AssignedConstructionSlotId != slotId) { continue; }
            npc.AssignedConstructionSlotId = 0;
            npc.ConstructionWorkplaceIndex = -1;
            npc.WorkingConstructionSlotId = 0;
            npc.ConstructionAssignmentOrder = 0;
            SetLocalIdle(npc); // 완공은 전체 추종 해제가 아니므로 현장에서 대기한다.
        }
    }

    public static bool TryAssignLeadingDefender(
    int postId,
    out string failureReason)
    {
        failureReason = "";

        if (!CampDefensePost.TryGetActive(postId, out CampDefensePost post))
        {
            failureReason = "방어 위치를 확인해 주세요.";
            return false;
        }

        if (GetDefenseWorkerCount(postId) >= post.Capacity)
        {
            failureReason = "방어 지점의 정원이 가득 찼습니다. 이동 중인 예약 인원도 정원에 포함합니다.";
            return false;
        }

        if (!TryGetLeadingFollower(out CampNpcData npc) ||
            !npc.IsInCampScene)
        {
            failureReason = "F로 원거리 NPC를 데려와 주세요.";
            return false;
        }

        if (npc.Profession != CampNpcProfession.Ranged)
        {
            failureReason = "이 위치에는 원거리 NPC만 배치할 수 있습니다.";
            return false;
        }

        if (npc.HasPendingTowerExit || npc.AssignedConstructionSlotId != 0 ||
            npc.AssignedDefensePostId != 0)
        {
            failureReason = "기존 배정을 먼저 회수해 주세요.";
            return false;
        }

        followingNpcIds.RemoveAt(0);

        npc.IsFollowing = false;
        npc.IsReturningToCamp = false;
        npc.AssignedDefensePostId = postId;
        npc.EnteredDefensePostId = 0;
        npc.DefenseSeatIndex = FindFreeDefenseSeat(postId);
        npc.DefenseAssignmentOrder = ++nextAssignmentOrder;
        // 회수·재입장해도 이전 사격의 쿨다운을 유지한다.

        return true;
    }

    public static bool TryGetLastDefender(
        int postId,
        out CampNpcData defender)
    {
        defender = null;

        foreach (CampNpcData npc in npcs.Values)
        {
            if (npc.AssignedDefensePostId != postId)
            {
                continue;
            }

            if (defender == null ||
                npc.DefenseAssignmentOrder > defender.DefenseAssignmentOrder)
            {
                defender = npc;
            }
        }

        return defender != null;
    }

    public static int GetTowerCapacity(int level) =>
        level >= 1 && level <= 9 ? (level - 1) / 3 + 1 : 0;

    public static int GetDefenseWorkerCount(int postId)
    {
        int count = 0;
        foreach (CampNpcData npc in npcs.Values)
            if (npc.AssignedDefensePostId == postId && postId > 0) count++;
        return count;
    }

    private static int FindFreeDefenseSeat(int postId)
    {
        for (int seat = 0; ; seat++)
        {
            bool used = false;
            foreach (CampNpcData other in npcs.Values)
                if (other.AssignedDefensePostId == postId && other.DefenseSeatIndex == seat)
                { used = true; break; }
            if (!used) return seat;
        }
    }

    // 씬의 Start와 동적 건설 복원이 끝난 뒤 전투 Update에서 호출한다.
    public static void ReconcileDefenseAssignments()
    {
        foreach (CampNpcData npc in npcs.Values)
        {
            if (npc.AssignedDefensePostId == 0 || !npc.IsInCampScene) continue;
            if (CampDefensePost.TryGetActive(npc.AssignedDefensePostId, out _)) continue;
            npc.AssignedDefensePostId = 0;
            npc.EnteredDefensePostId = 0;
            npc.DefenseSeatIndex = -1;
            npc.DefenseAssignmentOrder = 0;
            SetLocalIdle(npc);
        }
    }

    public static bool TryRecallLastDefender(
        int postId,
        out int npcId,
        out string failureReason)
    {
        npcId = 0;
        failureReason = "";

        if (!TryGetLastDefender(postId, out CampNpcData npc))
        {
            failureReason = "배치된 방어 NPC가 없습니다.";
            return false;
        }

        if (!npc.IsInCampScene)
        {
            failureReason = "방어 NPC의 캠프 오브젝트를 확인해 주세요.";
            return false;
        }

        npc.AssignedDefensePostId = 0;
        npc.EnteredDefensePostId = 0;
        npc.DefenseSeatIndex = -1;
        npc.DefenseAssignmentOrder = 0;
        // 회수·재입장해도 이전 사격의 쿨다운을 유지한다.

        AddFollower(npc);
        npcId = npc.Id;

        return true;
    }

    // 공격 직전 방어가 남아 있으면 해당 일반 공격의 초과 피해는 HP로 넘기지 않는다.
    internal static void ApplyDamageToGauges(ref int hp, ref int defense, int damage, bool directHp)
    {
        if (hp <= 0 || damage <= 0) return;
        if (!directHp && defense > 0) defense = Mathf.Max(0, defense - damage);
        else hp = Mathf.Max(0, hp - damage);
    }

    public static bool TryDamageNpc(int id, int damage, bool directHp = false)
    {
        if (damage <= 0 || !TryGetNpc(id, out CampNpcData npc) || !npc.IsAlive) return false;
        // 상주 유닛을 향한 일반 공격은 타워가 먼저 받는다.
        if (!directHp && npc.EnteredDefensePostId != 0 &&
            CampDefensePost.TryGetActive(npc.EnteredDefensePostId, out CampDefensePost post) && post.IsTower)
            return post.TakeDamage(damage);
        int hp = npc.CurrentHp, defense = npc.CurrentDefense;
        ApplyDamageToGauges(ref hp, ref defense, damage, directHp);
        npc.LastHitTime = Time.time;
        npc.CurrentHp = hp;
        npc.CurrentDefense = defense;
        if (!npc.IsAlive)
        {
            followingNpcIds.Remove(id);
            npc.IsFollowing = npc.IsReturningToCamp = npc.IsHoldingAfterCollapse = false;
            npc.HasPendingTowerExit = false;
            npc.AssignedConstructionSlotId = npc.WorkingConstructionSlotId = 0;
            npc.ConstructionWorkplaceIndex = -1;
            npc.ConstructionAssignmentOrder = 0;
            ClearDefenseAssignment(npc);
            // 사망 기록을 남겨 씬 재등록으로 같은 ID가 부활하지 않게 한다.
        }
        return true;
    }

    private static void ClearDefenseAssignment(CampNpcData npc)
    {
        npc.AssignedDefensePostId = npc.EnteredDefensePostId = 0;
        npc.DefenseSeatIndex = -1;
        npc.DefenseAssignmentOrder = 0;
    }

    public static void HandleTowerDestroyed(int postId, Rect footprint)
    {
        if (postId < 1) return;
        foreach (CampNpcData npc in npcs.Values)
        {
            if (npc.AssignedDefensePostId != postId) continue;
            bool entered = npc.EnteredDefensePostId == postId;
            ClearDefenseAssignment(npc); // 피해보다 먼저 해제해 중복 붕괴 처리를 막는다.
            if (!npc.IsAlive) continue;
            if (!entered) { AddFollower(npc); continue; }
            int collapseDamage = (int)(((long)npc.MaximumHp * 3 + 9) / 10);
            TryDamageNpc(npc.Id, collapseDamage, true);
            if (!npc.IsAlive) continue;
            npc.IsHoldingAfterCollapse = true;
            npc.HasPendingTowerExit = true;
            npc.CollapsedTowerFootprint = footprint;
            SetLocalIdle(npc);
        }
    }

    public static float GetConstructionSpeedMultiplier(int slotId)
    {
        float totalMultiplier = 0.0f;
        int workerCount = 0;

        foreach (CampNpcData npc in npcs.Values)
        {
            if (npc.AssignedConstructionSlotId != slotId ||
                npc.WorkingConstructionSlotId != slotId ||
                !npc.IsInCampScene ||
                !npc.CanBuild)
            {
                continue;
            }

            totalMultiplier +=
                npc.Profession == CampNpcProfession.Engineer
                    ? npc.EngineerBuildMultiplier
                    : 1.0f;

            workerCount++;
        }

        return workerCount > 0
            ? totalMultiplier / workerCount
            : 1.0f;
    }

    public static int GetWorkingConstructionWorkerCount(int slotId)
    {
        if (slotId < 1)
        {
            return 0;
        }

        int count = 0;

        foreach (CampNpcData npc in npcs.Values)
        {
            if (npc.AssignedConstructionSlotId == slotId &&
                npc.WorkingConstructionSlotId == slotId &&
                npc.IsInCampScene &&
                npc.CanBuild)
            {
                count++;
            }
        }

        return count;
    }
}
