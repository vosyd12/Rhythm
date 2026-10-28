using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public class CampWall : MonoBehaviour
{
    [SerializeField]
    private CampConstructionSlotView constructionSlot;

    [SerializeField]
    private SpriteRenderer wallVisual;

    [SerializeField, Min(1)]
    private int maximumHp = 10;

    [SerializeField]
    private Color hitColor = Color.red;

    [SerializeField, Min(0.01f)]
    private float hitFlashSeconds = 0.2f;

    private BoxCollider2D wallArea;
    private Color normalColor;
    private float flashUntil;
    private bool isInitialized;

    private static readonly HashSet<CampWall> activeWalls =
        new HashSet<CampWall>();

    public static IEnumerable<CampWall> All => activeWalls;

    public bool IsStanding =>
        isInitialized &&
        isActiveAndEnabled &&
        wallArea.enabled &&
        RunCampConstruction.IsCompleted(constructionSlot.SlotId);

    public int CurrentHp
    {
        get
        {
            if (!IsStanding ||
                !RunCampConstruction.TryGetConstruction(
                    constructionSlot.SlotId,
                    out CampConstructionData construction))
            {
                return 0;
            }

            construction.MaximumWallHp = maximumHp;
            if (construction.RemainingWallHp == 0)
            {
                construction.RemainingWallHp = maximumHp;
            }

            return construction.RemainingWallHp;
        }
    }

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        activeWalls.Clear();
    }

    private void Awake()
    {
        wallArea = GetComponent<BoxCollider2D>();

        if (constructionSlot == null || wallVisual == null)
        {
            Debug.LogError(
                "Construction Slot과 Wall Visual을 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        // 충돌 반응은 기존 Transform 이동 코드에서 계산한다.
        // 이 콜라이더는 벽의 실제 범위를 제공한다.
        wallArea.isTrigger = true;

        normalColor = wallVisual.color;
        isInitialized = true;
    }

    private void OnEnable()
    {
        if (isInitialized)
        {
            activeWalls.Add(this);
        }
    }

    private void OnDisable()
    {
        activeWalls.Remove(this);

        if (wallVisual != null)
        {
            wallVisual.color = normalColor;
        }
    }

    private void Update()
    {
        if (!IsStanding)
        {
            wallVisual.color = normalColor;
            return;
        }

        // 완공 직후 체력을 초기화한다.
        int hp = CurrentHp;

        wallVisual.color =
            Time.time < flashUntil ? hitColor : normalColor;
    }

    public Rect GetBlockingRect(float padding)
    {
        Bounds bounds = wallArea.bounds;

        return Rect.MinMaxRect(
            bounds.min.x - padding,
            bounds.min.y - padding,
            bounds.max.x + padding,
            bounds.max.y + padding);
    }

    public void TakeDamage(int damage)
    {
        if (damage <= 0 || !IsStanding)
        {
            return;
        }

        int hp = CurrentHp;

        if (!RunCampConstruction.TryGetConstruction(
                constructionSlot.SlotId,
                out CampConstructionData construction))
        {
            return;
        }

        construction.RemainingWallHp = Mathf.Max(0, hp - damage);
        flashUntil = Time.time + hitFlashSeconds;

        if (construction.RemainingWallHp == 0)
        {
            RunCampConstruction.RemoveDestroyedWall(
                constructionSlot.SlotId);

            // 건설 자리의 RefreshView도 다음 프레임에 같은 상태를 반영한다.
            wallVisual.enabled = false;
            wallVisual.color = normalColor;
        }
    }
}