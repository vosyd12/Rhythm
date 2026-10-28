using System;
using System.Collections.Generic;
using UnityEngine;

// 음원 일부 구간의 소리 변화에서 BPM 후보를 추정한다.
// 이번 단계에서는 후보만 출력하며 재생 BPM을 변경하지 않는다.
public class AudioBpmAnalyzer : MonoBehaviour
{
    // 실제 로드된 음원을 가져오기 위한 참조.
    [SerializeField]
    private AudioSource musicSource;

    // 기존 분석기의 초기 준비가 완료된 뒤 분석을 시작한다.
    [SerializeField]
    private AudioAnalysisTester audioAnalyzer;

    // 음원의 어느 시점부터 분석할지 지정한다.
    [SerializeField, Min(0.0f)]
    private float analysisStartSeconds = 0.0f;

    // 이번에는 최대 60초까지만 분석하도록 제한한다.
    [SerializeField, Range(5.0f, 60.0f)]
    private float analysisDurationSeconds = 30.0f;

    [SerializeField, Range(40.0f, 240.0f)]
    private float minimumBpm = 60.0f;

    [SerializeField, Range(40.0f, 240.0f)]
    private float maximumBpm = 200.0f;

    // 한 프레임에서 처리할 작은 분석 구간의 개수.
    [SerializeField, Min(1)]
    private int windowsPerFrame = 4;

    // 분석할 최대 구간 수.
    // 짧은 곡에서는 실제 구간 수가 줄어들 수 있다.
    [SerializeField, Range(1, 5)]
    private int maximumSegmentCount = 3;

    // 같은 BPM 그룹으로 묶을 상대 차이.
    // 0.02는 2%이며, 2 BPM을 의미하는 것이 아니다.
    [SerializeField, Range(0.001f, 0.05f)]
    private float candidateMergeTolerance = 0.02f;

    // 한 구간에서 비교에 사용할 상위 후보 개수.
    private const int CandidatesPerSegment = 3;

    // 각 분석 구간의 시작 샘플 프레임.
    private int[] segmentStartFrames;

    // 모든 구간에서 공통으로 사용할 길이.
    private int segmentFrameCount;

    // 현재 처리 중인 구간 번호. 내부에서는 0부터 시작한다.
    private int currentSegmentIndex;

    // 완료된 구간에서 모은 후보 기록.
    private readonly List<BpmConsensusCalculator.Observation>
        segmentObservations =
            new List<BpmConsensusCalculator.Observation>();

    // 전체 구간 비교 결과.
    private readonly List<BpmConsensusCalculator.Result>
        consensusCandidates =
            new List<BpmConsensusCalculator.Result>();

    public IReadOnlyList<BpmConsensusCalculator.Result>
        ConsensusCandidates => consensusCandidates;

    // FFT가 한 번에 사용할 샘플 프레임 수.
    // 2의 거듭제곱만 사용할 수 있다.
    [SerializeField]
    private int fftSize = 2048;

    // 주변 평균을 계산할 앞뒤 시간 범위.
    // 0.1이면 현재 위치 기준 앞 0.1초, 뒤 0.1초를 사용한다.
    [SerializeField, Range(0.01f, 0.5f)]
    private float localAverageRadiusSeconds = 0.1f;

    // 이 값보다 반복 점수가 낮은 후보는 제외한다.
    // 정답 확률이 아니라 반복성이 충분한지 보는 임시 기준이다.
    [SerializeField, Range(0.0f, 1.0f)]
    private float minimumPeriodicityScore = 0.2f;

    // 분석 창을 다음 위치로 이동할 시간 간격.
    // FFT 창 전체의 길이와는 다른 값이다.
    private const double HopSeconds = 0.01;

    private AudioClip clip;
    private bool hasStarted;

    private int startFrame;

    // 다음 분석 창으로 이동할 샘플 프레임 수.
    private int hopFrames;

    private int windowCount;
    private int nextWindowIndex;

    private int minimumLag;
    private int maximumLag;

    // 정수 샘플 프레임으로 반올림한 실제 분석 간격.
    private double actualHopSeconds;

    // FFT 창 하나의 모든 채널 샘플을 담는다.
    private float[] sampleBuffer;

    // 각 분석 위치에서 계산한 주파수 변화량.
    private double[] onsetStrengths;

    private SpectralFluxExtractor fluxExtractor;

    private readonly List<BpmPeriodicityEstimator.Candidate> candidates =
        new List<BpmPeriodicityEstimator.Candidate>();

