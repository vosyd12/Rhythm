using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class M7Rules
{
    public int version = 1, companionLimit = 5, startingWorkers = 2, populationInitial = 10;
    public float advanceWood = 0.5f, advanceGold = 0.5f, legacyGold = 0.3f, buildingRefund = 0.5f;
    public float[] stageEnemyMultipliers = { 1f, 1.2f, 1.4f, 1.6f };
    public bool Validate()
    {
        if (version != 1 || companionLimit < 0 || companionLimit > 30 || startingWorkers < 1 ||
            startingWorkers > 10 || populationInitial < companionLimit + startingWorkers || populationInitial > 100 ||
            stageEnemyMultipliers == null || stageEnemyMultipliers.Length != 4) return false;
        foreach (float n in new[] { advanceWood, advanceGold, legacyGold, buildingRefund })
            if (float.IsNaN(n) || n < 0 || n > 1) return false;
        foreach (float n in stageEnemyMultipliers) if (float.IsNaN(n) || n < 0.1f || n > 10) return false;
        return true;
    }
}

[Serializable]
public sealed class M7NpcSave
{
    public int id, profession, template, hp, maxHp, defense, maxDefense, level, experience;
    public int strength, vitality, agility, tenacity, damage, construction, defensePost, seat;
    public long constructionOrder, defenseOrder;
    public int workplace, follower = -1;
    public float engineer, growthRate, range, rate, cooldown;
    public string name, weapon;
    public bool recruited, flip, returning, holding, advancing;
    public Vector3 position, camp, destination;

    public static M7NpcSave Capture(CampNpcData n) => new M7NpcSave
    {
        id = n.Id, profession = (int)n.Profession, template = n.RecruitTemplateId, name = n.Name,
        hp = n.CurrentHp, maxHp = n.MaximumHp, defense = n.CurrentDefense, maxDefense = n.MaximumDefense,
        level = n.Level, experience = n.Experience, strength = n.Strength, vitality = n.Vitality,
        agility = n.Agility, tenacity = n.Tenacity, damage = n.BaseAttackDamage, range = n.BaseAttackRange,
        rate = n.BaseAttackRate, growthRate = n.GrowthAttackRate, engineer = n.EngineerBuildMultiplier,
        weapon = n.DefenseWeapon != null ? n.DefenseWeapon.name : "", recruited = n.IsRecruited,
        position = n.SavedPosition, camp = n.CampPosition, flip = n.SavedFlipX,
        construction = n.AssignedConstructionSlotId, constructionOrder = n.ConstructionAssignmentOrder,
        workplace = n.ConstructionWorkplaceIndex, defensePost = n.AssignedDefensePostId,
        defenseOrder = n.DefenseAssignmentOrder, seat = n.DefenseSeatIndex,
        follower = RunCampNpcs.GetFollowerIndex(n.Id), returning = n.IsReturningToCamp,
        holding = n.IsHoldingAfterCollapse, advancing = n.HasAdvanceOrder, destination = n.AdvanceDestination,
        cooldown = Mathf.Max(0, n.NextDefenseAttackTime - Time.time)
    };

    public void ClearOrders(Vector3 start)
    {
        position = camp = start; hp = maxHp; defense = maxDefense; recruited = true;
        construction = defensePost = 0; constructionOrder = defenseOrder = 0; workplace = seat = follower = -1;
        cooldown = 0; returning = holding = advancing = false;
    }
}

[Serializable]
public sealed class M7BuildingSave
{
    public int id, kind, group, totalWork, workPerStep;
    public Vector3 position;
    public float progress;
    public int wallHp, wallMax, towerLevel, towerHp, towerMax, towerDefense, towerDefenseMax;
    public bool towerInitialized;
}

[Serializable]
public sealed class M7ClearSnapshot
{
    public int stage, revision, walls, towers;
    public bool claimed;
}

