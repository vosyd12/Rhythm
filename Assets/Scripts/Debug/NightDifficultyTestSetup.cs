using UnityEngine;

// BaseCamp 씬을 직접 실행해 DDA 적용 결과를 확인하는 개발용 컴포넌트.
// 가상의 2일차 낮 기록을 준비한 뒤 2일차 낮으로 시작한다.
// NightSystem에 붙이고, Play를 누르기 전에 Scenario를 선택한다.
[DisallowMultipleComponent]
public class NightDifficultyTestSetup : MonoBehaviour
{
#if UNITY_EDITOR
    // Off: 일반 플레이.
    // 나머지는 가상의 당일 결과를 선택하는 항목이다.
    private enum TestScenario
    {
        Off,
        LowHitRate,
        LowBaseHp,
        Standard,
        StrongResults,
        NoPreviousStats,
        NoJudgments
    }

    [SerializeField]
    private TestScenario scenario = TestScenario.Off;

    private void Awake()
    {
        // 비활성화 상태이거나 Off이면 테스트를 적용하지 않는다.
        if (!enabled || scenario == TestScenario.Off)
        {
            return;
        }

        // 새 실행에서 BaseCamp 씬을 직접 시작했을 때만 사용한다.
        // 실제 플레이 기록이 있거나 날짜가 진행되었다면 덮어쓰지 않는다.
        if (RunProgress.CurrentDay != 1 ||
            RunProgress.TryGetDayStats(1, out _) ||
            RunProgress.TryGetNightStats(1, out _))
        {
            Debug.Log(
                "[DDA 테스트] 진행 기록이 있어 테스트 적용을 건너뜁니다.",
                this);

            return;
        }

        // 이 컴포넌트는 가상의 기록만 준비한다.
        // 난이도 계산은 기존 NightDifficultyCalculator가 수행한다.
        switch (scenario)
        {
            case TestScenario.LowHitRate:
                SaveTestStats(perfect: 5, miss: 5, baseHp: 5);
                break;

            case TestScenario.LowBaseHp:
                SaveTestStats(perfect: 10, miss: 0, baseHp: 2);
                break;

            case TestScenario.Standard:
                SaveTestStats(perfect: 7, miss: 3, baseHp: 3);
                break;

            case TestScenario.StrongResults:
                SaveTestStats(perfect: 9, miss: 1, baseHp: 4);
                break;

            case TestScenario.NoPreviousStats:
                // 기록을 저장하지 않아 "당일 기록 없음" 상태를 만든다.
                break;

            case TestScenario.NoJudgments:
                SaveTestStats(perfect: 0, miss: 0, baseHp: 5);
                break;

            default:
                return;
        }

        // 이후 NightDefenseTester.Start에서
        // 2일차 난이도를 계산하고 적 풀을 생성한다.
        RunProgress.SetDay(2);

        Debug.Log(
            $"[DDA 테스트] {scenario} / 가상의 2일차 낮 결과로 " +
            "2일차 낮을 시작합니다. 중립에서 DDA가 고정됩니다. 일반 플레이 시 Scenario를 Off로 바꾸세요.",
            this);
    }

    private static void SaveTestStats(
        int perfect,
        int miss,
        int baseHp)
    {
        // 낮 완료 기록을 만든다.
        // WoodEarned는 결과 기록이며, 실제 목재 지급은 발생하지 않는다.
        RunProgress.SaveDayStats(new DayPlayStats(
            day: 2,
            playbackId: RunProgress.AllocatePlaybackId(),
            perfectCount: perfect,
            goodCount: 0,
            normalCount: 0,
            missCount: miss,
            woodEarned: perfect * 3));

        // 과거 비교용 밤 기록도 보관하지만 M6 DDA에는 사용하지 않는다.
        // 거점 HP 외의 값도 가상의 완료 기록으로 채운다.
        RunProgress.SaveNightStats(new NightBattleStats(
            day: 2,
            isVictory: true,
            remainingBaseHp: baseHp,
            maxBaseHp: 5,
            spawnedCount: 5,
            defeatedCount: 5,
            totalEnemyCount: 5,
            attackDamage: 3,
            durationSeconds: 20.0f));
    }
#endif
}