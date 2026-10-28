using UnityEngine;

[DisallowMultipleComponent]
public class PlayerProjectile : MonoBehaviour
{
    private NightDefenseTester battle;
    private int battleVersion;

    private Vector2 direction;
    private float speed;
    private float remainingDistance;
    private float hitRadius;
    private int damage;

    private bool isInitialized;

    public void Initialize(
        NightDefenseTester battleSystem,
        Vector2 shotDirection,
        int shotDamage,
        float shotSpeed,
        float shotRange,
        float shotRadius)
    {
        battle = battleSystem;
        battleVersion = battle.BattleVersion;

        direction = shotDirection.normalized;
        damage = shotDamage;
        speed = shotSpeed;
        remainingDistance = shotRange;
        hitRadius = shotRadius;

        float angle =
            Mathf.Atan2(direction.y, direction.x) *
            Mathf.Rad2Deg;

        transform.rotation = Quaternion.Euler(0.0f, 0.0f, angle);

        isInitialized = true;
    }

    private void Update()
    {
        if (GameExitUI.IsWorldPaused) return;
        if (!isInitialized)
        {
            return;
        }

        if (battle == null ||
            !battle.isActiveAndEnabled ||
            battle.BattleVersion != battleVersion ||
            (!battle.IsPreparing && !battle.IsRunning))
        {
            Destroy(gameObject);
            return;
        }

        float distance = Mathf.Min(
            speed * Time.deltaTime,
            remainingDistance);

        if (distance <= 0.0f)
        {
            return;
        }

        Vector2 from = transform.position;
        Vector2 to = from + direction * distance;

        if (battle.TryFindProjectileHit(
                from,
                to,
                hitRadius,
                out int enemyIndex,
                out float fraction))
        {
            SetPosition(Vector2.Lerp(from, to, fraction));

            battle.TryDamageEnemyFromProjectile(
                enemyIndex,
                damage);

            Destroy(gameObject);
            return;
        }

        // 확정한 규칙에 따라 아군 벽은 검사하지 않는다.
        SetPosition(to);
        remainingDistance -= distance;

        if (remainingDistance <= 0.0001f)
        {
            Destroy(gameObject);
        }
    }

    private void SetPosition(Vector2 position)
    {
        Vector3 current = transform.position;

        transform.position = new Vector3(
            position.x,
            position.y,
            current.z);
    }
}