using System.Collections.Generic;
using UnityEngine;

// 건설 한 건의 작업 설정과 진행 상태.
// 씬 오브젝트는 보관하지 않는다.
public sealed class CampConstructionData
{
    public int SlotId { get; }
    public int TotalWork { get; }

    public int WorkPerAPWithOne { get; }

    internal CampConstructionGroup Group { get; }
    public float CompletedWork => Progress01 * TotalWork;
    public int GroupSize => Group.Members.Count;

    // 백수 한 명이 도착한 뒤의 기준 시간. 추가 건물은 한 칸마다 2초를 더한다.
    public float GetGroupBuildSeconds(float workStepSeconds)
    {
        CampConstructionData first = Group.Members[0];
        return (float)first.TotalWork / first.WorkPerAPWithOne * workStepSeconds +
            (GroupSize - 1) * RunCampConstruction.AdditionalBuildingSeconds;
    }

    // 완성된 벽에만 사용한다.
    // 0은 아직 벽 체력을 초기화하지 않았다는 뜻이다.
    // 파괴되면 이 건설 데이터 자체를 제거한다.
    public int RemainingWallHp { get; internal set; }
    public int MaximumWallHp { get; internal set; }
    internal float RepairSeconds;
    // 0은 첫 방어 지점 연결 때 초기 레벨을 적용한다는 뜻이다.
    public int TowerLevel { get; internal set; }
    public bool TowerVitalsInitialized { get; internal set; }
    public int MaximumTowerHp { get; internal set; }
    public int RemainingTowerHp { get; internal set; }
    public int MaximumTowerDefense { get; internal set; }
    public int RemainingTowerDefense { get; internal set; }

    public bool IsCompleted =>
        Group.CompletedWork >= Group.TotalWork;

    public float Progress01 =>
        Group.CompletedWork / Group.TotalWork;

    internal CampConstructionData(
        int slotId,
        int totalWork,
        int workPerAPWithOne,
        CampConstructionGroup group)
    {
        SlotId = slotId;
        TotalWork = totalWork;
        WorkPerAPWithOne = workPerAPWithOne;
        Group = group;
    }

    public int GetWorkPerAP(int workerCount)
    {
        return workerCount > 0
            ? WorkPerAPWithOne
            : 0;
    }

    // 현재 인원을 유지했을 때 필요한 추가 AP.
    // -1은 배치된 인원이 없어서 진행할 수 없다는 뜻이다.
    public int GetRemainingAP(int workerCount)
    {
        if (IsCompleted)
        {
            return 0;
        }

        int workPerAP = GetWorkPerAP(workerCount);

        if (workPerAP == 0)
        {
            return -1;
        }

        float remainingWork = Group.TotalWork - Group.CompletedWork;

        return Mathf.CeilToInt(remainingWork / workPerAP);
    }
}

// 같은 드래그의 건물들은 진행 상태를 공유하고, 각 칸의 작업 예약은 유지한다.
internal sealed class CampConstructionGroup
{
    internal readonly List<CampConstructionData> Members =
        new List<CampConstructionData>();
    internal float TotalWork;
    internal float CompletedWork;
}

public static partial class RunCampConstruction
{
    public const float AdditionalBuildingSeconds = 2f;
    private static readonly Dictionary<int, CampConstructionData>
        constructions = new Dictionary<int, CampConstructionData>();

