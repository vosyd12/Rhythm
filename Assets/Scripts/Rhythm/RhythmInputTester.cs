using System;
using System.Collections.Generic;
using UnityEngine;

// 프로젝트 안에 한 번만 선언한다.
public enum RhythmJudgment
{
    Perfect,
    Good,
    Normal,
    Miss
}

// 좌우 Tap과 Hold의 입력 및 최종 판정을 담당한다.
public partial class RhythmInputTester : MonoBehaviour
{
    [SerializeField]
    private MusicPlayback musicPlayback;

    [SerializeField]
    private RhythmEventTester rhythmEventTester;

    // Inspector 값은 120 BPM에서 사용할 기준 판정 범위다.
    [SerializeField]
    private float perfectWindowMs = 60.0f;

    [SerializeField]
    private float goodWindowMs = 140.0f;

    [SerializeField]
    private float normalWindowMs = 210.0f;

    // Hold를 일찍 놓았을 때 Normal로 인정하는 추가 여유.
    // BPM 보정 후의 Normal 범위에 밀리초 단위로 더한다.
    [SerializeField, Min(0.0f)]
    private float holdEarlyReleaseGraceMs = 100.0f;

    [SerializeField]
    private bool logInputTiming = true;

    // 외부 시스템도 실제 적용 중인 판정 범위를 읽는다.
    public float PerfectWindowMs =>
        perfectWindowMs * GetTimingWindowScale();

    public float GoodWindowMs =>
        goodWindowMs * GetTimingWindowScale();

    public float NormalWindowMs =>
        normalWindowMs * GetTimingWindowScale();

    // 실제 판정과 화면 안내에서 같은 조기 해제 범위를 사용한다.
    public float HoldEarlyReleaseWindowMs =>
        NormalWindowMs + holdEarlyReleaseGraceMs;

    private ForestInputReader inputReader;

    private bool isInitialized;
    private bool hasJudged;
    private bool hasFinalized;
    private int observedPlaybackId = -1;

    // 현재 유지 중인 Hold.
    // null이면 Hold가 시작되지 않은 상태다.
    private RhythmNoteData activeHold;

    // 시작 판정은 보관만 하고 정산기로 보내지 않는다.
    private RhythmJudgment holdStartJudgment;

    public bool HasJudged => hasJudged;

    // 다음 입력을 기다리는 노트와 별도로,
    // 현재 유지하고 있는 Hold를 연출기에서 조회한다.
    public RhythmNoteData ActiveHoldNote => activeHold;

    // Tap 또는 Hold 하나의 최종 결과만 전달한다.
    public event Action<RhythmJudgment> JudgmentCompleted;

    // 실제 판정한 노트, 판정 결과, 곡 기준 처리 시각을 전달한다.
    // 나무와 도끼 연출기가 사용할 알림이다.
    public event Action<RhythmNoteData, RhythmJudgment, double> NoteJudged;

    // 유효한 입력으로 Hold를 시작했을 때 전달하는 연출용 알림.

    // 첫 번째 값: 시작한 Hold 노트.
    // 두 번째 값: 실제로 누른 곡 기준 시각.

    // 최종 판정 알림과 분리하므로
    // Hold 시작 타격으로 판정 횟수나 목재가 추가되지 않는다.
    public event Action<RhythmNoteData, double> HoldStarted;

    private readonly struct PendingInput
    {
        public double SongTime { get; }
        public RhythmInputSide Side { get; }

        // true: 누름 / false: 놓음.
        public bool IsPress { get; }

        public PendingInput(
            double songTime,
            RhythmInputSide side,
            bool isPress)
        {
            SongTime = songTime;
            Side = side;
            IsPress = isPress;
        }
    }

    private readonly Queue<PendingInput> pendingInputs =
        new Queue<PendingInput>();

    // 입력 이벤트가 전달될 시간을 확보하기 위해
    // 자동 시간 초과 검사는 이전 프레임의 곡 시간을 사용한다.
    private bool hasTimeoutSample;
    private double previousSongTime;

