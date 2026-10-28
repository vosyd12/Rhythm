using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

// 밤 전투의 규칙과 진행 상태를 관리한다.
// 이미지 표현은 NightBattleView, 화면 UI는 NightScreenUI가 담당한다.
public partial class NightDefenseTester : MonoBehaviour
{
    public enum NightState
    {
        Preparing,
        Running,
        Victory,
        Defeat,
        Warning,
        NightComplete,
        Escaping,
        Settlement
    }

    [Header("M5 지역 루프 시험값")]
    [SerializeField, Min(1f)] private float dayDurationSeconds = 120f;
    [SerializeField, Min(1f)] private float warningDurationSeconds = 15f;
    [SerializeField, Min(1f)] private float nightDurationSeconds = 90f;
    [SerializeField, Min(1)] private int riftMaximumHp = 30;
    [SerializeField, Min(0.1f)] private float riftDetectionRadius = 6f;
    [SerializeField, Min(0.1f)] private float riftBuildingExclusionRadius = 2f;
    [SerializeField, Range(2f, 3f)] private float escapeDurationSeconds = 2.5f;
    [Tooltip("캠프 중심에서 균열 중심까지 허용하는 최소 거리입니다.")]
    [SerializeField, Min(0f)] private float riftMinimumCampDistance = 30f;
    private const float RiftRadius = 0.75f;
    private float phaseRemaining;
    private bool riftReady, pendingDefeat, resultRecorded, settlementOpened, stageCleared;
    private int riftHp;
    private Vector2 riftPosition;
    private float nextRiftSearchTime;
    private bool riftPlacementBlocked;
    private NightBattleStats completedNightStats;
    public event System.Action<NightBattleStats> SettlementReady;
    public bool IsDay => isInitialized && state == NightState.Preparing;
    public bool IsWarning => isInitialized && state == NightState.Warning;
    public bool IsEscaping => isInitialized && state == NightState.Escaping;
    public bool IsSettlement => isInitialized && state == NightState.Settlement;
    public bool HasRoundResult => isInitialized && (IsVictory || state == NightState.NightComplete || IsSettlement);
    public bool IsStageCleared => stageCleared;
    public float PhaseSecondsRemaining => Mathf.Max(0f, phaseRemaining);
    public int TimelinePhaseIndex => IsDay ? 0 : IsWarning ? 1 : IsRunning ? 2 : 3;
    public float PhaseElapsed01
    {
        get
        {
            float duration = IsDay ? dayDurationSeconds : IsWarning ? warningDurationSeconds : nightDurationSeconds;
            return Mathf.Clamp01(1f - phaseRemaining / Mathf.Max(1f, duration));
        }
    }
    public bool TryGetRiftIndicatorPosition(out Vector3 position)
    {
        position = new Vector3(riftPosition.x, riftPosition.y, baseTarget != null ? baseTarget.position.z : 0f);
        return isInitialized && riftReady && !stageCleared && !isChangingScene && (IsWarning || IsRunning);
    }
    public bool CanAdvancePhase => CanUseCommands && (IsPreparing || IsRunning);
    public bool CanAdvanceResult => isActiveAndEnabled && isInitialized && !isChangingScene && HasRoundResult &&
        (!PlayerCombatStats.ActionsBlocked || stageCleared);
    public string PhaseName => IsDay ? "낮" : IsWarning ? "중립 예고" : IsRunning ? "밤" :
        IsEscaping ? "적 도주" : IsSettlement ? "정산" : IsVictory ? "지역 클리어" :
        state == NightState.NightComplete ? "밤 방어 완료" : "패배";
    public string ResultButtonLabel => IsSettlement ? (stageCleared ? (RunCampaign.Stage == 4 ? "최종 승리" : "동행 선택 · 다음 지역") : "다음 날") : "정산 보기";

    public bool IsRiftRevealed => riftReady && PlayerCombatStats.Instance != null &&
        ((Vector2)PlayerCombatStats.Instance.transform.position - riftPosition).sqrMagnitude <=
        riftDetectionRadius * riftDetectionRadius;

    public string RiftHint
    {
        get
        {
            if (riftPlacementBlocked) return "균열을 배치할 빈 공간이 없습니다.";
            if (!riftReady || IsDay) return "";
            if (stageCleared) return "균열 파괴 · 지역 확보";
            if (HasRoundResult) return "밤 종료 · 다음 밤에 새 균열 출현";
            Vector2 origin = PlayerCombatStats.Instance != null
                ? (Vector2)PlayerCombatStats.Instance.transform.position : (Vector2)baseTarget.position;
            Vector2 offset = riftPosition - origin;
            string eastWest = offset.x > 1f ? "동" : offset.x < -1f ? "서" : "";
            string northSouth = offset.y > 1f ? "북" : offset.y < -1f ? "남" : "";
            string direction = northSouth + eastWest;
            return IsRiftRevealed ? (IsWarning ? "균열 발견 · 밤에 활성화" : "균열 발견 · 공격해 파괴") :
                $"균열 예상 방향: {(direction.Length > 0 ? direction : "주변")}";
        }
    }

    private void AdvanceRegionPhase(float deltaTime)
    {
        if (!(deltaTime > 0f) || float.IsInfinity(deltaTime)) return;
        phaseRemaining = Mathf.Max(0f, phaseRemaining - deltaTime);
        if (IsDay)
        {
            RunProgress.SaveRemainingDay(nightDay, phaseRemaining);
            if (phaseRemaining <= 0f && Time.time >= nextRiftSearchTime) BeginWarning();
        }
        else if (IsWarning && phaseRemaining <= 0f) BeginNight();
        else if (IsEscaping)
        {
            MoveEscapingEnemies(deltaTime);
            if (phaseRemaining <= 0f)
            {
                for (int i = 0; i < Mathf.Min(spawnedCount, enemies.Length); i++) battleView.HideEnemy(i);
                state = NightState.Victory;
            }
        }
    }

    private void OnDisable() { if (IsDay) RunProgress.SaveRemainingDay(nightDay, phaseRemaining); }

    private void LateUpdate()
    {
        // 모든 Update/투사체 판정 뒤에 확정해 같은 프레임의 균열 승리를 우선한다.
        if (!isInitialized || !IsRunning || Time.deltaTime <= 0f || GameExitUI.IsWorldPaused) return;
        if (pendingDefeat || currentBaseHp <= 0) FinishNight(NightState.Defeat);
        else if (phaseRemaining <= 0f) FinishNight(NightState.NightComplete);
    }

    [ContextMenu("테스트/M5 현재 단계 넘기기")]
    public void AdvancePhaseForTest()
    {
        if (!Application.isPlaying || !CanAdvancePhase) return;
        if (IsDay) BeginWarning();
        else if (IsWarning) BeginNight();
        else phaseRemaining = 0f;
    }

    private void BeginWarning()
    {
        if (!IsDay) return;
        ForestGatheringController.Instance?.EndGathering();
        nextRiftSearchTime = Time.time + 1f;
        if (!TryPrepareRift()) { riftPlacementBlocked = true; return; }
        riftPlacementBlocked = false;
        FreezeM6Dda();
        ApplyNightDifficulty();
        EnsureBattleCapacity();
        state = NightState.Warning;
        phaseRemaining = Mathf.Max(1f, warningDurationSeconds);
    }

