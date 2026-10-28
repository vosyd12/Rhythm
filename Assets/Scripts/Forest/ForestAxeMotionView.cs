using UnityEngine;
using UnityEngine.UI;

// 좌우 도끼의 준비, 충전, 타격과 복귀 동작을 담당한다.
// 입력 판정과 목재 지급은 변경하지 않는다.
public class ForestAxeMotionView : MonoBehaviour
{
    [Header("기존 시스템")]
    [SerializeField] private MusicPlayback musicPlayback;
    [SerializeField] private RhythmInputTester rhythmInputTester;
    [SerializeField] private RhythmEventTester rhythmEventTester;

    [Header("도끼 회전 중심")]
    [SerializeField] private RectTransform leftAxePivot;
    [SerializeField] private RectTransform rightAxePivot;

    [Header("오른쪽 도끼 기준 각도")]
    [SerializeField] private float readyAngle = 65.0f;
    [SerializeField] private float contactAngle = 0.0f;
    [SerializeField] private float followThroughAngle = -25.0f;

    [Header("기본 타격 시간")]
    [SerializeField, Min(0.01f)]
    private float prepareSeconds = 0.3f;

    [SerializeField, Min(0.01f)]
    private float contactSeconds = 0.045f;

    [SerializeField, Min(0.01f)]
    private float followThroughSeconds = 0.07f;

    [SerializeField, Min(0.01f)]
    private float returnSeconds = 0.16f;

    [Header("Hold 충전")]
    [SerializeField, Min(0.01f)]
    private float chargeLiftSeconds = 0.22f;

    // 준비 각도에 더해지는 뒤로 젖히기 각도.
    [SerializeField, Min(0.0f)]
    private float chargePullbackAngle = 55.0f;

    // 마지막 한 박에 추가로 젖히는 각도.
    [SerializeField, Min(0.0f)]
    private float chargeFinalPullbackAngle = 15.0f;

    [SerializeField, Min(0.0f)]
    private float chargeShakeAngle = 5.0f;

    [SerializeField, Min(0.0f)]
    private float chargeShakeFrequency = 9.0f;

    [Header("Hold 마지막 강타")]
    [SerializeField, Min(0.01f)]
    private float finishContactSeconds = 0.04f;

    [SerializeField]
    private float finishFollowThroughAngle = -80.0f;

    [SerializeField, Min(0.01f)]
    private float finishFollowThroughSeconds = 0.07f;

    [SerializeField, Min(0.0f)]
    private float finishReboundExtraAngle = 15.0f;

    [SerializeField, Min(0.01f)]
    private float finishReboundSeconds = 0.10f;

    [SerializeField, Min(0.01f)]
    private float finishSettleSeconds = 0.14f;

    [Header("충전 외형")]

    [SerializeField]
    private Image leftAxeHead;

    [SerializeField]
    private Image rightAxeHead;

    [SerializeField]
    private Outline leftChargeOutline;

    [SerializeField]
    private Outline rightChargeOutline;

    [SerializeField, Range(1.0f, 2.0f)]
    private float maximumChargeScale = 1.35f;

    [SerializeField]
    private Color chargedHeadColor =
        new Color(0.85f, 1.0f, 1.0f, 1.0f);

    [SerializeField]
    private Color chargeOutlineColor =
        new Color(0.10f, 0.80f, 1.0f, 0.85f);

    [SerializeField, Min(0.0f)]
    private float maximumOutlineDistance = 6.0f;

    [Header("참격")]

    [SerializeField]
    private ForestSlashGraphic leftSlash;

    [SerializeField]
    private ForestSlashGraphic rightSlash;

    [SerializeField, Min(0.01f)]
    private float slashFadeSeconds = 0.12f;

    private enum Motion
    {
        Waiting,
        Strike,
        Charging,
        Finish,
        Cancel
    }

    private sealed class AxeState
    {
        public RectTransform Pivot;
        public float Direction;
        public Motion CurrentMotion;
        public double StartedAt;

        public float Angle;
        public float StartAngle;

        public Image Head;
        public Outline ChargeOutline;

        public Vector3 RestHeadScale;
        public Color RestHeadColor;

        public float ChargeAmount;
        public float ReleasedChargeAmount;

        public ForestSlashGraphic Slash;
        public Vector3 RestSlashScale;
        public float SlashScale;

        // 최초 밝기와 사라지는 속도를 각 도끼가 따로 기억한다.
        public float SlashStartAlpha;
        public float SlashFadePower;
    }