    private void Awake()
    {
        if (musicPlayback == null ||
            rhythmEventTester == null)
        {
            Debug.LogError(
                "Music Playback과 Rhythm Event Tester를 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        if (!(perfectWindowMs > 0.0f &&
              goodWindowMs >= perfectWindowMs &&
              normalWindowMs >= goodWindowMs) ||
            float.IsInfinity(normalWindowMs))
        {
            Debug.LogError(
                "판정 범위는 0 < Perfect <= Good <= Normal이어야 합니다.",
                this);

            enabled = false;
            return;
        }

        inputReader = new ForestInputReader();
        isInitialized = true;
    }

    private void OnEnable()
    {
        if (!isInitialized)
        {
            return;
        }

        // 입력기를 켜기 전에 수신 함수를 연결한다.
        inputReader.InputReceived += HandleInputReceived;
        inputReader.Enable();
    }

    private void OnDisable()
    {
        // 초기화 실패 시에는 입력기가 생성되지 않았을 수 있다.
        if (inputReader != null)
        {
            inputReader.InputReceived -= HandleInputReceived;
            inputReader.Disable();
        }

        pendingInputs.Clear();
        activeHold = null;
        hasTimeoutSample = false;
    }

    private void OnDestroy()
    {
        inputReader?.Dispose();
    }

    private void Update()
    {
        if (!isInitialized) { return; }

        ResetForNewPlayback();

        if (!musicPlayback.IsGameplayPlayback)
        {
            pendingInputs.Clear();
            activeHold = null;
            hasTimeoutSample = false;
            return;
        }

        if (hasFinalized || (UsesGatheringWindows && !IsGatheringWindowOpen)) { return; }

        AdvanceAfterJudgment();

        // 일시정지 직전에 접수된 입력도 먼저 처리한다.
        ProcessPendingInputs();

        if (musicPlayback.IsPaused ||
            !Application.isFocused ||
            !musicPlayback.TryGetSongTime(out double songTime))
        {
            hasTimeoutSample = false;
            return;
        }

        // 정상적인 놓기는 입력 큐에서 이미 처리했다.
        // 재개 과정 등에서 해제 입력을 놓쳤다면 Hold만 Miss로 정리한다.
        if (activeHold != null && !IsHoldButtonPressed())
        {
            CompleteHoldJudgment(
                RhythmJudgment.Miss,
                songTime,
                "Hold 유지 버튼이 눌려 있지 않음");
        }

        if (hasTimeoutSample)
        {
            // 유지 중인 Hold의 종료 제한을 따로 검사한다.
            ExpireHold(previousSongTime);

            AdvanceAfterJudgment();

            // Hold 중이어도 다음 Tap의 입력 제한은 계속 검사한다.
            while (rhythmEventTester.HasCurrentEvent &&
                   (previousSongTime - rhythmEventTester.EventTimeSeconds)
                       * 1000.0 > NormalWindowMs)
            {
                CompleteJudgment(
                    RhythmJudgment.Miss,
                    previousSongTime,
                    "시작 시간 안에 유효한 입력 없음");

                AdvanceAfterJudgment();
            }
        }

        previousSongTime = songTime;
        hasTimeoutSample = true;
    }

    private void ExpireHold(double songTime)
    {
        if (activeHold == null) { return; }

        double lateMs =
            (songTime - activeHold.EndTimeSeconds) * 1000.0;

        if (lateMs > NormalWindowMs)
        {
            CompleteHoldJudgment(
                RhythmJudgment.Miss,
                songTime,
                "Hold 종료 시간 안에 버튼을 놓지 않음");
        }
    }

    private bool IsHoldButtonPressed()
    {
        // 호출부에서 activeHold가 있는지 확인한 뒤 호출한다.
        return inputReader.IsPressed(activeHold.Side);
    }

