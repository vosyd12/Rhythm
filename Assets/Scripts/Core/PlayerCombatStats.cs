using UnityEngine;

[DisallowMultipleComponent]
public partial class PlayerCombatStats : MonoBehaviour
{
    [SerializeField, Min(0)]
    private int flatDamageBonus;

    [SerializeField, Min(0.01f)]
    private float damageMultiplier = 1.0f;

    [SerializeField, Min(0.01f)]
    private float attackSpeedMultiplier = 1.0f;

    [SerializeField, Min(0.01f)]
    private float projectileSpeedMultiplier = 1.0f;

    [SerializeField, Min(0.01f)]
    private float rangeMultiplier = 1.0f;

    public const int MaximumHp = 20;
    public const int MaximumDefense = 5;
    public const float MaximumStamina = 100f;
    public static PlayerCombatStats Instance { get; private set; }
    public static int CurrentHp { get; private set; } = MaximumHp;
    public static int CurrentDefense { get; private set; } = MaximumDefense;
    public static float Stamina { get; private set; } = MaximumStamina;
    public static float TreatmentRemaining { get; private set; }
    public static bool ActionsBlocked => TreatmentRemaining > 0f;
    public static float LastHitTime { get; private set; } = float.NegativeInfinity;
    private SpriteRenderer bodyVisual;
    private Color bodyColor;
    private static float recoveryDelay;
    private static bool exhausted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InitializeRun() { Instance = null; ResetForNewRun(); }

    public static void ResetForNewRun()
    {
        CurrentHp = MaximumHp;
        CurrentDefense = MaximumDefense;
        Stamina = MaximumStamina;
        TreatmentRemaining = recoveryDelay = 0f;
        exhausted = false;
        LastHitTime = float.NegativeInfinity;
    }

    private void Awake()
    {
        Instance = this;
        Transform body = transform.Find("Visual");
        bodyVisual = body != null ? body.GetComponent<SpriteRenderer>() : GetComponentInChildren<SpriteRenderer>();
        if (bodyVisual != null) bodyColor = bodyVisual.color;
    }

    private void OnDisable()
    {
        if (bodyVisual != null) bodyVisual.color = bodyColor;
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        if (GameExitUI.IsWorldPaused) return;
        AdvanceTreatment(Time.deltaTime);
        if (bodyVisual != null)
            bodyVisual.color = Time.time - LastHitTime < 0.18f
                ? new Color(1f, 0.35f, 0.4f, bodyColor.a) : bodyColor;
    }

    public static void AdvanceTreatment(float seconds)
    {
        if (!(seconds > 0f) || float.IsInfinity(seconds) || !ActionsBlocked) return;
        TreatmentRemaining = Mathf.Max(0f, TreatmentRemaining - seconds);
        if (!ActionsBlocked) ResetForNewRun();
    }

    public static bool TakeDamage(int damage)
    {
        if (damage <= 0 || ActionsBlocked) return false;
        int hp = CurrentHp, defense = CurrentDefense;
        RunCampNpcs.ApplyDamageToGauges(ref hp, ref defense, damage, false);
        LastHitTime = Time.time;
        CurrentHp = hp;
        CurrentDefense = defense;
        if (hp == 0)
        {
            TreatmentRemaining = 15f;
            // 치료 중에도 NPC는 기존 배정을 유지한다.
        }
        return true;
    }

    public static float GetRunningFraction(bool wantsRun, float seconds)
    {
        if (!wantsRun || ActionsBlocked || exhausted || !(seconds > 0f) ||
            float.IsInfinity(seconds) || Stamina <= 0f) return 0f;
        return Mathf.Min(1f, Stamina / (seconds * 20f));
    }

    // 소진 프레임은 남은 스태미너만큼만 달려 프레임 간격에 따른 무료 달리기를 막는다.
    public static float AdvanceStamina(bool wantsRun, float seconds)
    {
        if (!(seconds > 0f) || float.IsInfinity(seconds) || ActionsBlocked) return 0f;
        if (!wantsRun) exhausted = false;
        if (wantsRun && !exhausted && Stamina > 0f)
        {
            float runningSeconds = Mathf.Min(seconds, Stamina / 20f);
            Stamina = Mathf.Max(0f, Stamina - runningSeconds * 20f);
            recoveryDelay = 1f;
            exhausted = Stamina <= 0f;
            return runningSeconds / seconds;
        }
        float recoverySeconds = Mathf.Max(0f, seconds - recoveryDelay);
        recoveryDelay = Mathf.Max(0f, recoveryDelay - seconds);
        Stamina = Mathf.Min(MaximumStamina, Stamina + recoverySeconds * 25f);
        return 0f;
    }

    public static void RecoverAfterNight()
    {
        if (!ActionsBlocked) ResetForNewRun();
    }

#if UNITY_EDITOR
    [ContextMenu("테스트/M4 지휘관 일반 피해 1")]
    private void TestDamage() { if (Application.isPlaying) TakeDamage(1); }
    [ContextMenu("테스트/M4 지휘관 전투 불능")]
    private void TestIncapacitation()
    {
        if (!Application.isPlaying) return;
        TakeDamage(int.MaxValue);
        TakeDamage(int.MaxValue);
    }
#endif

    public int CalculateDamage(int weaponDamage)
    {
        return Mathf.Max(
            1,
            Mathf.RoundToInt(
                (weaponDamage + flatDamageBonus + RunProgress.AttackDamageBonus + (RunEconomy.EquipmentEquipped ? RunEconomy.EquipmentBonus : 0)) *
                damageMultiplier));
    }

    public float CalculateAttackRate(float weaponRate)
    {
        return Mathf.Max(
            0.01f,
            weaponRate * attackSpeedMultiplier);
    }

    public float CalculateProjectileSpeed(float weaponSpeed)
    {
        return Mathf.Max(
            0.01f,
            weaponSpeed * projectileSpeedMultiplier);
    }

    public float CalculateRange(float weaponRange)
    {
        return Mathf.Max(
            0.01f,
            weaponRange * rangeMultiplier);
    }
}
