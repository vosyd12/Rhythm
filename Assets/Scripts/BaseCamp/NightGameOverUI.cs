using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 밤 전투의 패배 상태를 읽어서 게임오버 화면을 표시한다.
// NEW RUN 버튼을 누르면 공통 진행 데이터를 초기화하고 낮으로 이동한다.
public class NightGameOverUI : MonoBehaviour
{
    [SerializeField]
    private NightDefenseTester nightDefenseTester;

    [SerializeField]
    private GameObject gameOverPanel;

    [SerializeField]
    private Button newRunButton;

    [SerializeField]
    private TMP_Text newRunButtonText;

    // 씬을 준비하는 동안 재시작 요청이 중복되는 것을 막는다.
    private bool isRestarting;

    private void Awake()
    {
        if (nightDefenseTester == null ||
            gameOverPanel == null ||
            newRunButton == null ||
            newRunButtonText == null)
        {
            Debug.LogError(
                "Night Defense Tester, Game Over Panel, " +
                "New Run Button, New Run Button Text를 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        gameOverPanel.SetActive(false);
        RefreshUI();
    }

    private void Update()
    {
        RefreshUI();
    }

    private void RefreshUI()
    {
        bool isDefeated = nightDefenseTester.IsDefeated && M7CampaignUI.Instance == null;

        // 표시 상태가 달라졌을 때만 패널을 켜거나 끈다.
        if (gameOverPanel.activeSelf != isDefeated)
        {
            if (isDefeated)
            {
                // 같은 Canvas 안에서 기존 UI 위에 표시한다.
                gameOverPanel.transform.SetAsLastSibling();
            }

            gameOverPanel.SetActive(isDefeated);
        }

        // 패배한 뒤, 아직 재시작을 요청하지 않았을 때만 누를 수 있다.
        newRunButton.interactable =
            isDefeated && !isRestarting;

        newRunButtonText.text =
            isRestarting ? "LOADING..." : "NEW RUN";
    }

    // NEW RUN 버튼의 On Click에 연결할 함수.
    public void OnClickNewRun()
    {
        Debug.Log(
            $"[NEW RUN 추적 1] 버튼 함수 진입 / " +
            $"활성={isActiveAndEnabled}, " +
            $"재시작중={isRestarting}, " +
            $"패배={nightDefenseTester != null && nightDefenseTester.IsDefeated}",
            this);

        // 버튼 외의 경로로 호출되더라도 실행 조건을 확인한다.
        if (!Application.isPlaying ||
            !isActiveAndEnabled ||
            isRestarting ||
            !nightDefenseTester.IsDefeated)
        {
            return;
        }

        string daySceneName = nightDefenseTester.DaySceneName;

        // 데이터를 지우기 전에 이동할 씬이 있는지 확인한다.
        if (string.IsNullOrWhiteSpace(daySceneName) ||
            !Application.CanStreamedLevelBeLoaded(daySceneName))
        {
            Debug.LogError(
                $"낮 씬을 불러올 수 없습니다: {daySceneName}\n" +
                "씬 이름과 Build Profiles의 Scene List를 확인해 주세요.",
                this);

            return;
        }

        // 로딩 요청 전에 버튼을 잠가 중복 클릭을 막는다.
        isRestarting = true;
        RefreshUI();

        AsyncOperation operation;

        try
        {
            // 낮 씬을 비동기로 준비한다.
            // Single은 새 씬으로 현재 밤 씬을 교체하는 방식이다.
            operation = SceneManager.LoadSceneAsync(
                daySceneName,
                LoadSceneMode.Single);
        }
        catch (System.Exception exception)
        {
            // 로딩 요청에 실패했다면 다시 누를 수 있도록 복구한다.
            // 이 시점에는 진행 데이터를 아직 초기화하지 않았다.
            isRestarting = false;
            RefreshUI();

            Debug.LogException(exception, this);
            return;
        }

        if (operation == null)
        {
            isRestarting = false;
            RefreshUI();

            Debug.LogError(
                "낮 씬 로딩 작업을 생성하지 못했습니다.",
                this);

            return;
        }

        // 로딩이 준비되어도 새 씬을 바로 실행하지 않도록 잠시 막는다.
        // 낮 씬의 초기화 코드가 실행되기 전에 진행 데이터를 지우기 위함이다.
        operation.allowSceneActivation = false;

        StartCoroutine(ActivateNewRun(operation));
    }

    private IEnumerator ActivateNewRun(AsyncOperation operation)
    {
        // 씬 활성화를 막으면 로딩 진행률은 0.9에서 대기한다.
        // 여기까지 도달하면 활성화 직전의 준비가 끝난 상태다.
        while (operation.progress < 0.9f)
        {
            // 다음 프레임까지 기다린다.
            // 반복문으로 메인 스레드를 계속 붙잡지 않는다.
            yield return null;
        }

        Debug.Log("[NEW RUN 추적 2] 씬 로딩 준비 완료, 초기화 호출", this);

        // 새 낮 씬이 시작되기 전에 공통 진행 데이터를 초기화한다.
        RunProgress.ResetForNewRun();

        Debug.Log("[NEW RUN 추적 3] 초기화 함수 반환 완료", this);

        Debug.Log(
            $"[새 게임 시작] " +
            $"DAY {RunProgress.CurrentDay} / " +
            $"WOOD {RunResources.StoredWood} / " +
            $"ATK BONUS {RunProgress.AttackDamageBonus}");

        // 초기화가 끝났으므로 낮 씬을 실행하도록 허용한다.
        operation.allowSceneActivation = true;
    }
}