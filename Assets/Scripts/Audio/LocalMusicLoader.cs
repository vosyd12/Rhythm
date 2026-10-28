using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

// 외부 음원을 불러오는 기능을 담당한다.
// 이번 수정에서는 파일 경로와 확장자까지만 확인한다.
public class LocalMusicLoader : MonoBehaviour
{
    // private: 다른 클래스에서 이 값을 직접 변경하지 못하게 한다.

    // SerializeField: Unity가 값을 저장하고
    // Inspector에서 입력할 수 있게 한다.
    [SerializeField]
    private string filePath;

    // 분석기와 재생기가 함께 사용하는 AudioSource.
    [SerializeField]
    private AudioSource musicSource;

    [SerializeField]
    private MusicPlayback musicPlayback;

    [SerializeField]
    private AudioAnalysisTester audioAnalyzer;

    [SerializeField]
    private RhythmPatternGenerator patternGenerator;

    // 필요한 참조를 확인하고 초기 준비를 마쳤는지 기록한다.
    private bool isInitialized;

    // 진행 중인 파일 읽기 요청.
    // null이면 현재 진행 중인 요청이 없다는 뜻이다.
    private UnityWebRequest loadRequest;

    // 외부 파일에서 만들어 보관하는 음원 데이터.
    // 파일 경로 문자열과 실제 음원 데이터를 구분해서 관리한다.
    private AudioClip loadedClip;

    // 요청 객체가 있으면 파일을 읽는 중이다.
    // 별도의 bool을 중복 관리하지 않고 현재 요청 상태를 조회한다.
    public bool IsLoading => loadRequest != null;

    // 생성한 AudioClip을 보관하고 있는지 조회한다.
    public bool IsAudioLoaded => loadedClip != null;

    // 실패한 경우에는 요청 객체를 정리하므로,
    // 요청 객체의 존재 여부만으로 실패를 구분할 수 없다.
    // 따라서 실패 여부는 별도로 기록한다.
    //
    // 외부에서는 조회만 하고, 변경은 이 클래스에서만 한다.
    public bool HasLoadFailed { get; private set; }

    // 가장 최근 로드 실패에 대한 화면 안내 문구.
    // 외부 UI는 읽을 수 있고, 변경은 로더 내부에서만 한다.
    public string LoadErrorMessage { get; private set; }
        = string.Empty;

    // Inspector에 설정한 경로를 입력창의 초기값으로 사용한다.
    // 외부에서는 읽기만 할 수 있다.
    public string FilePath => filePath;

    // 새 음원을 요청할 수 있는 상태인지 계산한다.

    // 초기화 완료 && 컴포넌트 활성 && 로드 중이 아님 && 로드된 음원이 없음
    // 네 조건을 모두 만족해야 true가 된다.
    public bool CanLoad =>
        isInitialized &&
        isActiveAndEnabled &&
        !IsLoading &&
        !IsAudioLoaded;

    // 컴포넌트의 Inspector 메뉴에 실행 항목을 추가한다.
    // Start나 Update처럼 자동으로 실행되는 함수는 아니다.
    [ContextMenu("외부 음원 경로 확인")]
    private void CheckFilePath()
    {
        // 검사에 성공했을 때만 확인 로그를 출력한다.
        // 여기서는 음원 형식을 사용하지 않으므로 out _로 받지 않는다.
        if (TryGetAudioFile(out string path, out _))
        {
            Debug.Log($"[외부 음원 경로 확인 완료] {path}");
        }
    }

    private void Awake()
    {
        // 컴포넌트가 꺼져 있어도 Awake는 호출될 수 있다.
        // 로더를 사용하도록 켜 둔 경우에만 진행한다.
        if (!enabled) { return; }

        // 기존 AudioSource의 음원으로 먼저 초기화되지 않도록
        // 세 컴포넌트의 첫 Start 실행을 보류한다.
        SetPlaybackSystemsEnabled(false);

        if (musicSource == null ||
            musicPlayback == null ||
            audioAnalyzer == null ||
            patternGenerator == null)
        {
            Debug.LogError(
                "Music Source, Music Playback, Audio Analyzer, " +
                "Pattern Generator를 모두 연결해 주세요.");

            enabled = false;
            return;
        }

        // 컴포넌트만 다시 켜서 실행할 것이므로
        // 해당 컴포넌트가 붙은 오브젝트는 활성 상태여야 한다.
        if (!musicSource.isActiveAndEnabled ||
            !musicPlayback.gameObject.activeInHierarchy ||
            !audioAnalyzer.gameObject.activeInHierarchy ||
            !patternGenerator.gameObject.activeInHierarchy)
        {
            Debug.LogError(
                "연결한 오브젝트와 AudioSource를 활성화해 주세요.");

            enabled = false;
            return;
        }

        // 실제 재생 시작은 MusicPlayback이 담당한다.
        // Inspector에서도 Play On Awake를 꺼 두어야 한다.
        musicSource.playOnAwake = false;
        musicSource.Stop();

        // 이전 낮에서 정상적으로 로드한 경로가 있으면 복구한다.
        if (RunMusicSelection.HasLoadedPath &&
            File.Exists(RunMusicSelection.LastLoadedPath))
        {
            filePath = RunMusicSelection.LastLoadedPath;
        }

        // 저장한 경로가 없으면 Inspector의 기본 filePath를 사용한다.
        isInitialized = true;
    }

