using UnityEngine;
using UnityEngine.UI;

// 나무 종류에 따라 몸통 너비와 색상을 변경한다.
// 접근 이동, 입력 판정, 목재 지급은 처리하지 않는다.
public class ForestTreeAppearance : MonoBehaviour
{
    [Header("나무 이미지")]

    [SerializeField]
    private Image trunkImage;

    [SerializeField]
    private Image leavesImage;

    [Header("Tap 외형")]

    [SerializeField, Min(1.0f)]
    private float tapTrunkWidth = 28.0f;

    [SerializeField]
    private Color tapTrunkColor =
        new Color(0.45f, 0.25f, 0.10f, 1.0f);

    [SerializeField]
    private Color tapLeavesColor =
        new Color(0.20f, 0.75f, 0.25f, 1.0f);

    [Header("Hold 외형")]

    [SerializeField, Min(1.0f)]
    private float holdTrunkWidth = 56.0f;

    [SerializeField]
    private Color holdTrunkColor =
        new Color(0.70f, 0.35f, 0.12f, 1.0f);

    [SerializeField]
    private Color holdLeavesColor =
        new Color(1.0f, 0.70f, 0.15f, 1.0f);

    [Header("Sync 외형")]

    [SerializeField, Min(1.0f)]
    private float syncTrunkWidth = 40.0f;

    [SerializeField]
    private Color syncTrunkColor =
        new Color(0.25f, 0.40f, 0.55f, 1.0f);

    [SerializeField]
    private Color syncLeavesColor =
        new Color(0.15f, 0.80f, 1.0f, 1.0f);

    [Header("Hold 해제 안내")]

    [SerializeField, Range(0.0f, 1.0f)]
    private float holdCutHeightRatio = 0.30f;

    [SerializeField, Range(0.0f, 1.0f)]
    private float holdGuideStartHeightRatio = 0.85f;

    [SerializeField, Min(0.0f)]
    private float holdGuideExtraWidth = 16.0f;

    [SerializeField, Min(1.0f)]
    private float holdGuideThickness = 4.0f;

    [Header("Hold 중 반대쪽 Tap 안내")]

    [SerializeField, Min(2.0f)]
    private float crossTapMarkSize = 9.0f;

    // 반대쪽 Tap 나무에 붙이는 표식.
    [SerializeField, HideInInspector]
    private Image crossTapTreeMark;

    [SerializeField]
    private Color holdMovingGuideColor =
        new Color(0.15f, 0.85f, 1.0f, 1.0f);

    // 실행 중 생성한 선을 보관한다.
    // 나무를 복제할 때 자식 이미지 참조도 함께 연결되도록 직렬화한다.
    [SerializeField, HideInInspector]
    private Image holdCutLine;

    [SerializeField, HideInInspector]
    private Image holdMovingLine;

    // 움직이는 나무를 복제해도 원래 위치를 물려받도록 직렬화한다.
    [SerializeField, HideInInspector]
    private bool hasApproachBasePositions;

    [SerializeField, HideInInspector]
    private Vector3 trunkRestPosition;

    [SerializeField, HideInInspector]
    private Vector3 leavesRestPosition;

    [SerializeField, HideInInspector]
    private Vector3 trunkRestScale;

    [SerializeField, HideInInspector]
    private Vector3 leavesRestScale;

    [SerializeField, HideInInspector]
    private Quaternion trunkRestRotation;

    [SerializeField, HideInInspector]
    private Quaternion leavesRestRotation;

    // 표시기에서 필수 이미지 연결 여부를 확인한다.
    public bool IsConfigured =>
        trunkImage != null &&
        leavesImage != null;

    // 같은 종류를 표시하는 동안에는
    // 매 프레임 너비와 색상을 다시 설정하지 않는다.
    private bool hasAppliedAppearance;
    private ForestNoteKind displayedKind;

    private void OnEnable()
    {
        if (IsConfigured && !hasApproachBasePositions)
        {
            trunkRestPosition = trunkImage.rectTransform.localPosition;
            leavesRestPosition = leavesImage.rectTransform.localPosition;

            trunkRestScale = trunkImage.rectTransform.localScale;
            leavesRestScale = leavesImage.rectTransform.localScale;

            trunkRestRotation = trunkImage.rectTransform.localRotation;
            leavesRestRotation = leavesImage.rectTransform.localRotation;

            hasApproachBasePositions = true;
        }

        SetApproachMotion(0.0f, 0.0f, 0.0f, 0.0f);

        hasAppliedAppearance = false;

        HideHoldReleaseGuide();
        SetCrossTapMarker(false);
    }

    private void OnDisable()
    {
        SetApproachMotion(0.0f, 0.0f, 0.0f, 0.0f);

        HideHoldReleaseGuide();
        SetCrossTapMarker(false);
    }

