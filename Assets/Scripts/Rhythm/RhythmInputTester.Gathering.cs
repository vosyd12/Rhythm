using System;

public partial class RhythmInputTester
{
    public bool UsesGatheringWindows { get; set; }
    public bool IsGatheringWindowOpen { get; private set; }

    public void BeginGatheringWindow(double songTime)
    {
        if (!UsesGatheringWindows || !isInitialized) return;
        ResetForNewPlayback();
        AdvanceAfterJudgment();
        pendingInputs.Clear(); activeHold = null; hasTimeoutSample = false;
        // 진입 이전 노트는 실패로 산정하지 않고, 접근 표시를 볼 수 있는 새 노트부터 받는다.
        double firstTarget = songTime + rhythmEventTester.ApproachSeconds;
        while (rhythmEventTester.HasCurrentEvent && rhythmEventTester.EventTimeSeconds < firstTarget)
            rhythmEventTester.MoveNextEvent();
        IsGatheringWindowOpen = true;
    }

    public void EndGatheringWindow(double songTime)
    {
        if (!IsGatheringWindowOpen) return;
        ProcessPendingInputs(); AdvanceAfterJudgment();
        CompleteHoldJudgment(RhythmJudgment.Miss, songTime, "채집 종료 전 Hold 미완료");
        // 아직 도착하지 않은 노트는 집계하지 않는다. 현재 구간에서 놓친 노트만 마감한다.
        while (rhythmEventTester.HasCurrentEvent && rhythmEventTester.EventTimeSeconds <= songTime)
        {
            CompleteJudgment(RhythmJudgment.Miss, songTime, "채집 구간 종료");
            AdvanceAfterJudgment();
        }
        IsGatheringWindowOpen = false;
        pendingInputs.Clear(); hasTimeoutSample = false;
    }
}