    private void ProcessPendingInputs()
    {
        while (pendingInputs.Count > 0)
        {
            PendingInput input = pendingInputs.Dequeue();
            // 이번 입력을 처리하면서 지나간 노트를 정리했는지 기록한다.
            bool expiredNoteBeforeTarget = false;

            AdvanceAfterJudgment();

            // 이 입력 시각까지 Hold 종료 제한을 넘겼다면 먼저 정리한다.
            ExpireHold(input.SongTime);

            // 놓기 입력은 유지 중인 Hold의 해당 방향에만 사용한다.
            if (!input.IsPress)
            {
                if (activeHold != null &&
                    input.Side == activeHold.Side)
                {
                    FinishHold(input);
                }

                continue;
            }

            // Hold 중에는 같은 손의 추가 누르기와 Space를 받지 않는다.
            if (activeHold != null &&
                (input.Side == activeHold.Side ||
                 input.Side == RhythmInputSide.Both))
            {
                continue;
            }

            while (rhythmEventTester.HasCurrentEvent)
            {
                RhythmNoteData note = rhythmEventTester.CurrentNote;

                double offsetMs =
                    (input.SongTime - note.TargetTimeSeconds) * 1000.0;

                if (offsetMs > NormalWindowMs)
                {
                    CompleteJudgment(
                        RhythmJudgment.Miss,
                        input.SongTime,
                        "입력 시각 이전에 시작 제한 시간 경과");

                    AdvanceAfterJudgment();

                    expiredNoteBeforeTarget = true;

                    // 다음 노트가 정상 판정 범위라면 이번 입력으로 처리할 수 있다.
                    continue;
                }

                if (offsetMs < -NormalWindowMs)
                {
                    // 앞 노트의 시간 초과를 정리한 입력으로
                    // 아직 이른 다음 노트까지 선입력 Miss로 만들지 않는다.
                    if (expiredNoteBeforeTarget)
                    {
                        break;
                    }

                    double approachStartTime =
                        note.TargetTimeSeconds -
                        rhythmEventTester.ApproachSeconds;

                    bool hasStartedApproaching =
                        input.SongTime >= approachStartTime;

                    bool matchesCurrentNote =
                        note.IsResource &&
                        input.Side == note.Side;

                    // 유지 중인 Hold와 반대쪽 Tap의 판정을 분리한다.
                    bool canJudgeWhileHolding =
                        activeHold == null ||
                        (note.Kind == ForestNoteKind.Tap &&
                         note.Side != activeHold.Side);

                    if (hasStartedApproaching &&
                        matchesCurrentNote &&
                        canJudgeWhileHolding)
                    {
                        CompleteJudgment(
                            RhythmJudgment.Miss,
                            input.SongTime,
                            $"접근 중 선입력 / {note.Kind} / 오차 {offsetMs:F1}ms");
                    }

                    // 한 번의 입력으로 다른 노트까지 처리하지 않는다.
                    break;
                }

                if (!note.IsResource)
                {
                    break;
                }

                if (input.Side != note.Side)
                {
                    break;
                }

                // 이번 단계에서는 Hold 하나 + 반대쪽 Tap만 허용한다.
                if (activeHold != null &&
                    (note.Kind != ForestNoteKind.Tap ||
                     note.Side == activeHold.Side))
                {
                    break;
                }

                RhythmJudgment startJudgment = JudgeTiming(offsetMs);

                if (note.IsHold)
                {
                    activeHold = note;
                    holdStartJudgment = startJudgment;

                    // 유지할 Hold는 activeHold에 보관했다.
                    // 이제 시작을 기다리는 목록 위치만 다음으로 옮긴다.
                    rhythmEventTester.MoveNextEvent();

                    // 시작 모션만 알린다. 최종 판정이나 보상은 보내지 않는다.
                    HoldStarted?.Invoke(note, input.SongTime);

                    if (logInputTiming)
                    {
                        Debug.Log(
                            $"[Hold 시작] 노트 {note.Id + 1} / " +
                            $"{note.Side} / 시작 판정 {startJudgment} / " +
                            $"놓기 목표 {note.EndTimeSeconds:F3}초",
                            this);
                    }
                }
                else
                {
                    CompleteJudgment(
                        startJudgment,
                        input.SongTime,
                        $"{note.Kind} / 오차 {offsetMs:F1}ms");
                }

                // 한 번 누른 입력으로 여러 노트를 시작하지 않는다.
                break;
            }
        }
    }