    private void EnsureBattleCapacity()
    {
        int capacity = Mathf.Min(currentEnemyCount, MaximumActiveEnemies);
        battleView.TryInitialize(capacity);
        int previous = enemies.Length;
        if (capacity <= previous) return;
        System.Array.Resize(ref enemies, capacity);
        for (int i = previous; i < enemies.Length; i++) enemies[i] = new EnemyData();
    }

    private bool IsSafeRiftPosition(Vector2 point, Rect bounds)
    {
        Rect footprint = new Rect(point - Vector2.one * RiftRadius, Vector2.one * (RiftRadius * 2f));
        if (footprint.xMin < bounds.xMin || footprint.xMax > bounds.xMax ||
            footprint.yMin < bounds.yMin || footprint.yMax > bounds.yMax ||
            Vector2.Distance(point, baseTarget.position) < Mathf.Max(riftMinimumCampDistance, riftBuildingExclusionRadius + RiftRadius)) return false;
        foreach (CampPlacementBlocker blocker in CampPlacementBlocker.All)
            if (blocker != null && blocker.Overlaps(footprint)) return false;
        foreach (CampConstructionSlotView slot in CampConstructionSlotView.All)
            if (slot != null && RunCampConstruction.HasStarted(slot.SlotId) && slot.Footprint.Overlaps(footprint)) return false;
        foreach (Collider2D obstacle in Physics2D.OverlapCircleAll(point, RiftRadius))
            if (!obstacle.isTrigger) return false;
        return true;
    }

    private bool TryPrepareRift()
    {
        if (riftReady) return true;
        CampWallNavigation navigation = CampWallNavigation.Instance;
        if (navigation == null) return false;
        Rect bounds = navigation.MapBounds;
        if (bounds.width < RiftRadius * 2f || bounds.height < RiftRadius * 2f) return false;
        Vector2 fallback = Vector2.zero;
        bool hasFallback = false;
        for (int attempt = 0; attempt < 1152; attempt++)
        {
            Vector2 point = attempt < 128
                ? new Vector2(Random.Range(bounds.xMin + RiftRadius, bounds.xMax - RiftRadius),
                    Random.Range(bounds.yMin + RiftRadius, bounds.yMax - RiftRadius))
                : new Vector2(Mathf.Lerp(bounds.xMin + RiftRadius, bounds.xMax - RiftRadius, ((attempt - 128) % 32) / 31f),
                    Mathf.Lerp(bounds.yMin + RiftRadius, bounds.yMax - RiftRadius, ((attempt - 128) / 32) / 31f));
            if (!IsSafeRiftPosition(point, bounds)) continue;
            if (!hasFallback) { fallback = point; hasFallback = true; }
            if (RunProgress.HasPreviousRift && Vector2.Distance(point, RunProgress.PreviousRiftPosition) < 1f) continue;
            fallback = point;
            break;
        }
        if (!hasFallback) return false;
        riftPosition = fallback;
        RunProgress.RememberRift(riftPosition);
        riftHp = Mathf.Max(1, riftMaximumHp);
        riftReady = true;
        return true;
    }

    public bool BlocksRiftConstruction(Rect footprint)
    {
        if (!riftReady || stageCleared) return false;
        Vector2 nearest = new Vector2(Mathf.Clamp(riftPosition.x, footprint.xMin, footprint.xMax),
            Mathf.Clamp(riftPosition.y, footprint.yMin, footprint.yMax));
        return (nearest - riftPosition).sqrMagnitude <= riftBuildingExclusionRadius * riftBuildingExclusionRadius;
    }

    private bool TryDamageRift(int damage)
    {
        if (!IsRunning || !riftReady || riftHp <= 0 || damage <= 0) return false;
        riftHp = Mathf.Max(0, riftHp - damage);
        battleView.ShowRiftHit();
        if (riftHp == 0) FinishNight(NightState.Victory);
        return true;
    }

    private void MoveEscapingEnemies(float deltaTime)
    {
        for (int i = 0; i < Mathf.Min(spawnedCount, enemies.Length); i++)
        {
            EnemyData enemy = enemies[i];
            if (!enemy.IsAlive) continue;
            Vector2 direction = (enemy.Position - (Vector2)baseTarget.position).normalized;
            if (direction.sqrMagnitude < 0.01f) direction = Vector2.right;
            Vector2 desired = enemy.Position + direction * enemyMoveSpeed * deltaTime;
            if (CampWallNavigation.Instance != null)
                desired = CampWallNavigation.Instance.StopBeforeObstacle(enemy.Position, desired,
                    CampWallNavigation.Instance.EnemyRadius, out _, out _);
            enemy.Position = desired;
            battleView.SetEnemyPosition(i, desired);
        }
    }

    private void OpenSettlement()
    {
        if (settlementOpened || !(IsVictory || state == NightState.NightComplete)) return;
        settlementOpened = true;
        state = NightState.Settlement;
        RunEconomy.SettleNight(nightDay, defeatedCount);
        if (stageCleared) { RunCampaign.CompleteStage(); M7CampaignUI.Instance?.ShowCampaign(); }
        else RunCampaign.CompleteNightSettlement();
        SettlementReady?.Invoke(completedNightStats);
    }

#if UNITY_EDITOR
    [ContextMenu("테스트/M5 균열 위치로 지휘관 이동")]
    private void TeleportToRiftForTest()
    {
        if (!Application.isPlaying || !riftReady || PlayerCombatStats.Instance == null) return;
        Vector2 towardCamp = ((Vector2)baseTarget.position - riftPosition).normalized;
        Vector2 destination = riftPosition + towardCamp * 1.5f;
        if (CampWallNavigation.Instance != null)
            destination = CampWallNavigation.Instance.MoveAlly(riftPosition, destination);
        PlayerCombatStats.Instance.transform.position = new Vector3(destination.x, destination.y,
            PlayerCombatStats.Instance.transform.position.z);
    }
    [ContextMenu("테스트/M5 균열 파괴")]
    private void DestroyRiftForTest() { if (Application.isPlaying) TryDamageRift(int.MaxValue); }
#endif


    private sealed class EnemyData
    {
        public Vector2 Position;
        public int Hp, MaximumHp, Defense, MaximumDefense, Damage, Kind;
        public float AttackRange;
        public bool IsElite, IsOrc;
        public bool IsAlive;
        public bool HasReachedBase;
        public float NextAttackTime;

        public readonly List<Vector2> Path = new List<Vector2>();
        public int PathIndex;
        public float NextPathTime;
        public bool BreakThroughWalls;
        public CampWall TargetWall;
        public CampDefensePost TargetTower;
        public int TargetNpcId;
        public bool TargetPlayer;
        public bool IsRetaliating;
        public float LastSuccessfulAttack;
        public Vector2 PathDestination;
    }

    [Header("전투 표시")]

    [SerializeField]
    private NightBattleView battleView;

    [Header("월드 거점과 적 출현")]

    [SerializeField]
    private Transform baseTarget;

    [Tooltip("비균열 테스트의 원형 출현 호환값입니다. M5의 적은 활성 균열에서 출현합니다.")]
    [SerializeField, Min(0.01f)]
    private float spawnMinimumDistance = 12.0f;

    [Tooltip("비균열 테스트의 원형 출현 호환값입니다. M5의 적은 활성 균열에서 출현합니다.")]
    [SerializeField, Min(0.01f)]
    private float spawnMaximumDistance = 16.0f;

    [SerializeField, Min(0.01f)]
    private float enemyStopDistance = 1.0f;

    [Header("적 설정")]

    [SerializeField, Min(1)]
    private int enemyCount = 5;

