using System;
using System.Collections.Generic;
using UnityEngine;

// 음원 분석, 후보 선택, Hold 계산의 진행을 연결한다.
// 최종 노트 종류와 방향을 결정하고 생성된 노트 목록을 제공한다.
public class RhythmPatternGenerator : MonoBehaviour
{
    [SerializeField]
    private AudioAnalysisTester audioAnalyzer;

    [SerializeField]
    private RhythmBeatClock beatClock;

    [SerializeField]
    private double firstEventTimeSeconds = 2.0;

    [SerializeField, Min(2)]
    private int minimumEventGapBeats = 2;

    // 로그 표시용 묶음이다. 실제 프레이즈 길이는 phraseBeats를 사용한다.
    [SerializeField, Min(1)]
    private int eventsPerPattern = 4;

    [SerializeField, Range(0.0f, 0.5f)]
    private float maxSnapDistanceBeats = 0.25f;

    [SerializeField, Min(0.0f)]
    private double endSafetySeconds = 0.3;

    [SerializeField, Range(1, 2)]
    private int beatSubdivisions = 2;

    [SerializeField, Min(1)]
    private int approachBeats = 4;

    [Header("Hold 생성")]

    [SerializeField]
    private bool enableHoldNotes = true;

    [SerializeField, Min(1)]
    private int minimumHoldBeats = 2;

    [SerializeField, Min(1)]
    private int maximumHoldBeats = 8;

    [SerializeField, Range(0.05f, 1.0f)]
    private float holdSustainRatio = 0.55f;

    [SerializeField, Min(0.01f)]
    private float holdQuietSeconds = 0.2f;

    [SerializeField, Range(0.0f, 1.0f)]
    private float minimumHoldSustainCoverage = 0.8f;

    [SerializeField, Min(0)]
    private int minimumHoldRestBeats = 4;

    [SerializeField, Range(0.5f, 2.0f)]
    private float rhythmConnectionGapBeats = 1.0f;

    [Header("Sync 생성")]

    [SerializeField]
    private bool enableSyncNotes = true;

    [SerializeField, Min(0.01f)]
    private float minimumSyncStrength = 0.8f;

    [SerializeField, Min(1)]
    private int minimumSyncGapBeats = 8;

    [Header("분석 기록 기반 후보")]

    [SerializeField, Min(0.0001f)]
    private float rhythmSilenceRms = 0.005f;

    [SerializeField, Min(0.001f)]
    private float minimumRhythmStrength = 0.1f;

    [Header("구간별 후보 선택")]

    [SerializeField, Range(1, 32)]
    private int phraseBeats = 8;

    [SerializeField, Range(0.0f, 1.0f)]
    private float phraseAccentRatio = 0.75f;

    [SerializeField, Range(0.0f, 1.0f)]
    private float phraseRhythmWeight = 0.4f;

    [Header("반복 구간 리듬 유지")]

    [SerializeField]
    private bool enableRepeatedPhraseMatching = true;

    [SerializeField, Range(0.0f, 1.0f)]
    private float repeatedPhraseSimilarity = 0.8f;

    [SerializeField, Range(0.0f, 1.0f)]
    private float repeatedPhraseBonus = 0.2f;

    [SerializeField, Range(1, 64)]
    private int phraseHistoryLimit = 32;

    [Header("노트 공백 진단")]

    [SerializeField]
    private bool logLongNoteGaps = true;

    [SerializeField, Min(0.1f)]
    private float diagnosticGapSeconds = 3.0f;

    private const double TimeToleranceSeconds = 0.000001;

    // 기존 Normal 210ms일 때의 생성 간격을 보존하는 기준.
    // 실제 입력 판정창과는 별도로 사용한다.
    private const float PatternSpacingReferenceMs = 210.0f;

    private readonly RhythmCandidateExtractor candidateExtractor =
        new RhythmCandidateExtractor();

    private readonly RhythmPhraseSelector phraseSelector =
        new RhythmPhraseSelector();

    // 외부에서 참조하는 목록이므로 새 목록으로 교체하지 않는다.
    private readonly List<RhythmNoteData> notes =
        new List<RhythmNoteData>();