    private void FinishHold(PendingInput input)
    {
        double releaseOffsetMs =
            (input.SongTime - activeHold.EndTimeSeconds) * 1000.0;

        RhythmJudgment releaseJudgment =
            JudgeTiming(releaseOffsetMs);

        // 기존 판정창을 벗어났더라도,
        // 조금 일찍 놓은 입력은 추가 여유 안에서 Normal로 인정한다.
        if (releaseJudgment == RhythmJudgment.Miss &&
            releaseOffsetMs < 0.0 &&
            -releaseOffsetMs <= HoldEarlyReleaseWindowMs)
        {
            releaseJudgment = RhythmJudgment.Normal;
        }

        // enum 순서는 Perfect=0, Good=1, Normal=2, Miss=3이다.
        // 숫자가 큰 쪽이 더 낮은 판정이므로 Max를 사용한다.
        RhythmJudgment finalJudgment = (RhythmJudgment)Math.Max(
            (int)holdStartJudgment,
            (int)releaseJudgment);

        CompleteHoldJudgment(
            finalJudgment,
            input.SongTime,
            $"Hold 종료 / 시작 {holdStartJudgment} / " +
            $"놓기 {releaseJudgment} / 오차 {releaseOffsetMs:F1}ms");
    }

    private RhythmJudgment JudgeTiming(double offsetMs)
    {
        double errorMs = Math.Abs(offsetMs);

        if (errorMs <= PerfectWindowMs)
        {
            return RhythmJudgment.Perfect;
        }

        if (errorMs <= GoodWindowMs)
        {
            return RhythmJudgment.Good;
        }

        if (errorMs <= NormalWindowMs)
        {
            return RhythmJudgment.Normal;
        }

        return RhythmJudgment.Miss;
    }

    private void CompleteJudgment(
        RhythmJudgment judgment,
        double songTime,
        string detail)
    {
        if (hasJudged ||
            !rhythmEventTester.HasCurrentEvent)
        {
            return;
        }

        RhythmNoteData note = rhythmEventTester.CurrentNote;

        // 현재 대기 노트만 완료 처리한다.
        // 별도로 유지 중인 Hold는 해제하지 않는다.
        hasJudged = true;

        PublishJudgment(note, judgment, songTime, detail);
    }

    private void CompleteHoldJudgment(
        RhythmJudgment judgment,
        double songTime,
        string detail)
    {
        if (activeHold == null) { return; }

        RhythmNoteData note = activeHold;

        // 알림 전에 해제하여 같은 Hold의 중복 완료를 막는다.
        activeHold = null;

        // Hold 시작 때 이미 목록을 이동했다.
        // 여기서는 hasJudged나 현재 목록 위치를 변경하지 않는다.
        PublishJudgment(note, judgment, songTime, detail);
    }

    private void PublishJudgment(
        RhythmNoteData note,
        RhythmJudgment judgment,
        double songTime,
        string detail)
    {

        Debug.Log(
            $"[리듬 판정 추적] " +
            $"노트 {note.Id + 1} / {note.Kind} / {note.Side} / " +
            $"판정 {judgment} / 처리 {songTime:F3}초 / {detail}",
            this);

        // 연출기에는 실제 판정한 노트를 전달한다.
        NoteJudged?.Invoke(note, judgment, songTime);

        // 기존 정산 알림은 최종 판정마다 한 번만 보낸다.
        JudgmentCompleted?.Invoke(judgment);
    }

    private void AdvanceAfterJudgment()
    {
        if (!hasJudged) { return; }

        rhythmEventTester.MoveNextEvent();
        hasJudged = false;
    }

    private void ResetForNewPlayback()
    {
        if (observedPlaybackId == musicPlayback.PlaybackId)
        {
            return;
        }

        observedPlaybackId = musicPlayback.PlaybackId;

        pendingInputs.Clear();
        rhythmEventTester.ResetEvents();

        activeHold = null;
        hasJudged = false;
        hasFinalized = false;
        hasTimeoutSample = false;
    }

