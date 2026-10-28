using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 밤 전투의 이미지와 시각 효과만 처리한다.
// 적 체력, 공격 판정, 승패와 보상은 처리하지 않는다.
public class NightBattleView : MonoBehaviour
{
    [SerializeField]
    private SpriteRenderer enemyTemplate;

    [SerializeField]
    private SpriteRenderer baseVisual;

    [SerializeField]
    private SpriteRenderer shotLine;

    private SpriteRenderer riftView;
    private float lastRiftHitTime = float.NegativeInfinity;
    public void SetRift(bool visible, Vector2 position, bool warning, float damage01 = 0f)
    {
        if (riftView == null)
        {
            riftView = Instantiate(enemyTemplate, transform);
            riftView.name = "Rift";
            riftView.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
        }
        riftView.gameObject.SetActive(visible);
        riftView.transform.position = new Vector3(position.x, position.y, baseVisual.transform.position.z);
        riftView.color = Time.time - lastRiftHitTime < 0.18f ? Color.white :
            warning ? new Color(0.45f, 0.3f, 0.65f, 0.65f) :
                Color.Lerp(new Color(0.8f, 0.15f, 0.9f, 1f), new Color(1f, 0.15f, 0.15f, 1f), Mathf.Clamp01(damage01));
    }
    public void ShowRiftHit() { lastRiftHitTime = Time.time; if (riftView != null) riftView.color = Color.white; }

    private SpriteRenderer[] enemyViews;
    private sealed class WorldGauge
    {
        public Transform Root;
        public SpriteRenderer Hp;
        public SpriteRenderer Defense;
        public bool Seen;
    }
    private Sprite gaugeSprite;
    private WorldGauge[] enemyGauges;
    private WorldGauge playerGauge;
    private readonly Dictionary<int, WorldGauge> npcGauges = new Dictionary<int, WorldGauge>();
    private readonly Dictionary<int, WorldGauge> towerGauges = new Dictionary<int, WorldGauge>();

    private readonly Dictionary<int, Color> enemyTints = new Dictionary<int, Color>();
    private sealed class LootVisual { public SpriteRenderer Renderer; public float Start; }
    private readonly List<LootVisual> lootViews = new List<LootVisual>();
    public void SetEnemyStyle(int index, bool elite, int kind)
    {
        Color tint = elite ? new Color(1f, 0.45f, 0.1f) : kind == 2 ? new Color(0.7f, 0.4f, 1f) :
            kind == 1 ? new Color(0.3f, 0.75f, 1f) : originalEnemyColor;
        enemyTints[index] = tint; enemyViews[index].color = tint;
    }
    public void ShowLoot(Vector2 position, bool rare)
    {
        LootVisual available = null;
        foreach (var view in lootViews)
            if (!view.Renderer.gameObject.activeSelf) { available = view; break; }
        if (available == null)
        {
            if (lootViews.Count >= 64) available = lootViews[0];
            else
            {
                available = new LootVisual { Renderer = Instantiate(enemyTemplate, transform) };
                available.Renderer.name = "LootVisual"; lootViews.Add(available);
            }
        }
        available.Renderer.gameObject.SetActive(true);
        available.Renderer.transform.position = new Vector3(position.x, position.y, baseVisual.transform.position.z);
        available.Renderer.transform.localScale = new Vector3(0.3f, 0.3f, 1);
        available.Renderer.color = rare ? new Color(1f, 0.75f, 0.05f, 1) : new Color(0.55f, 0.9f, 0.95f, 1);
        available.Start = Time.time;
    }

    private Color originalEnemyColor;
    private Color originalBaseColor;

    private sealed class ShotVisual
    {
        public SpriteRenderer Renderer;
        public float HideTime;
    }
    private readonly List<ShotVisual> shots = new List<ShotVisual>();
    private bool isInitialized;

