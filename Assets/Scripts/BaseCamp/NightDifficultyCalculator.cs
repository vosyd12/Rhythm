// 당일 정상 완주 판정의 합계를 평가한다. 밤 도중 고정은 전투 상태가 담당한다.
public static class NightDifficultyCalculator
{
    public static float CalculateFraction(int day, M6BalanceData settings, out string reason)
    {
        reason = "당일 판정 없음";
        if (!RunProgress.TryGetDayStats(day, out var stats) || !stats.HasJudgments) return 0;
        reason = "당일 판정 합계";
        return stats.HitRate < settings.lowHit ? -settings.ddaFraction :
            stats.HitRate >= settings.highHit ? settings.ddaFraction : 0;
    }

    // 기존 디버그 호출과 API를 유지한다. 전날 밤 결과는 사용하지 않는다.
    public static int CalculateEnemyCountAdjustment(int nightDay, out string reason)
    {
        var settings = M6Balance.Snapshot();
        float fraction = CalculateFraction(nightDay, settings, out reason);
        long step = System.Math.Max(0L, (long)nightDay - 1);
        int baseline = RunEconomy.ClampInt(settings.enemyCount + step * settings.countPerDay);
        if (nightDay % settings.eliteEveryDays == 0)
            baseline = RunEconomy.ClampInt((long)System.Math.Round(baseline * (double)settings.eliteCountMultiplier));
        return System.Math.Max(1, RunEconomy.ClampInt((long)System.Math.Round(baseline * (1d + fraction)))) - baseline;
    }
}