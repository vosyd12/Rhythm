using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MusicInfoUI : MonoBehaviour
{
    [SerializeField]
    private MusicPlayback musicPlayback;

    [SerializeField]
    private TMP_Text musicInfoText;

    // 새로운 재생 요청이 들어왔는지 확인하기 위한 번호.
    private int observedPlaybackId = -1;

    // 마지막으로 확인한 곡 기준 시간.
    // 수동 정지 상태에서는 마지막 표시 시간을 유지한다.
    private double displayedSongTime;

    // 곡 진행 비율에 맞춰 채워질 UI 이미지.
    [SerializeField]
    private Image musicProgressFill;

    private void Awake()
    {
        // 재생기는 음원 로드 전에는 비활성화되어 있으므로
        // 여기서는 필요한 참조가 연결되었는지만 검사한다.
        if (musicPlayback == null ||
            musicInfoText == null ||
            musicProgressFill == null)
        {
            Debug.LogError(
                "Music Playback, Music Info Text, " +
                "Music Progress Fill을 연결해 주세요.");

            enabled = false;
            return;
        }

        // 표시 문자열을 서식 태그로 해석하지 않고
        // 일반 텍스트로 표시한다.
        musicInfoText.richText = false;

        musicProgressFill.fillAmount = 0.0f;
    }

    private void Update()
    {
        // 처음부터 재생하면 PlaybackId가 증가한다.
        // 이전 재생 시간이 남지 않도록 표시 시간을 초기화한다.
        if (observedPlaybackId != musicPlayback.PlaybackId)
        {
            observedPlaybackId = musicPlayback.PlaybackId;
            displayedSongTime = 0.0;
        }

        // 아직 곡 이름이 준비되지 않았다면 표시할 내용이 없다.
        // 컴포넌트는 계속 동작하도록 텍스트만 비운다.
        if (string.IsNullOrEmpty(musicPlayback.SongName))
        {
            musicInfoText.text = string.Empty;
            // 아직 곡이 준비되지 않았다면 진행 막대도 비운다.
            musicProgressFill.fillAmount = 0.0f;
            return;
        }

        if (musicPlayback.HasNaturallyEnded)
        {
            // 정상 종료 후에는 TryGetSongTime이 false를 반환한다.
            // 따라서 종료 상태에서는 전체 길이를 직접 표시한다.
            displayedSongTime = musicPlayback.SongDurationSeconds;
        }
        else if (musicPlayback.TryGetSongTime(out double songTime))
        {
            // 판정 시스템과 같은 곡 기준 시간을 사용한다.
            displayedSongTime = songTime;
        }

        // 예약 재생 전에는 초기값 0초가 유지된다.
        // 수동 정지 후에는 마지막으로 읽은 시간이 유지된다.

        double duration = musicPlayback.SongDurationSeconds;

        // 전체 길이가 준비되지 않았을 때는 0으로 나누지 않는다.
        if (duration <= 0.0)
        {
            musicProgressFill.fillAmount = 0.0f;
        }

        else
        {
            // 시간 텍스트와 동일한 곡 시간을 사용한다.
            // 예: 현재 45초 / 전체 180초 = 0.25
            double progress = displayedSongTime / duration;

            // Image.fillAmount는 float 값을 받으므로 변환한다.
            // Clamp01은 값을 0~1 범위로 제한한다.
            // 예: -0.1 → 0, 0.25 → 0.25, 1.1 → 1
            musicProgressFill.fillAmount =
                Mathf.Clamp01((float)progress);
        }

        // 재생 준비 중 전체 길이가 아직 설정되지 않았다면
        // 길이가 미정이라는 의미로 --:--를 표시한다.
        string durationText = duration > 0.0
            ? FormatTime(duration)
            : "--:--";

        string message =
            $"MUSIC  {musicPlayback.SongName}\n" +
            $"{FormatTime(displayedSongTime)} / {durationText}";

        // 표시 내용이 달라졌을 때만 텍스트에 적용한다.
        if (musicInfoText.text != message)
        {
            musicInfoText.text = message;
        }
    }

    private static string FormatTime(double seconds)
    {
        // 음수는 0으로 제한하고 소수점 이하는 버린다.
        // 예: 125.8초 → 125초
        int wholeSeconds = (int)Math.Max(0.0, seconds);

        // 정수 나눗셈으로 분을 계산한다.
        // 125 / 60 = 2분
        int minutes = wholeSeconds / 60;

        // 나머지 연산으로 분을 제외한 초를 계산한다.
        // 125 % 60 = 5초
        int remainingSeconds = wholeSeconds % 60;

        // 00 형식은 최소 두 자리로 표시한다.
        // 2분 5초 → "02:05"
        return $"{minutes:00}:{remainingSeconds:00}";
    }
}