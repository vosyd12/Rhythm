using UnityEngine;
using System.Collections;

public partial class MusicPlayback : MonoBehaviour
{
    [SerializeField]
    private AudioSource musicSource;

    // 초기 분석에서 나온 후보 처리가 끝났는지 확인한다.
    // 실제 분석이나 패턴 선택은 각 담당 컴포넌트가 수행한다.
    [SerializeField]
    private RhythmPatternGenerator patternGenerator;

    private AudioClip clip;
    private int lastLoggedSecond = -1;

    // 음악을 시작하도록 예약한 DSP 시각.
    // 매 프레임의 현재 시각을 저장하는 변수가 아니다.
    private double songStartDspTime;

    // 정상 종료 여부를 판단할 DSP 시각.
    private double songEndDspTime;

    private bool isInitialized;
    private bool isPlaybackActive;

    // 실제 음원 끝을 확인한 뒤 입력 시스템이 한 번 더 갱신될 시간을 준다.
    // 마지막 프레임 도중 발생한 입력을 정산보다 먼저 처리하기 위함이다.
    private bool hasObservedSongEnd;

    // 이 실제 시각 이후에 발생한 입력만 현재 재생에 사용할 수 있다.
    // 일시정지는 DSP 시계를 멈추지만 입력의 실제 시간은 계속 흐른다.
    // 재개할 때 이 기준을 바꾸면 정지 중 발생한 늦은 입력을 걸러낼 수 있다.
    private double inputAcceptedFromRealtime = double.PositiveInfinity;

    // 재생을 요청했지만 초기 준비가 끝나기를 기다리는 상태.
    // 준비가 완료되어 예약하거나, 수동 정지하면 false가 된다.
    private bool isStartRequested;

    public int PlaybackId { get; private set; }

    // 현재 재생 요청이 실제 플레이인지 나타낸다.

    // false: 미리듣기. 판정과 보상 지급 금지.
    // true: 실제 플레이. 정상 완주하면 정산 가능.

    // 실제 플레이가 끝나도 true를 유지해야
    // 정산 시스템에서 완료 결과를 처리할 수 있다.
    public bool IsGameplayPlayback { get; private set; }

    // 현재 음원의 길이. 데이터 준비 후 예약할 때 확정한다.
    // Pitch 1을 기준으로 사용한다.
    public double SongDurationSeconds { get; private set; }

    // UI에서 조회할 곡 이름.
    // 외부에서는 읽을 수 있고, 변경은 재생 시스템 내부에서 한다.
    public string SongName { get; private set; } = string.Empty;

    // 수동 정지와 구분하기 위한 정상 종료 기록.
    // 다음 재생을 요청할 때 false로 초기화한다.
    public bool HasNaturallyEnded { get; private set; }

    // 현재 재생을 일시정지했는지 외부에서 확인한다.
    public bool IsPaused { get; private set; }

    // 실행 중인 카운트다운을 나중에 취소할 수 있도록 보관한다.
    private Coroutine resumeCoroutine;

    // 화면에 표시할 남은 초: 3 → 2 → 1
    // 0이면 카운트다운 중이 아니다.
    public int ResumeCountdownSeconds { get; private set; }

    public bool IsResumeCountingDown =>
        ResumeCountdownSeconds > 0;

    // 실제 재생 중이거나, 일시정지 상태일 때 버튼을 사용할 수 있다.
    // 로딩 중, 예약 시작 전, 수동 정지 후, 정상 종료 후에는 사용할 수 없다.
    public bool CanTogglePause =>
        isInitialized &&
        isActiveAndEnabled &&
        Application.isFocused &&
        isPlaybackActive &&
        !IsResumeCountingDown &&
        (IsPaused || TryGetSongTime(out _));

    // 실제 플레이 시작 요청을 받을 수 있는지 확인한다.
    public bool CanStartGameplay =>
        Application.isPlaying &&
        Application.isFocused &&
        isInitialized &&
        isActiveAndEnabled &&
        !IsGameplayPlayback &&
        musicSource != null &&
        clip != null &&
        musicSource.clip == clip &&
        patternGenerator != null &&
        patternGenerator.CanPrepareForGameplay;

