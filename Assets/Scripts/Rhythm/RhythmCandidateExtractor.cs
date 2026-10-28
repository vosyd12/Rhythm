using System;
using System.Collections.Generic;
using UnityEngine;

// 음량과 증가량 기록에서 리듬 후보를 찾는다.
// 실제 노트 종류나 프레이즈 배치는 결정하지 않는다.
public sealed class RhythmCandidateExtractor
{
    // 같은 후보의 시간과 강도를 하나로 보관한다.
    public struct Candidate
    {
        public double Time { get; }
        public float Strength { get; }

        public Candidate(double time, float strength)
        {
            Time = time;
            Strength = strength;
        }
    }

    private readonly List<Candidate> candidates =
        new List<Candidate>();

    // 0번 창에는 비교할 이전 창이 없다.
    private int nextWindow = 1;

    public IReadOnlyList<Candidate> Candidates => candidates;

    public void Reset()
    {
        candidates.Clear();
        nextWindow = 1;
    }

    // 마지막 창은 다음 창과 비교할 수 없으므로 검사 대상에서 제외한다.
    public bool HasProcessedAvailableWindows(int windowCount)
    {
        return nextWindow >= Math.Max(1, windowCount - 1);
    }

    public bool TryRefresh(
        IReadOnlyList<float> rmsValues,
        IReadOnlyList<float> riseValues,
        double hopSeconds,
        float silenceRms,
        float minimumStrength,
        out string failureReason)
    {
        failureReason = null;

        if (!(hopSeconds > 0.0) ||
            rmsValues.Count == 0 ||
            rmsValues.Count != riseValues.Count)
        {
            failureReason =
                "RMS 기록과 증가량 기록을 확인해 주세요. " +
                "AudioAnalysisTester의 AppendWindowAnalysis 연결이 필요합니다.";

            return false;
        }

        int contextWindows = Math.Max(
            1,
            (int)Math.Round(1.0 / hopSeconds));

        // 다음 창까지 준비된 후보만 검사한다.
        // 추가 분석으로 기록이 늘어나면 이전 위치에서 이어서 처리한다.
        while (nextWindow + 1 < rmsValues.Count)
        {
            int index = nextWindow;
            nextWindow++;

            float currentRms = rmsValues[index];
            float rise = riseValues[index];

            if (currentRms < silenceRms || rise <= 0.0f)
            {
                continue;
            }

            float previousRise = riseValues[index - 1];
            float nextRise = riseValues[index + 1];

            if (rise <= previousRise || rise < nextRise)
            {
                continue;
            }

            int firstIndex = Math.Max(0, index - contextWindows);

            double rmsSum = 0.0;

            for (int i = firstIndex; i < index; i++)
            {
                rmsSum += rmsValues[i];
            }

            int count = index - firstIndex;
            float averageRms = (float)(rmsSum / count);

            float strength =
                rise / Mathf.Max(0.0001f, averageRms);

            if (strength < minimumStrength)
            {
                continue;
            }

            double candidateTime = (index + 0.5) * hopSeconds;

            candidates.Add(new Candidate(candidateTime, strength));
        }

        return true;
    }
}