    [SerializeField, Min(1)]
    private int enemyMaxHp = 3;

    [SerializeField, Min(0.01f)]
    private float enemyMoveSpeed = 100.0f;

    [SerializeField, Min(0.01f)]
    private float spawnIntervalSeconds = 2.0f;

    [SerializeField, Min(0.01f)]
    private float enemyAttackIntervalSeconds = 1.2f;

    [Header("거점 설정")]

    [SerializeField, Min(1)]
    private int maxBaseHp = 5;

    [Tooltip("무기 데이터가 지정되지 않은 원거리 NPC의 기본 사거리입니다. 지정 무기·플레이어 무기는 자체 설정을 사용합니다.")]
    [SerializeField, Min(0.0f)]
    private float attackRange = 360.0f;

    [Tooltip("무기 데이터가 지정되지 않은 원거리 NPC의 기본 공격력입니다. 지정 무기는 해당 무기의 Damage를 사용합니다.")]
    [SerializeField, Min(1)]
    private int attackDamage = 1;

    [SerializeField, Min(0.01f)]
    private float attackIntervalSeconds = 0.7f;

    [Header("다음 날 이동")]

    [SerializeField]
    private string daySceneName = "BaseCamp";

    [SerializeField]
    private bool enableAdaptiveDifficulty = true;

    [Header("벽 공격")]
    [SerializeField, Min(1)]
    private int enemyWallDamage = 1;

    [Header("플레이어 투사체 피격")]
    [SerializeField, Min(0.01f)]
    private float enemyHitRadius = 0.35f;

    // 밤 재시작 시 이전 투사체가 새 적을 공격하지 않도록 구분한다.
    public int BattleVersion { get; private set; }

    public Vector3 BaseWorldPosition => baseTarget.position;

    public float LastBaseHitTime { get; private set; }
        = float.NegativeInfinity;

#if UNITY_EDITOR
    [Header("목재 테스트 (에디터 전용)")]

    [Tooltip("Play 중 이 값을 바꾸면 보유 목재에 즉시 반영합니다. 시작 값은 자동 지급하지 않습니다.")]
    [SerializeField, Min(0)]
    private int debugWoodTarget = 10;
    private int observedEnemyCount, observedEnemyHp, observedBaseMaxHp, observedWood;
    private bool observedAdaptiveDifficulty;
    private int observedEnemyDamage;
    private float observedEnemySpeed;
    private float observedSpawnInterval, observedEnemyAttackInterval, observedAttackInterval;
    private float observedDayDuration, observedWarningDuration, observedNightDuration;
    private int observedRiftMaximumHp;

    private void CaptureTestSettings()
    {
        observedEnemyCount = enemyCount;
        observedEnemyHp = enemyMaxHp;
        observedBaseMaxHp = maxBaseHp;
        observedWood = debugWoodTarget;
        observedAdaptiveDifficulty = enableAdaptiveDifficulty;
        observedEnemyDamage = enemyWallDamage; observedEnemySpeed = enemyMoveSpeed;
        observedSpawnInterval = spawnIntervalSeconds;
        observedEnemyAttackInterval = enemyAttackIntervalSeconds;
        observedAttackInterval = attackIntervalSeconds;
        observedDayDuration = dayDurationSeconds;
        observedWarningDuration = warningDurationSeconds;
        observedNightDuration = nightDurationSeconds;
        observedRiftMaximumHp = riftMaximumHp;
    }

    // OnValidate에서는 Unity 오브젝트를 건드리지 않고 주 스레드 Update에서 반영한다.
    private void RefreshRuntimeTestSettings()
    {
        if (!Application.isPlaying || !isInitialized || isChangingScene) return;
        if ((IsPreparing || IsRunning) && (enemyCount != observedEnemyCount || enemyMaxHp != observedEnemyHp ||
            enableAdaptiveDifficulty != observedAdaptiveDifficulty || enemyWallDamage != observedEnemyDamage))
        {
            int previousMaxHp = currentEnemyMaxHp;
            ApplyNightDifficulty();
            // 이미 생성된 적을 지우거나 처치 수를 되돌리지 않는다.
            currentEnemyCount = Mathf.Max(spawnedCount, currentEnemyCount);
            EnsureBattleCapacity();
            for (int i = 0; i < Mathf.Min(spawnedCount, enemies.Length); i++)
            {
                EnemyData enemy = enemies[i];
                if (!enemy.IsAlive) continue;
                // 받은 피해량을 보존하고 Inspector 조정만으로 부활·처치하지 않는다.
                enemy.Damage = Mathf.Max(1, RunEconomy.ClampInt((long)currentEnemyDamage * (enemy.IsElite ? nightSettings.eliteDamageMultiplier : 1)));
                int multiplier = enemy.IsElite ? nightSettings.eliteHpMultiplier : 1;
                int oldMaximum = enemy.MaximumHp > 0 ? enemy.MaximumHp : previousMaxHp;
                enemy.MaximumHp = Mathf.Max(1, RunEconomy.ClampInt((long)currentEnemyMaxHp * multiplier));
                enemy.Hp = Mathf.Clamp(enemy.MaximumHp - (oldMaximum - enemy.Hp), 1, enemy.MaximumHp);
                ShowM6EnemyVitals(i);
            }
        }
        if (maxBaseHp != observedBaseMaxHp)
        {
            maxBaseHp = Mathf.Max(1, maxBaseHp);
            if (IsPreparing || IsRunning)
            {
                currentBaseHp = Mathf.Clamp(maxBaseHp - (observedBaseMaxHp - currentBaseHp), 1, maxBaseHp);
                RunProgress.SaveBaseHp(currentBaseHp, maxBaseHp);
            }
        }
        if (spawnIntervalSeconds != observedSpawnInterval && IsPositiveFinite(spawnIntervalSeconds))
            nextSpawnTime = Time.time + spawnIntervalSeconds;
        if (enemyAttackIntervalSeconds != observedEnemyAttackInterval && IsPositiveFinite(enemyAttackIntervalSeconds))
            for (int i = 0; i < Mathf.Min(spawnedCount, enemies.Length); i++)
                enemies[i].NextAttackTime = Time.time + enemyAttackIntervalSeconds;
        if (attackIntervalSeconds != observedAttackInterval && IsPositiveFinite(attackIntervalSeconds))
            foreach (CampNpcData npc in RunCampNpcs.All)
                if (npc.DefenseWeapon == null && npc.BaseAttackRate <= 0)
                {
                    bool inTower = npc.EnteredDefensePostId != 0 &&
                        CampDefensePost.TryGetActive(npc.EnteredDefensePostId, out CampDefensePost post) && post.IsTower;
                    npc.NextDefenseAttackTime = Time.time + attackIntervalSeconds /
                        (inTower ? TowerAttackSpeedMultiplier : 1f);
                }
        if (IsDay && dayDurationSeconds != observedDayDuration && IsPositiveFinite(dayDurationSeconds))
            phaseRemaining = Mathf.Max(0f, dayDurationSeconds - (observedDayDuration - phaseRemaining));
        if (IsWarning && warningDurationSeconds != observedWarningDuration && IsPositiveFinite(warningDurationSeconds))
            phaseRemaining = Mathf.Max(0f, warningDurationSeconds - (observedWarningDuration - phaseRemaining));
        if (IsRunning && nightDurationSeconds != observedNightDuration && IsPositiveFinite(nightDurationSeconds))
            phaseRemaining = Mathf.Max(0f, nightDurationSeconds - (observedNightDuration - phaseRemaining));
        if (riftReady && !resultRecorded && riftMaximumHp != observedRiftMaximumHp)
            riftHp = Mathf.Clamp(riftMaximumHp - (observedRiftMaximumHp - riftHp), 1, Mathf.Max(1, riftMaximumHp));
        if (debugWoodTarget != observedWood && debugWoodTarget >= 0) ApplyDebugWood();
        PersistInspectorChanges();
        CaptureTestSettings();
    }

