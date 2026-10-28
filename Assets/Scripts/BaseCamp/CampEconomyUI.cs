using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 기존 B/I/E 창의 실제 조작 화면. 창 닫기와 월드 입력 차단은 CampWindowController가 담당한다.
public sealed class CampEconomyUI : MonoBehaviour
{
    private NightDefenseTester battle;
    private TMP_FontAsset font;
    private RectTransform root;
    private TMP_Text balance, feedback;
    private ScrollRect scroll;
    private Action refreshMenu;
    private int initialPage;
    private readonly List<Action> refreshers = new List<Action>();
    private readonly List<RectTransform> pages = new List<RectTransform>();
    private readonly List<Image> tabImages = new List<Image>();

    public static void Attach(GameObject panel, TMP_FontAsset font, NightDefenseTester battle,
        bool equipment, bool construction = false)
    {
        if (panel == null || panel.GetComponent<CampEconomyUI>() != null) return;
        panel.AddComponent<CampEconomyUI>().Build(font, battle, equipment, construction);
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private TMP_Text Label(Transform parent, string text, float height)
    {
        var rect = Rect("Text", parent);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.fontSize = 18; label.color = new Color(0.92f, 0.95f, 1f);
        label.text = text; label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.Normal;
        var layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = layout.minHeight = height;
        return label;
    }

    private Button Button(Transform parent, Func<string> caption, Func<bool> callback,
        Func<bool> available = null, Func<string> success = null, bool gameplay = true, float height = 58,
        Func<string> failure = null)
    {
        var rect = Rect("Action", parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.12f, 0.23f, 0.34f, 1);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var size = rect.gameObject.AddComponent<LayoutElement>();
        size.preferredHeight = size.minHeight = height;
        var label = Label(rect, "", height); Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(12, 2);
        label.rectTransform.offsetMax = new Vector2(-12, -2);
        label.enableAutoSizing = true; label.fontSizeMin = 13; label.fontSizeMax = 18;
        refreshers.Add(() =>
        {
            string next = caption();
            if (label.text != next) label.text = next;
            button.interactable = (!gameplay || battle.CanUseEconomy) && (available == null || available());
        });
        button.onClick.AddListener(() =>
        {
            if ((gameplay && !battle.CanUseEconomy) || (available != null && !available())) return;
            bool ok = callback();
            feedback.text = ok ? (success != null ? success() : "처리 완료 · 보유량이 반영되었습니다.")
                : (failure != null ? failure() : "처리하지 못했습니다. 재화·인원 한도·캠프 주변 빈 공간을 확인하세요.");
            if (EventSystemAvailable()) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            RefreshContents();
        });
        return button;
    }

    private static bool EventSystemAvailable() => UnityEngine.EventSystems.EventSystem.current != null;

    private void Build(TMP_FontAsset typeface, NightDefenseTester tester, bool equipment, bool construction)
    {
        font = typeface; battle = tester; initialPage = equipment ? 2 : 0;
        root = Rect("CampOperationsPanel", transform); Stretch(root);
        root.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.045f, 0.075f, 1f);
        var outer = root.gameObject.AddComponent<VerticalLayoutGroup>();
        outer.padding = new RectOffset(16, 16, 12, 12); outer.spacing = 6;
        outer.childControlHeight = outer.childControlWidth = true;
        outer.childForceExpandHeight = false; outer.childForceExpandWidth = true;
        Label(root, construction ? "건설 지시  ·  B / Esc 닫기" : equipment ? "강화·장비·타워  ·  E / Esc 닫기" : "판매·모집  ·  I / Esc 닫기", 30);
        balance = Label(root, "", 48);
        feedback = Label(root, construction ? "건물을 고른 뒤 지도를 클릭해 위치를 지정하세요."
            : "I → 판매 / E → 성장·장비에서 타워 강화 · 아래 항목은 휠로 스크롤하세요.", 44);
        feedback.fontSize = 15;
        RectTransform tabs = null;
        if (!construction)
        {
            tabs = Rect("Tabs", root);
            var tabLayout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabLayout.spacing = 5; tabLayout.childControlWidth = true; tabLayout.childForceExpandWidth = true;
            var tabSize = tabs.gameObject.AddComponent<LayoutElement>();
            tabSize.minHeight = tabSize.preferredHeight = 38; tabSize.flexibleHeight = 0;
        }
        var viewport = Rect("Scroll", root);
        var viewportSize = viewport.gameObject.AddComponent<LayoutElement>();
        viewportSize.minHeight = 32; viewportSize.flexibleHeight = 1;
        viewport.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.06f, 0.09f);
        viewport.gameObject.AddComponent<RectMask2D>();
        scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport;
        scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 35;
        for (int i = 0; i < (construction ? 1 : 4); i++)
        {
            var page = Rect("Page" + i, viewport);
            page.anchorMin = new Vector2(0, 1); page.anchorMax = Vector2.one;
            page.pivot = new Vector2(0.5f, 1); page.sizeDelta = Vector2.zero;
            var layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8; layout.padding = new RectOffset(6, 22, 6, 6);
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            page.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            pages.Add(page);
            if (tabs == null) continue;
            int index = i; string caption = new[] { "판매", "모집", "성장·장비", "정산·기록" }[i];
            var tab = Button(tabs, () => caption, () => { Select(index); return true; }, gameplay: false, height: 38);
            tabImages.Add(tab.GetComponent<Image>());
        }
        var scrollbarRect = Rect("Scrollbar", viewport);
        scrollbarRect.anchorMin = new Vector2(1, 0); scrollbarRect.anchorMax = Vector2.one;
        scrollbarRect.sizeDelta = new Vector2(10, -8); scrollbarRect.anchoredPosition = new Vector2(-6, 0);
        scrollbarRect.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.13f, 0.19f);
        var handle = Rect("Handle", scrollbarRect); Stretch(handle);
        var handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = new Color(0.35f, 0.6f, 0.75f);
        var scrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = scrollbar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        if (construction) BuildConstruction(pages[0]);
        else
        {
            BuildTrade(pages[0]); BuildRecruitment(pages[1]);
            BuildUpgrades(pages[2]); BuildRecords(pages[3]);
        }
        Select(initialPage); RefreshContents();
    }

    private void BuildConstruction(RectTransform page)
    {
        var placement = CampWallPlacementController.Instance;
        if (placement == null) { Label(page, "건설 컨트롤러가 연결되지 않았습니다.", 60); return; }
        Label(page, "1. 벽 또는 타워 선택 → 지도에서 위치 클릭\n2. 목재 차감 후 대기 중인 백수/공병 1명이 이동해 건설\n벽은 드래그로 연속 지정 · 우클릭/Esc로 배치 종료", 90);
        Button(page, () => $"벽 건설 · 한 칸 {placement.WallWoodCost}목재\n" +
            (RunCampaign.Data.wallTickets > 0 ? $"복구권 {RunCampaign.Data.wallTickets}개 우선 사용 · 무료 즉시 완공" : RunResources.StoredWood < placement.WallWoodCost ? "목재 부족 · 숲에서 채집하세요." : "클릭: 한 칸 / 드래그: 가로·세로 연속 배치"),
            () => { placement.BeginWallPlacement(); return CampWallPlacementController.IsPlacing; },
            () => placement.CanBeginPlacement && (RunCampaign.Data.wallTickets > 0 || RunResources.StoredWood >= placement.WallWoodCost));
        Button(page, () => $"타워 건설 · 한 채 {placement.TowerWoodCost}목재\n" +
            (RunCampaign.Data.towerTickets > 0 ? $"복구권 {RunCampaign.Data.towerTickets}개 우선 사용 · 무료 즉시 완공" : RunResources.StoredWood < placement.TowerWoodCost ? "목재 부족 · 숲에서 채집하세요." : "위치 지정 → 완공 후 원거리 NPC를 수동 입장"),
            () => { placement.BeginTowerPlacement(); return CampWallPlacementController.IsPlacing; },
            () => placement.CanBeginPlacement && (RunCampaign.Data.towerTickets > 0 || RunResources.StoredWood >= placement.TowerWoodCost));
        var workers = Label(page, "", 100);
        refreshers.Add(() =>
        {
            int idle = 0;
            foreach (var npc in RunCampNpcs.All)
                if (npc.CanBuild && npc.IsInCampScene && npc.HasSavedPosition && !npc.HasPendingTowerExit &&
                    !npc.IsFollowing && !npc.IsReturningToCamp && npc.AssignedConstructionSlotId == 0 && npc.AssignedDefensePostId == 0) idle++;
            workers.text = $"자동 배정 가능한 작업자: {idle}명\n" + (idle > 0
                ? "착공하면 작업자 1명이 자동 배정됩니다. 이동 후 진행률이 오릅니다."
                : "작업자 부족: I → 모집에서 백수/공병 모집 또는 추종 작업자를 F로 대기시키세요.") +
                "\n추가 투입: 작업자 근처 F 호출 → 현장 근처 Space 짧게";
        });
        Label(page, "타워는 무인 공격하지 않습니다. 원거리 NPC 근처 F → 완공 타워 근처 Space 짧게로 입장시킵니다.\n타워 강화: 지휘관이 타워에 접근한 뒤 E → 성장·장비", 90);
    }

    private void BuildTrade(RectTransform page)
    {
        Label(page, "전리품은 밤 종료 후 정산 버튼으로 수령합니다.\n여기에서 원하는 항목을 수동 판매해 골드를 얻으세요.", 60);
        foreach (int amount in new[] { 1, 10 })
        {
            int count = amount;
            Button(page, () => $"목재 {count} 판매 → {count * (long)M6Balance.Snapshot().woodGold}골드" +
                (RunResources.StoredWood < count ? " · 목재 부족" : ""),
                () => RunResources.TrySellWood(count, M6Balance.Snapshot().woodGold),
                () => RunResources.StoredWood >= count, () => $"목재 {count} 판매 완료 · 현재 골드 {RunResources.StoredGold}");
        }
        Button(page, () => $"일반 전리품 {RunEconomy.CommonLoot}개 모두 판매 → {(long)RunEconomy.CommonLoot * M6Balance.Snapshot().commonGold}골드\n개당 {M6Balance.Snapshot().commonGold}골드" + (RunEconomy.CommonLoot == 0 ? " · 보유 전리품 없음" : ""),
            () => RunEconomy.SellLoot(false), () => RunEconomy.CommonLoot > 0,
            () => $"일반 전리품 판매 완료 · 현재 골드 {RunResources.StoredGold}");
        Button(page, () => $"희귀 전리품 {RunEconomy.RareLoot}개 모두 판매 → {(long)RunEconomy.RareLoot * M6Balance.Snapshot().rareGold}골드\n개당 {M6Balance.Snapshot().rareGold}골드" + (RunEconomy.RareLoot == 0 ? " · 보유 전리품 없음" : ""),
            () => RunEconomy.SellLoot(true), () => RunEconomy.RareLoot > 0,
            () => $"희귀 전리품 판매 완료 · 현재 골드 {RunResources.StoredGold}");
    }

    private void BuildRecruitment(RectTransform page)
    {
        foreach (var profession in new[] { CampNpcProfession.Unemployed, CampNpcProfession.Engineer, CampNpcProfession.Ranged })
        {
            var job = profession;
            string name = job == CampNpcProfession.Unemployed ? "백수" : job == CampNpcProfession.Engineer ? "공병" : "원거리";
            Func<int> price = () => job == CampNpcProfession.Unemployed ? M6Balance.Snapshot().unemployedCost :
                job == CampNpcProfession.Engineer ? M6Balance.Snapshot().engineerCost : M6Balance.Snapshot().rangedCost;
            Button(page, () => $"{name} 모집 · {price()}골드\n" + (RunEconomy.LivingCount >= EffectivePopulationLimit ? "인구 한도 도달" : RunResources.StoredGold < price() ? "골드 부족 · 판매 탭을 이용하세요." : "캠프 주변 빈 공간에 즉시 합류"),
                () => battle.TryRecruitNpc(job), () => RunResources.StoredGold >= price() && RunEconomy.LivingCount < EffectivePopulationLimit,
                () => $"{name} 모집 완료 · 병력 {RunEconomy.LivingCount}/{EffectivePopulationLimit}");
        }
        Button(page, () => $"인구 한도 +{M6Balance.Snapshot().populationStep} · {M6Balance.Snapshot().populationCost}골드\n현재 {EffectivePopulationLimit} / 최대 {M6Balance.Snapshot().populationMaximum}" +
            (EffectivePopulationLimit >= M6Balance.Snapshot().populationMaximum ? " · 최대 한도" : RunResources.StoredGold < M6Balance.Snapshot().populationCost ? " · 골드 부족" : ""),
            RunEconomy.ExpandPopulation, () => EffectivePopulationLimit < M6Balance.Snapshot().populationMaximum && RunResources.StoredGold >= M6Balance.Snapshot().populationCost);
        Label(page, "기존 NPC도 인구에 포함하며 사망자는 자리를 반환합니다.\n백수/공병: 건설·수리 / 원거리: 사격·타워 입장", 64);
        Label(page, "원거리는 준비된 대원 유형 중 하나가 무작위로 합류합니다. 선택된 유형의 무기·사거리·공격 속도·HP·방어를 그대로 사용합니다.", 68);
    }

    private int EffectivePopulationLimit => Mathf.Min(RunEconomy.PopulationLimit, M6Balance.Snapshot().populationMaximum);

    private void BuildUpgrades(RectTransform page)
    {
        var towerInfo = Label(page, "", 110);
        refreshers.Add(() =>
        {
            var tower = battle.NearestUpgradeTower;
            towerInfo.text = tower == null ? "타워 업그레이드: 지휘관이 완공 타워 가까이 이동한 뒤 E를 여세요.\n현재 범위 안에 완공 타워가 없습니다.\n레벨 1~3 정원 1명 / 4~6 정원 2명 / 7~9 정원 3명"
                : $"대상 타워 #{tower.PostId} · Lv.{tower.TowerLevel} · 입장 {tower.EnteredCount}/{tower.Capacity}명\nHP {tower.CurrentHp}/{tower.MaximumHp} · 방어 {tower.CurrentDefense}/{tower.MaximumDefense}\n사거리 ×1.25 · 공격속도 ×1.2 · 공격 +2 (레벨 공통)";
        });
        Button(page, () =>
        {
            var tower = battle.NearestUpgradeTower;
            if (tower == null) return "타워 업그레이드 · 완공 타워 가까이 이동하세요.";
            if (tower.TowerLevel >= M6Balance.Snapshot().towerMaximum) return $"타워 #{tower.PostId} · 최대 Lv.{tower.TowerLevel}";
            int cost = tower.TowerLevel * M6Balance.Snapshot().towerWoodPerLevel;
            return $"타워 #{tower.PostId} Lv.{tower.TowerLevel} → {tower.TowerLevel + 1} · {cost}목재\n" +
                (RunResources.StoredWood < cost ? "목재 부족" : "입장 병력·손상·공격 대기시간 유지");
        }, battle.TryUpgradeNearestTower, () => battle.NearestUpgradeTower != null &&
            battle.NearestUpgradeTower.TowerLevel < M6Balance.Snapshot().towerMaximum &&
            RunResources.StoredWood >= battle.NearestUpgradeTower.TowerLevel * M6Balance.Snapshot().towerWoodPerLevel,
            () => $"타워 강화 완료 · Lv.{battle.NearestUpgradeTower.TowerLevel} · 정원 {battle.NearestUpgradeTower.Capacity}명");
        Button(page, () => $"공통 공격 +1 · {M6Balance.Snapshot().attackCost}골드 (현재 +{RunProgress.AttackDamageBonus})\n" +
            (RunProgress.AttackDamageBonus >= M6Balance.Snapshot().attackMaximum ? "최대 강화 도달" : RunProgress.HasPurchasedAttackUpgradeToday ? "오늘 구매 완료 · 다음 날 다시 구매 가능" : !battle.IsPreparing ? "낮·중립에 구매 가능" : RunResources.StoredGold < M6Balance.Snapshot().attackCost ? "골드 부족" : "지휘관·NPC 공통 / 하루 1회"),
            () => { battle.BuyAttackUpgrade(); return RunProgress.HasPurchasedAttackUpgradeToday; }, () => battle.CanBuyAttackUpgrade);
        Button(page, () => $"강화 총기 해금 · {M6Balance.Snapshot().equipmentCost}골드 · 공격 +{M6Balance.Snapshot().equipmentDamage}\n" +
            (RunEconomy.EquipmentUnlocked ? "해금 완료 · 아래에서 무료 장착 변경" : RunResources.StoredGold < M6Balance.Snapshot().equipmentCost ? "골드 부족" : "1회 구매 시 즉시 장착"),
            RunEconomy.UnlockEquipment, () => !RunEconomy.EquipmentUnlocked && RunResources.StoredGold >= M6Balance.Snapshot().equipmentCost);
        Button(page, () => "기본 총기 장착" + (!RunEconomy.EquipmentEquipped ? " · 현재 장착 중" : " · 무료"),
            () => { RunEconomy.Equip(false); return true; }, () => RunEconomy.EquipmentEquipped);
        Button(page, () => "강화 총기 장착" + (!RunEconomy.EquipmentUnlocked ? " · 해금 필요" : RunEconomy.EquipmentEquipped ? " · 현재 장착 중" : " · 무료"),
            () => { RunEconomy.Equip(true); return RunEconomy.EquipmentEquipped; }, () => RunEconomy.EquipmentUnlocked && !RunEconomy.EquipmentEquipped);
    }

    private void BuildRecords(RectTransform page)
    {
        var report = Label(page, "", 200); report.fontSize = 16;
        refreshers.Add(() =>
        {
            string text = "최근 정산\n" + (RunEconomy.LastSettlement.Length > 0 ? RunEconomy.LastSettlement : "아직 정산 내역이 없습니다.") + "\n\n병력 성장\n";
            foreach (var npc in RunCampNpcs.All)
                if (npc.IsAlive) text += $"{npc.Name} Lv.{npc.Level} EXP {npc.Experience} | 힘{npc.Strength} 체{npc.Vitality} 민{npc.Agility} 끈{npc.Tenacity}" +
                    (npc.Profession == CampNpcProfession.Ranged ? $" · 사거리 {battle.GetNpcFinalRange(npc):F1} · 기본 공격 {(npc.BaseAttackDamage >= 0 ? npc.BaseAttackDamage : npc.DefenseWeapon != null ? npc.DefenseWeapon.Damage : battle.NpcFallbackDamage)} · 기본 공속 {(npc.BaseAttackRate > 0 ? npc.BaseAttackRate : npc.DefenseWeapon != null ? npc.DefenseWeapon.AttacksPerSecond : battle.NpcFallbackAttackRate):F1}/초" : "") + "\n";
            if (report.text != text) report.text = text;
            report.GetComponent<LayoutElement>().preferredHeight = Mathf.Max(200, report.preferredHeight + 12);
        });
        string reloadError = "";
        Button(page, () => "밸런스 설정 다시 읽기", () =>
        {
            return M6Balance.TryReload(out reloadError);
        }, success: () => "밸런스 설정을 적용했습니다.", failure: () => reloadError);
        var settings = Label(page, "", 170); settings.fontSize = 14;
        refreshers.Add(() => settings.text = M6Balance.Status + "\n현재 JSON: " + M6Balance.SourcePath + "\n기본 파일: " + M6Balance.DefaultPath + "\n사용자 파일: " + M6Balance.OverridePath +
            "\n가격: 다음 거래 / 적: 다음 밤 / NPC: 새 모집\n" + battle.EnemyRosterSummary);
    }

    private void Select(int index)
    {
        for (int i = 0; i < pages.Count; i++)
        {
            pages[i].gameObject.SetActive(i == index);
            if (i < tabImages.Count) tabImages[i].color = i == index ? new Color(0.18f, 0.4f, 0.55f) : new Color(0.12f, 0.23f, 0.34f);
        }
        scroll.content = pages[index]; scroll.verticalNormalizedPosition = 1;
    }

    private void OnEnable()
    {
        if (root == null) return;
        Select(initialPage); RefreshContents();
    }

    private void RefreshContents()
    {
        if (battle == null || balance == null) return;
        balance.text = $"목재 {RunResources.StoredWood}   골드 {RunResources.StoredGold}   병력 {RunEconomy.LivingCount}/{EffectivePopulationLimit}\n" +
            $"보유 전리품 일반 {RunEconomy.CommonLoot} · 희귀 {RunEconomy.RareLoot}  |  이번 밤 예정 {RunEconomy.PendingCommonLoot}/{RunEconomy.PendingRareLoot}";
        foreach (var refresh in refreshers) refresh();
    }

    private void LateUpdate()
    {
        if (refreshMenu != null) { refreshMenu(); return; }
        if (root == null || battle == null) return;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            var screen = (RectTransform)canvas.rootCanvas.transform;
            var panel = (RectTransform)transform;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(Mathf.Min(780, Mathf.Max(260, screen.rect.width - 32)), Mathf.Min(720, Mathf.Max(240, screen.rect.height - 32)));
        }
        RefreshContents();
    }

    public static void CreateMenuBar(CampWindowController windows, TMP_FontAsset font, NightDefenseTester battle)
    {
        var canvas = windows.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var bar = Rect("CampMenuButtons", canvas.rootCanvas.transform);
        bar.anchorMin = bar.anchorMax = bar.pivot = Vector2.right;
        bar.anchoredPosition = new Vector2(-18, 18); bar.sizeDelta = new Vector2(460, 46);
        var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6; layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        var ui = bar.gameObject.AddComponent<CampEconomyUI>(); ui.font = font; ui.battle = battle;
        var buttons = new List<Button>();
        string[] names = { "B  건설", "I  판매·모집", "E  강화·타워" };
        Action[] callbacks = { windows.ToggleConstruction, windows.ToggleInventory, windows.ToggleEquipment };
        for (int i = 0; i < names.Length; i++)
        {
            var rect = Rect("Menu" + i, bar);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0.08f, 0.18f, 0.28f, 0.98f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Action callback = callbacks[i]; button.onClick.AddListener(() =>
            {
                if (PlayerCombatStats.ActionsBlocked || GameExitUI.IsWorldPaused || battle.IsChangingScene) return;
                callback();
            });
            var label = ui.Label(rect, names[i], 46); Stretch(label.rectTransform);
            label.alignment = TextAlignmentOptions.Center; buttons.Add(button);
        }
        ui.refreshMenu = () =>
        {
            bool usable = battle.IsInitialized && !battle.IsChangingScene && !PlayerCombatStats.ActionsBlocked && !GameExitUI.IsWorldPaused;
            foreach (var button in buttons) button.interactable = usable;
            var screen = (RectTransform)canvas.rootCanvas.transform;
            bar.sizeDelta = new Vector2(Mathf.Min(460, Mathf.Max(240, screen.rect.width - 36)), 46);
        };
    }
}
