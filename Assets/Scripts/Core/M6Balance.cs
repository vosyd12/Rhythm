using System;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class M6NightSystemData
{
    public float dayDurationSeconds = 120, warningDurationSeconds = 15, nightDurationSeconds = 90;
    public int riftMaximumHp = 30;
    public float riftDetectionRadius = 6, riftBuildingExclusionRadius = 2, riftMinimumCampDistance = 30, escapeDurationSeconds = 2.5f;
    public float spawnMinimumDistance = 12, spawnMaximumDistance = 16, enemyStopDistance = 1;
    public int maxBaseHp = 50, attackDamage = 3;
    public float attackRange = 4, attackIntervalSeconds = 1, enemyHitRadius = 0.35f, constructionWorkStepSeconds = 0.5f;
    public bool enableAdaptiveDifficulty = true;
    public int debugWoodTarget = 500;
    public M6NightSystemData Copy() => (M6NightSystemData)MemberwiseClone();

    public bool Validate(out string error)
    {
        foreach (var field in GetType().GetFields())
        {
            if (field.FieldType == typeof(bool)) continue;
            double value = Convert.ToDouble(field.GetValue(this));
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1000000)
            { error = "nightSystem." + field.Name + ": 0~1000000 범위의 수치가 필요합니다."; return false; }
        }
        if (dayDurationSeconds <= 0 || warningDurationSeconds <= 0 || nightDurationSeconds <= 0 || riftMaximumHp < 1 ||
            riftDetectionRadius <= 0 || riftBuildingExclusionRadius <= 0 || escapeDurationSeconds < 2 || escapeDurationSeconds > 3 ||
            spawnMinimumDistance <= enemyStopDistance || spawnMaximumDistance < spawnMinimumDistance || enemyStopDistance <= 0 ||
            maxBaseHp < 1 || attackDamage < 1 || attackIntervalSeconds <= 0 || enemyHitRadius <= 0 || constructionWorkStepSeconds <= 0)
        { error = "nightSystem의 체력·시간·공격·거리 설정 조합이 유효하지 않습니다."; return false; }
        error = ""; return true;
    }
}

// 플레이 저장과 분리된 밸런스 값. 읽은 후보 전체를 검증한 뒤 교체한다.
[Serializable]
public sealed class M6BalanceData
{
    public int version = -1;
    public M6NightSystemData nightSystem;
    public ForestGatheringSettings gathering;
    public ForestGatheringSettings GatheringSettings => gathering ?? new ForestGatheringSettings();
    public string[] NPCName;
    public int woodGold = -1, commonGold = -1, rareGold = -1;
    public float commonChance = -1, rareChance = -1;
    public int unemployedCost = -1, engineerCost = -1, rangedCost = -1;
    public int populationInitial = -1, populationStep = -1, populationMaximum = -1, populationCost = -1;
    public int attackCost = -1, attackMaximum = -1, equipmentCost = -1, equipmentDamage = -1;
    public int towerMaximum = -1, towerWoodPerLevel = -1;
    public int xpPerKill = -1, levelMaximum = -1, xpPerLevel = -1;
    public int growthMinimum = -1, growthMaximum = -1;
    public float agilityRate = -1, agilityMaximum = -1;
    public int npcHp = -1, npcDefense = -1;
    public float npcRange = -1, npcAttackRate = -1;
    public int npcDamage = -1;
    public int enemyCount = -1, enemyHp = -1, enemyDamage = -1;
    public float enemySpeed = -1, spawnInterval = -1, enemyAttackInterval = -1;
    public int countPerDay = -1, hpPerDay = -1, defenseEveryDays = -1, defenseMaximum = -1, damageEveryDays = -1;
    public float lowHit = -1, highHit = -1, ddaFraction = -1;
    public int mediumDay = -1, rangedDay = -1;
    public float mediumChance = -1, rangedChance = -1, mediumRange = -1, rangedRange = -1;
    public int eliteEveryDays = -1, eliteHpMultiplier = -1, eliteDamageMultiplier = -1;
    public float eliteCountMultiplier = -1, orcChance = -1, reducedOrcMultiplier = -1;
    public int reducedOrcNights = -1;

