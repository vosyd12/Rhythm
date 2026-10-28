using System;
using System.Collections.Generic;
using UnityEngine;

// 구간 안의 후보를 비교하여 실제 노트 생성에 사용할 후보를 선택한다.
// 반복 구절의 후보 배치와 이전 선택 기록도 함께 관리한다.
public sealed class RhythmPhraseSelector
{
    public struct Settings
    {
        public double FirstBeatTimeSeconds;
        public double SecondsPerBeat;
        public double EarliestEventTime;
        public double EndSafetySeconds;
        public double InputSafetySeconds;

        public int BeatSubdivisions;
        public int MinimumEventGapBeats;

        public float MaxSnapDistanceBeats;
        public float ConnectionGapBeats;
        public float AccentRatio;
        public float RhythmWeight;

        public bool EnableRepeatedMatching;
        public float RepeatedSimilarity;
        public float RepeatedBonus;
        public int HistoryLimit;
    }

    // 선택된 후보의 관련 정보를 하나로 보관한다.
    public struct SelectedCandidate
    {
        public double Time { get; }
        public float Strength { get; }
        public bool IsAccent { get; }
        public double MinimumGap { get; }

        public SelectedCandidate(
            double time,
            float strength,
            bool isAccent,
            double minimumGap)
        {
            Time = time;
            Strength = strength;
            IsAccent = isAccent;
            MinimumGap = minimumGap;
        }
    }

    private sealed class PhraseHistoryEntry
    {
        public bool[] CandidateSlots;
        public float[] RelativeStrengths;
        public bool[] SelectedSlots;
    }

    private const double TimeToleranceSeconds = 0.000001;

    private readonly List<SelectedCandidate> selectedCandidates =
        new List<SelectedCandidate>();

    private readonly List<int> phraseCandidates =
        new List<int>();

    private readonly List<int> phraseSelectedCandidates =
        new List<int>();

    private readonly Dictionary<int, float> phraseCandidateScores =
        new Dictionary<int, float>();

    private readonly HashSet<int> rhythmConnectionCandidates =
        new HashSet<int>();

    private readonly List<PhraseHistoryEntry> phraseHistory =
        new List<PhraseHistoryEntry>();

    private IReadOnlyList<RhythmCandidateExtractor.Candidate> source;
    private Settings settings;

    private int[] phraseBestCandidateIndices;
    private int nextPhraseCandidateIndex;

    private double lastPhraseSelectedTime = double.NegativeInfinity;
    private double lastPhraseMinimumGap = double.PositiveInfinity;

    public IReadOnlyList<SelectedCandidate> SelectedCandidates =>
        selectedCandidates;

    // 현재 구간에서 박자 칸별 비교를 통과한 후보 수.
    public int LastPhraseCandidateCount => phraseCandidates.Count;

    public void Reset(int slotsPerPhrase)
    {
        selectedCandidates.Clear();
        phraseCandidates.Clear();
        phraseSelectedCandidates.Clear();
        phraseCandidateScores.Clear();
        rhythmConnectionCandidates.Clear();
        phraseHistory.Clear();

        phraseBestCandidateIndices = new int[slotsPerPhrase];

        nextPhraseCandidateIndex = 0;
        lastPhraseSelectedTime = double.NegativeInfinity;
        lastPhraseMinimumGap = double.PositiveInfinity;

        source = null;
        settings = default(Settings);
    }

