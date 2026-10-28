using System.Collections.Generic;
using UnityEngine;

public static partial class RunProgress
{
    public static void CaptureCampaign(M7CampaignSave save)
    {
        save.day = CurrentDay; save.attackBonus = AttackDamageBonus;
        save.purchasedToday = HasPurchasedAttackUpgradeToday; save.playbackId = nextPlaybackId;
        save.baseHp = savedBaseHp; save.baseMax = savedBaseMaximum;
    }
    public static void RestoreCampaign(M7CampaignSave save)
    {
        CurrentDay = save.day; AttackDamageBonus = save.attackBonus; nextPlaybackId = save.playbackId;
        savedBaseHp = save.baseHp; savedBaseMaximum = save.baseMax;
        if (save.purchasedToday) attackUpgradePurchaseDays.Add(save.day);
    }
}

public static partial class RunEconomy
{
    public static void CaptureCampaign(M7CampaignSave save)
    {
        save.common = CommonLoot; save.rare = RareLoot; save.population = PopulationLimit;
        save.equipmentUnlocked = EquipmentUnlocked; save.equipmentEquipped = EquipmentEquipped;
        save.equipmentBonus = EquipmentBonus; save.reducedFrom = ReducedOrcFromDay; save.reducedThrough = ReducedOrcThroughDay;
        save.settlement = LastSettlement;
    }
    public static void RestoreCampaign(M7CampaignSave save)
    {
        CommonLoot = save.common; RareLoot = save.rare; PopulationLimit = save.population;
        EquipmentUnlocked = save.equipmentUnlocked; EquipmentEquipped = save.equipmentEquipped;
        EquipmentBonus = save.equipmentBonus; ReducedOrcFromDay = save.reducedFrom; ReducedOrcThroughDay = save.reducedThrough;
        LastSettlement = save.settlement;
        if (save.mode == "NightSettlement" || save.mode == "Advance" || save.mode == "Victory") settledDays.Add(save.day);
    }
}

public partial class PlayerCombatStats
{
    public static void CaptureCampaign(M7CampaignSave save)
    { save.playerHp = CurrentHp; save.playerDefense = CurrentDefense; save.stamina = Stamina; save.treatment = TreatmentRemaining; }
    public static void RestoreCampaign(M7CampaignSave save)
    { CurrentHp = save.playerHp; CurrentDefense = save.playerDefense; Stamina = save.stamina; TreatmentRemaining = save.treatment; }
}

public static partial class RunCampNpcs
{
    public static void RestoreCampaign(List<M7NpcSave> units, Dictionary<string, RangedWeaponData> weapons)
    {
        var followers = new List<M7NpcSave>();
        foreach (var s in units)
        {
            var n = new CampNpcData(s.id, (CampNpcProfession)s.profession);
            n.Name = s.name; n.RecruitTemplateId = s.template; n.IsRecruited = s.recruited;
            n.MaximumHp = s.maxHp; n.CurrentHp = s.hp; n.MaximumDefense = s.maxDefense; n.CurrentDefense = s.defense;
            n.Level = s.level; n.Experience = s.experience; n.Strength = s.strength; n.Vitality = s.vitality;
            n.Agility = s.agility; n.Tenacity = s.tenacity; n.GrowthAttackRate = s.growthRate;
            n.BaseAttackDamage = s.damage; n.BaseAttackRange = s.range; n.BaseAttackRate = s.rate;
            n.EngineerBuildMultiplier = s.engineer; n.DefenseWeapon = s.weapon.Length > 0 ? weapons[s.weapon] : null;
            n.CampPosition = s.camp; n.HasCampPosition = true; n.SavedPosition = s.position; n.HasSavedPosition = true;
            n.IdleCenterX = s.position.x; n.SavedFlipX = s.flip; n.IsReturningToCamp = s.returning;
            n.IsHoldingAfterCollapse = s.holding; n.HasAdvanceOrder = s.advancing; n.AdvanceDestination = s.destination;
            n.AssignedConstructionSlotId = s.construction; n.ConstructionAssignmentOrder = s.constructionOrder;
            n.ConstructionWorkplaceIndex = s.workplace; n.AssignedDefensePostId = s.defensePost;
            n.DefenseAssignmentOrder = s.defenseOrder; n.DefenseSeatIndex = s.seat;
            n.NextDefenseAttackTime = Time.time + s.cooldown;
            nextAssignmentOrder = System.Math.Max(nextAssignmentOrder, System.Math.Max(s.constructionOrder, s.defenseOrder));
            npcs.Add(n.Id, n);
            if (s.follower >= 0 && n.IsAlive) followers.Add(s);
        }
        followers.Sort((a, b) => a.follower.CompareTo(b.follower));
        foreach (var s in followers) { npcs[s.id].IsFollowing = true; followingNpcIds.Add(s.id); }
    }

