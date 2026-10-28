using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// �� ���� ���¸� �о� ȭ�� ����, ��ư, ���� ������ �����Ѵ�.
// ���� ��Ģ�� ���� ó���� NightDefenseTester�� ����Ѵ�.
[DefaultExecutionOrder(100)]
public class NightScreenUI : MonoBehaviour
{
    [SerializeField]
    private NightDefenseTester nightDefense;

    [Header("���� ȭ�� ����")]

    [SerializeField]
    private CanvasGroup playGroup;

    [SerializeField]
    private CanvasGroup victoryGroup;

    [Header("���� ǥ��")]

    [SerializeField]
    private TMP_Text statusText;

    [Header("���� �� ��ư")]

    [SerializeField]
    private Button nextDayButton;

    [SerializeField]
    private TMP_Text nextDayButtonText;

    [Header("ȭ�� �� ķ�� �ǰ� ���")]

    [SerializeField]
    private Camera worldCamera;

    [SerializeField]
    private RectTransform dangerFrame;

    [SerializeField]
    private Image dangerImage;

    [SerializeField, Min(0.1f)]
    private float dangerDisplaySeconds = 1.5f;

    // ��� �̹����� ũ��. ȭ�� �����ڸ��� ������ ���� ǥ�õȴ�.
    [SerializeField, Range(0.1f, 0.8f)]
    private float dangerSizeRatio = 0.45f;

    // ������ �� �� �� �����ϰ�, ������Ʈ�� ���ŵ� �� �����Ѵ�.
    private Texture2D dangerTexture;
    private Sprite dangerSprite;

    private static NightScreenUI contextOwner;
    public static bool HasContextPanel => contextOwner != null && contextOwner.isActiveAndEnabled && contextOwner.contextHud != null;
    private RectTransform contextHud;
    private TMP_Text contextLabel;
    private ForestEntranceInteraction forestEntrance;
    private CampNpcCommandController npcCommands;
    private CampWallPlacementController placement;
    private RectTransform combatHud;
    private readonly Image[] gaugeFills = new Image[3];
    private readonly TMP_Text[] gaugeLabels = new TMP_Text[3];
    private TMP_Text treatmentLabel;
    private TMP_Text resultTitle;
    private const float TopHudInset = 98f;
    private const float DayBannerSeconds = 2.6f;
    private RectTransform phaseHud;
    private TMP_Text phaseCaption;
    private readonly Image[] phaseFills = new Image[3];
    private readonly TMP_Text[] phaseLabels = new TMP_Text[3];
    private CanvasGroup dayBanner;
    private TMP_Text dayBannerText;
    private float dayBannerElapsed = DayBannerSeconds;
    private Image riftDirectionImage;
    private TMP_Text riftDirectionLabel;

