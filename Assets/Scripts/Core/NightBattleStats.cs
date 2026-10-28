// 완료한 밤 전투 한 회의 결과.
public readonly struct NightBattleStats
{
    public int Day { get; }
    public bool IsVictory { get; }
    public bool IsStageClear { get; }

    public int RemainingBaseHp { get; }
    public int MaxBaseHp { get; }

    public int SpawnedCount { get; }
    public int DefeatedCount { get; }
    public int TotalEnemyCount { get; }

    // 종료 시점의 공격력.
    public int AttackDamage { get; }

    // 전투 시작 버튼을 누른 순간부터 승패 확정까지의 시간.
    public float DurationSeconds { get; }

    public float BaseHpRatio =>
        MaxBaseHp > 0
            ? (float)RemainingBaseHp / MaxBaseHp
            : 0.0f;

    public NightBattleStats(
        int day,
        bool isVictory,
        int remainingBaseHp,
        int maxBaseHp,
        int spawnedCount,
        int defeatedCount,
        int totalEnemyCount,
        int attackDamage,
        float durationSeconds,
        bool isStageClear = false)
    {
        Day = day;
        IsVictory = isVictory;
        IsStageClear = isStageClear;

        RemainingBaseHp = remainingBaseHp;
        MaxBaseHp = maxBaseHp;

        SpawnedCount = spawnedCount;
        DefeatedCount = defeatedCount;
        TotalEnemyCount = totalEnemyCount;

        AttackDamage = attackDamage;
        DurationSeconds = durationSeconds;
    }
}