using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 결과·선택·이어하기를 실제 버튼으로 제공한다. Inspector 연결을 새로 요구하지 않는다.
public sealed class M7CampaignUI : MonoBehaviour
{
    public static M7CampaignUI Instance { get; private set; }
    public static bool IsModalOpen => Instance != null && Instance.modal != null && Instance.modal.gameObject.activeSelf;
    public static bool IsSelectingCommandPoint => Instance != null && (Instance.selectingPoint || Instance.pointFrame == Time.frameCount);
    private NightDefenseTester battle;
    private TMP_FontAsset font;
    private RectTransform modal, content, footer;
    private TMP_Text title, summary, message, barLabel;
    private RectTransform bar;
    private Button campaignButton, advanceButton, retreatButton;
    private readonly HashSet<int> selected = new HashSet<int>();
    private readonly List<Action> refresh = new List<Action>();
    private bool startup, confirmNew, confirmLoad, selectingPoint, wallGold, towerGold;
    private string page = "", observedMode = "Playing", feedback = "";
    private int pointFrame;
    private bool ready;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInstance() => Instance = null;

    private IEnumerator Start()
    {
        Instance = this; battle = GetComponent<NightDefenseTester>();
        yield return null;
        var labels = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);
        foreach (var label in labels) if (label.font != null) { font = label.font; if (font.name.Contains("Nanum")) break; }
        Build();
        startup = !RunCampaign.SessionStarted && M7SaveFile.Exists;
        RunCampaign.SceneReady();
        observedMode = RunCampaign.Data.mode;
        if (startup) ShowStartup(); else if (observedMode != "Playing" && observedMode != "NightSettlement") ShowCampaign();
        ready = true;
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private static RectTransform Rect(string name, Transform parent)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        r.SetParent(parent, false); return r;
    }
    private static void Stretch(RectTransform r)
    { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
    private TMP_Text Label(Transform parent, string text, float height = 42)
    {
        var r = Rect("Text", parent); var label = r.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.text = text; label.fontSize = 18; label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.Normal; label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        var layout = r.gameObject.AddComponent<LayoutElement>(); layout.minHeight = layout.preferredHeight = height;
        return label;
    }
    private Button Button(Transform parent, string caption, Action action, float height = 45)
    {
        var r = Rect(caption, parent); var image = r.gameObject.AddComponent<Image>(); image.color = new Color(0.12f, 0.25f, 0.36f);
        var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var size = r.gameObject.AddComponent<LayoutElement>(); size.preferredHeight = size.minHeight = height;
        var label = Label(r, caption, height); Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(10, 2); label.rectTransform.offsetMax = new Vector2(-10, -2);
        label.enableAutoSizing = true; label.fontSizeMin = 12; label.fontSizeMax = 18;
        label.alignment = TextAlignmentOptions.Center;
        button.onClick.AddListener(() =>
        {
            if (RunCampaign.IsTravelling) return;
            action(); if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        });
        return button;
    }
    private void Vertical(RectTransform r, int padding = 0)
    {
        var layout = r.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 7;
        layout.padding = new RectOffset(padding, padding, padding, padding);
        layout.childControlHeight = layout.childControlWidth = true;
        layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
    }
    private void Build()
    {
        var canvasObject = new GameObject("M7CampaignCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 300;
        canvasObject.transform.SetParent(transform, false);
        var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
        bar = Rect("CampaignBar", canvas.transform); bar.anchorMin = bar.anchorMax = bar.pivot = Vector2.zero;
        bar.anchoredPosition = new Vector2(18, 18); bar.sizeDelta = new Vector2(650, 46);
        var row = bar.gameObject.AddComponent<HorizontalLayoutGroup>(); row.spacing = 5;
        row.childControlHeight = row.childControlWidth = true; row.childForceExpandHeight = row.childForceExpandWidth = true;
        barLabel = Label(bar, "", 46); barLabel.fontSize = 14;
        var statusWidth = barLabel.gameObject.GetComponent<LayoutElement>(); statusWidth.preferredWidth = 250;
        campaignButton = Button(bar, "캠페인·이어하기", ShowCampaign);
        advanceButton = Button(bar, "G 진격 지점", BeginAdvancePoint);
        retreatButton = Button(bar, "H 캠프 후퇴", RetreatTroops);

        modal = Rect("CampaignModal", canvas.transform); Stretch(modal);
        modal.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.8f);
        var panel = Rect("Panel", modal); panel.anchorMin = new Vector2(0.12f, 0.045f); panel.anchorMax = new Vector2(0.88f, 0.955f);
        panel.offsetMin = panel.offsetMax = Vector2.zero;
        panel.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.05f, 0.08f, 1);
        Vertical(panel, 18);
        title = Label(panel, "캠페인", 38); title.fontSize = 26;
        summary = Label(panel, "", 68); summary.fontSize = 16;
        var scrollRoot = Rect("Scroll", panel); var flexible = scrollRoot.gameObject.AddComponent<LayoutElement>(); flexible.flexibleHeight = 1;
        scrollRoot.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.07f, 0.1f);
        var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = Rect("Viewport", scrollRoot); Stretch(viewport); viewport.offsetMax = new Vector2(-16, 0);
        viewport.gameObject.AddComponent<RectMask2D>(); content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero; Vertical(content, 8);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content; scroll.viewport = viewport; scroll.scrollSensitivity = 30;
        var track = Rect("Scrollbar", scrollRoot); track.anchorMin = new Vector2(1, 0); track.anchorMax = Vector2.one;
        track.pivot = Vector2.right; track.sizeDelta = new Vector2(12, 0); track.anchoredPosition = Vector2.zero;
        track.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.13f, 0.18f);
        var handle = Rect("Handle", track); Stretch(handle); var handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = new Color(0.3f, 0.6f, 0.7f);
        var scrollbar = track.gameObject.AddComponent<Scrollbar>(); scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop; scroll.verticalScrollbar = scrollbar;
        message = Label(panel, "", 48); message.fontSize = 15; message.color = new Color(1, 0.85f, 0.45f);
        footer = Rect("Actions", panel);
        var footerSize = footer.gameObject.AddComponent<LayoutElement>();
        footerSize.minHeight = footerSize.preferredHeight = 48; footerSize.flexibleHeight = 0;
        var actions = footer.gameObject.AddComponent<HorizontalLayoutGroup>(); actions.spacing = 8;
        actions.childControlHeight = actions.childControlWidth = true; actions.childForceExpandHeight = actions.childForceExpandWidth = true;
        modal.gameObject.SetActive(false);
    }
    private void Clear(Transform parent)
    { for (int i = parent.childCount - 1; i >= 0; i--) { var go = parent.GetChild(i).gameObject; go.SetActive(false); Destroy(go); } }
    private void Open(string heading, string hint)
    {
        selectingPoint = false; CampWallPlacementController.Instance?.CancelPlacement();
        FindAnyObjectByType<CampWindowController>()?.CloseAll();
        Clear(content); Clear(footer); refresh.Clear(); title.text = heading; summary.text = hint; feedback = "";
        modal.gameObject.SetActive(true);
    }
    public void Close()
    {
        if (startup || RunCampaign.Data.mode == "Retreat" || RunCampaign.Data.mode == "Defeat" || RunCampaign.Data.mode == "Victory") return;
        modal.gameObject.SetActive(false); confirmNew = confirmLoad = false;
    }
    public void ShowStartup()
    {
        page = "Startup"; startup = true;
        Open("캠페인 이어하기", "저장된 진행이 있습니다. 마지막 체크포인트부터 이어가거나 새 캠페인을 시작하세요.");
        bool valid = M7SaveFile.TryRead(out var save, out string note);
        Label(content, valid ? $"{save.stage}지역 {RunCampaign.StageName(save.stage)} · {save.day}일차\n지휘관 {save.commander}대 · 상태 {save.mode}" : note, 80);
        Label(content, "자동 저장: 낮 시작 / 지역 이동 / 보상 확정\n중간 종료 시 마지막 저장 시점부터 다시 시작합니다.", 70);
        if (valid) Button(footer, "이어하기", LoadSaved);
        Button(footer, "새 캠페인", NewCampaign);
    }
    public void ShowCampaign()
    {
        if (modal == null || RunCampaign.IsTravelling || ForestGatheringController.BlocksCommands) return;
        if (PlayerCombatStats.ActionsBlocked && (RunCampaign.Data.mode == "Playing" || RunCampaign.Data.mode == "NightSettlement") && !startup) return;
        if (startup) { ShowStartup(); return; }
        string mode = RunCampaign.Data.mode;
        if (mode == "Advance") { ShowAdvance(); return; }
        if (mode == "Retreat") { ShowRetreat(); return; }
        if (mode == "Defeat" || mode == "Victory") { ShowEnding(mode == "Victory"); return; }
        page = "Overview";
        Open("캠페인 진행", $"{RunCampaign.Stage}지역 · {RunCampaign.StageName(RunCampaign.Stage)} / {RunProgress.CurrentDay}일차 · 지휘관 {RunCampaign.Data.commander}대");
        for (int i = 1; i <= 4; i++)
        {
            int stage = i; var clear = RunCampaign.Data.clears.Find(s => s.stage == stage);
            Label(content, $"{stage}. {RunCampaign.StageName(stage)}" + (stage == RunCampaign.Stage ? " ← 현재 전선" : "") +
                (clear == null ? "" : $"\n클리어 기록 #{clear.revision}: 벽 {clear.walls} / 타워 {clear.towers} · 보상 {(clear.claimed ? "수령함" : "미수령")}"), 62);
        }
        Label(content, $"복구 건설권: 벽 {RunCampaign.Data.wallTickets} / 타워 {RunCampaign.Data.towerTickets}\nB에서 원하는 위치에 배치하면 목재·작업자 없이 레벨1로 즉시 완공합니다.", 70);
        Label(content, "F로 전투 NPC를 호출 → G 또는 진격 버튼 → 지도 클릭\nH 또는 후퇴 버튼: 현재 추종 전투 NPC만 캠프로 복귀\n작업자·타워 상주자는 기존 임무를 유지합니다.", 90);
        Label(content, "저장 파일: " + M7SaveFile.PathName + "\n" + RunCampaign.Status, 80);
        Button(content, "M7 설정 다시 읽기", () => { bool ok = RunCampaign.ReloadRules(out string e); feedback = ok ? "M7 설정을 읽었습니다. 다음 이동·밤부터 적용합니다." : e; });
        Label(content, "M7 설정: " + RunCampaign.RulesPath, 48);
        if (M7SaveFile.Exists) Button(footer, "마지막 저장 불러오기", () =>
        {
            if (!confirmLoad) { confirmLoad = true; feedback = "현재 저장되지 않은 진행은 사라집니다. 같은 버튼을 다시 누르면 불러옵니다."; return; }
            LoadSaved();
        });
        Button(footer, "새 캠페인", NewCampaign); Button(footer, "닫기", Close);
    }
    private void LoadSaved()
    { if (!RunCampaign.Continue(battle.DaySceneName, out string error)) feedback = error; }
    private void NewCampaign()
    {
        if (!confirmNew) { confirmNew = true; feedback = "현재 저장 슬롯을 새 캠페인으로 교체합니다. 새 캠페인 버튼을 다시 눌러 확정하세요."; return; }
        if (!RunCampaign.NewCampaign(battle.DaySceneName, out string error)) feedback = error;
    }
    private void ShowAdvance()
    {
        page = "Advance";
        Open("동행 대원 선택", $"{RunCampaign.Stage}지역 확보 → {RunCampaign.Stage + 1}지역 {RunCampaign.StageName(RunCampaign.Stage + 1)}\n최대 {RunCampaign.Rules.companionLimit}명 · 레벨·EXP·성장·무기·개인 이름 유지");
        var units = new List<CampNpcData>(); foreach (var n in RunCampNpcs.All) if (n.IsAlive) units.Add(n);
        units.Sort((a, b) => a.Id.CompareTo(b.Id)); selected.RemoveWhere(id => !units.Exists(n => n.Id == id));
        foreach (var n in units)
        {
            int id = n.Id;
            var button = Button(content, "", () =>
            {
                if (!selected.Remove(id))
                { if (selected.Count >= RunCampaign.Rules.companionLimit) { feedback = "동행 인원이 가득 찼습니다."; return; } selected.Add(id); }
            }, 54);
            var label = button.GetComponentInChildren<TMP_Text>();
            refresh.Add(() => label.text = $"{(selected.Contains(id) ? "[선택]" : "[  ]")} {n.Name} · {Profession(n.Profession)} · Lv.{n.Level} / EXP {n.Experience}\nHP {n.MaximumHp} · 방어 {n.MaximumDefense} · 공격 성장 +{n.Strength}");
        }
        var rules = RunCampaign.Rules;
        Label(content, $"목재 {RunResources.StoredWood} → {RunCampaign.TransferAmount(RunResources.StoredWood, rules.advanceWood)} / 골드 {RunResources.StoredGold} → {RunCampaign.TransferAmount(RunResources.StoredGold, rules.advanceGold)}\n전리품·장비·공통 강화 유지 · 새 지역 작업자 {rules.startingWorkers}명 추가 · 1일차부터 시작", 78);
        Button(footer, "선택 대원과 진격", () => { if (!RunCampaign.Advance(selected, battle.DaySceneName, out string error)) feedback = error; });
        Button(footer, "정산으로 돌아가기", Close);
    }
    private static string Profession(CampNpcProfession p) => p == CampNpcProfession.Ranged ? "원거리" : p == CampNpcProfession.Engineer ? "공병" : "작업자";
    private void ShowRetreat()
    {
        page = "Retreat";
        Open("전선 붕괴 · 후퇴 보상", $"{RunCampaign.Stage}지역 상실 → {RunCampaign.Stage - 1}지역 {RunCampaign.StageName(RunCampaign.Stage - 1)}\n새 지휘관·작업자 {RunCampaign.Rules.startingWorkers}명 · 골드 {RunCampaign.Rules.legacyGold:P0} 유품 계승");
        var snapshot = RunCampaign.RetreatSnapshot();
        Label(content, snapshot == null ? "도착 지역의 클리어 기록이 없어 건물 보상이 없습니다." :
            $"도착 지역의 마지막 클리어 기록 #{snapshot.revision}\n완공·생존 벽 {snapshot.walls} / 타워 {snapshot.towers}" + (snapshot.claimed ? "\n이미 수령한 기록입니다. 추가 보상은 없습니다." : ""), 90);
        var wall = Button(content, "", () => wallGold = !wallGold);
        var tower = Button(content, "", () => towerGold = !towerGold);
        refresh.Add(() => wall.GetComponentInChildren<TMP_Text>().text = "벽 보상: " + (wallGold ? "골드 환산" : "무료 즉시 건설권") + " · 클릭해서 변경");
        refresh.Add(() => tower.GetComponentInChildren<TMP_Text>().text = "타워 보상: " + (towerGold ? "골드 환산" : "무료 즉시 건설권") + " · 클릭해서 변경");
        var preview = Label(content, "", 85);
        refresh.Add(() => preview.text = $"보상 골드 {RunCampaign.Refund(snapshot, wallGold, towerGold)} + 유품 골드 {RunCampaign.TransferAmount(RunResources.StoredGold, RunCampaign.Rules.legacyGold)}\n건물 환산: 레벨1 기본 건설비 × {RunCampaign.Rules.buildingRefund:P0}\n재건 레벨1 · 기존 건물 위치/업그레이드/일반 병력은 계승하지 않습니다.");
        Button(footer, "보상 확정 · 이전 지역으로 후퇴", () =>
        { if (!RunCampaign.Retreat(wallGold, towerGold, battle.DaySceneName, out string error)) feedback = error; });
    }
    private void ShowEnding(bool victory)
    {
        page = "Ending";
        Open(victory ? "캠페인 최종 승리" : "최후 방어선 붕괴", victory ? "대도시의 균열을 파괴했습니다. 네 지역을 수복했습니다." : "작은 마을의 캠프가 무너졌습니다. 이번 캠페인이 종료되었습니다.");
        Label(content, $"지휘관 {RunCampaign.Data.commander}대 · {RunProgress.CurrentDay}일차\n보유 골드 {RunResources.StoredGold} · 일반 전리품 {RunEconomy.CommonLoot} · 희귀 전리품 {RunEconomy.RareLoot}", 100);
        Button(footer, "새 캠페인", NewCampaign);
    }
    private bool CanOrder() => ready && !IsModalOpen && !GameExitUI.IsWorldPaused && !RunCampaign.IsTravelling &&
        !PlayerCombatStats.ActionsBlocked && !CampWindowController.WorldInputBlocked && !CampWallPlacementController.IsPlacing &&
        (battle.IsPreparing || battle.IsRunning) && RunCampaign.Data.mode == "Playing";
    public void BeginAdvancePoint()
    { if (!CanOrder()) return; selectingPoint = true; pointFrame = Time.frameCount; feedback = "진격 지점을 지도에서 클릭하세요. 우클릭/Esc 취소"; }
    public void RetreatTroops()
    {
        if (!CanOrder()) return; selectingPoint = false;
        int count = RunCampNpcs.IssueCombatOrder(Vector3.zero, true); feedback = $"추종 전투 NPC {count}명 캠프 복귀";
    }
    private void Update()
    {
        if (!ready) return;
        var screen = (RectTransform)bar.GetComponentInParent<Canvas>().transform;
        bar.anchoredPosition = new Vector2(18, screen.rect.width < 1150 ? 72 : 18);
        bar.sizeDelta = new Vector2(Mathf.Min(650, screen.rect.width - 36), 46);
        campaignButton.interactable = !RunCampaign.IsTravelling && (!PlayerCombatStats.ActionsBlocked || (RunCampaign.Data.mode != "Playing" && RunCampaign.Data.mode != "NightSettlement"));
        advanceButton.interactable = retreatButton.interactable = CanOrder();
        barLabel.text = $"{RunCampaign.Stage}지역 {RunCampaign.StageName(RunCampaign.Stage)} · {RunProgress.CurrentDay}일차\n" +
            (selectingPoint ? "진격 지점 클릭 · 우클릭 취소" : feedback.Length > 0 && !IsModalOpen ? feedback : $"복구권 벽 {RunCampaign.Data.wallTickets} / 타워 {RunCampaign.Data.towerTickets}");
        string mode = RunCampaign.Data.mode;
        if (mode != observedMode) { observedMode = mode; if (mode != "Playing" && mode != "NightSettlement") ShowCampaign(); }
        if (IsModalOpen)
        {
            message.text = feedback.Length > 0 ? feedback : page == "Advance" ? $"동행 {selected.Count}/{RunCampaign.Rules.companionLimit}명 선택 · 목록은 휠로 스크롤" : RunCampaign.Status;
            foreach (var action in refresh) action();
        }
        var keyboard = Keyboard.current;
        if (!Application.isFocused || keyboard == null) return;
        if (IsModalOpen) { if (keyboard.escapeKey.wasPressedThisFrame) Close(); return; }
        if (CanOrder())
        {
            if (keyboard.gKey.wasPressedThisFrame) BeginAdvancePoint();
            if (keyboard.hKey.wasPressedThisFrame) RetreatTroops();
        }
        if (!selectingPoint) return;
        if (!CanOrder() || keyboard.escapeKey.wasPressedThisFrame || Mouse.current?.rightButton.wasPressedThisFrame == true)
        { selectingPoint = false; return; }
        if (Time.frameCount <= pointFrame || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) return;
        var camera = Camera.main; if (camera == null) return;
        Vector3 point = camera.ScreenToWorldPoint(Mouse.current.position.ReadValue()); point.z = 0;
        if (CampWallNavigation.Instance == null || !CampWallNavigation.Instance.MapBounds.Contains(point))
        { feedback = "맵 안의 지점을 선택하세요."; return; }
        int issued = RunCampNpcs.IssueCombatOrder(point, false);
        feedback = $"추종 전투 NPC {issued}명 진격 · 도착 후 대기/사격"; selectingPoint = false; pointFrame = Time.frameCount;
    }
}
