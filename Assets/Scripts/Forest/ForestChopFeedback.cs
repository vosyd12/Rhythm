using UnityEngine;
using UnityEngine.UI;

// 타격 표시의 색상과 사라지는 효과만 담당한다.
// 도끼 회전, 입력 판정, 목재 지급은 처리하지 않는다.
public class ForestChopFeedback : MonoBehaviour
{
    [Header("기존 시스템")]
    [SerializeField] private MusicPlayback musicPlayback;
    [SerializeField] private RhythmInputTester rhythmInputTester;
    [SerializeField] private ForestAxeMotionView axeMotionView;

    [Header("좌우 타격 표시")]
    [SerializeField] private Image hitFlash;
    [SerializeField] private Image leftHitFlash;

    [Header("표시 시간")]
    [SerializeField, Min(0.0f)]
    private float hitHoldSeconds = 0.04f;

    [SerializeField, Min(0.01f)]
    private float recoverySeconds = 0.18f;

    [Header("판정별 섬광 크기")]

    [SerializeField, Min(0.1f)]
    private float normalFlashScale = 1.0f;

    [SerializeField, Min(0.1f)]
    private float goodFlashScale = 1.35f;

    [SerializeField, Min(0.1f)]
    private float perfectFlashScale = 1.75f;

    [Header("타격 화면 흔들림")]

    [SerializeField]
    private RectTransform shakeRoot;

    [SerializeField, Min(0.0f)]
    private float normalShakeDistance = 2.0f;

    [SerializeField, Min(0.0f)]
    private float goodShakeDistance = 4.0f;

    [SerializeField, Min(0.0f)]
    private float perfectShakeDistance = 6.0f;

    [SerializeField, Min(0.01f)]
    private float shakeDuration = 0.12f;

    private Vector2 shakeRestPosition;
    private Vector2 shakeDirection;

    private double shakeStartedAt;
    private float shakeDistance;
    private bool isShaking;

    private sealed class FlashState
    {
        public Image Image;
        public Color StartColor;
        public double StartedAt;
        public int NoteId = -1;
        public bool IsActive;

        public Vector3 BaseScale;
        public float SizeMultiplier;
    }

    private FlashState leftFlash;
    private FlashState rightFlash;

    private bool isInitialized;
    private int observedPlaybackId = -1;