    private void Start()
    {
        if (musicSource == null ||
            musicSource.clip == null ||
            patternGenerator == null)
        {
            FailPlayback(
                "AudioSource, 음원, Rhythm Pattern Generator를 연결해 주세요.");
            return;
        }

        // 이번 준비와 재생에서 사용할 음원을 고정한다.
        clip = musicSource.clip;

        // 로더가 AudioClip에 붙인 이름을 표시용으로 저장한다.
        // 현재 로더에서는 확장자를 제외한 파일명을 사용한다.
        SongName = clip.name;

        musicSource.playOnAwake = false;
        musicSource.loop = false;
        musicSource.pitch = 1.0f;
        // 이 음원은 오디오 시스템의 일시정지 설정을 따른다.
        musicSource.ignoreListenerPause = false;

        isInitialized = true;

        // 곧바로 예약하지 않고, 준비 완료를 기다리는 요청을 등록한다.
        RestartMusic();
    }

    private void Update()
    {
        // 창 밖에서 준비가 끝나더라도 새로운 재생 예약을 진행하지 않는다.
        // 이미 재생 중이라면 일시정지 상태로 전환한다.
        if (IsWorldPlayback) SyncWorldPause(GameExitUI.IsWorldPaused);
        if (!Application.isFocused)
        {
            if (IsWorldPlayback) SyncWorldPause(true); else PauseForFocusLoss();
            return;
        }

        // 일시정지 중에는 재생 진행과 정상 종료 검사를 쉬어 간다.
        if (IsPaused) { return; }

        // 초기화 실패 후 Inspector에서 다시 활성화해도 미연결 참조를 읽지 않는다.
        if (!isInitialized) { return; }

        // 완료된 패턴이 있어도 후속 구간 분석이 실패하면 재생을 중단한다.
        // 정상 종료 후 남은 분석을 처리하는 동안의 실패도 반영한다.
        if (patternGenerator.HasGenerationFailed ||
            !patternGenerator.isActiveAndEnabled)
        {
            FailPlayback(
                "패턴 준비가 실패했거나 패턴 컴포넌트가 비활성화되었습니다.");
            return;
        }

        // 이미 분석한 음원과 실제 재생 음원이 달라지는 것을 막는다.
        // 현재 단계에서 다른 음원은 Play를 멈춘 뒤 지정한다.
        if (musicSource.clip != clip)
        {
            FailPlayback(
                "실행 중 음원이 변경되었습니다. Play를 멈춘 뒤 다시 실행해 주세요.");
            return;
        }

        if (isStartRequested)
        {
            TrySchedulePlayback();
            return;
        }

        if (!isPlaybackActive) { return; }

        // 로그 출력 조건보다 먼저 종료 여부를 확인한다.
        // 마지막 초의 로그를 이미 출력했어도 종료 처리는 실행돼야 한다.
        if (AudioSettings.dspTime >= songEndDspTime)
        {
            // Dynamic Update 입력은 MonoBehaviour.Update 전에 전달된다.
            // 이번 프레임 도중 곡이 끝났다면 다음 입력 갱신까지 한 번 기다린다.
            // 정산 시점만 한 프레임 늦추며 음원을 늘려 재생하지는 않는다.
            if (!hasObservedSongEnd)
            {
                hasObservedSongEnd = true;
                return;
            }

            isPlaybackActive = false;
            HasNaturallyEnded = true;

            Debug.Log(
                $"[음원 정상 종료] 재생 회차: {PlaybackId}");
            return;
        }

        if (!TryGetSongTime(out double playbackSeconds)) { return; }

        int currentSecond = (int)playbackSeconds;
        if (currentSecond == lastLoggedSecond) { return; }

        lastLoggedSecond = currentSecond;

        // 현재 음원 샘플 위치를 초로 변환한다.
        double sourceSeconds =
            (double)musicSource.timeSamples / clip.frequency;

        Debug.Log(
            $"곡 기준 시간: {playbackSeconds:F2}초 / " +
            $"음원 재생 위치: {sourceSeconds:F2}초");
    }