    // 생성기가 분석 준비 여부를 확인한 구간만 전달한다.
    public void Select(
        IReadOnlyList<RhythmCandidateExtractor.Candidate> candidates,
        Settings selectionSettings,
        int firstGrid,
        int endGrid,
        double clipDuration)
    {
        source = candidates;
        settings = selectionSettings;

        for (int i = 0; i < phraseBestCandidateIndices.Length; i++)
        {
            phraseBestCandidateIndices[i] = -1;
        }

        phraseCandidates.Clear();
        phraseSelectedCandidates.Clear();

        double maxSnapSeconds =
            settings.SecondsPerBeat * settings.MaxSnapDistanceBeats;

        // 같은 박자 칸에 여러 후보가 있으면 대표 후보 하나를 고른다.
        while (nextPhraseCandidateIndex < source.Count)
        {
            int candidateIndex = nextPhraseCandidateIndex;
            int gridIndex = GetGridIndex(candidateIndex);

            if (gridIndex >= endGrid)
            {
                break;
            }

            nextPhraseCandidateIndex++;

            if (gridIndex < firstGrid)
            {
                continue;
            }

            double targetTime = GetTargetTime(candidateIndex);
            double snapDistance =
                Math.Abs(source[candidateIndex].Time - targetTime);

            if (snapDistance > maxSnapSeconds + TimeToleranceSeconds ||
                targetTime < settings.EarliestEventTime ||
                targetTime + settings.EndSafetySeconds > clipDuration)
            {
                continue;
            }

            int slot = gridIndex - firstGrid;
            int previousIndex = phraseBestCandidateIndices[slot];

            if (previousIndex < 0 ||
                CompareCandidates(candidateIndex, previousIndex) < 0)
            {
                phraseBestCandidateIndices[slot] = candidateIndex;
            }
        }

        foreach (int candidateIndex in phraseBestCandidateIndices)
        {
            if (candidateIndex >= 0)
            {
                phraseCandidates.Add(candidateIndex);
            }
        }

        BuildPhraseCandidateScores();

        PhraseHistoryEntry currentPhrase = CapturePhraseCandidates();

        ApplyRepeatedPhraseBonus(currentPhrase);
        phraseCandidates.Sort(ComparePhraseCandidateScores);

        // 점수가 높은 후보부터 검사하되 필요한 간격을 확보한다.
        foreach (int candidateIndex in phraseCandidates)
        {
            double targetTime = GetTargetTime(candidateIndex);
            double candidateGap = GetCandidateMinimumGap(candidateIndex);

            double boundaryGap = Math.Min(
                candidateGap,
                lastPhraseMinimumGap);

            if (targetTime - lastPhraseSelectedTime +
                TimeToleranceSeconds < boundaryGap)
            {
                continue;
            }

            bool conflicts = false;

            foreach (int selectedIndex in phraseSelectedCandidates)
            {
                double selectedTime = GetTargetTime(selectedIndex);
                double selectedGap =
                    GetCandidateMinimumGap(selectedIndex);

                double requiredPairGap =
                    Math.Min(candidateGap, selectedGap);

                if (Math.Abs(targetTime - selectedTime) +
                    TimeToleranceSeconds < requiredPairGap)
                {
                    conflicts = true;
                    break;
                }
            }

            if (!conflicts)
            {
                phraseSelectedCandidates.Add(candidateIndex);
            }
        }

        // 원본 후보 인덱스는 시간순이다.
        phraseSelectedCandidates.Sort();

        float peakStrength = 0.0f;

        foreach (int candidateIndex in phraseSelectedCandidates)
        {
            peakStrength = Mathf.Max(
                peakStrength,
                source[candidateIndex].Strength);
        }

        float accentThreshold = peakStrength * settings.AccentRatio;

        foreach (int candidateIndex in phraseSelectedCandidates)
        {
            RhythmCandidateExtractor.Candidate candidate =
                source[candidateIndex];

            bool isAccent =
                peakStrength > 0.0f &&
                candidate.Strength >= accentThreshold;

            double minimumGap = GetCandidateMinimumGap(candidateIndex);

            selectedCandidates.Add(new SelectedCandidate(
                candidate.Time,
                candidate.Strength,
                isAccent,
                minimumGap));

            lastPhraseSelectedTime = GetTargetTime(candidateIndex);
            lastPhraseMinimumGap = minimumGap;
        }

        RememberPhraseSelection(currentPhrase, firstGrid);
    }

