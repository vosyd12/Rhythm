using System.Collections.Generic;
using UnityEngine;

// M6의 경제·출현 규칙을 기존 전투 컴포넌트에 연결한다.
public partial class NightDefenseTester
{
    private const int MaximumActiveEnemies = 64;
    private M6BalanceData nightSettings;
    private int frozenDdaDay;
    private float frozenDdaFraction;
    private int currentEnemyDefense, currentEnemyDamage;
    public int ActiveEnemyCount
    { get { int count = 0; if (enemies != null) foreach (var enemy in enemies) if (enemy.IsAlive) count++; return count; } }
    public string EnemyRosterSummary
    {
        get
        {
            int melee = 0, medium = 0, ranged = 0, orcs = 0, elites = 0;
            if (enemies != null) foreach (var enemy in enemies)
            {
                if (!enemy.IsAlive) continue;
                if (enemy.Kind == 2) ranged++; else if (enemy.Kind == 1) medium++; else melee++;
                if (enemy.IsOrc) orcs++; if (enemy.IsElite) elites++;
            }
            return $"활성 근/중/원 {melee}/{medium}/{ranged} · 오크 {orcs} · 엘리트 {elites}\n오크 감소 적용: {RunEconomy.ReducedOrcFromDay}~{RunEconomy.ReducedOrcThroughDay}일차";
        }
    }

    public string BalanceSource => "NightSystem 조정값 → JSON 자동 저장 · " + M6Balance.SourcePath;

    private void InitializeM6()
    {
        RestoreSavedInspectorSettings();
        CaptureM6Settings();
        CampNpcIdleView.RestoreRecruitedViews();
    }
    private void CaptureM6Settings()
    {
        nightSettings = M6Balance.Snapshot();
        if (lastAppliedBalanceRevision != M6Balance.Revision) RestoreSavedInspectorSettings();
    }
    private float CalculateM6Dda(out string reason)
    {
        reason = "당일 판정 없음";
        if (!enableAdaptiveDifficulty) { reason = "OFF"; return 0; }
        if (frozenDdaDay == nightDay) { reason = "중립에서 고정"; return frozenDdaFraction; }
        return NightDifficultyCalculator.CalculateFraction(nightDay, nightSettings, out reason);
    }
    private void FreezeM6Dda()
    {
        CaptureM6Settings(); nightDay = RunProgress.CurrentDay;
        frozenDdaDay = 0; frozenDdaFraction = CalculateM6Dda(out _); frozenDdaDay = nightDay;
    }
    private void ApplyM6Difficulty()
    {
        nightDay = RunProgress.CurrentDay;
        if (nightSettings == null) nightSettings = M6Balance.Snapshot();
        long step = (long)nightDay - 1;
        int baseline = RunEconomy.ClampInt((long)enemyCount + step * nightSettings.countPerDay);
        bool eliteNight = nightDay % nightSettings.eliteEveryDays == 0;
        if (eliteNight) baseline = RunEconomy.ClampInt((long)System.Math.Round(baseline * (double)nightSettings.eliteCountMultiplier));
        float dda = CalculateM6Dda(out nightDifficultyReason);
        double stageMultiplier = RunCampaign.Rules.stageEnemyMultipliers[RunCampaign.Stage - 1];
        currentEnemyCount = Mathf.Max(1, RunEconomy.ClampInt((long)System.Math.Round(baseline * (1d + dda) * stageMultiplier)));
        nightEnemyCountAdjustment = currentEnemyCount - baseline;
        currentEnemyMaxHp = Mathf.Max(1, RunEconomy.ClampInt((long)enemyMaxHp + step * nightSettings.hpPerDay));
        currentEnemyDefense = (int)System.Math.Min(nightSettings.defenseMaximum, step / nightSettings.defenseEveryDays);
        currentEnemyDamage = Mathf.Max(1, RunEconomy.ClampInt((long)enemyWallDamage + step / nightSettings.damageEveryDays));
    }
    private float EnemyReach(EnemyData enemy) => enemy.AttackRange > 0 ? enemy.AttackRange : enemyStopDistance;
    private int EnemyDamage(EnemyData enemy) => enemy.Damage > 0 ? enemy.Damage : enemyWallDamage;
    private int EnemyMaximumHp(EnemyData enemy) => enemy.MaximumHp > 0 ? enemy.MaximumHp : currentEnemyMaxHp;

