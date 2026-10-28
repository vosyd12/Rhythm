using System;
using System.Collections.Generic;
using UnityEngine;

// 소리의 지속성을 검사해 Hold 가능 여부와 종료 시각을 계산한다.
// 노트 배치, 손 점유, 추가 분석 요청은 생성기가 담당한다.
public static class RhythmHoldEvaluator
{
    public enum Decision
    {
        Tap,
        Hold,
        WaitingForAnalysis
    }

    public struct Settings
    {
        public int MinimumBeats;
        public int MaximumBeats;
        public float SustainRatio;
        public float QuietSeconds;
        public float MinimumSustainCoverage;
        public float SilenceRms;
        public double EndSafetySeconds;
    }

    private const double TimeToleranceSeconds = 0.000001;

    public static Decision Evaluate(
        IReadOnlyList<float> rmsValues,
        double hopSeconds,
        bool isAnalysisComplete,
        double analyzedUntilSeconds,
        double candidateTime,
        double startTime,
        double secondsPerBeat,
        double clipDuration,
        Settings settings,
        out double holdEndTime,
        out double requiredAnalysisTime)
    {
        holdEndTime = startTime;
        requiredAnalysisTime = 0.0;

        double latestEndTime = Math.Min(
            startTime + settings.MaximumBeats * secondsPerBeat,
            clipDuration - settings.EndSafetySeconds);

        if (latestEndTime - startTime + TimeToleranceSeconds <
            settings.MinimumBeats * secondsPerBeat)
        {
            return Decision.Tap;
        }

        requiredAnalysisTime = Math.Min(
            clipDuration,
            latestEndTime + settings.QuietSeconds + hopSeconds);

        if (!isAnalysisComplete &&
            analyzedUntilSeconds + TimeToleranceSeconds <
            requiredAnalysisTime)
        {
            return Decision.WaitingForAnalysis;
        }

        int firstWindow =
            (int)Math.Floor(candidateTime / hopSeconds);

        if (firstWindow < 0 || firstWindow >= rmsValues.Count)
        {
            return Decision.Tap;
        }

        float referenceRms = 0.0f;

        int referenceEnd = Math.Min(
            rmsValues.Count,
            firstWindow + 2);

        for (int i = firstWindow; i < referenceEnd; i++)
        {
            referenceRms = Mathf.Max(referenceRms, rmsValues[i]);
        }

        if (referenceRms < settings.SilenceRms)
        {
            return Decision.Tap;
        }

        float sustainThreshold = Mathf.Max(
            settings.SilenceRms,
            referenceRms * settings.SustainRatio);

        int firstQuietWindow = -1;
        double detectedEndTime = latestEndTime;

        for (int i = firstWindow; i < rmsValues.Count; i++)
        {
            double windowStartTime = i * hopSeconds;

            if (windowStartTime >
                latestEndTime + settings.QuietSeconds + hopSeconds)
            {
                break;
            }

            if (rmsValues[i] < sustainThreshold)
            {
                if (firstQuietWindow < 0)
                {
                    firstQuietWindow = i;
                }

                double quietDuration =
                    (i - firstQuietWindow + 1) * hopSeconds;

                if (quietDuration + TimeToleranceSeconds >=
                    settings.QuietSeconds)
                {
                    detectedEndTime = Math.Min(
                        latestEndTime,
                        firstQuietWindow * hopSeconds);

                    break;
                }
            }
            else
            {
                firstQuietWindow = -1;
            }
        }

        // 소리가 유지된 시간을 정수 박자 길이로 내린다.
        int durationBeats = (int)Math.Floor(
            (detectedEndTime - startTime + TimeToleranceSeconds) /
            secondsPerBeat);

        durationBeats = Math.Min(
            durationBeats,
            settings.MaximumBeats);

        if (durationBeats < settings.MinimumBeats)
        {
            return Decision.Tap;
        }

        double proposedEndTime =
            startTime + durationBeats * secondsPerBeat;

        int inspectedWindows = 0;
        int sustainedWindows = 0;

        int coverageStartWindow = Math.Max(
            firstWindow,
            (int)Math.Ceiling(startTime / hopSeconds - 0.5));

        for (int i = coverageStartWindow; i < rmsValues.Count; i++)
        {
            double windowCenterTime = (i + 0.5) * hopSeconds;

            if (windowCenterTime >= proposedEndTime)
            {
                break;
            }

            inspectedWindows++;

            if (rmsValues[i] >= sustainThreshold)
            {
                sustainedWindows++;
            }
        }

        if (inspectedWindows == 0)
        {
            return Decision.Tap;
        }

        float sustainCoverage =
            (float)sustainedWindows / inspectedWindows;

        if (sustainCoverage < settings.MinimumSustainCoverage)
        {
            return Decision.Tap;
        }

        holdEndTime = proposedEndTime;
        return Decision.Hold;
    }
}