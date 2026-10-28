using UnityEngine;

public class ForestStageShakeView : MonoBehaviour
{
    [Header("기존 시스템")]
    [SerializeField] private MusicPlayback musicPlayback;
    [SerializeField] private RhythmInputTester rhythmInputTester;

    [Header("흔들 대상")]
    [SerializeField] private RectTransform forestStage;

    [Header("판정별 흔들림 크기")]
    [SerializeField, Min(0.0f)]
    private float normalStrength = 1.5f;

    [SerializeField, Min(0.0f)]
    private float goodStrength = 3.0f;

    [SerializeField, Min(0.0f)]
    private float perfectStrength = 5.0f;

    [Header("흔들림 시간")]
    [SerializeField, Min(0.01f)]
    private float shakeSeconds = 0.10f;

    [SerializeField, Min(0.01f)]
    private float holdShakeSeconds = 0.14f;

    [Header("흔들림 형태")]
    [SerializeField, Min(1.0f)]
    private float shakeFrequency = 25.0f;

    [SerializeField, Range(0.0f, 1.0f)]
    private float verticalRatio = 0.4f;

    [SerializeField, Min(1.0f)]
    private float holdStrengthMultiplier = 1.6f;

    private Vector2 restPosition;

    private double startedAt;
    private float activeStrength;
    private float activeDuration;
    private float direction;

    private int observedPlaybackId = -1;
    private bool isInitialized;
    private bool isShaking;

    private void Awake()
    {
        if (musicPlayback == null ||
            rhythmInputTester == null ||
            forestStage == null)
        {
            Debug.LogError(
                "음악, 입력기와 ForestStage를 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        isInitialized = true;
    }

    private void OnEnable()
    {
        if (!isInitialized) { return; }

        observedPlaybackId = musicPlayback.PlaybackId;
        rhythmInputTester.NoteJudged += HandleJudgment;
    }

    private void OnDisable()
    {
        if (rhythmInputTester != null)
        {
            rhythmInputTester.NoteJudged -= HandleJudgment;
        }

        ResetShake();
    }

    private void HandleJudgment(
        RhythmNoteData note,
        RhythmJudgment judgment,
        double songTime)
    {
        if (!isInitialized ||
            !isActiveAndEnabled ||
            !musicPlayback.IsGameplayPlayback ||
            musicPlayback.HasNaturallyEnded ||
            musicPlayback.IsPaused ||
            !rhythmInputTester.isActiveAndEnabled ||
            note == null ||
            !note.IsResource ||
            judgment == RhythmJudgment.Miss)
        {
            return;
        }

        ResetForNewPlayback();

        float strength;

        switch (judgment)
        {
            case RhythmJudgment.Perfect:
                strength = perfectStrength;
                break;

            case RhythmJudgment.Good:
                strength = goodStrength;
                break;

            default:
                strength = normalStrength;
                break;
        }

        if (note.IsHold)
        {
            strength *= Mathf.Max(
                1.0f, holdStrengthMultiplier);
        }

        if (strength <= 0.0f)
        {
            return;
        }

        // 흔들리는 중에는 현재 위치를 원점으로 다시 저장하지 않는다.
        // 그래야 연속 타격 후에도 화면 위치가 밀리지 않는다.
        if (!isShaking)
        {
            restPosition = forestStage.anchoredPosition;
        }

        // 연속 타격은 최근 타격의 강도로 다시 시작한다.
        // 흔들림 크기를 계속 더하지 않는다.
        activeStrength = strength;

        activeDuration = Mathf.Max(
            0.01f,
            note.IsHold ? holdShakeSeconds : shakeSeconds);

        direction =
            note.Side == RhythmInputSide.Left ? -1.0f : 1.0f;

        startedAt = songTime;
        isShaking = true;
    }

    private void LateUpdate()
    {
        if (!isInitialized) { return; }

        if (musicPlayback == null ||
            rhythmInputTester == null ||
            forestStage == null)
        {
            ResetShake();
            enabled = false;
            return;
        }

        ResetForNewPlayback();

        if (!musicPlayback.IsGameplayPlayback ||
            musicPlayback.HasNaturallyEnded ||
            !rhythmInputTester.isActiveAndEnabled)
        {
            ResetShake();
            return;
        }

        if (!isShaking || musicPlayback.IsPaused)
        {
            return;
        }

        if (!musicPlayback.TryGetSongTime(out double songTime))
        {
            ResetShake();
            return;
        }

        float elapsed = Mathf.Max(
            0.0f, (float)(songTime - startedAt));

        float progress = Mathf.Clamp01(
            elapsed / activeDuration);

        if (progress >= 1.0f)
        {
            ResetShake();
            return;
        }

        // 처음에 강하게 반응하고 빠르게 안정된다.
        float remaining = 1.0f - progress;
        float strength =
            activeStrength * remaining * remaining;

        float angle =
            elapsed *
            Mathf.Max(1.0f, shakeFrequency) *
            Mathf.PI * 2.0f;

        Vector2 offset = new Vector2(
            Mathf.Cos(angle) * direction,
            Mathf.Sin(angle * 1.35f) *
            Mathf.Clamp01(verticalRatio));

        forestStage.anchoredPosition =
            restPosition + offset * strength;
    }

    private void ResetForNewPlayback()
    {
        if (observedPlaybackId == musicPlayback.PlaybackId)
        {
            return;
        }

        ResetShake();
        observedPlaybackId = musicPlayback.PlaybackId;
    }

    private void ResetShake()
    {
        if (!isShaking) { return; }

        if (forestStage != null)
        {
            forestStage.anchoredPosition = restPosition;
        }

        isShaking = false;
    }
}