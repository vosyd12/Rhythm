using UnityEngine;
using System.Collections.Generic;

// 한 번의 게임 실행 동안 날짜를 유지한다.
public static partial class RunProgress
{
    public static int CurrentDay { get; private set; }
    private static int nextPlaybackId;
    public static int AllocatePlaybackId() => checked(++nextPlaybackId);
    private static readonly HashSet<int> announcedDays = new HashSet<int>();
    public static bool TryConsumeDayAnnouncement(int day) => day > 0 && announcedDays.Add(day);
    private static int savedBaseHp = -1, savedBaseMaximum;
    public static int RestoreBaseHp(int maximum)
    {
        if (savedBaseHp < 0) return maximum;
        return Mathf.Clamp(maximum - (savedBaseMaximum - savedBaseHp), savedBaseHp > 0 ? 1 : 0, maximum);
    }
    public static void SaveBaseHp(int hp, int maximum) { savedBaseHp = hp; savedBaseMaximum = maximum; }
    private static readonly Dictionary<int, float> remainingDaySeconds = new Dictionary<int, float>();
    public static bool HasPreviousRift { get; private set; }
    public static Vector2 PreviousRiftPosition { get; private set; }
    public static float GetRemainingDay(int day, float duration)
    {
        if (!remainingDaySeconds.TryGetValue(day, out float remaining))
        { remaining = Mathf.Max(1f, duration); remainingDaySeconds[day] = remaining; }
        return remaining;
    }
    public static void SaveRemainingDay(int day, float remaining) => remainingDaySeconds[day] = Mathf.Max(0f, remaining);
    public static void RememberRift(Vector2 position) { PreviousRiftPosition = position; HasPreviousRift = true; }
    // 이번 게임 실행 동안 구매한 공격력 보너스의 합계.
    public static int AttackDamageBonus { get; private set; }

    // 날짜 번호로 해당 날짜의 플레이 결과를 찾는다.
    private static readonly Dictionary<int, DayPlayStats> dayStats =
        new Dictionary<int, DayPlayStats>();

    // 날짜별로 가장 최근에 완료한 밤 전투 결과를 보관한다.
    private static readonly Dictionary<int, NightBattleStats> nightStats =
        new Dictionary<int, NightBattleStats>();

    // 이번 게임 실행에서 공격력 강화를 구매한 날짜를 저장한다.
    //
    // HashSet은 같은 값을 중복으로 보관하지 않는 자료구조다.
    // 예: 1일차와 3일차에 구매했다면 1, 3을 보관한다.
    private static readonly HashSet<int> attackUpgradePurchaseDays =
        new HashSet<int>();

    // 현재 날짜가 구매 기록에 포함되어 있는지 조회한다.
    // 씬을 다시 불러와도 static 클래스의 기록은 유지된다.
    public static bool HasPurchasedAttackUpgradeToday =>
        attackUpgradePurchaseDays.Contains(CurrentDay);

    // 구매 조건 검사부터 목재 차감, 강화 적용, 구매 기록까지 처리한다.

    // true: 구매 완료.
    // false: 구매하지 않았으며, 목재와 공격력도 변경하지 않는다.
    public static bool TryPurchaseGoldAttackUpgrade()
    {
        var settings = M6Balance.Snapshot();
        if (HasPurchasedAttackUpgradeToday || AttackDamageBonus >= settings.attackMaximum ||
            !RunResources.TrySpendGold(settings.attackCost)) return false;
        AttackDamageBonus++; attackUpgradePurchaseDays.Add(CurrentDay); return true;
    }

    public static bool TryPurchaseAttackUpgrade(
        int woodCost,
        int damageBonus)
    {
        return woodCost > 0 && damageBonus > 0 && TryPurchaseGoldAttackUpgrade();
    }

    // 새로 실행하면 1일차부터 시작한다.
    // 일반적인 씬 전환에서는 호출되지 않는다.
    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        CurrentDay = 1; nextPlaybackId = 0;
        remainingDaySeconds.Clear();
        announcedDays.Clear();
        savedBaseHp = -1; savedBaseMaximum = 0;
        HasPreviousRift = false;
        AttackDamageBonus = 0;