    private int GetGridIndex(int candidateIndex)
    {
        double beatPosition =
            (source[candidateIndex].Time -
             settings.FirstBeatTimeSeconds) /
            settings.SecondsPerBeat;

        return (int)Math.Floor(
            beatPosition * settings.BeatSubdivisions + 0.5);
    }

    private double GetTargetTime(int candidateIndex)
    {
        int gridIndex = GetGridIndex(candidateIndex);

        return settings.FirstBeatTimeSeconds +
               (double)gridIndex / settings.BeatSubdivisions *
               settings.SecondsPerBeat;
    }

    private int CompareCandidates(int a, int b)
    {
        int strengthOrder =
            source[b].Strength.CompareTo(source[a].Strength);

        if (strengthOrder != 0)
        {
            return strengthOrder;
        }

        double distanceA =
            Math.Abs(source[a].Time - GetTargetTime(a));

        double distanceB =
            Math.Abs(source[b].Time - GetTargetTime(b));

        int distanceOrder = distanceA.CompareTo(distanceB);

        return distanceOrder != 0
            ? distanceOrder
            : a.CompareTo(b);
    }

    private double GetCandidateMinimumGap(int candidateIndex)
    {
        double gapBeats = settings.MinimumEventGapBeats;

        if (rhythmConnectionCandidates.Contains(candidateIndex))
        {
            double connectionBeats =
                Math.Max(0.5, settings.ConnectionGapBeats);

            gapBeats = Math.Min(gapBeats, connectionBeats);
        }

        return Math.Max(
            gapBeats * settings.SecondsPerBeat,
            settings.InputSafetySeconds);
    }

    private void BuildPhraseCandidateScores()
    {
        phraseCandidateScores.Clear();
        rhythmConnectionCandidates.Clear();

        float peakStrength = 0.0f;

        foreach (int candidateIndex in phraseCandidates)
        {
            peakStrength = Mathf.Max(
                peakStrength,
                source[candidateIndex].Strength);
        }

        for (int slot = 0; slot < phraseBestCandidateIndices.Length; slot++)
        {
            int candidateIndex = phraseBestCandidateIndices[slot];

            if (candidateIndex < 0)
            {
                continue;
            }

            int previousSlot = -1;
            int nextSlot = -1;

            for (int i = slot - 1; i >= 0; i--)
            {
                if (phraseBestCandidateIndices[i] >= 0)
                {
                    previousSlot = i;
                    break;
                }
            }

            for (int i = slot + 1; i < phraseBestCandidateIndices.Length; i++)
            {
                if (phraseBestCandidateIndices[i] >= 0)
                {
                    nextSlot = i;
                    break;
                }
            }

            float rhythmScore = 0.0f;

            if (previousSlot >= 0 && nextSlot >= 0)
            {
                int previousGap = slot - previousSlot;
                int nextGap = nextSlot - slot;

                if (previousGap == nextGap)
                {
                    rhythmScore = 1.0f;
                    rhythmConnectionCandidates.Add(candidateIndex);
                }
            }

            float strengthScore = peakStrength > 0.0f
                ? source[candidateIndex].Strength / peakStrength
                : 0.0f;

            float score =
                strengthScore * (1.0f - settings.RhythmWeight) +
                rhythmScore * settings.RhythmWeight;

            phraseCandidateScores.Add(candidateIndex, score);
        }
    }

    private int ComparePhraseCandidateScores(int a, int b)
    {
        int scoreOrder =
            phraseCandidateScores[b].CompareTo(phraseCandidateScores[a]);

        return scoreOrder != 0
            ? scoreOrder
            : CompareCandidates(a, b);
    }