    [ContextMenu("미리듣기 처음부터 재생")]
    private void RestartMusic()
    {
        if (!Application.isPlaying ||
            !isInitialized ||
            !isActiveAndEnabled)
        {
            Debug.LogWarning(
                "초기화가 완료된 활성 상태의 Play 모드에서 실행해 주세요.");

            return;
        }

        // 실제 플레이를 시작한 뒤에는 미리듣기로 돌아가지 않는다.
        // 완료한 판정이나 정산 상태가 임의로 초기화되는 것을 막는다.
        if (IsGameplayPlayback)
        {
            Debug.LogWarning(
                "실제 플레이가 시작된 뒤에는 미리듣기를 재시작할 수 없습니다.");

            return;
        }

        RequestPlayback(false);
    }

    // 시작 버튼 담당 컴포넌트에서 호출한다.
    public bool TryStartGameplay(
        double bpm,
        double firstBeatTimeSeconds)
    {
        if (!CanStartGameplay)
        {
            Debug.LogWarning(
                "아직 실제 플레이를 시작할 수 없는 상태입니다.",
                this);

            return false;
        }

        // 박자 확정과 패턴 초기화에 성공한 경우에만
        // 미리듣기를 종료하고 실제 재생을 요청한다.
        if (!patternGenerator.TryPrepareForGameplay(
                bpm,
                firstBeatTimeSeconds))
        {
            return false;
        }

        RequestPlayback(true);
        return true;
    }

    // 미리듣기와 실제 플레이에서 공통으로 사용하는 재생 초기화.
    private void RequestPlayback(bool isGameplay)
    {
        // 이전 음악이나 예약을 취소한다.
        musicSource.Stop();

        // 미리듣기 중 일시정지 또는 재개 카운트다운 상태였어도
        // 새로운 시작은 멈추지 않은 상태에서 진행한다.
        ReleasePause();

        IsGameplayPlayback = isGameplay;

        isPlaybackActive = false;
        isStartRequested = true;

        lastLoggedSecond = -1;
        HasNaturallyEnded = false;
        hasObservedSongEnd = false;

        // 아직 실제 재생 예약 전이므로 입력을 받지 않는다.
        inputAcceptedFromRealtime = double.PositiveInfinity;

        // 입력기와 정산기가 이 번호의 변경을 감지해서
        // 이전 입력 큐, 판정 횟수, 완료 상태를 초기화한다.
        PlaybackId = RunProgress.AllocatePlaybackId();

        string modeLabel = isGameplay ? "실제 플레이" : "미리듣기";

        Debug.Log(
            $"[{modeLabel} 준비 요청] 재생 회차: {PlaybackId}",
            this);
    }

    private void TrySchedulePlayback()
    {
        // 실제 플레이는 확정한 박자로 초기 패턴을 만든 뒤 시작한다.

        // 미리듣기는 실제 패턴을 기다리지 않는다.
        // 그렇지 않으면:
        // 박자를 고르려면 미리듣기 필요
        // → 미리듣기는 패턴 대기
        // → 패턴은 박자 선택 대기
        // 형태로 서로 기다리게 된다.
        if (IsGameplayPlayback &&
            !patternGenerator.IsInitialPatternReady)
        {
            return;
        }

        if (clip.loadState != AudioDataLoadState.Loaded ||
            clip.samples <= 0 ||
            clip.frequency <= 0)
        {
            FailPlayback("재생할 음원의 데이터가 준비되지 않았습니다.");
            return;
        }

        // 음원 길이 = 전체 샘플 프레임 수 / 초당 샘플 프레임 수.
        SongDurationSeconds = (double)clip.samples / clip.frequency;

        // 음악은 언제나 파일 처음부터 시작한다.
        // 분석한 박자 기준 시각을 여기에 더하지 않는다.
        songStartDspTime = AudioSettings.dspTime + 1.0;
        songEndDspTime = songStartDspTime + SongDurationSeconds;

        // 요청을 소비하므로 다음 Update에서 예약을 반복하지 않는다.
        isStartRequested = false;

        musicSource.PlayScheduled(songStartDspTime);
        isPlaybackActive = true;

        // 이 예약 이후에 발생한 입력만 현재 회차에서 사용한다.
        inputAcceptedFromRealtime = Time.realtimeSinceStartupAsDouble;

        string modeLabel =
            IsGameplayPlayback ? "실제 플레이" : "미리듣기";

        Debug.Log(
            $"[{modeLabel} 재생 예약] " +
            $"재생 회차: {PlaybackId} / " +
            "1초 뒤 음악을 처음부터 재생합니다.",
            this);
    }