    public M6BalanceData Copy()
    {
        var copy = (M6BalanceData)MemberwiseClone();
        copy.nightSystem = nightSystem == null ? null : nightSystem.Copy();
        copy.gathering = gathering == null ? null : gathering.Copy();
        copy.NPCName = NPCName == null ? null : (string[])NPCName.Clone();
        return copy;
    }
    public static M6BalanceData Defaults() => new M6BalanceData
    {
        version = 1, woodGold = 1, commonGold = 1, rareGold = 5, commonChance = 0.7f, rareChance = 0.1f,
        unemployedCost = 10, engineerCost = 25, rangedCost = 25,
        populationInitial = 10, populationStep = 5, populationMaximum = 30, populationCost = 20,
        attackCost = 20, attackMaximum = 10, equipmentCost = 30, equipmentDamage = 1,
        towerMaximum = 9, towerWoodPerLevel = 10, xpPerKill = 5, levelMaximum = 10, xpPerLevel = 20,
        growthMinimum = 1, growthMaximum = 2, agilityRate = 0.02f, agilityMaximum = 0.4f,
        npcHp = 10, npcDefense = 3, npcRange = 15, npcAttackRate = 3, npcDamage = 1,
        enemyCount = 5, enemyHp = 5, enemyDamage = 1, enemySpeed = 2, spawnInterval = 2, enemyAttackInterval = 1.2f,
        countPerDay = 2, hpPerDay = 1, defenseEveryDays = 3, defenseMaximum = 5, damageEveryDays = 3,
        lowHit = 0.6f, highHit = 0.9f, ddaFraction = 0.1f,
        mediumDay = 5, rangedDay = 10, mediumChance = 0.2f, rangedChance = 0.2f, mediumRange = 4, rangedRange = 8,
        eliteEveryDays = 5, eliteHpMultiplier = 3, eliteDamageMultiplier = 2, eliteCountMultiplier = 1.5f,
        orcChance = 0.5f, reducedOrcMultiplier = 0.5f, reducedOrcNights = 3
    };

    public bool Validate(out string error)
    {
        // 누락된 필드는 -1로 남으므로 0을 허용하는 값도 빠짐없이 검사한다.
        foreach (var field in typeof(M6BalanceData).GetFields())
        {
            if (field.FieldType != typeof(int) && field.FieldType != typeof(float)) continue;
            object value = field.GetValue(this);
            double number = value is int i ? i : (double)(float)value;
            if (double.IsNaN(number) || double.IsInfinity(number) || number < 0 || number > 1000000)
            { error = field.Name + ": 누락 또는 범위 오류(0~1000000)"; return false; }
        }
        if (version != 1) { error = "version은 1이어야 합니다."; return false; }
        string[] positive = { "woodGold", "commonGold", "rareGold", "unemployedCost", "engineerCost", "rangedCost",
            "populationInitial", "populationStep", "populationMaximum", "populationCost", "attackCost", "attackMaximum",
            "equipmentCost", "towerMaximum", "towerWoodPerLevel", "levelMaximum", "xpPerLevel", "growthMinimum",
            "growthMaximum", "npcHp", "npcRange", "npcAttackRate", "npcDamage", "enemyCount", "enemyHp", "enemyDamage",
            "enemySpeed", "spawnInterval", "enemyAttackInterval", "defenseEveryDays", "damageEveryDays", "mediumDay",
            "rangedDay", "mediumRange", "rangedRange", "eliteEveryDays", "eliteHpMultiplier", "eliteDamageMultiplier", "eliteCountMultiplier" };
        foreach (string name in positive)
            if (Convert.ToDouble(typeof(M6BalanceData).GetField(name).GetValue(this)) <= 0)
            { error = name + ": 0보다 커야 합니다."; return false; }
        if (commonChance + rareChance > 1 || mediumChance + rangedChance > 1 || orcChance > 1 ||
            reducedOrcMultiplier > 1 || lowHit >= highHit || highHit > 1 || ddaFraction > 0.5f ||
            agilityMaximum > 1 || agilityRate > 1 || populationInitial > populationMaximum || populationMaximum > 100 ||
            towerMaximum > 9 || levelMaximum > 100 || growthMinimum > growthMaximum || growthMaximum > 100 ||
            mediumDay > rangedDay || eliteHpMultiplier > 100 || eliteDamageMultiplier > 100 || eliteCountMultiplier > 10)
        { error = "확률·상하한·레벨·성장·병과 순서의 조합이 유효하지 않습니다."; return false; }
        if (nightSystem != null && !nightSystem.Validate(out error)) return false;
        if (gathering != null && !gathering.Validate()) { error = "gathering의 채집 시간·준비 시간·목재 생산 상한이 유효하지 않습니다."; return false; }
        if (NPCName != null)
        {
            if (NPCName.Length < 1 || NPCName.Length > 1024) { error = "NPCName은 1~1024개의 이름 목록이어야 합니다."; return false; }
            foreach (string name in NPCName)
                if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40)
                { error = "NPCName에는 1~40자의 이름을 입력하세요."; return false; }
        }
        error = ""; return true;
    }
}

