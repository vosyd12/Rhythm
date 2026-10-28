using System;
using System.Collections.Generic;
using UnityEngine;

// 음원을 구간별로 분석하고, 리듬 이벤트 후보 시각을 생성한다.

// 처리 순서:
// 1. 음원 데이터가 메모리에 로드되기를 기다린다.
// 2. 처음 약 30초도 여러 프레임에 나누어 분석한다.
// 3. 첫 구간이 끝나면 IsInitialAnalysisReady를 true로 만든다.
// 4. 재생 중 다음 약 30초를 미리 분석하고 후보를 목록 뒤에 추가한다.

// 이 스크립트는 음악을 재생하거나 입력을 판정하지 않는다.
// RMS 분석은 별도 스레드가 아니라 Unity의 메인 스레드에서 실행한다.
public class AudioAnalysisTester : MonoBehaviour
{
    // private 필드도 Unity가 저장하고 Inspector에 표시하도록 지정한다.
    [SerializeField]
    private AudioSource musicSource;

    // 현재 곡 시간을 조회해서 다음 분석을 시작할 시점을 결정한다.
    [SerializeField]
    private MusicPlayback musicPlayback;

    // 한 프레임에서 RMS를 계산할 분석 창의 최대 개수.
    // 값이 크면 분석은 빨리 끝나지만 한 프레임의 작업량도 증가한다.
    [SerializeField, Min(1)]
    private int windowsPerFrame = 8;

    // 한 번에 준비할 분석 구간: 약 30초.
    private const double ChunkSeconds = 30.0;

    // RMS 하나를 계산할 작은 분석 창: 약 0.1초.
    // 분석 창을 줄이면 시간 구분은 세밀해지지만 계산량이 증가한다.
    private const double WindowSeconds = 0.1;

    // 후보로 선택되지 않은 창도 포함한 곡 전체 RMS 기록.
    // 각 원소는 actualHopSeconds 길이의 분석 창 하나에 해당한다.
    private readonly List<float> windowRmsValues =
        new List<float>();

    // 각 창에서 직전 창보다 RMS가 얼마나 증가했는지 기록한다.
    // 특정 악기나 정확한 박자를 확정하는 값은 아니다.
    private readonly List<float> windowRiseValues =
        new List<float>();

    public IReadOnlyList<float> WindowRmsValues =>
        windowRmsValues;

    public IReadOnlyList<float> WindowRiseValues =>
        windowRiseValues;

    // 분석 창 사이의 실제 시간 간격.
    // 초기화 전에는 0이므로 사용하는 쪽에서 준비 여부를 확인한다.
    public double AnalysisHopSeconds => actualHopSeconds;

    // 기록이 준비된 구간의 끝.
    // 다음 구간을 아직 분석하지 않은 것을 무음으로 오해하지 않도록 제공한다.
    public double AnalyzedUntilSeconds =>
        Math.Min(
            windowRmsValues.Count * actualHopSeconds,
            ClipDurationSeconds);

    // 첫 구간의 RMS 계산과 후보 추출까지 끝났는지 나타낸다.
    // 후보가 0개인 경우에도 정상적으로 분석했다면 true가 된다.
    public bool IsInitialAnalysisReady { get; private set; }

    // 실패를 외부에 전달하여 무한 대기나 잘못된 정산을 막는다.
    public bool HasAnalysisFailed { get; private set; }

    // 초기화할 때 분석 대상 음원을 저장한다.
    private AudioClip clip;

    // 현재 분석하는 음원의 전체 길이를 초 단위로 제공한다.

    // samples는 채널별 샘플 프레임 수이므로
    // 초당 샘플 프레임 수인 frequency로 나누면 된다.
    // 채널 수는 곱하지 않는다.
    public double ClipDurationSeconds =>
        clip != null && clip.frequency > 0
            ? (double)clip.samples / clip.frequency
            : 0.0;

    // 데이터 로딩 대기와 RMS 분석 진행을 구분한다.
    private bool isWaitingForAudioData;

    // 샘플 프레임:
    // 같은 시점에 존재하는 모든 채널의 샘플을 묶은 단위.
    // 스테레오라면 프레임 하나에 왼쪽/오른쪽 값이 하나씩 있다.
    private int windowFrames;

    // 정수 프레임 수로 반올림한 뒤 실제 창 길이를 계산한다.
    private double actualHopSeconds;

    // 약 30초 구간의 분석 창 개수. 보통 300개다.
    private int windowsPerChunk;

    // 마지막에 남는 0.1초 미만의 구간도 하나의 창으로 포함한다.
    private int totalWindows;

    // 약 0.1초의 모든 채널 샘플을 담는 재사용 버퍼.
    // 크기 = 샘플 프레임 수 × 채널 수.
    private float[] sampleBuffer;