    [ContextMenu("음악 정지")]
    private void StopMusic()
    {
        if (!Application.isPlaying || !isInitialized) { return; }

        // 준비 대기 중 정지했다면 나중에 준비가 끝나도 자동 재생하지 않는다.
        isStartRequested = false;
        isPlaybackActive = false;

        musicSource.Stop();
        ReleasePause();
        lastLoggedSecond = -1;

        // 정상 종료 후 누른 정지는 이미 끝난 결과를 취소하지 않는다.
        Debug.Log("음악과 대기 중인 재생 요청을 정지했습니다.");
    }

    public bool TryGetSongTime(out double songTime)
    {
        songTime = 0.0;

        // 준비 대기, 수동 정지, 정상 종료 상태에서는 제공하지 않는다.
        if (!isPlaybackActive) { return false; }

        double elapsedTime =
            AudioSettings.dspTime - songStartDspTime;

        // 다른 컴포넌트의 Update가 먼저 실행돼도 입력 허용 경계를 지킨다.
        if (elapsedTime < 0.0 ||
            elapsedTime >= SongDurationSeconds)
        {
            return false;
        }

        // 일시정지가 아닐 때는 실제 재생 상태를 확인한다.
        // 일시정지 중에는 멈춘 DSP 시각으로 계산한 곡 시간을 제공한다.
        // 화면의 노트와 진행 막대가 그 시간을 계속 사용할 수 있게 한다.
        if (!IsPaused && !musicSource.isPlaying) { return false; }

        songTime = elapsedTime;
        return true;
    }

    // Input System에 기록된 실제 입력 시각을 곡 기준 시각으로 바꾼다.
    // 입력 콜백에서 context.time을 전달하며, 화면 표시에는 TryGetSongTime을 쓴다.
    public bool TryGetInputSongTime(double inputRealtime, out double songTime)
    {
        songTime = 0.0;

        if (!IsGameplayPlayback ||  
            // 해당 조건은 미리듣기에서는 곡 시간 조회는 허용하지만
            // 판정용 입력 시간은 제공하지 않겠다는 뜻
            !isInitialized ||
            !isActiveAndEnabled ||
            IsPaused ||
            !Application.isFocused ||
            (!isPlaybackActive && !HasNaturallyEnded) ||
            double.IsNaN(inputRealtime) ||
            double.IsInfinity(inputRealtime) ||
            inputRealtime < inputAcceptedFromRealtime)
        {
            return false;
        }

        double realtimeNow = Time.realtimeSinceStartupAsDouble;
        double dspNow = AudioSettings.dspTime;

        // 입력 기록부터 지금까지 흐른 실제 시간.
        double inputAge = realtimeNow - inputRealtime;
        if (inputAge < 0.0) { return false; }

        // 현재 DSP 시각에서 입력 전달 지연을 빼면 입력 당시의 DSP 시각이다.
        // 여기서 곡 시작 DSP 시각을 빼면 곡 시작을 0초로 한 시간이 된다.
        // 실제 시간과 DSP 시각의 원점이 같다고 가정하지 않는다.
        double convertedTime = dspNow - inputAge - songStartDspTime;

        if (convertedTime < 0.0 || convertedTime >= SongDurationSeconds)
        {
            return false;
        }

        // 정상 종료 프레임에 늦게 전달된 '곡 종료 전 입력'도 변환해야 하므로
        // 여기서는 AudioSource.isPlaying을 조건으로 사용하지 않는다.
        songTime = convertedTime;
        return true;
    }

    private void FailPlayback(string message)
    {
        isStartRequested = false;
        isPlaybackActive = false;

        // 실패한 시도를 정상 완주로 정산하지 않는다.
        HasNaturallyEnded = false;

        Debug.LogError($"[재생 중단] {message}", this);
        enabled = false;
    }

    private void OnDisable()
    {
        isStartRequested = false;
        isPlaybackActive = false;

        // 초기화가 실패했거나 씬이 정리되는 상황도 고려한다.
        if (musicSource != null) { musicSource.Stop(); }

        ReleasePause();
    }