[Serializable]
public sealed class M7CampaignSave
{
    public int version = 1, stage = 1, commander = 1, day = 1, wood, gold, common, rare, population = 10;
    public int attackBonus, equipmentBonus, reducedFrom, reducedThrough, baseHp = -1, baseMax;
    public int wallTickets, towerTickets, playbackId;
    public bool rosterOwned, equipmentUnlocked, equipmentEquipped, purchasedToday;
    public int playerHp = 20, playerDefense = 5;
    public float treatment, stamina = 100;
    public string mode = "Playing", savedAt = "", settlement = "";
    public List<M7NpcSave> npcs = new List<M7NpcSave>();
    public List<M7BuildingSave> buildings = new List<M7BuildingSave>();
    public List<M7ClearSnapshot> clears = new List<M7ClearSnapshot>();
    public M7CampaignSave Copy() => JsonUtility.FromJson<M7CampaignSave>(JsonUtility.ToJson(this));

    public bool Validate(out string error)
    {
        error = "저장 데이터의 버전·범위·참조가 유효하지 않습니다.";
        if (version != 1 || stage < 1 || stage > 4 || commander < 1 || day < 1 ||
            wood < 0 || gold < 0 || common < 0 || rare < 0 || population < 1 || population > 100 ||
            attackBonus < 0 || attackBonus > 1000000 || equipmentBonus < 0 || wallTickets < 0 || towerTickets < 0 ||
            wallTickets > 5000 || towerTickets > 5000 || playbackId < 0 || baseHp < -1 || baseMax < 0 || baseHp > baseMax ||
            playerHp < 0 || playerHp > PlayerCombatStats.MaximumHp || playerDefense < 0 || playerDefense > PlayerCombatStats.MaximumDefense ||
            !Finite(treatment, 0, 15) || !Finite(stamina, 0, 100) ||
            (mode != "Playing" && mode != "NightSettlement" && mode != "Advance" && mode != "Retreat" && mode != "Victory" && mode != "Defeat") ||
            (mode == "Advance" && stage == 4) || (mode == "Retreat" && stage == 1) ||
            (mode == "Victory" && stage != 4) || (mode == "Defeat" && stage != 1) ||
            settlement == null || settlement.Length > 20000 || savedAt == null || savedAt.Length > 100 ||
            (equipmentEquipped && !equipmentUnlocked) || (playerHp == 0 && treatment <= 0) || reducedFrom < 0 || reducedThrough < reducedFrom ||
            npcs == null || buildings == null || clears == null || npcs.Count > 200 || buildings.Count > 5000 || clears.Count > 4)
            return false;
        var ids = new HashSet<int>();
        foreach (var n in npcs)
        {
            if (n == null || n.id < 1 || !ids.Add(n.id) || n.profession < 0 || n.profession > 2 || n.template < 0 ||
                string.IsNullOrWhiteSpace(n.name) || n.name.Length > 40 || n.weapon == null || n.weapon.Length > 100 ||
                n.maxHp < 1 || n.maxHp > 1000000 || n.hp < 0 || n.hp > n.maxHp || n.maxDefense < 0 || n.maxDefense > 1000000 ||
                n.defense < 0 || n.defense > n.maxDefense || n.level < 1 || n.level > 100 || n.experience < 0 ||
                n.strength < 0 || n.strength > 1000000 || n.vitality < 0 || n.vitality > 1000000 ||
                n.agility < 0 || n.agility > 1000000 || n.tenacity < 0 || n.tenacity > 1000000 || n.damage < -1 || n.damage > 1000000 ||
                !Finite(n.engineer, 1, 100) || !Finite(n.growthRate, 1, 100) || !Finite(n.range, -1, 1000000) ||
                !Finite(n.rate, -1, 1000000) || !Finite(n.cooldown, 0, 1000000) || !Position(n.position) || !Position(n.camp) ||
                !Position(n.destination) || n.follower < -1 || n.follower > 99 || n.construction < 0 || n.defensePost < 0 ||
                n.constructionOrder < 0 || n.defenseOrder < 0 || n.workplace < -1 || n.workplace > 100 || n.seat < -1 || n.seat > 2) return false;
        }
        ids.Clear();
        var byId = new Dictionary<int, M7BuildingSave>();
        foreach (var b in buildings)
        {
            if (b == null || b.id < 1 || !ids.Add(b.id) || b.kind < 0 || b.kind > 1 || b.group < 1 ||
                !Position(b.position) || b.totalWork < 1 || b.totalWork > 1000000 || b.workPerStep < 1 ||
                !Finite(b.progress, 0, 1) || b.wallHp < 0 || b.wallMax < b.wallHp || b.towerLevel < 0 || b.towerLevel > 9 ||
                b.towerHp < 0 || b.towerMax < b.towerHp || b.towerDefense < 0 || b.towerDefenseMax < b.towerDefense) return false;
            byId.Add(b.id, b);
        }
        foreach (var b in buildings)
            if (!byId.TryGetValue(b.group, out var leader) || leader.group != b.group || leader.progress != b.progress) return false;
        var followers = new HashSet<int>();
        foreach (var n in npcs)
        {
            if ((n.construction != 0 && !ids.Contains(n.construction)) || (n.defensePost != 0 && !ids.Contains(n.defensePost)) ||
                (n.follower >= 0 && !followers.Add(n.follower))) return false;
        }
        ids.Clear();
        foreach (var s in clears)
            if (s == null || s.stage < 1 || s.stage > 4 || !ids.Add(s.stage) || s.revision < 1 || s.walls < 0 ||
                s.towers < 0 || s.walls > 5000 || s.towers > 5000) return false;
        if ((mode == "Advance" || mode == "Victory") && !clears.Exists(s => s.stage == stage)) return false;
        error = ""; return true;
    }
    private static bool Finite(float n, float min, float max) => !float.IsNaN(n) && !float.IsInfinity(n) && n >= min && n <= max;
    private static bool Position(Vector3 p) => Finite(p.x, -10000, 10000) && Finite(p.y, -10000, 10000) && Finite(p.z, -10000, 10000);
}