    // 분석 처리가 끝났다는 뜻이며,
    // 반드시 유효한 후보를 찾았다는 뜻은 아니다.
    public bool IsComplete { get; private set; }

    // 모든 구간 비교가 끝나고 최종 후보가 있을 때만 true.
    public bool HasEstimate =>
        IsComplete && consensusCandidates.Count > 0;

    // 실제 음원을 대상으로 분석을 시작했는지 확인한다.
    public bool HasStarted => hasStarted;

    // 이번 음원에서 분석할 전체 구간 수.
    public int TotalSegmentCount =>
        segmentStartFrames != null
            ? segmentStartFrames.Length
            : 0;

    // 처리를 끝낸 구간 수.
    // currentSegmentIndex는 구간 완료 시 증가하므로 완료 개수로도 사용한다.
    public int CompletedSegmentCount =>
        Math.Min(currentSegmentIndex, TotalSegmentCount);

    // 화면에는 0번 대신 1번 구간부터 표시한다.
    // 분석 전이나 전체 완료 상태에서는 현재 구간이 없으므로 0을 반환한다.
    public int CurrentSegmentNumber =>
        !hasStarted || IsComplete
            ? 0
            : Math.Min(
                currentSegmentIndex + 1,
                TotalSegmentCount);

    // 설정 오류나 음원 읽기 실패로 전체 분석을 중단한 이유.
    // 후보를 찾지 못한 것과 처리 자체가 중단된 것을 구분한다.
    public string FailureReason { get; private set; } = string.Empty;

    // 박자 위치를 추정할 때 사용할 구간별 소리 변화량.
    private readonly List<RhythmOnsetSegment> onsetSegments =
        new List<RhythmOnsetSegment>();

    public IReadOnlyList<RhythmOnsetSegment> OnsetSegments =>
        onsetSegments;

    // FFT 분석 창 처리량을 기준으로 계산한 진행률.
    // 남은 시간을 예측하는 값은 아니다.
    public float AnalysisProgress
    {
        get
        {
            int total = TotalSegmentCount;

            if (total == 0)
            {
                return 0.0f;
            }

            float currentProgress = 0.0f;

            if (currentSegmentIndex < total && windowCount > 0)
            {
                // 현재 구간에서 처리한 창 개수 / 전체 창 개수.
                currentProgress = Mathf.Clamp01(
                    (float)nextWindowIndex / windowCount);
            }

            // 전체 진행률:
            // (완료 구간 수 + 현재 구간 진행률) / 전체 구간 수.
            //
            // 예: 총 3구간, 1구간 완료, 현재 구간 절반 처리
            // (1 + 0.5) / 3 = 0.5 → 50%.
            return Mathf.Clamp01(
                (CompletedSegmentCount + currentProgress) / total);
        }
    }

    private void Awake()
    {
        if (musicSource == null || audioAnalyzer == null)
        {
            FinishWithoutEstimate(
                "Music Source와 Audio Analyzer를 연결해 주세요.");
            return;
        }

        bool validFftSize =
            fftSize >= 128 &&
            fftSize <= 8192 &&
            (fftSize & (fftSize - 1)) == 0;

        bool validSettings =
            analysisStartSeconds >= 0.0f &&
            !float.IsInfinity(analysisStartSeconds) &&
            analysisDurationSeconds >= 5.0f &&
            analysisDurationSeconds <= 60.0f &&
            minimumBpm >= 40.0f &&
            maximumBpm <= 240.0f &&
            maximumBpm > minimumBpm &&
            windowsPerFrame >= 1 &&
            minimumPeriodicityScore >= 0.0f &&
            minimumPeriodicityScore <= 1.0f &&
            localAverageRadiusSeconds >= 0.01f &&
            localAverageRadiusSeconds <= 0.5f &&
            maximumSegmentCount >= 1 &&
            maximumSegmentCount <= 5 &&
            candidateMergeTolerance >= 0.001f &&
            candidateMergeTolerance <= 0.05f;

        if (!validFftSize || !validSettings)
        {
            FinishWithoutEstimate(
                "BPM 분석 설정값을 확인해 주세요. " +
                "FFT Size는 128~8192 사이의 2의 거듭제곱이어야 합니다.");
        }
    }