    private void Awake()
    {
        if (musicPlayback == null ||
            rhythmInputTester == null ||
            axeMotionView == null ||
            hitFlash == null ||
            leftHitFlash == null ||
            hitFlash == leftHitFlash)
        {
            Debug.LogError(
                "음악, 입력기, 도끼 연출기와 서로 다른 좌우 타격 표시를 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        if (!(hitHoldSeconds >= 0.0f && recoverySeconds > 0.0f) ||
            float.IsInfinity(hitHoldSeconds) ||
            float.IsInfinity(recoverySeconds))
        {
            Debug.LogError(
                "타격 표시 시간을 확인해 주세요.",
                this);

            enabled = false;
            return;
        }

        leftFlash = new FlashState
        {
            Image = leftHitFlash,
            BaseScale = leftHitFlash.rectTransform.localScale,
            SizeMultiplier = 1.0f
        };

        rightFlash = new FlashState
        {
            Image = hitFlash,
            BaseScale = hitFlash.rectTransform.localScale,
            SizeMultiplier = 1.0f
        };

        leftHitFlash.raycastTarget = false;
        hitFlash.raycastTarget = false;

        if (shakeRoot != null)
        {
            shakeRestPosition = shakeRoot.anchoredPosition;
        }

        isInitialized = true;
        ResetFeedback();
    }

    private void OnEnable()
    {
        if (!isInitialized) { return; }

        rhythmInputTester.NoteJudged += HandleJudgment;

        observedPlaybackId = musicPlayback.PlaybackId;
        ResetFeedback();
    }

    private void OnDisable()
    {
        if (rhythmInputTester != null)
        {
            rhythmInputTester.NoteJudged -= HandleJudgment;
        }

        ResetFeedback();
    }

    private void HandleJudgment(
        RhythmNoteData note,
        RhythmJudgment judgment,
        double songTime)
    {
        if (!CanReceiveEffects() || !note.IsResource)
        {
            return;
        }

        ResetForNewPlayback();

        if (judgment == RhythmJudgment.Miss)
        {
            // 실패한 노트의 효과만 제거한다.
            // 반대쪽 Hold나 다른 노트의 타격 표시는 유지한다.
            if (leftFlash.NoteId == note.Id)
            {
                HideFlash(leftFlash);
            }

            if (rightFlash.NoteId == note.Id)
            {
                HideFlash(rightFlash);
            }

            return;
        }

        ShowFlash(
            note,
            GetJudgmentColor(judgment),
            GetJudgmentScale(judgment),
            songTime);

        BeginStageShake(note, judgment, songTime);
    }

    private void LateUpdate()
    {
        if (!isInitialized) { return; }

        ResetForNewPlayback();

        if (!musicPlayback.IsGameplayPlayback ||
            !rhythmInputTester.isActiveAndEnabled ||
            !axeMotionView.isActiveAndEnabled)
        {
            ResetFeedback();
            return;
        }

        if (musicPlayback.IsPaused) { return; }

        if (!musicPlayback.TryGetSongTime(out double songTime))
        {
            ResetFeedback();
            return;
        }

        UpdateFlash(leftFlash, songTime);
        UpdateFlash(rightFlash, songTime);
        UpdateStageShake(songTime);
    }

    private void ShowFlash(
     RhythmNoteData note,
     Color color,
     float sizeMultiplier,
     double songTime)
    {
        if (note.Side == RhythmInputSide.Left ||
            note.Side == RhythmInputSide.Both)
        {
            BeginFlash(
                leftFlash, note.Id, color, sizeMultiplier, songTime);
        }

        if (note.Side == RhythmInputSide.Right ||
            note.Side == RhythmInputSide.Both)
        {
            BeginFlash(
                rightFlash, note.Id, color, sizeMultiplier, songTime);
        }
    }

    private void BeginFlash(
      FlashState flash,
      int noteId,
      Color color,
      float sizeMultiplier,
      double songTime)
    {
        flash.NoteId = noteId;
        flash.StartColor = color;
        flash.SizeMultiplier = Mathf.Max(0.1f, sizeMultiplier);
        flash.StartedAt = songTime;
        flash.IsActive = true;

        flash.Image.rectTransform.localScale =
            Vector3.Scale(
                flash.BaseScale,
                new Vector3(
                    flash.SizeMultiplier,
                    flash.SizeMultiplier,
                    1.0f));

        flash.Image.color = color;
        flash.Image.enabled = true;
    }

    private void UpdateFlash(FlashState flash, double songTime)
    {
        if (!flash.IsActive) { return; }

        float elapsed = Mathf.Max(
            0.0f,
            (float)(songTime - flash.StartedAt));

        float progress = Mathf.Clamp01(
            (elapsed - hitHoldSeconds) / recoverySeconds);

        if (progress >= 1.0f)
        {
            HideFlash(flash);
            return;
        }

        Color color = flash.StartColor;
        color.a *= 1.0f - progress;

        flash.Image.color = color;

        // 처음에는 크게 표시하고, 흐려지면서 조금 작아진다.
        float scale =
            flash.SizeMultiplier * Mathf.Lerp(1.0f, 0.75f, progress);

        flash.Image.rectTransform.localScale =
            Vector3.Scale(
                flash.BaseScale,
                new Vector3(scale, scale, 1.0f));
    }

    private void HideFlash(FlashState flash)
    {
        flash.IsActive = false;
        flash.NoteId = -1;

        // 씬 종료 시 이미지가 먼저 제거될 수 있다.
        if (flash.Image != null)
        {
            flash.Image.enabled = false;
            flash.Image.rectTransform.localScale = flash.BaseScale;
        }
    }

    private static Color GetJudgmentColor(RhythmJudgment judgment)
    {
        switch (judgment)
        {
            case RhythmJudgment.Perfect:
                return new Color(0.3f, 1.0f, 0.5f, 1.0f);

            case RhythmJudgment.Good:
                return new Color(1.0f, 0.85f, 0.2f, 1.0f);

            default:
                return Color.white;
        }
    }

    private bool CanReceiveEffects()
    {
        return isInitialized &&
               isActiveAndEnabled &&
               musicPlayback.IsGameplayPlayback;
    }

    private void ResetForNewPlayback()
    {
        if (observedPlaybackId == musicPlayback.PlaybackId)
        {
            return;
        }

        observedPlaybackId = musicPlayback.PlaybackId;
        ResetFeedback();
    }

    private void ResetFeedback()
    {
        if (!isInitialized) { return; }

        HideFlash(leftFlash);
        HideFlash(rightFlash);

        ResetStageShake();
    }

    private float GetJudgmentScale(RhythmJudgment judgment)
    {
        switch (judgment)
        {
            case RhythmJudgment.Perfect:
                return perfectFlashScale;

            case RhythmJudgment.Good:
                return goodFlashScale;

            default:
                return normalFlashScale;
        }
    }

    private void BeginStageShake(
    RhythmNoteData note,
    RhythmJudgment judgment,
    double songTime)
    {
        if (shakeRoot == null)
        {
            return;
        }

        switch (judgment)
        {
            case RhythmJudgment.Perfect:
                shakeDistance = perfectShakeDistance;
                break;

            case RhythmJudgment.Good:
                shakeDistance = goodShakeDistance;
                break;

            default:
                shakeDistance = normalShakeDistance;
                break;
        }

        switch (note.Side)
        {
            case RhythmInputSide.Left:
                shakeDirection = Vector2.left;
                break;

            case RhythmInputSide.Right:
                shakeDirection = Vector2.right;
                break;

            default:
                // Sync는 좌우 대신 아래쪽 충격을 준다.
                shakeDirection = Vector2.down;
                break;
        }

        shakeStartedAt = songTime;
        isShaking = true;
    }

    private void UpdateStageShake(double songTime)
    {
        if (!isShaking || shakeRoot == null)
        {
            return;
        }

        float elapsed = Mathf.Max(
            0.0f,
            (float)(songTime - shakeStartedAt));

        float duration = Mathf.Max(0.01f, shakeDuration);
        float progress = Mathf.Clamp01(elapsed / duration);

        if (progress >= 1.0f)
        {
            ResetStageShake();
            return;
        }

        // 처음에 강하고, 끝으로 갈수록 빠르게 약해진다.
        float decay = (1.0f - progress) * (1.0f - progress);

        // 짧게 왕복하면서 원래 위치로 돌아온다.
        float wave = Mathf.Cos(progress * Mathf.PI * 4.0f);

        Vector2 offset =
            shakeDirection * shakeDistance * wave * decay;

        shakeRoot.anchoredPosition =
            shakeRestPosition + offset;
    }

    private void ResetStageShake()
    {
        isShaking = false;

        if (shakeRoot != null)
        {
            shakeRoot.anchoredPosition = shakeRestPosition;
        }
    }
}