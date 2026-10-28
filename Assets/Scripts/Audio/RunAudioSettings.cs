using UnityEngine;

// 현재 실행 중인 게임에서 사용할 음량을 보관하고,
// 다음 실행에서도 복원할 수 있도록 PlayerPrefs에 기록한다.
public static class RunAudioSettings
{
    // PlayerPrefs에서 음량을 구분하는 저장 항목 이름.
    // 저장할 때와 읽을 때 같은 이름을 사용해야 한다.
    private const string MusicVolumeKey =
        "RhythmDefense.MusicVolume";

    // 저장된 설정이 없을 때 사용할 기본 음량.
    private const float DefaultMusicVolume = 1.0f;

    public static float MusicVolume { get; private set; }
        = DefaultMusicVolume;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        // 게임 실행을 시작할 때 저장된 음량을 읽는다.
        // 해당 항목이 없으면 두 번째 인수인 기본값을 사용한다.
        float savedVolume = PlayerPrefs.GetFloat(
            MusicVolumeKey,
            DefaultMusicVolume);

        // 복원한 값도 AudioSource의 음량 범위인 0~1로 제한한다.
        MusicVolume = Mathf.Clamp01(savedVolume);
    }

    public static void SetMusicVolume(float volume)
    {
        // 현재 실행 중인 게임에서 사용할 음량을 갱신한다.
        // 예: -0.2 → 0, 0.4 → 0.4, 1.3 → 1
        MusicVolume = Mathf.Clamp01(volume);

        // 다음 실행에 사용할 값도 PlayerPrefs에 기록한다.
        PlayerPrefs.SetFloat(MusicVolumeKey, MusicVolume);

        // 슬라이더를 움직이면 이 함수가 여러 번 호출될 수 있다.
        // 실제 저장을 확정하는 Save는 종료 시점에 호출한다.
    }
}