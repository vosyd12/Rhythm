using TMPro;
using UnityEngine;

// 최종 판정 알림을 받아 글자만 표시한다.
public class ForestJudgmentTextView : MonoBehaviour
{
    [Header("기존 시스템")]
    [SerializeField] private MusicPlayback musicPlayback;
    [SerializeField] private RhythmInputTester rhythmInputTester;

    [Header("판정 글자")]
    [SerializeField] private TMP_Text judgmentText;

    [Header("표시 연출")]
    [SerializeField, Min(0.01f)]
    private float displaySeconds = 0.45f;

    [SerializeField, Min(0.0f)]
    private float riseDistance = 24.0f;

    [Header("판정별 네온 강도")]
    [SerializeField, Range(0.0f, 1.0f)]
    private float normalGlowStrength = 0.2f;

    [SerializeField, Range(0.0f, 1.0f)]
    private float goodGlowStrength = 0.55f;

    [SerializeField, Range(0.0f, 1.0f)]
    private float perfectGlowStrength = 0.85f;

    private Material originalTextMaterial;
    private Material neonMaterial;

    private float currentGlowStrength;

    private RectTransform textRect;
    private Vector2 restPosition;
    private Vector3 restScale;

    private Color judgmentColor;
    private float judgmentScale;
    private double startedAt;

    private int observedPlaybackId = -1;
    private bool isInitialized;
    private bool isShowing;