    private int nextCandidateIndex;
    private int nextPhraseIndex;
    private int lastSelectedGridIndex = -1;

    private double lastEventStartTime = double.NegativeInfinity;
    private double lastGeneratedMinimumGap = double.PositiveInfinity;
    private double lastSyncTargetTime = double.NegativeInfinity;

    private RhythmNoteData lastGeneratedHold;

    private bool isInitialized;
    private bool isPatternRequested;
    private bool allPhrasesPrepared;
    private bool hasGenerationFailed;

    private int gapRejectedByBeat;
    private int gapRejectedBySnap;
    private int gapRejectedByTime;
    private int gapRejectedBySpacing;

    public int ApproachBeats => approachBeats;

    public double ApproachSeconds =>
        beatClock != null && beatClock.IsConfigured
            ? beatClock.SecondsPerBeat * approachBeats
            : 0.0;

    public IReadOnlyList<RhythmNoteData> Notes => notes;

    public bool IsInitialPatternReady { get; private set; }

    public bool HasGenerationFailed =>
        hasGenerationFailed ||
        (audioAnalyzer != null && audioAnalyzer.HasAnalysisFailed);

    public bool IsGenerationComplete =>
        isInitialized &&
        isPatternRequested &&
        !HasGenerationFailed &&
        audioAnalyzer != null &&
        audioAnalyzer.IsAnalysisComplete &&
        candidateExtractor.HasProcessedAvailableWindows(
            audioAnalyzer.WindowRmsValues.Count) &&
        allPhrasesPrepared &&
        nextCandidateIndex >= phraseSelector.SelectedCandidates.Count;

    public bool CanPrepareForGameplay =>
        isActiveAndEnabled &&
        isInitialized &&
        !isPatternRequested &&
        !HasGenerationFailed &&
        audioAnalyzer != null &&
        audioAnalyzer.isActiveAndEnabled &&
        audioAnalyzer.IsInitialAnalysisReady &&
        beatClock != null &&
        beatClock.CanConfigure;

    private void Start()
    {
        if (audioAnalyzer == null ||
            beatClock == null)
        {
            FailGeneration(
                "Audio Analyzer와 Beat Clock을 연결해 주세요.");

            return;
        }

        if (!(rhythmSilenceRms > 0.0f) ||
            float.IsInfinity(rhythmSilenceRms) ||
            !(minimumRhythmStrength > 0.0f) ||
            float.IsInfinity(minimumRhythmStrength))
        {
            FailGeneration("분석 기록 기반 후보 설정값을 확인해 주세요.");
            return;
        }

        if (firstEventTimeSeconds < 0.0 ||
            approachBeats < 1 ||
            minimumEventGapBeats < 2 ||
            eventsPerPattern < 1)
        {
            FailGeneration(
                "첫 이벤트 시각은 0 이상, 접근 박자는 1 이상, " +
                "이벤트 간격은 2박 이상, 로그 묶음 개수는 1 이상이어야 합니다.");

            return;
        }

        if (minimumHoldBeats < 1 ||
            maximumHoldBeats < minimumHoldBeats ||
            !(holdSustainRatio >= 0.05f && holdSustainRatio <= 1.0f) ||
            !(holdQuietSeconds > 0.0f) ||
            float.IsInfinity(holdQuietSeconds) ||
            !(minimumHoldSustainCoverage >= 0.0f &&
              minimumHoldSustainCoverage <= 1.0f) ||
            minimumHoldRestBeats < 0)
        {
            FailGeneration(
                "Hold 길이, 유지 비율, 종료 확인 시간, " +
                "유지 구간 비율과 Hold 사이 간격을 확인해 주세요.");

            return;
        }

        if (!(minimumSyncStrength > 0.0f) ||
            float.IsInfinity(minimumSyncStrength) ||
            minimumSyncGapBeats < 1)
        {
            FailGeneration(
                "Sync 강세 기준은 0보다 커야 하며, " +
                "최소 간격은 1박 이상이어야 합니다.");

            return;
        }

        if (beatSubdivisions < 1 || beatSubdivisions > 2)
        {
            FailGeneration("Beat Subdivisions는 1 또는 2로 설정해 주세요.");
            return;
        }

        if (phraseBeats < 1 || phraseBeats > 32)
        {
            FailGeneration("Phrase Beats는 1~32로 설정해 주세요.");
            return;
        }

        isInitialized = true;
    }

