using System;
using System.Collections.Generic;

// 여러 구간에서 나온 BPM 후보를 비교한다.
// 음원 읽기나 FFT 계산은 담당하지 않는다.
public static class BpmConsensusCalculator
{
    // 어느 구간에서 어떤 후보가 나왔는지 저장한다.
    public readonly struct Observation
    {
        public int SegmentIndex { get; }
        public double Bpm { get; }
        public double Score { get; }

        public Observation(
            int segmentIndex,
            double bpm,
            double score)
        {
            SegmentIndex = segmentIndex;
            Bpm = bpm;
            Score = score;
        }
    }

    // 여러 구간의 후보를 묶은 결과.
    public readonly struct Result
    {
        public double Bpm { get; }
        public int SupportingSegments { get; }
        public double AverageScore { get; }

        public Result(
            double bpm,
            int supportingSegments,
            double averageScore)
        {
            Bpm = bpm;
            SupportingSegments = supportingSegments;
            AverageScore = averageScore;
        }
    }

    private sealed class CandidateGroup
    {
        // 그룹을 처음 만들 때의 기준 BPM.
        // 기준을 계속 이동시키지 않아 연쇄적으로 범위가 커지는 것을 막는다.
        public double AnchorBpm { get; }

        // 키: 분석 구간 번호.
        // 값: 해당 구간에서 이 그룹에 들어온 가장 강한 후보.
        //
        // 같은 구간에서 비슷한 후보가 여러 개 들어와도
        // Dictionary의 같은 키를 사용하므로 한 번만 집계한다.
        private readonly Dictionary<int, Observation> votes =
            new Dictionary<int, Observation>();

        public CandidateGroup(double anchorBpm)
        {
            AnchorBpm = anchorBpm;
        }

        public void Add(Observation observation)
        {
            if (votes.TryGetValue(
                    observation.SegmentIndex,
                    out Observation previous) &&
                previous.Score >= observation.Score)
            {
                return;
            }

            votes[observation.SegmentIndex] = observation;
        }

        public Result CreateResult()
        {
            double weightedBpmSum = 0.0;
            double weightSum = 0.0;
            double scoreSum = 0.0;

            foreach (Observation observation in votes.Values)
            {
                // 반복 점수가 높은 후보에 조금 더 큰 비중을 준다.
                // 점수가 0이어도 분모가 0이 되지 않도록 최소값을 둔다.
                double weight = Math.Max(
                    0.000001,
                    observation.Score);

                weightedBpmSum += observation.Bpm * weight;
                weightSum += weight;
                scoreSum += observation.Score;
            }

            // 대표 BPM = Σ(BPM × 가중치) / Σ가중치.
            //
            // 후보를 요약하는 값이며,
            // 실제 플레이에 바로 적용할 정밀 BPM을 확정하는 계산은 아니다.
            double representativeBpm =
                weightedBpmSum / weightSum;

            // 이 그룹을 지지한 구간들의 평균 반복 점수.
            // 정답 확률을 의미하지 않는다.
            double averageScore =
                scoreSum / votes.Count;

            return new Result(
                representativeBpm,
                votes.Count,
                averageScore);
        }
    }

    public static List<Result> Calculate(
        IReadOnlyList<Observation> observations,
        double mergeTolerance)
    {
        List<Observation> sorted =
            new List<Observation>(observations.Count);

        for (int i = 0; i < observations.Count; i++)
        {
            sorted.Add(observations[i]);
        }

        // 강한 후보를 먼저 그룹의 기준으로 사용한다.
        sorted.Sort((a, b) =>
        {
            int scoreOrder = b.Score.CompareTo(a.Score);

            if (scoreOrder != 0)
            {
                return scoreOrder;
            }

            int segmentOrder =
                a.SegmentIndex.CompareTo(b.SegmentIndex);

            return segmentOrder != 0
                ? segmentOrder
                : a.Bpm.CompareTo(b.Bpm);
        });

        List<CandidateGroup> groups =
            new List<CandidateGroup>();

        foreach (Observation observation in sorted)
        {
            CandidateGroup nearestGroup = null;
            double nearestDifference = double.PositiveInfinity;

            foreach (CandidateGroup group in groups)
            {
                // 상대 차이 = |후보 BPM - 기준 BPM| / 기준 BPM.
                //
                // 기준 120, 후보 121:
                // 1 / 120 ≈ 0.00833 → 약 0.83%.
                double difference =
                    Math.Abs(observation.Bpm - group.AnchorBpm) /
                    group.AnchorBpm;

                if (difference <= mergeTolerance &&
                    difference < nearestDifference)
                {
                    nearestGroup = group;
                    nearestDifference = difference;
                }
            }

            if (nearestGroup == null)
            {
                nearestGroup =
                    new CandidateGroup(observation.Bpm);

                groups.Add(nearestGroup);
            }

            nearestGroup.Add(observation);
        }

        List<Result> results = new List<Result>();

        foreach (CandidateGroup group in groups)
        {
            results.Add(group.CreateResult());
        }

        results.Sort((a, b) =>
        {
            // 먼저 서로 다른 몇 개 구간에서 나왔는지 비교한다.
            int supportOrder =
                b.SupportingSegments.CompareTo(a.SupportingSegments);

            if (supportOrder != 0)
            {
                return supportOrder;
            }

            // 구간 수가 같으면 평균 반복 점수를 비교한다.
            int scoreOrder =
                b.AverageScore.CompareTo(a.AverageScore);

            return scoreOrder != 0
                ? scoreOrder
                : b.Bpm.CompareTo(a.Bpm);
        });

        return results;
    }
}