using System.Collections.Generic;
using UnityEngine;

// 최종 판정에 따른 나무 결과 연출만 처리한다.
// Sync도 판정은 하나지만 결과 나무는 좌우 두 개를 사용한다.
public class ForestTreeResultView : MonoBehaviour
{
    [SerializeField]
    private MusicPlayback musicPlayback;

    [SerializeField]
    private RhythmInputTester rhythmInputTester;

    [SerializeField]
    private RhythmBeatStepView stepView;

    [SerializeField]
    private RectTransform exitPoint;

    [SerializeField]
    private RectTransform leftExitPoint;

    [SerializeField]
    private RectTransform[] resultTrees = new RectTransform[2];

    [SerializeField, Min(0.01f)]
    private float fallSeconds = 0.3f;

    [SerializeField, Min(0.01f)]
    private float missExitSeconds = 0.4f;

    [SerializeField]
    private float fallAngle = -85.0f;

    [Header("Tap / Sync 타격 반응")]

    [SerializeField, Min(0.01f)]
    private float impactSeconds = 0.06f;

    [SerializeField, Range(0.0f, 60.0f)]
    private float impactAngle = 24.0f;

    [SerializeField, Min(0.0f)]
    private float impactPauseSeconds = 0.05f;

    [SerializeField, Min(0.01f)]
    private float resultFadeSeconds = 0.12f;

    [Header("Hold 강타 반응")]

    [SerializeField, Range(1.0f, 2.0f)]
    private float holdImpactAngleMultiplier = 1.5f;

    [SerializeField, Range(0.3f, 1.0f)]
    private float holdImpactTimeMultiplier = 0.75f;

    [SerializeField, Range(1.0f, 3.0f)]
    private float holdImpactPauseMultiplier = 1.5f;

    private sealed class TreeEffect
    {
        public RectTransform Root;
        public CanvasGroup Group;
        public ForestTreeAppearance Appearance;
        public Quaternion RestRotation;

        public Vector3 StartPosition;
        public Vector3 EndPosition;
        public Quaternion EndRotation;

        public bool IsActive;
        public bool IsSuccess;
        public float ElapsedSeconds;

        // 타격 직후 빠르게 꺾일 목표 회전.
        public Quaternion ImpactRotation;
        // Tap과 Sync에만 새로운 타격 연출을 적용한다.
        public bool UseImpactMotion;
        public bool IsHold;
    }

    private readonly List<TreeEffect> effects =
        new List<TreeEffect>();

    private bool isInitialized;
    private int observedPlaybackId = -1;

    private void Awake()
    {
        if (musicPlayback == null ||
            rhythmInputTester == null ||
            stepView == null ||
            exitPoint == null ||
            leftExitPoint == null ||
            resultTrees == null ||
            resultTrees.Length == 0)
        {
            FailInitialization(
                "음악, 입력, 표시기, 퇴장점과 결과 나무를 연결해 주세요.");
            return;
        }

        if (!(fallSeconds > 0.0f && missExitSeconds > 0.0f) ||
            float.IsInfinity(fallSeconds) ||
            float.IsInfinity(missExitSeconds) ||
            float.IsNaN(fallAngle) ||
            float.IsInfinity(fallAngle))
        {
            FailInitialization("결과 연출 시간과 각도를 확인해 주세요.");
            return;
        }

        if (exitPoint.parent != leftExitPoint.parent ||
            exitPoint.parent != stepView.StepLane)
        {
            FailInitialization(
                "좌우 퇴장점과 결과 나무는 Step Lane 바로 아래에 배치해 주세요.");
            return;
        }

        for (int i = 0; i < resultTrees.Length; i++)
        {
            RectTransform root = resultTrees[i];

            if (root == null || root.parent != exitPoint.parent)
            {
                FailInitialization(
                    $"결과 나무 {i + 1}번의 연결과 부모를 확인해 주세요.");
                return;
            }

            for (int j = 0; j < i; j++)
            {
                if (root == resultTrees[j])
                {
                    FailInitialization("결과 나무를 중복 연결하지 마세요.");
                    return;
                }
            }

            CanvasGroup group = root.GetComponent<CanvasGroup>();
            ForestTreeAppearance appearance =
                root.GetComponent<ForestTreeAppearance>();

            if (group == null ||
                appearance == null ||
                !appearance.IsConfigured)
            {
                FailInitialization(
                    $"{root.name}의 CanvasGroup과 ForestTreeAppearance를 확인해 주세요.");
                return;
            }

            AddEffect(
                root,
                group,
                appearance,
                root.localRotation);
        }

        isInitialized = true;
    }

    private TreeEffect AddEffect(
       RectTransform root,
       CanvasGroup group,
       ForestTreeAppearance appearance,
       Quaternion restRotation)
    {
        TreeEffect effect = new TreeEffect
        {
            Root = root,
            Group = group,
            Appearance = appearance,
            RestRotation = restRotation
        };

        effect.Group.interactable = false;
        effect.Group.blocksRaycasts = false;

        effects.Add(effect);
        Hide(effect);

        return effect;
    }