    public void FinalizeRemainingEvents()
    {
        if (!isInitialized ||
            !musicPlayback.IsGameplayPlayback ||
            !musicPlayback.HasNaturallyEnded)
        {
            return;
        }

        ResetForNewPlayback();

        if (hasFinalized || (UsesGatheringWindows && !IsGatheringWindowOpen)) { return; }

        ProcessPendingInputs();
        AdvanceAfterJudgment();

        // 시작한 Hold는 이미 대기 목록에서 분리되어 있다.
        // 곡 종료 시 아직 남아 있다면 별도로 한 번 정리한다.
        CompleteHoldJudgment(
            RhythmJudgment.Miss,
            musicPlayback.SongDurationSeconds,
            "음원 종료까지 Hold 완료하지 못함");

        // 진행 중이던 Hold도 완료하지 못했다면 한 번만 Miss로 처리한다.
        while (rhythmEventTester.HasCurrentEvent)
        {
            CompleteJudgment(
                RhythmJudgment.Miss,
                musicPlayback.SongDurationSeconds,
                "음원 종료까지 노트 완료하지 못함");

            AdvanceAfterJudgment();
        }

        hasFinalized = true;
        hasTimeoutSample = false;
    }

    private float GetTimingWindowScale()
    {
        if (rhythmEventTester == null)
        {
            return 1.0f;
        }

        int approachBeats = rhythmEventTester.ApproachBeats;
        double approachSeconds = rhythmEventTester.ApproachSeconds;

        // BPM 확정 전에는 기준값을 사용한다.
        if (approachBeats < 1 ||
            !(approachSeconds > 0.0) ||
            double.IsInfinity(approachSeconds))
        {
            return 1.0f;
        }

        double secondsPerBeat =
            approachSeconds / approachBeats;

        // 120 BPM의 한 박자는 0.5초.
        // 느릴수록 배율이 커지고, 빠를수록 작아진다.
        double scale = secondsPerBeat / 0.5;

        return (float)Math.Max(
            0.8,
            Math.Min(1.4, scale));
    }

    private void HandleInputReceived(
    double inputRealtime,
    RhythmInputSide side,
    bool isPress)
    {
        if (!isInitialized || !isActiveAndEnabled)
        {
            return;
        }

        ResetForNewPlayback();

        if (hasFinalized || (UsesGatheringWindows && (!IsGatheringWindowOpen || GameExitUI.IsWorldPaused)))
        {
            return;
        }

        // MusicPlayback이 미리듣기, 일시정지, 포커스 상실,
        // 입력 허용 시각을 검사하고 곡 기준 시각으로 변환한다.
        if (!musicPlayback.TryGetInputSongTime(
                inputRealtime,
                out double inputSongTime))
        {
            return;
        }

        pendingInputs.Enqueue(
            new PendingInput(
                inputSongTime,
                side,
                isPress));

        if (!logInputTiming)
        {
            return;
        }

        if (side == RhythmInputSide.Both)
        {
            Debug.Log(
                $"[Sync 입력 접수] Space / 곡 {inputSongTime:F3}초",
                this);
        }
        else
        {
            string actionLabel = isPress ? "누름" : "놓음";

            Debug.Log(
                $"[입력 접수] {side} / {actionLabel} / " +
                $"곡 {inputSongTime:F3}초",
                this);
        }
    }

    // 조회만 한다. Hold를 종료하거나 판정을 발생시키지 않는다.
    public bool CanReleaseActiveHold(double songTime)
    {
        if (activeHold == null ||
            holdStartJudgment == RhythmJudgment.Miss)
        {
            return false;
        }

        double releaseOffsetMs =
            (songTime - activeHold.EndTimeSeconds) * 1000.0;

        // 일찍 놓을 때만 추가 여유를 적용한다.
        // 늦게 놓을 때는 기존 Normal 범위를 사용한다.
        return releaseOffsetMs >= -HoldEarlyReleaseWindowMs &&
               releaseOffsetMs <= NormalWindowMs;
    }
}