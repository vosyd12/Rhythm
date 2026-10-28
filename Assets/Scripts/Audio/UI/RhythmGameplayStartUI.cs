using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 선택한 분석 결과로 실제 리듬 플레이를 시작하는 버튼을 담당한다.
public class RhythmGameplayStartUI : MonoBehaviour
{
    [SerializeField]
    private MusicBeatSelection beatSelection;

    [SerializeField]
    private MusicPlayback musicPlayback;

    [SerializeField]
    private Button startGameplayButton;

    [SerializeField]
    private TMP_Text startGameplayButtonText;

    private void Awake()
    {
        if (beatSelection == null ||
            musicPlayback == null ||
            startGameplayButton == null ||
            startGameplayButtonText == null)
        {
            Debug.LogError(
                "박자 선택, 음악 재생기, 시작 버튼과 " +
                "버튼 글자를 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        startGameplayButton.interactable = false;
    }

    private void Update()
    {
        RefreshUI();
    }

    // 시작 버튼의 On Click에 등록한다.
    public void OnClickStartGameplay()
    {
        if (ForestGatheringController.Owns(musicPlayback))
        { ForestGatheringController.Instance.StartPreparedMusic(); return; }
        if (!Application.isPlaying ||
            !isActiveAndEnabled ||
            !musicPlayback.CanStartGameplay)
        {
            return;
        }

        // 현재 선택한 후보의 분석이 완료되어야 시작할 수 있다.
        if (!beatSelection.TryGetSelectedBeat(
           out double bpm,
           out double phaseSeconds))
        {
            Debug.LogWarning(
                "선택한 후보의 박자 분석이 아직 준비되지 않았습니다.",
                this);

            return;
        }

        // 박자 확정 → 패턴 초기화 → 실제 재생 요청을 진행한다.
        if (!musicPlayback.TryStartGameplay(bpm, phaseSeconds))
        {
            RefreshUI();
            return;
        }

        // 버튼 선택이 남아 Space로 다시 실행되는 것을 막는다.
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        RefreshUI();
    }

    private void RefreshUI()
    {
        if (ForestGatheringController.Owns(musicPlayback))
        {
            // 복원된 곡이 이미 재생 중이어도 준비창에서 확인하고 복귀할 수 있다.
            bool ready = ForestGatheringController.Instance.CanStartPreparedMusic;
            startGameplayButton.interactable = ready;
            startGameplayButtonText.text = ready
                ? (musicPlayback.IsGameplayPlayback ? "캠프 조작으로 복귀" : "이 박자로 시작")
                : "박자 준비 중...";
            return;
        }

        bool hasStarted = musicPlayback.IsGameplayPlayback;

        bool canStart =
            !hasStarted &&
            musicPlayback.CanStartGameplay &&
            beatSelection.TryGetSelectedBeat(out _, out _);

        startGameplayButton.interactable = canStart;

        if (hasStarted)
        {
            return;
        }

        startGameplayButtonText.text = canStart
            ? "이 박자로 시작"
            : "박자 준비 중...";
    }
}