    private void Update()
    {
        if (IsComplete) { return; }

        if (!hasStarted)
        {
            if (audioAnalyzer.HasAnalysisFailed)
            {
                FinishWithoutEstimate(
                    "기존 음원 분석이 실패하여 BPM 분석을 시작하지 못했습니다.");

                return;
            }

            // 외부 음원이 로드되고 초기 분석까지 준비되기를 기다린다.
            if (!audioAnalyzer.IsInitialAnalysisReady) { return; }

            BeginAnalysis();
            return;
        }

        if (musicSource.clip != clip)
        {
            FinishWithoutEstimate("분석 도중 음원이 변경되었습니다.");
            return;
        }

        ProcessWindows();

        if (!IsComplete && nextWindowIndex >= windowCount)
        {
            EstimateCandidates();
        }
    }

    private void BeginAnalysis()
    {
        // 새 분석을 시작할 때 이전 중단 이유를 제거한다.
        FailureReason = string.Empty;

        // 새로운 음원 분석에 이전 구간 기록이 섞이지 않도록 한다.
        onsetSegments.Clear();

        clip = musicSource.clip;

        if (clip == null ||
            clip.loadState != AudioDataLoadState.Loaded ||
            clip.samples <= 0 ||
            clip.frequency <= 0 ||
            clip.channels <= 0)
        {
            FinishWithoutEstimate(
                "읽을 음원 데이터가 준비되지 않았습니다.");

            return;
        }

        double clipDuration =
            (double)clip.samples / clip.frequency;

        if (analysisStartSeconds >= clipDuration)
        {
            FinishWithoutEstimate(
                "분석 시작 시각이 음원 길이보다 크거나 같습니다.");

            return;
        }

        hopFrames = Math.Max(
            1,
            (int)Math.Round(clip.frequency * HopSeconds));

        actualHopSeconds =
            (double)hopFrames / clip.frequency;

        if (hopFrames > fftSize)
        {
            FinishWithoutEstimate(
                "분석 간격보다 FFT 창이 작습니다. FFT Size를 늘려 주세요.");

            return;
        }

        minimumLag = Math.Max(
            1,
            (int)Math.Ceiling(
                60.0 / (maximumBpm * actualHopSeconds)));

        maximumLag = (int)Math.Floor(
            60.0 / (minimumBpm * actualHopSeconds));

        if (maximumLag < minimumLag)
        {
            FinishWithoutEstimate("BPM 범위를 확인해 주세요.");
            return;
        }

        int firstFrame = (int)(
            (double)analysisStartSeconds * clip.frequency);

        int availableFrames = clip.samples - firstFrame;

        int requestedFrames = (int)(
            (double)analysisDurationSeconds * clip.frequency);

        // 가장 느린 후보도 최소 네 주기를 비교할 수 있게 한다.
        int minimumWindowCount = maximumLag * 4;

        // 창 개수가 K개일 때 필요한 샘플 길이:
        // (K - 1) × 이동 간격 + FFT 창 길이.
        int minimumRequiredFrames =
            (minimumWindowCount - 1) * hopFrames + fftSize;

        // 지나치게 짧은 구간을 많이 나누지 않도록
        // 이번 단계에서는 구간당 최소 5초도 요구한다.
        minimumRequiredFrames = Math.Max(
            minimumRequiredFrames,
            clip.frequency * 5);

        if (requestedFrames < minimumRequiredFrames)
        {
            FinishWithoutEstimate(
                "Analysis Duration Seconds를 늘려 주세요. " +
                "현재 BPM 범위를 비교하기에는 구간이 너무 짧습니다.");

            return;
        }

        // 겹치지 않는 구간을 몇 개 확보할 수 있는지 계산한다.
        int segmentCount = Math.Min(
            maximumSegmentCount,
            availableFrames / minimumRequiredFrames);

        if (segmentCount < 1)
        {
            FinishWithoutEstimate(
                "분석 시작 위치 뒤에 충분한 음원이 남아 있지 않습니다.");

            return;
        }

        // 짧은 곡은 구간 길이를 줄여 여러 부분을 비교한다.
        // 전체 길이 / 구간 수 이하이므로 서로 겹치지 않게 배치할 수 있다.
        segmentFrameCount = Math.Min(
            requestedFrames,
            availableFrames / segmentCount);

        segmentStartFrames = new int[segmentCount];

        // 첫 구간 시작점부터 마지막 구간 시작점까지 이동할 수 있는 거리.
        int startRange = availableFrames - segmentFrameCount;

        for (int i = 0; i < segmentCount; i++)
        {
            // 구간 하나라면 지정한 시작 위치를 그대로 사용한다.
            // 여러 개라면 시작 위치들을 전체 범위에 균등하게 배치한다.
            int offset = segmentCount == 1
                ? 0
                : (int)Math.Round(
                    (double)startRange * i / (segmentCount - 1));

            segmentStartFrames[i] = firstFrame + offset;
        }

        sampleBuffer =
            new float[fftSize * clip.channels];

        candidates.Clear();
        segmentObservations.Clear();
        consensusCandidates.Clear();

        currentSegmentIndex = 0;
        hasStarted = true;

        Debug.Log(
            $"[다중 구간 BPM 분석 시작] " +
            $"구간 {segmentCount}개 / " +
            $"구간당 {(double)segmentFrameCount / clip.frequency:F2}s",
            this);

        BeginCurrentSegment();
    }