    // NightDefenseTester가 Start에서 호출한다.
    // 두 컴포넌트의 Awake 실행 순서에 의존하지 않는다.
    public bool TryInitialize(int enemyCount)
    {
        if (isInitialized)
        {
            EnsureEnemyCapacity(enemyCount);
            return true;
        }

        if (enemyTemplate == null ||
            baseVisual == null ||
            shotLine == null ||
            enemyCount < 1)
        {
            Debug.LogError(
                "적 SpriteRenderer 프리팹, 거점 외형, " +
                "발사선과 적 수를 확인해 주세요.",
                this);

            return false;
        }

        originalEnemyColor = enemyTemplate.color;
        originalBaseColor = baseVisual.color;

        enemyViews = new SpriteRenderer[enemyCount];
        enemyGauges = new WorldGauge[enemyCount];

        for (int i = 0; i < enemyViews.Length; i++)
        {
            SpriteRenderer enemy = Instantiate(
                enemyTemplate,
                transform);

            enemy.name = $"Enemy_{i + 1}";
            enemy.gameObject.SetActive(false);
            enemyViews[i] = enemy;
            enemyGauges[i] = CreateWorldGauge("EnemyHP_" + i);
        }

        shots.Add(new ShotVisual { Renderer = shotLine });
        isInitialized = true;
        ResetVisuals();
        return true;
    }

    // Inspector 검증 중 늘어난 적 수만큼 런타임 표시만 확장한다.
    private void EnsureEnemyCapacity(int count)
    {
        if (count <= enemyViews.Length) return;
        int previous = enemyViews.Length;
        System.Array.Resize(ref enemyViews, count);
        System.Array.Resize(ref enemyGauges, count);
        for (int i = previous; i < count; i++)
        {
            enemyViews[i] = Instantiate(enemyTemplate, transform);
            enemyViews[i].name = $"Enemy_{i + 1}";
            enemyViews[i].gameObject.SetActive(false);
            enemyGauges[i] = CreateWorldGauge("EnemyHP_" + i);
        }
    }

    public void ResetVisuals()
    {
        foreach (var loot in lootViews) loot.Renderer.gameObject.SetActive(false);
        foreach (ShotVisual shot in shots)
        {
            shot.HideTime = 0f;
            shot.Renderer.gameObject.SetActive(false);
        }

        if (enemyGauges != null)
            foreach (WorldGauge gauge in enemyGauges)
                if (gauge != null) gauge.Root.gameObject.SetActive(false);
        baseVisual.color = originalBaseColor;

        foreach (SpriteRenderer enemy in enemyViews)
        {
            enemy.color = originalEnemyColor;
            enemy.gameObject.SetActive(false);
        }
    }

    public void ShowEnemy(int index, Vector2 position)
    {
        SpriteRenderer enemy = enemyViews[index];

        SetEnemyPosition(index, position);
        enemy.color = originalEnemyColor;
        enemy.gameObject.SetActive(true);
    }

    public void SetEnemyPosition(int index, Vector2 position)
    {
        enemyViews[index].transform.position = new Vector3(
            position.x,
            position.y,
            baseVisual.transform.position.z);
    }

    public void HideEnemy(int index)
    {
        enemyViews[index].gameObject.SetActive(false);
        enemyGauges[index].Root.gameObject.SetActive(false);
    }

    public void ShowEnemyHit(int index)
    {
        enemyViews[index].color = Color.white;
    }

    public void ShowBaseHit()
    {
        baseVisual.color = new Color(1.0f, 0.35f, 0.4f);
    }

    public void ShowShot(Vector2 targetPosition)
    {
        ShowShotFrom(baseVisual.bounds.center, targetPosition);
    }
    public void SetEnemyVitals(int index, int hp, int maximumHp, int defense = 0, int maximumDefense = 0)
    {
        if (enemyGauges == null || index < 0 || index >= enemyGauges.Length) return;
        enemyGauges[index].Hp.color = new Color(1f, 0.3f, 0.35f);
        SetWorldGauge(enemyGauges[index], enemyViews[index].transform.position,
            hp, maximumHp, defense, maximumDefense);
    }

