using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 음원 선택과 로드 요청을 처리한다.
// BPM 분석 실패 시 현재 씬을 다시 준비하여 재선택하게 한다.
public class MusicSelectionUI : MonoBehaviour
{
    [SerializeField]
    private LocalMusicLoader musicLoader;

    [SerializeField]
    private TMP_InputField pathInput;

    [SerializeField]
    private Button loadButton;

    [SerializeField]
    private Button browseButton;

    [Header("분석 실패 후 재선택")]

    [SerializeField]
    private MusicBeatSelection beatSelection;

    [SerializeField]
    private MusicPlayback musicPlayback;

    [SerializeField]
    private TMP_Text loadButtonText;

    private string originalLoadButtonText;
    private bool isReloadingScene;

    // 모든 분석이 끝났지만 사용할 박자를 얻지 못한 상태.
    // 계산 중이거나 실제 플레이 중에는 재선택하지 않는다.
    private bool CanRetryAnalysis =>
        musicLoader.IsAudioLoaded &&
        !musicPlayback.IsGameplayPlayback &&
        beatSelection.HasNoUsableBeat;

    private void Awake()
    {
        if (musicLoader == null ||
            pathInput == null ||
            loadButton == null ||
            browseButton == null ||
            beatSelection == null ||
            musicPlayback == null ||
            loadButtonText == null)
        {
            Debug.LogError(
                "MusicSelectionUI의 로더, 입력창, 버튼, " +
                "박자 선택, 음악 재생기와 버튼 글자를 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        originalLoadButtonText = loadButtonText.text;
    }

    private void Start()
    {
        pathInput.SetTextWithoutNotify(musicLoader.FilePath);
        RefreshUI();
    }

    private void Update()
    {
        RefreshUI();
    }

    public void OnClickBrowseMusic()
    {
        if (!Application.isPlaying ||
            !isActiveAndEnabled ||
            isReloadingScene ||
            !musicLoader.CanLoad)
        {
            return;
        }

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        string selectedPath = MusicFileDialog.OpenAudioFile();

        if (string.IsNullOrEmpty(selectedPath))
        {
            return;
        }

        pathInput.SetTextWithoutNotify(selectedPath);
        RefreshUI();
#endif
    }

    public void OnClickLoadMusic()
    {
        if (!Application.isPlaying ||
            !isActiveAndEnabled ||
            isReloadingScene)
        {
            return;
        }

        // 버튼에 남은 선택으로 Space 입력이 재실행되는 것을 막는다.
        EventSystem eventSystem = EventSystem.current;

        if (eventSystem != null)
        {
            eventSystem.SetSelectedGameObject(null);
        }

        // 같은 버튼이 분석 실패 상태에서는 재선택 요청을 처리한다.
        if (CanRetryAnalysis)
        {
            ReloadForMusicSelection();
            return;
        }

        if (!musicLoader.CanLoad)
        {
            return;
        }

        string requestedPath = pathInput.text;
        pathInput.DeactivateInputField();

        musicLoader.LoadFromPath(requestedPath);
        RefreshUI();
    }

    private void ReloadForMusicSelection()
    {
        if (ForestGatheringController.Owns(musicPlayback))
        { ForestGatheringController.Instance.ResetMusicSelection(); return; }
        // 이 UI가 속한 씬을 다시 로드한다.
        string sceneName = gameObject.scene.name;

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError(
                $"음원 선택 화면을 다시 열 수 없습니다: {sceneName}\n" +
                "Build Profiles의 Scene List를 확인해 주세요.",
                this);
            return;
        }

        isReloadingScene = true;
        RefreshUI();

        try
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single);

            if (operation == null)
            {
                throw new System.InvalidOperationException(
                    "음원 선택 씬의 로드 요청을 생성하지 못했습니다.");
            }
        }
        catch (System.Exception exception)
        {
            // 요청 자체가 실패하면 버튼으로 다시 시도할 수 있게 한다.
            isReloadingScene = false;
            RefreshUI();

            Debug.LogException(exception, this);
        }
    }

    private void RefreshUI()
    {
        bool canRetry = CanRetryAnalysis;
        bool canLoad =
            !isReloadingScene && musicLoader.CanLoad;

        bool showSelection =
            !isReloadingScene && !musicLoader.IsAudioLoaded;

        pathInput.interactable = canLoad;
        browseButton.interactable = canLoad;

        loadButton.interactable =
            !isReloadingScene &&
            (canRetry ||
             (canLoad && !string.IsNullOrWhiteSpace(pathInput.text)));

        if (isReloadingScene)
        {
            loadButtonText.text = "다시 준비하는 중...";
        }
        else if (canRetry)
        {
            loadButtonText.text = "음원 다시 선택";
        }
        else
        {
            loadButtonText.text = originalLoadButtonText;
        }

        SetVisible(pathInput.gameObject, showSelection);

        // 분석 실패 시에는 기존 로드 버튼만 다시 표시한다.
        SetVisible(
            loadButton.gameObject,
            showSelection || canRetry || isReloadingScene);

        bool showBrowse = false;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        showBrowse = showSelection;
#endif

        SetVisible(browseButton.gameObject, showBrowse);
    }

    private static void SetVisible(GameObject target, bool visible)
    {
        if (target.activeSelf != visible)
        {
            target.SetActive(visible);
        }
    }
}