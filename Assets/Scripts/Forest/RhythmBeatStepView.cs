using System;
using UnityEngine;

// Sync의 좌우 나무도 같은 진행률을 사용한다.
// 기존 코드와의 연결을 유지한다.
// Sync에는 위치가 두 개 있으므로 방향을 지정하는 함수를 사용한다.

public class RhythmBeatStepView : MonoBehaviour
{
    [SerializeField]
    private MusicPlayback musicPlayback;

    [SerializeField]
    private RhythmEventTester rhythmEventTester;

    [SerializeField]
    private RhythmInputTester rhythmInputTester;

    [SerializeField]
    private RectTransform stepLane;

    [SerializeField]
    private RectTransform stepNode;

    [SerializeField]
    private RectTransform[] extraNodes = new RectTransform[3];

    [Header("Sync 짝 나무")]

    // 주 나무와 같은 순서로 연결한다.
    // 0: Step Node의 짝
    // 1: Extra Nodes[0]의 짝
    // 2: Extra Nodes[1]의 짝 ...
    [SerializeField]
    private RectTransform[] syncPartnerNodes = new RectTransform[4];

    [Header("오른쪽 나무 경로")]

    [SerializeField]
    private RectTransform startPoint;

    [SerializeField]
    private RectTransform hitPoint;

    [Header("왼쪽 나무 경로")]

    [SerializeField]
    private RectTransform leftStartPoint;

    [SerializeField]
    private RectTransform leftHitPoint;

    [Header("나무 접근 모션")]

    [SerializeField, Range(0.1f, 0.45f)]
    private float stepTravelRatio = 0.30f;

    [SerializeField, Min(0.0f)]
    private float hopHeight = 20.0f;

    [SerializeField, Range(0.1f, 0.45f)]
    private float landingDurationRatio = 0.30f;

    [SerializeField, Range(0.0f, 0.25f)]
    private float landingSquash = 0.10f;

    [SerializeField, Range(0.0f, 20.0f)]
    private float approachLeanAngle = 7.0f;

    [SerializeField, Range(0.0f, 25.0f)]
    private float leavesSwayAngle = 10.0f;

    public RectTransform StepLane => stepLane;

    private RectTransform[] nodes;
    private ForestTreeAppearance[] appearances;
    private ForestTreeAppearance[] partnerAppearances;
    private int[] displayedNoteIds;

    // 이번 화면 갱신에서 표시할 노트를 담는다.
    // 배열은 초기화 때 한 번만 만들고 계속 재사용한다.
    private RhythmNoteData[] desiredNotes;

    private int displayedPlaybackId = -1;
    private bool isInitialized;

    private Quaternion primaryTemplateRotation;
    private Quaternion partnerTemplateRotation;

    private const double BeatTolerance = 0.0000001;

    private void Awake()
    {
        if (musicPlayback == null ||
            rhythmEventTester == null ||
            rhythmInputTester == null ||
            stepLane == null ||
            stepNode == null ||
            startPoint == null ||
            hitPoint == null ||
            leftStartPoint == null ||
            leftHitPoint == null)
        {
            FailInitialization("표시기 참조와 좌우 기준점을 연결해 주세요.");
            return;
        }

        if (startPoint.parent != stepLane ||
            hitPoint.parent != stepLane ||
            leftStartPoint.parent != stepLane ||
            leftHitPoint.parent != stepLane)
        {
            FailInitialization(
                "좌우 시작점과 판정점을 Step Lane 바로 아래에 배치해 주세요.");
            return;
        }

        int extraCount = extraNodes != null ? extraNodes.Length : 0;
        int count = 1 + extraCount;

        if (syncPartnerNodes == null ||
            syncPartnerNodes.Length != count)
        {
            FailInitialization(
                $"Sync Partner Nodes를 주 나무 개수와 같은 {count}개로 설정해 주세요.");
            return;
        }

        nodes = new RectTransform[count];
        appearances = new ForestTreeAppearance[count];
        partnerAppearances = new ForestTreeAppearance[count];
        // 각 슬롯에 표시 중인 노트 Id를 저장할 배열.
        displayedNoteIds = new int[count];
        desiredNotes = new RhythmNoteData[count];

        nodes[0] = stepNode;

        for (int i = 0; i < extraCount; i++)
        {
            nodes[i + 1] = extraNodes[i];
        }

        // 주 나무와 짝 나무 전체에서 중복 연결을 검사한다.
        var usedRoots =
            new System.Collections.Generic.HashSet<RectTransform>();

        for (int i = 0; i < count; i++)
        {
            RectTransform primary = nodes[i];
            RectTransform partner = syncPartnerNodes[i];

            if (primary == null || partner == null ||
                primary.parent != stepLane ||
                partner.parent != stepLane)
            {
                FailInitialization(
                    $"{i + 1}번 주 나무·짝 나무의 연결과 부모를 확인해 주세요.");
                return;
            }

            if (!usedRoots.Add(primary) || !usedRoots.Add(partner))
            {
                FailInitialization(
                    "주 나무와 짝 나무에 같은 오브젝트를 중복 연결하지 마세요.");
                return;
            }

            appearances[i] =
                primary.GetComponent<ForestTreeAppearance>();

            partnerAppearances[i] =
                partner.GetComponent<ForestTreeAppearance>();

            if (appearances[i] == null ||
                partnerAppearances[i] == null ||
                !appearances[i].IsConfigured ||
                !partnerAppearances[i].IsConfigured)
            {
                FailInitialization(
                    $"{i + 1}번 나무의 ForestTreeAppearance와 이미지를 연결해 주세요.");
                return;
            }
        }

        primaryTemplateRotation = nodes[0].localRotation;
        partnerTemplateRotation = syncPartnerNodes[0].localRotation;

        isInitialized = true;
        HideAll();
    }