    private AxeState leftAxe;
    private AxeState rightAxe;

    private RhythmNoteData holdNote;
    private double holdSecondsPerBeat;

    private bool isInitialized;
    private int observedPlaybackId = -1;

    private void Awake()
    {
        if (musicPlayback == null ||
            rhythmInputTester == null ||
            rhythmEventTester == null ||
            leftAxePivot == null ||
            rightAxePivot == null ||
            leftAxePivot == rightAxePivot ||
            leftAxeHead == null ||
            rightAxeHead == null ||
            leftAxeHead == rightAxeHead ||
            leftChargeOutline == null ||
            rightChargeOutline == null ||
            leftSlash == null ||
            rightSlash == null ||
            leftSlash == rightSlash)
        {
            Debug.LogError(
             "기존 시스템과 좌우 도끼 중심, 날 이미지, " +
             "충전 Outline, 서로 다른 좌우 Slash를 연결해 주세요.",
             this);

            enabled = false;
            return;
        }

        if (!leftAxeHead.transform.IsChildOf(leftAxePivot) ||
            !rightAxeHead.transform.IsChildOf(rightAxePivot) ||
            leftAxeHead.rectTransform == leftAxePivot ||
            rightAxeHead.rectTransform == rightAxePivot ||
            leftChargeOutline.gameObject != leftAxeHead.gameObject ||
            rightChargeOutline.gameObject != rightAxeHead.gameObject)
        {
            Debug.LogError(
                "각 도끼 중심 아래의 AxeHead와 " +
                "그 AxeHead에 붙은 Outline을 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        leftAxe = new AxeState
        {
            Pivot = leftAxePivot,
            Direction = -1.0f,
            Head = leftAxeHead,
            ChargeOutline = leftChargeOutline,
            RestHeadScale = leftAxeHead.rectTransform.localScale,
            RestHeadColor = leftAxeHead.color,
            Slash = leftSlash,
            RestSlashScale = leftSlash.rectTransform.localScale
        };

        rightAxe = new AxeState
        {
            Pivot = rightAxePivot,
            Direction = 1.0f,
            Head = rightAxeHead,
            ChargeOutline = rightChargeOutline,
            RestHeadScale = rightAxeHead.rectTransform.localScale,
            RestHeadColor = rightAxeHead.color,
            Slash = rightSlash,
            RestSlashScale = rightSlash.rectTransform.localScale
        };

        leftChargeOutline.useGraphicAlpha = true;
        rightChargeOutline.useGraphicAlpha = true;

        leftSlash.raycastTarget = false;
        rightSlash.raycastTarget = false;
        isInitialized = true;
        ResetVisuals();
    }

    private void OnEnable()
    {
        if (!isInitialized) { return; }

        rhythmInputTester.HoldStarted += HandleHoldStarted;
        rhythmInputTester.NoteJudged += HandleJudgment;

        observedPlaybackId = musicPlayback.PlaybackId;
        ResetVisuals();
    }

    private void OnDisable()
    {
        if (rhythmInputTester != null)
        {
            rhythmInputTester.HoldStarted -= HandleHoldStarted;
            rhythmInputTester.NoteJudged -= HandleJudgment;
        }

        ResetVisuals();
    }

    private void HandleHoldStarted(
        RhythmNoteData note,
        double inputSongTime)
    {
        if (!CanReceiveEffects()) { return; }

        ResetForNewPlayback();

        int approachBeats = rhythmEventTester.ApproachBeats;

        if (approachBeats < 1) { return; }

        double secondsPerBeat =
            rhythmEventTester.ApproachSeconds / approachBeats;

        if (!(secondsPerBeat > 0.0) ||
            double.IsInfinity(secondsPerBeat))
        {
            Debug.LogWarning(
                "Hold 충전에 사용할 박자 간격을 확인해 주세요.",
                this);

            return;
        }

        holdNote = note;
        holdSecondsPerBeat = secondsPerBeat;

        // 시작 시 타격하지 않고 충전 자세로 들어간다.
        BeginMotion(
            GetAxe(note.Side),
            Motion.Charging,
            inputSongTime);
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

        bool finishesActiveHold =
            holdNote != null &&
            holdNote.Id == note.Id;

        if (finishesActiveHold)
        {
            holdNote = null;
        }

        if (judgment == RhythmJudgment.Miss)
        {
            // 반대쪽 Tap의 Miss로 충전 중인 팔을 취소하지 않는다.
            if (finishesActiveHold)
            {
                BeginMotion(
                    GetAxe(note.Side),
                    Motion.Cancel,
                    songTime);
            }

            return;
        }

        if (note.Kind == ForestNoteKind.Sync)
        {
            BeginMotion(leftAxe, Motion.Strike, songTime);
            BeginMotion(rightAxe, Motion.Strike, songTime);

            BeginSlash(leftAxe, judgment);
            BeginSlash(rightAxe, judgment);
            return;
        }

        AxeState axe = GetAxe(note.Side);

        BeginMotion(
            axe,
            note.IsHold ? Motion.Finish : Motion.Strike,
            songTime);

        BeginSlash(axe, judgment);
    }

    private void LateUpdate()
    {
        if (!isInitialized) { return; }

        ResetForNewPlayback();

        if (!musicPlayback.IsGameplayPlayback ||
            !rhythmInputTester.isActiveAndEnabled ||
            !rhythmEventTester.isActiveAndEnabled)
        {
            ResetVisuals();
            return;
        }

        if (musicPlayback.IsPaused) { return; }

        if (!musicPlayback.TryGetSongTime(out double songTime))
        {
            ResetVisuals();
            return;
        }

        ValidateActiveHold(songTime);

        UpdateAxe(leftAxe, RhythmInputSide.Left, songTime);
        UpdateAxe(rightAxe, RhythmInputSide.Right, songTime);

        UpdateChargeAppearance(leftAxe, songTime);
        UpdateChargeAppearance(rightAxe, songTime);

        UpdateSlash(leftAxe, songTime);
        UpdateSlash(rightAxe, songTime);
    }

    // 판정기에서 Hold가 해제됐을 때 충전 자세가 남지 않도록 한다.
    private void ValidateActiveHold(double songTime)
    {
        if (holdNote == null) { return; }

        RhythmNoteData activeHold = rhythmInputTester.ActiveHoldNote;

        if (activeHold != null && activeHold.Id == holdNote.Id)
        {
            return;
        }

        // 判定側でHoldが解除された場合、充電姿勢を残さない。
        BeginMotion(
            GetAxe(holdNote.Side),
            Motion.Cancel,
            songTime);

        holdNote = null;
    }

    private void BeginMotion(
        AxeState axe,
        Motion motion,
        double songTime)
    {
        HideSlash(axe);

        // 놓기 또는 취소 직전의 충전 외형을 기억한다.
        axe.ReleasedChargeAmount = axe.ChargeAmount;

        axe.StartAngle = axe.Pivot.gameObject.activeSelf
            ? axe.Angle
            : readyAngle;

        axe.CurrentMotion = motion;
        axe.StartedAt = songTime;

        if (motion == Motion.Charging || motion == Motion.Cancel)
        {
            // 충전과 취소는 현재 자세에서 이어간다.
            ShowAtAngle(axe, axe.StartAngle);
            return;
        }

        // 성공 판정과 기존 나무·섬광 연출이 발생하는 순간에 접촉한다.
        // 강타를 늦게 시작해서 판정과 어긋나게 만들지 않는다.
        ShowAtAngle(axe, contactAngle);
    }

    private void UpdateAxe(
        AxeState axe,
        RhythmInputSide side,
        double songTime)
    {
        if (axe.CurrentMotion == Motion.Waiting)
        {
            ShowPreparation(axe, side, songTime);
            return;
        }

        float elapsed = Mathf.Max(
            0.0f,
            (float)(songTime - axe.StartedAt));

        if (axe.CurrentMotion == Motion.Charging)
        {
            UpdateCharge(axe, elapsed, songTime);
            return;
        }

        if (axe.CurrentMotion == Motion.Finish)
        {
            UpdateFinish(axe, elapsed);
            return;
        }

        float returnDuration = Mathf.Max(0.01f, returnSeconds);

        if (axe.CurrentMotion == Motion.Cancel)
        {
            float progress = elapsed / returnDuration;

            if (progress >= 1.0f)
            {
                FinishMotion(axe);
                return;
            }

            ShowAtAngle(
                axe,
                Mathf.Lerp(
                    axe.StartAngle,
                    readyAngle,
                    Smooth(progress)));

            return;
        }

        // 기존 Tap과 Sync의 타격 동작.
        float contactDuration = Mathf.Max(0.01f, contactSeconds);

        if (elapsed < contactDuration)
        {
            ShowAtAngle(axe, contactAngle);
            return;
        }

        elapsed -= contactDuration;

        float followDuration =
            Mathf.Max(0.01f, followThroughSeconds);

        if (elapsed < followDuration)
        {
            float progress = elapsed / followDuration;
            float remaining = 1.0f - progress;
            float eased = 1.0f - remaining * remaining;

            ShowAtAngle(
                axe,
                Mathf.Lerp(
                    contactAngle,
                    followThroughAngle,
                    eased));

            return;
        }

        elapsed -= followDuration;

        float returnProgress = elapsed / returnDuration;

        if (returnProgress >= 1.0f)
        {
            FinishMotion(axe);
            return;
        }

        ShowAtAngle(
            axe,
            Mathf.Lerp(
                followThroughAngle,
                readyAngle,
                Smooth(returnProgress)));
    }

    private void UpdateCharge(
        AxeState axe,
        float elapsed,
        double songTime)
    {
        if (holdNote == null)
        {
            FinishMotion(axe);
            return;
        }

        // 충전 정도는 실제 Hold의 목표 구간을 기준으로 계산한다.
        float progress = Mathf.Clamp01(
            (float)(
                (songTime - holdNote.TargetTimeSeconds) /
                holdNote.DurationSeconds));

        // 짧은 Hold도 충전 자세로 들어갈 시간이 확보되게 한다.
        float liftDuration = Mathf.Max(
            0.01f,
            Mathf.Min(
                chargeLiftSeconds,
                (float)holdNote.DurationSeconds * 0.25f));

        float lift = Smooth(elapsed / liftDuration);

        // 처음부터 크게 젖히고, 유지할수록 추가로 힘을 모은다.
        float pullback =
            chargePullbackAngle *
            Mathf.Lerp(0.65f, 1.0f, progress);

        float lastBeatProgress = Mathf.Clamp01(
            (float)(
                1.0 -
                (holdNote.EndTimeSeconds - songTime) /
                holdSecondsPerBeat));

        float targetAngle =
            readyAngle +
            pullback +
            chargeFinalPullbackAngle * Smooth(lastBeatProgress);

        // 마지막에 가까워질수록 진동이 두드러진다.
        float shake =
            Mathf.Sin(
                elapsed * chargeShakeFrequency * Mathf.PI * 2.0f) *
            chargeShakeAngle *
            progress * progress *
            lift;

        ShowAtAngle(
            axe,
            Mathf.Lerp(axe.StartAngle, targetAngle, lift) + shake);
    }

    private void UpdateFinish(AxeState axe, float elapsed)
    {
        // 음악이나 다른 팔을 멈추지 않고, 이 도끼만 접촉 자세를 유지한다.
        float contactDuration =
            Mathf.Max(0.01f, finishContactSeconds);

        if (elapsed < contactDuration)
        {
            ShowAtAngle(axe, contactAngle);
            return;
        }

        elapsed -= contactDuration;

        float followDuration =
            Mathf.Max(0.01f, finishFollowThroughSeconds);

        if (elapsed < followDuration)
        {
            float progress = elapsed / followDuration;
            float remaining = 1.0f - progress;
            float eased = 1.0f - remaining * remaining;

            ShowAtAngle(
                axe,
                Mathf.Lerp(
                    contactAngle,
                    finishFollowThroughAngle,
                    eased));

            return;
        }

        elapsed -= followDuration;

        float reboundAngle = readyAngle + finishReboundExtraAngle;
        float reboundDuration =
            Mathf.Max(0.01f, finishReboundSeconds);

        if (elapsed < reboundDuration)
        {
            ShowAtAngle(
                axe,
                Mathf.Lerp(
                    finishFollowThroughAngle,
                    reboundAngle,
                    Smooth(elapsed / reboundDuration)));

            return;
        }

        elapsed -= reboundDuration;

        float settleProgress =
            elapsed / Mathf.Max(0.01f, finishSettleSeconds);

        if (settleProgress >= 1.0f)
        {
            FinishMotion(axe);
            return;
        }

        ShowAtAngle(
            axe,
            Mathf.Lerp(
                reboundAngle,
                readyAngle,
                Smooth(settleProgress)));
    }

    private void ShowPreparation(
        AxeState axe,
        RhythmInputSide side,
        double songTime)
    {
        int offset = rhythmInputTester.HasJudged ? 1 : 0;

        if (!rhythmEventTester.TryGetUpcomingNote(
                offset, out RhythmNoteData note))
        {
            SetVisible(axe, false);
            return;
        }

        bool matchesSide =
            note.Side == side ||
            note.Kind == ForestNoteKind.Sync;

        double remaining = note.TargetTimeSeconds - songTime;
        double lateWindow = rhythmInputTester.NormalWindowMs / 1000.0;

        if (!note.IsResource ||
            !matchesSide ||
            remaining > prepareSeconds ||
            remaining < -lateWindow)
        {
            SetVisible(axe, false);
            return;
        }

        ShowAtAngle(axe, readyAngle);
    }

    private AxeState GetAxe(RhythmInputSide side)
    {
        return side == RhythmInputSide.Left ? leftAxe : rightAxe;
    }

    private void ShowAtAngle(AxeState axe, float angle)
    {
        axe.Angle = angle;

        axe.Pivot.localRotation = Quaternion.Euler(
            0.0f,
            0.0f,
            angle * axe.Direction);

        SetVisible(axe, true);
    }

    private void SetVisible(AxeState axe, bool visible)
    {
        if (axe.Pivot == null) { return; }

        GameObject target = axe.Pivot.gameObject;

        if (target.activeSelf != visible)
        {
            target.SetActive(visible);
        }
    }

    private void FinishMotion(AxeState axe)
    {
        axe.CurrentMotion = Motion.Waiting;

        HideSlash(axe);

        ApplyChargeAppearance(axe, 0.0f, false);
        axe.ReleasedChargeAmount = 0.0f;

        SetVisible(axe, false);
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
        ResetVisuals();
    }

    private void ResetVisuals()
    {
        if (!isInitialized) { return; }

        holdNote = null;
        holdSecondsPerBeat = 0.0;

        FinishMotion(leftAxe);
        FinishMotion(rightAxe);
    }

    private static float Smooth(float progress)
    {
        float t = Mathf.Clamp01(progress);
        return t * t * (3.0f - 2.0f * t);
    }

    private void UpdateChargeAppearance(
    AxeState axe,
    double songTime)
    {
        float elapsed = Mathf.Max(
            0.0f,
            (float)(songTime - axe.StartedAt));

        float amount = 0.0f;
        bool showImpactWhite = false;

        switch (axe.CurrentMotion)
        {
            case Motion.Charging:
                {
                    if (holdNote == null)
                    {
                        break;
                    }

                    float progress = Mathf.Clamp01(
                        (float)(
                            (songTime - holdNote.TargetTimeSeconds) /
                            holdNote.DurationSeconds));

                    // 충전 시작 순간에는 부드럽게 켜지고,
                    // 이후 유지 시간에 맞춰 확대와 밝기가 증가한다.
                    float activation = Smooth(elapsed / 0.12f);

                    amount = Mathf.Lerp(0.10f, 1.0f, progress) * activation;
                    break;
                }

            case Motion.Finish:
                {
                    float duration =
                        Mathf.Max(0.01f, finishContactSeconds) +
                        Mathf.Max(0.01f, finishFollowThroughSeconds) +
                        Mathf.Max(0.01f, finishReboundSeconds) +
                        Mathf.Max(0.01f, finishSettleSeconds);

                    float remaining =
                        1.0f - Smooth(elapsed / duration);

                    amount = axe.ReleasedChargeAmount * remaining;

                    showImpactWhite =
                        elapsed < Mathf.Max(0.01f, finishContactSeconds);

                    break;
                }

            case Motion.Cancel:
                {
                    float remaining =
                        1.0f - Smooth(
                            elapsed / Mathf.Max(0.01f, returnSeconds));

                    amount = axe.ReleasedChargeAmount * remaining;
                    break;
                }
        }

        ApplyChargeAppearance(axe, amount, showImpactWhite);
    }

    private void ApplyChargeAppearance(
        AxeState axe,
        float amount,
        bool showImpactWhite)
    {
        amount = Mathf.Clamp01(amount);
        axe.ChargeAmount = amount;

        // 씬 종료 중 자식 이미지가 먼저 제거될 수 있다.
        if (axe.Head != null)
        {
            float scale = Mathf.Lerp(
                1.0f,
                maximumChargeScale,
                amount);

            // 회전 중심과 손잡이는 유지하고 날만 확대한다.
            axe.Head.rectTransform.localScale = Vector3.Scale(
                axe.RestHeadScale,
                new Vector3(scale, scale, 1.0f));

            Color color = showImpactWhite
                ? Color.white
                : Color.Lerp(
                    axe.RestHeadColor,
                    chargedHeadColor,
                    amount);

            color.a = axe.RestHeadColor.a;
            axe.Head.color = color;
        }

        if (axe.ChargeOutline != null)
        {
            Color outlineColor = chargeOutlineColor;
            outlineColor.a *= amount;

            axe.ChargeOutline.effectColor = outlineColor;

            float distance = maximumOutlineDistance * amount;

            axe.ChargeOutline.effectDistance =
                new Vector2(distance, distance);

            axe.ChargeOutline.enabled = amount > 0.001f;
        }
    }

    private void BeginSlash(
       AxeState axe,
       RhythmJudgment judgment)
    {
        if (axe.Slash == null)
        {
            return;
        }

        if (judgment == RhythmJudgment.Miss)
        {
            HideSlash(axe);
            return;
        }

        Color slashColor;
        float scale;
        float startAlpha;
        float fadePower;

        switch (judgment)
        {
            case RhythmJudgment.Perfect:
                slashColor = new Color(
                    0.3f, 1.0f, 0.5f, 1.0f);

                scale = 1.4f;
                startAlpha = 1.0f;
                fadePower = 0.9f;
                break;

            case RhythmJudgment.Good:
                slashColor = new Color(
                    1.0f, 0.85f, 0.2f, 1.0f);

                scale = 1.2f;
                startAlpha = 0.85f;
                fadePower = 1.4f;
                break;

            default:
                slashColor = Color.white;

                scale = 1.0f;
                startAlpha = 0.65f;
                fadePower = 2.0f;
                break;
        }

        // Hold 성공은 기존 크기 확대에 밝기와 잔상을 더한다.
        if (axe.CurrentMotion == Motion.Finish)
        {
            scale *= 1.35f;

            startAlpha = Mathf.Clamp01(
                startAlpha * 1.2f);

            fadePower *= 0.75f;
        }

        axe.SlashScale = scale;
        axe.SlashStartAlpha = startAlpha;
        axe.SlashFadePower = fadePower;

        axe.Slash.rectTransform.localScale = Vector3.Scale(
            axe.RestSlashScale,
            new Vector3(scale * 0.9f, scale * 0.9f, 1.0f));

        slashColor.a = startAlpha;

        axe.Slash.color = slashColor;
        axe.Slash.enabled = true;
    }

    private void UpdateSlash(AxeState axe, double songTime)
    {
        if (axe.Slash == null || !axe.Slash.enabled)
        {
            return;
        }

        bool isStrike = axe.CurrentMotion == Motion.Strike;
        bool isFinish = axe.CurrentMotion == Motion.Finish;

        if (!isStrike && !isFinish)
        {
            HideSlash(axe);
            return;
        }

        // 도끼가 복귀하거나 반동으로 돌아오기 전에 참격을 끝낸다.
        float motionDuration = isFinish
            ? Mathf.Max(0.01f, finishContactSeconds) +
              Mathf.Max(0.01f, finishFollowThroughSeconds)
            : Mathf.Max(0.01f, contactSeconds) +
              Mathf.Max(0.01f, followThroughSeconds);

        float duration = Mathf.Min(
            Mathf.Max(0.01f, slashFadeSeconds),
            motionDuration);

        float elapsed = Mathf.Max(
            0.0f,
            (float)(songTime - axe.StartedAt));

        float progress = Mathf.Clamp01(elapsed / duration);

        if (progress >= 1.0f)
        {
            HideSlash(axe);
            return;
        }

        // 참격이 살짝 퍼지면서 흐려진다.
        float scale =
            axe.SlashScale * Mathf.Lerp(0.9f, 1.15f, progress);

        axe.Slash.rectTransform.localScale = Vector3.Scale(
            axe.RestSlashScale,
            new Vector3(scale, scale, 1.0f));

        float remaining = 1.0f - progress;

        Color slashColor = axe.Slash.color;

        // 직전 프레임의 알파를 누적해서 줄이지 않고,
        // 시작 밝기와 현재 진행률로 매번 계산한다.
        slashColor.a =
            axe.SlashStartAlpha *
            Mathf.Pow(remaining, axe.SlashFadePower);

        axe.Slash.color = slashColor;
    }

    private void HideSlash(AxeState axe)
    {
        if (axe.Slash == null)
        {
            return;
        }

        axe.Slash.enabled = false;
        axe.Slash.rectTransform.localScale = axe.RestSlashScale;
    }
}