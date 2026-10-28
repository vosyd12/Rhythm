using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 정산 결과를 표시하고 같은 날짜의 기지로 복귀한다.
// 판정 기록과 목재 지급은 FarmingResultTester가 담당한다.
public class DayResultUI : MonoBehaviour
{
    [SerializeField]
    private FarmingResultTester farmingResultTester;

    [SerializeField]
    private GameObject resultPanel;

    [SerializeField]
    private TMP_Text resultText;

    [Header("기지 복귀")]

    [SerializeField]
    private Button goToNightButton;

    [SerializeField]
    private TMP_Text goToNightButtonText;

    private const string BaseCampSceneName = "BaseCamp";

    private bool isInitialized;
    private bool isChangingScene;

    private bool CanReturnToBaseCamp =>
        isInitialized &&
        isActiveAndEnabled &&
        farmingResultTester.isActiveAndEnabled &&
        farmingResultTester.HasSettled &&
        !isChangingScene;

    private void Awake()
    {
        if (farmingResultTester == null ||
            resultPanel == null ||
            resultText == null ||
            goToNightButton == null ||
            goToNightButtonText == null)
        {
            Debug.LogError(
                "DayResultUI의 정산기, 결과창, 결과 글자, " +
                "복귀 버튼과 버튼 글자를 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        // 이 컴포넌트는 결과창 바깥의 활성 오브젝트에 부착한다.
        resultPanel.SetActive(false);
        goToNightButton.interactable = false;
        goToNightButtonText.text = "기지로 복귀";

        isInitialized = true;
    }

    private void LateUpdate()
    {
        RefreshUI();
    }

    private void RefreshUI()
    {
        bool shouldShow = farmingResultTester.HasSettled;

        if (resultPanel.activeSelf != shouldShow)
        {
            if (shouldShow)
            {
                // 정산된 값으로 내용을 준비한 뒤 결과창을 표시한다.
                RefreshResultText();
                resultPanel.transform.SetAsLastSibling();
            }

            resultPanel.SetActive(shouldShow);
        }

        // 결과창의 활성 상태가 같아도 버튼 잠금 상태는 갱신한다.
        goToNightButton.interactable = CanReturnToBaseCamp;

        string buttonText = isChangingScene
            ? "이동 중..."
            : "기지로 복귀";

        if (goToNightButtonText.text != buttonText)
        {
            goToNightButtonText.text = buttonText;
        }
    }

    private void RefreshResultText()
    {
        // 정산기가 저장한 값을 표시하기만 한다.
        // 여기에서는 목재를 지급하지 않는다.
        resultText.text =
            $"DAY {RunProgress.CurrentDay} RESULT\n\n" +
            $"PERFECT  {farmingResultTester.PerfectCount}\n" +
            $"GOOD  {farmingResultTester.GoodCount}\n" +
            $"NORMAL  {farmingResultTester.NormalCount}\n" +
            $"MISS  {farmingResultTester.MissCount}\n" +
            $"TOTAL  {farmingResultTester.TotalCount}\n\n" +
            $"WOOD EARNED  {farmingResultTester.RoundWood}\n" +
            $"WOOD OWNED  {farmingResultTester.StoredWood}";
    }

    [ContextMenu("정산 후 기지로 복귀")]
    public void ReturnToBaseCamp()
    {
        if (!Application.isPlaying ||
            !CanReturnToBaseCamp)
        {
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(BaseCampSceneName))
        {
            Debug.LogError(
                "BaseCamp 씬을 불러올 수 없습니다. " +
                "Build Profiles의 Scene List를 확인해 주세요.",
                this);

            return;
        }

        // 씬 이동 요청 전에 버튼과 중복 호출을 잠근다.
        isChangingScene = true;
        RefreshUI();

        Debug.Log(
            $"[기지 복귀 요청] DAY {RunProgress.CurrentDay} / " +
            $"보유 목재: {farmingResultTester.StoredWood}",
            this);

        try
        {
            // 같은 날짜의 기지로 복귀한다.
            // 목재는 정산기가 지급했으며 같은 날짜의 낮 상태로 복귀한다.
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                BaseCampSceneName,
                LoadSceneMode.Single);

            if (operation == null)
            {
                throw new System.InvalidOperationException(
                    "기지 씬 로드 요청을 생성하지 못했습니다.");
            }
        }
        catch (System.Exception exception)
        {
            // 요청이 실패하면 복귀 버튼으로 다시 시도할 수 있게 한다.
            isChangingScene = false;
            RefreshUI();

            Debug.LogException(exception, this);
        }
    }
}