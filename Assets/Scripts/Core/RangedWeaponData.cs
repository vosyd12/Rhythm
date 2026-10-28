using UnityEngine;

public enum RangedFireMode
{
    Single,
    Automatic
}

[CreateAssetMenu(
    menuName = "Camp/원거리 무기",
    fileName = "RangedWeapon")]
public class RangedWeaponData : ScriptableObject
{
    [SerializeField]
    private string weaponName = "테스트 총기";

    [SerializeField]
    private RangedFireMode fireMode = RangedFireMode.Automatic;

    [SerializeField, Min(1)]
    private int damage = 1;

    [Tooltip("초당 공격 횟수")]
    [SerializeField, Min(0.01f)]
    private float attacksPerSecond = 3.0f;

    [SerializeField, Min(0.01f)]
    private float projectileSpeed = 20.0f;

    [SerializeField, Min(0.01f)]
    private float range = 15.0f;

    [SerializeField, Min(0.01f)]
    private float projectileRadius = 0.1f;

    [SerializeField]
    private PlayerProjectile projectilePrefab;

    public string WeaponName => weaponName;
    public RangedFireMode FireMode => fireMode;
    public int Damage => damage;
    public float AttacksPerSecond => attacksPerSecond;
    public float ProjectileSpeed => projectileSpeed;
    public float Range => range;
    public float ProjectileRadius => projectileRadius;
    public PlayerProjectile ProjectilePrefab => projectilePrefab;
}