    public void SetKind(ForestNoteKind kind)
    {
        if (kind != ForestNoteKind.Tap &&
            kind != ForestNoteKind.Hold &&
            kind != ForestNoteKind.Sync)
        {
            return;
        }

        if (!IsConfigured)
        {
            Debug.LogError(
                "Trunk Image와 Leaves Image를 연결해 주세요.",
                this);

            return;
        }

        if (hasAppliedAppearance && displayedKind == kind)
        {
            return;
        }

        float width;
        Color trunkColor;
        Color leavesColor;

        switch (kind)
        {
            case ForestNoteKind.Hold:
                width = holdTrunkWidth;
                trunkColor = holdTrunkColor;
                leavesColor = holdLeavesColor;
                break;

            case ForestNoteKind.Sync:
                width = syncTrunkWidth;
                trunkColor = syncTrunkColor;
                leavesColor = syncLeavesColor;
                break;

            default:
                width = tapTrunkWidth;
                trunkColor = tapTrunkColor;
                leavesColor = tapLeavesColor;
                break;
        }

        // 몸통 너비만 변경한다.
        // 접근 위치, 높이, 회전과 루트 크기는 유지한다.
        trunkImage.rectTransform.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            Mathf.Max(1.0f, width));

        trunkImage.color = trunkColor;
        leavesImage.color = leavesColor;