    private PhraseHistoryEntry CapturePhraseCandidates()
    {
        int slotCount = phraseBestCandidateIndices.Length;

        PhraseHistoryEntry entry = new PhraseHistoryEntry
        {
            CandidateSlots = new bool[slotCount],
            RelativeStrengths = new float[slotCount],
            SelectedSlots = new bool[slotCount]
        };

        float peakStrength = 0.0f;

        for (int slot = 0; slot < slotCount; slot++)
        {
            int candidateIndex = phraseBestCandidateIndices[slot];

            if (candidateIndex >= 0)
            {
                peakStrength = Mathf.Max(
                    peakStrength,
                    source[candidateIndex].Strength);
            }
        }

        for (int slot = 0; slot < slotCount; slot++)
        {
            int candidateIndex = phraseBestCandidateIndices[slot];

            if (candidateIndex < 0)
            {
                continue;
            }

            entry.CandidateSlots[slot] = true;

            entry.RelativeStrengths[slot] = peakStrength > 0.0f
                ? source[candidateIndex].Strength / peakStrength
                : 0.0f;
        }

        return entry;
    }

    private float CalculatePhraseSimilarity(
        PhraseHistoryEntry current,
        PhraseHistoryEntry previous)
    {
        if (current.CandidateSlots.Length != previous.CandidateSlots.Length)
        {
            return 0.0f;
        }

        int unionCount = 0;
        int matchingCount = 0;
        float strengthDifferenceSum = 0.0f;

        for (int slot = 0; slot < current.CandidateSlots.Length; slot++)
        {
            bool hasCurrent = current.CandidateSlots[slot];
            bool hasPrevious = previous.CandidateSlots[slot];

            if (hasCurrent || hasPrevious)
            {
                unionCount++;
            }

            if (hasCurrent && hasPrevious)
            {
                matchingCount++;

                strengthDifferenceSum += Mathf.Abs(
                    current.RelativeStrengths[slot] -
                    previous.RelativeStrengths[slot]);
            }
        }

        if (matchingCount < 3)
        {
            return 0.0f;
        }

        float positionSimilarity =
            (float)matchingCount / unionCount;

        float averageStrengthDifference =
            strengthDifferenceSum / matchingCount;

        return positionSimilarity *
               (1.0f - 0.25f * averageStrengthDifference);
    }

    private void ApplyRepeatedPhraseBonus(PhraseHistoryEntry current)
    {
        if (!settings.EnableRepeatedMatching || phraseHistory.Count == 0)
        {
            return;
        }

        PhraseHistoryEntry bestMatch = null;
        float bestSimilarity = 0.0f;

        for (int i = phraseHistory.Count - 1; i >= 0; i--)
        {
            float similarity =
                CalculatePhraseSimilarity(current, phraseHistory[i]);

            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestMatch = phraseHistory[i];
            }
        }

        if (bestMatch == null ||
            bestSimilarity < settings.RepeatedSimilarity)
        {
            return;
        }

        for (int slot = 0; slot < phraseBestCandidateIndices.Length; slot++)
        {
            int candidateIndex = phraseBestCandidateIndices[slot];

            if (candidateIndex < 0 || !bestMatch.SelectedSlots[slot])
            {
                continue;
            }

            phraseCandidateScores[candidateIndex] +=
                settings.RepeatedBonus * bestSimilarity;
        }
    }

    private void RememberPhraseSelection(
        PhraseHistoryEntry entry,
        int firstGrid)
    {
        if (!settings.EnableRepeatedMatching ||
            phraseSelectedCandidates.Count == 0)
        {
            return;
        }

        foreach (int candidateIndex in phraseSelectedCandidates)
        {
            int slot = GetGridIndex(candidateIndex) - firstGrid;

            if (slot >= 0 && slot < entry.SelectedSlots.Length)
            {
                entry.SelectedSlots[slot] = true;
            }
        }

        phraseHistory.Add(entry);

        int historyLimit = Mathf.Clamp(settings.HistoryLimit, 1, 64);

        while (phraseHistory.Count > historyLimit)
        {
            phraseHistory.RemoveAt(0);
        }
    }
}