    private void OnEnable()
    {
        if (!isInitialized) { return; }

        rhythmInputTester.NoteJudged += HandleJudgment;
    }

    private void OnDisable()
    {
        if (rhythmInputTester != null)
        {
            rhythmInputTester.NoteJudged -= HandleJudgment;
        }

        ClearEffects();
    }

    private void HandleJudgment(
       RhythmNoteData note,
       RhythmJudgment judgment,
       double songTime)
    {
        if (!isInitialized ||
            !musicPlayback.IsGameplayPlayback)
        {
            return;
        }

        ResetForNewPlayback();

        // CurrentNote를 다시 읽지 않고,
        // 입력기가 전달한 실제 판정 노트를 사용한다.
        bool isSuccess = judgment != RhythmJudgment.Miss;

        if (note.Kind == ForestNoteKind.Sync)
        {
            // Sync의 판정은 한 번이다.
            // 같은 결과로 좌우 나무 연출만 각각 시작한다.
            StartResult(
                note,
                RhythmInputSide.Left,
                isSuccess);

            StartResult(
                note,
                RhythmInputSide.Right,
                isSuccess);
        }

        else
        {
            StartResult(
                note,
                note.Side,
                isSuccess);
        }
    }

    private void StartResult(
        RhythmNoteData note,
        RhythmInputSide side,
        bool isSuccess)
    {
        if (!stepView.TryGetResultPosition(
                note,
                isSuccess,
                side,
                out Vector3 position))
        {
            return;
        }

        // 아래의 기존 외형·쓰러짐 코드는 kind를 그대로 사용한다.
        ForestNoteKind kind = note.Kind;

        TreeEffect effect = FindAvailableEffect();

        effect.StartPosition = position;

        effect.EndPosition = side == RhythmInputSide.Left
            ? leftExitPoint.localPosition
            : exitPoint.localPosition;

        // 기존 좌우 쓰러짐 방향을 유지한다.
        float signedAngle = side == RhythmInputSide.Left
            ? -fallAngle
            : fallAngle;

        effect.EndRotation =
            effect.RestRotation *
            Quaternion.Euler(0.0f, 0.0f, signedAngle);

        float angleMultiplier = note.IsHold
            ? Mathf.Clamp(holdImpactAngleMultiplier, 1.0f, 2.0f)
            : 1.0f;

        // 첫 반동이 최종 쓰러짐 각도를 넘지 않도록 한다.
        float firstImpactAngle = Mathf.Min(
            Mathf.Max(0.0f, impactAngle) * angleMultiplier,
            Mathf.Abs(signedAngle));

        effect.ImpactRotation =
            effect.RestRotation *
            Quaternion.Euler(
                0.0f,
                0.0f,
                Mathf.Sign(signedAngle) * firstImpactAngle);

        // Tap, Sync, Hold의 성공 결과에 타격 반응을 적용한다.
        effect.UseImpactMotion = note.IsResource;
        effect.IsHold = note.IsHold;

        effect.IsSuccess = isSuccess;
        effect.ElapsedSeconds = 0.0f;
        effect.IsActive = true;

        // OnEnable의 초기화를 먼저 마친 뒤 표시 상태를 설정한다.
        effect.Root.gameObject.SetActive(true);

        effect.Root.localPosition = position;
        effect.Root.localRotation = effect.RestRotation;
        effect.Group.alpha = 1.0f;

        effect.Appearance.SetKind(kind);
    }

    private void Update()
    {
        if (!isInitialized) { return; }

        ResetForNewPlayback();

        if (!musicPlayback.IsGameplayPlayback)
        {
            ClearEffects();
            return;
        }

        if (musicPlayback.IsPaused) { return; }

        foreach (TreeEffect effect in effects)
        {
            if (!effect.IsActive) { continue; }

            effect.ElapsedSeconds += Time.unscaledDeltaTime;

            // Tap / Sync 성공은 단계별 타격 연출을 사용한다.
            if (effect.IsSuccess && effect.UseImpactMotion)
            {
                UpdateImpactResult(effect);
                continue;
            }

            // Hold 성공과 Miss는 기존 연출을 유지한다.
            float duration = effect.IsSuccess
                ? fallSeconds
                : missExitSeconds;

            float progress =
                Mathf.Clamp01(effect.ElapsedSeconds / duration);

            if (effect.IsSuccess)
            {
                effect.Root.localPosition = effect.StartPosition;

                float fallProgress = progress * progress;

                effect.Root.localRotation = Quaternion.Slerp(
                    effect.RestRotation,
                    effect.EndRotation,
                    fallProgress);
            }
            else
            {
                effect.Root.localRotation = effect.RestRotation;

                effect.Root.localPosition = Vector3.Lerp(
                    effect.StartPosition,
                    effect.EndPosition,
                    progress);
            }

            effect.Group.alpha =
                1.0f - Mathf.InverseLerp(0.5f, 1.0f, progress);

            if (progress >= 1.0f)
            {
                Hide(effect);
            }
        }
    }

