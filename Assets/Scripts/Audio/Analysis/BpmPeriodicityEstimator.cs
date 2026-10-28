using System;
using System.Collections.Generic;

// 음원에서 추출한 변화량으로 BPM 후보를 계산한다.
// 음원 읽기, 분석 진행, 로그와 UI는 담당하지 않는다.
internal static class BpmPeriodicityEstimator
{
    public readonly struct Candidate
    {
        public double Bpm { get; }
        public double Score { get; }

        public Candidate(double bpm, double score)
        {
            Bpm = bpm;
            Score = score;
        }
    }

    // 전달받은 배열 자체를 보정한다.
    // 배열과 설정의 유효성은 호출하는 AudioBpmAnalyzer에서 보장한다.
    public static bool PrepareOnsetEnvelope(
        double[] strengths,
        double sampleIntervalSeconds,
        double localAverageRadiusSeconds)
    {
        int count = strengths.Length;

        int radius = Math.Max(
            1,
            (int)Math.Round(
                localAverageRadiusSeconds / sampleIntervalSeconds));

        // 원본의 누적 합을 먼저 만들어 둔다.
        // 아래에서 strengths를 수정해도 주변 평균은 원본으로 계산된다.
        double[] prefix = new double[count + 1];

        for (int i = 0; i < count; i++)
        {
            prefix[i + 1] = prefix[i] + strengths[i];
        }

        double maximum = 0.0;

        for (int i = 0; i < count; i++)
        {
            int left = Math.Max(0, i - radius);
            int right = Math.Min(count, i + radius + 1);

            double localMean =
                (prefix[right] - prefix[left]) /
                (right - left);

            // 주변보다 두드러진 변화만 남긴다.
            double emphasized = Math.Max(
                0.0,
                strengths[i] - localMean);

            strengths[i] = emphasized;
            maximum = Math.Max(maximum, emphasized);
        }

        if (maximum <= 0.000000000001)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            strengths[i] /= maximum;
        }

        return true;
    }

    // PrepareOnsetEnvelope에 성공한 배열을 전달한다.
    //
    // 반복성 계산을 위해 배열에서 평균을 직접 뺀다.
    // 박자 위치 추정에 사용할 보정 결과는 호출 전에 별도로 보관해야 한다.
    //
    // 반환값은 반복성을 계산할 변화량이 충분했는지를 뜻한다.
    // true여도 점수 기준을 통과한 후보가 없을 수 있다.
    public static bool TryEstimateCandidates(
        double[] strengths,
        int minimumLag,
        int maximumLag,
        double sampleIntervalSeconds,
        double minimumScore,
        List<Candidate> candidates,
        out double highestScore,
        out double diagnosticBpm)
    {
        candidates.Clear();

        highestScore = double.NegativeInfinity;
        diagnosticBpm = 0.0;

        double mean = 0.0;

        for (int i = 0; i < strengths.Length; i++)
        {
            mean += strengths[i];
        }

        mean /= strengths.Length;

        double variationEnergy = 0.0;

        for (int i = 0; i < strengths.Length; i++)
        {
            strengths[i] -= mean;
            variationEnergy += strengths[i] * strengths[i];
        }

        if (variationEnergy < 0.00000001)
        {
            return false;
        }

        double[] scores = new double[maximumLag + 1];
        int highestScoreLag = minimumLag;

        for (int lag = minimumLag; lag <= maximumLag; lag++)
        {
            double score = CalculatePeriodicity(strengths, lag);

            scores[lag] = score;

            if (score > highestScore)
            {
                highestScore = score;
                highestScoreLag = lag;
            }
        }

        // 소수 보정을 적용하기 전, 최고 점수 간격의 진단용 BPM.
        diagnosticBpm =
            60.0 / (highestScoreLag * sampleIntervalSeconds);

        for (int lag = minimumLag; lag <= maximumLag; lag++)
        {
            double score = scores[lag];

            if (score < minimumScore)
            {
                continue;
            }

            // 주변 간격보다 점수가 낮으면 대표 후보에서 제외한다.
            if (lag > minimumLag && score < scores[lag - 1])
            {
                continue;
            }

            if (lag < maximumLag && score < scores[lag + 1])
            {
                continue;
            }

            double refinedLag = lag;

            if (lag > minimumLag && lag < maximumLag)
            {
                double left = scores[lag - 1];
                double center = score;
                double right = scores[lag + 1];

                // 세 점을 포물선으로 근사해 정수 사이의 꼭짓점을 구한다.
                double denominator =
                    left - 2.0 * center + right;

                if (denominator < -0.000000000001)
                {
                    double shift =
                        0.5 * (left - right) / denominator;

                    refinedLag += Math.Max(
                        -0.5,
                        Math.Min(0.5, shift));
                }
            }

            double bpm =
                60.0 / (refinedLag * sampleIntervalSeconds);

            candidates.Add(new Candidate(bpm, score));
        }

        // 점수가 같으면 기존 방식대로 높은 BPM을 먼저 배치한다.
        candidates.Sort((a, b) =>
        {
            int scoreOrder = b.Score.CompareTo(a.Score);

            return scoreOrder != 0
                ? scoreOrder
                : b.Bpm.CompareTo(a.Bpm);
        });

        return true;
    }

    private static double CalculatePeriodicity(
        double[] strengths,
        int lag)
    {
        double productSum = 0.0;
        double currentEnergy = 0.0;
        double shiftedEnergy = 0.0;

        for (int i = lag; i < strengths.Length; i++)
        {
            double current = strengths[i];
            double shifted = strengths[i - lag];

            productSum += current * shifted;

            currentEnergy += current * current;
            shiftedEnergy += shifted * shifted;
        }

        double denominator =
            Math.Sqrt(currentEnergy * shiftedEnergy);

        if (denominator <= 0.000000000001)
        {
            return 0.0;
        }

        return productSum / denominator;
    }
}