using UnityEngine;

public partial class NightDefenseTester
{
    private void RestoreCampaignOutcome()
    {
        string mode = RunCampaign.Data.mode;
        if (mode == "Advance" || mode == "Victory")
        { state = NightState.Settlement; stageCleared = resultRecorded = settlementOpened = true; }
        else if (mode == "NightSettlement")
        { state = NightState.Settlement; stageCleared = false; resultRecorded = settlementOpened = true; }
        else if (mode == "Retreat" || mode == "Defeat")
        { state = NightState.Defeat; resultRecorded = true; }
    }
#if UNITY_EDITOR
    [ContextMenu("테스트/M7 현재 지역 즉시 클리어")]
    private void TestCampaignClear()
    {
        if (!CanRunCampaignTest()) return;
        if (IsDay || IsWarning) BeginNight();
        if (IsRunning) TryDamageRift(int.MaxValue);
        else Debug.LogWarning("[M7 테스트] 밤 전투를 시작할 수 없습니다. 현재 단계와 균열 배치 가능 여부를 확인해 주세요.", this);
    }
    [ContextMenu("테스트/M7 캠프 붕괴")]
    private void TestCampaignDefeat()
    {
        if (!CanRunCampaignTest()) return;
        if (IsDay || IsWarning) BeginNight();
        if (IsRunning) { currentBaseHp = 0; FinishNight(NightState.Defeat); }
        else Debug.LogWarning("[M7 테스트] 밤 전투를 시작할 수 없습니다. 현재 단계와 균열 배치 가능 여부를 확인해 주세요.", this);
    }

    private bool CanRunCampaignTest()
    {
        // Inspector 메뉴를 누르면 GameView 포커스를 잃으므로 월드의 포커스 정지는 검사하지 않는다.
        // 실제 선택 화면·종료 메뉴·지역 이동 중의 결과 변경은 계속 차단한다.
        if (Application.isPlaying && isActiveAndEnabled && IsInitialized && !isChangingScene &&
            RunCampaign.Data.mode == "Playing" && !RunCampaign.IsTravelling &&
            !GameExitUI.IsMenuOpen && !M7CampaignUI.IsModalOpen) return true;
        Debug.LogWarning("[M7 테스트] Play 초기화 후 선택 창·종료 메뉴를 닫고, 진행 중인 지역에서 실행해 주세요. 지역 이동·결과 확정 중에는 사용할 수 없습니다.", this);
        return false;
    }
#endif
}