    // 반환값 bool: 검사 성공 여부.
    // out path: 정리한 파일 경로.
    // out audioType: 파일을 해석할 음원 형식.
    private bool TryGetAudioFile(
        out string path,
        out AudioType audioType)
    {
        // 실패해서 중간에 반환하더라도 out 값은 지정되어 있어야 한다.
        path = string.Empty;
        audioType = AudioType.UNKNOWN;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            Debug.LogWarning("File Path에 음원 경로를 입력해 주세요.");
            return false;
        }

        path = filePath.Trim().Trim('"');

        if (!File.Exists(path))
        {
            Debug.LogWarning(
                $"파일을 찾을 수 없거나 접근할 수 없습니다: {path}");

            return false;
        }

        string extension =
            Path.GetExtension(path).ToLowerInvariant();

        // 파일 형식에 맞는 해석 방법을 Unity에 전달한다.
        // 확장자 검사만으로 파일 내용의 정상 여부까지 보장되지는 않는다.
        switch (extension)
        {
            case ".wav":
                audioType = AudioType.WAV;
                break;

            case ".ogg":
                audioType = AudioType.OGGVORBIS;
                break;

            case ".mp3":
                audioType = AudioType.MPEG;
                break;

            default:
                Debug.LogWarning(
                    "현재는 WAV, OGG, MP3 파일을 지정해 주세요.");

                return false;
        }

