using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using LocalMeetingSubtitle.App.Infrastructure;
using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.ModelDownloads;
using LocalMeetingSubtitle.Audio;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Audio;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Transcription;
using LocalMeetingSubtitle.Storage;
using Microsoft.Win32;

namespace LocalMeetingSubtitle.App.ViewModels;

/// <summary>
/// The application's main view-model. Owns the transcription pipeline, the in-memory transcript
/// and every command surfaced by the main window. All pipeline events are marshalled onto the
/// WPF dispatcher before touching observable state.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private const int TargetSampleRate = 16000;

    private readonly Dispatcher _dispatcher;
    private readonly IAppLogger _log;
    private readonly ISubtitleRepository _subtitleRepository;
    private readonly IHotwordService _hotwordService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IModelManager _modelManager;
    private readonly ISubtitleExportService _exportService;
    private readonly IPerformanceMonitor _performanceMonitor;
    private readonly Func<WasapiLoopbackCaptureService> _captureFactory;
    private WasapiLoopbackCaptureService? _captureService;

    /// <summary>
    /// Created on first use. Constructing the WASAPI capture service initialises the COM audio
    /// stack, which measured ~5 s on the dev machine; doing it eagerly delayed the first window.
    /// </summary>
    private WasapiLoopbackCaptureService CaptureService => _captureService ??= _captureFactory();
    private readonly Func<IAsrEngine> _engineFactory;
    private readonly ISpeakerRepository _speakerRepository;
    private readonly ISpeakerDiarizationService _diarization;
    private readonly IRecordingService _recording;
    private readonly IAudioAssetRepository _audioAssets;
    private readonly LocalAudioAssetStore _recordingStore;

    private readonly List<SubtitleSegment> _segments = new();
    private readonly Dictionary<long, SpeakerAssignment> _assignments = new();
    private readonly Dictionary<string, Speaker> _speakersById = new();

    private AppSettings _settings = new();
    private MeetingSession? _session;
    private TranscriptionPipeline? _pipeline;
    private IAsrEngine? _ownedEngine;
    private ModelDescriptor? _descriptor;
    private SubtitleLineViewModel? _partial;

    // ---- Optional post-meeting recording (opt-in; off by default) ------------------
    /// <summary>The capture instance whose frames are being recorded (subscription owner).</summary>
    private WasapiLoopbackCaptureService? _recordingCapture;
    private EventHandler<AudioFramesEventArgs>? _recordingFramesHandler;
    private string? _lastRecordingAssetId;
    private string? _lastRecordingPath;
    private bool _lastRecordingTemporary;

    private bool _nativeOk;
    private string _nativeVersion = "";
    private string _nativeError = "";
    private bool _modelInstalled;
    private bool _isBusy;

    private DispatcherTimer? _warningTimer;
    private DispatcherTimer? _healthTimer;
    private string _audioHealthText = "";
    private bool _audioHealthWarning;

    public MainViewModel(
        IShellService shell,
        IAppLogger log,
        ISubtitleRepository subtitleRepository,
        IHotwordService hotwordService,
        ISettingsRepository settingsRepository,
        IModelManager modelManager,
        ISubtitleExportService exportService,
        IPerformanceMonitor performanceMonitor,
        Func<WasapiLoopbackCaptureService> captureFactory,
        Func<IAsrEngine> engineFactory,
        ISpeakerRepository speakerRepository,
        ISpeakerDiarizationService diarization,
        IRecordingService recording,
        IAudioAssetRepository audioAssets,
        LocalAudioAssetStore recordingStore)
    {
        Shell = shell;
        _log = log;
        _subtitleRepository = subtitleRepository;
        _hotwordService = hotwordService;
        _settingsRepository = settingsRepository;
        _modelManager = modelManager;
        _exportService = exportService;
        _performanceMonitor = performanceMonitor;
        _captureFactory = captureFactory;
        _engineFactory = engineFactory;
        _speakerRepository = speakerRepository;
        _diarization = diarization;
        _recording = recording;
        _audioAssets = audioAssets;
        _recordingStore = recordingStore;
        _dispatcher = Dispatcher.CurrentDispatcher;

        StartCommand = new AsyncRelayCommand(StartAsync, () => CanStart);
        PauseCommand = new AsyncRelayCommand(PauseAsync, () => State == TranscriptionState.Transcribing);
        ResumeCommand = new AsyncRelayCommand(ResumeAsync, () => State == TranscriptionState.Paused);
        StopCommand = new AsyncRelayCommand(StopAsync, () => State is TranscriptionState.Transcribing or TranscriptionState.Paused);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => _segments.Count > 0);
        ApplyHotwordsCommand = new AsyncRelayCommand(ReapplyHotwordsAsync, () => _descriptor is not null && _modelInstalled);
        ImportAndDiarizeCommand = new AsyncRelayCommand(ImportAndDiarizeAsync, () => _session is not null && !IsDiarizing);
        ToggleSearchCommand = new RelayCommand(() => IsSearchVisible = !IsSearchVisible);
        OpenSettingsCommand = new RelayCommand(() => Shell.ShowSettings());
        OpenSpeakerManagementCommand = new RelayCommand(() => Shell.ShowSpeakerManagement());
        ToggleFloatingCommand = new RelayCommand(() => Shell.ToggleFloating());
        ReturnToLatestCommand = new RelayCommand(() => IsTailLocked = true);
        DismissWarningCommand = new RelayCommand(() => HasWarning = false);
        RefreshDevicesCommand = new AsyncRelayCommand(RefreshDevicesAsync);

        _performanceMonitor.Sampled += OnPerformanceSampled;
    }

    public IShellService Shell { get; }

    public ObservableCollection<AudioDeviceInfo> Devices { get; } = new();

    public ObservableCollection<SubtitleLineViewModel> Lines { get; } = new();

    /// <summary>The latest few lines, mirrored for the floating subtitle window.</summary>
    public ObservableCollection<SubtitleLineViewModel> FloatingLines { get; } = new();

    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand PauseCommand { get; }
    public AsyncRelayCommand ResumeCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand ExportCommand { get; }
    public AsyncRelayCommand ApplyHotwordsCommand { get; }
    public AsyncRelayCommand ImportAndDiarizeCommand { get; }
    public RelayCommand ToggleSearchCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenSpeakerManagementCommand { get; }
    public RelayCommand ToggleFloatingCommand { get; }
    public RelayCommand ReturnToLatestCommand { get; }
    public RelayCommand DismissWarningCommand { get; }
    public AsyncRelayCommand RefreshDevicesCommand { get; }

    /// <summary>Raised (on the UI thread) whenever a line is added or the partial text changes.</summary>
    public event EventHandler? SubtitleChanged;

    // ---- Status / header -------------------------------------------------

    private TranscriptionState _state = TranscriptionState.Idle;
    public TranscriptionState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(CanChangeDevice));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StateText => State switch
    {
        TranscriptionState.Idle => "空闲 / Idle",
        TranscriptionState.LoadingModel => "加载模型 / Loading model",
        TranscriptionState.Ready => "就绪 / Ready",
        TranscriptionState.Transcribing => "转写中 / Transcribing",
        TranscriptionState.Paused => "已暂停 / Paused",
        TranscriptionState.Faulted => "错误 / Faulted",
        _ => State.ToString()
    };

    private string _statusText = "准备中… / Preparing…";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private bool _isPreparing = true;
    public bool IsPreparing
    {
        get => _isPreparing;
        private set => SetProperty(ref _isPreparing, value);
    }

    private AudioDeviceInfo? _selectedDevice;
    public AudioDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value) && value is not null)
            {
                _settings.LastAudioDeviceId = value.Id;
                SaveSettingsSafe();
                OnPropertyChanged(nameof(DeviceText));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string DeviceText => SelectedDevice?.FriendlyName ?? "（无音频设备 / no device）";

    public bool CanChangeDevice => State is TranscriptionState.Idle or TranscriptionState.Ready or TranscriptionState.Faulted;

    public string ModelName => _descriptor?.DisplayName ?? "（无 / none）";

    public string ModelStatusText => _descriptor is null
        ? "未配置 / not configured"
        : _modelInstalled ? "已安装 / installed" : "未安装 / not installed";

    private double _latencyMs;
    public double LatencyMs
    {
        get => _latencyMs;
        private set
        {
            if (SetProperty(ref _latencyMs, value)) OnPropertyChanged(nameof(LatencyText));
        }
    }

    public string LatencyText => $"{LatencyMs:0} ms";

    /// <summary>
    /// Plain-language diagnosis of where transcription is stuck — model unusable, nothing being
    /// captured, or audio arriving but the recognizer never decoding — so the user can tell what to
    /// fix without reading the log.
    /// </summary>
    public string AudioHealthText
    {
        get => _audioHealthText;
        private set => SetProperty(ref _audioHealthText, value);
    }

    /// <summary>True when <see cref="AudioHealthText"/> reports something the user must act on.</summary>
    public bool HasAudioHealthWarning
    {
        get => _audioHealthWarning;
        private set => SetProperty(ref _audioHealthWarning, value);
    }

    public string HotwordModeText => _hotwordService.Mode switch
    {
        HotwordMode.ModelLevel => "模型级热词 / model-level",
        HotwordMode.TextCorrectionOnly => "仅文本纠正 / text-correction",
        HotwordMode.NotSupported => "不支持 / not supported",
        HotwordMode.LoadFailed => "加载失败 / load failed",
        _ => _hotwordService.Mode.ToString()
    };

    public string HotwordDetailText => _hotwordService.ModeDetail ?? "";

    private double _level;
    public double Level
    {
        get => _level;
        private set => SetProperty(ref _level, value);
    }

    // ---- Warnings (non-modal banner) -------------------------------------

    private string _warningMessage = "";
    public string WarningMessage
    {
        get => _warningMessage;
        private set
        {
            if (SetProperty(ref _warningMessage, value)) OnPropertyChanged(nameof(HasWarning));
        }
    }

    private bool _hasWarning;
    public bool HasWarning
    {
        get => _hasWarning;
        set => SetProperty(ref _hasWarning, value);
    }

    // ---- Auto-scroll -----------------------------------------------------

    private bool _isTailLocked = true;
    /// <summary>True while the view is pinned to the newest line (auto-scroll active).</summary>
    public bool IsTailLocked
    {
        get => _isTailLocked;
        set
        {
            if (SetProperty(ref _isTailLocked, value)) OnPropertyChanged(nameof(ShowReturnToLatest));
        }
    }

    public bool ShowReturnToLatest => !_isTailLocked && AutoScrollEnabled;

    private bool _autoScrollEnabled = true;
    public bool AutoScrollEnabled
    {
        get => _autoScrollEnabled;
        set
        {
            if (SetProperty(ref _autoScrollEnabled, value))
            {
                if (value) IsTailLocked = true;
                OnPropertyChanged(nameof(ShowReturnToLatest));
            }
        }
    }

    // ---- Search ----------------------------------------------------------

    private bool _isSearchVisible;
    public bool IsSearchVisible
    {
        get => _isSearchVisible;
        set => SetProperty(ref _isSearchVisible, value);
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value)) ApplySearch();
        }
    }

    private int _matchCount;
    public int MatchCount
    {
        get => _matchCount;
        private set
        {
            if (SetProperty(ref _matchCount, value)) OnPropertyChanged(nameof(MatchText));
        }
    }

    public string MatchText => string.IsNullOrEmpty(SearchText) ? "" : $"{MatchCount} 条匹配 / match(es)";

    public bool HasLines => Lines.Count > 0;

    // ---- View settings (bound to the UI) ---------------------------------

    private string _subtitleFontFamily = "Microsoft YaHei UI";
    public string SubtitleFontFamily
    {
        get => _subtitleFontFamily;
        private set => SetProperty(ref _subtitleFontFamily, value);
    }

    private double _subtitleFontSize = 16;
    public double SubtitleFontSize
    {
        get => _subtitleFontSize;
        private set => SetProperty(ref _subtitleFontSize, value);
    }

    private double _floatingFontSize = 28;
    public double FloatingFontSize
    {
        get => _floatingFontSize;
        set => SetProperty(ref _floatingFontSize, value);
    }

    private double _floatingBackgroundOpacity = 0.75;
    public double FloatingBackgroundOpacity
    {
        get => _floatingBackgroundOpacity;
        set => SetProperty(ref _floatingBackgroundOpacity, value);
    }

    private bool _floatingClickThrough;
    public bool FloatingClickThrough
    {
        get => _floatingClickThrough;
        set => SetProperty(ref _floatingClickThrough, value);
    }

    public bool MinimizeToTrayOnClose { get; private set; } = true;

    /// <summary>True when the dark theme is active (drives the header theme-toggle icon).</summary>
    public bool IsDarkTheme => string.Equals(_settings.Theme, ThemeManager.Dark, StringComparison.OrdinalIgnoreCase);

    /// <summary>Flips the light/dark theme, applies it immediately and persists the choice.</summary>
    public void ToggleTheme()
    {
        var next = IsDarkTheme ? ThemeManager.Light : ThemeManager.Dark;
        _settings.Theme = next;
        ThemeManager.Apply(next);
        OnPropertyChanged(nameof(IsDarkTheme));
        SaveSettingsSafe();
    }

    /// <summary>Segments of the current session (for export/search).</summary>
    public IReadOnlyList<SubtitleSegment> Segments => _segments;

    /// <summary>The id of the active session, or <c>null</c> before one has started.</summary>
    public string? CurrentSessionId => _session?.SessionId;

    // =====================================================================
    // Speaker diarization (post-meeting, offline)
    // =====================================================================

    private bool _isDiarizing;
    private double _diarizationProgress;
    private string _diarizationStatusText = "";
    private bool _hasSpeakers;
    private SpeakerFilterItem? _selectedSpeakerFilter;

    /// <summary>By-speaker filter entries ("全部 / All" plus one per non-merged speaker).</summary>
    public ObservableCollection<SpeakerFilterItem> SpeakerFilters { get; } = new();

    /// <summary>
    /// The active by-speaker filter. Selecting an entry hides every row whose assigned speaker
    /// does not match; selecting "全部 / All" (a <c>null</c> speaker id) shows everything.
    /// </summary>
    public SpeakerFilterItem? SelectedSpeakerFilter
    {
        get => _selectedSpeakerFilter;
        set
        {
            if (SetProperty(ref _selectedSpeakerFilter, value))
            {
                ApplySpeakerFilter();
            }
        }
    }

    /// <summary>True when there is more than the single "全部 / All" entry, i.e. a filter is worth showing.</summary>
    public bool HasSpeakerFilter => SpeakerFilters.Count > 1;

    /// <summary>True while a post-meeting diarization run is in flight.</summary>
    public bool IsDiarizing
    {
        get => _isDiarizing;
        private set
        {
            if (SetProperty(ref _isDiarizing, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double DiarizationProgress
    {
        get => _diarizationProgress;
        private set => SetProperty(ref _diarizationProgress, value);
    }

    public string DiarizationStatusText
    {
        get => _diarizationStatusText;
        private set
        {
            if (SetProperty(ref _diarizationStatusText, value))
            {
                OnPropertyChanged(nameof(HasDiarizationStatus));
            }
        }
    }

    public bool HasDiarizationStatus => !string.IsNullOrEmpty(_diarizationStatusText);

    /// <summary>True once at least one subtitle in the session has a speaker.</summary>
    public bool HasSpeakers
    {
        get => _hasSpeakers;
        private set => SetProperty(ref _hasSpeakers, value);
    }

    /// <summary>
    /// Runs offline speaker diarization over the current session. When the session has an opt-in
    /// recording it is analyzed directly (no file dialog); otherwise the user picks a file.
    /// </summary>
    public async Task ImportAndDiarizeAsync()
    {
        if (_session is null)
        {
            RaiseWarning("请先开始并结束一次会议，再导入录音 / start and stop a meeting first.");
            return;
        }

        // Prefer this session's own recording when it still exists and is registered.
        string? path = null;
        string? recordingAssetId = null;
        if (!string.IsNullOrWhiteSpace(_lastRecordingAssetId)
            && !string.IsNullOrWhiteSpace(_lastRecordingPath)
            && File.Exists(_lastRecordingPath))
        {
            var asset = await _audioAssets.GetAsync(_lastRecordingAssetId).ConfigureAwait(true);
            if (asset is not null && string.Equals(asset.SessionId, _session.SessionId, StringComparison.Ordinal))
            {
                path = _lastRecordingPath;
                recordingAssetId = _lastRecordingAssetId;
            }
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            path = Shell.PickAudioFile();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
        }

        var usedRecording = recordingAssetId is not null;

        IsDiarizing = true;
        DiarizationProgress = 0;
        DiarizationStatusText = "正在分析录音… / diarizing…";

        try
        {
            var speakerCount = Math.Max(0, _settings.DiarizationSpeakerCount);
            var request = new DiarizationRequest(
                _session.SessionId,
                path,
                speakerCount > 0 ? SpeakerCountMode.Manual : SpeakerCountMode.Auto,
                speakerCount,
                _settings.DiarizationClusteringThreshold,
                recordingAssetId);

            var progress = new Progress<DiarizationProgress>(p =>
            {
                DiarizationProgress = p.Fraction;
                DiarizationStatusText = p.TotalChunks > 0
                    ? $"正在分析 {p.ProcessedChunks}/{p.TotalChunks}… / diarizing {p.Fraction:P0}"
                    : "正在分析… / diarizing…";
            });

            var result = await _diarization.RunAsync(request, progress).ConfigureAwait(true);

            if (result.Success)
            {
                await RefreshSpeakersAsync().ConfigureAwait(true);
                var status = $"完成：{result.SpeakerCount} 位发言人，{result.AssignedSegments} 条字幕已归属 / done";

                // A temporary recording exists only for one analysis: delete it once it has served.
                if (usedRecording && _lastRecordingTemporary && recordingAssetId is not null)
                {
                    await CleanupRecordingAsync(recordingAssetId).ConfigureAwait(true);
                    status += "（临时录音已清除 / temporary recording cleared）";
                }

                DiarizationStatusText = status;
                RaiseWarning($"说话人分离完成 / diarization done：{result.SpeakerCount} speakers, {result.AssignedSegments} segments, {result.NeedsConfirmation} need confirmation.");
            }
            else
            {
                DiarizationStatusText = "分析失败 / failed：" + (result.Error ?? "unknown");
                RaiseWarning("说话人分离失败 / diarization failed：" + (result.Error ?? "unknown"));
            }
        }
        catch (Exception ex)
        {
            _log.Error("Diarization failed", ex);
            DiarizationStatusText = "分析失败 / failed：" + ex.Message;
            RaiseWarning("说话人分离失败 / diarization failed：" + ex.Message);
        }
        finally
        {
            IsDiarizing = false;
        }
    }

    /// <summary>
    /// Loads speakers + assignments for the current session, re-applies the speaker tags to every
    /// row and rebuilds the by-speaker filter. Public so the speaker-management window can push
    /// its edits back into the main view.
    /// </summary>
    public async Task RefreshSpeakersAsync()
    {
        if (_session is null)
        {
            return;
        }

        var speakers = await _speakerRepository.GetSpeakersAsync(_session.SessionId).ConfigureAwait(true);
        var assignments = await _speakerRepository.GetAssignmentsAsync(_session.SessionId).ConfigureAwait(true);

        _speakersById.Clear();
        foreach (var speaker in speakers)
        {
            _speakersById[speaker.SpeakerId] = speaker;
        }

        _assignments.Clear();
        foreach (var assignment in assignments)
        {
            _assignments[assignment.SegmentId] = assignment;
        }

        foreach (var line in Lines)
        {
            ApplySpeakerToLine(line);
        }

        HasSpeakers = _assignments.Values.Any(a => a.SpeakerId is not null);
        RebuildSpeakerFilters(speakers);
        SubtitleChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Rebuilds the filter combo, keeping the current selection when that speaker still exists.</summary>
    private void RebuildSpeakerFilters(IReadOnlyList<Speaker> speakers)
    {
        var previousId = _selectedSpeakerFilter?.SpeakerId;

        SpeakerFilters.Clear();
        SpeakerFilters.Add(new SpeakerFilterItem(null, "全部 / All"));
        foreach (var speaker in speakers.Where(s => !s.IsMerged).OrderBy(s => s.SortOrder))
        {
            SpeakerFilters.Add(new SpeakerFilterItem(speaker.SpeakerId, SpeakerDisplayName(speaker)));
        }

        // Re-point the selection: a stable speaker id wins, otherwise fall back to "All".
        SelectedSpeakerFilter = SpeakerFilters.FirstOrDefault(f => f.SpeakerId == previousId) ?? SpeakerFilters[0];
        OnPropertyChanged(nameof(HasSpeakerFilter));
    }

    private void ApplySpeakerFilter()
    {
        foreach (var line in Lines)
        {
            line.IsSpeakerVisible = IsSpeakerIdVisible(SpeakerIdForLine(line));
        }
    }

    private void ApplySpeakerToLine(SubtitleLineViewModel line)
    {
        var speakerId = SpeakerIdForLine(line);

        line.IsSpeakerVisible = IsSpeakerIdVisible(speakerId);

        if (line.Segment is null
            || speakerId is null
            || !_assignments.TryGetValue(line.Segment.SegmentId, out var assignment)
            || !_speakersById.TryGetValue(speakerId, out var speaker))
        {
            line.SetSpeaker(null, 0, false);
            return;
        }

        line.SetSpeaker(SpeakerDisplayName(speaker), speaker.ColorArgb, assignment.NeedsConfirmation);
    }

    /// <summary>The assigned speaker id for a row, or <c>null</c> when unknown/unassigned.</summary>
    private string? SpeakerIdForLine(SubtitleLineViewModel line) =>
        line.Segment is not null && _assignments.TryGetValue(line.Segment.SegmentId, out var assignment)
            ? assignment.SpeakerId
            : null;

    /// <summary>True when a row with the given speaker passes the active filter (a null filter shows all).</summary>
    private bool IsSpeakerIdVisible(string? speakerId)
    {
        var filterId = _selectedSpeakerFilter?.SpeakerId;
        return filterId is null || string.Equals(filterId, speakerId, StringComparison.Ordinal);
    }

    private static string SpeakerDisplayName(Speaker speaker) =>
        string.IsNullOrWhiteSpace(speaker.DisplayName) ? "发言人 " + speaker.Label : speaker.DisplayName;

    // =====================================================================
    // Optional post-meeting recording (opt-in; off by default)
    // =====================================================================

    /// <summary>
    /// Starts an opt-in recording and registers it as an audio asset. Any failure is contained:
    /// it is logged and surfaced as a non-blocking warning, and transcription keeps running.
    /// </summary>
    private async Task TryStartRecordingAsync(WasapiLoopbackCaptureService capture, MeetingSession session)
    {
        var mode = _settings.RecordingMode;
        try
        {
            LocalDataPaths.EnsureRecordingsDirectory();

            // Every recording file is named <audioAssetId>.wav so the cleanup sweep can tell whether
            // a file is still referenced by a row.
            var assetId = Guid.NewGuid().ToString("N");
            var path = Path.Combine(LocalDataPaths.RecordingsDirectory, assetId + ".wav");

            await _recording.StartAsync(path).ConfigureAwait(true);

            _recordingCapture = capture;
            _recordingFramesHandler = (_, e) => _recording.Write(e.Samples, e.Format);
            capture.FramesAvailable += _recordingFramesHandler;

            var temporary = mode == RecordingMode.Temporary;
            await _audioAssets.AddAsync(new AudioAsset
            {
                AudioAssetId = assetId,
                SessionId = session.SessionId,
                Path = path,
                Kind = temporary ? AudioAssetKind.TempRecording : AudioAssetKind.RetainedRecording,
                IsTemporary = temporary,
                DeleteAfterUtc = temporary ? DateTimeOffset.Now.AddHours(24) : null
            }).ConfigureAwait(true);

            _lastRecordingAssetId = assetId;
            _lastRecordingPath = path;
            _lastRecordingTemporary = temporary;
            _log.Info($"Post-meeting recording enabled ({mode}) → {path}.");
        }
        catch (Exception ex)
        {
            _log.Error("Starting post-meeting recording failed; continuing without it", ex);
            RaiseWarning("录音启动失败，转写继续 / recording failed to start; transcription continues：" + ex.Message);
            await StopRecordingSafeAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Unsubscribes the frames handler, stops the recorder (if running) and updates the asset row
    /// with the finished size/duration. Safe to call when no recording is active.
    /// </summary>
    private async Task StopRecordingSafeAsync()
    {
        var handler = _recordingFramesHandler;
        var capture = _recordingCapture;
        if (capture is not null && handler is not null)
        {
            try
            {
                capture.FramesAvailable -= handler;
            }
            catch (Exception ex)
            {
                _log.Warn($"Unsubscribing the recorder failed: {ex.Message}");
            }
        }

        _recordingFramesHandler = null;
        _recordingCapture = null;

        if (!_recording.IsRecording)
        {
            return;
        }

        try
        {
            var result = await _recording.StopAsync().ConfigureAwait(true);
            _lastRecordingPath = result.Path;

            var assetId = _lastRecordingAssetId;
            if (assetId is not null)
            {
                var asset = await _audioAssets.GetAsync(assetId).ConfigureAwait(true);
                if (asset is not null)
                {
                    asset.SampleRate = result.SampleRate;
                    asset.Channels = result.Channels;
                    asset.DurationMs = (long)result.Duration.TotalMilliseconds;
                    asset.SizeBytes = result.SizeBytes;
                    await _audioAssets.AddAsync(asset).ConfigureAwait(true);
                }
            }

            _log.Info($"Recording stopped: {result.Path} ({result.Duration.TotalSeconds:0.0}s, {result.SizeBytes} bytes).");
        }
        catch (Exception ex)
        {
            _log.Error("Stopping the recording failed", ex);
        }
    }

    /// <summary>Crash recovery: removes expired temporary recordings and orphaned files on startup.</summary>
    private async Task RunRecordingCleanupAsync()
    {
        try
        {
            var deleted = await _recordingStore.CleanupAsync(_audioAssets, DateTimeOffset.Now).ConfigureAwait(true);
            if (deleted > 0)
            {
                _log.Info($"Recording cleanup removed {deleted} file(s).");
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Recording cleanup failed: {ex.Message}");
        }
    }

    /// <summary>Deletes a temporary recording's file (best effort) and its registry row.</summary>
    private async Task CleanupRecordingAsync(string audioAssetId)
    {
        try
        {
            var asset = await _audioAssets.GetAsync(audioAssetId).ConfigureAwait(true);
            if (asset is not null && File.Exists(asset.Path))
            {
                try
                {
                    File.Delete(asset.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log.Warn($"Deleting the temporary recording file failed: {ex.Message}");
                }
            }

            await _audioAssets.DeleteAsync(audioAssetId).ConfigureAwait(true);

            if (string.Equals(_lastRecordingAssetId, audioAssetId, StringComparison.Ordinal))
            {
                _lastRecordingAssetId = null;
                _lastRecordingPath = null;
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Cleaning up the temporary recording failed: {ex.Message}");
        }
    }

    // =====================================================================
    // Initialization
    // =====================================================================

    public async Task InitializeAsync()
    {
        try
        {
            _settings = await _settingsRepository.LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to load settings; using defaults", ex);
            _settings = new AppSettings();
        }

        ApplySettings(_settings);

        // Crash recovery: drop expired temporary recordings and orphaned files left by a prior run.
        await RunRecordingCleanupAsync().ConfigureAwait(true);

        await Task.Run(() =>
        {
            _nativeOk = SherpaNativeProbe.TryLoad(out var version, out var error);
            _nativeVersion = version ?? "";
            _nativeError = error ?? "";
            ResolveModel();
        }).ConfigureAwait(true);

        OnPropertyChanged(nameof(ModelName));
        OnPropertyChanged(nameof(ModelStatusText));

        await RefreshDevicesAsync().ConfigureAwait(true);

        IsPreparing = false;
        UpdateReadinessStatus();

        _healthTimer ??= CreateHealthTimer();
        _healthTimer.Start();
        UpdateAudioHealth();

        _log.Info($"Startup check complete: native={_nativeOk} ({_nativeVersion}), model={_descriptor?.Id ?? "none"} installed={_modelInstalled}, devices={Devices.Count}.");
    }

    private void ResolveModel()
    {
        ModelDescriptor? descriptor = null;
        if (!string.IsNullOrWhiteSpace(_settings.LastModelId))
        {
            descriptor = _modelManager.FindById(_settings.LastModelId!);
        }

        descriptor ??= _modelManager.Catalog.FirstOrDefault(m => _modelManager.IsInstalled(m));
        descriptor ??= _modelManager.Catalog.FirstOrDefault();

        _descriptor = descriptor;
        _modelInstalled = descriptor is not null && _modelManager.IsInstalled(descriptor);

        if (descriptor is not null) _settings.LastModelId = descriptor.Id;
    }

    private async Task RefreshDevicesAsync()
    {
        var previousId = SelectedDevice?.Id ?? _settings.LastAudioDeviceId;
        try
        {
            // COM/audio-stack initialisation and enumeration run off the UI thread.
            var found = await Task.Run(() => CaptureService.EnumerateDevices()).ConfigureAwait(true);
            Devices.Clear();
            foreach (var device in found) Devices.Add(device);
        }
        catch (Exception ex)
        {
            _log.Error("Enumerating audio devices failed", ex);
            Devices.Clear();
        }

        var chosen = (previousId is null ? null : Devices.FirstOrDefault(d => d.Id == previousId))
                     ?? Devices.FirstOrDefault(d => d.IsDefault)
                     ?? Devices.FirstOrDefault();
        SelectedDevice = chosen;

        if (Devices.Count == 0)
        {
            RaiseWarning("未发现可捕获的音频设备（扬声器/耳机）。请检查系统音频输出。/ No capturable audio device found.");
        }
    }

    private void UpdateReadinessStatus()
    {
        if (State is not (TranscriptionState.Idle or TranscriptionState.Ready)) return;

        if (!_nativeOk)
        {
            StatusText = $"未找到 sherpa-onnx 原生库 / native library missing：{_nativeError}";
            return;
        }
        if (_descriptor is null)
        {
            StatusText = "未配置语音模型 / no ASR model configured";
            return;
        }
        if (!_modelInstalled)
        {
            StatusText = $"模型未安装 / model not installed：{_descriptor.DisplayName}";
            return;
        }
        StatusText = $"就绪 / Ready — {_descriptor.DisplayName}";
    }

    // =====================================================================
    // Commands
    // =====================================================================

    private bool CanStart =>
        !_isPreparing && !_isBusy && _nativeOk && _modelInstalled &&
        SelectedDevice is not null && State is TranscriptionState.Idle or TranscriptionState.Ready;

    private async Task StartAsync()
    {
        if (!CanStart) return;

        var descriptor = _descriptor;
        var device = SelectedDevice;
        if (descriptor is null || device is null)
        {
            RaiseWarning("无法开始：缺少模型或音频设备。/ Cannot start: missing model or device.");
            return;
        }

        _isBusy = true;
        State = TranscriptionState.LoadingModel;
        StatusText = "加载模型 / Loading model…";
        RelayCommand.RaiseCanExecuteChanged();

        IAsrEngine? engine = null;
        try
        {
            _hotwordService.SetEngineCapability(descriptor.SupportsHotwords, true);
            await _hotwordService.ReloadAsync().ConfigureAwait(true);

            var hotwordPath = BuildHotwordFile();
            var options = AsrOptionsFactory.FromDescriptor(descriptor, _modelManager.ModelsRoot, hotwordPath, _settings.AsrNumThreads);

            engine = _engineFactory();
            _ownedEngine = engine;
            var init = await Task.Run(async () => await engine.InitializeAsync(options).ConfigureAwait(false)).ConfigureAwait(true);

            if (!init.Ok)
            {
                engine.Dispose();
                engine = null;
                _ownedEngine = null;
                State = TranscriptionState.Faulted;
                StatusText = "模型加载失败 / model load failed";
                RaiseWarning(init.Message);
                return;
            }

            var capture = new WasapiLoopbackCaptureService(_log);
            var preprocessor = new DefaultAudioPreprocessor(TargetSampleRate);
            var pipeline = new TranscriptionPipeline(
                capture, preprocessor, engine, _hotwordService, _subtitleRepository,
                _performanceMonitor, _log, new PipelineOptions());

            pipeline.TranscriptUpdated += OnTranscriptUpdated;
            pipeline.StatusChanged += OnStatusChanged;
            pipeline.LevelChanged += OnLevelChanged;
            pipeline.Overload += OnOverload;
            pipeline.ErrorOccurred += OnErrorOccurred;

            var session = new MeetingSession
            {
                Title = $"Meeting {DateTime.Now:yyyy-MM-dd HH:mm}",
                ModelId = descriptor.Id,
                AudioDeviceId = device.Id,
                AudioDeviceName = device.FriendlyName
            };

            await _subtitleRepository.CreateSessionAsync(session).ConfigureAwait(true);
            _session = session;

            ClearTranscript();
            _pipeline = pipeline;

            await pipeline.StartAsync(device.Id, session).ConfigureAwait(true);
            _performanceMonitor.Start();
            _log.Info($"Session {session.SessionId} started on '{device.FriendlyName}' with {descriptor.Id}.");

            // Optional post-meeting recording. A failure here must never break transcription.
            if (_settings.RecordingMode != RecordingMode.None)
            {
                await TryStartRecordingAsync(capture, session).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _log.Error("Failed to start transcription", ex);
            await TearDownPipelineAsync().ConfigureAwait(true);
            State = TranscriptionState.Faulted;
            StatusText = "启动失败 / start failed";
            RaiseWarning("启动失败 / start failed：" + ex.Message);
        }
        finally
        {
            _isBusy = false;
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task PauseAsync()
    {
        if (_pipeline is null) return;
        try
        {
            await _pipeline.PauseAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Error("Pause failed", ex);
            RaiseWarning("暂停失败 / pause failed：" + ex.Message);
        }
    }

    private async Task ResumeAsync()
    {
        if (_pipeline is null) return;
        try
        {
            await _pipeline.ResumeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Error("Resume failed", ex);
            RaiseWarning("恢复失败 / resume failed：" + ex.Message);
        }
    }

    private async Task StopAsync()
    {
        try
        {
            await TearDownPipelineAsync().ConfigureAwait(true);
            StatusText = "已停止 / Stopped";
        }
        catch (Exception ex)
        {
            _log.Error("Stop failed", ex);
            RaiseWarning("停止失败 / stop failed：" + ex.Message);
        }
    }

    /// <summary>Stops transcription (if running) and disposes the pipeline. Safe to call multiple times.</summary>
    public async Task StopIfRunningAsync()
    {
        await TearDownPipelineAsync().ConfigureAwait(true);
    }

    private async Task TearDownPipelineAsync()
    {
        var pipeline = _pipeline;
        _pipeline = null;
        if (pipeline is not null)
        {
            pipeline.TranscriptUpdated -= OnTranscriptUpdated;
            pipeline.StatusChanged -= OnStatusChanged;
            pipeline.LevelChanged -= OnLevelChanged;
            pipeline.Overload -= OnOverload;
            pipeline.ErrorOccurred -= OnErrorOccurred;
            try
            {
                await pipeline.StopAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _log.Error("Pipeline stop failed", ex);
            }
            try
            {
                await pipeline.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _log.Warn($"Pipeline dispose failed: {ex.Message}");
            }
        }

        _performanceMonitor.Stop();

        // The pipeline does not own/dispose the engine, so we do it here (idempotent).
        var engine = _ownedEngine;
        _ownedEngine = null;
        if (engine is not null)
        {
            try { engine.Dispose(); }
            catch (Exception ex) { _log.Warn($"Engine dispose failed: {ex.Message}"); }
        }

        // Stop any opt-in recording and persist its final size/duration.
        await StopRecordingSafeAsync().ConfigureAwait(true);

        if (State is TranscriptionState.Transcribing or TranscriptionState.Paused or TranscriptionState.LoadingModel)
        {
            State = TranscriptionState.Ready;
        }
        UpdateReadinessStatus();
    }

    private async Task ExportAsync()
    {
        if (_segments.Count == 0)
        {
            RaiseWarning("当前没有可导出的字幕 / nothing to export yet.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出字幕 / Export subtitles",
            FileName = $"transcript-{DateTime.Now:yyyyMMdd-HHmmss}",
            InitialDirectory = EnsureExportsDirectory(),
            Filter = "文本 / Text (*.txt)|*.txt|字幕 / SubRip (*.srt)|*.srt|Markdown (*.md)|*.md|CSV (*.csv)|*.csv",
            FilterIndex = 1,
            AddExtension = true
        };

        if (dialog.ShowDialog() != true) return;

        var format = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
        {
            ".srt" => ExportFormat.Srt,
            ".md" or ".markdown" => ExportFormat.Markdown,
            ".csv" => ExportFormat.Csv,
            _ => ExportFormat.Txt
        };

        // Populate the speaker column from the current assignments so speaker-aware formats can
        // render it. SpeakerName lives only on the export copy, never in the database.
        foreach (var segment in _segments)
        {
            segment.SpeakerName = SpeakerNameForSegment(segment.SegmentId);
        }

        var session = _session ?? new MeetingSession { Title = "Export" };
        var request = new ExportRequest(session, _segments.ToList(), format, dialog.FileName, _settings.ExportIncludeTimestamps);

        try
        {
            await _exportService.ExportAsync(request).ConfigureAwait(true);
            RaiseWarning($"已导出 / exported {_segments.Count} → {dialog.FileName}");
            _log.Info($"Exported {_segments.Count} segments to {dialog.FileName} ({format}).");
        }
        catch (Exception ex)
        {
            _log.Error("Export failed", ex);
            RaiseWarning("导出失败 / export failed：" + ex.Message);
        }
    }

    /// <summary>
    /// Resolves the speaker display name for one segment from the in-memory assignments: an
    /// anonymous speaker renders as "发言人 &lt;label&gt;", a renamed one as its <c>DisplayName</c>,
    /// and an unassigned/unknown segment yields <c>null</c> so the formatters omit the column.
    /// </summary>
    private string? SpeakerNameForSegment(long segmentId)
    {
        if (!_assignments.TryGetValue(segmentId, out var assignment)
            || assignment.SpeakerId is null
            || !_speakersById.TryGetValue(assignment.SpeakerId, out var speaker))
        {
            return null;
        }

        return SpeakerDisplayName(speaker);
    }

    /// <summary>Reloads hotwords and, when transcribing, hot-swaps the engine (applies next utterance).</summary>
    public async Task ReapplyHotwordsAsync()
    {
        try
        {
            _hotwordService.SetEngineCapability(_descriptor?.SupportsHotwords ?? false, true);
            await _hotwordService.ReloadAsync().ConfigureAwait(true);
            OnPropertyChanged(nameof(HotwordModeText));
            OnPropertyChanged(nameof(HotwordDetailText));

            if (_pipeline is null || State is not (TranscriptionState.Transcribing or TranscriptionState.Paused) || _descriptor is null)
            {
                RaiseWarning("热词已重新加载（将在下次转写时生效）/ hotwords reloaded (applies next time).");
                return;
            }

            var hotwordPath = BuildHotwordFile();
            var options = AsrOptionsFactory.FromDescriptor(_descriptor, _modelManager.ModelsRoot, hotwordPath, _settings.AsrNumThreads);
            var newEngine = _engineFactory();
            var init = await Task.Run(async () => await newEngine.InitializeAsync(options).ConfigureAwait(false)).ConfigureAwait(true);

            if (!init.Ok)
            {
                newEngine.Dispose();
                RaiseWarning("应用热词失败 / applying hotwords failed：" + init.Message);
                return;
            }

            _pipeline.RequestEngineSwap(newEngine);
            _ownedEngine = newEngine; // the pipeline disposes the old engine; we own the new one
            RaiseWarning("热词已更新，从下一句话开始生效 / hotwords updated — applies from the next utterance.");
            _log.Info("Requested engine swap to apply updated hotwords.");
        }
        catch (Exception ex)
        {
            _log.Error("Reapplying hotwords failed", ex);
            RaiseWarning("应用热词失败 / applying hotwords failed：" + ex.Message);
        }
    }

    // =====================================================================
    // Pipeline event handlers (raised off the UI thread)
    // =====================================================================

    private void OnTranscriptUpdated(object? sender, TranscriptEvent ev) => Dispatch(() => HandleTranscript(ev));

    private void OnStatusChanged(object? sender, TranscriptionStatus status) => Dispatch(() =>
    {
        State = status.State;
        StatusText = status.Message;
        OnPropertyChanged(nameof(HotwordModeText));
        OnPropertyChanged(nameof(HotwordDetailText));
    });

    private void OnLevelChanged(object? sender, double level) => Dispatch(() => Level = Math.Clamp(level, 0, 1));

    private void OnOverload(object? sender, PipelineOverloadEventArgs args) => Dispatch(() => RaiseWarning(args.Message));

    private void OnErrorOccurred(object? sender, string message) => Dispatch(() => RaiseWarning(message));

    private void OnPerformanceSampled(object? sender, PerformanceSnapshot snapshot)
        => Dispatch(() => LatencyMs = snapshot.EndToEndLatencyMs);

    private void HandleTranscript(TranscriptEvent ev)
    {
        switch (ev.Kind)
        {
            case TranscriptEventKind.Partial:
                if (_partial is null)
                {
                    _partial = new SubtitleLineViewModel(ev.SequenceNumber, ev.StartOffset, ev.CorrectedText, isFinal: false);
                    Lines.Add(_partial);
                    OnPropertyChanged(nameof(HasLines));
                }
                else
                {
                    _partial.Text = ev.CorrectedText;
                }
                break;

            case TranscriptEventKind.Final:
                var segment = new SubtitleSegment
                {
                    SessionId = _session?.SessionId ?? "",
                    SequenceNumber = ev.SequenceNumber,
                    StartOffset = ev.StartOffset,
                    EndOffset = ev.EndOffset,
                    OriginalText = ev.Text,
                    CorrectedText = ev.CorrectedText
                };

                SubtitleLineViewModel line;
                if (_partial is not null)
                {
                    line = _partial;
                    _partial = null;
                    line.MakeFinal(ev.SequenceNumber, ev.StartOffset, ev.CorrectedText);
                }
                else
                {
                    line = new SubtitleLineViewModel(ev.SequenceNumber, ev.StartOffset, ev.CorrectedText, isFinal: true);
                    Lines.Add(line);
                    OnPropertyChanged(nameof(HasLines));
                }

                line.Segment = segment;
                _segments.Add(segment);
                ApplySearchTo(line);
                ApplySpeakerToLine(line);
                RelayCommand.RaiseCanExecuteChanged();
                break;

            case TranscriptEventKind.Cleared:
                if (_partial is not null)
                {
                    Lines.Remove(_partial);
                    _partial = null;
                    OnPropertyChanged(nameof(HasLines));
                }
                break;
        }

        UpdateFloatingLines();
        SubtitleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateFloatingLines()
    {
        const int max = 3;
        var start = Math.Max(0, Lines.Count - max);
        FloatingLines.Clear();
        for (int i = start; i < Lines.Count; i++) FloatingLines.Add(Lines[i]);
    }

    private void ClearTranscript()
    {
        _partial = null;
        Lines.Clear();
        FloatingLines.Clear();
        _segments.Clear();
        _assignments.Clear();
        _speakersById.Clear();
        HasSpeakers = false;
        SpeakerFilters.Clear();
        _selectedSpeakerFilter = null;
        OnPropertyChanged(nameof(SelectedSpeakerFilter));
        OnPropertyChanged(nameof(HasSpeakerFilter));
        MatchCount = 0;
        OnPropertyChanged(nameof(HasLines));
        SubtitleChanged?.Invoke(this, EventArgs.Empty);
    }

    // =====================================================================
    // Search
    // =====================================================================

    private void ApplySearch()
    {
        int matches = 0;
        foreach (var line in Lines)
        {
            ApplySearchTo(line);
            if (line.IsMatch) matches++;
        }
        MatchCount = matches;
        OnPropertyChanged(nameof(MatchText));
    }

    private void ApplySearchTo(SubtitleLineViewModel line)
    {
        if (!line.IsFinal || string.IsNullOrEmpty(SearchText))
        {
            line.IsMatch = false;
            return;
        }
        line.IsMatch = line.Text.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    // =====================================================================
    // Settings
    // =====================================================================

    /// <summary>Applies a freshly loaded/saved settings object to the UI-facing state.</summary>
    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        SubtitleFontFamily = string.IsNullOrWhiteSpace(settings.SubtitleFontFamily) ? "Microsoft YaHei UI" : settings.SubtitleFontFamily!;
        SubtitleFontSize = settings.SubtitleFontSize <= 0 ? 16 : settings.SubtitleFontSize;
        FloatingFontSize = settings.FloatingFontSize <= 0 ? 28 : settings.FloatingFontSize;
        FloatingBackgroundOpacity = Math.Clamp(settings.FloatingBackgroundOpacity, 0.1, 1.0);
        FloatingClickThrough = settings.FloatingClickThrough;
        MinimizeToTrayOnClose = settings.MinimizeToTrayOnClose;
        AutoScrollEnabled = settings.AutoScrollEnabled;

        // The built-in lexicon is applied through the hotword service (effective on the next
        // engine build; ReapplyHotwordsAsync applies it immediately while transcribing).
        _hotwordService.UseBuiltInLexicon = settings.UseBuiltInLexicon;

        var theme = string.IsNullOrWhiteSpace(settings.Theme) ? ThemeManager.Light : settings.Theme;
        if (!string.Equals(theme, ThemeManager.Current, StringComparison.OrdinalIgnoreCase))
        {
            ThemeManager.Apply(theme);
        }
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    public AppSettings CurrentSettings => _settings;

    private async void SaveSettingsSafe()
    {
        try
        {
            await _settingsRepository.SaveAsync(_settings).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Warn($"Saving settings failed: {ex.Message}");
        }
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary>Writes the model-level hotwords file to a temp path (null when not applicable).</summary>
    public string? BuildHotwordFile()
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "字幕君");
            var path = Path.Combine(dir, "hotwords.txt");
            return _hotwordService.WriteModelHotwordFile(path);
        }
        catch (Exception ex)
        {
            _log.Warn($"Writing hotwords file failed: {ex.Message}");
            return null;
        }
    }

    private static string EnsureExportsDirectory()
    {
        try
        {
            return LocalMeetingSubtitle.Storage.LocalDataPaths.EnsureExportsDirectory();
        }
        catch
        {
            return Path.GetTempPath();
        }
    }

    private void RaiseWarning(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        Dispatch(() =>
        {
            WarningMessage = message;
            HasWarning = true;
            _warningTimer ??= CreateWarningTimer();
            _warningTimer.Stop();
            _warningTimer.Start();
        });
    }

    private DispatcherTimer CreateWarningTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            HasWarning = false;
        };
        return timer;
    }

    private DispatcherTimer CreateHealthTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => UpdateAudioHealth();
        return timer;
    }

    /// <summary>
    /// Works out which link of the chain is stopping subtitles from appearing and shows it in the
    /// footer: unusable model, no audio being captured, or audio captured but never decoded.
    /// </summary>
    private void UpdateAudioHealth()
    {
        if (!_nativeOk)
        {
            SetAudioHealth("原生库缺失 / native library missing", warning: true);
            return;
        }

        if (_descriptor is null)
        {
            SetAudioHealth("未配置模型 / no model configured", warning: true);
            return;
        }

        if (!_modelInstalled)
        {
            SetAudioHealth("模型未安装 / model not installed", warning: true);
            return;
        }

        // sherpa-onnx's native layer cannot read model files from a non-ASCII path, so warn before
        // the recognizer silently produces nothing.
        var modelsRoot = _modelManager.ModelsRoot;
        if (!string.IsNullOrEmpty(modelsRoot) && modelsRoot.Any(c => c > 127))
        {
            SetAudioHealth("模型路径含非 ASCII 字符，无法识别 — 请把程序与数据放到纯英文路径 / model path is non-ASCII", true);
            return;
        }

        var pipeline = _pipeline;
        if (pipeline is null)
        {
            SetAudioHealth("就绪（未开始）/ ready", warning: false);
            return;
        }

        var d = pipeline.GetDiagnostics();
        if (!d.Running)
        {
            SetAudioHealth("就绪（未开始）/ ready", warning: false);
        }
        else if (d.FramesReceived == 0 || d.SecondsSinceLastFrame > 2.0)
        {
            SetAudioHealth("未采集到声音 — 请检查播放设备 / no audio captured", true);
        }
        else if (d.Decodes == 0)
        {
            double seconds = d.SamplesAccepted / (double)TargetSampleRate;
            SetAudioHealth($"已采集 {seconds:0.0}s 音频，识别器未就绪（正在自动重建会话）/ {seconds:0.0}s captured, recognizer not ready — rebuilding", true);
        }
        else
        {
            SetAudioHealth("识别中 / recognizing", warning: false);
        }
    }

    private void SetAudioHealth(string text, bool warning)
    {
        AudioHealthText = text;
        HasAudioHealthWarning = warning;
    }

    private void Dispatch(Action action)
    {
        try
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.BeginInvoke(action);
        }
        catch (Exception ex)
        {
            _log.Warn($"Dispatching UI update failed: {ex.Message}");
        }
    }
}