    // 작업자가 부족한 건설을 착공 순서로 확인한다.
    private static readonly List<CampConstructionGroup> constructionOrder =
        new List<CampConstructionGroup>();

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        ResetForNewRun();
    }

    public static bool HasStarted(int slotId)
    {
        return constructions.ContainsKey(slotId);
    }

    public static bool TryGetConstruction(
        int slotId,
        out CampConstructionData construction)
    {
        return constructions.TryGetValue(
            slotId, out construction);
    }

    public static bool IsCompleted(int slotId)
    {
        return TryGetConstruction(slotId, out var construction) &&
               construction.IsCompleted;
    }

    public static bool TryStart(
       int slotId,
       int woodCost,
       int totalWork,
       int workPerAPWithOne,
       int groupSlotId = 0)
    {
        if (slotId < 1 ||
            woodCost < 1 ||
            totalWork < 1 ||
            workPerAPWithOne < 1)
        {
            Debug.LogError(
                "건설 ID, 비용, 작업량과 작업 속도를 확인해 주세요.");

            return false;
        }

        if (HasStarted(slotId))
        {
            return false;
        }

        CampConstructionGroup group = null;
        if (groupSlotId != 0)
        {
            // 드래그 확정 중에만 확장한다. 진행 중/완공 묶음에는 합치지 않는다.
            if (!TryGetConstruction(groupSlotId, out CampConstructionData member) ||
                member.IsCompleted || member.Group.CompletedWork > 0f)
                return false;
            group = member.Group;
        }

        if (!RunResources.TrySpendWood(woodCost))
        {
            return false;
        }

        if (group == null)
        {
            group = new CampConstructionGroup();
            constructionOrder.Add(group);
        }
        var construction = new CampConstructionData(
            slotId, totalWork, workPerAPWithOne, group);
        group.Members.Add(construction);
        group.TotalWork += totalWork;
        constructions.Add(slotId, construction);

        // 새 건설을 포함한 대기 자리의 예약을 처리한다.
        ReserveAvailableBuilders();

        return true;
    }

    public static void AdvanceWork(int spentActionPoints)
    {
        // 기존 AP 소비 호출은 잠시 유지하지만,
        // AP로 건설 작업량을 증가시키지는 않는다.
    }

    public static void AdvanceRealtimeWork(
     float deltaTime,
     float workStepSeconds)
    {
        if (!(deltaTime > 0.0f) || float.IsInfinity(deltaTime) ||
            !(workStepSeconds > 0.0f) || float.IsInfinity(workStepSeconds))
        {
            return;
        }

        ReserveAvailableBuilders();

        AdvanceRepairs(deltaTime);

        foreach (CampConstructionGroup group in constructionOrder)
        {
            if (group.CompletedWork >= group.TotalWork)
            {
                continue;
            }

            float workPerStep = 0f;
            foreach (CampConstructionData construction in group.Members)
            {
                int workerCount = RunCampNpcs.GetWorkingConstructionWorkerCount(
                    construction.SlotId);
                if (workerCount == 0) continue;
                workPerStep += construction.GetWorkPerAP(workerCount) * workerCount *
                    RunCampNpcs.GetConstructionSpeedMultiplier(construction.SlotId);
            }

            CampConstructionData first = group.Members[0];
            float referenceRate = first.WorkPerAPWithOne;
            float duration = first.GetGroupBuildSeconds(workStepSeconds);
            group.CompletedWork = Mathf.Min(group.TotalWork,
                group.CompletedWork + group.TotalWork *
                (workPerStep / referenceRate) / duration * deltaTime);

            if (group.CompletedWork >= group.TotalWork)
            {
                // 공유 상태를 먼저 확정한 뒤 모든 칸의 작업자를 함께 해제한다.
                foreach (CampConstructionData construction in group.Members)
                    RunCampNpcs.ReleaseConstructionWorkers(construction.SlotId);
            }
        }
    }

    public static void ResetForNewRun()
    {
        constructions.Clear();
        constructionOrder.Clear();
        RunCampPlacedBuildings.ResetForNewRun();
    }

    private static void ReserveAvailableBuilders()
    {
        foreach (CampConstructionGroup group in constructionOrder)
        {
            if (group.CompletedWork >= group.TotalWork ||
                GetGroupWorkerCount(group.Members[0].SlotId) > 0) continue;

            foreach (CampConstructionData construction in group.Members)
            {
                if (CampConstructionSlotView.TryGetActiveSlot(
                        construction.SlotId, out CampConstructionSlotView slot) &&
                    RunCampNpcs.TryReserveNearestBuilder(
                        construction.SlotId, slot.transform.position)) break;
            }
        }
    }

    public static int GetGroupWorkerCount(int slotId)
    {
        if (!TryGetConstruction(slotId, out CampConstructionData construction)) return 0;
        int count = 0;
        foreach (CampConstructionData member in construction.Group.Members)
            count += RunCampNpcs.GetConstructionWorkerCount(member.SlotId);
        return count;
    }

    public static bool TryGetWorkSlot(int slotId, out int workSlotId)
    {
        workSlotId = 0;
        if (!TryGetConstruction(slotId, out CampConstructionData construction) ||
            (construction.IsCompleted && !NeedsRepair(slotId))) return false;
        if (CampConstructionSlotView.TryGetActiveSlot(slotId, out _))
        {
            workSlotId = slotId;
            return true;
        }
        return false;
    }

    public static bool TryGetLastGroupWorker(int slotId, out CampNpcData worker)
    {
        worker = null;
        if (!TryGetConstruction(slotId, out CampConstructionData construction)) return false;
        if (construction.IsCompleted) return RunCampNpcs.TryGetLastConstructionWorker(slotId, out worker);
        foreach (CampConstructionData member in construction.Group.Members)
        {
            if (RunCampNpcs.TryGetLastConstructionWorker(member.SlotId, out CampNpcData candidate) &&
                (worker == null || candidate.ConstructionAssignmentOrder > worker.ConstructionAssignmentOrder))
                worker = candidate;
        }
        return worker != null;
    }

    public static bool NeedsRepair(int slotId)
    {
        if (!TryGetConstruction(slotId, out CampConstructionData data) || !data.IsCompleted) return false;
        return data.TowerVitalsInitialized
            ? data.RemainingTowerHp > 0 && (data.RemainingTowerHp < data.MaximumTowerHp ||
                data.RemainingTowerDefense < data.MaximumTowerDefense)
            : data.RemainingWallHp > 0 && data.RemainingWallHp < data.MaximumWallHp;
    }

    private static void AdvanceRepairs(float deltaTime)
    {
        foreach (CampConstructionData data in constructions.Values)
        {
            if (!data.IsCompleted) continue;
            int count = RunCampNpcs.GetWorkingConstructionWorkerCount(data.SlotId);
            if (!NeedsRepair(data.SlotId))
            {
                data.RepairSeconds = 0f;
                RunCampNpcs.ReleaseConstructionWorkers(data.SlotId);
                continue;
            }
            // 수리는 2초 간격을 유지하고 추가 작업자로 자원 차감이 중복되지 않게 한다.
            if (count == 0 || RunResources.StoredWood < 1) { data.RepairSeconds = 0f; continue; }
            data.RepairSeconds += deltaTime;
            while (data.RepairSeconds >= 2f && NeedsRepair(data.SlotId))
            {
                if (!RunResources.TrySpendWood(1)) { data.RepairSeconds = 0f; break; }
                data.RepairSeconds -= 2f;
                if (data.TowerVitalsInitialized)
                {
                    data.RemainingTowerHp += Mathf.Min(data.MaximumTowerHp - data.RemainingTowerHp,
                        Mathf.CeilToInt(data.MaximumTowerHp * 0.2f));
                    data.RemainingTowerDefense += Mathf.Min(data.MaximumTowerDefense - data.RemainingTowerDefense,
                        Mathf.CeilToInt(data.MaximumTowerDefense * 0.2f));
                }
                else data.RemainingWallHp += Mathf.Min(data.MaximumWallHp - data.RemainingWallHp,
                    Mathf.CeilToInt(data.MaximumWallHp * 0.2f));
            }
            if (!NeedsRepair(data.SlotId))
            {
                data.RepairSeconds = 0f;
                RunCampNpcs.ReleaseConstructionWorkers(data.SlotId);
            }
        }
    }

    public static bool EnsureTowerVitals(int slotId, int hp, int defense)
    {
        if (!TryGetConstruction(slotId, out CampConstructionData data) || !data.IsCompleted) return false;
        if (!data.TowerVitalsInitialized)
        {
            data.MaximumTowerHp = data.RemainingTowerHp = Mathf.Max(1, hp);
            data.MaximumTowerDefense = data.RemainingTowerDefense = Mathf.Max(0, defense);
            data.TowerVitalsInitialized = true;
        }
        return true;
    }

    public static bool TryDamageTower(int slotId, int damage, bool directHp = false)
    {
        if (damage <= 0 || !TryGetConstruction(slotId, out CampConstructionData data) ||
            !data.IsCompleted || !data.TowerVitalsInitialized || data.RemainingTowerHp <= 0) return false;
        int hp = data.RemainingTowerHp, defense = data.RemainingTowerDefense;
        RunCampNpcs.ApplyDamageToGauges(ref hp, ref defense, damage, directHp);
        data.RemainingTowerHp = hp;
        data.RemainingTowerDefense = defense;
        if (hp == 0) RemoveDestroyedWall(slotId);
        return true;
    }

    public static bool RemoveDestroyedWall(int slotId)
    {
        if (!constructions.TryGetValue(
                slotId,
                out CampConstructionData construction) ||
            !construction.IsCompleted)
        {
            return false;
        }

        RunCampNpcs.ReleaseConstructionWorkers(slotId);
        constructions.Remove(slotId);
        CampDefensePost.NotifyConstructionDestroyed(slotId);
        construction.Group.Members.Remove(construction);
        if (construction.Group.Members.Count == 0)
            constructionOrder.Remove(construction.Group);

        return true;
    }
}
