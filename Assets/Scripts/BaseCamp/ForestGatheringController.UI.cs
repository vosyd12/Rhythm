using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class ForestGatheringController
{
    private TMP_Text gatheringLabel;
    private GameObject gatheringPanel;
    private Button musicButton, returnButton, resetMusicButton;
    private void BuildHud()
    {
        var root = new GameObject("ForestGatheringHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 80;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        TMP_FontAsset font = null;
        foreach (var text in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include))
            if (text.font != null) { font = text.font; if (font.name.Contains("Nanum")) break; }
        var panel = UiRect("채집 상태", root.transform, new Vector2(0.5f, 1), new Vector2(0, -125), new Vector2(900, 60));
        panel.gameObject.AddComponent<Image>().color = new Color(0.04f, 0.1f, 0.12f, 0.94f);
        panel.GetComponent<Image>().raycastTarget = false;
        gatheringPanel = panel.gameObject;
        var textRect = UiRect("Text", panel, Vector2.one * 0.5f, Vector2.zero, new Vector2(890, 56));
        gatheringLabel = textRect.gameObject.AddComponent<TextMeshProUGUI>(); gatheringLabel.font = font;
        gatheringLabel.fontSize = 17; gatheringLabel.alignment = TextAlignmentOptions.Center; gatheringLabel.raycastTarget = false;
        musicButton = UiButton(root.transform, "낮 음원 준비", new Vector2(1, 0), new Vector2(-95, 35), font, () => OpenMusicPreparation());
        returnButton = UiButton(root.transform, "캠프 조작으로 복귀", new Vector2(1, 0), new Vector2(-95, 90), font,
            () => { EndGathering(); ClosePreparation(); });
        resetMusicButton = UiButton(root.transform, "음원 다시 선택", new Vector2(1, 0), new Vector2(-95, 145), font, ResetMusicSelection);
        RefreshHud();
    }
    private static RectTransform UiRect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = pos; rect.sizeDelta = size; return rect;
    }
    private static Button UiButton(Transform parent, string caption, Vector2 anchor, Vector2 pos, TMP_FontAsset font, UnityEngine.Events.UnityAction action)
    {
        var rect = UiRect(caption, parent, anchor, pos, new Vector2(180, 45));
        var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0.12f, 0.3f, 0.36f);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None }; button.onClick.AddListener(action);
        var child = UiRect("Text", rect, Vector2.one * 0.5f, Vector2.zero, new Vector2(174, 43));
        var label = child.gameObject.AddComponent<TextMeshProUGUI>(); label.text = caption; label.font = font;
        label.fontSize = 17; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        return button;
    }
    private void RefreshHud()
    {
        if (gatheringLabel == null || battle == null) return;
        bool overlay = showPreparation || IsGathering;
        gatheringPanel.SetActive(battle.IsDay || overlay);
        // 준비창의 경로 입력·파일 선택 버튼을 안내 패널이 덮지 않게 한다.
        ((RectTransform)gatheringPanel.transform).anchoredPosition = new Vector2(0, showPreparation ? -220 : -125);
        gatheringLabel.text = IsGathering ? $"로봇 원격 채집 · 남은 구간 {GatheringSecondsRemaining:F1}초 · 낮 {battle.PhaseSecondsRemaining:F1}초\n{Status}" :
            $"{(showPreparation ? "음원 준비" : "캠프 운영")} · 남은 낮 {battle.PhaseSecondsRemaining:F1}초\n{Status}";
        musicButton.gameObject.SetActive(!overlay && battle.IsDay);
        musicButton.interactable = !GameExitUI.IsWorldPaused && !M7CampaignUI.IsModalOpen && !PlayerCombatStats.ActionsBlocked;
        returnButton.gameObject.SetActive(overlay);
        resetMusicButton.gameObject.SetActive(showPreparation);
    }
}
