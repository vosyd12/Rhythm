using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// 숲 입구의 안내 표시와 탐사 입장 요청을 담당한다.
// ForestEntrance 부모 오브젝트에 부착한다.
public class ForestEntranceInteraction : MonoBehaviour
{
    [SerializeField]
    private PlayerMovement playerMovement;

    [SerializeField]
    private TMP_Text interactionPromptText;

    // 현재 기지에서 탐사를 시작할 수 있는 상태인지 조회한다.
    [SerializeField]
    private NightDefenseTester nightDefenseTester;

    [SerializeField, Min(0.1f)]
    private float interactionRange = 2.5f;

    // 현재 리듬 작업이 구현되어 있는 씬을 사용한다.
    [SerializeField]
    private string expeditionSceneName = "Boot";


    public string ContextMessage => isInitialized && isActiveAndEnabled && IsPlayerInRange() &&
        nightDefenseTester.CanStartExpedition ? interactionPromptText.text : "";
    private bool isInitialized;

    // 씬 이동을 요청한 뒤 추가 입력으로 중복 차감되지 않게 한다.
    private bool isChangingScene;

    private void Awake()
    {
        if (playerMovement == null ||
            interactionPromptText == null ||
            nightDefenseTester == null)
        {
            Debug.LogError(
                "Player Movement, Interaction Prompt Text, " +
                "Night Defense Tester를 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        if (interactionRange <= 0.0f ||
            string.IsNullOrWhiteSpace(expeditionSceneName))
        {
            Debug.LogError(
                "상호작용 거리와 탐사 씬 이름을 확인해 주세요.",
                this);

            enabled = false;
            return;
        }

        isInitialized = true;
    }

    private void OnEnable()
    {
        if (!isInitialized) { return; }

        playerMovement.InteractionRequested +=
            HandleInteractionRequested;

        interactionPromptText.enabled = false;
    }

    private void OnDisable()
    {
        // 다시 활성화할 때 이벤트가 중복 등록되지 않도록 해제한다.
        if (playerMovement != null)
        {
            playerMovement.InteractionRequested -=
                HandleInteractionRequested;
        }

        if (interactionPromptText != null)
        {
            interactionPromptText.enabled = false;
        }
    }

    private void LateUpdate()
    {
        // 플레이어 이동이 끝난 위치로 안내를 갱신한다.
        RefreshPromptUI();
    }

    private bool IsPlayerInRange()
    {
        Vector2 playerPosition =
            playerMovement.transform.position;

        Vector2 entrancePosition =
            transform.position;

        // XY 평면에서 두 기준점 사이의 거리를 계산한다.
        // 거리 = sqrt(X 차이² + Y 차이²)
        float distance = Vector2.Distance(
            playerPosition,
            entrancePosition);

        return distance <= interactionRange;
    }

    private void RefreshPromptUI()
    {
        // 가까이 있으며 탐사 가능한 상태일 때 안내를 표시한다.
        bool showPrompt =
            IsPlayerInRange() &&
            nightDefenseTester.CanStartExpedition;

        bool renderHere = showPrompt && !NightScreenUI.HasContextPanel;
        if (interactionPromptText.enabled != renderHere)
        {
            interactionPromptText.enabled = renderHere;
        }

        if (!showPrompt) { return; }

        if (isChangingScene)
        {
            interactionPromptText.text = "숲으로 이동 중...";
            return;
        }

        interactionPromptText.text = "숲 채집\nR : 로봇 원격 조종";
    }

    private void HandleInteractionRequested()
    {
        // 이동 중에는 같은 요청을 다시 처리하지 않는다.
        if (isChangingScene ||
            !IsPlayerInRange() ||
            !nightDefenseTester.CanStartExpedition)
        {
            return;
        }

        // 씬 이름 오류나 빌드 목록 누락은 이동 잠금 전에 확인한다.
        if (!Application.CanStreamedLevelBeLoaded(
                expeditionSceneName))
        {
            Debug.LogError(
                $"탐사 씬을 불러올 수 없습니다: {expeditionSceneName}\n" +
                "Build Profiles의 Scene List를 확인해 주세요.",
                this);

            return;
        }

        if (ForestGatheringController.Instance != null)
        { ForestGatheringController.Instance.RequestGathering(); return; }

        isChangingScene = true;
        RefreshPromptUI();

        try
        {
            // 현재 BaseCamp 씬을 교체하고 Boot 씬을 불러온다.
            // 날짜를 변경하는 함수는 호출하지 않는다.
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                expeditionSceneName,
                LoadSceneMode.Single);

            if (operation == null)
            {
                throw new System.InvalidOperationException(
                    "탐사 씬 로드 요청을 생성하지 못했습니다.");
            }
        }

        catch (System.Exception exception)
        {
            isChangingScene = false;
            RefreshPromptUI();

            Debug.LogException(exception, this);
            return;
        }

        Debug.Log($"[숲 탐사 입장 요청] DAY {RunProgress.CurrentDay}", this);
    }
}