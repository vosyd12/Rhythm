using UnityEngine;

// 한 번의 게임 실행 동안 사용하는 재료 보유량을 관리한다.
public static class RunResources
{
    // 외부에서는 조회하고, 변경은 이 클래스의 함수를 통해서 한다.
    public static int StoredWood { get; private set; }
    public static int StoredGold { get; private set; }

    // 게임 실행을 시작할 때 보유량을 초기화한다.
    // 일반적인 씬 전환 때는 호출되지 않는다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]

    private static void Initialize()
    {
        StoredWood = 0;
        StoredGold = 0;
    }

    // 정산한 목재를 보유량에 추가한다.
    public static void AddWood(int amount)
    {
        if (amount < 0)
        {
            Debug.LogWarning("지급할 목재 수량은 음수일 수 없습니다.");
            return;
        }

        if (!TryAddWood(amount))
        {
            Debug.LogWarning("목재 지급 후 보유량이 정수 범위를 초과합니다.");
        }
    }

    // 지급 실패 시 기존 보유량을 보존한다. 가격/보상 정책은 호출자가 결정한다.
    public static bool TryAddWood(int amount)
    {
        if (amount < 0 || amount > int.MaxValue - StoredWood) return false;
        StoredWood += amount;
        return true;
    }

    public static bool TryAddGold(int amount)
    {
        if (amount < 0 || amount > int.MaxValue - StoredGold) return false;
        StoredGold += amount;
        return true;
    }

    public static bool TrySpendGold(int amount)
    {
        if (amount <= 0 || StoredGold < amount) return false;
        StoredGold -= amount;
        return true;
    }

    // 승인된 단가를 받아 목재 차감과 골드 지급을 한 번에 확정한다.
    // 판매 UI·자동 판매 여부·실제 단가는 이 함수에서 정하지 않는다.
    public static bool TrySellWood(int amount, int goldPerWood)
    {
        if (amount <= 0 || goldPerWood <= 0 || StoredWood < amount) return false;
        long earned = (long)amount * goldPerWood;
        if (earned > int.MaxValue - StoredGold) return false;
        StoredWood -= amount;
        StoredGold += (int)earned;
        return true;
    }

    // 목재 소비를 시도한다.
    // true: 차감 완료
    // false: 차감 실패, 기존 보유량 유지
    public static bool TrySpendWood(int amount)
    {
        if (amount <= 0)
        {
            Debug.LogWarning("소비할 목재 수량은 1 이상이어야 합니다.");
            return false;
        }

        // 목재가 부족하면 보유량을 변경하지 않는다.
        if (StoredWood < amount)
        {
            return false;
        }

        StoredWood -= amount;
        return true;
    }

    // NEW RUN에서 목재 보유량을 새 게임 상태로 되돌린다.
    public static void ResetForNewRun()
    {
        // 게임 실행을 시작할 때 사용하는 초기화 규칙을 재사용한다.
        Initialize();
    }
}