    public bool TryPrepareForGameplay(
        double bpm,
        double firstBeatTimeSeconds)
    {
        if (!Application.isPlaying || !CanPrepareForGameplay)
        {
            return false;
        }

        if (!beatClock.TryConfigureForGameplay(bpm, firstBeatTimeSeconds))
        {
            return false;
        }

        notes.Clear();

        candidateExtractor.Reset();
        phraseSelector.Reset(phraseBeats * beatSubdivisions);

        nextCandidateIndex = 0;
        nextPhraseIndex = 0;
        lastSelectedGridIndex = -1;

        lastEventStartTime = double.NegativeInfinity;
        lastGeneratedMinimumGap = double.PositiveInfinity;
        lastSyncTargetTime = double.NegativeInfinity;
        lastGeneratedHold = null;

        allPhrasesPrepared = false;
        IsInitialPatternReady = false;
        isPatternRequested = true;

        ResetGapDiagnosticCounts();

        Debug.Log(
            "[실제 패턴 준비 요청] 확정한 박자로 후보를 검사합니다.",
            this);

        return true;
    }

    private void Update()
    {
        if (hasGenerationFailed || !isPatternRequested)
        {
            return;
        }

        if (audioAnalyzer.HasAnalysisFailed ||
            !audioAnalyzer.isActiveAndEnabled ||
            !beatClock.isActiveAndEnabled)
        {
            FailGeneration(
                "분석 또는 박자 계산 컴포넌트의 상태를 확인해 주세요.");

            return;
        }

        if (!audioAnalyzer.IsInitialAnalysisReady)
        {
            return;
        }

        if (!candidateExtractor.TryRefresh(
                audioAnalyzer.WindowRmsValues,
                audioAnalyzer.WindowRiseValues,
                audioAnalyzer.AnalysisHopSeconds,
                rhythmSilenceRms,
                minimumRhythmStrength,
                out string failureReason))
        {
            FailGeneration(failureReason);
            return;
        }

        double clipDuration = audioAnalyzer.ClipDurationSeconds;

        if (clipDuration <= 0.0)
        {
            FailGeneration("분석 중인 음원의 길이를 확인할 수 없습니다.");
            return;
        }

        double secondsPerBeat = beatClock.SecondsPerBeat;
        double songTime = GetSongTime(clipDuration, secondsPerBeat);

        PrepareReadyPhrases(clipDuration, secondsPerBeat, songTime);
        GenerateReadyNotes(clipDuration, secondsPerBeat, songTime);
        RefreshInitialPatternReady(secondsPerBeat);
    }

    private double GetSongTime(
        double clipDuration,
        double secondsPerBeat)
    {
        // 자연 종료 후에도 마지막 구간의 생성 처리를 마쳐야 한다.
        if (beatClock.HasNaturallyEnded)
        {
            return clipDuration;
        }

        if (beatClock.TryGetBeatPosition(out double beatPosition))
        {
            return beatClock.FirstBeatTimeSeconds +
                   beatPosition * secondsPerBeat;
        }

        return 0.0;
    }

    private RhythmPhraseSelector.Settings GetPhraseSettings(
        double secondsPerBeat)
    {
        return new RhythmPhraseSelector.Settings
        {
            FirstBeatTimeSeconds = beatClock.FirstBeatTimeSeconds,
            SecondsPerBeat = secondsPerBeat,
            EarliestEventTime =
                Math.Max(firstEventTimeSeconds, ApproachSeconds),
            EndSafetySeconds = endSafetySeconds,
            InputSafetySeconds =
                GetPatternSafetySeconds(secondsPerBeat),

            BeatSubdivisions = beatSubdivisions,
            MinimumEventGapBeats = minimumEventGapBeats,
            MaxSnapDistanceBeats = maxSnapDistanceBeats,
            ConnectionGapBeats = rhythmConnectionGapBeats,
            AccentRatio = phraseAccentRatio,
            RhythmWeight = phraseRhythmWeight,

            EnableRepeatedMatching = enableRepeatedPhraseMatching,
            RepeatedSimilarity = repeatedPhraseSimilarity,
            RepeatedBonus = repeatedPhraseBonus,
            HistoryLimit = phraseHistoryLimit
        };
    }