    private TreeEffect FindAvailableEffect()
    {
        foreach (TreeEffect effect in effects)
        {
            if (!effect.IsActive)
            {
                return effect;
            }
        }

        // 모두 사용 중이면 기존 연출을 덮어쓰지 않고 하나 추가한다.
        // 추가한 나무도 목록에 남겨 다음 판정에서 재사용한다.
        RectTransform clone = Instantiate(
            resultTrees[0],
            exitPoint.parent,
            false);

        clone.name = $"TreeResult_Extra_{effects.Count}";

        // 복제 원본이 현재 쓰러지는 중일 수 있으므로
        // 현재 회전이 아닌 저장해 둔 준비 회전을 사용한다.
        return AddEffect(
            clone,
            clone.GetComponent<CanvasGroup>(),
            clone.GetComponent<ForestTreeAppearance>(),
            effects[0].RestRotation);
    }

    private void ResetForNewPlayback()
    {
        if (observedPlaybackId == musicPlayback.PlaybackId)
        {
            return;
        }

        observedPlaybackId = musicPlayback.PlaybackId;
        ClearEffects();
    }

    private void Hide(TreeEffect effect)
    {
        effect.IsActive = false;

        if (effect.Group != null)
        {
            effect.Group.alpha = 0.0f;
        }

        if (effect.Root != null)
        {
            effect.Root.localRotation = effect.RestRotation;
            effect.Root.gameObject.SetActive(false);
        }
    }

    private void ClearEffects()
    {
        foreach (TreeEffect effect in effects)
        {
            Hide(effect);
        }
    }

    private void FailInitialization(string message)
    {
        Debug.LogError(
            $"[나무 결과 연출 초기화 실패] {message}",
            this);

        enabled = false;
    }

    private void UpdateImpactResult(TreeEffect effect)
    {
        // 성공한 나무는 타격 위치에서 쓰러진다.
        effect.Root.localPosition = effect.StartPosition;

        float elapsed = effect.ElapsedSeconds;

        float impactTimeMultiplier = effect.IsHold
            ? Mathf.Clamp(holdImpactTimeMultiplier, 0.3f, 1.0f)
            : 1.0f;

        float pauseTimeMultiplier = effect.IsHold
            ? Mathf.Clamp(holdImpactPauseMultiplier, 1.0f, 3.0f)
            : 1.0f;

        float impactDuration = Mathf.Max(
            0.01f,
            impactSeconds * impactTimeMultiplier);

        float pauseDuration = Mathf.Max(
            0.0f,
            impactPauseSeconds * pauseTimeMultiplier);
        
        float fallDuration = Mathf.Max(0.01f, fallSeconds);
        float fadeDuration = Mathf.Max(0.01f, resultFadeSeconds);

        // 넘어지는 동안에는 나무를 선명하게 보여준다.
        effect.Group.alpha = 1.0f;

        // 1. 타격 반응:
        // 처음에 빠르게 꺾이고 목표 각도 근처에서 감속한다.
        if (elapsed < impactDuration)
        {
            float progress = Mathf.Clamp01(
                elapsed / impactDuration);

            float remaining = 1.0f - progress;

            float impactProgress =
                1.0f - remaining * remaining * remaining;

            effect.Root.localRotation = Quaternion.Slerp(
                effect.RestRotation,
                effect.ImpactRotation,
                impactProgress);

            return;
        }

        elapsed -= impactDuration;

        // 2. 짧은 버팀:
        // 음악이나 입력은 멈추지 않고 나무 자세만 유지한다.
        if (elapsed < pauseDuration)
        {
            effect.Root.localRotation = effect.ImpactRotation;
            return;
        }

        elapsed -= pauseDuration;

        // 3. 쓰러짐:
        // 꺾인 자세에서 시작해 점점 빠르게 넘어간다.
        if (elapsed < fallDuration)
        {
            float progress = Mathf.Clamp01(
                elapsed / fallDuration);

            float fallProgress = progress * progress;

            effect.Root.localRotation = Quaternion.Slerp(
                effect.ImpactRotation,
                effect.EndRotation,
                fallProgress);

            return;
        }

        elapsed -= fallDuration;

        // 4. 정리:
        // 완전히 넘어진 뒤에만 투명해진다.
        effect.Root.localRotation = effect.EndRotation;

        float fadeProgress = Mathf.Clamp01(
            elapsed / fadeDuration);

        effect.Group.alpha = 1.0f - fadeProgress;

        if (fadeProgress >= 1.0f)
        {
            Hide(effect);
        }
    }
}