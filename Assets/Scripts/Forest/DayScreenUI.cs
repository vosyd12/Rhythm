using UnityEngine;

// 미리듣기와 실제 게임 재생 상태에 따라 낮 화면을 전환한다.
// 정산 후 결과창은 DayResultUI가 표시한다.
public class DayScreenUI : MonoBehaviour
{
    [SerializeField]
    private MusicPlayback musicPlayback;

    [SerializeField]
    private FarmingResultTester farmingResult;

    [SerializeField]
    private CanvasGroup preparationGroup;

    [SerializeField]
    private CanvasGroup playGroup;

    private void Awake()
    {
        if (musicPlayback == null ||
            farmingResult == null ||
            preparationGroup == null ||
            playGroup == null)
        {
            Debug.LogError(
                "Music Playback, Farming Result, " +
                "Preparation Group, Play Group을 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        RefreshUI();
    }

    private void Update()
    {
        // 로드 완료 또는 정산 완료 여부에 맞춰 화면을 갱신한다.
        RefreshUI();
    }

    private void RefreshUI()
    {
        bool hasSettled = farmingResult.HasSettled;
        bool isGameplay = musicPlayback.IsGameplayPlayback;

        // 음원 선택, 분석, 미리듣기, BPM 후보 선택까지 준비 화면을 유지한다.
        bool showPreparation =
            !isGameplay && !hasSettled;

        // 실제 게임 재생으로 전환된 뒤에만 플레이 화면을 표시한다.
        // 일시정지 중에도 플레이 화면은 유지한다.
        bool showPlay =
            isGameplay && !hasSettled;

        SetGroupVisible(preparationGroup, showPreparation);
        SetGroupVisible(playGroup, showPlay);
    }

    private void SetGroupVisible(
        CanvasGroup group,
        bool visible)
    {
        // Alpha 1: 표시, Alpha 0: 완전히 투명.
        float targetAlpha = visible ? 1.0f : 0.0f;

        // 이미 원하는 상태라면 같은 값을 반복해서 적용하지 않는다.
        if (group.alpha == targetAlpha &&
            group.interactable == visible &&
            group.blocksRaycasts == visible)
        {
            return;
        }

        // 화면에 보이는 정도를 설정한다.
        group.alpha = targetAlpha;

        // 숨긴 구역의 버튼과 입력창을 조작할 수 없게 한다.
        group.interactable = visible;

        // 숨긴 UI가 다른 UI로 향하는 마우스 클릭을 가로채지 않게 한다.
        group.blocksRaycasts = visible;
    }
}