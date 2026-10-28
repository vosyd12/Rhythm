using UnityEngine;

public partial class MusicPlayback
{
    public bool IsWorldPlayback { get; set; }
    // 월드 정지와 오디오의 DSP 시계를 함께 멈추고, 포커스 복귀 시 함께 재개한다.
    public void SyncWorldPause(bool pause)
    {
        if (!IsWorldPlayback) return;
        if (pause && !IsPaused)
        { IsPaused = true; AudioListener.pause = true; }
        else if (!pause && IsPaused) ReleasePause();
    }
    public bool ReplayWorldMusic()
    {
        if (!IsWorldPlayback || !isInitialized || !HasNaturallyEnded || !patternGenerator.IsGenerationComplete) return false;
        RequestPlayback(true);
        return true;
    }
}