// 밸런스 JSON과 분리한 PC 로컬 1슬롯. 후보 검증 후 원자적으로 교체한다.
public static class M7SaveFile
{
    public static string DirectoryPath
    {
        get
        {
#if UNITY_EDITOR
            if (Application.isBatchMode) return Path.GetFullPath("M7TestSave");
#endif
            return Application.persistentDataPath;
        }
    }
    public static string PathName => Path.Combine(DirectoryPath, "campaign-save.json");
    public static string BackupPath => PathName + ".bak";
    public static bool Exists => File.Exists(PathName) || File.Exists(BackupPath);
    public static bool ReadAt(string path, out M7CampaignSave data, out string error)
    {
        data = null;
        try
        {
            if (!File.Exists(path)) { error = "저장 파일이 없습니다."; return false; }
            long length = new FileInfo(path).Length;
            if (length < 2 || length > 4 * 1024 * 1024) { error = "저장 파일 크기 오류입니다."; return false; }
            string json = File.ReadAllText(path);
            foreach (var field in typeof(M7CampaignSave).GetFields())
                if (!json.Contains("\"" + field.Name + "\"")) { error = "저장 필드 누락: " + field.Name; return false; }
            data = JsonUtility.FromJson<M7CampaignSave>(json);
            if (data == null) { error = "빈 저장 데이터입니다."; return false; }
            return data.Validate(out error);
        }
        catch (Exception ex) { error = "저장 파일 읽기 실패: " + ex.Message; return false; }
    }
    public static bool TryRead(out M7CampaignSave data, out string error)
    {
        if (ReadAt(PathName, out data, out error)) return true;
        string original = error;
        if (ReadAt(BackupPath, out data, out error)) { error = "이전 정상 백업에서 복원합니다. " + original; return true; }
        error = original + " 백업: " + error; return false;
    }
    public static bool TryWrite(M7CampaignSave data, out string error)
    {
        if (data == null) { error = "저장할 데이터가 없습니다."; return false; }
        if (!data.Validate(out error)) return false;
        string temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            data.savedAt = DateTime.UtcNow.ToString("O");
            string json = JsonUtility.ToJson(data, true);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024) { error = "저장 용량이 4MB를 초과했습니다."; return false; }
            Directory.CreateDirectory(DirectoryPath);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            { writer.Write(json); writer.Flush(); stream.Flush(true); }
            if (File.Exists(PathName))
                File.Replace(temporary, PathName, ReadAt(PathName, out _, out _) ? BackupPath : null);
            else File.Move(temporary, PathName);
            error = ""; return true;
        }
        catch (Exception ex) { error = "저장 실패: " + ex.Message; return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