    private void LateUpdate()
    {
        if (!isInitialized) { return; }

        if (!musicPlayback.IsGameplayPlayback ||
            !rhythmEventTester.isActiveAndEnabled ||
            !rhythmInputTester.isActiveAndEnabled)
        {
            HideAll();
            return;
        }

        int approachBeats = rhythmEventTester.ApproachBeats;
        double approachSeconds = rhythmEventTester.ApproachSeconds;

        if (approachBeats < 1 ||
            !(approachSeconds > 0.0) ||
            !musicPlayback.TryGetSongTime(out double songTime))
        {
            HideAll();
            return;
        }

        // 새 재생에서는 이전 곡의 슬롯 배정을 제거한다.
        if (displayedPlaybackId != musicPlayback.PlaybackId)
        {
            HideAll();
            displayedPlaybackId = musicPlayback.PlaybackId;
        }

        // 노트 목록을 채우기 전에 충분한 슬롯을 확보한다.
        double secondsPerBeat = approachSeconds / approachBeats;

        double smallestGapSeconds = secondsPerBeat * 0.5;

        double lateWindowSeconds =
            rhythmInputTester.NormalWindowMs / 1000.0;

        int requiredSlots = (int)Math.Ceiling(
            (approachSeconds + lateWindowSeconds) /
            smallestGapSeconds) + 3;

        EnsureSlotCapacity(requiredSlots);

        // 1. 이번 프레임에 표시할 노트 목록을 준비한다.
        int desiredCount = 0;

        RhythmNoteData heldNote = rhythmInputTester.ActiveHoldNote;

        // 유지 중인 Hold를 우선 포함한다.
        if (heldNote != null)
        {
            desiredNotes[desiredCount++] = heldNote;
        }

        // 현재 노트가 이미 판정됐다면 다음 노트부터 조회한다.
        int offset = rhythmInputTester.HasJudged ? 1 : 0;

        while (desiredCount < desiredNotes.Length &&
               rhythmEventTester.TryGetUpcomingNote(
                   offset++, out RhythmNoteData note))
        {
            // 입력기 수정 전에는 Hold가 CurrentNote에도 남아 있다.
            // 같은 Hold를 두 번 포함하지 않는다.
            if (heldNote != null && note.Id == heldNote.Id)
            {
                continue;
            }

            // 시간순 목록이므로 이후 노트도 아직 접근 시작 전이다.
            if (songTime < note.TargetTimeSeconds - approachSeconds)
            {
                break;
            }

            if (!note.IsResource) { continue; }

            desiredNotes[desiredCount++] = note;
        }

        // 사용하지 않는 배열 칸의 이전 참조를 정리한다.
        Array.Clear(
            desiredNotes,
            desiredCount,
            desiredNotes.Length - desiredCount);

        // 2. 더 이상 표시하지 않을 노트의 슬롯만 비운다.
        for (int slot = 0; slot < nodes.Length; slot++)
        {
            bool keepSlot = false;

            for (int i = 0; i < desiredCount; i++)
            {
                if (displayedNoteIds[slot] == desiredNotes[i].Id)
                {
                    keepSlot = true;
                    break;
                }
            }

            if (!keepSlot)
            {
                HideSlot(slot);
            }
        }

        // 실행 중 추가된 나무보다 판정 안내가 앞에 그려지게 한다.
        // 두 판정점의 부모는 계속 ForestStage로 유지된다.
        leftHitPoint.SetAsLastSibling();
        hitPoint.SetAsLastSibling();

        for (int i = 0; i < desiredCount; i++)
        {
            RhythmNoteData note = desiredNotes[i];

            int slot = FindNoteSlot(note.Id);

            if (slot < 0)
            {
                slot = FindNoteSlot(-1);

                if (slot < 0) { break; }

                displayedNoteIds[slot] = note.Id;
            }

            float progress = GetTreeApproachProgress(
                note,
                songTime,
                secondsPerBeat,
                approachBeats);

            bool isSync = note.Kind == ForestNoteKind.Sync;

            RhythmInputSide primarySide = isSync
                ? RhythmInputSide.Left
                : note.Side;

            if (!TryGetPoints(
                    primarySide,
                    out RectTransform spawn,
                    out RectTransform hit))
            {
                HideSlot(slot);
                continue;
            }

            ApplyTree(
                nodes[slot],
                appearances[slot],
                note,
                primarySide,
                Vector3.Lerp(
                    spawn.localPosition,
                    hit.localPosition,
                    progress),
                songTime);

            if (isSync)
            {
                ApplyTree(
                    syncPartnerNodes[slot],
                    partnerAppearances[slot],
                    note,
                    RhythmInputSide.Right,
                    Vector3.Lerp(
                        startPoint.localPosition,
                        hitPoint.localPosition,
                        progress),
                    songTime);
            }
            else
            {
                SetVisible(syncPartnerNodes[slot], false);
            }
        }
    }

