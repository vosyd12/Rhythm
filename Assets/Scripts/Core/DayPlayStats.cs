// 정상 완주한 낮 플레이 한 회의 결과.
// 숫자만 보관하는 데이터형이므로 GameObject에 붙이지 않는다.
public readonly struct DayPlayStats
{
    public int Day { get; }
    public int PlaybackId { get; }

    public int PerfectCount { get; }
    public int GoodCount { get; }
    public int NormalCount { get; }
    public int MissCount { get; }

    // 이 플레이에서 실제 정산한 목재.
    public int WoodEarned { get; }

    public int SuccessCount =>
        RunEconomy.ClampInt((long)PerfectCount + GoodCount + NormalCount);

    public int TotalCount =>
        RunEconomy.ClampInt((long)PerfectCount + GoodCount + NormalCount + MissCount);

    // 판정 대상이 없었다면 성적 평가에 사용하지 않는다.
    public bool HasJudgments => TotalCount > 0;

    // 비율은 0~1로 반환한다.
    public float HitRate =>
        HasJudgments ? (float)(((double)PerfectCount + GoodCount + NormalCount) / ((double)PerfectCount + GoodCount + NormalCount + MissCount)) : 0.0f;

    public float PerfectRate =>
        HasJudgments ? (float)(PerfectCount / ((double)PerfectCount + GoodCount + NormalCount + MissCount)) : 0.0f;

    public float MissRate =>
        HasJudgments ? (float)(MissCount / ((double)PerfectCount + GoodCount + NormalCount + MissCount)) : 0.0f;

    public DayPlayStats(
        int day,
        int playbackId,
        int perfectCount,
        int goodCount,
        int normalCount,
        int missCount,
        int woodEarned)
    {
        Day = day;
        PlaybackId = playbackId;

        PerfectCount = perfectCount;
        GoodCount = goodCount;
        NormalCount = normalCount;
        MissCount = missCount;

        WoodEarned = woodEarned;
    }
}