    private void PrepareReadyPhrases(
        double clipDuration,
        double secondsPerBeat,
        double songTime)
    {
        int gridsPerPhrase = phraseBeats * beatSubdivisions;
        double phraseSeconds = phraseBeats * secondsPerBeat;

        double requestHorizon =
            Math.Max(0.0, songTime) + ApproachSeconds + phraseSeconds;

        RhythmPhraseSelector.Settings settings =
            GetPhraseSettings(secondsPerBeat);

        while (!allPhrasesPrepared)
        {
            int firstGrid = nextPhraseIndex * gridsPerPhrase;
            int endGrid = firstGrid + gridsPerPhrase;

            double phraseStart = beatClock.GetBeatTimeSeconds(
                (double)firstGrid / beatSubdivisions);

            if (phraseStart >= clipDuration)
            {
                allPhrasesPrepared = true;
                break;
            }

            double phraseEnd = Math.Min(
                beatClock.GetBeatTimeSeconds(
                    (double)endGrid / beatSubdivisions),
                clipDuration);

            double requiredAnalysisTime = Math.Min(
                clipDuration,
                phraseEnd +
                secondsPerBeat / (2.0 * beatSubdivisions) +
                audioAnalyzer.AnalysisHopSeconds * 2.0);

            bool dataReady =
                audioAnalyzer.IsAnalysisComplete ||
                audioAnalyzer.AnalyzedUntilSeconds +
                TimeToleranceSeconds >= requiredAnalysisTime;

            if (!dataReady)
            {
                if (nextPhraseIndex == 0 || phraseStart <= requestHorizon)
                {
                    audioAnalyzer.RequestAnalysisThrough(requiredAnalysisTime);
                }

                break;
            }

            int previousSelectedCount =
                phraseSelector.SelectedCandidates.Count;

            phraseSelector.Select(
                candidateExtractor.Candidates,
                settings,
                firstGrid,
                endGrid,
                clipDuration);

            int selectedCount =
                phraseSelector.SelectedCandidates.Count -
                previousSelectedCount;

            Debug.Log(
                $"[구간 후보 선택] 구간 {nextPhraseIndex + 1} / " +
                $"박자 {(double)firstGrid / beatSubdivisions:F1}" +
                $"~{(double)endGrid / beatSubdivisions:F1} / " +
                $"칸별 후보 {phraseSelector.LastPhraseCandidateCount}개 / " +
                $"선택 {selectedCount}개",
                this);

            nextPhraseIndex++;
        }
    }

