
using UnityEngine;

// 리듬 플레이의 판정 횟수를 기록한다.
// 음악을 정상 완주하면 목재를 한 번 지급하고 정산 결과를 저장한다.
public class FarmingResultTester : MonoBehaviour
{
    [SerializeField]
    private RhythmInputTester rhythmInputTester;

    [SerializeField]
    private MusicPlayback musicPlayback;

    [SerializeField]
    private RhythmPatternGenerator patternGenerator;

    [Header("숲 탐사 보상")]

    // 이번 탐사 한 번에서 획득할 수 있는 최대 목재.

    // 곡 길이나 생성된 노트 수가 달라도 이 상한은 동일하다.
    // 100은 연결 확인을 위한 임시 값이며 Inspector에서 조절한다.
    [SerializeField, Min(0)]
    private int maximumWoodReward = 100;

    private bool isInitialized;
    private bool hasSettled;

    // 현재 재생 회차의 정산이 완료되었을 때만 true.
    // 재생 회차가 바뀐 직후에도 이전 결과로 복귀하지 못하게 한다.
    public bool HasSettled =>
        hasSettled &&
        musicPlayback != null &&
        musicPlayback.IsGameplayPlayback &&
        observedPlaybackId == musicPlayback.PlaybackId;

    // 이번 플레이에서 정산한 목재
    public int RoundWood { get; private set; }
    // 낮 화면에서도 공통으로 관리하는 목재 보유량을 조회한다.
    public int StoredWood => RunResources.StoredWood;


    // 외부에서는 읽을 수 있지만 값을 직접 변경할 수는 없다.
    public int PerfectCount { get; private set; }
    public int GoodCount { get; private set; }
    public int NormalCount { get; private set; }
    public int MissCount { get; private set; }

    // 별도 카운터를 중복 관리하지 않고 각 판정 횟수의 합을 구한다.
    public int TotalCount =>
        PerfectCount + GoodCount + NormalCount + MissCount;

    private int observedPlaybackId = -1;