    private void ConfigureM6Enemy(EnemyData enemy)
    {
        if (nightSettings == null) nightSettings = M6Balance.Snapshot();
        enemy.IsElite = spawnedCount == 0 && nightDay % nightSettings.eliteEveryDays == 0;
        enemy.IsOrc = enemy.IsElite || Random.value < RunEconomy.OrcChance(nightDay, nightSettings);
        float roll = Random.value;
        enemy.Kind = nightDay >= nightSettings.rangedDay && roll < nightSettings.rangedChance ? 2 :
            nightDay >= nightSettings.mediumDay && roll < nightSettings.rangedChance * (nightDay >= nightSettings.rangedDay ? 1 : 0) + nightSettings.mediumChance ? 1 : 0;
        enemy.AttackRange = enemy.Kind == 2 ? nightSettings.rangedRange : enemy.Kind == 1 ? nightSettings.mediumRange : 0;
        enemy.MaximumHp = enemy.IsElite ? Mathf.Max(1, RunEconomy.ClampInt((long)currentEnemyMaxHp * nightSettings.eliteHpMultiplier)) : currentEnemyMaxHp;
        enemy.Hp = enemy.MaximumHp; enemy.Defense = enemy.MaximumDefense = currentEnemyDefense;
        enemy.Damage = enemy.IsElite ? Mathf.Max(1, RunEconomy.ClampInt((long)currentEnemyDamage * nightSettings.eliteDamageMultiplier)) : currentEnemyDamage;
    }
    private void ShowM6EnemyVitals(int index)
    {
        var enemy = enemies[index];
        battleView.SetEnemyVitals(index, enemy.Hp, EnemyMaximumHp(enemy), enemy.Defense, enemy.MaximumDefense);
    }

    public bool TryRecruitNpc(CampNpcProfession profession)
    {
        if (!CanUseEconomy) return false;
        if (!CampNpcIdleView.TryChooseRecruitTemplate(profession, out var template) ||
            !TryFindRecruitPosition(out Vector3 position)) return false;
        CampNpcData prototype = template.CreateRecruitPrototype(this);
        if (!RunEconomy.TryRecruit(profession, position, prototype, out var npc)) return false;
        template.CreateRecruitedView(npc); return true;
    }
    private bool TryFindRecruitPosition(out Vector3 position)
    {
        position = baseTarget.position;
        var navigation = CampWallNavigation.Instance;
        if (navigation == null) return false;
        float radius = Mathf.Max(0.3f, navigation.AllyRadius);
        Rect bounds = navigation.MapBounds;
        var path = new List<Vector2>();
        for (int ring = 1; ring <= 12; ring++)
            for (int index = 0; index < 24; index++)
            {
                float angle = index * Mathf.PI / 12;
                Vector2 point = (Vector2)baseTarget.position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (1.5f + ring * 0.65f);
                if (point.x - radius < bounds.xMin || point.x + radius > bounds.xMax || point.y - radius < bounds.yMin || point.y + radius > bounds.yMax) continue;
                bool blocked = false;
                foreach (var slot in CampConstructionSlotView.All)
                {
                    Rect area = slot.Footprint;
                    if (RunCampConstruction.HasStarted(slot.SlotId) && point.x >= area.xMin - radius && point.x <= area.xMax + radius &&
                        point.y >= area.yMin - radius && point.y <= area.yMax + radius) { blocked = true; break; }
                }
                if (blocked) continue;
                foreach (var obstacle in Physics2D.OverlapCircleAll(point, radius)) if (!obstacle.isTrigger) { blocked = true; break; }
                foreach (var npc in RunCampNpcs.All)
                    if (npc.IsAlive && npc.HasSavedPosition && Vector2.Distance(npc.SavedPosition, point) < radius * 2 + 0.1f) { blocked = true; break; }
                if (blocked || !navigation.TryFindPath(point, point, radius, path, true)) continue;
                position = new Vector3(point.x, point.y, baseTarget.position.z); return true;
            }
        return false;
    }
    private int lastAppliedBalanceRevision = -1;
    private string lastInspectorAttempt;
    public float NpcFallbackRange => attackRange;
    public int NpcFallbackDamage => attackDamage;
    public float NpcFallbackAttackRate => 1f / attackIntervalSeconds;
    public string InspectorSaveStatus { get; private set; } = "";