    private void GenerateReadyNotes(
        double clipDuration,
        double secondsPerBeat,
        double songTime)
    {
        IReadOnlyList<RhythmPhraseSelector.SelectedCandidate> candidates =
            phraseSelector.SelectedCandidates;

        double earliestEventTime =
            Math.Max(firstEventTimeSeconds, ApproachSeconds);

        double minimumEventGapSeconds =
            secondsPerBeat * minimumEventGapBeats;

        double handReleaseGapSeconds = Math.Max(
            minimumEventGapSeconds,
                GetPatternSafetySeconds(secondsPerBeat));

        double maxSnapSeconds =
            secondsPerBeat * maxSnapDistanceBeats;

        double firstPhraseEndTime =
            beatClock.GetBeatTimeSeconds((double)phraseBeats);

        double generationHorizon = Math.Max(
            firstPhraseEndTime,
            Math.Max(0.0, songTime) +
            ApproachSeconds +
            phraseBeats * secondsPerBeat);

        while (nextCandidateIndex < candidates.Count)
        {
            int processingCandidateIndex = nextCandidateIndex;

            RhythmPhraseSelector.SelectedCandidate candidate =
                candidates[processingCandidateIndex];

            if (candidate.Time > generationHorizon)
            {
                break;
            }

            nextCandidateIndex++;

            int gridIndex = GetGridIndex(candidate.Time, secondsPerBeat);

            if (gridIndex < 0 || gridIndex <= lastSelectedGridIndex)
            {
                gapRejectedByBeat++;
                continue;
            }

            double eventTime = beatClock.GetBeatTimeSeconds(
                (double)gridIndex / beatSubdivisions);

            double snapDistance = Math.Abs(candidate.Time - eventTime);

            if (snapDistance > maxSnapSeconds + TimeToleranceSeconds)
            {
                gapRejectedBySnap++;
                continue;
            }

            if (eventTime < earliestEventTime ||
                eventTime + endSafetySeconds > clipDuration)
            {
                gapRejectedByTime++;
                continue;
            }

            double requiredGap = Math.Min(
                candidate.MinimumGap,
                lastGeneratedMinimumGap);

            if (eventTime - lastEventStartTime +
                TimeToleranceSeconds < requiredGap)
            {
                gapRejectedBySpacing++;
                continue;
            }

            if (!TryCreateNote(
                    candidate,
                    eventTime,
                    secondsPerBeat,
                    clipDuration,
                    handReleaseGapSeconds,
                    out RhythmNoteData note))
            {
                // Hold の追加分析を待つ候補は消費しない。
                nextCandidateIndex = processingCandidateIndex;
                break;
            }

            LogGapDiagnostics(note, requiredGap, maxSnapSeconds);
            notes.Add(note);

            lastEventStartTime = note.TargetTimeSeconds;
            lastGeneratedMinimumGap = candidate.MinimumGap;
            lastSelectedGridIndex = gridIndex;

            if (note.Kind == ForestNoteKind.Sync)
            {
                lastSyncTargetTime = note.TargetTimeSeconds;
            }

            if (note.IsHold)
            {
                lastGeneratedHold = note;
            }

            LogGeneratedNote(note, candidate, gridIndex, secondsPerBeat);
        }
    }

    private bool TryCreateNote(
        RhythmPhraseSelector.SelectedCandidate candidate,
        double eventTime,
        double secondsPerBeat,
        double clipDuration,
        double handReleaseGapSeconds,
        out RhythmNoteData note)
    {
        note = null;

        int noteId = notes.Count;

        RhythmInputSide side = noteId % 2 == 0
            ? RhythmInputSide.Left
            : RhythmInputSide.Right;

        bool hasReservedHold =
            lastGeneratedHold != null &&
            eventTime - lastGeneratedHold.EndTimeSeconds +
            TimeToleranceSeconds < handReleaseGapSeconds;

        if (hasReservedHold)
        {
            side = lastGeneratedHold.Side == RhythmInputSide.Left
                ? RhythmInputSide.Right
                : RhythmInputSide.Left;
        }

        double minimumSyncGapSeconds =
            secondsPerBeat * minimumSyncGapBeats;

        bool makeSync =
            !hasReservedHold &&
            enableSyncNotes &&
            candidate.IsAccent &&
            candidate.Strength >= minimumSyncStrength &&
            eventTime - lastSyncTargetTime + TimeToleranceSeconds >=
            minimumSyncGapSeconds;

        ForestNoteKind kind = ForestNoteKind.Tap;
        double endTime = eventTime;

        if (makeSync)
        {
            kind = ForestNoteKind.Sync;
            side = RhythmInputSide.Both;
        }
        else if (!hasReservedHold &&
                 enableHoldNotes &&
                 HasEnoughRestSinceLastHold(eventTime, secondsPerBeat))
        {
            RhythmHoldEvaluator.Decision decision = EvaluateHold(
                candidate.Time,
                eventTime,
                secondsPerBeat,
                clipDuration,
                out double calculatedHoldEndTime);

            if (decision == RhythmHoldEvaluator.Decision.WaitingForAnalysis)
            {
                return false;
            }

            if (decision == RhythmHoldEvaluator.Decision.Hold)
            {
                kind = ForestNoteKind.Hold;
                endTime = calculatedHoldEndTime;
            }
        }

        note = new RhythmNoteData(
            id: noteId,
            kind: kind,
            side: side,
            targetTimeSeconds: eventTime,
            endTimeSeconds: endTime);

        return true;
    }

