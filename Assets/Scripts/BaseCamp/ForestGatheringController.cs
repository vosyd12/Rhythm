using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[Serializable]
public sealed class ForestGatheringSettings
{
    public float sessionSeconds = 20;
    public float preparationReserveSeconds = 20;
    public float maximumWoodPerSecond = 5;
    public ForestGatheringSettings Copy() => (ForestGatheringSettings)MemberwiseClone();
    public bool Validate() => Finite(sessionSeconds, 1, 120) && Finite(preparationReserveSeconds, 1, 120) &&
        Finite(maximumWoodPerSecond, 0, 1000);
    private static bool Finite(float n, float min, float max) => !float.IsNaN(n) && !float.IsInfinity(n) && n >= min && n <= max;
}

// 기존 숲 프리팹의 음악/분석/판정을 캠프가 살아 있는 동안 사용한다. 원본 Boot 흐름은 유지한다.
[DefaultExecutionOrder(-100)]
public sealed partial class ForestGatheringController : MonoBehaviour
{
    public static ForestGatheringController Instance { get; private set; }
    public static bool BlocksCommands => Instance != null && (Instance.IsGathering || Instance.showPreparation);
    public static bool Owns(MusicPlayback music) => Instance != null && Instance.playback == music;
    public bool IsGathering { get; private set; }
    public float GatheringSecondsRemaining => Mathf.Max(0, sessionLimit - elapsed);
    public string Status { get; private set; } = "숲 입구에서 R: 짧은 로봇 채집";
    public int LastWoodReward { get; private set; }
    public MusicPlayback Playback => playback;
    public bool CanStartPreparedMusic => showPreparation && battle != null && battle.IsDay &&
        playback != null && playback.isActiveAndEnabled && !GameExitUI.IsWorldPaused &&
        (playback.IsGameplayPlayback || (playback.CanStartGameplay && beatSelection != null &&
            beatSelection.TryGetSelectedBeat(out _, out _)));

    private NightDefenseTester battle;
    private GameObject musicRoot;
    private MusicPlayback playback;
    private LocalMusicLoader loader;
    private MusicBeatSelection beatSelection;
    private RhythmInputTester judgments;
    private AudioSource daySource;
    private Canvas[] forestCanvases;
    private CanvasGroup preparationGroup, playGroup;
    private bool showPreparation, requestedGathering, autoStart;
    private float elapsed, sessionLimit, rewardRate, preparationReserve;
    private int segmentId, perfect, good, normal, miss;
    private double lastSongTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInstance() => Instance = null;