    private int FindNoteSlot(int noteId)
    {
        for (int i = 0; i < displayedNoteIds.Length; i++)
        {
            if (displayedNoteIds[i] == noteId)
            {
                return i;
            }
        }

        // 해당 노트를 표시하는 슬롯이 없다.
        return -1;
    }

    private void ApplyTree(
       RectTransform root,
       ForestTreeAppearance appearance,
       RhythmNoteData note,
       RhythmInputSide visualSide,
       Vector3 position,
       double songTime)
    {
        root.localPosition = position;

        // 활성화 초기화를 먼저 마치고 외형을 적용한다.
        SetVisible(root, true);
        appearance.SetKind(note.Kind);

        RhythmNoteData heldNote =
            rhythmInputTester.ActiveHoldNote;

        bool isHoldingThisNote =
            note.IsHold &&
            heldNote != null &&
            heldNote.Id == note.Id;

        float hop = 0.0f;
        float squash = 0.0f;

        float lean = 0.0f;
        float leavesSway = 0.0f;

        float direction =
            visualSide == RhythmInputSide.Left ? 1.0f : -1.0f;

        int motionBeats = rhythmEventTester.ApproachBeats;

        if (!isHoldingThisNote &&
            songTime < note.TargetTimeSeconds &&
            motionBeats > 0)
        {
            double motionBeatSeconds =
                rhythmEventTester.ApproachSeconds / motionBeats;

            if (motionBeatSeconds > 0.0)
            {
                double approachStart =
                    note.TargetTimeSeconds -
                    motionBeatSeconds * motionBeats;

                double elapsedMotionBeats =
                    (songTime - approachStart) / motionBeatSeconds;

                int stepIndex = Mathf.Clamp(
                    (int)Math.Floor(elapsedMotionBeats + BeatTolerance),
                    0,
                    motionBeats - 1);

                float phase = Mathf.Clamp01(
                    (float)(elapsedMotionBeats - stepIndex));

                float landingRatio = Mathf.Clamp(
                    landingDurationRatio, 0.1f, 0.45f);

                // 처음 등장할 때는 제외하고,
                // 한 칸 이상 이동한 뒤 박자 직후에 착지 반응을 준다.
                if (stepIndex > 0 && phase < landingRatio)
                {
                    float landingProgress = phase / landingRatio;

                    float pulse = Mathf.Sin(
                        Mathf.PI * landingProgress);

                    squash = landingSquash * pulse * pulse;

                    // 착지 직후 흔들리고, 시간이 지날수록 빠르게 줄어든다.
                    float decay =
                        (1.0f - landingProgress) *
                        (1.0f - landingProgress);

                    lean =
                        direction * approachLeanAngle * 0.7f *
                        Mathf.Sin(landingProgress * Mathf.PI * 3.0f) *
                        decay;

                    leavesSway =
                        -direction * leavesSwayAngle *
                        Mathf.Sin(landingProgress * Mathf.PI * 2.0f) *
                        decay;
                }

                float travelRatio = Mathf.Clamp(
                    stepTravelRatio, 0.1f, 0.45f);

                // 좌우 이동과 같은 구간에서 뛰어오른다.
                float travelProgress = Mathf.Clamp01(
                    (phase - (1.0f - travelRatio)) / travelRatio);

                // 출발과 도착에서는 0, 이동 중간에서 가장 높다.
                float arc = Mathf.Sin(Mathf.PI * travelProgress);
                hop = hopHeight * arc * arc;

                // 이동할 때는 진행 방향으로 기울고,
                // 잎은 반대쪽으로 살짝 처지는 느낌을 준다.
                lean += -direction * approachLeanAngle * arc * arc;

                leavesSway += direction * leavesSwayAngle * 0.45f * arc * arc;

                // Hold는 조금 묵직하게 움직인다.
                if (note.IsHold)
                {
                    hop *= 0.65f;
                    squash *= 0.65f;
                    lean *= 0.65f;
                    leavesSway *= 0.65f;
                }

                // 마지막 접근은 타격 위치가 잘 보이도록 낮게 뛴다.
                if (stepIndex == motionBeats - 1)
                {
                    hop *= 0.35f;
                    squash *= 0.35f;
                    lean *= 0.35f;
                    leavesSway *= 0.35f;
                }
            }
        }

        // Hold 시작 또는 목표 시각 도착 후에는 모든 모션값을 0으로 복구한다.
        appearance.SetApproachMotion(
            hop,
            squash,
            lean,
            leavesSway);

        bool isCrossTap =
            heldNote != null &&
            IsTapInsideHold(note, heldNote);

        appearance.SetCrossTapMarker(isCrossTap);

        float progress = 0.0f;
        float lastBeatProgress = 0.0f;

        if (isHoldingThisNote)
        {
            progress = Mathf.Clamp01(
                (float)(
                    (songTime - note.TargetTimeSeconds) /
                    note.DurationSeconds));

            int approachBeats = rhythmEventTester.ApproachBeats;

            if (approachBeats > 0)
            {
                double secondsPerBeat =
                    rhythmEventTester.ApproachSeconds / approachBeats;

                if (secondsPerBeat > 0.0 &&
                    !double.IsInfinity(secondsPerBeat))
                {
                    lastBeatProgress = Mathf.Clamp01(
                        (float)(
                            1.0 -
                            (note.EndTimeSeconds - songTime) /
                            secondsPerBeat));
                }
            }
        }

        if (note.IsHold)
        {
            appearance.SetHoldProgress(progress);
        }

        // Tap·Sync 또는 유지하지 않는 나무에서는 안내선을 숨긴다.
        bool canReleaseThisNote =
            isHoldingThisNote &&
            rhythmInputTester.CanReleaseActiveHold(songTime);

        appearance.SetHoldReleaseGuide(
            isHoldingThisNote,
            progress,
            lastBeatProgress,
            canReleaseThisNote);
    }