    private RhythmHoldEvaluator.Decision EvaluateHold(
        double candidateTime,
        double startTime,
        double secondsPerBeat,
        double clipDuration,
        out double holdEndTime)
    {
        RhythmHoldEvaluator.Settings settings =
            new RhythmHoldEvaluator.Settings
            {
                MinimumBeats = minimumHoldBeats,
                MaximumBeats = maximumHoldBeats,
                SustainRatio = holdSustainRatio,
                QuietSeconds = holdQuietSeconds,
                MinimumSustainCoverage = minimumHoldSustainCoverage,
                SilenceRms = rhythmSilenceRms,
                EndSafetySeconds = endSafetySeconds
            };

        RhythmHoldEvaluator.Decision decision =
            RhythmHoldEvaluator.Evaluate(
                audioAnalyzer.WindowRmsValues,
                audioAnalyzer.AnalysisHopSeconds,
                audioAnalyzer.IsAnalysisComplete,
                audioAnalyzer.AnalyzedUntilSeconds,
                candidateTime,
                startTime,
                secondsPerBeat,
                clipDuration,
                settings,
                out holdEndTime,
                out double requiredAnalysisTime);

        if (decision == RhythmHoldEvaluator.Decision.WaitingForAnalysis)
        {
            audioAnalyzer.RequestAnalysisThrough(requiredAnalysisTime);
        }

        return decision;
    }

    private bool HasEnoughRestSinceLastHold(
        double startTime,
        double secondsPerBeat)
    {
        if (lastGeneratedHold == null)
        {
            return true;
        }

        double requiredRestSeconds =
            minimumHoldRestBeats * secondsPerBeat;

        return startTime - lastGeneratedHold.EndTimeSeconds +
               TimeToleranceSeconds >= requiredRestSeconds;
    }

    private int GetGridIndex(double candidateTime, double secondsPerBeat)
    {
        double beatPosition =
            (candidateTime - beatClock.FirstBeatTimeSeconds) /
            secondsPerBeat;

        return (int)Math.Floor(
            beatPosition * beatSubdivisions + 0.5);
    }

    private void RefreshInitialPatternReady(double secondsPerBeat)
    {
        if (IsInitialPatternReady)
        {
            return;
        }

        IReadOnlyList<RhythmPhraseSelector.SelectedCandidate> candidates =
            phraseSelector.SelectedCandidates;

        double firstPhraseEndTime =
            beatClock.GetBeatTimeSeconds((double)phraseBeats);

        bool firstPhraseCandidatesProcessed =
            nextCandidateIndex >= candidates.Count;

        if (!firstPhraseCandidatesProcessed)
        {
            int gridIndex = GetGridIndex(
                candidates[nextCandidateIndex].Time,
                secondsPerBeat);

            double targetTime = beatClock.GetBeatTimeSeconds(
                (double)gridIndex / beatSubdivisions);

            firstPhraseCandidatesProcessed =
                targetTime >= firstPhraseEndTime;
        }

        if ((nextPhraseIndex > 0 || allPhrasesPrepared) &&
            firstPhraseCandidatesProcessed)
        {
            IsInitialPatternReady = true;

            Debug.Log(
                $"[초기 패턴 준비 완료]\n" +
                $"BPM: {beatClock.Bpm:F2}\n" +
                $"노트 최소 간격: {minimumEventGapBeats}박 " +
                $"({minimumEventGapBeats * secondsPerBeat:F3}초)\n" +
                $"선택한 이벤트: {notes.Count}개",
                this);
        }
    }

