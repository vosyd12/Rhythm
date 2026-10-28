using UnityEngine;

// 마지막으로 로드에 성공한 음원 경로를 기억하고,
// 게임을 다시 실행해도 해당 경로를 복원한다.
public static class RunMusicSelection
{
    // 마지막 음원 경로를 구분하는 저장 항목 이름.
    private const string LastLoadedPathKey =
        "RhythmDefense.LastLoadedPath";

    public static string LastLoadedPath { get; private set; }
        = string.Empty;

    public static bool HasLoadedPath =>
        !string.IsNullOrEmpty(LastLoadedPath);

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        // 게임 실행 시 이전에 저장한 경로를 읽는다.
        // 저장된 경로가 없으면 빈 문자열을 사용한다.
        LastLoadedPath = PlayerPrefs.GetString(
            LastLoadedPathKey,
            string.Empty);
    }

    // 로더가 음원을 정상적으로 얻고 데이터 검사를 통과한 뒤 호출한다.
    public static void SaveLoadedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Debug.LogWarning("저장할 음원 경로가 비어 있습니다.");
            return;
        }

        // 현재 실행 중인 게임에서 사용할 경로를 갱신한다.
        LastLoadedPath = path;

        // 다음 실행에서도 읽을 수 있도록 경로 문자열을 기록한다.
        PlayerPrefs.SetString(
            LastLoadedPathKey,
            LastLoadedPath);

        // 현재 로더에서는 음악 재생을 준비하기 전에 이 함수가 호출된다.
        // 로드에 성공한 시점에 저장을 확정한다.

        // Save는 경로뿐 아니라 PlayerPrefs에 기록된
        // 다른 변경 사항도 함께 저장한다.
        PlayerPrefs.Save();
    }
}