        // 새 게임 실행 시 이전 실행의 통계를 지운다.
        dayStats.Clear();
        dayPlaybackIds.Clear();
        nightStats.Clear();
        attackUpgradePurchaseDays.Clear();
    }

    // 같은 날 정상 완주한 서로 다른 재생 회차를 합산한다.
    private static readonly Dictionary<int, HashSet<int>> dayPlaybackIds = new Dictionary<int, HashSet<int>>();
    public static void SaveDayStats(DayPlayStats stats)
    {
        if (stats.Day < 1 || stats.PlaybackId < 1 || stats.PerfectCount < 0 || stats.GoodCount < 0 ||
            stats.NormalCount < 0 || stats.MissCount < 0 || stats.WoodEarned < 0) return;
        if (!dayPlaybackIds.TryGetValue(stats.Day, out var ids))
            dayPlaybackIds.Add(stats.Day, ids = new HashSet<int>());
        if (!ids.Add(stats.PlaybackId)) return;
        dayStats.TryGetValue(stats.Day, out var prior);
        dayStats[stats.Day] = new DayPlayStats(stats.Day, stats.PlaybackId,
            RunEconomy.ClampInt((long)prior.PerfectCount + stats.PerfectCount),
            RunEconomy.ClampInt((long)prior.GoodCount + stats.GoodCount),
            RunEconomy.ClampInt((long)prior.NormalCount + stats.NormalCount),
            RunEconomy.ClampInt((long)prior.MissCount + stats.MissCount),
            RunEconomy.ClampInt((long)prior.WoodEarned + stats.WoodEarned));
    }

    // 해당 날짜의 기록이 있으면 true를 반환한다.
    public static bool TryGetDayStats(
        int day,
        out DayPlayStats stats)
    {
        return dayStats.TryGetValue(day, out stats);
    }

    // 같은 밤을 다시 완료하면 최신 결과로 교체한다.
    public static void SaveNightStats(NightBattleStats stats)
    {
        nightStats[stats.Day] = stats;
    }

    public static bool TryGetNightStats(
        int day,
        out NightBattleStats stats)
    {
        return nightStats.TryGetValue(day, out stats);
    }

    // 다음 날 이동과 이동 실패 시 복구에 사용한다.
    public static void SetDay(int day)
    {
        if (day < 1)
        {
            Debug.LogError("날짜는 1 이상이어야 합니다.");
            return;
        }

        CurrentDay = day;
    }

    // 자신의 기록 초기화
    public static void ResetForNewRun()
    {
        Debug.Log("[새 게임 데이터] 초기화 시작");

        // 초기화 규칙을 여러 곳에 복사하지 않고 기존 함수를 호출한다.
        Initialize();

        // 새 게임에서 유지하면 안 되는 진행 데이터 초기화.
        PlayerCombatStats.ResetForNewRun();
        RunEconomy.ResetForNewRun();
        RunResources.ResetForNewRun();      // 목재

        // 새 게임에서는 이전 실행의 AP 소비 기록도 제거한다.
        RunDayActions.ResetForNewRun();     // 날짜별 AP 기록

        // 새 게임에서는 기본 NPC 두 명으로 다시 시작한다.   
        RunCampNpcs.ResetForNewRun();       // NPC 데이터와 배치

        RunCampConstruction.ResetForNewRun();   // 착공작업량 완공 기록

        string npc1Assignment =
    RunCampNpcs.TryGetNpc(1, out CampNpcData npc1)
        ? npc1.AssignedConstructionSlotId.ToString()
        : "NPC 데이터 없음";

        string npc2Assignment =
            RunCampNpcs.TryGetNpc(2, out CampNpcData npc2)
                ? npc2.AssignedConstructionSlotId.ToString()
                : "NPC 데이터 없음";

        Debug.Log(
            "[새 게임 데이터] 초기화 직후\n" +
            $"DAY={CurrentDay}, " +
            $"WOOD={RunResources.StoredWood}\n" +
            $"NPC 1 배치={npc1Assignment}, " +
            $"NPC 2 배치={npc2Assignment}\n" +
            $"1번 벽 착공={RunCampConstruction.HasStarted(1)}, " +
            $"2번 벽 착공={RunCampConstruction.HasStarted(2)}");
    }
}