    private void LogGeneratedNote(
        RhythmNoteData note,
        RhythmPhraseSelector.SelectedCandidate candidate,
        int gridIndex,
        double secondsPerBeat)
    {
        int patternNumber = note.Id / eventsPerPattern + 1;
        int eventNumberInPattern = note.Id % eventsPerPattern + 1;

        Debug.Log(
            $"[박자 이벤트 생성] " +
            $"노트 {note.Id + 1} / " +
            $"종류 {note.Kind} / " +
            $"유지 {note.DurationSeconds / secondsPerBeat:F1}박 / " +
            $"강세 {candidate.Strength:F2} / " +
            $"패턴 {patternNumber} / " +
            $"{eventNumberInPattern}/{eventsPerPattern} / " +
            $"시작 {note.TargetTimeSeconds:F3}s / " +
            $"종료 {note.EndTimeSeconds:F3}s / " +
            $"유지 {note.DurationSeconds:F3}s",
            this);

        Debug.Log(
            $"[박자 이벤트 생성] " +
            $"패턴 {patternNumber} / " +
            $"{eventNumberInPattern}/{eventsPerPattern} / " +
            $"후보 {candidate.Time:F3}s → 목표 {note.TargetTimeSeconds:F3}s / " +
            $"박자 위치 {(double)gridIndex / beatSubdivisions:F2} / " +
            $"박자 분할 {beatSubdivisions}",
            this);
    }

    private void LogGapDiagnostics(
        RhythmNoteData nextNote,
        double requiredGap,
        double maxSnapSeconds)
    {
        if (logLongNoteGaps && notes.Count > 0)
        {
            RhythmNoteData previousNote = notes[notes.Count - 1];

            double gapSeconds =
                nextNote.TargetTimeSeconds -
                previousNote.EndTimeSeconds;

            if (gapSeconds >= diagnosticGapSeconds)
            {
                int rejectedCount =
                    gapRejectedByBeat +
                    gapRejectedBySnap +
                    gapRejectedByTime +
                    gapRejectedBySpacing;

                Debug.Log(
                    $"[노트 공백 진단]\n" +
                    $"노트 {previousNote.Id + 1} ({previousNote.Kind}) 종료 " +
                    $"{previousNote.EndTimeSeconds:F3}s → " +
                    $"노트 {nextNote.Id + 1} ({nextNote.Kind}) 시작 " +
                    $"{nextNote.TargetTimeSeconds:F3}s\n" +
                    $"입력 공백: {gapSeconds:F3}초\n" +
                    $"앞 노트 선택 이후 제외 후보: {rejectedCount}개 " +
                    $"(이번 채택 후보는 별도 1개)\n" +
                    $"박자 번호·중복 제외: {gapRejectedByBeat}개\n" +
                    $"스냅 거리 제외: {gapRejectedBySnap}개\n" +
                    $"곡 시작·종료 여유 제외: {gapRejectedByTime}개\n" +
                    $"노트 간격 제외: {gapRejectedBySpacing}개\n" +
                    $"적용 BPM: {beatClock.Bpm:F3} / " +
                    $"첫 박자: {beatClock.FirstBeatTimeSeconds:F3}s\n" +
                    $"스냅 허용: {maxSnapSeconds * 1000.0:F1}ms / " +
                    $"이번 필요 간격: {requiredGap:F3}초",
                    this);
            }
        }

        ResetGapDiagnosticCounts();
    }

    private void ResetGapDiagnosticCounts()
    {
        gapRejectedByBeat = 0;
        gapRejectedBySnap = 0;
        gapRejectedByTime = 0;
        gapRejectedBySpacing = 0;
    }

    private void FailGeneration(string message)
    {
        hasGenerationFailed = true;
        IsInitialPatternReady = false;

        Debug.LogError($"[패턴 준비 실패] {message}", this);
        enabled = false;
    }

    private double GetPatternSafetySeconds(double secondsPerBeat)
    {
        // 기존 생성 패턴을 유지하기 위해 BPM에 따른 간격 보정도 보존한다.
        float beatScale = (float)Math.Max(
            0.8,
            Math.Min(1.4, secondsPerBeat / 0.5));

        return PatternSpacingReferenceMs * beatScale
            * 2.0 / 1000.0 + 0.001;
    }
}