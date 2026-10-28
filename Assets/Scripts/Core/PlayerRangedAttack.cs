using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 기본 실행 순서의 카메라 LateUpdate 이후 조준을 계산한다.
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class PlayerRangedAttack : MonoBehaviour
{
    [SerializeField]
    private Camera aimingCamera;

    [SerializeField]
    private NightDefenseTester battle;

    [SerializeField]
    private PlayerCombatStats combatStats;

    [SerializeField]
    private RangedWeaponData equippedWeapon;

    [Tooltip("플레이어와 함께 이동하는 조준 회전축")]
    [SerializeField]
    private Transform aimPivot;

    [Tooltip("Aim Pivot의 자식 발사 위치")]
    [SerializeField]
    private Transform muzzle;

    public Vector2 AimDirection { get; private set; }
        = Vector2.right;

    // 1이면 한 번의 공격을 회복하는 중이고, 0이면 발사 가능하다.
    private float recovery;
    private bool requireMouseRelease;

    private void Awake()
    {
        if (aimingCamera == null ||
            battle == null ||
            combatStats == null ||
            equippedWeapon == null ||
            aimPivot == null ||
            muzzle == null ||
            equippedWeapon.ProjectilePrefab == null ||
            !muzzle.IsChildOf(aimPivot) ||
            !aimPivot.IsChildOf(transform) ||
            aimPivot == transform)
        {
            Debug.LogError(
                "카메라, 전투 시스템, 스탯, 무기와 " +
                "플레이어 자식 Aim Pivot·Muzzle을 확인해 주세요.",
                this);

            enabled = false;
        }
    }

    private void LateUpdate()
    {
        float deltaTime = Time.deltaTime;

        if (deltaTime <= 0.0f)
        {
            return;
        }

        float attackRate = combatStats.CalculateAttackRate(
            equippedWeapon.AttacksPerSecond);

        recovery = Mathf.Max(
            0.0f,
            recovery - attackRate * deltaTime);

        Mouse mouse = Mouse.current;

        if (mouse == null)
        {
            return;
        }

        if (!mouse.leftButton.isPressed)
        {
            requireMouseRelease = false;
        }

        EventSystem events = EventSystem.current;

        bool blocked =
            PlayerCombatStats.ActionsBlocked ||
            !Application.isFocused ||
            CampWindowController.WorldInputBlocked ||
            CampWallPlacementController.IsPlacing ||
            M7CampaignUI.IsSelectingCommandPoint ||
            (events != null &&
             (events.IsPointerOverGameObject() ||
              events.currentSelectedGameObject != null));

        if (blocked ||
            !battle.isActiveAndEnabled ||
            battle.IsChangingScene ||
            (!battle.IsPreparing && !battle.IsRunning))
        {
            requireMouseRelease = true;
            return;
        }

        Vector2 screenPosition = mouse.position.ReadValue();

        if (!aimingCamera.pixelRect.Contains(screenPosition))
        {
            requireMouseRelease = true;
            return;
        }

        Ray ray = aimingCamera.ScreenPointToRay(screenPosition);

        Plane playPlane = new Plane(
            Vector3.forward,
            new Vector3(0.0f, 0.0f, transform.position.z));

        if (!playPlane.Raycast(ray, out float distance))
        {
            return;
        }

        Vector2 cursorPosition = ray.GetPoint(distance);
        Vector2 offset =
            cursorPosition - (Vector2)aimPivot.position;

        if (offset.sqrMagnitude > 0.0001f)
        {
            AimDirection = offset.normalized;
        }

        float angle =
            Mathf.Atan2(AimDirection.y, AimDirection.x) *
            Mathf.Rad2Deg;

        aimPivot.rotation =
            Quaternion.Euler(0.0f, 0.0f, angle);

        bool wantsToFire =
            equippedWeapon.FireMode == RangedFireMode.Automatic
                ? mouse.leftButton.isPressed
                : mouse.leftButton.wasPressedThisFrame;

        if (!wantsToFire ||
            requireMouseRelease ||
            recovery > 0.0f)
        {
            return;
        }

        Fire();
        recovery = 1.0f;
    }

    private void Fire()
    {
        PlayerProjectile projectile = Instantiate(
            equippedWeapon.ProjectilePrefab,
            muzzle.position,
            aimPivot.rotation);

        projectile.Initialize(
            battle,
            AimDirection,
            combatStats.CalculateDamage(equippedWeapon.Damage),
            combatStats.CalculateProjectileSpeed(
                equippedWeapon.ProjectileSpeed),
            combatStats.CalculateRange(equippedWeapon.Range),
            equippedWeapon.ProjectileRadius);
    }
}