    // currentSegmentIndex가 가리키는 분석 구간을 준비한다.
    private void BeginCurrentSegment()
    {
        // 이번 구간이 시작되는 음원 샘플 프레임 위치.
        startFrame =
            segmentStartFrames[currentSegmentIndex];

        // 이번 구간에서 FFT 창을 몇 번 읽을지 계산한다.

        // 첫 창은 fftSize만큼 필요하고,
        // 이후에는 hopFrames씩 이동하며 다음 창을 읽는다.
        // 마지막 창이 구간 끝을 넘지 않는 개수만 사용한다.
        windowCount =
            1 + (segmentFrameCount - fftSize) / hopFrames;

        // 새 구간의 첫 번째 분석 창부터 시작한다.
        nextWindowIndex = 0;

        // 직전 구간의 후보를 제거한다.
        // 이미 완료한 구간의 기록은 segmentObservations에 따로 보관되어 있다.
        candidates.Clear();

        // 이번 구간에서 추출할 소리 변화량을 저장할 공간.
        onsetStrengths = new double[windowCount];

        // 서로 떨어진 구간을 연속된 소리처럼 비교하면 안 된다.
        // 이전 구간의 주파수 기록을 이어받지 않도록 새로 생성한다.
        fluxExtractor =
            new SpectralFluxExtractor(fftSize, clip.channels);

        // 샘플 프레임 위치 ÷ 초당 샘플 프레임 수 = 초 단위 시각.
        double startSeconds =
            (double)startFrame / clip.frequency;

        double endSeconds =
            (double)(startFrame + segmentFrameCount) / clip.frequency;

        Debug.Log(
            $"[BPM 구간 시작] " +
            $"{currentSegmentIndex + 1}/{segmentStartFrames.Length} / " +
            $"{startSeconds:F2}~{endSeconds:F2}s",
            this);
    }

    private void ProcessWindows()
    {
        int processed = 0;

        while (nextWindowIndex < windowCount &&
               processed < windowsPerFrame)
        {
            int sampleFrame =
                startFrame + nextWindowIndex * hopFrames;

            // 시작 위치는 샘플 프레임 단위다.
            // 채널 수는 배열 길이에만 반영한다.
            if (!clip.GetData(sampleBuffer, sampleFrame))
            {
                FinishWithoutEstimate(
                    "음원 샘플을 읽지 못했습니다.");

                return;
            }

            // 기존 RMS 증가량 대신 주파수별 증가량을 기록한다.
            onsetStrengths[nextWindowIndex] =
                fluxExtractor.GetFlux(sampleBuffer);

            nextWindowIndex++;
            processed++;
        }
    }

    private void EstimateCandidates()
    {
        bool hasEnvelope =
            BpmPeriodicityEstimator.PrepareOnsetEnvelope(
                onsetStrengths,
                actualHopSeconds,
                localAverageRadiusSeconds);

        if (!hasEnvelope)
        {
            CompleteCurrentSegment(
                "두드러진 주파수 변화를 찾지 못했습니다.");

            return;
        }

        // FFT 창의 중심을 변화량의 대표 시각으로 사용한다.
        double firstSampleTimeSeconds =
            (startFrame + (fftSize - 1) * 0.5) /
            clip.frequency;

        // 다음 계산에서 원본 배열의 평균을 빼기 전에 저장한다.
        // RhythmOnsetSegment 생성자가 배열을 복사한다.
        onsetSegments.Add(
            new RhythmOnsetSegment(
                firstSampleTimeSeconds,
                actualHopSeconds,
                onsetStrengths));

        bool hasVariation =
            BpmPeriodicityEstimator.TryEstimateCandidates(
                onsetStrengths,
                minimumLag,
                maximumLag,
                actualHopSeconds,
                minimumPeriodicityScore,
                candidates,
                out double highestScore,
                out double diagnosticBpm);

        if (!hasVariation)
        {
            CompleteCurrentSegment(
                "반복성을 비교할 변화량이 너무 작습니다.");

            return;
        }

        Debug.Log(
            $"[BPM 분석 진단] " +
            $"최고 반복 점수: {highestScore:F4} / " +
            $"통과 기준: {minimumPeriodicityScore:F4} / " +
            $"해당 간격의 BPM: {diagnosticBpm:F2}",
            this);

        // 점수 기준을 통과한 후보가 없어도 다음 구간은 계속 분석한다.
        CompleteCurrentSegment();
    }

