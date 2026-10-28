using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RunCampaign
{
    public static M7CampaignSave Data { get; private set; } = new M7CampaignSave();
    public static M7Rules Rules { get; private set; } = new M7Rules();
    public static int Stage => Data.stage;
    public static bool RosterOwned => Data.rosterOwned;
    public static bool SessionStarted { get; private set; }
    public static bool IsTravelling => pending != null;
    public static string Status { get; private set; } = "아직 저장하지 않았습니다.";
    public static string RulesPath => Path.Combine(Application.streamingAssetsPath, "m7-campaign.json");
    private static M7CampaignSave pending;
    private static bool skipCheckpoint;
    private static M7ClearSnapshot pendingClear;
    public static string StageName(int stage) => new[] { "작은 마을", "소도시", "중도시", "대도시" }[Mathf.Clamp(stage, 1, 4) - 1];
    public static int Tickets(CampBuildingKind kind) => kind == CampBuildingKind.Wall ? Data.wallTickets : Data.towerTickets;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    { Data = new M7CampaignSave(); Rules = new M7Rules(); pending = null; pendingClear = null; SessionStarted = skipCheckpoint = false; Status = "아직 저장하지 않았습니다."; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Subscribe()
    { SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneLoaded += SceneLoaded; ReloadRules(out _); }

    public static bool ReloadRules(out string error)
    {
        try
        {
            if (new FileInfo(RulesPath).Length > 32768) { error = "M7 설정은 32KB 이하여야 합니다."; return false; }
            string json = File.ReadAllText(RulesPath);
            foreach (var key in new[] { "version", "companionLimit", "startingWorkers", "populationInitial", "advanceWood", "advanceGold", "legacyGold", "buildingRefund", "stageEnemyMultipliers" })
                if (!json.Contains("\"" + key + "\"")) { error = "M7 설정 누락: " + key; return false; }
            var candidate = JsonUtility.FromJson<M7Rules>(json);
            if (candidate == null || !candidate.Validate()) { error = "M7 설정 범위·인원·비율이 유효하지 않습니다."; return false; }
            Rules = candidate; error = ""; return true;
        }
        catch (Exception ex) { error = "M7 설정 읽기 실패: " + ex.Message; return false; }
    }

    private static Dictionary<string, RangedWeaponData> Weapons()
    {
        var result = new Dictionary<string, RangedWeaponData>();
        foreach (var weapon in Resources.FindObjectsOfTypeAll<RangedWeaponData>()) result[weapon.name] = weapon;
        return result;
    }
    private static bool ValidateAssets(M7CampaignSave candidate, out string error)
    {
        if (!candidate.Validate(out error)) return false;
        var placement = CampWallPlacementController.Instance;
        if (placement == null || placement.DefinitionFor(CampBuildingKind.Wall) == null || placement.DefinitionFor(CampBuildingKind.Tower) == null)
        { error = "캠페인 건물 정의가 연결되지 않았습니다."; return false; }
        var weapons = Weapons();
        var templates = UnityEngine.Object.FindObjectsByType<CampNpcIdleView>();
        foreach (var n in candidate.npcs)
        {
            if (n.weapon.Length > 0 && !weapons.ContainsKey(n.weapon)) { error = "저장된 무기를 찾지 못했습니다: " + n.weapon; return false; }
            bool found = false;
            foreach (var view in templates) if (view.RecruitTemplateId == (n.template > 0 ? n.template : n.id)) { found = true; break; }
            if (!found) { error = "저장된 NPC 원형을 찾지 못했습니다: " + n.name; return false; }
        }
        return true;
    }

    private static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var battle = UnityEngine.Object.FindAnyObjectByType<NightDefenseTester>();
        if (battle == null) return;
        if (pending != null)
        {
            var candidate = pending; pending = null;
            ApplyLive(candidate); skipCheckpoint = candidate.rosterOwned || candidate.npcs.Count > 0;
        }
        else if (Data.mode == "NightSettlement" && RunProgress.CurrentDay > Data.day) Data.mode = "Playing";
        M7StageWorld.Build(Stage, battle);
    }
    private static void ApplyLive(M7CampaignSave save)
    {
        RunProgress.ResetForNewRun();
        RunProgress.RestoreCampaign(save); RunEconomy.RestoreCampaign(save);
        RunResources.TryAddWood(save.wood); RunResources.TryAddGold(save.gold);
        PlayerCombatStats.RestoreCampaign(save);
        RunCampConstruction.RestoreCampaign(save.buildings, CampWallPlacementController.Instance);
        RunCampNpcs.RestoreCampaign(save.npcs, Weapons());
        Data = save; SessionStarted = true; pendingClear = null;
    }
    public static M7CampaignSave Capture()
    {
        var save = Data.Copy();
        RunProgress.CaptureCampaign(save); RunEconomy.CaptureCampaign(save); PlayerCombatStats.CaptureCampaign(save);
        save.wood = RunResources.StoredWood; save.gold = RunResources.StoredGold;
        save.npcs.Clear();
        foreach (var n in RunCampNpcs.All)
            if (n.IsAlive || (!n.IsRecruited && !Data.rosterOwned)) save.npcs.Add(M7NpcSave.Capture(n));
        save.buildings = RunCampConstruction.CaptureCampaign();
        // 파괴된 건물에 대한 이미 해제된 예약은 저장하지 않는다.
        var ids = new HashSet<int>(); foreach (var b in save.buildings) ids.Add(b.id);
        foreach (var n in save.npcs)
        { if (!ids.Contains(n.construction)) n.construction = 0; if (!ids.Contains(n.defensePost)) n.defensePost = 0; }
        return save;
    }
    public static bool Checkpoint()
    {
        if (!SessionStarted || IsTravelling) return false;
        var save = Capture();
        if (!M7SaveFile.TryWrite(save, out string error)) { Status = error; return false; }
        Data = save; Status = "자동 저장 완료: " + Stage + "지역 " + save.day + "일차 · " + DateTime.Now.ToString("HH:mm:ss"); return true;
    }
    public static void SceneReady()
    {
        if (skipCheckpoint) { skipCheckpoint = false; return; }
        if (!SessionStarted && M7SaveFile.Exists) return;
        SessionStarted = true;
        if (Data.mode == "Playing" && (Data.savedAt.Length == 0 || Data.day != RunProgress.CurrentDay ||
            (!Data.rosterOwned && Data.npcs.Count == 0))) Checkpoint();
    }
    public static bool Continue(string scene, out string error)
    {
        if (!M7SaveFile.TryRead(out var save, out error)) { Status = error; return false; }
        string note = error;
        if (!Travel(save, scene, false, out error)) return false;
        Status = note.Length > 0 ? note : "저장된 체크포인트를 불러옵니다."; return true;
    }
    public static bool NewCampaign(string scene, out string error) => Travel(new M7CampaignSave(), scene, true, out error);

    private static bool Travel(M7CampaignSave candidate, string scene, bool persist, out string error)
    {
        if (IsTravelling) { error = "지역 이동 중입니다."; return false; }
        if (!Application.CanStreamedLevelBeLoaded(scene)) { error = "지역 씬을 불러올 수 없습니다: " + scene; return false; }
        if (!ValidateAssets(candidate, out error)) return false;
        if (persist && !M7SaveFile.TryWrite(candidate, out error)) { Status = error; return false; }
        if (persist) Status = "지역 이동 저장 완료: " + candidate.stage + "지역 " + candidate.day + "일차";
        pending = candidate;
        try
        {
            if (SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single) == null) throw new InvalidOperationException("씬 이동 요청 실패");
            error = ""; return true;
        }
        catch (Exception ex)
        {
            pending = null; error = "지역 이동 실패: " + ex.Message; Status = error;
            if (SessionStarted) M7SaveFile.TryWrite(Capture(), out _);
            return false;
        }
    }

    public static void RecordClear()
    {
        if (pendingClear != null || Data.mode != "Playing") return;
        int revision = 1;
        foreach (var s in Data.clears) if (s.stage == Stage) revision = s.revision == int.MaxValue ? int.MaxValue : s.revision + 1;
        pendingClear = new M7ClearSnapshot { stage = Stage, revision = revision };
        foreach (var b in RunCampPlacedBuildings.All)
        {
            if (!RunCampConstruction.TryGetConstruction(b.SlotId, out var c) || !c.IsCompleted) continue;
            // 같은 프레임에 막 완공해 아직 뷰가 HP를 연결하지 않은 건물도 포함한다.
            bool alive = b.Kind == CampBuildingKind.Wall ? c.MaximumWallHp == 0 || c.RemainingWallHp > 0 :
                !c.TowerVitalsInitialized || c.RemainingTowerHp > 0;
            if (!alive) continue;
            if (b.Kind == CampBuildingKind.Wall) pendingClear.walls++; else pendingClear.towers++;
        }
    }
    public static void CompleteStage()
    {
        if (Data.mode != "Playing") return;
        RecordClear();
        Data.clears.RemoveAll(s => s.stage == Stage); Data.clears.Add(pendingClear); pendingClear = null;
        Data.mode = Stage == 4 ? "Victory" : "Advance";
        Checkpoint();
    }
    public static void CompleteNightSettlement()
    {
        if (Data.mode != "Playing") return;
        Data.mode = "NightSettlement"; Checkpoint();
    }
    public static void Defeat()
    {
        if (Data.mode != "Playing") return;
        Data.mode = Stage > 1 ? "Retreat" : "Defeat"; Checkpoint();
    }

    public static int TransferAmount(int amount, float ratio) => (int)decimal.Floor(amount * (decimal)ratio);
    private static void ResetRegion(M7CampaignSave save)
    {
        save.day = 1; save.baseHp = -1; save.baseMax = 0; save.buildings.Clear();
        save.wallTickets = save.towerTickets = save.reducedFrom = save.reducedThrough = 0;
        save.population = Rules.populationInitial; save.purchasedToday = false; save.rosterOwned = true;
        save.settlement = "";
        save.playerHp = PlayerCombatStats.MaximumHp; save.playerDefense = PlayerCombatStats.MaximumDefense;
        save.stamina = 100; save.treatment = 0; save.mode = "Playing";
    }
    private static void PositionRoster(M7CampaignSave save)
    {
        int i = 0;
        foreach (var n in save.npcs) n.ClearOrders(new Vector3(-3 + (i % 5) * 1.2f, -3 - (i++ / 5) * 1.2f, 0));
    }
    private static bool AddWorkers(M7CampaignSave save, out string error)
    {
        var battle = UnityEngine.Object.FindAnyObjectByType<NightDefenseTester>();
        if (battle == null || !CampNpcIdleView.TryChooseRecruitTemplate(CampNpcProfession.Unemployed, out var view))
        { error = "기본 작업자 원형이 없습니다."; return false; }
        int id = 10000;
        foreach (var n in save.npcs) id = Math.Max(id, n.id + 1);
        if (id > int.MaxValue - Rules.startingWorkers) { error = "대원 ID 한도입니다."; return false; }
        for (int i = 0; i < Rules.startingWorkers; i++)
        {
            var n = M7NpcSave.Capture(view.CreateRecruitPrototype(battle));
            n.id = id++; n.profession = (int)CampNpcProfession.Unemployed; n.name = M6Balance.PickNpcName("신병");
            n.recruited = true; save.npcs.Add(n);
        }
        error = ""; return true;
    }
    public static bool Advance(ICollection<int> selected, string scene, out string error)
    {
        if (Data.mode != "Advance" || Stage >= 4 || selected == null || selected.Count > Rules.companionLimit)
        { error = "동행 인원 또는 진격 상태를 확인하세요."; return false; }
        var save = Capture(); var ids = new HashSet<int>(selected);
        if (ids.Count != selected.Count) { error = "동행 대원이 중복되었습니다."; return false; }
        foreach (int id in ids)
            if (!save.npcs.Exists(n => n.id == id && n.hp > 0)) { error = "생존 대원만 선택할 수 있습니다."; return false; }
        save.npcs.RemoveAll(n => !ids.Contains(n.id));
        int nextId = 10000;
        foreach (var n in save.npcs) nextId = Math.Max(nextId, n.id + 1);
        if (nextId > int.MaxValue - save.npcs.Count - Rules.startingWorkers)
        { error = "대원 ID 한도입니다."; return false; }
        foreach (var n in save.npcs)
        {
            if (n.template == 0) n.template = n.id;
            if (n.id < 10000) n.id = nextId++;
        }
        save.stage++;
        save.wood = TransferAmount(save.wood, Rules.advanceWood); save.gold = TransferAmount(save.gold, Rules.advanceGold);
        ResetRegion(save);
        if (!AddWorkers(save, out error)) return false;
        PositionRoster(save); return Travel(save, scene, true, out error);
    }
    public static M7ClearSnapshot RetreatSnapshot()
    { return Data.clears.Find(s => s.stage == Stage - 1); }
    public static int Refund(M7ClearSnapshot snapshot, bool wallGold, bool towerGold)
    {
        if (snapshot == null || snapshot.claimed) return 0;
        var placement = CampWallPlacementController.Instance;
        long value = (wallGold ? (long)snapshot.walls * placement.WallWoodCost : 0) +
            (towerGold ? (long)snapshot.towers * placement.TowerWoodCost : 0);
        return RunEconomy.ClampInt((long)decimal.Floor(value * (decimal)Rules.buildingRefund));
    }
    public static bool Retreat(bool wallGold, bool towerGold, string scene, out string error)
    {
        if (Data.mode != "Retreat" || Stage <= 1 || Data.commander == int.MaxValue) { error = "후퇴할 수 없는 상태입니다."; return false; }
        var save = Capture(); var snapshot = save.clears.Find(s => s.stage == Stage - 1);
        int refund = Refund(snapshot, wallGold, towerGold);
        save.stage--; save.commander++; save.npcs.Clear(); save.gold = RunEconomy.ClampInt((long)TransferAmount(save.gold, Rules.legacyGold) + refund);
        save.wood = save.common = save.rare = save.attackBonus = save.equipmentBonus = 0;
        save.equipmentUnlocked = save.equipmentEquipped = false;
        ResetRegion(save);
        if (snapshot != null && !snapshot.claimed)
        {
            if (!wallGold) save.wallTickets = snapshot.walls;
            if (!towerGold) save.towerTickets = snapshot.towers;
            snapshot.claimed = true;
        }
        if (!AddWorkers(save, out error)) return false;
        PositionRoster(save); return Travel(save, scene, true, out error);
    }
    public static bool ConsumeTicket(CampBuildingKind kind)
    {
        if (Tickets(kind) <= 0 || Data.mode != "Playing") return false;
        if (kind == CampBuildingKind.Wall) Data.wallTickets--; else Data.towerTickets--; return true;
    }
}