    // 음원 끝에 남는 짧은 구간 전용 버퍼.
    // 끝을 넘어 읽어 음원 처음 데이터가 섞이는 것을 방지한다.
    private float[] finalSampleBuffer;

    // 현재 구간과 경계 비교에 필요한 RMS를 재사용 버퍼에 보관한다.
    private float[] rmsBuffer;

    // 곡 전체 분석 창 번호를 기준으로 한 현재 구간의 시작 위치.
    private int chunkStartWindow;

    // 마지막 구간에서는 windowsPerChunk보다 작을 수 있다.
    private int chunkWindowCount;

    // 경계 앞뒤 데이터를 포함하므로 chunkStartWindow와 다를 수 있다.
    private int readStartWindow;

    // 경계 비교용 창까지 포함한 전체 읽기 개수.
    private int readWindowCount;

    // 다음 프레임에도 이어서 사용할 RMS 버퍼 저장 위치.
    private int readIndex;

    // 현재 분석이 끝난 뒤 다음 구간이 시작할 창 번호.
    private int nextChunkStartWindow;

    private bool isAnalyzing;

    // 생성기에서 요청한 분석 범위의 끝.
    private double requestedAnalysisUntil;

    // 음원 전체 분석 완료 상태는 곡 종료 정산에서 사용한다.
    // 실패는 정상 완료로 처리하지 않는다.
    public bool IsAnalysisComplete =>
        !HasAnalysisFailed &&
        !isWaitingForAudioData &&
        totalWindows > 0 &&
        !isAnalyzing &&
        nextChunkStartWindow >= totalWindows;

    private void Start()
    {
        // 고정 참조는 초기화 시점에 검사한다.
        if (musicSource == null ||
            musicSource.clip == null ||
            musicPlayback == null)
        {
            FailAnalysis("음원과 Music Playback을 연결해 주세요.");
            return;
        }

        if (windowsPerFrame < 1)
        {
            FailAnalysis("Windows Per Frame은 1 이상이어야 합니다.");
            return;
        }

        clip = musicSource.clip;

        // GetData로 전체 샘플을 읽는 현재 분석 방식은 Streaming을 지원하지 않는다.
        if (clip.loadType == AudioClipLoadType.Streaming)
        {
            FailAnalysis(
                "분석할 음원의 Load Type을 Decompress On Load로 설정해 주세요.");
            return;
        }

        // Preload Audio Data가 꺼져 있다면 직접 로드를 요청한다.
        // Load In Background 설정에 따라 이 호출은 동기 또는 비동기로 동작한다.
        if (clip.loadState == AudioDataLoadState.Unloaded)
        {
            bool loadAccepted = clip.LoadAudioData();

            if (!loadAccepted &&
                clip.loadState != AudioDataLoadState.Loading &&
                clip.loadState != AudioDataLoadState.Loaded)
            {
                FailAnalysis("음원 데이터 로드 요청에 실패했습니다.");
                return;
            }
        }

        // 로드 완료 여부는 Update에서 확인한다.
        // 첫 RMS 계산도 Update의 windowsPerFrame 예산을 사용한다.
        isWaitingForAudioData = true;

        Debug.Log("[분석 준비] 음원 데이터가 준비되기를 기다립니다.");
    }

    private void Update()
    {
        if (HasAnalysisFailed) { return; }

        if (musicSource.clip != clip)
        {
            FailAnalysis(
                "실행 중 분석 음원이 변경되었습니다. 새로 Play해 주세요.");
            return;
        }

        if (isWaitingForAudioData)
        {
            TryBeginInitialAnalysis();
            return;
        }

        // 첫 구간과 이후 구간 모두 프레임별 계산 예산을 사용한다.
        if (isAnalyzing)
        {
            ProcessAnalysis(windowsPerFrame);
            return;
        }

        if (nextChunkStartWindow >= totalWindows) { return; }

        // 재생 전이라도 생성기가 필요한 분석 범위를 요청했다면 진행한다.
        // 기존 프레임별 계산 예산은 그대로 사용한다.
        if (AnalyzedUntilSeconds + 0.000001 < requestedAnalysisUntil)
        {
            BeginChunk(nextChunkStartWindow);
            ProcessAnalysis(windowsPerFrame);
            return;
        }

        // 재생 전이나 정지 상태에서는 새 후속 구간을 시작하지 않는다.
        // 이미 시작한 분석은 위 분기에서 계속 처리한다.
        double songTime;

        if (musicPlayback.HasNaturallyEnded)
        {
            // 정상 종료 후에도 남은 분석을 끝내서 정산할 수 있게 한다.
            songTime = musicPlayback.SongDurationSeconds;
        }
        else if (!musicPlayback.TryGetSongTime(out songTime))
        {
            return;
        }

        // 준비 완료 구간 | 다음 분석 시작 시각 | 다음 분석 구간
        // 0~30초        | 곡 0초             | 30~60초
        // 30~60초       | 곡 30초            | 60~90초
        // 60~90초       | 곡 60초            | 90~120초
        double prepareTime = chunkStartWindow * actualHopSeconds;

        if (songTime < prepareTime) { return; }

        BeginChunk(nextChunkStartWindow);
        ProcessAnalysis(windowsPerFrame);
    }