        displayedKind = kind;
        hasAppliedAppearance = true;
    }

    // 실제 Hold 생성 전에도 Inspector에서 외형을 확인한다.
    [ContextMenu("미리보기/Tap 외형")]
    private void PreviewTap()
    {
        PreviewAppearance(ForestNoteKind.Tap);
    }

    [ContextMenu("미리보기/Hold 외형")]
    private void PreviewHold()
    {
        PreviewAppearance(ForestNoteKind.Hold);
    }

    [ContextMenu("미리보기/Sync 외형")]
    private void PreviewSync()
    {
        PreviewAppearance(ForestNoteKind.Sync);
    }

    private void PreviewAppearance(ForestNoteKind kind)
    {
        // 실행 중에는 실제 노트 종류를 표시기가 결정한다.
        // 외형 미리보기는 Play 모드를 종료한 상태에서 사용한다.
        if (Application.isPlaying)
        {
            Debug.LogWarning(
                "외형 미리보기는 Play 모드를 종료한 뒤 사용해 주세요.",
                this);

            return;
        }

        // Inspector에서 색이나 너비를 바꾼 뒤
        // 같은 종류를 다시 선택해도 변경값을 반영한다.
        hasAppliedAppearance = false;

        SetKind(kind);
    }


    public void SetHoldProgress(float progress)
    {
        if (!IsConfigured ||
            !hasAppliedAppearance ||
            displayedKind != ForestNoteKind.Hold)
        {
            return;
        }

        // 유지가 끝나도 Hold의 기본 색상을 알아볼 수 있게 한다.
        // 정확한 해제 시각 안내는 다음 단계에서 별도로 개선한다.
        float brightness =
            Mathf.Clamp01(progress) * 0.25f;

        leavesImage.color = Color.Lerp(
            holdLeavesColor,
            Color.white,
            brightness);
    }

    public void SetHoldReleaseGuide(
        bool isHoldingThisNote,
        float progress,
        float lastBeatProgress,
        bool canRelease)
    {
        if (!isHoldingThisNote ||
            !IsConfigured ||
            !hasAppliedAppearance ||
            displayedKind != ForestNoteKind.Hold)
        {
            HideHoldReleaseGuide();
            return;
        }

        EnsureHoldReleaseGuide();

        RectTransform trunkRect = trunkImage.rectTransform;

        float height = trunkRect.rect.height;
        float width = trunkRect.rect.width + holdGuideExtraWidth;

        float cutRatio = Mathf.Clamp01(holdCutHeightRatio);

        // 시작 표시가 절단선 아래에서 출발하지 않게 한다.
        float startRatio = Mathf.Max(
            cutRatio,
            Mathf.Clamp01(holdGuideStartHeightRatio));

        float cutY = height * cutRatio;
        float startY = height * startRatio;

        float clampedProgress = Mathf.Clamp01(progress);
        float finalBeat = Mathf.Clamp01(lastBeatProgress);

        // 타이밍 표시는 가감속하지 않고 일정하게 목표를 향해 이동한다.
        float movingY = Mathf.Lerp(
            startY,
            cutY,
            clampedProgress);

        // 해제 가능 구간에서는 고정 목표선을 더 두껍고 넓게 표시한다.
        float thicknessMultiplier = canRelease
            ? 2.2f
            : Mathf.Lerp(1.0f, 1.6f, finalBeat);

        float cutThickness =
            holdGuideThickness * thicknessMultiplier;

        float cutWidth = width * (canRelease ? 1.15f : 1.0f);

        SetGuideRect(
            holdCutLine,
            cutY,
            cutWidth,
            cutThickness);

        SetGuideRect(
            holdMovingLine,
            movingY,
            width,
            holdGuideThickness);

        // 아직 이르면 옅게, 해제 가능 구간이면 선명하게 표시한다.
        holdCutLine.color = new Color(
            1.0f,
            1.0f,
            1.0f,
            canRelease ? 1.0f : 0.45f);

        // 목표에 도착하면 두 선이 흰색 한 줄로 겹친다.
        holdMovingLine.color = clampedProgress >= 1.0f
            ? Color.white
            : holdMovingGuideColor;

        holdCutLine.enabled = true;
        holdMovingLine.enabled = true;
    }

    private void EnsureHoldReleaseGuide()
    {
        if (holdCutLine == null)
        {
            holdCutLine = CreateHoldGuideImage("HoldCutLine");
        }

        if (holdMovingLine == null)
        {
            holdMovingLine = CreateHoldGuideImage("HoldMovingLine");
        }
    }

    private Image CreateHoldGuideImage(string objectName)
    {
        GameObject guideObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));

        RectTransform rect =
            guideObject.GetComponent<RectTransform>();

        rect.SetParent(trunkImage.rectTransform, false);

        // 몸통 아래쪽 중앙을 기준으로 높이를 계산한다.
        rect.anchorMin = new Vector2(0.5f, 0.0f);
        rect.anchorMax = new Vector2(0.5f, 0.0f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        Image guide = guideObject.GetComponent<Image>();

        // 표시선이 마우스 입력을 가로채지 않게 한다.
        guide.raycastTarget = false;
        guide.enabled = false;

        return guide;
    }

    private static void SetGuideRect(
        Image guide,
        float y,
        float width,
        float thickness)
    {
        RectTransform rect = guide.rectTransform;

        rect.anchoredPosition = new Vector2(0.0f, y);
        rect.sizeDelta = new Vector2(width, thickness);
    }

    private void HideHoldReleaseGuide()
    {
        if (holdCutLine != null)
        {
            holdCutLine.enabled = false;
        }

        if (holdMovingLine != null)
        {
            holdMovingLine.enabled = false;
        }
    }

    public void SetCrossTapMarker(bool show)
    {
        bool visible =
            show &&
            IsConfigured &&
            hasAppliedAppearance &&
            displayedKind == ForestNoteKind.Tap;

        if (!visible)
        {
            if (crossTapTreeMark != null)
            {
                crossTapTreeMark.enabled = false;
            }

            return;
        }

        if (crossTapTreeMark == null)
        {
            crossTapTreeMark =
                CreateHoldGuideImage("HoldCrossTapTreeMark");

            RectTransform rect = crossTapTreeMark.rectTransform;

            // 초록색 잎 중앙에 흰 마름모를 표시한다.
            rect.SetParent(leavesImage.rectTransform, false);

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            rect.anchoredPosition = Vector2.zero;
            rect.localRotation =
                Quaternion.Euler(0.0f, 0.0f, 45.0f);
        }

        float size = Mathf.Max(2.0f, crossTapMarkSize);

        crossTapTreeMark.rectTransform.sizeDelta =
            new Vector2(size, size);

        crossTapTreeMark.color = Color.white;
        crossTapTreeMark.enabled = true;
    }

    public void SetApproachMotion(
      float height,
      float squash,
      float leanDegrees,
      float leavesSwayDegrees)
    {
        if (!IsConfigured || !hasApproachBasePositions)
        {
            return;
        }

        squash = Mathf.Clamp(squash, 0.0f, 0.25f);

        Vector3 offset = Vector3.up * height;

        Vector3 shapeScale = new Vector3(
            1.0f + squash * 0.6f,
            1.0f - squash,
            1.0f);

        Quaternion leanRotation =
            Quaternion.Euler(0.0f, 0.0f, leanDegrees);

        RectTransform trunk = trunkImage.rectTransform;
        RectTransform leaves = leavesImage.rectTransform;

        // 몸통 아래 중앙을 기준으로 기울인다.
        trunk.localPosition = trunkRestPosition + offset;
        trunk.localRotation = leanRotation * trunkRestRotation;
        trunk.localScale = Vector3.Scale(
            trunkRestScale, shapeScale);

        // 잎의 위치도 몸통과 함께 회전시킨다.
        Vector3 leavesOffset = Vector3.Scale(
            leavesRestPosition - trunkRestPosition,
            shapeScale);

        leaves.localPosition =
            trunkRestPosition + offset +
            leanRotation * leavesOffset;

        // 잎에는 몸통의 기울기 외에 추가 흔들림을 준다.
        leaves.localRotation =
            leanRotation *
            Quaternion.Euler(0.0f, 0.0f, leavesSwayDegrees) *
            leavesRestRotation;

        leaves.localScale = Vector3.Scale(
            leavesRestScale, shapeScale);
    }
}