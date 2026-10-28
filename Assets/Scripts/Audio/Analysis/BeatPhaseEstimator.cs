using System;
using System.Collections.Generic;

// 고정 BPM을 가정하고 박자 기준 위치를 비교한다.
// 음악 재생이나 UI 표시는 담당하지 않는다.
public static class BeatPhaseEstimator
{
    // 기준 위치를 탐색할 목표 간격.
    // 탐색 간격이며 실제 검출 정확도를 보장하는 값은 아니다.
    private const double TargetPhaseStepSeconds = 0.005;

    public readonly struct Result
    {
        public double Bpm { get; }

        // 0 이상, 한 박자 길이 미만의 기준 위치.
        public double PhaseSeconds { get; }

        public double SecondsPerBeat => 60.0 / Bpm;

        public Result(double bpm, double phaseSeconds)
        {
            Bpm = bpm;
            PhaseSeconds = phaseSeconds;
        }
    }

    public static bool TryEstimate(
        IReadOnlyList<RhythmOnsetSegment> segments,
        double bpm,
        out Result result)
    {
        result = default;

        // 현재 AudioBpmAnalyzer가 지원하는 BPM 범위.
        if (segments == null ||
            segments.Count == 0 ||
            double.IsNaN(bpm) ||
            double.IsInfinity(bpm) ||
            bpm < 40.0 ||
            bpm > 240.0)
        {
            return false;
        }

        double period = 60.0 / bpm;

        // 한 박자 전체를 약 5ms 간격으로 나누어 검사한다.
        int phaseCount = Math.Max(
            1,
            (int)Math.Ceiling(
                period / TargetPhaseStepSeconds));

        double phaseStep = period / phaseCount;

        double bestScore = double.NegativeInfinity;
        double bestPhase = 0.0;

        double scoreSum = 0.0;
        int validPhaseCount = 0;

        for (int phaseIndex = 0;
             phaseIndex < phaseCount;
             phaseIndex++)
        {
            double phase = phaseIndex * phaseStep;

            double strengthSum = 0.0;
            int sampledBeatCount = 0;

            foreach (RhythmOnsetSegment segment in segments)
            {
                if (segment.Strengths.Count < 3)
                {
                    continue;
                }

                double interval =
                    segment.SampleIntervalSeconds;

                // 예상 박자의 앞뒤 값도 읽으므로
                // 구간 양 끝에서 한 칸씩 여유를 둔다.
                double firstTime =
                    segment.FirstSampleTimeSeconds + interval;

                double lastTime =
                    segment.LastSampleTimeSeconds - interval;

                // phase + n × period가 구간 안에 들어오는
                // 첫 번째 정수 n을 찾는다.
                long firstBeatIndex = (long)Math.Ceiling(
                    (firstTime - phase) / period);

                for (long beatIndex = firstBeatIndex; ; beatIndex++)
                {
                    // 시간을 계속 더하지 않고
                    // 기준 위치와 번호로 매번 계산한다.
                    double beatTime =
                        phase + beatIndex * period;

                    if (beatTime > lastTime)
                    {
                        break;
                    }

                    // 중심 50%, 앞뒤 각 25%로 비교한다.
                    // 분석 값 한 칸에만 지나치게 의존하지 않도록 한다.
                    double strength =
                        SampleStrength(segment, beatTime - interval) * 0.25 +
                        SampleStrength(segment, beatTime) * 0.50 +
                        SampleStrength(segment, beatTime + interval) * 0.25;

                    strengthSum += strength;
                    sampledBeatCount++;
                }
            }

            if (sampledBeatCount == 0)
            {
                continue;
            }

            // 박자를 많이 검사한 위치가 무조건 유리하지 않도록
            // 합계 대신 박자 하나당 평균을 비교한다.
            double score =
                strengthSum / sampledBeatCount;

            scoreSum += score;
            validPhaseCount++;

            if (score > bestScore)
            {
                bestScore = score;
                bestPhase = phase;
            }
        }

        if (validPhaseCount == 0)
        {
            return false;
        }

        double averageScore =
            scoreSum / validPhaseCount;

        // 변화가 없거나 모든 위치의 점수가 사실상 같으면
        // 특정 위치를 찾았다고 처리하지 않는다.
        if (bestScore <= 0.000000001 ||
            bestScore - averageScore <= 0.000000001)
        {
            return false;
        }

        result = new Result(bpm, bestPhase);
        return true;
    }

    private static double SampleStrength(
        RhythmOnsetSegment segment,
        double songTime)
    {
        // 곡 기준 시각을 배열 안의 위치로 변환한다.
        double position =
            (songTime - segment.FirstSampleTimeSeconds) /
            segment.SampleIntervalSeconds;

        int leftIndex = Math.Max(
            0,
            Math.Min(
                segment.Strengths.Count - 1,
                (int)Math.Floor(position)));

        int rightIndex = Math.Min(
            leftIndex + 1,
            segment.Strengths.Count - 1);

        double blend = Math.Max(
            0.0,
            Math.Min(1.0, position - leftIndex));

        // 두 분석 시각 사이의 값은 선형 보간한다.

        // 예: 배열 위치가 10.25라면
        // 10번 값 75% + 11번 값 25%.
        return
            segment.Strengths[leftIndex] * (1.0 - blend) +
            segment.Strengths[rightIndex] * blend;
    }
}