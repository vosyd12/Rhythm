using System.Collections.Generic;
using UnityEngine;

// 한 번의 게임 실행 동안 날짜별 남은 AP를 보관한다.
// MonoBehaviour가 아니므로 GameObject에 붙이지 않는다.
public static class RunDayActions
{
    // 첫 구현은 Normal 난이도의 하루 4 AP를 사용한다.
    private const int DefaultActionPoints = 4;

    public static int MaxActionPoints => DefaultActionPoints;

    // 키: 날짜
    // 값: 해당 날짜에 남아 있는 AP
    //
    // 날짜별로 저장하므로 같은 날 씬을 왕복해도 AP가 유지된다.
    private static readonly Dictionary<int, int> remainingByDay =
        new Dictionary<int, int>();

    public static int RemainingActionPoints
    {
        get
        {
            int day = RunProgress.CurrentDay;

            if (remainingByDay.TryGetValue(day, out int remaining))
            {
                return remaining;
            }

            // 아직 AP를 소비하지 않은 날짜는 최대 AP로 취급한다.
            // 다음 날로 넘어가면 그 날짜의 기록이 없으므로 4가 된다.
            return MaxActionPoints;
        }
    }

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        // 새로운 게임 실행에서는 이전 실행의 AP 기록을 지운다.
        // 일반적인 씬 전환에서는 호출되지 않는다.
        ResetForNewRun();
    }

    // 일반 행동은 AP 차감과 함께 건설을 진행한다.
    // 실패 시 AP를 복구해야 하는 행동은 진행을 잠시 보류할 수 있다.
    public static bool TrySpendActionPoints(
        int amount,
        bool advanceConstruction = true)
    {
        if (amount <= 0)
        {
            Debug.LogWarning("소비할 AP는 1 이상이어야 합니다.");
            return false;
        }

        int remaining = RemainingActionPoints;

        if (remaining < amount)
        {
            return false;
        }

        remainingByDay[RunProgress.CurrentDay] =
            remaining - amount;

        if (advanceConstruction)
        {
            RunCampConstruction.AdvanceWork(amount);
        }

        return true;
    }

    public static void ResetForNewRun()
    {
        // 날짜별 AP 기록만 초기화한다.
        remainingByDay.Clear();
    }

    // 씬 로드 요청 자체가 실패했을 때만 사용한다.
    // 정상 입장 후의 재시작, 중단, 판정 실패에는 호출하지 않는다.
    public static void RestoreAfterFailedSceneLoad(
        int day,
        int previousActionPoints)
    {
        if (day < 1 ||
            previousActionPoints < 0 ||
            previousActionPoints > MaxActionPoints)
        {
            Debug.LogError("복구할 날짜와 AP 값을 확인해 주세요.");
            return;
        }

        // AP를 더하는 방식이 아니라, 차감 직전 값으로 되돌린다.
        // 예: 입장 전 4 → 차감 후 3 → 요청 실패 시 4로 복구.

        // 다른 날짜의 AP 기록은 변경하지 않는다.
        remainingByDay[day] = previousActionPoints;
    }

#if UNITY_EDITOR
    public static void RestoreActionPointsForDebug()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        remainingByDay[RunProgress.CurrentDay] = MaxActionPoints;

        Debug.Log(
            $"[AP 디버그] 현재 날짜의 AP를 {MaxActionPoints}로 복구했습니다.");
    }
#endif
}