    private WorldGauge CreateWorldGauge(string gaugeName)
    {
        if (gaugeSprite == null)
            gaugeSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f), 1f);
        var root = new GameObject(gaugeName);
        root.transform.SetParent(transform, false);
        var gauge = new WorldGauge { Root = root.transform };
        MakeGaugeSprite("Track", root.transform, new Color(0.03f, 0.04f, 0.06f, 0.95f), 0)
            .transform.localScale = new Vector3(0.84f, 0.14f, 1f);
        gauge.Hp = MakeGaugeSprite("HP", root.transform, new Color(0.25f, 0.95f, 0.4f), 1);
        gauge.Defense = MakeGaugeSprite("Defense", root.transform, new Color(0.25f, 0.65f, 1f), 1);
        root.SetActive(false);
        return gauge;
    }

    private SpriteRenderer MakeGaugeSprite(string objectName, Transform parent, Color color, int order)
    {
        var item = new GameObject(objectName);
        item.transform.SetParent(parent, false);
        SpriteRenderer renderer = item.AddComponent<SpriteRenderer>();
        renderer.sprite = gaugeSprite;
        renderer.sharedMaterial = enemyTemplate.sharedMaterial;
        renderer.color = color;
        renderer.sortingLayerID = enemyTemplate.sortingLayerID;
        renderer.sortingOrder = Mathf.Max(enemyTemplate.sortingOrder, baseVisual.sortingOrder) + 20 + order;
        return renderer;
    }

    private void SetWorldGauge(WorldGauge gauge, Vector3 position, int hp, int maximumHp, int defense, int maximumDefense)
    {
        gauge.Seen = true;
        gauge.Root.gameObject.SetActive(maximumHp > 0);
        gauge.Root.position = new Vector3(position.x, position.y + 0.7f, position.z);
        float ratio = maximumHp > 0 ? Mathf.Clamp01((float)hp / maximumHp) : 0f;
        gauge.Hp.transform.localScale = new Vector3(0.8f * ratio, 0.05f, 1f);
        gauge.Hp.transform.localPosition = new Vector3((ratio - 1f) * 0.4f, 0.035f, 0f);
        ratio = maximumDefense > 0 ? Mathf.Clamp01((float)defense / maximumDefense) : 0f;
        gauge.Defense.transform.localScale = new Vector3(0.8f * ratio, 0.04f, 1f);
        gauge.Defense.transform.localPosition = new Vector3((ratio - 1f) * 0.4f, -0.035f, 0f);
        gauge.Defense.enabled = maximumDefense > 0;
    }

    private void UpdateActorGauges()
    {
        foreach (WorldGauge gauge in npcGauges.Values) gauge.Seen = false;
        foreach (CampNpcData npc in RunCampNpcs.All)
        {
            if (!npc.IsAlive || !npc.IsInCampScene || !npc.HasSavedPosition || npc.HasPendingTowerExit ||
                (npc.EnteredDefensePostId != 0 && CampDefensePost.TryGetActive(npc.EnteredDefensePostId, out var post) && post.IsTower)) continue;
            if (!npcGauges.TryGetValue(npc.Id, out WorldGauge gauge))
                npcGauges.Add(npc.Id, gauge = CreateWorldGauge("NPCVitals_" + npc.Id));
            SetWorldGauge(gauge, npc.SavedPosition, npc.CurrentHp, npc.MaximumHp, npc.CurrentDefense, npc.MaximumDefense);
        }
        foreach (WorldGauge gauge in npcGauges.Values)
            if (!gauge.Seen) gauge.Root.gameObject.SetActive(false);

        foreach (WorldGauge gauge in towerGauges.Values) gauge.Seen = false;
        foreach (CampDefensePost post in CampDefensePost.All)
        {
            if (!post.IsTower || !post.IsAvailable) continue;
            if (!towerGauges.TryGetValue(post.PostId, out WorldGauge gauge))
                towerGauges.Add(post.PostId, gauge = CreateWorldGauge("TowerVitals_" + post.PostId));
            SetWorldGauge(gauge, post.Position, post.CurrentHp, post.MaximumHp, post.CurrentDefense, post.MaximumDefense);
        }
        foreach (WorldGauge gauge in towerGauges.Values)
            if (!gauge.Seen) gauge.Root.gameObject.SetActive(false);

        if (PlayerCombatStats.Instance != null)
        {
            if (playerGauge == null) playerGauge = CreateWorldGauge("CommanderVitals");
            SetWorldGauge(playerGauge, PlayerCombatStats.Instance.transform.position,
                PlayerCombatStats.CurrentHp, PlayerCombatStats.MaximumHp,
                PlayerCombatStats.CurrentDefense, PlayerCombatStats.MaximumDefense);
        }
        else if (playerGauge != null) playerGauge.Root.gameObject.SetActive(false);
        for (int i = 0; i < enemyViews.Length; i++)
            if (enemyViews[i].gameObject.activeSelf)
                enemyGauges[i].Root.position = enemyViews[i].transform.position + new Vector3(0f, 0.7f, 0f);
    }

    public void UpdateVisualEffects()
    {
        UpdateActorGauges();
        foreach (var loot in lootViews)
        {
            if (!loot.Renderer.gameObject.activeSelf) continue;
            float elapsed = Time.time - loot.Start;
            if (elapsed >= 2.5f) { loot.Renderer.gameObject.SetActive(false); continue; }
            Color color = loot.Renderer.color;
            color.a = 1f - Mathf.Clamp01((elapsed - 1.5f) / 1f); loot.Renderer.color = color;
        }
        foreach (ShotVisual shot in shots)
        {
            if (shot.Renderer.gameObject.activeSelf && Time.time >= shot.HideTime)
                shot.Renderer.gameObject.SetActive(false);
        }

        float blend =
            1.0f - Mathf.Exp(-12.0f * Time.deltaTime);

        baseVisual.color = Color.Lerp(
            baseVisual.color,
            originalBaseColor,
            blend);

        for (int i = 0; i < enemyViews.Length; i++)
        {
            SpriteRenderer enemy = enemyViews[i];
            if (!enemy.gameObject.activeSelf)
            {
                continue;
            }

            enemy.color = Color.Lerp(
                enemy.color,
                enemyTints.TryGetValue(i, out Color tint) ? tint : originalEnemyColor,
                blend);
        }
    }

    private void OnDestroy()
    {
        if (riftView != null) Destroy(riftView.gameObject);
        if (gaugeSprite != null) Destroy(gaugeSprite);
        if (enemyViews == null)
        {
            return;
        }

        foreach (SpriteRenderer enemy in enemyViews)
        {
            if (enemy != null)
            {
                Destroy(enemy.gameObject);
            }
        }
    }

    public void ShowShotFrom(Vector2 startPosition, Vector2 targetPosition)
    {
        ShotVisual available = null;
        foreach (ShotVisual shot in shots)
        {
            if (!shot.Renderer.gameObject.activeSelf || Time.time >= shot.HideTime)
            { available = shot; break; }
        }
        if (available == null)
        {
            available = new ShotVisual { Renderer = Instantiate(shotLine, transform) };
            shots.Add(available);
        }
        Vector2 direction = targetPosition - startPosition;
        Vector2 middle = (targetPosition + startPosition) * 0.5f;
        Transform line = available.Renderer.transform;
        line.position = new Vector3(middle.x, middle.y, baseVisual.transform.position.z);
        line.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        line.localScale = new Vector3(direction.magnitude, 0.05f, 1f);
        available.Renderer.gameObject.SetActive(true);
        available.HideTime = Time.time + 0.08f;
    }
}