    [ContextMenu("테스트/Inspector 수치 지금 반영")]
    private void ApplyRuntimeTestSettings() => RefreshRuntimeTestSettings();

    [ContextMenu("테스트/강제 게임오버")]
    private void ForceGameOverForTest()
    {
        if (!Application.isPlaying || !CanUseCommands)
        {
            Debug.LogWarning(
                "Play 모드에서 초기화가 완료된 상태로 실행해 주세요.",
                this);

            return;
        }

        // 준비 상태라면 기존 전투 시작 절차를 먼저 실행한다.
        if (state == NightState.Preparing)
        {
            BeginNight();
        }

        if (state != NightState.Running)
        {
            Debug.LogWarning(
                "밤 준비 또는 전투 중에 실행해 주세요.",
                this);

            return;
        }

        currentBaseHp = 0;

        // 실제 패배와 동일한 결과 저장 및 게임오버 UI 경로를 사용한다.
        FinishNight(NightState.Defeat);
    }
#endif

    public const int AttackUpgradeWoodCost = 5;
    public const int AttackUpgradeDamageBonus = 1;

    private const float TowerRangeMultiplier = 1.25f;
    private const float TowerAttackSpeedMultiplier = 1.2f;
    private const int TowerAttackDamageBonus = 2;

    [Header("실시간 건설")]

    [Tooltip("기존 인원별 작업량이 쌓이는 데 걸리는 기준 시간입니다.")]
    [SerializeField, Min(0.1f)]
    private float constructionWorkStepSeconds = 10.0f;

    private EnemyData[] enemies;

    private NightState state;
    private bool isInitialized;
    private bool isChangingScene;

    private int nightDay;
    private int currentEnemyCount;
    private int currentEnemyMaxHp;

    private int currentBaseHp;
    private int spawnedCount;
    private int defeatedCount;

    private int nightEnemyCountAdjustment;
    private string nightDifficultyReason;

    private float battleStartedAt;
    private float nextSpawnTime;

    public bool IsInitialized => isInitialized;
    public bool IsChangingScene => isChangingScene;
    public NightState State => state;

    public bool IsPreparing =>
        isInitialized && (state == NightState.Preparing || state == NightState.Warning);

    public bool IsRunning =>
        isInitialized && state == NightState.Running;

    public bool IsVictory =>
        isInitialized && state == NightState.Victory;

    public bool IsDefeated =>
        isInitialized && state == NightState.Defeat;

    public int NightDay => nightDay;
    public int CurrentBaseHp => currentBaseHp;
    public int MaxBaseHp => maxBaseHp;
    public int TotalEnemyCount => currentEnemyCount;
    public int DefeatedCount => defeatedCount;

    public int CurrentAttackDamage =>
        attackDamage + RunProgress.AttackDamageBonus;

    public int EnemyCountAdjustment => nightEnemyCountAdjustment;
    public string DifficultyReason => nightDifficultyReason;
    public string DaySceneName => daySceneName;

    public bool HasPurchasedAttackUpgrade =>
        RunProgress.HasPurchasedAttackUpgradeToday;

    private bool CanUseCommands =>
        isActiveAndEnabled &&
        isInitialized &&
        !isChangingScene && !PlayerCombatStats.ActionsBlocked;

    public bool CanStartNight =>
        CanUseCommands && IsPreparing;

    public bool CanStartExpedition => CanUseCommands && IsDay;

    public bool CanBuyAttackUpgrade =>
        CanUseEconomy && IsPreparing &&
        !HasPurchasedAttackUpgrade &&
        RunProgress.AttackDamageBonus < M6Balance.Snapshot().attackMaximum && RunResources.StoredGold >= M6Balance.Snapshot().attackCost;

    public bool CanMoveToNextDay =>
        CanAdvanceResult;

    private void Start()
    {
        InitializeM6();
        if (battleView == null ||
            !battleView.isActiveAndEnabled ||
            baseTarget == null)
        {
            FailInitialization(
                "활성 상태의 Night Battle View와 Base Target을 연결해 주세요.");

            return;
        }

        if (enemyCount < 1 ||
            enemyMaxHp < 1 ||
            maxBaseHp < 1 ||
            attackDamage < 1 ||
            !IsPositiveFinite(enemyMoveSpeed) ||
            !IsPositiveFinite(spawnIntervalSeconds) ||
            !IsPositiveFinite(enemyAttackIntervalSeconds) ||
            !IsPositiveFinite(attackIntervalSeconds) ||
            !(attackRange >= 0.0f) ||
            float.IsInfinity(attackRange))
        {
            FailInitialization(
                "적 수, 체력, 공격력, 이동 속도와 공격 간격을 확인해 주세요.");

            return;
        }

        if (!IsPositiveFinite(enemyStopDistance) ||
            !IsPositiveFinite(spawnMinimumDistance) ||
            !IsPositiveFinite(spawnMaximumDistance) ||
            spawnMinimumDistance <= enemyStopDistance ||
            spawnMaximumDistance < spawnMinimumDistance)
        {
            FailInitialization(
                "출현 최소 거리는 적 정지 거리보다 커야 하며, " +
                "최대 거리는 최소 거리 이상이어야 합니다.");

            return;
        }

        ApplyNightDifficulty();

        if (!battleView.TryInitialize(Mathf.Min(currentEnemyCount, MaximumActiveEnemies)))
        {
            FailInitialization(
                "전투 표시기를 초기화하지 못했습니다. 앞의 오류를 확인해 주세요.");

            return;
        }

        enemies = new EnemyData[Mathf.Min(currentEnemyCount, MaximumActiveEnemies)];

        for (int i = 0; i < enemies.Length; i++)
        {
            enemies[i] = new EnemyData();
        }

        isInitialized = true;
        PrepareNight();
        RestoreCampaignOutcome();
        if (GetComponent<M7CampaignUI>() == null) gameObject.AddComponent<M7CampaignUI>();
        if (GetComponent<ForestGatheringController>() == null) gameObject.AddComponent<ForestGatheringController>();
#if UNITY_EDITOR
        CaptureTestSettings();
#endif
    }

    private static bool IsPositiveFinite(float value)
    {
        return value > 0.0f && !float.IsInfinity(value);
    }

    private bool commanderAtCamp;