    private void Awake()
    {
        if (rhythmInputTester == null ||
            musicPlayback == null ||
            patternGenerator == null)
        {
            Debug.LogError(
                "FarmingResultTester의 판정기, 음악 재생기와 " +
                "패턴 생성기를 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        isInitialized = true;
    }

    private void OnEnable()
    {
        if (!isInitialized) { return; }

        rhythmInputTester.JudgmentCompleted += HandleJudgment;

        ResetForNewPlayback();
    }

    private void OnDisable()
    {
        // 등록했던 알림을 해제한다.
        // 초기화 실패나 연결 대상 제거 상황도 고려한다.
        if (rhythmInputTester != null)
        {
            rhythmInputTester.JudgmentCompleted -= HandleJudgment;
        }
    }

    private void LateUpdate()
    {
        if (!isInitialized) { return; }

        ResetForNewPlayback();

        // 미리듣기를 끝까지 들어도 정산하지 않는다.
        if (!musicPlayback.IsGameplayPlayback ||
            hasSettled ||
            !musicPlayback.HasNaturallyEnded)
        {
            return;
        }

        // 다음 구간 분석이나 후보 선택이 아직 남았다면 기다린다.
        if (!patternGenerator.IsGenerationComplete)
        {
            return;
        }

        // 이 함수 안에서 발생하는 판정 알림은 즉시 처리되므로,
        // 반환됐을 때에는 마지막 Miss까지 횟수에 반영되어 있다.
        rhythmInputTester.FinalizeRemainingEvents();

        SettleRewards();
    }

    private void SettleRewards()
    {
        // 정산 함수 자체에서도 조건을 확인한다.
        // 다른 곳에서 호출하더라도 미리듣기 보상이나 중복 지급을 막는다.
        if (!musicPlayback.IsGameplayPlayback ||
            !musicPlayback.HasNaturallyEnded ||
            hasSettled)
        {
            return;
        }

        // 최종 기록을 사용해서 정산 시점에만 계산한다.
        RoundWood = CalculateWoodReward();

        // 지급 전에 정산 완료 상태를 기록한다.
        hasSettled = true;

        // 이번 곡의 정산 결과를 공통 보유량에 한 번 지급한다.
        RunResources.AddWood(RoundWood);

        // 마지막 Miss 처리와 목재 지급까지 끝난 결과를 저장한다.
        DayPlayStats stats = new DayPlayStats(
            day: RunProgress.CurrentDay,
            playbackId: observedPlaybackId,
            perfectCount: PerfectCount,
            goodCount: GoodCount,
            normalCount: NormalCount,
            missCount: MissCount,
            woodEarned: RoundWood);

        RunProgress.SaveDayStats(stats);

        Debug.Log(
            $"[정산 완료] 재생 회차: {observedPlaybackId} / " +
            $"Perfect: {PerfectCount}, Good: {GoodCount}, " +
            $"Normal: {NormalCount}, Miss: {MissCount} / " +
            $"이번 목재: {RoundWood} / 보유 목재: {StoredWood}");
    }

    private int CalculateWoodReward()
    {
        // 기존 판정별 점수 비중을 유지한다.

        // Perfect: 3점
        // Good:    2점
        // Normal:  1점
        // Miss:    0점
        const int perfectWeight = 3;
        const int goodWeight = 2;
        const int normalWeight = 1;

        // Miss도 전체 판정 수에 포함한다.

        // Miss를 제외하면 성공한 노트만으로 비율을 계산하게 되어
        // 대부분 놓치고 하나만 Perfect여도 최대 보상을 받을 수 있다.
        long totalJudgments =
            (long)PerfectCount +
            GoodCount +
            NormalCount +
            MissCount;

        // 판정할 노트가 없었던 곡은 보상을 지급하지 않는다.
        // 0으로 나누는 계산도 방지한다.
        if (totalJudgments <= 0)
        {
            return 0;
        }

        // 실제로 획득한 판정 점수.

        // 첫 곱셈부터 long으로 계산해서
        // int보다 넓은 정수 범위로 중간 계산을 처리한다.
        long earnedScore =
            (long)PerfectCount * perfectWeight +
            (long)GoodCount * goodWeight +
            (long)NormalCount * normalWeight;

        // 모든 노트가 Perfect였을 때 얻을 수 있는 점수.
        long maximumScore =
            totalJudgments * perfectWeight;

        // 코드나 실행 중 설정 변경으로 음수가 들어와도
        // 보상이 음수가 되지 않도록 한 번 더 제한한다.
        int rewardLimit = Mathf.Max(0, maximumWoodReward);

        // 지급 목재 = 최대 보상 × 획득 점수 / 만점.

        // 예:
        // 최대 보상 100, 획득 점수 15, 만점 30
        // → 100 × 15 / 30 = 목재 50.

        // 곱셈을 먼저 하고 나눗셈을 나중에 한다.
        // earnedScore / maximumScore부터 정수로 계산하면
        // 만점 미만의 비율이 0으로 잘릴 수 있기 때문이다.
        long reward =
            (long)rewardLimit * earnedScore / maximumScore;

        // 양수 정수 나눗셈이므로 소수점 이하는 버린다.
        // 예: 66.666... → 목재 66.
        return (int)reward;
    }

    private void ResetForNewPlayback()
    {
        // 같은 재생 회차에서는 기록을 유지한다.
        if (observedPlaybackId == musicPlayback.PlaybackId)
        {
            return;
        }

        observedPlaybackId = musicPlayback.PlaybackId;

        // 현재 테스트에서는 음악을 처음부터 재생하면
        // 새로운 시도로 취급하고 이전 판정 기록을 초기화한다.
        PerfectCount = 0;
        GoodCount = 0;
        NormalCount = 0;
        MissCount = 0;
        hasSettled = false;
        RoundWood = 0;
    }

    private void HandleJudgment(RhythmJudgment judgment)
    {
        // 컴포넌트 Update 순서에 관계없이
        // 새 회차 초기화를 먼저 끝내고 판정을 기록한다.
        ResetForNewPlayback();

        // 정산이 확정된 기록은 이후 알림으로 변경하지 않는다.
        if (!musicPlayback.IsGameplayPlayback || hasSettled) { return; }

        switch (judgment)
        {
            case RhythmJudgment.Perfect:
                PerfectCount++;
                break;

            case RhythmJudgment.Good:
                GoodCount++;
                break;

            case RhythmJudgment.Normal:
                NormalCount++;
                break;

            case RhythmJudgment.Miss:
                MissCount++;
                break;

            default:
                return;
        }

        Debug.Log(
            $"[판정 기록] {judgment} / " +
            $"Perfect: {PerfectCount}, " +
            $"Good: {GoodCount}, " +
            $"Normal: {NormalCount}, " +
            $"Miss: {MissCount} / " +
            $"총 판정: {TotalCount}");
    }
}