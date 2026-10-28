using UnityEngine;

// 상위 BPM 후보의 선택 상태와 박자 위치 계산 결과를 관리한다.
// 화면 표시와 실제 음악 재생은 담당하지 않는다.
public class MusicBeatSelection : MonoBehaviour
{
    [SerializeField]
    private AudioBpmAnalyzer bpmAnalyzer;

    private const int MaximumCandidates = 3;

    private BeatPhaseEstimator.Result[] phaseResults;
    private bool[] hasPhase;

    private int calculatedCandidateCount;
    private bool hasAnyValidPhase;

    public int SelectedCandidateIndex { get; private set; }

    public bool IsBpmAnalysisComplete =>
        isActiveAndEnabled &&
        bpmAnalyzer != null &&
        bpmAnalyzer.IsComplete;

    public int CandidateCount =>
        IsBpmAnalysisComplete && bpmAnalyzer.HasEstimate
            ? Mathf.Min(
                MaximumCandidates,
                bpmAnalyzer.ConsensusCandidates.Count)
            : 0;

    public int CalculatedCandidateCount =>
        calculatedCandidateCount;

    // 후보가 없거나 분석 자료가 없는 경우도
    // 더 계산할 수 없는 완료 상태로 구분한다.
    public bool IsPhaseAnalysisComplete
    {
        get
        {
            if (!IsBpmAnalysisComplete)
            {
                return false;
            }

            if (CandidateCount == 0 ||
                bpmAnalyzer.OnsetSegments.Count == 0)
            {
                return true;
            }

            return phaseResults != null &&
                   phaseResults.Length == CandidateCount &&
                   calculatedCandidateCount == phaseResults.Length;
        }
    }

    // 계산 중에는 false다.
    // 모든 후보를 확인했는데 사용할 박자가 없을 때만 true다.
    public bool HasNoUsableBeat =>
        IsPhaseAnalysisComplete &&
        !hasAnyValidPhase;

    public bool CanSelectNextCandidate =>
        IsBpmAnalysisComplete &&
        CandidateCount > 1;

    private void Awake()
    {
        if (bpmAnalyzer == null)
        {
            Debug.LogError(
                "MusicBeatSelection의 BPM Analyzer를 연결해 주세요.",
                this);

            enabled = false;
        }
    }

    private void Update()
    {
        if (!IsBpmAnalysisComplete)
        {
            ResetAnalysis();
            return;
        }

        int count = CandidateCount;

        if (count == 0 ||
            bpmAnalyzer.OnsetSegments.Count == 0)
        {
            return;
        }

        if (phaseResults == null)
        {
            phaseResults = new BeatPhaseEstimator.Result[count];
            hasPhase = new bool[count];
        }

        if (calculatedCandidateCount >= count)
        {
            return;
        }

        // 기존처럼 한 프레임에 후보 하나만 계산한다.
        int index = calculatedCandidateCount;

        double bpm =
            bpmAnalyzer.ConsensusCandidates[index].Bpm;

        bool succeeded = BeatPhaseEstimator.TryEstimate(
            bpmAnalyzer.OnsetSegments,
            bpm,
            out phaseResults[index]);

        hasPhase[index] = succeeded;
        hasAnyValidPhase |= succeeded;

        calculatedCandidateCount++;
    }

    private void ResetAnalysis()
    {
        phaseResults = null;
        hasPhase = null;

        calculatedCandidateCount = 0;
        hasAnyValidPhase = false;
        SelectedCandidateIndex = 0;
    }

    public bool TrySelectNextCandidate()
    {
        if (!Application.isPlaying ||
            !CanSelectNextCandidate)
        {
            return false;
        }

        SelectedCandidateIndex =
            (SelectedCandidateIndex + 1) % CandidateCount;

        return true;
    }

    public bool TryGetSelectedPhase(
        out BeatPhaseEstimator.Result result)
    {
        result = default;

        if (!Application.isPlaying ||
            !IsPhaseAnalysisComplete ||
            !hasAnyValidPhase)
        {
            return false;
        }

        // 선택 인덱스는 이 클래스만 변경한다.
        // 모든 후보의 계산을 마쳤으므로 대응하는 배열이 존재한다.
        if (!hasPhase[SelectedCandidateIndex])
        {
            return false;
        }

        result = phaseResults[SelectedCandidateIndex];
        return true;
    }

    public bool TryGetSelectedBeat(
        out double bpm,
        out double phaseSeconds)
    {
        bpm = 0.0;
        phaseSeconds = 0.0;

        if (!TryGetSelectedPhase(
                out BeatPhaseEstimator.Result result))
        {
            return false;
        }

        // 반드시 같은 후보의 BPM과 기준 시각을 한 쌍으로 반환한다.
        bpm = result.Bpm;
        phaseSeconds = result.PhaseSeconds;

        return true;
    }
}