    private void Update()
    {
        if (!isInitialized)
        {
            return;
        }

#if UNITY_EDITOR
        RefreshRuntimeTestSettings();
#endif
        if (Time.deltaTime <= 0f || GameExitUI.IsWorldPaused) return;
        if (PlayerCombatStats.ActionsBlocked && !commanderAtCamp && PlayerCombatStats.Instance != null)
        {
            PlayerCombatStats.Instance.transform.position = baseTarget.position;
            commanderAtCamp = true;
        }
        if (!PlayerCombatStats.ActionsBlocked) commanderAtCamp = false;
        AdvanceRegionPhase(Time.deltaTime);
        battleView.SetRift(riftReady && (IsWarning || IsRunning) && !stageCleared && IsRiftRevealed, riftPosition, IsWarning,
            1f - (float)riftHp / Mathf.Max(1, riftMaximumHp));
        battleView.UpdateVisualEffects();
        RunCampNpcs.ReconcileDefenseAssignments();

        // 준비 중과 밤 전투 중 모두 건설을 진행한다.
        // 승리·패배 결과 상태에서는 진행하지 않는다.
        if (IsPreparing || IsRunning)
        {
            RunCampConstruction.AdvanceRealtimeWork(
                Time.deltaTime,
                constructionWorkStepSeconds);
        }

        if (state != NightState.Running)
        {
            return;
        }

        if (spawnedCount < currentEnemyCount &&
            Time.time >= nextSpawnTime)
        {
            SpawnEnemy();
        }

        MoveEnemies();
        TryBaseAttack();
        if (IsRunning) ProcessEnemyAttacks();

    }

    private void SpawnEnemy()
    {
        int index = -1;
        for (int slot = 0; slot < enemies.Length; slot++) if (!enemies[slot].IsAlive) { index = slot; break; }
        if (index < 0) return; // 전투 표시 슬롯이 비면 다음 프레임에 대기 출현을 재시도한다.
        EnemyData enemy = enemies[index];

        enemy.Position = GetRandomSpawnPosition();
        ConfigureM6Enemy(enemy);
        enemy.IsAlive = true;
        ClearCombatTarget(enemy);
        enemy.HasReachedBase = false;
        enemy.NextAttackTime = Time.time + enemyAttackIntervalSeconds;

        enemy.Path.Clear();
        enemy.PathIndex = 0;
        enemy.NextPathTime = 0.0f;
        enemy.BreakThroughWalls = false;
        enemy.TargetWall = null;

        battleView.ShowEnemy(index, enemy.Position);
        ShowM6EnemyVitals(index);
        battleView.SetEnemyStyle(index, enemy.IsElite, enemy.Kind);

        spawnedCount++;
        nextSpawnTime = Time.time + spawnIntervalSeconds;
    }

    private void ClearCombatTarget(EnemyData enemy)
    {
        enemy.TargetNpcId = 0;
        enemy.TargetPlayer = false;
        enemy.TargetTower = null;
        enemy.IsRetaliating = false;
        enemy.NextPathTime = 0f;
    }

    private void SetRetaliation(int index, CampNpcData attacker)
    {
        EnemyData enemy = enemies[index];
        if (!enemy.IsAlive) return;
        CampDefensePost tower = null;
        bool player = attacker == null;
        if (player && (PlayerCombatStats.Instance == null || PlayerCombatStats.ActionsBlocked)) return;
        if (attacker != null && (!attacker.IsAlive || !attacker.IsInCampScene)) return;
        if (attacker != null && attacker.EnteredDefensePostId != 0 &&
            CampDefensePost.TryGetActive(attacker.EnteredDefensePostId, out var post) && post.IsTower) tower = post;
        int npcId = attacker != null && tower == null ? attacker.Id : 0;
        bool sameTarget = enemy.IsRetaliating && enemy.TargetTower == tower &&
            enemy.TargetPlayer == player && enemy.TargetNpcId == npcId;
        if (sameTarget) return;
        float lastAttack = enemy.IsRetaliating ? enemy.LastSuccessfulAttack : Time.time;
        ClearCombatTarget(enemy);
        enemy.TargetPlayer = player;
        enemy.TargetTower = tower;
        enemy.TargetNpcId = npcId;
        enemy.IsRetaliating = true;
        enemy.LastSuccessfulAttack = lastAttack;
    }

    private bool TryGetCombatTarget(EnemyData enemy, out Vector2 position)
    {
        position = default;
        if (enemy.TargetPlayer)
        {
            if (PlayerCombatStats.Instance == null || !PlayerCombatStats.Instance.isActiveAndEnabled ||
                PlayerCombatStats.ActionsBlocked) return false;
            position = PlayerCombatStats.Instance.transform.position;
            return true;
        }
        if (enemy.TargetTower != null)
        {
            if (!enemy.TargetTower.IsAvailable) return false;
            Rect footprint = enemy.TargetTower.Footprint;
            position = new Vector2(Mathf.Clamp(enemy.Position.x, footprint.xMin, footprint.xMax),
                Mathf.Clamp(enemy.Position.y, footprint.yMin, footprint.yMax));
            return true;
        }
        if (!RunCampNpcs.TryGetNpc(enemy.TargetNpcId, out var npc) || !npc.IsAlive ||
            !npc.IsInCampScene || !npc.HasSavedPosition || npc.HasPendingTowerExit) return false;
        // 추적 중인 NPC가 타워에 입장하면 타워가 즉시 대신 피해를 받는다.
        if (npc.EnteredDefensePostId != 0 && CampDefensePost.TryGetActive(npc.EnteredDefensePostId, out var tower) && tower.IsTower)
        {
            enemy.TargetTower = tower;
            enemy.TargetNpcId = 0;
            return TryGetCombatTarget(enemy, out position);
        }
        position = npc.SavedPosition;
        return true;
    }

    private bool CanHitTarget(EnemyData enemy, Vector2 target)
    {
        if (Vector2.Distance(enemy.Position, target) > EnemyReach(enemy) + 0.01f) return false;
        var navigation = CampWallNavigation.Instance;
        if (navigation == null) return false;
        return !navigation.TryFindFirstObstacle(enemy.Position, target, 0f, out _, out var tower, out _) ||
            (enemy.TargetTower != null && tower == enemy.TargetTower);
    }

    private void SelectNearbyTarget(EnemyData enemy)
    {
        float best = EnemyReach(enemy) + 0.001f;
        if (PlayerCombatStats.Instance != null && !PlayerCombatStats.ActionsBlocked)
        {
            Vector2 position = PlayerCombatStats.Instance.transform.position;
            float distance = Vector2.Distance(enemy.Position, position);
            if (distance <= best && CanHitTarget(enemy, position))
            { enemy.TargetPlayer = true; best = distance; }
        }
        foreach (var npc in RunCampNpcs.All)
        {
            if (!npc.IsAlive || !npc.IsInCampScene || !npc.HasSavedPosition || npc.HasPendingTowerExit ||
                (npc.EnteredDefensePostId != 0 && CampDefensePost.TryGetActive(npc.EnteredDefensePostId, out var post) && post.IsTower)) continue;
            float distance = Vector2.Distance(enemy.Position, npc.SavedPosition);
            if (distance >= best || !CanHitTarget(enemy, npc.SavedPosition)) continue;
            enemy.TargetPlayer = false;
            enemy.TargetNpcId = npc.Id;
            best = distance;
        }
    }

