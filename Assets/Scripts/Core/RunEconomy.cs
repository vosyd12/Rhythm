using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 정산 1회성·모집·성장을 기존 런 데이터에 연결한다. 씬 참조는 보관하지 않는다.
public static partial class RunEconomy
{
    public static int CommonLoot { get; private set; }
    public static int RareLoot { get; private set; }
    public static int PopulationLimit { get; private set; } = 10;
    public static bool EquipmentUnlocked { get; private set; }
    public static bool EquipmentEquipped { get; private set; }
    public static int EquipmentBonus { get; private set; }
    public static int ReducedOrcThroughDay { get; private set; }
    public static int ReducedOrcFromDay { get; private set; }
    public static string LastSettlement { get; private set; } = "";
    public static int PendingCommonLoot => pendingCommon;
    public static int PendingRareLoot => pendingRare;
    private static readonly HashSet<int> settledDays = new HashSet<int>();
    private static int nightDay, pendingCommon, pendingRare;
    private static bool locked, survived;
    private static M6BalanceData nightSettings;
    public static int LivingCount
    { get { int count = 0; foreach (var npc in RunCampNpcs.All) if (npc.IsAlive) count++; return count; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetForNewRun()
    {
        CommonLoot = RareLoot = ReducedOrcFromDay = ReducedOrcThroughDay = nightDay = pendingCommon = pendingRare = EquipmentBonus = 0;
        PopulationLimit = M6Balance.Snapshot().populationInitial;
        EquipmentUnlocked = EquipmentEquipped = locked = survived = false;
        settledDays.Clear(); LastSettlement = ""; nightSettings = null;
    }

    public static void BeginNight(int day, M6BalanceData settings)
    {
        nightDay = day; nightSettings = settings.Copy(); pendingCommon = pendingRare = 0;
        locked = survived = false;
    }
    public static int RecordKill(float roll)
    {
        if (locked || nightSettings == null || settledDays.Contains(nightDay) || !(roll >= 0 && roll < 1)) return 0;
        if (roll < nightSettings.commonChance) { pendingCommon++; return 1; }
        if (roll < nightSettings.commonChance + nightSettings.rareChance) { pendingRare++; return 2; }
        return 0;
    }
    public static void LockNight(bool won) { locked = true; survived = won; }
    public static bool SettleNight(int day, int kills)
    {
        if (day != nightDay || !locked || !survived || nightSettings == null || !settledDays.Add(day)) return false;
        CommonLoot = ClampInt((long)CommonLoot + pendingCommon);
        RareLoot = ClampInt((long)RareLoot + pendingRare);
        var units = new List<CampNpcData>();
        foreach (var npc in RunCampNpcs.All) if (npc.IsAlive) units.Add(npc);
        units.Sort((a, b) => a.Id.CompareTo(b.Id));
        long pool = (long)Math.Max(0, kills) * nightSettings.xpPerKill;
        var report = new StringBuilder($"일반 {pendingCommon} · 희귀 {pendingRare} 획득 / 총 EXP {pool}");
        for (int i = 0; i < units.Count; i++)
        {
            int share = ClampInt(pool / units.Count + (i < pool % units.Count ? 1 : 0));
            string growth = AddExperience(units[i], share, nightSettings);
            if (growth.Length > 0) report.Append("\n" + growth);
        }
        LastSettlement = report.ToString(); pendingCommon = pendingRare = 0; return true;
    }
    public static int ClampInt(long value) => (int)Math.Max(0L, Math.Min(int.MaxValue, value));

    public static string AddExperience(CampNpcData npc, int amount, M6BalanceData settings)
    {
        if (!npc.IsAlive || amount <= 0) return "";
        npc.Experience = ClampInt((long)npc.Experience + amount);
        var result = new StringBuilder();
        while (npc.Level < settings.levelMaximum && npc.Experience >= (long)npc.Level * settings.xpPerLevel)
        {
            npc.Experience -= npc.Level * settings.xpPerLevel; npc.Level++;
            int stat = UnityEngine.Random.Range(0, 4);
            int gain = UnityEngine.Random.Range(settings.growthMinimum, settings.growthMaximum + 1);
            string name;
            if (stat == 0) { npc.Strength += gain; name = "힘"; }
            else if (stat == 1)
            { npc.Vitality += gain; npc.MaximumHp += gain; npc.CurrentHp += gain; name = "체력"; }
            else if (stat == 2)
            {
                npc.Agility += gain; name = "민첩";
                float remaining = Mathf.Max(0f, settings.agilityMaximum - (npc.GrowthAttackRate - 1f));
                npc.GrowthAttackRate += Mathf.Min(remaining, gain * settings.agilityRate);
            }
            else { npc.Tenacity += gain; npc.MaximumDefense += gain; npc.CurrentDefense += gain; name = "끈기"; }

            result.Append($"{npc.Name} Lv.{npc.Level} {name}+{gain}  ");
        }
        if (npc.Level >= settings.levelMaximum) npc.Experience = 0;
        return result.ToString();
    }

    public static bool SellLoot(bool rare)
    {
        var settings = M6Balance.Snapshot(); int count = rare ? RareLoot : CommonLoot;
        long gold = (long)count * (rare ? settings.rareGold : settings.commonGold);
        if (count <= 0 || gold > int.MaxValue || !RunResources.TryAddGold((int)gold)) return false;
        if (rare) RareLoot = 0; else CommonLoot = 0;
        return true;
    }
    public static bool ExpandPopulation()
    {
        var settings = M6Balance.Snapshot();
        if (PopulationLimit >= settings.populationMaximum || !RunResources.TrySpendGold(settings.populationCost)) return false;
        PopulationLimit = Math.Min(settings.populationMaximum, PopulationLimit + settings.populationStep); return true;
    }
    public static bool UnlockEquipment()
    {
        var settings = M6Balance.Snapshot();
        if (EquipmentUnlocked || !RunResources.TrySpendGold(settings.equipmentCost)) return false;
        EquipmentUnlocked = true; EquipmentBonus = settings.equipmentDamage; EquipmentEquipped = true; return true;
    }
    public static void Equip(bool upgraded) { EquipmentEquipped = upgraded && EquipmentUnlocked; }
    public static void RecordEliteKill(int day, M6BalanceData settings)
    {
        int start = ClampInt((long)day + 1);
        ReducedOrcFromDay = ReducedOrcThroughDay >= day && ReducedOrcFromDay > 0 ? Math.Min(ReducedOrcFromDay, start) : start;
        ReducedOrcThroughDay = Math.Max(ReducedOrcThroughDay, ClampInt((long)day + settings.reducedOrcNights));
    }
    public static float OrcChance(int day, M6BalanceData settings) =>
        day >= ReducedOrcFromDay && day <= ReducedOrcThroughDay ? settings.orcChance * settings.reducedOrcMultiplier : settings.orcChance;

    public static bool TryRecruit(CampNpcProfession profession, Vector3 position, CampNpcData template, out CampNpcData npc)
    {
        npc = null; var settings = M6Balance.Snapshot();
        if (template == null || LivingCount >= PopulationLimit || LivingCount >= settings.populationMaximum || !Enum.IsDefined(typeof(CampNpcProfession), profession)) return false;
        long nextId = 10000;
        foreach (var unit in RunCampNpcs.All) nextId = Math.Max(nextId, (long)unit.Id + 1);
        if (nextId >= int.MaxValue) return false;
        int id = (int)nextId;
        int cost = profession == CampNpcProfession.Unemployed ? settings.unemployedCost :
            profession == CampNpcProfession.Engineer ? settings.engineerCost : settings.rangedCost;
        if (!RunResources.TrySpendGold(cost)) return false;
        npc = RunCampNpcs.RegisterNpc(id, profession, "", template.EngineerBuildMultiplier, template.DefenseWeapon, template.MaximumHp, template.MaximumDefense);
        npc.RecruitTemplateId = template.RecruitTemplateId;
        npc.CampPosition = npc.SavedPosition = position; npc.HasCampPosition = npc.HasSavedPosition = true;
        npc.IdleCenterX = position.x; npc.IsRecruited = true;
        npc.BaseAttackDamage = template.BaseAttackDamage; npc.BaseAttackRange = template.BaseAttackRange; npc.BaseAttackRate = template.BaseAttackRate;
        return true;
    }
}