    private void FinishWithoutEstimate(string reason)
    {
        // UI에서도 중단 이유를 표시할 수 있도록 기록한다.
        FailureReason = reason;

        // 파일 읽기 실패, 잘못된 설정 등으로
        // 전체 분석을 중단할 때 사용하는 함수다.

        // 특정 구간에 후보가 없는 경우는
        // CompleteCurrentSegment를 통해 다음 구간으로 진행한다.
        candidates.Clear();
        consensusCandidates.Clear();

        IsComplete = true;

        Debug.LogWarning(
            $"[BPM 추정 중단] {reason}",
            this);
    }

    private void CompleteCurrentSegment(string emptyReason = null)
    {
        int segmentNumber = currentSegmentIndex + 1;

        if (emptyReason != null)
        {
            candidates.Clear();
        }

        if (candidates.Count == 0)
        {
            string reason = emptyReason ??
                "반복 점수 기준을 만족하는 후보가 없습니다.";

            // 후보 없음은 이 구간의 분석 결과다.
            // 다른 구간을 분석할 수 있으므로 전체 실패로 처리하지 않는다.
            Debug.Log(
                $"[BPM 구간 완료] {segmentNumber}번 / 후보 없음 / {reason}",
                this);
        }
        else
        {
            int selectedCount = Math.Min(
                CandidatesPerSegment,
                candidates.Count);

            for (int i = 0; i < selectedCount; i++)
            {
                BpmPeriodicityEstimator.Candidate candidate = candidates[i];

                segmentObservations.Add(
                    new BpmConsensusCalculator.Observation(
                        currentSegmentIndex,
                        candidate.Bpm,
                        candidate.Score));

                Debug.Log(
                    $"[BPM 구간 후보] 구간 {segmentNumber} / " +
                    $"후보 {i + 1} / " +
                    $"{candidate.Bpm:F2} BPM / " +
                    $"반복 점수 {candidate.Score:F3}",
                    this);
            }
        }

        currentSegmentIndex++;

        if (currentSegmentIndex < segmentStartFrames.Length)
        {
            BeginCurrentSegment();
            return;
        }

        CompleteAllSegments();
    }

    private void CompleteAllSegments()
    {
        List<BpmConsensusCalculator.Result> results =
            BpmConsensusCalculator.Calculate(
                segmentObservations,
                candidateMergeTolerance);

        consensusCandidates.Clear();
        consensusCandidates.AddRange(results);

        // 구간별 임시 후보는 비우고,
        // 최종 결과는 consensusCandidates에 보관한다.
        candidates.Clear();
        IsComplete = true;

        if (!HasEstimate)
        {
            Debug.LogWarning(
                "[BPM 종합 완료] 모든 분석 구간에서 " +
                "기준을 만족하는 후보를 찾지 못했습니다.",
                this);

            return;
        }

        int displayCount = Math.Min(
            3,
            consensusCandidates.Count);

        for (int i = 0; i < displayCount; i++)
        {
            BpmConsensusCalculator.Result result =
                consensusCandidates[i];

            Debug.Log(
                $"[BPM 종합 후보 {i + 1}] " +
                $"{result.Bpm:F2} BPM / " +
                $"지지 구간 {result.SupportingSegments}" +
                $"/{segmentStartFrames.Length} / " +
                $"평균 반복 점수 {result.AverageScore:F3}",
                this);
        }

        if (segmentStartFrames.Length == 1)
        {
            Debug.Log(
                "[BPM 비교 상태] 분석 가능한 구간이 하나여서 " +
                "구간 간 비교는 하지 못했습니다.",
                this);
        }
        else if (consensusCandidates[0].SupportingSegments < 2)
        {
            Debug.Log(
                "[BPM 비교 상태] 여러 구간에서 공통으로 나온 후보가 없습니다. " +
                "현재 결과만으로 하나의 BPM을 확정하지 않습니다.",
                this);
        }
    }
}