    public M6BalanceData CaptureInspectorSettings()
    {
        var candidate = M6Balance.Snapshot();
        candidate.enemyCount = enemyCount; candidate.enemyHp = enemyMaxHp;
        candidate.enemyDamage = enemyWallDamage; candidate.enemySpeed = enemyMoveSpeed;
        candidate.spawnInterval = spawnIntervalSeconds; candidate.enemyAttackInterval = enemyAttackIntervalSeconds;
        candidate.nightSystem = JsonUtility.FromJson<M6NightSystemData>(JsonUtility.ToJson(this));
        return candidate;
    }
    private string InspectorSignature() => JsonUtility.ToJson(CaptureInspectorSettings().nightSystem) +
        $"|{enemyCount}|{enemyMaxHp}|{enemyWallDamage}|{enemyMoveSpeed:R}|{spawnIntervalSeconds:R}|{enemyAttackIntervalSeconds:R}";
    private void RestoreSavedInspectorSettings()
    {
        var saved = M6Balance.Snapshot();
        if (saved.nightSystem != null)
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(saved.nightSystem), this);
        enemyCount = saved.enemyCount; enemyMaxHp = saved.enemyHp;
        enemyWallDamage = saved.enemyDamage; enemyMoveSpeed = saved.enemySpeed;
        spawnIntervalSeconds = saved.spawnInterval; enemyAttackIntervalSeconds = saved.enemyAttackInterval;
        lastAppliedBalanceRevision = M6Balance.Revision;
        lastInspectorAttempt = InspectorSignature();
#if UNITY_EDITOR
        if (isInitialized) CaptureTestSettings();
#endif
    }
    public bool SaveInspectorSettings(out string error)
    {
        bool saved = M6Balance.TrySaveInspector(CaptureInspectorSettings(), out error);
        InspectorSaveStatus = saved ? "NightSystem 수치를 JSON에 자동 저장했습니다." : error;
        if (saved)
        {
            lastAppliedBalanceRevision = M6Balance.Revision;
            lastInspectorAttempt = InspectorSignature();
        }
        return saved;
    }
    private void PersistInspectorChanges()
    {
        string signature = InspectorSignature();
        if (signature == lastInspectorAttempt) return;
        lastInspectorAttempt = signature;
        if (!SaveInspectorSettings(out string error)) Debug.LogWarning(error, this);
    }

    public float GetNpcFinalRange(CampNpcData npc)
    {
        float range = npc.BaseAttackRange >= 0 ? npc.BaseAttackRange : npc.DefenseWeapon != null ? npc.DefenseWeapon.Range : attackRange;
        if (npc.EnteredDefensePostId != 0 && npc.EnteredDefensePostId == npc.AssignedDefensePostId &&
            CampDefensePost.TryGetActive(npc.EnteredDefensePostId, out var post) && post.IsTower && post.Contains(npc.SavedPosition))
            range *= TowerRangeMultiplier;
        return range;
    }

    public bool CanUseEconomy => CanUseCommands && (IsPreparing || IsRunning || IsSettlement) && !GameExitUI.IsWorldPaused;
    public CampDefensePost NearestUpgradeTower => PlayerCombatStats.Instance != null
        ? CampDefensePost.FindNearest(PlayerCombatStats.Instance.transform.position, true) : null;
    public bool TryUpgradeNearestTower()
    {
        if (!CanUseEconomy || PlayerCombatStats.Instance == null) return false;
        var post = NearestUpgradeTower;
        return post != null && post.TryUpgrade();
    }
#if UNITY_EDITOR
    [ContextMenu("테스트/M6 경제 검증 재화 지급(목재500 골드500)")]
    private void GrantM6TestResources()
    {
        if (!Application.isPlaying || !CanUseCommands) return;
        RunResources.TryAddWood(500); RunResources.TryAddGold(500);
    }
    [ContextMenu("테스트/M6 5일차 낮으로 이동")]
    private void JumpToEliteDay() => JumpToM6Day(5);
    [ContextMenu("테스트/M6 10일차 낮으로 이동")]
    private void JumpToRangedDay() => JumpToM6Day(10);
    private void JumpToM6Day(int day)
    {
        if (!Application.isPlaying || !CanUseCommands) return;
        RunProgress.SetDay(day); frozenDdaDay = 0; CaptureM6Settings(); ApplyNightDifficulty(); EnsureBattleCapacity();
        RunProgress.SaveRemainingDay(day, Mathf.Max(1, dayDurationSeconds)); PrepareNight(); CaptureTestSettings();
    }
    [ContextMenu("테스트/M6 다음 밤부터 JSON 설정 우선")]
    private void UseJsonNextNight()
    {
        if (Application.isPlaying && !M6Balance.TryReload(out string error)) Debug.LogWarning(error, this);
    }
#endif
}
