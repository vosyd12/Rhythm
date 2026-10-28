using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

// 종료 버튼의 클릭을 받아 게임을 종료한다.
// 낮과 밤 씬에서 같은 컴포넌트를 사용할 수 있다.
[DefaultExecutionOrder(-200)]
public class GameExitUI : MonoBehaviour
{
    private CanvasGroup menuGroup;
    private bool ownsTimePause;
    private float previousTimeScale = 1f;
    private MusicPlayback music;
    private bool pausedMusic;

    private void RefreshPauseState()
    {
        bool pause = IsMenuOpen || M7CampaignUI.IsModalOpen || RunCampaign.IsTravelling || !Application.isFocused;
        if (pause && !ownsTimePause)
        {
            previousTimeScale = Time.timeScale;
            ownsTimePause = true;
            Time.timeScale = 0f;
            if (music != null && !music.IsPaused && music.CanTogglePause)
            { music.TogglePause(); pausedMusic = true; }
        }
        else if (!pause) ReleaseTimePause();
    }
    private void ReleaseTimePause()
    {
        if (ownsTimePause) { Time.timeScale = previousTimeScale; ownsTimePause = false; }
        if (pausedMusic && music != null && music.isActiveAndEnabled && music.IsPaused && music.CanTogglePause)
            music.TogglePause();
        pausedMusic = false;
    }
    private void OnApplicationFocus(bool hasFocus) { RefreshPauseState(); }
    public static bool IsMenuOpen { get; private set; }
    public static bool IsWorldPaused => IsMenuOpen || M7CampaignUI.IsModalOpen || RunCampaign.IsTravelling || !Application.isFocused || Time.timeScale <= 0f;
    public static bool EscapeHandledThisFrame => escapeFrame == Time.frameCount;
    private static int escapeFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetMenuState() { IsMenuOpen = false; escapeFrame = -1; }

    private void Awake()
    {
        music = FindAnyObjectByType<MusicPlayback>();
        menuGroup = GetComponent<CanvasGroup>();
        if (menuGroup == null) menuGroup = gameObject.AddComponent<CanvasGroup>();
        // 기존 버튼 참조는 보존하고 메뉴가 열렸을 때만 화면 중앙에 표시한다.
        RectTransform rect = GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
        }
        SetMenuVisible(false);
    }

    private void SetMenuVisible(bool visible)
    {
        IsMenuOpen = visible;
        RefreshPauseState();
        if (menuGroup == null) return;
        menuGroup.alpha = visible ? 1f : 0f;
        menuGroup.interactable = menuGroup.blocksRaycasts = visible;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private void Update()
    {
        RefreshPauseState();
        Keyboard keyboard = Keyboard.current;
        if (M7CampaignUI.IsModalOpen || M7CampaignUI.IsSelectingCommandPoint || ForestGatheringController.BlocksCommands || !Application.isFocused || keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
        {
            var input = EventSystem.current.currentSelectedGameObject.GetComponentInParent<TMPro.TMP_InputField>();
            if (input != null && input.isFocused) return;
        }
        // 배치 취소와 기존 창 닫기를 먼저 처리하고 다음 Esc에서 종료 메뉴를 연다.
        if (!IsMenuOpen && (CampWallPlacementController.IsPlacing || CampWindowController.WorldInputBlocked)) return;
        escapeFrame = Time.frameCount;
        SetMenuVisible(!IsMenuOpen);
    }

    private void OnDisable() { SetMenuVisible(false); ReleaseTimePause(); }

    // Button의 On Click에 연결할 함수.
    // public이며 매개변수가 없으므로 Inspector에서 선택할 수 있다.
    public void OnClickQuit()
    {
        // 게임 실행 중이며 이 컴포넌트가 활성화되어 있을 때만 처리한다.
        if (!Application.isPlaying || !isActiveAndEnabled)
        {
            return;
        }

        // 음량 조절 등으로 PlayerPrefs에 기록해 둔 변경 사항을
        // 게임을 종료하기 전에 저장한다.
        PlayerPrefs.Save();

        Debug.Log("[게임 종료] 종료를 요청합니다.");

#if UNITY_EDITOR
        // Unity 에디터에서 실행하는 경우:
        // 에디터는 열어 둔 채 Play 모드를 종료한다.
        UnityEditor.EditorApplication.isPlaying = false;
#else
        // 빌드된 실행파일에서 실행하는 경우:
        // 현재 실행 중인 게임 프로그램을 종료한다.
        Application.Quit();
#endif
    }
}