    private bool TryGetPoints(
        RhythmInputSide side,
        out RectTransform spawn,
        out RectTransform hit)
    {
        spawn = null;
        hit = null;

        if (side == RhythmInputSide.Left)
        {
            spawn = leftStartPoint;
            hit = leftHitPoint;
            return true;
        }

        if (side == RhythmInputSide.Right)
        {
            spawn = startPoint;
            hit = hitPoint;
            return true;
        }

        return false;
    }

    public bool TryGetResultPosition(
        RhythmNoteData note,
        bool isSuccess,
        RhythmInputSide side,
        out Vector3 localPosition)
    {
        localPosition = Vector3.zero;

        if (!isInitialized ||
            !isActiveAndEnabled ||
            !musicPlayback.IsGameplayPlayback ||
            note == null)
        {
            return false;
        }

        bool isSync = note.Kind == ForestNoteKind.Sync;

        // Sync는 좌우 결과가 각각 있지만,
        // 일반 노트는 자신의 방향에만 결과를 표시한다.
        if (!isSync && side != note.Side)
        {
            return false;
        }

        if (!TryGetPoints(
                side,
                out _,
                out RectTransform hit))
        {
            return false;
        }

        // 성공한 나무는 해당 방향의 판정점에서 쓰러진다.
        localPosition = hit.localPosition;

        if (isSuccess)
        {
            return true;
        }

        // Miss는 실제로 표시 중이던 나무의 마지막 위치를 사용한다.
        if (displayedPlaybackId == musicPlayback.PlaybackId)
        {
            for (int i = 0; i < nodes.Length; i++)
            {
                if (displayedNoteIds[i] != note.Id)
                {
                    continue;
                }

                RectTransform root =
                    isSync && side == RhythmInputSide.Right
                        ? syncPartnerNodes[i]
                        : nodes[i];

                if (root.gameObject.activeInHierarchy)
                {
                    localPosition = root.localPosition;
                }

                return true;
            }
        }

        // 같은 프레임 안에 처리되어 표시 슬롯이 없었던 경우에는
        // 판정점을 기준으로 결과를 표시한다.
        return true;
    }

