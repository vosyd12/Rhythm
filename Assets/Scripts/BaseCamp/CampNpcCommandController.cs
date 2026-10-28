using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class CampNpcCommandController : MonoBehaviour
{
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private NightDefenseTester nightDefenseTester;
    [SerializeField] private TMP_Text promptText;
    [SerializeField, Min(0.1f)] private float callRange = 1.5f;

    public string ContextMessage => CanCommand() && promptText != null ? promptText.text : "";
    private bool isInitialized;
    private string commandFeedback = "";
    private float commandFeedbackUntil;

    private void Awake()
    {
        if (playerMovement == null || nightDefenseTester == null || promptText == null)
        { Debug.LogError("플레이어, 밤 전투 시스템과 NPC 명령 안내를 연결해 주세요.", this); enabled = false; return; }
        if (!(callRange > 0.0f) || float.IsInfinity(callRange))
        { Debug.LogError("NPC 호출 거리를 확인해 주세요.", this); enabled = false; return; }
        promptText.raycastTarget = false;
        promptText.enabled = false;
        isInitialized = true;
    }

    private void OnEnable()
    {
        if (!isInitialized) { return; }
        playerMovement.NpcFollowRequested += HandleFollowRequested;
        playerMovement.NpcFollowReleaseRequested += HandleReleaseRequested;
        playerMovement.NpcSelectionRequested += HandleSelectionRequested;
        playerMovement.NpcWorkRequested += HandleWorkRequested;
        playerMovement.NpcWorkContextRequested += HasWorkContext;
        playerMovement.NpcRecallRequested += HandleRecallRequested;
    }

    private void OnDisable()
    {
        if (playerMovement != null)
        {
            playerMovement.NpcFollowRequested -= HandleFollowRequested;
            playerMovement.NpcFollowReleaseRequested -= HandleReleaseRequested;
            playerMovement.NpcSelectionRequested -= HandleSelectionRequested;
            playerMovement.NpcWorkRequested -= HandleWorkRequested;
            playerMovement.NpcWorkContextRequested -= HasWorkContext;
            playerMovement.NpcRecallRequested -= HandleRecallRequested;
        }
        commandFeedback = "";
        if (promptText != null) { promptText.enabled = false; }
    }

    private bool CanCommand() =>
        !PlayerCombatStats.ActionsBlocked && !CampWindowController.WorldInputBlocked && !CampWallPlacementController.IsPlacing &&
        isInitialized &&
        isActiveAndEnabled &&
        playerMovement.isActiveAndEnabled &&
        nightDefenseTester.isActiveAndEnabled &&
        nightDefenseTester.IsInitialized &&
        !nightDefenseTester.IsChangingScene &&
        (nightDefenseTester.IsPreparing ||
         nightDefenseTester.IsRunning);

    private CampConstructionSlotView FindNearestConstructionSlot() =>
        CampConstructionSlotView.FindNearestCommandSlot(playerMovement.transform.position);

    private bool HasWorkContext() =>
        CanCommand() &&
        (RunCampNpcs.FollowingCount > 0 ||
         FindNearestConstructionSlot() != null ||
         CampDefensePost.FindNearest(
             playerMovement.transform.position) != null);

    private bool TryFindCallTarget(out CampNpcData npc) =>
        RunCampNpcs.TryGetNearestIdleNpc(playerMovement.transform.position, callRange, out npc);

    private void HandleFollowRequested()
    {
        if (!CanCommand()) { return; }

        // 기존 대열을 유지한 채 새 NPC를 합류시킬 수 있어야 한다.
        if (TryFindCallTarget(out CampNpcData npc))
        {
            if (RunCampNpcs.TryStartFollowing(npc.Id))
            { 
                ShowCommandFeedback($"{npc.Name} 대열 맨 뒤 합류"); 
            }

            return;
        }

        if (RunCampNpcs.TryDismissLeadingFollower(out int dismissedId))
        { 
            ShowCommandFeedback($"{GetNpcName(dismissedId)} 현재 위치 주변에서 대기"); 
        }
    }

    private void HandleSelectionRequested()
    {
        if (!CanCommand()) { return; }
        if (RunCampNpcs.TrySelectNextFollower(out CampNpcData selected))
        {
            ShowCommandFeedback($"지시 대상 : {selected.Name} · HP {selected.CurrentHp}/{selected.MaximumHp} · 방어 {selected.CurrentDefense}/{selected.MaximumDefense}");
        }
    }

    private void HandleReleaseRequested()
    {
        if (!CanCommand()) { return; }
        int count = RunCampNpcs.FollowingCount;
        RunCampNpcs.ReturnAllFollowersToCamp();
        if (count > 0) { ShowCommandFeedback($"추종 NPC {count}명 캠프로 복귀"); }
    }

    private bool HandleWorkRequested()
    {
        if (!CanCommand())
        {
            return false;
        }

        if (!TryFindDefensePost(out CampDefensePost defensePost))
        {
            CampConstructionSlotView target = FindNearestConstructionSlot();
            if (target == null || !RunCampConstruction.HasStarted(target.SlotId))
            {
                ShowCommandFeedback("B 건설창에서 벽 또는 타워를 선택한 뒤 지도에 배치해 주세요.");
                return true;
            }
            RunCampNpcs.TryGetLeadingFollower(out CampNpcData builder);
            if (!RunCampNpcs.TryAssignLeadingBuilder(target.SlotId, out string buildReason))
            {
                ShowCommandFeedback(buildReason);
                return true;
            }
            ShowCommandFeedback($"{builder.Name} → " + (RunCampConstruction.IsCompleted(target.SlotId) ? "수리 배치 · 2초마다 목재 1" : "작업 공간 추가 · 건설 배치"));
            return true;
        }

        RunCampNpcs.TryGetLeadingFollower(
            out CampNpcData defender);

        if (!RunCampNpcs.TryAssignLeadingDefender(
                defensePost.PostId,
                out string reason))
        {
            ShowCommandFeedback(reason);
            return true;
        }

        ShowCommandFeedback(
            $"{defender.Name} → {defensePost.PostName} 입장 예약" +
            (defensePost.IsTower ? $" · {defensePost.ReservedCount}/{defensePost.Capacity}명" : ""));

        return true;
    }

    private void HandleRecallRequested()
    {
        if (!CanCommand())
        {
            return;
        }

        if (TryFindDefensePost(out CampDefensePost defensePost))
        {
            if (!RunCampNpcs.TryRecallLastDefender(
                    defensePost.PostId,
                    out int defenderId,
                    out string defenseReason))
            {
                ShowCommandFeedback(defenseReason);
                return;
            }

            ShowCommandFeedback(
                $"{GetNpcName(defenderId)} 방어 회수 · 대열 맨 뒤 합류");

            return;
        }

        CampConstructionSlotView target =
            FindNearestConstructionSlot();

        if (target == null)
        {
            ShowCommandFeedback(
                "회수할 작업이 있는 건물 가까이에서 지시해 주세요.");

            return;
        }

        if (!RunCampConstruction.TryGetLastGroupWorker(target.SlotId, out CampNpcData lastWorker))
        {
            ShowCommandFeedback("이 건설 묶음에 배치된 작업자가 없습니다.");
            return;
        }
        if (!RunCampNpcs.TryRecallLastConstructionWorker(
                lastWorker.AssignedConstructionSlotId,
                out int id,
                out string reason))
        {
            ShowCommandFeedback(reason);
            return;
        }

        ShowCommandFeedback(
            $"{GetNpcName(id)} 작업 회수 · 대열 맨 뒤 합류");
    }

    private void LateUpdate()
    {
        if (!CanCommand())
        {
            promptText.enabled = false;
            return;
        }

        int count = RunCampNpcs.FollowingCount;
        string message = "";

        if (TryFindCallTarget(out CampNpcData callTarget))
        {
            message = $"F : {callTarget.Name} 호출";
        }
        else if (RunCampNpcs.TryGetLeadingFollower(
                     out CampNpcData first))
        {
            message = $"F : {first.Name} 현재 위치에 대기";
        }

        if (count > 0)
        {
            AppendLine(
                ref message,
                $"추종 {count}명 · Tab : 선두 선택 · F 길게 : 전원 캠프 복귀");

            if (RunCampNpcs.TryGetLeadingFollower(
                    out CampNpcData selected))
            {
                AppendLine(
                    ref message,
                    $"지시 대상 : {selected.Name} · HP {selected.CurrentHp}/{selected.MaximumHp} · 방어 {selected.CurrentDefense}/{selected.MaximumDefense}");
            }
        }

        CampConstructionSlotView target =
            FindNearestConstructionSlot();

        TryFindDefensePost(out CampDefensePost defensePost);

        // 같은 거리 판정을 사용해 실제 배치 대상과 안내를 일치시킨다.
        if (defensePost != null && count > 0)
        {
            AppendLine(
                ref message,
                $"Space 짧게 : {defensePost.PostName} 방어 배치");
        }

        else if (target != null && count > 0 &&
                 RunCampConstruction.HasStarted(target.SlotId) &&
                 (!RunCampConstruction.IsCompleted(target.SlotId) || RunCampConstruction.NeedsRepair(target.SlotId)))
        {
            AppendLine(ref message, RunCampConstruction.IsCompleted(target.SlotId) ? "Space 짧게 : 수리 배치 · 2초/목재 1 · HP/방어 20%" : "Space 짧게 : 작업 공간 추가 · 건설 배치");
        }

        // 긴 Space는 기존 거리 기준으로 회수 대상을 선택한다.
        if (defensePost != null && defensePost.IsTower)
        {
            AppendLine(ref message,
                $"타워 레벨 {defensePost.TowerLevel} · HP {defensePost.CurrentHp} · 방어 {defensePost.CurrentDefense} · 입장 {defensePost.EnteredCount}/{defensePost.Capacity}명 · 이동 예약 {Mathf.Max(0, defensePost.ReservedCount - defensePost.EnteredCount)}명");
        }
        CampDefensePost upgradeTower = nightDefenseTester.NearestUpgradeTower;
        if (upgradeTower != null)
            AppendLine(ref message, upgradeTower.TowerLevel >= M6Balance.Snapshot().towerMaximum
                ? "타워 최대 레벨"
                : $"E → 성장·장비 : 타워 Lv.{upgradeTower.TowerLevel} → {upgradeTower.TowerLevel + 1} · {upgradeTower.TowerLevel * M6Balance.Snapshot().towerWoodPerLevel}목재");
        if (TryFindDefensePost(out CampDefensePost recallPost))
        {
            if (RunCampNpcs.TryGetLastDefender(
                    recallPost.PostId,
                    out CampNpcData defender))
            {
                AppendLine(
                    ref message,
                    $"Space 길게 : {defender.Name} 방어 회수");
            }
        }

        else if (target != null)
        {

            if (RunCampConstruction.TryGetLastGroupWorker(
                    target.SlotId,
                    out CampNpcData worker))
            {
                AppendLine(
                    ref message,
                    $"Space 길게 : {worker.Name} 작업 회수");
            }
        }

        if (Time.unscaledTime < commandFeedbackUntil &&
            commandFeedback.Length > 0)
        {
            message = commandFeedback +
                (message.Length > 0 ? "\n" + message : "");
        }

        promptText.enabled = !NightScreenUI.HasContextPanel && message.Length > 0;

        if (promptText.text != message)
        {
            promptText.text = message;
        }
    }

    private static void AppendLine(ref string text, string line)
    {
        text += text.Length > 0 ? "\n" + line : line;
    }

    private void ShowCommandFeedback(string message)
    {
        commandFeedback = message;
        commandFeedbackUntil = Time.unscaledTime + 1.5f;
    }

    private bool TryFindDefensePost(out CampDefensePost post)
    {
        Vector2 playerPosition = playerMovement.transform.position;

        post = CampDefensePost.FindNearest(playerPosition);

        if (post == null)
        {
            return false;
        }

        // 원거리 선두는 건설/수리를 할 수 없으므로 유효한 방어 위치를 우선한다.
        if (RunCampNpcs.TryGetLeadingFollower(out CampNpcData rangedLeader) &&
            rangedLeader.Profession == CampNpcProfession.Ranged) return true;

        CampConstructionSlotView construction =
            FindNearestConstructionSlot();

        // 건설 자리와 방어 위치가 모두 가까우면
        // 플레이어에게 더 가까운 대상을 선택한다.
        if (construction != null && RunCampConstruction.IsCompleted(construction.SlotId) &&
            (RunCampConstruction.NeedsRepair(construction.SlotId) ||
             RunCampNpcs.GetConstructionWorkerCount(construction.SlotId) > 0) &&
            (!RunCampNpcs.TryGetLeadingFollower(out CampNpcData leader) || leader.CanBuild))
        {
            post = null;
            return false;
        }
        if (construction != null)
        {
            float constructionDistance =
                ((Vector2)construction.transform.position -
                 playerPosition).sqrMagnitude;

            float defenseDistance =
                (post.Position - playerPosition).sqrMagnitude;

            if (constructionDistance < defenseDistance)
            {
                post = null;
                return false;
            }
        }

        // 같은 위치인 완공 타워에서는 방어 배치를 선택한다.
        return true;
    }

    private static string GetNpcName(int id)
    {
        return RunCampNpcs.TryGetNpc(id, out CampNpcData npc)
            ? npc.Name
            : $"NPC#{id}";
    }
}
