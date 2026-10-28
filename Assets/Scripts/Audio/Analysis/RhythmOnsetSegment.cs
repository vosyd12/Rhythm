using System;
using System.Collections.Generic;

// 한 분석 구간의 소리 변화량과 곡 기준 시각을 보관한다.
public sealed class RhythmOnsetSegment
{
    // Strengths[0]에 대응하는 곡 기준 시각.
    // 첫 박자 시각을 뜻하는 것은 아니다.
    public double FirstSampleTimeSeconds { get; }

    // 소리 변화량을 계산한 시각 사이의 간격.
    public double SampleIntervalSeconds { get; }

    // 주변 평균 제거와 정규화까지 끝낸 소리 변화량.
    public IReadOnlyList<double> Strengths { get; }

    public double LastSampleTimeSeconds =>
        FirstSampleTimeSeconds +
        (Strengths.Count - 1) * SampleIntervalSeconds;

    public RhythmOnsetSegment(
        double firstSampleTimeSeconds,
        double sampleIntervalSeconds,
        double[] strengths)
    {
        FirstSampleTimeSeconds = firstSampleTimeSeconds;
        SampleIntervalSeconds = sampleIntervalSeconds;

        // 이후 BPM 계산에서 원본 배열의 평균을 빼거나,
        // 다음 구간을 준비해도 저장된 결과가 변하지 않도록 복사한다.
        double[] copiedStrengths =
            (double[])strengths.Clone();

        Strengths = Array.AsReadOnly(copiedStrengths);
    }
}