    private void CreatePhasePresentation()
    {
        Canvas canvas = statusText.canvas;
        if (canvas == null) return;
        Transform root = canvas.rootCanvas.transform;
        phaseHud = MakeHudRect("RegionPhaseHUD", root, new Vector2(720f, 68f), new Vector2(18f, -12f));
        Image panel = phaseHud.gameObject.AddComponent<Image>();
        panel.color = new Color(0.025f, 0.04f, 0.065f, 0.94f);
        panel.raycastTarget = false;
        phaseCaption = MakeHudLabel(phaseHud, new Vector2(680f, 24f), new Vector2(12f, -4f));
        phaseCaption.alignment = TextAlignmentOptions.Center;
        phaseCaption.textWrappingMode = TextWrappingModes.NoWrap;
        phaseCaption.enableAutoSizing = true;
        phaseCaption.fontSizeMin = 13f;
        phaseCaption.fontSizeMax = 19f;
        string[] names = { "낮", "중립", "밤" };
        Color[] colors = { new Color(0.3f, 0.85f, 0.55f), new Color(1f, 0.82f, 0.15f), new Color(0.55f, 0.5f, 1f) };
        for (int i = 0; i < 3; i++)
        {
            RectTransform track = MakeHudRect("Phase_" + i, phaseHud, Vector2.zero, Vector2.zero);
            track.anchorMin = new Vector2(i / 3f, 0f);
            track.anchorMax = new Vector2((i + 1) / 3f, 0f);
            track.pivot = new Vector2(0.5f, 0f);
            track.offsetMin = new Vector2(6f, 8f);
            track.offsetMax = new Vector2(-6f, 32f);
            Image background = track.gameObject.AddComponent<Image>();
            background.color = new Color(0.16f, 0.2f, 0.26f);
            background.raycastTarget = false;
            RectTransform fill = MakeHudRect("Progress", track, Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            phaseFills[i] = fill.gameObject.AddComponent<Image>();
            phaseFills[i].color = colors[i];
            phaseFills[i].raycastTarget = false;
            phaseLabels[i] = MakeHudLabel(track, Vector2.zero, Vector2.zero);
            phaseLabels[i].text = names[i];
            phaseLabels[i].alignment = TextAlignmentOptions.Center;
            phaseLabels[i].fontSize = 16f;
            phaseLabels[i].rectTransform.anchorMin = Vector2.zero;
            phaseLabels[i].rectTransform.anchorMax = Vector2.one;
            phaseLabels[i].rectTransform.offsetMin = phaseLabels[i].rectTransform.offsetMax = Vector2.zero;
        }

        RectTransform banner = MakeHudRect("NewDayAnnouncement", root, new Vector2(560f, 120f), Vector2.zero);
        banner.anchorMin = banner.anchorMax = banner.pivot = new Vector2(0.5f, 0.5f);
        dayBanner = banner.gameObject.AddComponent<CanvasGroup>();
        dayBanner.alpha = 0f;
        dayBanner.interactable = dayBanner.blocksRaycasts = false;
        dayBannerText = MakeHudLabel(banner, Vector2.zero, Vector2.zero);
        dayBannerText.alignment = TextAlignmentOptions.Center;
        dayBannerText.enableAutoSizing = true;
        dayBannerText.fontSizeMin = 24f;
        dayBannerText.fontSizeMax = 56f;
        dayBannerText.rectTransform.anchorMin = Vector2.zero;
        dayBannerText.rectTransform.anchorMax = Vector2.one;
        dayBannerText.rectTransform.offsetMin = dayBannerText.rectTransform.offsetMax = Vector2.zero;
        banner.gameObject.SetActive(false);

        RectTransform marker = MakeHudRect("RiftDirectionIndicator", dangerFrame, Vector2.zero, Vector2.zero);
        marker.anchorMin = marker.anchorMax = marker.pivot = new Vector2(0.5f, 0.5f);
        riftDirectionImage = marker.gameObject.AddComponent<Image>();
        riftDirectionImage.sprite = dangerSprite;
        riftDirectionImage.color = new Color(1f, 0.83f, 0.05f, 0.8f);
        riftDirectionImage.raycastTarget = false;
        riftDirectionImage.enabled = false;
        riftDirectionLabel = MakeHudLabel(dangerFrame, new Vector2(90f, 26f), Vector2.zero);
        riftDirectionLabel.rectTransform.anchorMin = riftDirectionLabel.rectTransform.anchorMax =
            riftDirectionLabel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        riftDirectionLabel.text = "균열";
        riftDirectionLabel.color = new Color(1f, 0.9f, 0.15f);
        riftDirectionLabel.alignment = TextAlignmentOptions.Center;
        riftDirectionLabel.enabled = false;
    }

    private static float EvaluateDayBannerAlpha(float elapsed)
    {
        if (elapsed < 0f || elapsed >= DayBannerSeconds) return 0f;
        if (elapsed < 0.6f) return Mathf.SmoothStep(0f, 1f, elapsed / 0.6f);
        if (elapsed < 1.8f) return 1f;
        return 1f - Mathf.SmoothStep(0f, 1f, (elapsed - 1.8f) / 0.8f);
    }

    private static bool TryGetEdgePosition(Rect frame, Vector3 viewport, float inset, out Vector2 position)
    {
        position = Vector2.zero;
        if (viewport.z <= 0f || frame.width <= 0f || frame.height <= 0f) return false;
        Vector2 direction = new Vector2((viewport.x - 0.5f) * frame.width, (viewport.y - 0.5f) * frame.height);
        if (direction.sqrMagnitude < 0.0001f) return false;
        float x = Mathf.Abs(direction.x) > 0.001f
            ? Mathf.Max(0f, frame.width * 0.5f - inset) / Mathf.Abs(direction.x) : float.PositiveInfinity;
        float y = Mathf.Abs(direction.y) > 0.001f
            ? Mathf.Max(0f, frame.height * 0.5f - inset) / Mathf.Abs(direction.y) : float.PositiveInfinity;
        position = direction * Mathf.Min(x, y);
        return true;
    }

    private void RefreshPhasePresentation()
    {
        if (phaseHud == null) return;
        RectTransform canvas = statusText.canvas.rootCanvas.transform as RectTransform;
        float width = canvas.rect.width;
        phaseHud.sizeDelta = new Vector2(Mathf.Max(100f, width - 36f), 68f);
        phaseCaption.rectTransform.sizeDelta = new Vector2(Mathf.Max(80f, width - 60f), 24f);
        phaseHud.gameObject.SetActive(nightDefense.IsInitialized && !nightDefense.IsChangingScene);
        string caption = $"{nightDefense.NightDay}일차 · {nightDefense.PhaseName} · {nightDefense.PhaseSecondsRemaining:F0}초";
        if (GameExitUI.IsWorldPaused) caption += " · 일시정지";
        phaseCaption.text = caption;
        int current = nightDefense.TimelinePhaseIndex;
        for (int i = 0; i < 3; i++)
        {
            float amount = i < current ? 1f : i == current ? nightDefense.PhaseElapsed01 : 0f;
            phaseFills[i].rectTransform.anchorMax = new Vector2(amount, 1f);
            phaseFills[i].rectTransform.offsetMin = phaseFills[i].rectTransform.offsetMax = Vector2.zero;
            phaseLabels[i].color = i == current ? Color.white : new Color(0.75f, 0.8f, 0.85f);
        }
        if (combatHud != null) combatHud.anchoredPosition = new Vector2(-18f, -TopHudInset);

        if (nightDefense.IsDay && RunProgress.TryConsumeDayAnnouncement(nightDefense.NightDay))
        { dayBannerElapsed = 0f; dayBannerText.text = $"{nightDefense.NightDay}일차"; }
        if (!GameExitUI.IsWorldPaused) dayBannerElapsed += Time.deltaTime;
        dayBanner.alpha = EvaluateDayBannerAlpha(dayBannerElapsed);
        dayBanner.gameObject.SetActive(nightDefense.IsDay && dayBannerElapsed < DayBannerSeconds && !GameExitUI.IsMenuOpen);
        dayBanner.GetComponent<RectTransform>().sizeDelta = new Vector2(Mathf.Min(560f, Mathf.Max(100f, width - 36f)), 120f);
    }

    private void RefreshRiftDirection()
    {
        if (riftDirectionImage == null) return;
        riftDirectionImage.enabled = riftDirectionLabel.enabled = false;
        if (GameExitUI.IsMenuOpen || !nightDefense.TryGetRiftIndicatorPosition(out Vector3 world)) return;
        Vector3 viewport = worldCamera.WorldToViewportPoint(world);
        bool onScreen = viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
        if (onScreen && nightDefense.IsRiftRevealed) return;
        Rect frame = dangerFrame.rect;
        if (!TryGetEdgePosition(frame, viewport, 24f, out Vector2 edge)) return;
        riftDirectionImage.rectTransform.sizeDelta = new Vector2(frame.width * 0.24f, frame.height * 0.24f);
        riftDirectionImage.rectTransform.anchoredPosition = edge;
        riftDirectionImage.enabled = true;
        if (TryGetEdgePosition(frame, viewport, 70f, out Vector2 labelPosition))
        {
            riftDirectionLabel.rectTransform.anchoredPosition = labelPosition;
            riftDirectionLabel.enabled = true;
        }
    }


    private bool isInitialized;

    private void Awake()
    {
        if (nightDefense == null ||
            playGroup == null ||
            victoryGroup == null ||
            statusText == null ||
            nextDayButton == null ||
            nextDayButtonText == null ||
            worldCamera == null ||
            dangerFrame == null ||
            dangerImage == null)
        {
            Debug.LogError(
               "NightScreenUI�� ���� UI�� World Camera, " +
               "Danger Frame, Danger Image�� ������ �ּ���.",
               this);

            enabled = false;
            return;
        }

        dangerImage.raycastTarget = false;
        dangerImage.enabled = false;

        CreateDangerGradient();
        CreateCombatHud();
        CreateContextHud();
        CreatePhasePresentation();
        foreach (TMP_Text label in victoryGroup.GetComponentsInChildren<TMP_Text>(true))
            if (label.name == "NightVictoryText")
            {
                resultTitle = label;
                resultTitle.enableAutoSizing = true;
                resultTitle.fontSizeMin = 18f; resultTitle.fontSizeMax = 30f;
                resultTitle.alignment = TextAlignmentOptions.Center;
                resultTitle.textWrappingMode = TextWrappingModes.Normal;
                break;
            }

        isInitialized = true;
        RefreshUI();
    }

    private void OnEnable()
    {
        if (!isInitialized)
        {
            return;
        }

        nextDayButton.onClick.AddListener(ClearButtonSelection);
    }

    private void OnDisable()
    {
        if (nextDayButton != null)
        {
            nextDayButton.onClick.RemoveListener(ClearButtonSelection);
        }

        if (combatHud != null) combatHud.gameObject.SetActive(false);
        if (contextHud != null) contextHud.gameObject.SetActive(false);
        if (phaseHud != null) phaseHud.gameObject.SetActive(false);
        if (dayBanner != null) dayBanner.gameObject.SetActive(false);
        if (riftDirectionImage != null) riftDirectionImage.enabled = false;
        if (riftDirectionLabel != null) riftDirectionLabel.enabled = false;
        if (dangerImage != null)
        {
            dangerImage.enabled = false;
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        EventSystem events = EventSystem.current;
        if (!isInitialized ||
            !Application.isFocused ||
            Time.deltaTime <= 0.0f ||
            keyboard == null ||
            !nightDefense.CanAdvancePhase ||
            CampWindowController.WorldInputBlocked ||
            CampWallPlacementController.IsPlacing ||
            (events != null && events.currentSelectedGameObject != null))
        {
            return;
        }

        // ���� �����ӿ� ��û�� ��ȣ�ۿ�� �� ������ �ߺ� ó������ �ʴ´�.
        if (keyboard.nKey.wasPressedThisFrame &&
            !keyboard.rKey.wasPressedThisFrame)
        {
            nightDefense.AdvancePhaseForTest();
        }
    }

    private void LateUpdate()
    {
        if (!isInitialized)
        {
            return;
        }

        RefreshUI();
        RefreshBaseDanger();
        RefreshContextHud();
        RefreshPhasePresentation();
        RefreshRiftDirection();
    }

    private void RefreshBaseDanger()
    {
        float timeSinceHit =
            Time.time - nightDefense.LastBaseHitTime;

        if (!nightDefense.IsRunning ||
            timeSinceHit > dangerDisplaySeconds)
        {
            dangerImage.enabled = false;
            return;
        }

        Vector3 viewportPosition =
            worldCamera.WorldToViewportPoint(
                nightDefense.BaseWorldPosition);

        bool isOnScreen =
            viewportPosition.z > 0.0f &&
            viewportPosition.x >= 0.0f &&
            viewportPosition.x <= 1.0f &&
            viewportPosition.y >= 0.0f &&
            viewportPosition.y <= 1.0f;

        if (isOnScreen || viewportPosition.z <= 0.0f)
        {
            dangerImage.enabled = false;
            return;
        }

        Rect frameRect = dangerFrame.rect;
        RectTransform markerRect = dangerImage.rectTransform;

        if (frameRect.width <= 0.0f ||
            frameRect.height <= 0.0f)
        {
            dangerImage.enabled = false;
            return;
        }

        // �ػ󵵰� �޶����� ȭ�鿡�� �����ϴ� ������ �����Ѵ�.
        markerRect.sizeDelta = new Vector2(
            frameRect.width * dangerSizeRatio,
            frameRect.height * dangerSizeRatio);

        Vector2 direction = new Vector2(
            (viewportPosition.x - 0.5f) * frameRect.width,
            (viewportPosition.y - 0.5f) * frameRect.height);

        // �̹��� �߽��� ȭ�� �����ڸ��� ������ ����Ѵ�.
        float halfWidth = frameRect.width * 0.5f;
        float halfHeight = frameRect.height * 0.5f;

        float distanceToSide =
            Mathf.Abs(direction.x) > 0.001f
                ? halfWidth / Mathf.Abs(direction.x)
                : float.PositiveInfinity;

        float distanceToTopBottom =
            Mathf.Abs(direction.y) > 0.001f
                ? halfHeight / Mathf.Abs(direction.y)
                : float.PositiveInfinity;

        float edgeDistance = Mathf.Min(
            distanceToSide,
            distanceToTopBottom);

        markerRect.anchoredPosition =
            direction * edgeDistance;

        dangerImage.enabled = true;
    }

    private static void ClearButtonSelection()
    {
        EventSystem eventSystem = EventSystem.current;

        if (eventSystem != null)
        {
            eventSystem.SetSelectedGameObject(null);
        }
    }

    private void RefreshUI()
    {
        bool isPreparing = nightDefense.IsPreparing;
        bool isRunning = nightDefense.IsRunning;

        SetGroupVisible(
            playGroup,
            !nightDefense.IsDefeated,
            allowInput: false);

        SetGroupVisible(
            victoryGroup,
            nightDefense.HasRoundResult,
            allowInput: (!PlayerCombatStats.ActionsBlocked || nightDefense.IsStageCleared) && !GameExitUI.IsMenuOpen);

        RefreshButtons();
        RefreshStatusText();
        RefreshCombatHud();
    }

    private void RefreshButtons()
    {
        if (resultTitle != null)
        {
            var screen = (RectTransform)resultTitle.canvas.rootCanvas.transform;
            resultTitle.rectTransform.anchoredPosition = new Vector2(0f, 105f);
            resultTitle.rectTransform.sizeDelta = new Vector2(Mathf.Min(820f, Mathf.Max(260f, screen.rect.width - 48f)), 140f);
        }
        if (resultTitle != null) resultTitle.text = nightDefense.IsSettlement
            ? "전리품 정산\n" + RunEconomy.LastSettlement.Split('\n')[0] + "\nI 판매/모집 · E 강화/타워"
            : (nightDefense.IsStageCleared ? "지역 클리어" : "밤 생존 완료") +
                $"\n획득 예정: 일반 {RunEconomy.PendingCommonLoot} · 희귀 {RunEconomy.PendingRareLoot}\n아래 정산 버튼으로 보상을 수령하세요.";

        nextDayButton.interactable =
            nightDefense.CanAdvanceResult && !GameExitUI.IsMenuOpen;

        nextDayButtonText.text = nightDefense.IsChangingScene
            ? "LOADING..."
            : nightDefense.ResultButtonLabel;
    }

    private void RefreshStatusText()
    {
        if (!nightDefense.IsInitialized) { SetStatusText("전투 준비 중..."); return; }
        string message = $"캠프 HP {nightDefense.CurrentBaseHp}/{nightDefense.MaxBaseHp}  |  공격 {nightDefense.CurrentAttackDamage}\n" +
            $"목재 {RunResources.StoredWood}  |  골드 {RunResources.StoredGold}  |  처치 {nightDefense.DefeatedCount}/{nightDefense.TotalEnemyCount}\n" +
            $"보유 전리품  일반 {RunEconomy.CommonLoot} · 희귀 {RunEconomy.RareLoot}";
        if (nightDefense.IsRunning || nightDefense.IsEscaping || (nightDefense.HasRoundResult && !nightDefense.IsSettlement))
            message += $"\n이번 밤 획득 예정  일반 {RunEconomy.PendingCommonLoot} · 희귀 {RunEconomy.PendingRareLoot} (정산 시 수령)";
        if (nightDefense.IsPreparing) message += "\nB 건설 · I 판매/모집 · E 강화/타워 · R 상호작용";
        if (nightDefense.IsSettlement) message += "\n" + RunEconomy.LastSettlement.Split('\n')[0] + "\nI 판매/모집 · E 강화/타워";
        if (nightDefense.IsDefeated) message += "\n패배: 이번 밤 미정산 전리품은 획득하지 않습니다.";
        if (nightDefense.IsEscaping) message += "\n남은 적이 도주 중입니다.";
        if (nightDefense.IsStageCleared && nightDefense.HasRoundResult) message += "\n균열 파괴 · 지역 클리어";
        if (nightDefense.IsPreparing || nightDefense.IsRunning)
        {
            message += $"\n활성 적 {nightDefense.ActiveEnemyCount} · DDA {nightDefense.EnemyCountAdjustment:+0;-0;0}";
            if (RunProgress.TryGetDayStats(nightDefense.NightDay, out DayPlayStats stats) && stats.HasJudgments)
                message += $" · 적중 {stats.HitRate * 100f:F1}% · Perfect {stats.PerfectRate * 100f:F1}% · Miss {stats.MissRate * 100f:F1}%";
        }
        if ((nightDefense.HasRoundResult || nightDefense.IsDefeated) &&
            RunProgress.TryGetNightStats(nightDefense.NightDay, out NightBattleStats result))
            message += $"\n전투 {result.DurationSeconds:F1}초 · 캠프 HP {result.BaseHpRatio * 100f:F1}%";
        if (nightDefense.RiftHint.Length > 0) message += "\n" + nightDefense.RiftHint;
        SetStatusText(message);
    }

    private void CreateContextHud()
    {
        Canvas canvas = statusText.canvas;
        if (canvas == null) return;
        forestEntrance = FindAnyObjectByType<ForestEntranceInteraction>();
        npcCommands = FindAnyObjectByType<CampNpcCommandController>();
        placement = FindAnyObjectByType<CampWallPlacementController>();
        contextHud = MakeHudRect("InteractionContextHUD", canvas.rootCanvas.transform,
            new Vector2(720f, 240f), new Vector2(18f, 18f));
        contextHud.anchorMin = contextHud.anchorMax = contextHud.pivot = Vector2.zero;
        Image panel = contextHud.gameObject.AddComponent<Image>();
        panel.color = new Color(0.025f, 0.04f, 0.065f, 0.9f);
        panel.raycastTarget = false;
        contextLabel = MakeHudLabel(contextHud, Vector2.zero, Vector2.zero);
        contextLabel.alignment = TextAlignmentOptions.TopLeft;
        contextLabel.enableAutoSizing = true;
        contextLabel.fontSizeMin = 13f;
        contextLabel.fontSizeMax = 18f;
        contextLabel.overflowMode = TextOverflowModes.Ellipsis;
        contextLabel.textWrappingMode = TextWrappingModes.Normal;
        contextOwner = this;
        // 기존 텍스트는 alpha=0인 NightDebugRoot의 자식이었다. 실제 플레이 HUD로 표시한다.
        statusText.rectTransform.SetParent(canvas.rootCanvas.transform, false);
        statusText.alignment = TextAlignmentOptions.TopLeft;
        statusText.fontSize = 17f;
        statusText.textWrappingMode = TextWrappingModes.NoWrap;
        statusText.overflowMode = TextOverflowModes.Ellipsis;
        statusText.raycastTarget = false;
    }

    private void RefreshContextHud()
    {
        if (contextHud == null) return;
        RectTransform canvas = statusText.canvas.rootCanvas.transform as RectTransform;
        float width = canvas.rect.width;
        float height = canvas.rect.height;
        contextHud.sizeDelta = new Vector2(Mathf.Min(720f, Mathf.Max(100f, width - 36f)),
            Mathf.Min(260f, Mathf.Max(100f, height * 0.4f)));
        contextLabel.rectTransform.anchorMin = Vector2.zero;
        contextLabel.rectTransform.anchorMax = Vector2.one;
        contextLabel.rectTransform.offsetMin = new Vector2(12f, 10f);
        contextLabel.rectTransform.offsetMax = new Vector2(-12f, -10f);
        RectTransform status = statusText.rectTransform;
        status.anchorMin = status.anchorMax = status.pivot = new Vector2(0f, 1f);
        status.anchoredPosition = new Vector2(18f, -TopHudInset);
        float availableHeight = Mathf.Max(50f, height - TopHudInset - contextHud.sizeDelta.y - 48f);
        status.sizeDelta = new Vector2(Mathf.Max(100f, width - 346f), Mathf.Min(180f, availableHeight));
        if (contextHud.sizeDelta.y + 18f > height - TopHudInset - 150f - 12f)
            contextHud.sizeDelta = new Vector2(Mathf.Max(100f, width - 346f), contextHud.sizeDelta.y);
        string message = placement != null ? placement.ContextMessage : "";
        if (message.Length == 0 && forestEntrance != null) message = forestEntrance.ContextMessage;
        string command = npcCommands != null ? npcCommands.ContextMessage : "";
        if (!CampWallPlacementController.IsPlacing && command.Length > 0)
            message += (message.Length > 0 ? "\n" : "") + command;
        if (contextLabel.text != message) contextLabel.text = message;
        contextHud.gameObject.SetActive(message.Length > 0 && !CampWindowController.WorldInputBlocked &&
            !PlayerCombatStats.ActionsBlocked && !nightDefense.IsChangingScene);
        // ���� �޴��� ��� ���ְ� �ٰ� Quit ��ư�� ������ �ʰ� �Ѵ�.
        if (combatHud != null) combatHud.gameObject.SetActive(!GameExitUI.IsMenuOpen &&
            nightDefense.IsInitialized && !nightDefense.IsChangingScene);
    }

    private RectTransform MakeHudRect(string objectName, Transform parent, Vector2 size, Vector2 position)
    {
        var item = new GameObject(objectName, typeof(RectTransform));
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private TMP_Text MakeHudLabel(Transform parent, Vector2 size, Vector2 position)
    {
        RectTransform rect = MakeHudRect("Label", parent, size, position);
        TMP_Text label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = statusText.font;
        label.fontSize = 17f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        return label;
    }

    private void CreateCombatHud()
    {
        Canvas canvas = statusText.canvas;
        if (canvas == null) return;
        combatHud = MakeHudRect("CommanderCombatHUD", canvas.rootCanvas.transform,
            new Vector2(292f, 150f), new Vector2(-18f, -18f));
        combatHud.anchorMin = combatHud.anchorMax = combatHud.pivot = new Vector2(1f, 1f);
        Image panel = combatHud.gameObject.AddComponent<Image>();
        panel.color = new Color(0.025f, 0.04f, 0.065f, 0.9f);
        panel.raycastTarget = false;
        Color[] colors = { new Color(0.3f, 0.9f, 0.45f), new Color(0.25f, 0.65f, 1f), new Color(1f, 0.75f, 0.15f) };
        for (int i = 0; i < 3; i++)
        {
            float y = -10f - i * 36f;
            gaugeLabels[i] = MakeHudLabel(combatHud, new Vector2(268f, 22f), new Vector2(12f, y));
            RectTransform track = MakeHudRect("GaugeTrack", combatHud, new Vector2(268f, 9f), new Vector2(12f, y - 23f));
            Image back = track.gameObject.AddComponent<Image>();
            back.color = new Color(0.15f, 0.19f, 0.24f);
            back.raycastTarget = false;
            RectTransform fill = MakeHudRect("GaugeFill", track, Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            gaugeFills[i] = fill.gameObject.AddComponent<Image>();
            gaugeFills[i].color = colors[i];
            gaugeFills[i].raycastTarget = false;
        }
        treatmentLabel = MakeHudLabel(combatHud, new Vector2(268f, 23f), new Vector2(12f, -121f));
    }

    private void SetGauge(int index, string label, float value, float maximum)
    {
        RectTransform fill = gaugeFills[index].rectTransform;
        fill.anchorMax = new Vector2(maximum > 0f ? Mathf.Clamp01(value / maximum) : 0f, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        string content = $"{label}  {value:F0} / {maximum:F0}";
        if (gaugeLabels[index].text != content) gaugeLabels[index].text = content;
    }

    private void RefreshCombatHud()
    {
        if (combatHud == null) return;
        combatHud.gameObject.SetActive(nightDefense.IsInitialized && !nightDefense.IsChangingScene);
        SetGauge(0, "체력", PlayerCombatStats.CurrentHp, PlayerCombatStats.MaximumHp);
        SetGauge(1, "방어", PlayerCombatStats.CurrentDefense, PlayerCombatStats.MaximumDefense);
        SetGauge(2, "스태미너", PlayerCombatStats.Stamina, PlayerCombatStats.MaximumStamina);
        bool hit = Time.time - PlayerCombatStats.LastHitTime < 0.18f;
        gaugeLabels[0].color = gaugeLabels[1].color = hit ? new Color(1f, 0.4f, 0.4f) : Color.white;
        string state = PlayerCombatStats.ActionsBlocked
            ? $"치료 중  {PlayerCombatStats.TreatmentRemaining:F1}초"
            : hit ? "피격!" : "Shift 달리기";
        if (treatmentLabel.text != state) treatmentLabel.text = state;
    }

    private void OnGUI()
    {
        if (!PlayerCombatStats.ActionsBlocked) return;
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.15f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private void SetStatusText(string message)
    {
        if (statusText.text != message)
        {
            statusText.text = message;
        }
    }

    private static void SetGroupVisible(
        CanvasGroup group,
        bool visible,
        bool allowInput)
    {
        float targetAlpha = visible ? 1.0f : 0.0f;
        bool canInteract = visible && allowInput;

        if (group.alpha == targetAlpha &&
            group.interactable == canInteract &&
            group.blocksRaycasts == canInteract)
        {
            return;
        }

        group.alpha = targetAlpha;
        group.interactable = canInteract;
        group.blocksRaycasts = canInteract;
    }

    private void CreateDangerGradient()
    {
        const int textureSize = 128;

        dangerTexture = new Texture2D(
            textureSize,
            textureSize,
            TextureFormat.RGBA32,
            false);

        dangerTexture.filterMode = FilterMode.Bilinear;
        dangerTexture.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[textureSize * textureSize];

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                // �̹��� �߽��� 0, ���� ���� -1�� 1�� �ǵ��� ��ȯ�Ѵ�.
                float normalizedX =
                    x / (textureSize - 1.0f) * 2.0f - 1.0f;

                float normalizedY =
                    y / (textureSize - 1.0f) * 2.0f - 1.0f;

                float distanceFromCenter = Mathf.Sqrt(
                    normalizedX * normalizedX +
                    normalizedY * normalizedY);

                float alpha = Mathf.Clamp01(
                    1.0f - distanceFromCenter);

                // �����ڸ��� ������ �� �����ϰ� �������.
                alpha *= alpha;

                pixels[y * textureSize + x] =
                    new Color(1.0f, 1.0f, 1.0f, alpha);
            }
        }

        dangerTexture.SetPixels(pixels);
        dangerTexture.Apply(false);

        dangerSprite = Sprite.Create(
            dangerTexture,
            new Rect(0, 0, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            100.0f,
            0,
            SpriteMeshType.FullRect);

        dangerImage.sprite = dangerSprite;
        dangerImage.type = Image.Type.Simple;
        dangerImage.preserveAspect = false;
        dangerImage.maskable = true;
    }

    // ���� ������ �̹����� �ؽ�ó�� �����Ѵ�.
    private void OnDestroy()
    {
        if (phaseHud != null) Destroy(phaseHud.gameObject);
        if (dayBanner != null) Destroy(dayBanner.gameObject);
        if (riftDirectionImage != null) Destroy(riftDirectionImage.gameObject);
        if (riftDirectionLabel != null) Destroy(riftDirectionLabel.gameObject);
        if (contextOwner == this) contextOwner = null;
        if (contextHud != null) Destroy(contextHud.gameObject);
        if (combatHud != null) Destroy(combatHud.gameObject);
        if (dangerSprite != null)
        {
            Destroy(dangerSprite);
        }

        if (dangerTexture != null)
        {
            Destroy(dangerTexture);
        }
    }
}
