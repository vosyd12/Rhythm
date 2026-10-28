using System.Collections.Generic;
using UnityEngine;

// 현재 게임 실행 동안의 배치 기록이다. 씬 오브젝트 참조는 저장하지 않는다.
// Definition은 프리팹과 종류 설정을 가진 에셋 참조다.
public sealed class CampPlacedBuildingData
{
    public int SlotId { get; }
    public CampBuildingDefinition Definition { get; }
    public Vector3 Position { get; }
    public Vector2 Size { get; }
    public CampBuildingKind Kind { get; }
    public int WoodCost { get; }
    public int TotalWork { get; }
    public int WorkPerStep { get; }

    public CampPlacedBuildingData(int id, CampBuildingDefinition definition,
        Vector3 position)
    {
        SlotId = id;
        Definition = definition;
        Position = position;
        Size = definition.FootprintSize;
        Kind = definition.Kind;
        WoodCost = definition.WoodCost;
        TotalWork = definition.TotalWork;
        WorkPerStep = definition.WorkPerStep;
    }
}

public static class RunCampPlacedBuildings
{
    private static readonly Dictionary<int, CampPlacedBuildingData> placed =
        new Dictionary<int, CampPlacedBuildingData>();

    // 기존의 작은 고정 ID와 구분하되, 실제 중복 검사도 수행한다.
    private static int nextId = 1000000;
    public static IEnumerable<CampPlacedBuildingData> All => placed.Values;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize() => ResetForNewRun();

    public static void ResetForNewRun()
    {
        placed.Clear();
        nextId = 1000000;
    }

    public static int AllocateId()
    {
        while (nextId < int.MaxValue)
        {
            int id = nextId++;
            if (!placed.ContainsKey(id) &&
                !RunCampConstruction.HasStarted(id) &&
                !CampConstructionSlotView.TryGetActiveSlot(id, out _))
                return id;
        }

        throw new System.InvalidOperationException("배치 ID를 더 할당할 수 없습니다.");
    }

    public static void Add(CampPlacedBuildingData data)
    {
        placed.Add(data.SlotId, data);
    }
}