public static class M6Balance
{
    private static M6BalanceData active = M6BalanceData.Defaults();
    public static M6BalanceData Snapshot() => active.Copy();
    public static int Revision { get; private set; }
    public static string Status { get; private set; } = "기본 설정";
    public static string OverridePath => Path.Combine(Application.persistentDataPath, "m6-balance.json");
    public static string DefaultPath => Path.Combine(Application.streamingAssetsPath, "m6-balance.json");
    public static string SourcePath => File.Exists(OverridePath) ? OverridePath : DefaultPath;
    public static string PickNpcName(string fallback)
    {
        string[] names = active.NPCName;
        return names != null && names.Length > 0 ? names[UnityEngine.Random.Range(0, names.Length)].Trim() : fallback;
    }
    public static bool TrySaveInspector(M6BalanceData candidate, out string error)
    {
        if (candidate == null) { error = "Inspector 설정이 없습니다."; return false; }
        if (!candidate.Validate(out error)) return false;
#if UNITY_EDITOR
        string path = SourcePath;
#else
        string path = OverridePath;
#endif
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            // Inspector를 편집하는 동안 외부에서 수정한 가격·이름 등도 보존한다.
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 128 * 1024) { error = "기존 JSON이 128KB를 초과합니다. 저장하지 않았습니다."; return false; }
                var current = JsonUtility.FromJson<M6BalanceData>(File.ReadAllText(path));
                if (current == null || !current.Validate(out error)) { error = "기존 JSON이 유효하지 않아 덮어쓰지 않았습니다."; return false; }
                current.enemyCount = candidate.enemyCount; current.enemyHp = candidate.enemyHp;
                current.enemyDamage = candidate.enemyDamage; current.enemySpeed = candidate.enemySpeed;
                current.spawnInterval = candidate.spawnInterval; current.enemyAttackInterval = candidate.enemyAttackInterval;
                current.nightSystem = candidate.nightSystem;
                candidate = current;
            }
            string json = JsonUtility.ToJson(candidate, true);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 128 * 1024) { error = "설정 파일이 128KB를 초과합니다."; return false; }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temporary, json, new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            active = candidate.Copy(); Revision++; Status = "Inspector 자동 저장: " + path;
            error = ""; return true;
        }
        catch (Exception ex) { error = "Inspector 설정 저장 실패: " + ex.Message; Status = error; return false; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    { active = M6BalanceData.Defaults(); Revision = 0; TryReload(out _); RunEconomy.ResetForNewRun(); }

    public static bool TryApplyJson(string json, out string error)
    {
        try
        {
            M6BalanceData candidate = JsonUtility.FromJson<M6BalanceData>(json);
            if (candidate == null) { error = "설정 객체가 없습니다."; return false; }
            if (!candidate.Validate(out error)) return false;
            active = candidate; Revision++; return true;
        }
        catch (Exception ex) { error = "JSON 읽기 실패: " + ex.Message; return false; }
    }

    public static bool TryReload(out string error)
    {
        string path = File.Exists(OverridePath) ? OverridePath : DefaultPath;
        try
        {
            if (!File.Exists(path)) { error = "설정 파일을 찾을 수 없습니다: " + path; Status = error; return false; }
            if (new FileInfo(path).Length > 128 * 1024) { error = "설정 파일이 128KB를 초과합니다."; Status = error; return false; }
            if (!TryApplyJson(File.ReadAllText(path), out error)) { Status = error; return false; }
            Status = "적용 완료: " + path; return true;
        }
        catch (Exception ex) { error = "파일 읽기 실패: " + ex.Message; Status = error; return false; }
    }
}
