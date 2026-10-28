using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 선택한 박자를 표시등과 안내 문구로 보여 준다.
// 후보 선택과 박자 위치 계산은 MusicBeatSelection이 담당한다.
public class BeatPhasePreview : MonoBehaviour
{
    [SerializeField]
    private MusicBeatSelection beatSelection;

    [SerializeField]
    private MusicPlayback musicPlayback;

    [SerializeField]
    private Image beatLight;

    [SerializeField]
    private TMP_Text phaseText;

    [SerializeField]
    private Color idleColor =
        new Color(0.12f, 0.16f, 0.22f, 1.0f);

    [SerializeField]
    private Color pulseColor =
        new Color(0.25f, 0.85f, 1.0f, 1.0f);

    private void Awake()
    {
        if (beatSelection == null ||
            musicPlayback == null ||
            beatLight == null ||
            phaseText == null)
        {
            Debug.LogError(
                "BeatPhasePreview의 박자 선택, 음악 재생기, " +
                "표시등과 글자를 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        ShowIdle("박자 위치 분석 대기");
    }

    private void Update()
    {
        if (!beatSelection.isActiveAndEnabled)
        {
            ShowIdle("박자 선택 컴포넌트가 비활성화되어 있습니다.");
            return;
        }

        if (!beatSelection.IsBpmAnalysisComplete)
        {
            ShowIdle("BPM 종합 결과를 기다리고 있습니다.");
            return;
        }

        if (beatSelection.HasNoUsableBeat)
        {
            ShowIdle(
                "사용할 수 있는 박자 위치를 찾지 못했습니다.\n" +
                "‘음원 다시 선택’을 눌러 다른 곡을 선택해 주세요.");

            return;
        }

        if (!beatSelection.IsPhaseAnalysisComplete)
        {
            ShowIdle(
                $"박자 위치 계산 중 " +
                $"{beatSelection.CalculatedCandidateCount}" +
                $"/{beatSelection.CandidateCount}");

            return;
        }

        if (!beatSelection.TryGetSelectedPhase(
                out BeatPhaseEstimator.Result estimate))
        {
            ShowIdle(
                $"후보 {beatSelection.SelectedCandidateIndex + 1}: " +
                "위치 추정 불가\n다른 후보를 선택해 주세요.");

            return;
        }

        if (musicPlayback.IsPaused)
        {
            ShowIdle("일시정지");
            return;
        }

        if (!musicPlayback.TryGetSongTime(out double songTime))
        {
            ShowIdle(
                musicPlayback.HasNaturallyEnded
                    ? "곡 종료"
                    : "재생 대기 / 정지");

            return;
        }

        double elapsed = songTime - estimate.PhaseSeconds;
        float brightness = 0.0f;

        if (elapsed >= 0.0)
        {
            double period = estimate.SecondsPerBeat;

            double timeSinceBeat =
                elapsed - Math.Floor(elapsed / period) * period;

            double flashDuration =
                Math.Min(0.09, period * 0.25);

            brightness = Mathf.Clamp01(
                (float)(1.0 - timeSinceBeat / flashDuration));
        }

        beatLight.color = Color.Lerp(
            idleColor,
            pulseColor,
            brightness);

        SetMessage("음악의 박자와 불빛이 맞는지 확인해 주세요.");
    }

    private void ShowIdle(string message)
    {
        beatLight.color = idleColor;
        SetMessage(message);
    }

    private void SetMessage(string message)
    {
        if (phaseText.text != message)
        {
            phaseText.text = message;
        }
    }

    private void OnDisable()
    {
        if (beatLight != null)
        {
            beatLight.color = idleColor;
        }
    }
}