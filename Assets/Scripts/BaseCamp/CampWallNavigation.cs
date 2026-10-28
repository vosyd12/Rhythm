using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CampWallNavigation : MonoBehaviour
{
    [SerializeField]
    private NightDefenseTester nightDefenseTester;

    [SerializeField]
    private Transform leftBoundary;

    [SerializeField]
    private Transform rightBoundary;

    [SerializeField]
    private Transform bottomBoundary;

    [SerializeField]
    private Transform topBoundary;

    [SerializeField, Min(0.01f)]
    private float enemyRadius = 0.2f;

    [SerializeField, Min(0.01f)]
    private float allyRadius = 0.2f;

    public static CampWallNavigation Instance { get; private set; }

    public float EnemyRadius => enemyRadius;
    public float AllyRadius => allyRadius;

    // 현재 구조에서는 Preparing을 낮으로 취급한다.
    // 밤 결과 화면에서도 다음 날로 넘어갈 때까지 벽은 아군을 막는다.
    public bool BlocksAllies =>
        nightDefenseTester != null &&
        nightDefenseTester.IsInitialized &&
        !nightDefenseTester.IsPreparing;

    public Rect MapBounds => mapRect;
    private Rect mapRect;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInstance()
    {
        Instance = null;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError(
                "CampWallNavigation은 씬에 하나만 배치해 주세요.",
                this);

            enabled = false;
            return;
        }

        if (nightDefenseTester == null ||
            leftBoundary == null ||
            rightBoundary == null ||
            bottomBoundary == null ||
            topBoundary == null)
        {
            Debug.LogError(
                "밤 전투 시스템과 상하좌우 경계를 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        mapRect = Rect.MinMaxRect(
            leftBoundary.position.x,
            bottomBoundary.position.y,
            rightBoundary.position.x,
            topBoundary.position.y);

        if (mapRect.width <= 0.0f || mapRect.height <= 0.0f)
        {
            Debug.LogError("맵 경계 순서를 확인해 주세요.", this);
            enabled = false;
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private bool IsInsideMap(Vector2 point, float radius)
    {
        return point.x >= mapRect.xMin + radius &&
               point.x <= mapRect.xMax - radius &&
               point.y >= mapRect.yMin + radius &&
               point.y <= mapRect.yMax - radius;
    }

    private static bool Contains(Rect rect, Vector2 point)
    {
        return point.x >= rect.xMin &&
               point.x <= rect.xMax &&
               point.y >= rect.yMin &&
               point.y <= rect.yMax;
    }

    private static bool ClipAxis(
        float origin,
        float movement,
        float minimum,
        float maximum,
        ref float entry,
        ref float exit)
    {
        if (Mathf.Abs(movement) < 0.000001f)
        {
            return origin >= minimum && origin <= maximum;
        }

        float first = (minimum - origin) / movement;
        float second = (maximum - origin) / movement;

        if (first > second)
        {
            float temporary = first;
            first = second;
            second = temporary;
        }

        entry = Mathf.Max(entry, first);
        exit = Mathf.Min(exit, second);

        return entry <= exit;
    }

    private static bool IntersectsSegment(
        Rect rect,
        Vector2 from,
        Vector2 to,
        out float entry)
    {
        entry = 0.0f;
        float exit = 1.0f;
        Vector2 movement = to - from;

        return ClipAxis(
                   from.x, movement.x,
                   rect.xMin, rect.xMax,
                   ref entry, ref exit) &&
               ClipAxis(
                   from.y, movement.y,
                   rect.yMin, rect.yMax,
                   ref entry, ref exit);
    }

    private struct NavigationBlock
    {
        public Rect Area;
        public CampWall Wall;
        public CampDefensePost Tower;
    }

    // 동기 경로/충돌 호출의 작업 버퍼다. 호출 밖으로 보관하거나 공유하지 않는다.
    private readonly List<NavigationBlock> navigationBlocks = new List<NavigationBlock>();
    private Collider2D[] naturalColliders;
    private float nextColliderScan;

    private List<NavigationBlock> GetBlocks(float radius, bool includeTowers, bool includeWalls = true)
    {
        var blocks = navigationBlocks;
        blocks.Clear();
        foreach (CampWall wall in CampWall.All)
            if (includeWalls && wall != null && wall.IsStanding)
                blocks.Add(new NavigationBlock { Area = wall.GetBlockingRect(radius), Wall = wall });
        if (includeTowers)
            foreach (CampDefensePost tower in CampDefensePost.All)
                if (tower != null && tower.IsTower && tower.IsAvailable)
                    blocks.Add(new NavigationBlock { Area = Expand(tower.Footprint, radius), Tower = tower });

        if (naturalColliders == null || Time.time >= nextColliderScan)
        {
            naturalColliders = FindObjectsByType<Collider2D>();
            nextColliderScan = Time.time + 1f;
        }
        foreach (Collider2D obstacle in naturalColliders)
        {
            if (obstacle == null || !obstacle.isActiveAndEnabled || obstacle.isTrigger ||
                obstacle.GetComponentInParent<PlayerMovement>() != null ||
                obstacle.GetComponentInParent<CampNpcIdleView>() != null ||
                obstacle.GetComponentInParent<CampConstructionSlotView>() != null ||
                obstacle.GetComponentInParent<CampWall>() != null ||
                obstacle.GetComponentInParent<CampDefensePost>() != null) continue;
            Bounds bounds = obstacle.bounds;
            blocks.Add(new NavigationBlock { Area = Rect.MinMaxRect(
                bounds.min.x - radius, bounds.min.y - radius,
                bounds.max.x + radius, bounds.max.y + radius) });
        }
        return blocks;
    }

    private static Rect Expand(Rect rect, float radius) => Rect.MinMaxRect(
        rect.xMin - radius, rect.yMin - radius, rect.xMax + radius, rect.yMax + radius);

    private static bool FirstBlock(List<NavigationBlock> blocks, Vector2 from, Vector2 to,
        out NavigationBlock block, out float entry)
    {
        block = default;
        entry = float.PositiveInfinity;
        bool found = false;
        foreach (NavigationBlock candidate in blocks)
            if (IntersectsSegment(candidate.Area, from, to, out float hit) && hit < entry)
            { block = candidate; entry = hit; found = true; }
        return found;
    }

    public bool TryFindFirstObstacle(Vector2 from, Vector2 to, float radius,
        out CampWall wall, out CampDefensePost tower, out float entry, bool includeTowers = true)
    {
        bool found = FirstBlock(GetBlocks(radius, includeTowers, includeTowers || BlocksAllies), from, to, out var block, out entry);
        wall = block.Wall;
        tower = block.Tower;
        return found;
    }

    // 아군은 수동 입장을 위해 타워 내부로 이동할 수 있다.
    public bool TryFindFirstWall(Vector2 from, Vector2 to, float radius,
        out CampWall wall, out float entry) =>
        TryFindFirstObstacle(from, to, radius, out wall, out _, out entry, false);

    private bool IsFreePoint(Vector2 point, float radius, List<NavigationBlock> blocks)
    {
        if (!IsInsideMap(point, radius)) return false;
        foreach (var block in blocks) if (Contains(block.Area, point)) return false;
        return true;
    }

    public bool TryFindPath(Vector2 from, Vector2 destination, float radius,
        List<Vector2> result, bool includeTowers = false)
    {
        result.Clear();
        var blocks = GetBlocks(radius, includeTowers, includeTowers || BlocksAllies);
        if (!IsFreePoint(from, radius, blocks) || !IsFreePoint(destination, radius, blocks)) return false;
        if (!FirstBlock(blocks, from, destination, out _, out _))
        { result.Add(from); result.Add(destination); return true; }

        var nodes = new List<Vector2> { from, destination };
        foreach (var block in blocks)
        {
            Rect rect = Expand(block.Area, 0.01f);
            Vector2[] corners = { new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin, rect.yMax),
                new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax) };
            foreach (Vector2 corner in corners)
                if (IsFreePoint(corner, radius, blocks)) nodes.Add(corner);
        }
        int count = nodes.Count;
        var distances = new float[count];
        var previous = new int[count];
        var visited = new bool[count];
        for (int i = 0; i < count; i++) { distances[i] = float.PositiveInfinity; previous[i] = -1; }
        distances[0] = 0f;
        for (int step = 0; step < count; step++)
        {
            int current = -1;
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (!visited[i] && distances[i] < best) { best = distances[i]; current = i; }
            if (current < 0 || current == 1) break;
            visited[current] = true;
            for (int next = 0; next < count; next++)
            {
                if (visited[next] || next == current ||
                    FirstBlock(blocks, nodes[current], nodes[next], out _, out _)) continue;
                float candidate = distances[current] + Vector2.Distance(nodes[current], nodes[next]);
                if (candidate < distances[next]) { distances[next] = candidate; previous[next] = current; }
            }
        }
        if (float.IsPositiveInfinity(distances[1])) return false;
        for (int index = 1; index >= 0; index = previous[index])
        { result.Add(nodes[index]); if (index == 0) break; }
        result.Reverse();
        return true;
    }

    public Vector2 StopBeforeObstacle(Vector2 from, Vector2 desired, float radius,
        out CampWall wall, out CampDefensePost tower, bool includeTowers = true)
    {
        if (!TryFindFirstObstacle(from, desired, radius, out wall, out tower, out float entry, includeTowers))
            return new Vector2(Mathf.Clamp(desired.x, mapRect.xMin + radius, mapRect.xMax - radius),
                Mathf.Clamp(desired.y, mapRect.yMin + radius, mapRect.yMax - radius));
        float distance = Vector2.Distance(from, desired);
        return distance <= 0.000001f ? from :
            Vector2.Lerp(from, desired, Mathf.Max(0f, entry - 0.005f / distance));
    }

    public Vector2 StopBeforeWall(Vector2 from, Vector2 desired, float radius, out CampWall wall) =>
        StopBeforeObstacle(from, desired, radius, out wall, out _, false);

    // 낮에는 벽을 통과하던 NPC가 밤 전환/완공 시 벽 안에서 출발할 수 있다.
    // 일반 경로 검색은 막힌 출발점을 거부하므로 기존 탈출 이동을 먼저 연결한다.
    public bool TryGetAllyWallExit(Vector2 from, out Vector2 exit)
    {
        exit = from;
        if (!BlocksAllies) return false;
        foreach (CampWall wall in CampWall.All)
        {
            if (wall == null || !wall.IsStanding) continue;
            Rect area = wall.GetBlockingRect(allyRadius);
            if (!Contains(area, from)) continue;
            Vector2 candidate = GetNearestExit(area, from);
            if (!IsInsideMap(candidate, allyRadius)) continue;
            bool blocked = false;
            foreach (NavigationBlock other in GetBlocks(allyRadius, false))
            {
                if (other.Wall == wall) continue;
                if (IntersectsSegment(other.Area, from, candidate, out _)) { blocked = true; break; }
            }
            if (blocked) continue;
            exit = candidate;
            return true;
        }
        return false;
    }

    public Vector2 MoveAlly(Vector2 from, Vector2 desired)
    {

        Vector2 result = from;

        // 밤 전환이나 벽 완공 시 이미 겹쳐 있었다면,
        // 벽에서 빠져나오는 방향만 허용한다.
        foreach (CampWall wall in CampWall.All)
        {
            if (!BlocksAllies || wall == null || !wall.IsStanding)
            {
                continue;
            }

            Rect rect = wall.GetBlockingRect(allyRadius);

            if (!Contains(rect, from))
            {
                continue;
            }

            Vector2 escape = GetNearestExit(rect, from);
            Vector2 movement = desired - from;

            if (Vector2.Dot(movement, escape - from) <= 0.0f)
            {
                return from;
            }

            // 현재 겹친 벽만 제외하고 다른 벽과 자연 장애물은 계속 검사한다.
            foreach (NavigationBlock other in GetBlocks(allyRadius, false))
            {
                if (other.Wall == wall) continue;
                if (IntersectsSegment(other.Area, from, desired, out _))
                {
                    return from;
                }
            }

            return desired;
        }

        // 벽을 따라 움직일 수 있도록 X와 Y를 나누어 처리한다.
        result = StopBeforeWall(
            result,
            new Vector2(desired.x, result.y),
            allyRadius,
            out _);

        result = StopBeforeWall(
            result,
            new Vector2(result.x, desired.y),
            allyRadius,
            out _);

        return result;
    }

    private static Vector2 GetNearestExit(Rect rect, Vector2 point)
    {
        float left = point.x - rect.xMin;
        float right = rect.xMax - point.x;
        float bottom = point.y - rect.yMin;
        float top = rect.yMax - point.y;

        float minimum = Mathf.Min(
            Mathf.Min(left, right),
            Mathf.Min(bottom, top));

        if (minimum == left)
            return new Vector2(rect.xMin - 0.01f, point.y);

        if (minimum == right)
            return new Vector2(rect.xMax + 0.01f, point.y);

        if (minimum == bottom)
            return new Vector2(point.x, rect.yMin - 0.01f);

        return new Vector2(point.x, rect.yMax + 0.01f);
    }
}