    // UI 버튼에서 호출할 일시정지 / 재개 함수.
    public void TogglePause()
    {
        if (!CanTogglePause) { return; }

        if (IsPaused)
        {
            // 먼저 카운트다운 상태로 전환한다.
            // 같은 프레임에 다시 호출돼도 중복 요청을 막는다.
            ResumeCountdownSeconds = 3;

            // IsPaused는 true로 유지한다.
            // 카운트다운이 끝날 때까지 음악과 판정은 멈춰 있다.
            resumeCoroutine =
                StartCoroutine(ResumeAfterCountdown());
            return;
        }

        IsPaused = true;

        // 오디오 재생과 DSP 시계를 함께 멈춘다.
        // 기존 시작 시각과 종료 시각은 그대로 유지한다.
        AudioListener.pause = true;

        // PlaybackId를 변경하지 않으므로
        // 현재 재생 회차와 판정 기록이 유지된다.
        Debug.Log("[음악 일시정지]");
    }

    // 실제 시간이 흐르는 동안 카운트다운한 뒤 재개한다.
    private IEnumerator ResumeAfterCountdown()
    {
        while (ResumeCountdownSeconds > 0)
        {
            // 현재 숫자를 표시한 상태로 실제 시간 1초를 기다린다.

            // yield return은 게임 전체를 멈추는 명령이 아니다.
            // 이 함수의 실행을 잠시 양보하고 나중에 이어서 실행한다.
            yield return new WaitForSecondsRealtime(1.0f);

            // 3에서 시작해서 1초마다 2, 1, 0으로 감소한다.
            ResumeCountdownSeconds--;
        }

        // 정상적으로 끝나는 코루틴의 참조를 먼저 비운다.
        // 아래 ReleasePause에서 자기 자신을 중단하지 않게 한다.
        resumeCoroutine = null;

        // 준비 시간이 끝났으므로 오디오와 판정을 재개한다.
        ReleasePause();

        Debug.Log("[카운트다운 완료 / 음악 재개]");
    }

    // 이 컴포넌트에서 걸어 둔 일시정지를 해제한다.
    private void ReleasePause()
    {
        // 기다리던 재개 요청부터 정리한다.
        CancelResumeCountdown();

        if (!IsPaused) { return; }

        AudioListener.pause = false;
        IsPaused = false;

        // 재개 이후에 새로 발생한 입력만 받는다.
        // DSP는 정지 중 흐르지 않았으므로 시작 시각에 정지 시간을 더하지 않는다.
        inputAcceptedFromRealtime = Time.realtimeSinceStartupAsDouble;
    }

    // 재개를 기다리는 카운트다운만 취소한다.
    // IsPaused와 AudioListener.pause는 변경하지 않는다.
    private void CancelResumeCountdown()
    {
        if (resumeCoroutine != null)
        {
            StopCoroutine(resumeCoroutine);
            resumeCoroutine = null;
        }

        ResumeCountdownSeconds = 0;
    }

    // Unity가 창의 포커스 변경 시 자동으로 호출한다.
    private void OnApplicationFocus(bool hasFocus)
    {
        // 창으로 돌아온 경우에는 일시정지를 유지한다.
        // 사용자가 RESUME을 눌렀을 때 재개한다.
        if (hasFocus) { return; }

        if (IsWorldPlayback) SyncWorldPause(true); else PauseForFocusLoss();
    }

    private void PauseForFocusLoss()
    {
        // 초기화 전이나 재생 요청이 없는 상태는 처리하지 않는다.
        if (!isInitialized || !isPlaybackActive) { return; }

        // 이미 일시정지 중이어도 카운트다운은 진행 중일 수 있다.
        // 먼저 취소해서 창 밖에서 음악이 재개되는 것을 막는다.
        CancelResumeCountdown();

        // 이미 멈춰 있다면 그 상태를 유지한다.
        if (IsPaused) { return; }

        // 종료 시각이 지났다면 정상 종료 처리에 맡긴다.
        if (AudioSettings.dspTime >= songEndDspTime) { return; }

        IsPaused = true;

        // 음악과 판정의 기준이 되는 DSP 시계를 함께 멈춘다.
        AudioListener.pause = true;

        Debug.Log(
            "[자동 일시정지] 게임 창에서 포커스가 벗어났습니다.");
    }
}