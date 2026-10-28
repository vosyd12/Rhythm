using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MusicVolumeUI : MonoBehaviour
{
    [SerializeField]
    private AudioSource musicSource;

    [SerializeField]
    private Slider volumeSlider;

    [SerializeField]
    private TMP_Text volumeText;

    private void Awake()
    {
        if (musicSource == null ||
            volumeSlider == null ||
            volumeText == null)
        {
            Debug.LogError(
                "Music Source, Volume Slider, " +
                "Volume Text를 연결해 주세요.");

            enabled = false;
            return;
        }
    }

    private void OnEnable()
    {
        // 씬에 들어왔을 때 이전에 저장한 볼륨을 가져온다.
        float volume = RunAudioSettings.MusicVolume;

        // 슬라이더 위치만 복원한다.
        // 값을 변경했다는 이벤트는 발생시키지 않는다.
        volumeSlider.SetValueWithoutNotify(volume);

        // 실제 음량과 표시 텍스트도 같은 값으로 맞춘다.
        ApplyVolume(volume);

        // 슬라이더 값이 바뀔 때 호출할 함수를 등록한다.
        volumeSlider.onValueChanged.AddListener(
            HandleVolumeChanged);
    }

    private void OnDisable()
    {
        if (volumeSlider != null)
        {
            // 비활성화할 때 등록한 함수를 해제한다.
            // 다시 활성화해도 같은 함수가 중복 등록되지 않게 한다.
            volumeSlider.onValueChanged.RemoveListener(
                HandleVolumeChanged);
        }
    }

    private void HandleVolumeChanged(float volume)
    {
        // 슬라이더가 전달한 값을 공통 설정에 저장한다.
        RunAudioSettings.SetMusicVolume(volume);

        // 범위 제한까지 마친 실제 저장값을 적용한다.
        ApplyVolume(RunAudioSettings.MusicVolume);
    }

    private void ApplyVolume(float volume)
    {
        // 재생 중인 음악의 출력 음량에 바로 적용한다.
        musicSource.volume = volume;

        // 0~1 값을 화면에 표시할 0~100으로 변환한다.
        // 예: 0.42 × 100 = 42 → "VOLUME 42%"
        int percent = Mathf.RoundToInt(volume * 100.0f);

        volumeText.text = $"VOLUME {percent}%";
    }
}