    private IEnumerator Start()
    {
        Instance = this; battle = GetComponent<NightDefenseTester>();
        yield return null;
        BuildHud();
        CreateMusicSystems();
    }
    private void CreateMusicSystems(bool restoreSelection = true)
    {
        var prefab = Resources.Load<GameObject>("ForestGathering");
        if (prefab == null) { Status = "숲 채집 프리팹을 찾지 못했습니다."; return; }
        musicRoot = Instantiate(prefab, transform);
        musicRoot.name = "ForestGathering";
        playback = musicRoot.GetComponentInChildren<MusicPlayback>(true);
        loader = musicRoot.GetComponentInChildren<LocalMusicLoader>(true);
        beatSelection = musicRoot.GetComponentInChildren<MusicBeatSelection>(true);
        judgments = musicRoot.GetComponentInChildren<RhythmInputTester>(true);
        forestCanvases = musicRoot.GetComponentsInChildren<Canvas>(true);
        preparationGroup = FindPart("DayPreparationRoot")?.GetComponent<CanvasGroup>();
        playGroup = FindPart("DayPlayRoot")?.GetComponent<CanvasGroup>();
        if (playback == null || loader == null || beatSelection == null || judgments == null || preparationGroup == null || playGroup == null)
        { Status = "숲 채집 프리팹의 연결이 유효하지 않습니다."; musicRoot.SetActive(false); return; }
        playback.IsWorldPlayback = true;
        judgments.UsesGatheringWindows = true;
        judgments.NoteJudged += RecordJudgment;
        daySource = playback.GetComponent<AudioSource>();
        foreach (var canvas in forestCanvases)
        {
            // 캠프에서는 숲 전용 카메라를 사용하지 않는다. 비활성 카메라를 계속
            // 참조하면 준비창과 채집 화면이 표시 상태여도 실제로 그려지지 않는다.
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
            canvas.sortingOrder = 60;
            canvas.enabled = false;
        }
        SetForestVisible();
        if (restoreSelection && RunMusicSelection.HasLoadedPath && File.Exists(RunMusicSelection.LastLoadedPath))
        { loader.LoadFromPath(RunMusicSelection.LastLoadedPath); autoStart = true; }
    }
    private Transform FindPart(string part)
    {
        foreach (var t in musicRoot.GetComponentsInChildren<Transform>(true)) if (t.name == part) return t;
        return null;
    }
    public void RequestGathering()
    {
        if (!CanGather()) return;
        requestedGathering = true;
        if (playback != null && playback.IsGameplayPlayback && playback.TryGetSongTime(out _))
            BeginGathering();
        else OpenMusicPreparation(true);
    }
    private bool CanGather()
    {
        var settings = M6Balance.Snapshot().GatheringSettings;
        if (battle == null || !battle.IsDay || battle.IsChangingScene || PlayerCombatStats.ActionsBlocked ||
            RunCampaign.IsTravelling || RunCampaign.Data.mode != "Playing" || GameExitUI.IsWorldPaused ||
            M7CampaignUI.IsModalOpen || IsGathering) return false;
        if (battle.PhaseSecondsRemaining <= settings.preparationReserveSeconds + 1)
        { Status = "밤 준비 시간을 남기기 위해 지금은 채집을 시작할 수 없습니다."; return false; }
        return true;
    }
    public void OpenMusicPreparation(bool gatherAfterStart = false)
    {
        if (battle == null || !battle.IsDay || IsGathering || GameExitUI.IsWorldPaused || M7CampaignUI.IsModalOpen) return;
        if (musicRoot == null) { Status = "숲 채집 시스템을 준비하지 못했습니다."; return; }
        FindAnyObjectByType<CampWindowController>()?.CloseAll();
        CampWallPlacementController.Instance?.CancelPlacement();
        requestedGathering = gatherAfterStart;
        showPreparation = true;
        Status = "음원을 고르고 분석 후 시작하세요. 캠프의 낮 시간은 계속 흐릅니다.";
        SetForestVisible();
    }
    public void StartPreparedMusic()
    {
        if (!CanStartPreparedMusic) return;
        if (!playback.IsGameplayPlayback)
        {
            if (!beatSelection.TryGetSelectedBeat(out double bpm, out double phase) || !playback.TryStartGameplay(bpm, phase)) return;
        }
        autoStart = false;
        showPreparation = false;
        SetForestVisible();
        ClearSelection();
    }
    public void ClosePreparation()
    {
        if (showPreparation) Status = "음원 준비를 닫았습니다. 캠프에서 밤을 준비하세요.";
        showPreparation = requestedGathering = false; SetForestVisible(); ClearSelection();
    }
    public void ResetMusicSelection()
    {
        if (battle == null || !battle.IsDay || IsGathering || musicRoot == null) return;
        autoStart = requestedGathering = false;
        if (judgments != null) judgments.NoteJudged -= RecordJudgment;
        musicRoot.SetActive(false); Destroy(musicRoot);
        musicRoot = null; playback = null; loader = null; judgments = null;
        CreateMusicSystems(false);
        autoStart = false;
        showPreparation = true;
        Status = "새 음원을 선택하고 로드·분석 후 시작하세요.";
        SetForestVisible(); ClearSelection();
    }
    private void BeginGathering()
    {
        if (!CanGather() || playback == null || !playback.IsGameplayPlayback || !playback.TryGetSongTime(out double songTime)) return;
        var settings = M6Balance.Snapshot().GatheringSettings;
        FindAnyObjectByType<CampWindowController>()?.CloseAll();
        CampWallPlacementController.Instance?.CancelPlacement();
        perfect = good = normal = miss = 0;
        elapsed = 0; sessionLimit = Mathf.Min(settings.sessionSeconds, battle.PhaseSecondsRemaining - settings.preparationReserveSeconds);
        rewardRate = settings.maximumWoodPerSecond;
        preparationReserve = settings.preparationReserveSeconds;
        segmentId = RunProgress.AllocatePlaybackId();
        lastSongTime = songTime;
        showPreparation = requestedGathering = false;
        IsGathering = true;
        judgments.BeginGatheringWindow(songTime);
        Status = "로봇 조종 중 · 좌/우 클릭: Tap·Hold · Space: Sync · R/Esc: 복귀";
        SetForestVisible(); ClearSelection();
    }
    private void RecordJudgment(RhythmNoteData note, RhythmJudgment judgment, double time)
    {
        if (!IsGathering) return;
        if (judgment == RhythmJudgment.Perfect) perfect++;
        else if (judgment == RhythmJudgment.Good) good++;
        else if (judgment == RhythmJudgment.Normal) normal++;
        else miss++;
    }
    public static int CalculateReward(float seconds, float maximumRate, int p, int g, int n, int m)
    {
        long count = (long)p + g + n + m;
        if (count <= 0 || p < 0 || g < 0 || n < 0 || m < 0 || !(seconds > 0) || !(maximumRate >= 0) ||
            float.IsInfinity(seconds) || float.IsInfinity(maximumRate)) return 0;
        double value = Math.Floor((double)seconds * maximumRate * (3L * p + 2L * g + n) / (3L * count));
        return (int)Math.Min(int.MaxValue, value);
    }
    public void EndGathering()
    {
        requestedGathering = false;
        if (!IsGathering) return;
        if (playback != null && playback.TryGetSongTime(out double songTime)) lastSongTime = songTime;
        else if (playback != null && playback.HasNaturallyEnded) lastSongTime = playback.SongDurationSeconds;
        judgments?.EndGatheringWindow(lastSongTime);
        // 알림 정산 후 먼저 잠가 반복 호출·UI 재진입의 중복 지급을 막는다.
        IsGathering = false;
        int requested = CalculateReward(elapsed, rewardRate, perfect, good, normal, miss);
        LastWoodReward = Mathf.Min(requested, int.MaxValue - RunResources.StoredWood);
        RunResources.TryAddWood(LastWoodReward);
        RunProgress.SaveDayStats(new DayPlayStats(RunProgress.CurrentDay, segmentId, perfect, good, normal, miss, LastWoodReward));
        Status = $"채집 완료 · 목재 +{LastWoodReward} · Perfect {perfect} / Good {good} / Normal {normal} / Miss {miss}";
        SetForestVisible(); ClearSelection();
    }
    private static void ClearSelection() { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); }
    private void Update()
    {
        if (battle == null) return;
        bool dayMusic = battle.IsDay || battle.IsWarning;
        if (daySource != null) { daySource.mute = !dayMusic; daySource.volume = RunAudioSettings.MusicVolume; }
        if (!battle.IsDay || PlayerCombatStats.ActionsBlocked || RunCampaign.IsTravelling || RunCampaign.Data.mode != "Playing")
        { EndGathering(); if (showPreparation) ClosePreparation(); }
        if (playback != null)
        {
            playback.SyncWorldPause(GameExitUI.IsWorldPaused);
            if (IsGathering && !playback.isActiveAndEnabled)
            { EndGathering(); Status = $"음원 재생·분석이 중단되어 복귀했습니다. 목재 +{LastWoodReward}"; }
            if (!GameExitUI.IsWorldPaused && playback.HasNaturallyEnded)
            { EndGathering(); playback.ReplayWorldMusic(); }
            // 준비창에서는 플레이어가 박자를 확인하고 시작한다. 캠프에서의 자동 복원은 유지한다.
            if (autoStart && !showPreparation && playback.CanStartGameplay && beatSelection.TryGetSelectedBeat(out double bpm, out double phase))
            { playback.TryStartGameplay(bpm, phase); autoStart = false; }
        }
        if (GameExitUI.IsWorldPaused) { RefreshHud(); return; }
        if (requestedGathering && !showPreparation && playback != null && playback.TryGetSongTime(out _)) BeginGathering();
        var keys = Keyboard.current;
        if (BlocksCommands && keys != null && (keys.escapeKey.wasPressedThisFrame || (IsGathering && keys.rKey.wasPressedThisFrame)))
        { EndGathering(); ClosePreparation(); }
        if (IsGathering)
        {
            if (playback.TryGetSongTime(out double songTime)) lastSongTime = songTime;
            elapsed = Mathf.Min(sessionLimit, elapsed + Time.deltaTime);
            if (elapsed >= sessionLimit || battle.PhaseSecondsRemaining <= preparationReserve)
                EndGathering();
        }
        RefreshHud();
    }
    private void SetForestVisible()
    {
        if (forestCanvases == null) return;
        bool visible = showPreparation || IsGathering;
        foreach (var canvas in forestCanvases)
        {
            canvas.enabled = visible;
            var raycaster = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = visible;
        }
        SetGroup(preparationGroup, showPreparation); SetGroup(playGroup, IsGathering);
    }
    private static void SetGroup(CanvasGroup group, bool visible)
    { if (group != null) { group.alpha = visible ? 1 : 0; group.interactable = group.blocksRaycasts = visible; } }
    private void OnDisable()
    {
        EndGathering(); showPreparation = false; SetForestVisible();
        if (judgments != null) judgments.NoteJudged -= RecordJudgment;
        if (Instance == this) Instance = null;
    }
}
