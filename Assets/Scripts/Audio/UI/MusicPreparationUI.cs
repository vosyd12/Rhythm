using TMPro;
using UnityEngine;

// 음원 로드와 초기 준비 상태를 화면에 표시한다.
// 실제 파일 로드나 음원 분석은 각 담당 컴포넌트가 수행한다.
public class MusicPreparationUI : MonoBehaviour
{
    [SerializeField]
    private LocalMusicLoader musicLoader;

    [SerializeField]
    private AudioAnalysisTester audioAnalyzer;

    [SerializeField]
    private RhythmPatternGenerator patternGenerator;

    [SerializeField]
    private TMP_Text statusText;

    private void Awake()
    {
        // 기본 참조는 초기화 시점에 확인한다.
        if (musicLoader == null ||
            audioAnalyzer == null ||
            patternGenerator == null ||
            statusText == null)
        {
            Debug.LogError(
                "Music Loader, Audio Analyzer, " +
                "Pattern Generator, Status Text를 연결해 주세요.");

            enabled = false;
            return;
        }
    }

    private void Update()
    {
        // 현재 상태에 맞는 문구를 선택한다.
        string message = GetPreparationMessage();

        // 상태를 매 프레임 확인하되,
        // 같은 문구를 계속 대입하는 것은 피한다.
        if (statusText.text == message) { return; }

        statusText.text = message;
    }

    private string GetPreparationMessage()
    {
        // 실패 여부를 먼저 검사한다.
        // 실패하면 AudioClip이 없는 상태이므로,
        // 로드 완료 여부부터 검사하면 계속 대기로 표시할 수 있다.
        if (musicLoader.HasLoadFailed)
        {
            // 첫 줄에는 실패 상태,
            // 둘째 줄에는 로더가 기록한 안내 문구를 표시한다.
            // \n은 화면에서 줄을 바꾸는 문자다.
            return
                $"AUDIO LOAD FAILED\n" +
                $"{musicLoader.LoadErrorMessage}";
        }

        if (!musicLoader.isActiveAndEnabled)
        {
            return "AUDIO LOADER STOPPED";
        }

        if (!musicLoader.IsAudioLoaded)
        {
            // 요청 중이면 로딩, 요청도 없다면 대기로 표시한다.
            return musicLoader.IsLoading
                ? "LOADING AUDIO..."
                : "WAITING FOR AUDIO";
        }

        // 음원을 읽었더라도 분석이나 패턴 생성이 실패할 수 있다.
        if (audioAnalyzer.HasAnalysisFailed ||
            patternGenerator.HasGenerationFailed)
        {
            return "PREPARATION FAILED";
        }

        if (!audioAnalyzer.isActiveAndEnabled ||
            !patternGenerator.isActiveAndEnabled)
        {
            return "PREPARATION STOPPED";
        }

        // 곡 전체가 아니라 첫 구간의 분석 완료 여부를 확인한다.
        if (!audioAnalyzer.IsInitialAnalysisReady)
        {
            return "ANALYZING AUDIO...";
        }

        // 첫 구간의 후보를 실제 플레이 이벤트로 선택하는 과정이다.
        if (!patternGenerator.IsInitialPatternReady)
        {
            return "PREPARING PATTERN...";
        }

        // 초기 준비가 끝나면 글자만 지운다.
        // 오브젝트는 계속 켜 두므로 이후 분석 실패도 표시할 수 있다.
        return string.Empty;
    }
}