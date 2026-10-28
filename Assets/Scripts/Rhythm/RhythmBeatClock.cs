using UnityEngine;

// 음악의 곡 시간을 박자 위치로 변환한다.
// 실제 음악 재생은 MusicPlayback이 담당한다.
public class RhythmBeatClock : MonoBehaviour
{
    [SerializeField]
    private MusicPlayback musicPlayback;

    // 사용자가 박자를 확정할 때 전달한 값을 보관한다.
    // 이번 플레이의 시간·박자 변환에 사용한다.
    private double playbackBpm;
    private double playbackFirstBeatTimeSeconds;

    // 사용자가 이번 플레이의 박자를 확정했는지 나타낸다.
    public bool IsConfigured { get; private set; }

    // 외부에서는 Inspector 원본 값이 아니라
    // 이번 플레이에 확정한 값을 조회한다.
    public double Bpm => playbackBpm;

    public double FirstBeatTimeSeconds =>
        playbackFirstBeatTimeSeconds;

    // 한 박자의 길이 = 60초 / BPM.
    // 예: BPM 120이면 한 박자는 0.5초다.

    // 아직 박자를 확정하지 않았다면 0을 반환한다.
    public double SecondsPerBeat =>
        IsConfigured ? 60.0 / playbackBpm : 0.0;

    public bool CanConfigure =>
        isActiveAndEnabled &&
        musicPlayback != null &&
        !IsConfigured &&
        !musicPlayback.IsGameplayPlayback;

    public bool IsPaused =>
        musicPlayback != null &&
        musicPlayback.IsPaused;

    public bool HasNaturallyEnded =>
        musicPlayback != null &&
        musicPlayback.IsGameplayPlayback &&
        musicPlayback.HasNaturallyEnded;

    private void Awake()
    {
        if (musicPlayback == null)
        {
            Debug.LogError("Music Playback을 연결해 주세요.", this);

            enabled = false;
        }
    }

    // 현재 곡 시간이 기준 박자에서 얼마나 진행됐는지 구한다.

    // 반환 예:
    // 0.0 → 기준 박자에 도착.
    // 0.5 → 첫 박자의 절반을 지남.
    // 1.0 → 다음 박자에 도착.

    // 기준 박자 이전이라면 음수가 나온다.
    public bool TryConfigureForGameplay(
        double bpm,
        double firstBeatTimeSeconds)
    {
        if (!Application.isPlaying || !CanConfigure)
        {
            return false;
        }

        // NaN과 Infinity도 계산에 사용할 수 없으므로 차단한다.
        if (double.IsNaN(bpm) ||
            double.IsInfinity(bpm) ||
            bpm <= 0.0 ||
            double.IsNaN(firstBeatTimeSeconds) ||
            double.IsInfinity(firstBeatTimeSeconds) ||
            firstBeatTimeSeconds < 0.0)
        {
            Debug.LogError(
                "BPM과 기준 박자 시각을 확인해 주세요.",
                this);

            return false;
        }

        double secondsPerBeat = 60.0 / bpm;

        if (double.IsInfinity(secondsPerBeat) ||
            secondsPerBeat <= 0.0)
        {
            Debug.LogError("사용할 수 없는 BPM입니다.", this);
            return false;
        }

        playbackBpm = bpm;
        playbackFirstBeatTimeSeconds = firstBeatTimeSeconds;
        IsConfigured = true;

        Debug.Log(
            $"[플레이 박자 확정] " +
            $"BPM {Bpm:F2} / " +
            $"기준 시각 {FirstBeatTimeSeconds:F3}s / " +
            $"한 박자 {SecondsPerBeat:F3}s",
            this);

        return true;
    }

    // 곡 시간을 소수점이 포함된 박자 위치로 변환한다.
    public bool TryGetBeatPosition(out double beatPosition)
    {
        beatPosition = 0.0;

        if (!isActiveAndEnabled ||
            !IsConfigured ||
            musicPlayback == null ||
            !musicPlayback.IsGameplayPlayback)
        {
            return false;
        }

        if (!musicPlayback.TryGetSongTime(out double songTime))
        {
            return false;
        }

        // 박자 위치 = 기준 시각 이후 경과 시간 / 한 박자 길이.

        // 예: 기준 0.2초, 현재 1.4초, 한 박자 0.5초
        // (1.4 - 0.2) / 0.5 = 2.4박.
        beatPosition =
            (songTime - FirstBeatTimeSeconds) /
            SecondsPerBeat;

        return true;
    }

    // 기존 정수 박자 호출을 그대로 지원한다.
    public double GetBeatTimeSeconds(int beatIndex)
    {
        return GetBeatTimeSeconds((double)beatIndex);
    }

    // 소수점을 포함한 박자 위치를 곡 기준 시각으로 변환한다.
    // 박자를 확정한 뒤 사용한다.

    // 예: 기준 시각 0.2초, BPM 120
    // 1.0박 → 0.2 + 1.0 × 0.5 = 0.7초
    // 1.5박 → 0.2 + 1.5 × 0.5 = 0.95초
    public double GetBeatTimeSeconds(double beatPosition)
    {
        return FirstBeatTimeSeconds +
               beatPosition * SecondsPerBeat;
    }
}