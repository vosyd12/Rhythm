using System;
using UnityEngine;

// 해당 파일은 숲의 시각 연출 스크립트를 보관하는 용도

// 음악의 박자에 맞춰 캐릭터의 몸을 살짝 눌렀다가 복원한다.

// 이 스크립트는 캐릭터의 시각적 크기만 변경한다.
// 입력 판정, 나무 처리, 보상 계산에는 관여하지 않는다.
public class ForestPlayerBeatMotion : MonoBehaviour
{
    [SerializeField]
    private MusicPlayback musicPlayback;

    // 나무와 같은 음악 박자 기준을 사용한다.
    [SerializeField]
    private RhythmBeatClock beatClock;

    // 숲 화면에 표시하는 캐릭터의 RectTransform.
    // 발 위치를 고정하려면 Pivot Y를 0으로 설정한다.
    [SerializeField]
    private RectTransform playerVisual;

    // 박자에 도달했을 때 줄어드는 세로 크기의 비율.
    // 0.08이면 원래 높이의 92%까지 눌린다.
    [SerializeField, Range(0.0f, 0.2f)]
    private float squashAmount = 0.08f;

    // 몸이 눌릴 때 늘어나는 가로 크기의 비율.
    // 0.04이면 원래 너비의 104%까지 넓어진다.
    [SerializeField, Range(0.0f, 0.2f)]
    private float stretchAmount = 0.04f;

    // 한 박 중 원래 자세로 돌아오는 데 사용할 비율.
    //
    // 0.4라면:
    // 박자 시작부터 40% 구간 동안 복원하고,
    // 나머지 60% 구간에서는 원래 자세를 유지한다.
    [SerializeField, Range(0.1f, 1.0f)]
    private float recoveryBeatRatio = 0.4f;

    // Inspector에서 설정한 원래 크기를 보관한다.
    private Vector3 originalScale;

    private bool isInitialized;

    private void Awake()
    {
        if (musicPlayback == null ||
            beatClock == null ||
            playerVisual == null)
        {
            Debug.LogError(
                "Music Playback, Beat Clock, Player Visual을 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        // 원래 크기가 반드시 (1, 1, 1)이라고 가정하지 않는다.
        originalScale = playerVisual.localScale;

        isInitialized = true;
    }

    private void LateUpdate()
    {
        if (!isInitialized)
        {
            return;
        }

        // BPM 후보 미리듣기에서는 동작하지 않는다.
        // 실제 플레이를 시작한 뒤에만 캐릭터가 박자를 탄다.
        if (!musicPlayback.IsGameplayPlayback ||
            !beatClock.isActiveAndEnabled ||
            !beatClock.IsConfigured)
        {
            RestoreScale();
            return;
        }

        // 일시정지와 재개 카운트다운 중에는 현재 자세를 유지한다.
        if (musicPlayback.IsPaused)
        {
            return;
        }

        // 박자 위치 예:
        // 0.0 → 첫 기준 박자
        // 0.5 → 첫 박자와 다음 박자의 중간
        // 1.0 → 다음 박자
        //
        // 예약 재생 전이나 음악 종료 후에는 조회에 실패하므로
        // 원래 자세로 돌아간다.
        if (!beatClock.TryGetBeatPosition(out double beatPosition) ||
            beatPosition < 0.0)
        {
            RestoreScale();
            return;
        }

        // 정수 부분을 제거하여 현재 한 박의 진행률만 남긴다.
        //
        // 3.25 - Floor(3.25)
        // = 3.25 - 3
        // = 0.25
        //
        // 결과 범위는 0 이상 1 미만이다.
        double beatFraction =
            beatPosition - Math.Floor(beatPosition);

        float recoveryRatio =
            Mathf.Clamp(recoveryBeatRatio, 0.1f, 1.0f);

        // 복원 구간 안에서 얼마나 진행했는지 계산한다.
        //
        // recoveryRatio가 0.4일 때:
        // 박자 진행률 0.0 → 복원 진행률 0
        // 박자 진행률 0.2 → 복원 진행률 0.5
        // 박자 진행률 0.4 이상 → 복원 진행률 1
        float recoveryProgress = Mathf.Clamp01(
            (float)beatFraction / recoveryRatio);

        // 박자 시작에서는 1, 복원 완료 시점에서는 0.
        float remaining = 1.0f - recoveryProgress;

        // 제곱하면 초반에는 빠르게 복원되고,
        // 원래 자세에 가까워질수록 변화가 작아진다.
        float pulse = remaining * remaining;

        float widthMultiplier =
            1.0f + Mathf.Clamp(stretchAmount, 0.0f, 0.2f) * pulse;

        float heightMultiplier =
            1.0f - Mathf.Clamp(squashAmount, 0.0f, 0.2f) * pulse;

        // 매 프레임 원래 크기를 기준으로 계산한다.
        // 현재 크기에 계속 곱하면 크기가 누적해서 변하므로 피한다.
        playerVisual.localScale = new Vector3(
            originalScale.x * widthMultiplier,
            originalScale.y * heightMultiplier,
            originalScale.z);
    }

    private void RestoreScale()
    {
        if (!isInitialized || playerVisual == null)
        {
            return;
        }

        playerVisual.localScale = originalScale;
    }

    private void OnDisable()
    {
        // 컴포넌트를 끄거나 화면을 정리할 때
        // 눌린 자세가 남지 않게 한다.
        RestoreScale();
    }
}