        return true;
    }

    [ContextMenu("외부 음원 로드")]
    private void LoadExternalMusic()
    {
        LoadFromPath(filePath);
    }

    private IEnumerator LoadAudioClipRoutine(string path, AudioType audioType)
    {
        // 입력 경로를 전체 경로로 정리한 뒤 파일 URI로 변환한다.
        // URI는 파일이나 인터넷 자원의 위치를 나타내는 주소다.

        // new Uri를 사용하면 공백과 한글이 포함된 경로도
        // 주소 형식에 맞게 변환할 수 있다.
        string fullPath = Path.GetFullPath(path);
        string uri = new Uri(fullPath).AbsoluteUri;

        loadRequest = UnityWebRequestMultimedia.GetAudioClip(uri, audioType);

        // GetAudioClip이 붙여 준 음원 처리 담당 객체를 가져온다.
        var handler = (DownloadHandlerAudioClip)loadRequest.downloadHandler;

        // 이후 GetData로 분석할 수 있도록 음원 데이터를 준비한다.

        // streamAudio = false:
        // 전체 파일을 읽은 뒤 사용할 음원을 만든다.

        // compressed = false:
        // 압축된 상태로 메모리에 보관하는 옵션을 끈다.
        handler.streamAudio = false;
        handler.compressed = false;

        Debug.Log($"[외부 음원 로드 시작] {Path.GetFileName(path)}");

        // 요청을 시작하고 완료될 때까지 이 코루틴의 진행을 보류한다.
        // 기다리는 동안 Unity의 다른 프레임 작업은 계속 진행된다.
        yield return loadRequest.SendWebRequest();

        if (loadRequest.result != UnityWebRequest.Result.Success)
        {
            // 파일을 읽거나 음원 데이터를 처리하는 과정에서 실패했다.
            // 파일 선택 창에서 선택했다는 사실만으로
            // 정상적인 음원 로드까지 보장되는 것은 아니다.
            LoadErrorMessage = "COULD NOT READ OR DECODE AUDIO";

            HasLoadFailed = true;

            // 개발자가 확인할 상세 원인은 콘솔에 기록한다.
            Debug.LogError($"[외부 음원 로드 실패] {loadRequest.error}");

            // 실패한 요청을 정리해야 다음 로드를 시도할 수 있다.
            ReleaseLoadRequest();

            yield break;
        }

        // 요청 결과에서 실제 AudioClip을 가져온다.
        loadedClip = DownloadHandlerAudioClip.GetContent(loadRequest);

        // 요청에 사용한 자원은 정리한다.
        // 받아 둔 AudioClip은 loadedClip에서 별도로 관리한다.
        ReleaseLoadRequest();

        if (loadedClip == null ||
           loadedClip.samples <= 0 ||
           loadedClip.frequency <= 0)
        {
            // AudioClip이 없거나 재생 시간 계산에 필요한 값이
            // 유효하지 않으면 정상적인 음원으로 사용할 수 없다.
            LoadErrorMessage = "INVALID AUDIO DATA";

            HasLoadFailed = true;

            Debug.LogError("정상적인 음원 데이터를 얻지 못했습니다.");

            // 사용할 수 없는 음원 객체가 만들어졌다면 정리한다.
            if (loadedClip != null)
            {
                Destroy(loadedClip);
                loadedClip = null;
            }

            yield break;
        }

        // 음원 길이(초) = 전체 샘플 프레임 수 / 초당 샘플 프레임 수

        // 예: 441,000프레임 / 44,100Hz = 10초

        // samples는 채널별 샘플 프레임 수이므로
        // 스테레오라고 채널 수 2를 추가로 곱하거나 나누지 않는다.

        // double로 변환하여 정수 나눗셈으로 소수점이 버려지지 않게 한다.
        double durationSeconds =
            (double)loadedClip.samples / loadedClip.frequency;

        Debug.Log(
            $"[외부 음원 로드 완료] {Path.GetFileName(path)}\n" +
            $"길이: {durationSeconds:F2}초\n" +
            $"샘플 주파수: {loadedClip.frequency}Hz\n" +
            $"채널 수: {loadedClip.channels}");

        // Inspector에서 어떤 음원인지 알아볼 수 있도록
        // 이번에 생성한 AudioClip의 이름을 파일 이름으로 지정한다.
        loadedClip.name = Path.GetFileNameWithoutExtension(path);

        // 분석기와 재생기가 읽게 될 공통 음원을 교체한다.
        // 이 대입 자체가 음악 재생을 시작하는 것은 아니다.
        musicSource.clip = loadedClip;

        // fullPath는 이 코루틴 앞부분에서 만든 전체 파일 경로다.
        // 공백·따옴표를 정리하고 전체 경로로 변환한 결과를 사용한다.
        filePath = fullPath;

        // AudioClip 생성과 데이터 검사를 통과한 뒤에만 기록한다.
        // 경로 입력 직후 저장하면 잘못 입력한 경로도 기억하게 되므로
        // 저장 시점을 로드 성공 이후로 둔다.
        RunMusicSelection.SaveLoadedPath(fullPath);

        // 이제 각 컴포넌트의 첫 Start가 실행될 수 있다.
        // 그때 AudioSource에는 이미 외부 음원이 들어 있다.
        SetPlaybackSystemsEnabled(true);

        Debug.Log("[외부 음원 연결 완료] 초기 분석과 재생 준비를 시작합니다.");
    }

    private void ReleaseLoadRequest()
    {
        if (loadRequest == null) { return; }

        // 로드 도중 컴포넌트가 꺼졌다면 진행 중인 요청을 취소한다.
        if (!loadRequest.isDone) { loadRequest.Abort(); }

        // 요청이 사용한 자원을 해제한다.
        loadRequest.Dispose();
        loadRequest = null;
    }

    private void OnDisable()
    {
        if (isInitialized)
        {
            // 음원을 참조하는 분석·패턴·재생 처리를 먼저 멈춘다.
            SetPlaybackSystemsEnabled(false);

            // 우리가 연결한 음원일 때만 AudioSource에서 분리한다.
            if (musicSource != null &&
                loadedClip != null &&
                musicSource.clip == loadedClip)
            {
                musicSource.Stop();
                musicSource.clip = null;
            }
        }

        // 다시 켰을 때 이전 분석 상태로 로드를 재개하지 않게 한다.
        // 새로운 시작은 Play 재실행이나 새로운 Boot 씬 진입으로 처리한다.
        isInitialized = false;

        StopAllCoroutines();
        ReleaseLoadRequest();

        // 외부 파일에서 직접 생성한 음원만 정리한다.
        if (loadedClip != null)
        {
            Destroy(loadedClip);
            loadedClip = null;
        }
    }

    private void SetPlaybackSystemsEnabled(bool value)
    {
        // 연결 실패나 씬 정리 중에도 호출될 수 있으므로
        // 참조가 남아 있는 컴포넌트만 처리한다.
        if (audioAnalyzer != null)
        {
            audioAnalyzer.enabled = value;
        }

        if (patternGenerator != null)
        {
            patternGenerator.enabled = value;
        }

        if (musicPlayback != null)
        {
            musicPlayback.enabled = value;
        }
    }

    public void LoadFromPath(string path)
    {
        if (!Application.isPlaying || !CanLoad)
        {
            Debug.LogWarning(
                "[외부 음원 요청 무시] " +
                "초기화 전이거나, 비활성 상태이거나, " +
                "이미 로드 중 또는 로드 완료 상태입니다.",
                this);

            return;
        }

        filePath = path;

        HasLoadFailed = false;
        LoadErrorMessage = string.Empty;

        if (!TryGetAudioFile(
                out string validatedPath,
                out AudioType audioType))
        {
            LoadErrorMessage = "CHECK FILE PATH AND FILE TYPE";
            HasLoadFailed = true;
            return;
        }

        StartCoroutine(
            LoadAudioClipRoutine(validatedPath, audioType));
    }
}