    public static int IssueCombatOrder(Vector3 destination, bool retreat)
    {
        int count = 0;
        foreach (int id in new List<int>(followingNpcIds))
        {
            if (!TryGetNpc(id, out var n) || !n.IsAlive || n.Profession != CampNpcProfession.Ranged) continue;
            Vector3 target = destination + new Vector3((count % 3 - 1) * 0.8f, -(count / 3) * 0.8f);
            var navigation = CampWallNavigation.Instance;
            if (!retreat && (navigation == null || !navigation.MapBounds.Contains(target) ||
                !navigation.TryFindPath(n.SavedPosition, target, navigation.AllyRadius, new List<Vector2>(), true))) continue;
            n.IsFollowing = false; n.IsHoldingAfterCollapse = false;
            n.IsReturningToCamp = retreat; n.HasAdvanceOrder = !retreat;
            n.AdvanceDestination = target;
            followingNpcIds.Remove(id); count++;
        }
        return count;
    }
}

public static partial class RunCampConstruction
{
    public static List<M7BuildingSave> CaptureCampaign()
    {
        var result = new List<M7BuildingSave>();
        var groups = new Dictionary<CampConstructionGroup, int>();
        foreach (var placed in RunCampPlacedBuildings.All)
        {
            if (!TryGetConstruction(placed.SlotId, out var c)) continue;
            if (!groups.ContainsKey(c.Group)) groups.Add(c.Group, placed.SlotId);
            result.Add(new M7BuildingSave
            {
                id = placed.SlotId, kind = (int)placed.Kind, position = placed.Position,
                group = groups[c.Group], totalWork = c.TotalWork, workPerStep = c.WorkPerAPWithOne, progress = c.Progress01,
                wallHp = c.RemainingWallHp, wallMax = c.MaximumWallHp, towerLevel = c.TowerLevel,
                towerInitialized = c.TowerVitalsInitialized, towerHp = c.RemainingTowerHp, towerMax = c.MaximumTowerHp,
                towerDefense = c.RemainingTowerDefense, towerDefenseMax = c.MaximumTowerDefense
            });
        }
        return result;
    }
    public static void RestoreCampaign(List<M7BuildingSave> records, CampWallPlacementController placement)
    {
        var groups = new Dictionary<int, CampConstructionGroup>();
        foreach (var s in records)
        {
            if (!groups.TryGetValue(s.group, out var group))
            { group = new CampConstructionGroup(); groups.Add(s.group, group); constructionOrder.Add(group); }
            var c = new CampConstructionData(s.id, s.totalWork, s.workPerStep, group);
            c.RemainingWallHp = s.wallHp; c.MaximumWallHp = s.wallMax; c.TowerLevel = s.towerLevel;
            c.TowerVitalsInitialized = s.towerInitialized; c.RemainingTowerHp = s.towerHp; c.MaximumTowerHp = s.towerMax;
            c.RemainingTowerDefense = s.towerDefense; c.MaximumTowerDefense = s.towerDefenseMax;
            group.Members.Add(c); group.TotalWork += s.totalWork; constructions.Add(s.id, c);
            RunCampPlacedBuildings.Add(new CampPlacedBuildingData(s.id, placement.DefinitionFor((CampBuildingKind)s.kind), s.position));
        }
        foreach (var s in records) groups[s.group].CompletedWork = groups[s.group].TotalWork * s.progress;
    }
    public static bool CompleteWithTicket(int slotId, int totalWork, int workPerStep)
    {
        if (slotId < 1 || HasStarted(slotId) || totalWork < 1 || workPerStep < 1) return false;
        var group = new CampConstructionGroup { TotalWork = totalWork, CompletedWork = totalWork };
        var c = new CampConstructionData(slotId, totalWork, workPerStep, group) { TowerLevel = 1 };
        group.Members.Add(c); constructionOrder.Add(group); constructions.Add(slotId, c); return true;
    }
}