    private void Awake()
    {
        if (musicPlayback == null ||
            rhythmInputTester == null ||
            judgmentText == null)
        {
            Debug.LogError(
                "음악, 입력기, 판정 글자를 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        textRect = judgmentText.rectTransform;
        restPosition = textRect.anchoredPosition;
        restScale = textRect.localScale;

        judgmentText.raycastTarget = false;

        InitializeNeon();

        isInitialized = true;
        HideText();
    }

    private void OnEnable()
    {
        if (!isInitialized) { return; }

        observedPlaybackId = musicPlayback.PlaybackId;
        rhythmInputTester.NoteJudged += HandleJudgment;

        HideText();
    }

    private void OnDisable()
    {
        if (rhythmInputTester != null)
        {
            rhythmInputTester.NoteJudged -= HandleJudgment;
        }

        HideText();
    }

    private void HandleJudgment(
        RhythmNoteData note,
        RhythmJudgment judgment,
        double songTime)
    {
        if (!isInitialized ||
            !isActiveAndEnabled ||
            !rhythmInputTester.isActiveAndEnabled ||
            !musicPlayback.IsGameplayPlayback ||
            musicPlayback.HasNaturallyEnded ||
            note == null ||
            !note.IsResource)
        {
            return;
        }

        ResetForNewPlayback();

        judgmentColor = Color.white;
        judgmentScale = 1.0f;

        switch (judgment)
        {
            case RhythmJudgment.Perfect:
                judgmentColor = new Color(0.3f, 1.0f, 0.5f);
                judgmentScale = 1.25f;
                break;

            case RhythmJudgment.Good:
                judgmentColor = new Color(1.0f, 0.85f, 0.2f);
                judgmentScale = 1.1f;
                break;

            case RhythmJudgment.Miss:
                judgmentColor = new Color(1.0f, 0.35f, 0.35f);
                break;
        }

        SetGlowStrength(judgment);

        judgmentText.text = judgment.ToString().ToUpperInvariant();

        startedAt = songTime;
        isShowing = true;
        judgmentText.enabled = true;

        ApplyAnimation(0.0f);
    }

    private void LateUpdate()
    {
        if (!isInitialized) { return; }

        ResetForNewPlayback();

        if (!musicPlayback.IsGameplayPlayback ||
            musicPlayback.HasNaturallyEnded ||
            !rhythmInputTester.isActiveAndEnabled)
        {
            HideText();
            return;
        }

        if (musicPlayback.IsPaused) { return; }

        if (!musicPlayback.TryGetSongTime(out double songTime))
        {
            HideText();
            return;
        }

        if (!isShowing) { return; }

        float elapsed = Mathf.Max(
            0.0f, (float)(songTime - startedAt));

        float progress =
            elapsed / Mathf.Max(0.01f, displaySeconds);

        if (progress >= 1.0f)
        {
            HideText();
            return;
        }

        ApplyAnimation(progress);
    }

    private void ApplyAnimation(float progress)
    {
        // 처음에 크게 나타난 뒤 원래 크기로 돌아온다.
        float popProgress = Mathf.Clamp01(progress / 0.3f);

        float scale = judgmentScale * Mathf.Lerp(
            1.18f,
            1.0f,
            Mathf.SmoothStep(0.0f, 1.0f, popProgress));

        textRect.localScale = Vector3.Scale(
            restScale,
            new Vector3(scale, scale, 1.0f));

        // 기준 위치에서 조금씩 위로 떠오른다.
        textRect.anchoredPosition =
            restPosition + Vector2.up * riseDistance * progress;

        // 전체 표시 시간의 60%까지 선명하게 유지한다.
        // 나머지 40% 동안 글자와 빛을 함께 지운다.
        float visibility = 1.0f - Mathf.Clamp01(
            (progress - 0.6f) / 0.4f);

        // 성공 판정은 밝은 중심과 색이 있는 주변 빛으로 표현한다.
        // Miss는 기존 붉은 글자를 유지한다.
        Color faceColor = currentGlowStrength > 0.0f
            ? Color.Lerp(judgmentColor, Color.white, 0.9f)
            : judgmentColor;

        faceColor.a *= visibility;
        judgmentText.color = faceColor;

        if (neonMaterial != null)
        {
            // 성공 판정의 글자 중심을 일반 흰색보다 밝게 출력한다.
            // Miss는 기존 밝기를 유지한다.
            float brightness = currentGlowStrength > 0.0f ? 3.0f : 1.0f;

            neonMaterial.SetColor(
                "_FaceColor",
                new Color(brightness, brightness, brightness, 1.0f));

            Color glowColor = judgmentColor;
            glowColor.a = currentGlowStrength * visibility;

            neonMaterial.SetColor("_GlowColor", glowColor);
        }
    }

    private void ResetForNewPlayback()
    {
        if (observedPlaybackId == musicPlayback.PlaybackId)
        {
            return;
        }

        observedPlaybackId = musicPlayback.PlaybackId;
        HideText();
    }

    private void HideText()
    {
        isShowing = false;

        if (!isInitialized) { return; }

        if (judgmentText != null)
        {
            judgmentText.enabled = false;
        }

        if (textRect != null)
        {
            textRect.anchoredPosition = restPosition;
            textRect.localScale = restScale;
        }
    }

    private void OnDestroy()
    {
        if (neonMaterial == null) { return; }

        if (judgmentText != null &&
            judgmentText.fontSharedMaterial == neonMaterial)
        {
            judgmentText.fontSharedMaterial = originalTextMaterial;
        }

        Destroy(neonMaterial);
    }

    private void InitializeNeon()
    {
        originalTextMaterial = judgmentText.fontSharedMaterial;

        if (originalTextMaterial == null ||
            !originalTextMaterial.HasProperty("_GlowColor"))
        {
            Debug.LogWarning(
                "판정 글자에 TextMeshPro/Distance Field 머티리얼을 연결해 주세요.",
                this);

            return;
        }

        // 실행 중 변경할 전용 복사본을 사용한다.
        neonMaterial = new Material(originalTextMaterial);
        neonMaterial.name = "ForestJudgment_Neon_Runtime";
        neonMaterial.EnableKeyword("GLOW_ON");

        judgmentText.fontSharedMaterial = neonMaterial;

        // 글자 바깥으로 퍼지는 효과에 맞춰 여백을 갱신한다.
        judgmentText.UpdateMeshPadding();
    }

    private void SetGlowStrength(RhythmJudgment judgment)
    {
        switch (judgment)
        {
            case RhythmJudgment.Perfect:
                currentGlowStrength = perfectGlowStrength;
                break;

            case RhythmJudgment.Good:
                currentGlowStrength = goodGlowStrength;
                break;

            case RhythmJudgment.Normal:
                currentGlowStrength = normalGlowStrength;
                break;

            default:
                currentGlowStrength = 0.0f;
                break;
        }
    }
}