    private void HideSlot(int index)
    {
        displayedNoteIds[index] = -1;

        SetVisible(nodes[index], false);
        SetVisible(syncPartnerNodes[index], false);
    }

    private void HideAll()
    {

        displayedPlaybackId = -1;
        if (nodes == null) { return; }

        for (int i = 0; i < nodes.Length; i++)
        {
            HideSlot(i);
        }
    }

    private void SetVisible(RectTransform root, bool visible)
    {
        if (root != null && root.gameObject.activeSelf != visible)
        {
            root.gameObject.SetActive(visible);
        }
    }

    private void OnDisable()
    {
        HideAll();
    }

    private void FailInitialization(string message)
    {
        Debug.LogError($"[나무 표시 초기화 실패] {message}", this);
        enabled = false;
    }

    private void EnsureSlotCapacity(int requiredCount)
    {
        int oldCount = nodes.Length;

        if (requiredCount <= oldCount)
        {
            return;
        }

        // 여섯 배열의 길이를 항상 함께 늘린다.
        System.Array.Resize(ref nodes, requiredCount);
        System.Array.Resize(ref appearances, requiredCount);
        System.Array.Resize(ref syncPartnerNodes, requiredCount);
        System.Array.Resize(ref partnerAppearances, requiredCount);
        System.Array.Resize(ref displayedNoteIds, requiredCount);
        System.Array.Resize(ref desiredNotes, requiredCount);

        for (int i = oldCount; i < requiredCount; i++)
        {
            RectTransform primary = Instantiate(
                nodes[0],
                stepLane,
                false);

            RectTransform partner = Instantiate(
                syncPartnerNodes[0],
                stepLane,
                false);

            primary.name = $"TreeNode_Runtime_{i}";
            partner.name = $"SyncPartner_Runtime_{i}";

            primary.gameObject.SetActive(false);
            partner.gameObject.SetActive(false);

            primary.localRotation = primaryTemplateRotation;
            partner.localRotation = partnerTemplateRotation;

            nodes[i] = primary;
            syncPartnerNodes[i] = partner;

            appearances[i] =
                primary.GetComponent<ForestTreeAppearance>();

            partnerAppearances[i] =
                partner.GetComponent<ForestTreeAppearance>();

            displayedNoteIds[i] = -1;
            desiredNotes[i] = null;
        }
    }

    private float GetTreeApproachProgress(
        RhythmNoteData note,
        double songTime,
        double secondsPerBeat,
        int approachBeats)
    {
        RhythmNoteData activeHold =
            rhythmInputTester.ActiveHoldNote;

        // 시작한 Hold는 기존처럼 판정점에 고정한다.
        if (activeHold != null && activeHold.Id == note.Id)
        {
            return 1.0f;
        }

        // 목표 시각부터는 판정점에 정확히 고정한다.
        if (songTime >= note.TargetTimeSeconds)
        {
            return 1.0f;
        }

        double approachStartTime =
            note.TargetTimeSeconds -
            secondsPerBeat * approachBeats;

        double elapsedBeats =
            (songTime - approachStartTime) / secondsPerBeat;

        int completedSteps = Mathf.Clamp(
            (int)Math.Floor(elapsedBeats + BeatTolerance),
            0,
            approachBeats - 1);

        // 현재 한 박자 안에서 얼마나 진행됐는지 계산한다.
        float beatPhase = Mathf.Clamp01(
            (float)(elapsedBeats - completedSteps));

        float travelRatio = Mathf.Clamp(
            stepTravelRatio, 0.1f, 0.45f);

        // 박자 앞부분에는 제자리에 있고,
        // 마지막 travelRatio 구간에서 다음 칸으로 이동한다.
        float travelProgress = Mathf.Clamp01(
            (beatPhase - (1.0f - travelRatio)) / travelRatio);

        // 이동 시작과 끝을 부드럽게 연결한다.
        float easedProgress = Mathf.SmoothStep(
            0.0f, 1.0f, travelProgress);

        return (completedSteps + easedProgress) / approachBeats;
    }

    private static bool IsTapInsideHold(
        RhythmNoteData note,
        RhythmNoteData hold)
    {
        return note.Kind == ForestNoteKind.Tap &&
               note.Side != hold.Side &&
               note.TargetTimeSeconds >= hold.TargetTimeSeconds &&
               note.TargetTimeSeconds < hold.EndTimeSeconds;
    }
}