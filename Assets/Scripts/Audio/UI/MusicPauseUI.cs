using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MusicPauseUI : MonoBehaviour
{
    [SerializeField]
    private MusicPlayback musicPlayback;

    [SerializeField]
    private Button pauseButton;

    [SerializeField]
    private TMP_Text pauseButtonText;

    private void Awake()
    {
        if (musicPlayback == null ||
            pauseButton == null ||
            pauseButtonText == null)
        {
            Debug.LogError(
                "Music Playback, Pause Button, " +
                "Pause Button Text를 연결해 주세요.");

            enabled = false;
            return;
        }

        RefreshUI();
    }

    private void Update()
    {
        // 오디오 일시정지 중에도 UI의 Update는 계속 실행된다.
        // 따라서 RESUME 버튼을 누를 수 있다.
        RefreshUI();
    }

    private void RefreshUI()
    {
        // 카운트다운 중에는 CanTogglePause가 false이므로
        // 버튼이 비활성화되어 중복 클릭을 막는다.
        pauseButton.interactable = musicPlayback.CanTogglePause;

        string label;

        // 카운트다운 중에도 IsPaused는 true다.
        // 따라서 카운트다운 상태를 먼저 확인해야 숫자가 표시된다.
        if (musicPlayback.IsResumeCountingDown)
        {
            label =
                musicPlayback.ResumeCountdownSeconds.ToString();
        }

        else
        {
            label = musicPlayback.IsPaused
                ? "RESUME"
                : "PAUSE";
        }

        if (pauseButtonText.text != label)
        {
            pauseButtonText.text = label;
        }
    }
}