    private void TryBeginInitialAnalysis()
    {
        // 로딩 중은 실패가 아니므로 다음 프레임에 다시 확인한다.
        if (clip.loadState == AudioDataLoadState.Loading) { return; }

        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            FailAnalysis("음원 데이터를 로드하지 못했습니다.");
            return;
        }

        // 로드가 끝난 뒤 샘플 정보와 버퍼 크기를 검증한다.
        if (clip.samples <= 0 || clip.frequency <= 0 || clip.channels <= 0)
        {
            FailAnalysis("음원의 샘플 수, 주파수, 채널 정보를 확인해 주세요.");
            return;
        }

        isWaitingForAudioData = false;
        InitializeAnalysis();

        // Start에서 int.MaxValue로 한꺼번에 처리하지 않는다.
        // 다음 Update부터 ProcessAnalysis(windowsPerFrame)이 실행된다.
    }

    // 데이터가 로드된 뒤 한 번만 분석 버퍼를 만든다.
    private void InitializeAnalysis()
    {
        windowRmsValues.Clear();
        windowRiseValues.Clear();

        // 44,100Hz × 0.1초 = 4,410프레임.
        windowFrames = Math.Max(
            1, (int)Math.Round(clip.frequency * WindowSeconds));

        actualHopSeconds = (double)windowFrames / clip.frequency;

        windowsPerChunk = Math.Max(
            1, (int)Math.Round(ChunkSeconds / actualHopSeconds));

        // 양의 정수 나눗셈에서 올림한다. 짧은 마지막 창도 포함한다.
        totalWindows = (clip.samples - 1) / windowFrames + 1;

        // 스테레오 4,410프레임이면 float 8,820개를 저장한다.
        sampleBuffer = new float[windowFrames * clip.channels];

        int remainingFrames = clip.samples % windowFrames;
        if (remainingFrames > 0)
        {
            finalSampleBuffer = new float[remainingFrames * clip.channels];
        }

        // 강세 비교용 직전 창 1개와
        // 후보의 앞뒤 증가량 비교용 다음 창 1개를 확보한다.
        rmsBuffer = new float[windowsPerChunk + 1];

        BeginChunk(0);
    }

    // 다음 분석 구간을 설정한다.
    // 실제 데이터를 읽는 작업은 ProcessAnalysis에서 수행한다.
    private void BeginChunk(int startWindow)
    {
        chunkStartWindow = startWindow;

        chunkWindowCount = Math.Min(
            windowsPerChunk,
            totalWindows - chunkStartWindow);

        // 첫 창의 증가량 계산에 필요한 직전 창까지 읽는다.
        readStartWindow = Math.Max(0, chunkStartWindow - 1);

        int readEndWindow =
            chunkStartWindow + chunkWindowCount;

        readWindowCount = readEndWindow - readStartWindow;
        readIndex = 0;
        isAnalyzing = true;
    }

    // budget 개수만큼의 창을 처리하고 나머지는 다음 프레임에 이어서 한다.
    private void ProcessAnalysis(int budget)
    {
        int processed = 0;

        while (readIndex < readWindowCount && processed < budget)
        {
            int globalWindow = readStartWindow + readIndex;

            if (!ReadWindowRms(globalWindow, out float rms))
            {
                FailAnalysis(
                    $"음원 데이터를 읽지 못했습니다. 창 번호: {globalWindow}. " +
                    "Load Type이 Decompress On Load인지 확인해 주세요.");
                return;
            }

            rmsBuffer[readIndex] = rms;
            readIndex++;
            processed++;
        }

        if (readIndex == readWindowCount)
        {
            CompleteChunk();
        }
    }

    // 분석 창 하나의 모든 채널 데이터를 읽고 RMS 하나를 계산한다.
    private bool ReadWindowRms(int globalWindow, out float rms)
    {
        rms = 0.0f;

        int startFrame = globalWindow * windowFrames;
        int validFrames = Math.Min(
            windowFrames, clip.samples - startFrame);

        // 기존 배열 중 이번 창 길이에 맞는 배열을 선택한다.
        float[] buffer = validFrames == windowFrames
            ? sampleBuffer : finalSampleBuffer;

        // 시작 위치에는 채널 수를 곱하지 않는다.
        // 채널 수는 데이터를 받을 배열 길이에만 반영한다.
        if (!clip.GetData(buffer, startFrame)) { return false; }

        double squareSum = 0.0;

        for (int i = 0; i < buffer.Length; i++)
        {
            double value = buffer[i];

            // 양수와 음수 샘플이 서로 상쇄되지 않도록 제곱한다.
            squareSum += value * value;
        }

        // RMS = sqrt(샘플 제곱의 합 / 샘플 개수).
        // 여러 채널의 모든 샘플을 포함해서 평균을 구한다.
        // 신호 크기이며 사람이 느끼는 음량이나 정확한 박자를 뜻하지 않는다.
        rms = (float)Math.Sqrt(squareSum / buffer.Length);
        return true;
    }

    // 버퍼 첫 데이터가 곡의 298번 창이면 곡의 300번 창은 버퍼의 2번이다.
    // 호출부는 BeginChunk에서 확보한 범위 안의 번호를 전달한다.
    private float GetWindowRms(int globalWindow)
    {
        return rmsBuffer[globalWindow - readStartWindow];
    }

    private bool AppendWindowAnalysis()
    {
        // 이전 구간 바로 뒤에 이어서 기록하는지 확인한다.
        // 경계 비교용으로 다시 읽은 창을 중복 저장하지 않도록 한다.
        if (windowRmsValues.Count != chunkStartWindow ||
            windowRiseValues.Count != chunkStartWindow)
        {
            FailAnalysis(
                "분석 기록의 구간 연결이 맞지 않습니다. " +
                $"예상 시작: {chunkStartWindow}, " +
                $"RMS 개수: {windowRmsValues.Count}, " +
                $"증가량 개수: {windowRiseValues.Count}");

            return false;
        }

        for (int localIndex = 0;
             localIndex < chunkWindowCount;
             localIndex++)
        {
            int globalWindow = chunkStartWindow + localIndex;

            float currentRms = GetWindowRms(globalWindow);

            // 첫 창에는 비교할 이전 창이 없으므로 증가량을 0으로 둔다.
            // 이후 구간의 첫 창은 앞 구간의 마지막 창과 비교한다.
            float previousRms = globalWindow > 0
                ? GetWindowRms(globalWindow - 1)
                : currentRms;

            float rise = Mathf.Max(
                0.0f,
                currentRms - previousRms);

            // 최소 후보 간격이나 증가량 기준으로 걸러내지 않는다.
            // 패턴 생성 단계에서 구간 전체를 다시 검토할 자료다.
            windowRmsValues.Add(currentRms);
            windowRiseValues.Add(rise);
        }

        return true;
    }

    private void CompleteChunk()
    {
        if (!AppendWindowAnalysis())
        {
            return;
        }

        nextChunkStartWindow =
            chunkStartWindow + chunkWindowCount;

        isAnalyzing = false;

        double startSeconds =
            chunkStartWindow * actualHopSeconds;

        double endSeconds = Math.Min(
            nextChunkStartWindow * actualHopSeconds,
            ClipDurationSeconds);

        Debug.Log(
            $"[구간 분석 완료] {startSeconds:F2}~{endSeconds:F2}초",
            this);

        if (chunkStartWindow == 0)
        {
            IsInitialAnalysisReady = true;

            Debug.Log("[초기 분석 준비 완료]", this);
        }

        if (nextChunkStartWindow >= totalWindows)
        {
            Debug.Log(
                "[전체 분석 완료] 음원 끝까지 분석했습니다.",
                this);
        }
    }

    private void FailAnalysis(string message)
    {
        HasAnalysisFailed = true;
        IsInitialAnalysisReady = false;
        isWaitingForAudioData = false;
        isAnalyzing = false;

        Debug.LogError($"[음원 분석 실패] {message}", this);
        enabled = false;
    }

    public void RequestAnalysisThrough(double songTime)
    {
        if (HasAnalysisFailed ||
            double.IsNaN(songTime) ||
            double.IsInfinity(songTime) ||
            songTime < 0.0)
        {
            return;
        }

        // 이미 요청한 범위를 줄이지 않는다.
        requestedAnalysisUntil = Math.Max(
            requestedAnalysisUntil,
            Math.Min(songTime, ClipDurationSeconds));
    }
}

// RMS = 샘플값을 제곱한 뒤 평균을 구하고 제곱근을 씌운 값.
// 신호의 일정 시간 동안의 평균적인 크기를 나타낸다.
// 제곱을 사용하여 양수·음수 샘플이 서로 상쇄되는 문제를 피한다.