using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// BPM 분석 진행 상태와 선택한 후보를 표시한다.
public class BpmAnalysisDebugUI : MonoBehaviour
{
    [SerializeField]
    private AudioBpmAnalyzer bpmAnalyzer;

    [SerializeField]
    private MusicBeatSelection beatSelection;

    [SerializeField]
    private TMP_Text candidateText;

    [SerializeField]
    private Button nextCandidateButton;

    private const float RefreshIntervalSeconds = 0.1f;
    private float nextRefreshTime;

    private void Awake()
    {
        if (bpmAnalyzer == null ||
            beatSelection == null ||
            candidateText == null ||
            nextCandidateButton == null)
        {
            Debug.LogError(
                "BPM 분석기, 박자 선택, 후보 글자와 " +
                "후보 변경 버튼을 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        nextCandidateButton.interactable = false;
    }

    private void Start()
    {
        RefreshUI();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime =
            Time.unscaledTime + RefreshIntervalSeconds;

        RefreshUI();
    }

    // 기존 버튼의 On Click 연결을 그대로 사용한다.
    public void OnClickNextCandidate()
    {
        if (!Application.isPlaying ||
            !isActiveAndEnabled)
        {
            return;
        }

        if (!beatSelection.TrySelectNextCandidate())
        {
            return;
        }

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        RefreshUI();
    }

    private void RefreshUI()
    {
        nextCandidateButton.interactable = false;

        if (!string.IsNullOrEmpty(bpmAnalyzer.FailureReason))
        {
            candidateText.text =
                "박자 분석을 완료하지 못했습니다.\n" +
                bpmAnalyzer.FailureReason +
                "\n‘음원 다시 선택’을 눌러 다른 곡을 선택해 주세요.";

            return;
        }

        if (!bpmAnalyzer.IsComplete &&
            !bpmAnalyzer.isActiveAndEnabled)
        {
            candidateText.text =
                "박자 분석기가 비활성화되어 있습니다.";

            return;
        }

        if (!beatSelection.isActiveAndEnabled)
        {
            candidateText.text =
                "박자 선택 컴포넌트가 비활성화되어 있습니다.";

            return;
        }

        if (!bpmAnalyzer.HasStarted)
        {
            candidateText.text = "음원 분석 대기 중";
            return;
        }

        if (!bpmAnalyzer.IsComplete)
        {
            float percent =
                bpmAnalyzer.AnalysisProgress * 100.0f;

            candidateText.text =
                $"박자 분석 중 · {percent:F0}%";

            return;
        }

        int count = beatSelection.CandidateCount;

        if (count == 0)
        {
            candidateText.text =
                "사용할 수 있는 박자 후보가 없습니다.\n" +
                "‘음원 다시 선택’을 눌러 다른 곡을 선택해 주세요.";

            return;
        }

        int selectedIndex =
            beatSelection.SelectedCandidateIndex;

        BpmConsensusCalculator.Result selected =
            bpmAnalyzer.ConsensusCandidates[selectedIndex];

        candidateText.text =
            $"<color=#67C5FF>{selected.Bpm:F1} BPM</color>\n" +
            $"후보 {selectedIndex + 1} / {count}";

        nextCandidateButton.interactable =
            beatSelection.CanSelectNextCandidate;
    }
}