    private void MoveEnemies()
    {
        CampWallNavigation navigation = CampWallNavigation.Instance;
        if (navigation == null) return;
        for (int i = 0; i < Mathf.Min(spawnedCount, enemies.Length); i++)
        {
            EnemyData enemy = enemies[i];
            if (!enemy.IsAlive) continue;
            enemy.HasReachedBase = false;
            enemy.TargetWall = null;
            bool hasTarget = TryGetCombatTarget(enemy, out Vector2 destination);
            if ((!hasTarget && (enemy.TargetPlayer || enemy.TargetNpcId != 0 || enemy.TargetTower != null)) || (enemy.IsRetaliating && Time.time - enemy.LastSuccessfulAttack >= 3f) ||
                (hasTarget && !enemy.IsRetaliating && !CanHitTarget(enemy, destination)))
            { ClearCombatTarget(enemy); hasTarget = false; }
            if (!hasTarget)
            {
                SelectNearbyTarget(enemy);
                hasTarget = TryGetCombatTarget(enemy, out destination);
            }
            if (hasTarget && CanHitTarget(enemy, destination)) continue;
            if (!hasTarget) destination = baseTarget.position;
            float distance = Vector2.Distance(enemy.Position, destination);
            if (!hasTarget && distance <= EnemyReach(enemy) + 0.01f &&
                !navigation.TryFindFirstObstacle(enemy.Position, destination, 0f, out _, out _, out _))
            { enemy.HasReachedBase = true; continue; }

            // 타워 안쪽을 경로 목적지로 삼지 않도록 대상 바깥에서 멈춘다.
            bool clearShot = !navigation.TryFindFirstObstacle(enemy.Position, destination, 0f, out _, out var shotTower, out _) ||
                (enemy.TargetTower != null && shotTower == enemy.TargetTower);
            float approachRange = clearShot ? EnemyReach(enemy) : enemyStopDistance;
            Vector2 approach = destination + (enemy.Position - destination).normalized *
                Mathf.Min(approachRange * 0.9f, distance);
            if (Time.time >= enemy.NextPathTime || (approach - enemy.PathDestination).sqrMagnitude > 0.25f)
            {
                enemy.NextPathTime = Time.time + 0.5f;
                enemy.PathDestination = approach;
                bool found = navigation.TryFindPath(enemy.Position, approach, navigation.EnemyRadius, enemy.Path, true);
                if (!found && hasTarget)
                {
                    ClearCombatTarget(enemy);
                    enemy.Path.Clear();
                    continue;
                }
                enemy.BreakThroughWalls = !found;
                if (!found) { enemy.Path.Clear(); enemy.Path.Add(enemy.Position); enemy.Path.Add(approach); }
                enemy.PathIndex = 1;
            }
            while (enemy.PathIndex < enemy.Path.Count &&
                Vector2.Distance(enemy.Position, enemy.Path[enemy.PathIndex]) <= 0.01f) enemy.PathIndex++;
            if (enemy.PathIndex >= enemy.Path.Count) { enemy.NextPathTime = 0f; continue; }
            Vector2 desired = Vector2.MoveTowards(enemy.Position, enemy.Path[enemy.PathIndex], enemyMoveSpeed * Time.deltaTime);
            enemy.Position = navigation.StopBeforeObstacle(enemy.Position, desired, navigation.EnemyRadius,
                out CampWall wall, out CampDefensePost tower);
            battleView.SetEnemyPosition(i, enemy.Position);
            if (enemy.BreakThroughWalls)
            {
                enemy.TargetWall = wall;
                if (tower != null && tower.IsAvailable)
                { enemy.TargetTower = tower; enemy.LastSuccessfulAttack = Time.time; enemy.IsRetaliating = false; }
            }
            if (wall != null || tower != null) enemy.NextPathTime = 0f;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("테스트/M4 건설 배정 상태 출력")]
    private void LogConstructionState()
    {
        if (!Application.isPlaying) return;
        Debug.Log($"[건설 진단] 단계 {state}, 초기화 {isInitialized}, " +
            $"작업 기준 {constructionWorkStepSeconds:F2}초, 프레임 시간 {Time.deltaTime:F3}초", this);
        foreach (CampConstructionSlotView slot in CampConstructionSlotView.All)
        {
            if (!RunCampConstruction.TryGetConstruction(slot.SlotId, out CampConstructionData data)) continue;
            Debug.Log($"[건설 상태] {slot.BuildingKind} #{slot.SlotId} " +
                $"진행 {data.Progress01:P1}, 배정 {RunCampConstruction.GetGroupWorkerCount(slot.SlotId)}, " +
                $"도착 {RunCampNpcs.GetWorkingConstructionWorkerCount(slot.SlotId)}, 단계 {state}", this);
        }
        foreach (CampNpcData npc in RunCampNpcs.All)
            if (npc.CanBuild)
                Debug.Log($"[작업자] {npc.Name} #{npc.Id} " +
                    $"캠프 활성 {npc.IsInCampScene}, 추종 {npc.IsFollowing}, 복귀 {npc.IsReturningToCamp}, " +
                    $"배정 {npc.AssignedConstructionSlotId}, 작업 {npc.WorkingConstructionSlotId}, " +
                    $"위치 {npc.SavedPosition}", this);
    }
#endif

    private void TryBaseAttack()
    {
        foreach (CampNpcData npc in RunCampNpcs.All)
        {
            if (!npc.IsAlive || npc.Profession != CampNpcProfession.Ranged ||
                !npc.IsInCampScene || !npc.HasSavedPosition || npc.HasPendingTowerExit ||
                Time.deltaTime <= 0f || Time.time < npc.NextDefenseAttackTime) continue;

            CampDefensePost post = null;
            if (npc.EnteredDefensePostId != 0)
            {
                if (npc.EnteredDefensePostId != npc.AssignedDefensePostId ||
                    !CampDefensePost.TryGetActive(npc.EnteredDefensePostId, out post) ||
                    !post.Contains(npc.SavedPosition)) continue;
            }
            // 타워 밖 원거리 NPC도 대기·추종·복귀 중 자신의 무기로 공격한다.
            // 타워 이동 예약자는 입장 확인을 거쳐야 타워 보정을 받는다.

            RangedWeaponData weapon = npc.DefenseWeapon;
            float range = npc.BaseAttackRange >= 0 ? npc.BaseAttackRange : weapon != null ? weapon.Range : attackRange;
            float rate = (npc.BaseAttackRate > 0 ? npc.BaseAttackRate : weapon != null ? weapon.AttacksPerSecond : 1f / attackIntervalSeconds) * npc.GrowthAttackRate;
            int damage = (npc.BaseAttackDamage >= 0 ? npc.BaseAttackDamage + RunProgress.AttackDamageBonus : weapon != null ? weapon.Damage + RunProgress.AttackDamageBonus : CurrentAttackDamage) + npc.Strength;
            if (!(range >= 0f) || float.IsInfinity(range) ||
                !(rate > 0f) || float.IsInfinity(rate) || damage < 1) continue;

            if (post != null && post.IsTower)
            {
                range *= TowerRangeMultiplier;
                rate *= TowerAttackSpeedMultiplier;
                damage += TowerAttackDamageBonus;
            }

            Vector2 origin = npc.SavedPosition;
            int targetIndex = FindClosestEnemyIndex(origin, range);

            if (targetIndex < 0)
            {
                if (IsRunning && riftReady && (riftPosition - origin).sqrMagnitude <= range * range)
                {
                    npc.NextDefenseAttackTime = Time.time + 1f / rate;
                    battleView.ShowShotFrom(origin, riftPosition);
                    TryDamageRift(damage);
                    if (!IsRunning) break;
                }
                continue;
            }

            EnemyData target = enemies[targetIndex];

            npc.NextDefenseAttackTime =
                Time.time + 1f / rate;

            battleView.ShowShotFrom(origin, target.Position);

            if (ApplyEnemyDamage(targetIndex, damage)) SetRetaliation(targetIndex, npc);
        }
    }

    private int FindClosestEnemyIndex(Vector2 attackOrigin, float range)
    {
        int closestIndex = -1;
        float closestDistance = float.MaxValue;

        for (int i = 0; i < Mathf.Min(spawnedCount, enemies.Length); i++)
        {
            EnemyData enemy = enemies[i];

            if (!enemy.IsAlive)
            {
                continue;
            }

            float distance = Vector2.Distance(
                attackOrigin,
                enemy.Position);

            if (distance > range ||
                distance >= closestDistance)
            {
                continue;
            }

            closestIndex = i;
            closestDistance = distance;
        }

        return closestIndex;
    }

    private void ProcessEnemyAttacks()
    {
        for (int i = 0; i < Mathf.Min(spawnedCount, enemies.Length); i++)
        {
            EnemyData enemy = enemies[i];

            if (!enemy.IsAlive ||
                Time.time < enemy.NextAttackTime)
            {
                continue;
            }

            if (TryGetCombatTarget(enemy, out Vector2 targetPosition) && CanHitTarget(enemy, targetPosition))
            {
                bool hit = enemy.TargetPlayer ? PlayerCombatStats.TakeDamage(EnemyDamage(enemy)) :
                    enemy.TargetTower != null ? enemy.TargetTower.TakeDamage(EnemyDamage(enemy)) :
                    RunCampNpcs.TryDamageNpc(enemy.TargetNpcId, EnemyDamage(enemy));
                if (hit)
                {
                    enemy.LastSuccessfulAttack = Time.time;
                    enemy.NextAttackTime = Time.time + enemyAttackIntervalSeconds;
                }
                continue;
            }
            if (enemy.TargetWall != null &&
                enemy.TargetWall.IsStanding)
            {
                enemy.NextAttackTime =
                    Time.time + enemyAttackIntervalSeconds;

                enemy.TargetWall.TakeDamage(EnemyDamage(enemy));

                if (!enemy.TargetWall.IsStanding)
                {
                    enemy.TargetWall = null;
                    enemy.NextPathTime = 0.0f;
                }

                continue;
            }

            if (!enemy.HasReachedBase)
            {
                continue;
            }

            enemy.NextAttackTime =
                Time.time + enemyAttackIntervalSeconds;

            currentBaseHp = Mathf.Max(0, currentBaseHp - EnemyDamage(enemy));

            LastBaseHitTime = Time.time;
            battleView.ShowBaseHit();

            Debug.Log(
                $"[거점 피격] 남은 체력: {currentBaseHp}/{maxBaseHp}",
                this);

            if (currentBaseHp == 0)
            {
                pendingDefeat = true;
                return;
            }
        }
    }

    private void FinishNight(NightState result)
    {
        if (state != NightState.Running)
        {
            return;
        }

        if (resultRecorded) return;
        resultRecorded = true;
        RunEconomy.LockNight(result != NightState.Defeat);
        stageCleared = result == NightState.Victory;
        state = stageCleared ? NightState.Escaping : result;
        BattleVersion++;
        if (stageCleared) phaseRemaining = Mathf.Clamp(escapeDurationSeconds, 2f, 3f);
        else
            for (int i = 0; i < Mathf.Min(spawnedCount, enemies.Length); i++) battleView.HideEnemy(i);
        if (result != NightState.Defeat)
        {
            PlayerCombatStats.RecoverAfterNight();
            foreach (CampNpcData npc in RunCampNpcs.All)
                if (npc.IsAlive) { npc.CurrentHp = npc.MaximumHp; npc.CurrentDefense = npc.MaximumDefense; }
        }

        NightBattleStats stats = new NightBattleStats(
            day: nightDay,
            isVictory: result != NightState.Defeat,
            remainingBaseHp: currentBaseHp,
            maxBaseHp: maxBaseHp,
            spawnedCount: spawnedCount,
            defeatedCount: defeatedCount,
            totalEnemyCount: currentEnemyCount,
            attackDamage: CurrentAttackDamage,
            durationSeconds: Mathf.Max(
                0.0f,
                Time.time - battleStartedAt),
            isStageClear: stageCleared);

        RunProgress.SaveBaseHp(currentBaseHp, maxBaseHp);
        completedNightStats = stats;
        RunProgress.SaveNightStats(stats);
        if (stageCleared) RunCampaign.RecordClear();
        else if (result == NightState.Defeat) RunCampaign.Defeat();

        Debug.Log(
            $"[밤 결과 저장] {stats.Day}일차 / {result} / " +
            $"거점 HP: {stats.RemainingBaseHp}/{stats.MaxBaseHp} / " +
            $"생성: {stats.SpawnedCount}/{stats.TotalEnemyCount} / " +
            $"처치: {stats.DefeatedCount} / " +
            $"공격력: {stats.AttackDamage} / " +
            $"전투 시간: {stats.DurationSeconds:F1}초",
            this);
    }

    private void PrepareNight()
    {
        BattleVersion++;

        LastBaseHitTime = float.NegativeInfinity;
        currentBaseHp = RunProgress.RestoreBaseHp(maxBaseHp);
        spawnedCount = 0;
        defeatedCount = 0;

        state = NightState.Preparing;
        phaseRemaining = RunProgress.GetRemainingDay(nightDay, dayDurationSeconds);
        pendingDefeat = resultRecorded = settlementOpened = stageCleared = riftReady = riftPlacementBlocked = false;
        nextRiftSearchTime = 0f;
        battleView.SetRift(false, Vector2.zero, false);

        nextSpawnTime = 0.0f;

        foreach (EnemyData enemy in enemies)
        {
            enemy.Position = baseTarget.position;
            enemy.Hp = currentEnemyMaxHp; enemy.MaximumHp = currentEnemyMaxHp;
            enemy.IsAlive = false;
            enemy.HasReachedBase = false;
            enemy.NextAttackTime = Time.time + enemyAttackIntervalSeconds;
            enemy.Path.Clear();
            enemy.PathIndex = 0;
            enemy.NextPathTime = 0.0f;
            enemy.BreakThroughWalls = false;
            enemy.TargetWall = null;
        }

        battleView.ResetVisuals();

        Debug.Log("[밤 준비] 전투 시작을 기다립니다.", this);
    }

    [ContextMenu("밤 전투 시작")]
    public void BeginNight()
    {
        if (!Application.isPlaying || !CanStartNight)
        {
            Debug.LogWarning(
                "초기화가 완료된 밤 준비 상태에서 실행해 주세요.",
                this);

            return;
        }

        ForestGatheringController.Instance?.EndGathering();
        if (IsDay) { FreezeM6Dda(); ApplyNightDifficulty(); EnsureBattleCapacity(); }
        BattleVersion++;
        if (!riftReady && !TryPrepareRift()) { riftPlacementBlocked = true; return; }
        if (nightSettings == null) nightSettings = M6Balance.Snapshot();
        RunEconomy.BeginNight(nightDay, nightSettings);
        battleStartedAt = Time.time;
        phaseRemaining = Mathf.Max(1f, nightDurationSeconds);
        state = NightState.Running;

        nextSpawnTime = Time.time + 1.0f;

        Debug.Log("[밤 전투 시작]", this);
    }

    [ContextMenu("밤 준비로 초기화")]
    private void RestartNight()
    {
        if (!Application.isPlaying || !CanUseCommands)
        {
            return;
        }

        if (RunCampaign.Data.mode == "NightSettlement") RunCampaign.Data.mode = "Playing";
        else if (RunCampaign.Data.mode != "Playing") return;

#if UNITY_EDITOR
        RefreshRuntimeTestSettings();
#endif
        ApplyNightDifficulty();
        EnsureBattleCapacity();
        RunProgress.SaveRemainingDay(nightDay, Mathf.Max(1f, dayDurationSeconds));
        RunProgress.SaveBaseHp(maxBaseHp, maxBaseHp);
        PrepareNight();
    }

    [ContextMenu("공격력 강화 구매")]
    public void BuyAttackUpgrade()
    {
        if (!Application.isPlaying || !CanBuyAttackUpgrade) return;
        RunProgress.TryPurchaseGoldAttackUpgrade();
    }

    [ContextMenu("다음 날로 이동")]
    public void MoveToNextDay()
    {
        if (!Application.isPlaying || !CanMoveToNextDay)
        {
            return;
        }

        if (!IsSettlement) { OpenSettlement(); return; }
        if (stageCleared) { M7CampaignUI.Instance?.ShowCampaign(); return; }

        if (string.IsNullOrWhiteSpace(daySceneName) ||
            !Application.CanStreamedLevelBeLoaded(daySceneName))
        {
            Debug.LogError(
                $"낮 씬을 불러올 수 없습니다: {daySceneName}\n" +
                "씬 이름과 Build Profiles의 Scene List를 확인해 주세요.",
                this);

            return;
        }

        int previousDay = RunProgress.CurrentDay;
        if (nightDay == int.MaxValue) { Debug.LogWarning("날짜가 정수 한계에 도달했습니다. 새 런으로 시작해 주세요.", this); return; }
        int nextDay = nightDay + 1;

        isChangingScene = true;

        try
        {
            RunProgress.SetDay(nextDay);

            AsyncOperation operation = SceneManager.LoadSceneAsync(
                daySceneName,
                LoadSceneMode.Single);

            if (operation == null)
            {
                throw new System.InvalidOperationException(
                    "다음 날 기지 씬 로드 요청을 생성하지 못했습니다.");
            }
        }
        catch (System.Exception exception)
        {
            RunProgress.SetDay(previousDay);
            isChangingScene = false;

            Debug.LogException(exception, this);
            return;
        }

        Debug.Log(
            $"[다음 날 기지 이동 요청] DAY {previousDay} → {nextDay} / " +
            $"보유 목재: {RunResources.StoredWood}",
            this);
    }

    private void ApplyNightDifficulty() { ApplyM6Difficulty(); }

#if UNITY_EDITOR
    [ContextMenu("테스트/M6 골드 10 지급")]
    private void GrantGoldForTest()
    {
        if (!Application.isPlaying || !isInitialized || isChangingScene) return;
        bool granted = RunResources.TryAddGold(10);
        Debug.Log(granted ? $"[M6 골드 시험] 현재 {RunResources.StoredGold}" : "[M6 골드 시험] 보유량 초과로 지급하지 않았습니다.", this);
    }

    [ContextMenu("테스트/목재 보유량 적용")]
    private void ApplyDebugWood()
    {
        if (!Application.isPlaying || !isInitialized || isChangingScene)
        {
            Debug.LogWarning(
                "초기화가 완료된 Play 모드에서 실행해 주세요.",
                this);

            return;
        }

        if (debugWoodTarget < 0)
        {
            Debug.LogWarning("테스트 목재 수량은 0 이상이어야 합니다.", this);
            return;
        }

        int previousWood = RunResources.StoredWood;

        if (debugWoodTarget > previousWood)
        {
            RunResources.AddWood(debugWoodTarget - previousWood);
        }
        else if (debugWoodTarget < previousWood)
        {
            if (!RunResources.TrySpendWood(previousWood - debugWoodTarget))
            {
                Debug.LogWarning("테스트 목재 수량을 적용하지 못했습니다.", this);
                return;
            }
        }

        Debug.Log(
            $"[목재 테스트] {previousWood} → {RunResources.StoredWood}",
            this);
    }
#endif

    private void FailInitialization(string message)
    {
        Debug.LogError($"[밤 전투 초기화 실패] {message}", this);
        enabled = false;
    }

    private Vector2 GetRandomSpawnPosition()
    {
        // 예고한 균열 방향과 실제 적 출현 위치를 일치시킨다.
        if (riftReady) return riftPosition;
        float angle =
            Random.Range(0.0f, 360.0f) * Mathf.Deg2Rad;

        float distance = Random.Range(
            spawnMinimumDistance,
            spawnMaximumDistance);

        Vector2 direction = new Vector2(
            Mathf.Cos(angle),
            Mathf.Sin(angle));

        return (Vector2)baseTarget.position +
            direction * distance;
    }

    private bool ApplyEnemyDamage(int enemyIndex, int damage)
    {
        if (!IsRunning ||
            damage <= 0 ||
            enemyIndex < 0 ||
            enemyIndex >= Mathf.Min(spawnedCount, enemies.Length))
        {
            return false;
        }

        EnemyData enemy = enemies[enemyIndex];

        if (!enemy.IsAlive)
        {
            return false;
        }

        RunCampNpcs.ApplyDamageToGauges(ref enemy.Hp, ref enemy.Defense, damage, false);
        battleView.ShowEnemyHit(enemyIndex);
        ShowM6EnemyVitals(enemyIndex);

        if (enemy.Hp == 0)
        {
            enemy.IsAlive = false;
            enemy.TargetWall = null;

            defeatedCount++;
            int loot = RunEconomy.RecordKill(Random.value);
            if (loot != 0) battleView.ShowLoot(enemy.Position, loot == 2);
            if (enemy.IsElite) RunEconomy.RecordEliteKill(nightDay, nightSettings);
            battleView.HideEnemy(enemyIndex);
        }

        return true;
    }

    public bool TryDamageEnemyFromProjectile(
        int enemyIndex,
        int damage)
    {
        if (enemyIndex == -2) return TryDamageRift(damage);
        bool damaged = ApplyEnemyDamage(enemyIndex, damage);
        if (damaged) SetRetaliation(enemyIndex, null);
        return damaged;
    }

    public bool TryFindProjectileHit(Vector2 from, Vector2 to, float projectileRadius,
        out int enemyIndex, out float hitFraction)
    {
        enemyIndex = -1;
        hitFraction = float.PositiveInfinity;
        if (!IsRunning) return false;
        for (int i = 0; i < Mathf.Min(spawnedCount, enemies.Length); i++)
        {
            if (!enemies[i].IsAlive || !TrySegmentCircle(from, to, enemies[i].Position,
                enemyHitRadius + projectileRadius, out float fraction) || fraction >= hitFraction) continue;
            enemyIndex = i;
            hitFraction = fraction;
        }
        if (riftReady && TrySegmentCircle(from, to, riftPosition, RiftRadius + projectileRadius,
                out float riftFraction) && riftFraction < hitFraction)
        { enemyIndex = -2; hitFraction = riftFraction; }
        return enemyIndex != -1;
    }

    private static bool TrySegmentCircle(Vector2 from, Vector2 to, Vector2 center, float radius, out float fraction)
    {
        fraction = 0f;
        Vector2 offset = from - center;
        float c = offset.sqrMagnitude - radius * radius;
        if (c <= 0f) return true;
        Vector2 move = to - from;
        float lengthSquared = move.sqrMagnitude;
        if (lengthSquared <= 0.000001f) return false;
        float b = Vector2.Dot(offset, move);
        float discriminant = b * b - lengthSquared * c;
        if (discriminant < 0f) return false;
        fraction = (-b - Mathf.Sqrt(discriminant)) / lengthSquared;
        return fraction >= 0f && fraction <= 1f;
    }

}
