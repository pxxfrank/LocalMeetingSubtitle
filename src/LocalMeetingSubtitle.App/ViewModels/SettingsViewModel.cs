using System.Collections.ObjectModel;
using System.IO;
using LocalMeetingSubtitle.App.Infrastructure;
using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Hotwords;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.App.ViewModels;

/// <summary>Backs the settings window: subtitle fonts, floating-subtitle options, the recognition model, ASR threads and hotwords.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private static readonly string[] FontFamilies =
    {
        "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "SimHei", "SimSun", "Arial"
    };

    private readonly ISettingsRepository _settingsRepository;
    private readonly IHotwordRepository _hotwordRepository;
    private readonly IModelManager _modelManager;
    private readonly MainViewModel _main;
    private readonly IAppLogger _log;

    private AppSettings _settings = new();
    private CancellationTokenSource? _downloadCts;

    public SettingsViewModel(
        ISettingsRepository settingsRepository,
        IHotwordRepository hotwordRepository,
        IModelManager modelManager,
        MainViewModel main,
        IAppLogger log)
    {
        _settingsRepository = settingsRepository;
        _hotwordRepository = hotwordRepository;
        _modelManager = modelManager;
        _main = main;
        _log = log;

        Models = AsrModelCatalog.StreamingModels;
        _selectedModel = Models.FirstOrDefault() ?? AsrModelCatalog.StreamingZipformerZh14M;

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
        DownloadCommand = new AsyncRelayCommand(DownloadAsync, () => CanDownload);
        CancelDownloadCommand = new RelayCommand(CancelDownload, () => IsDownloading);
    }

    public ObservableCollection<string> AvailableFontFamilies { get; } = new(FontFamilies);

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CloseCommand { get; }
    public AsyncRelayCommand DownloadCommand { get; }
    public RelayCommand CancelDownloadCommand { get; }

    public event EventHandler? CloseRequested;

    // ---- Recognition model ------------------------------------------------

    /// <summary>Live-path models the user may choose from (offline models are excluded).</summary>
    public IReadOnlyList<ModelDescriptor> Models { get; }

    private ModelDescriptor _selectedModel;
    public ModelDescriptor SelectedModel
    {
        get => _selectedModel;
        set
        {
            // WPF can push a transient null through SelectedItem while ItemsSource settles; the list
            // never legitimately has "no selection", and a null would break the status properties.
            if (value is null || ReferenceEquals(value, _selectedModel)) return;
            if (SetProperty(ref _selectedModel, value))
            {
                DownloadError = "";
                OnPropertyChanged(nameof(SelectedModelStatusText));
                OnPropertyChanged(nameof(SelectedModelCaveat));
                OnPropertyChanged(nameof(IsSelectedModelInstalled));
                OnPropertyChanged(nameof(ShowDownloadButton));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsSelectedModelInstalled => _modelManager.IsInstalled(SelectedModel);

    /// <summary>True while the selected model still has to be downloaded.</summary>
    public bool ShowDownloadButton => !IsSelectedModelInstalled;

    public string SelectedModelStatusText => IsSelectedModelInstalled
        ? $"已安装 / installed（{FormatMegabytes(SelectedModel.ApproxSizeBytes)}）"
        : $"未安装 / not installed — 需要下载 / download required（{FormatMegabytes(SelectedModel.ApproxSizeBytes)}）";

    /// <summary>
    /// States truthfully how domain terms take effect for this model: the bilingual model's
    /// model-level hotwords measured as ineffective, so it advertises correction rules only.
    /// </summary>
    public string SelectedModelCaveat => SelectedModel.SupportsHotwords
        ? "该模型支持模型级热词：术语既参与模型加权，也参与文本纠正。/ Supports model-level hotwords: terms are boosted and corrected."
        : "该模型不支持模型级热词：术语仅通过内置/自定义文本纠正规则生效。/ No model-level hotwords: terms apply through the correction rules only.";

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (SetProperty(ref _isDownloading, value)) RelayCommand.RaiseCanExecuteChanged();
        }
    }

    private double _downloadProgress;
    public double DownloadProgress { get => _downloadProgress; private set => SetProperty(ref _downloadProgress, value); }

    private string _downloadError = "";
    public string DownloadError
    {
        get => _downloadError;
        private set
        {
            if (SetProperty(ref _downloadError, value)) OnPropertyChanged(nameof(HasDownloadError));
        }
    }

    public bool HasDownloadError => !string.IsNullOrEmpty(DownloadError);

    private bool CanDownload => !IsDownloading && !IsSelectedModelInstalled;

    private string _subtitleFontFamily = "Microsoft YaHei UI";
    public string SubtitleFontFamily { get => _subtitleFontFamily; set => SetProperty(ref _subtitleFontFamily, value); }

    private double _subtitleFontSize = 16;
    public double SubtitleFontSize { get => _subtitleFontSize; set => SetProperty(ref _subtitleFontSize, value); }

    private double _floatingFontSize = 28;
    public double FloatingFontSize { get => _floatingFontSize; set => SetProperty(ref _floatingFontSize, value); }

    private double _floatingBackgroundOpacity = 0.75;
    public double FloatingBackgroundOpacity { get => _floatingBackgroundOpacity; set => SetProperty(ref _floatingBackgroundOpacity, value); }

    private bool _floatingClickThrough;
    public bool FloatingClickThrough { get => _floatingClickThrough; set => SetProperty(ref _floatingClickThrough, value); }

    private int _asrNumThreads;
    public int AsrNumThreads { get => _asrNumThreads; set => SetProperty(ref _asrNumThreads, value); }

    private bool _minimizeToTrayOnClose = true;
    public bool MinimizeToTrayOnClose { get => _minimizeToTrayOnClose; set => SetProperty(ref _minimizeToTrayOnClose, value); }

    private bool _autoScrollEnabled = true;
    public bool AutoScrollEnabled { get => _autoScrollEnabled; set => SetProperty(ref _autoScrollEnabled, value); }

    private bool _exportIncludeTimestamps = true;
    public bool ExportIncludeTimestamps { get => _exportIncludeTimestamps; set => SetProperty(ref _exportIncludeTimestamps, value); }

    private bool _enableVadSegmenting = true;
    public bool EnableVadSegmenting { get => _enableVadSegmenting; set => SetProperty(ref _enableVadSegmenting, value); }

    private bool _useBuiltInLexicon = true;
    /// <summary>Merge the built-in Huawei-domain lexicon into hotwords + correction rules (default on).</summary>
    public bool UseBuiltInLexicon { get => _useBuiltInLexicon; set => SetProperty(ref _useBuiltInLexicon, value); }

    /// <summary>How many terms the built-in lexicon contributes (shown in the settings UI).</summary>
    public int LexiconTermCount => BuiltInLexicon.HotwordCount;

    private RecordingMode _recordingMode = RecordingMode.None;
    /// <summary>Opt-in post-meeting recording mode; defaults to <see cref="RecordingMode.None"/>.</summary>
    public RecordingMode RecordingMode { get => _recordingMode; set => SetProperty(ref _recordingMode, value); }

    /// <summary>The three recording modes offered by the settings combo box.</summary>
    public IReadOnlyList<RecordingModeOption> RecordingModes { get; } = new[]
    {
        new RecordingModeOption(RecordingMode.None, "不录音 / None"),
        new RecordingModeOption(RecordingMode.Temporary, "临时录音 / Temporary"),
        new RecordingModeOption(RecordingMode.Retain, "保留录音 / Retain")
    };

    private string _hotwordsText = "";
    public string HotwordsText { get => _hotwordsText; set => SetProperty(ref _hotwordsText, value); }

    private string _theme = ThemeManager.Light;
    /// <summary>"Light" or "Dark" — applied live (preview) and persisted with the other settings.</summary>
    public string Theme
    {
        get => _theme;
        set
        {
            var normalized = string.Equals(value, ThemeManager.Dark, StringComparison.OrdinalIgnoreCase)
                ? ThemeManager.Dark
                : ThemeManager.Light;
            if (SetProperty(ref _theme, normalized))
            {
                ThemeManager.Apply(normalized);
            }
        }
    }

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value)) OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public async Task LoadAsync()
    {
        try
        {
            _settings = await _settingsRepository.LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to load settings for the settings window", ex);
            _settings = new AppSettings();
        }

        SubtitleFontFamily = _settings.SubtitleFontFamily ?? "Microsoft YaHei UI";
        SubtitleFontSize = _settings.SubtitleFontSize <= 0 ? 16 : _settings.SubtitleFontSize;
        FloatingFontSize = _settings.FloatingFontSize <= 0 ? 28 : _settings.FloatingFontSize;
        FloatingBackgroundOpacity = _settings.FloatingBackgroundOpacity;
        FloatingClickThrough = _settings.FloatingClickThrough;
        AsrNumThreads = _settings.AsrNumThreads;
        MinimizeToTrayOnClose = _settings.MinimizeToTrayOnClose;
        AutoScrollEnabled = _settings.AutoScrollEnabled;
        ExportIncludeTimestamps = _settings.ExportIncludeTimestamps;
        EnableVadSegmenting = _settings.EnableVadSegmenting;
        UseBuiltInLexicon = _settings.UseBuiltInLexicon;
        RecordingMode = _settings.RecordingMode;
        Theme = string.IsNullOrWhiteSpace(_settings.Theme) ? ThemeManager.Light : _settings.Theme;

        SelectedModel = Models.FirstOrDefault(m => string.Equals(m.Id, _settings.LastModelId, StringComparison.OrdinalIgnoreCase))
            ?? _selectedModel;

        try
        {
            var hotwords = await _hotwordRepository.GetHotwordsAsync().ConfigureAwait(true);
            HotwordsText = string.Join(Environment.NewLine, hotwords.Select(h => h.Text));
        }
        catch (Exception ex)
        {
            _log.Error("Failed to load hotwords", ex);
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            var chosenModel = SelectedModel;
            var modelChanged = !string.Equals(chosenModel.Id, _settings.LastModelId, StringComparison.Ordinal);
            if (modelChanged && !_modelManager.IsInstalled(chosenModel))
            {
                // Never persist a model that cannot be loaded: Start would silently stay disabled.
                StatusMessage = "该模型尚未下载，请先下载 / that model is not downloaded yet.";
                return;
            }

            _settings.SubtitleFontFamily = string.IsNullOrWhiteSpace(SubtitleFontFamily) ? "Microsoft YaHei UI" : SubtitleFontFamily;
            _settings.SubtitleFontSize = SubtitleFontSize;
            _settings.FloatingFontSize = FloatingFontSize;
            _settings.FloatingBackgroundOpacity = Math.Clamp(FloatingBackgroundOpacity, 0.1, 1.0);
            _settings.FloatingClickThrough = FloatingClickThrough;
            _settings.AsrNumThreads = Math.Max(0, AsrNumThreads);
            _settings.MinimizeToTrayOnClose = MinimizeToTrayOnClose;
            _settings.AutoScrollEnabled = AutoScrollEnabled;
            _settings.ExportIncludeTimestamps = ExportIncludeTimestamps;
            _settings.EnableVadSegmenting = EnableVadSegmenting;
            _settings.UseBuiltInLexicon = UseBuiltInLexicon;
            _settings.RecordingMode = RecordingMode;
            _settings.Theme = Theme;
            if (modelChanged) _settings.LastModelId = chosenModel.Id;

            await _settingsRepository.SaveAsync(_settings).ConfigureAwait(true);

            var hotwords = ParseHotwords();
            await _hotwordRepository.ReplaceHotwordsAsync(hotwords).ConfigureAwait(true);

            // One engine rebuild covers both changes: SelectModel only records the choice, and
            // ReapplyHotwordsAsync re-reads the descriptor, sets its hotword capability and hot-swaps.
            if (modelChanged) _main.SelectModel(chosenModel);
            await _main.ReapplyHotwordsAsync().ConfigureAwait(true);

            _main.ApplySettings(_settings);
            StatusMessage = "已保存 / Saved.";
            _log.Info("Settings saved.");
        }
        catch (Exception ex)
        {
            _log.Error("Saving settings failed", ex);
            StatusMessage = "保存失败 / Save failed：" + ex.Message;
        }
    }

    /// <summary>Cancels an in-flight download; the window's ✕ bypasses <see cref="CloseCommand"/>.</summary>
    public void OnClosing()
    {
        try
        {
            _downloadCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already finished; nothing to cancel.
        }
    }

    private async Task DownloadAsync()
    {
        var descriptor = SelectedModel;
        DownloadError = "";

        // Preflight so the user gets a precise reason instead of a generic HTTP failure.
        var preflightError = CheckDownloadTarget(descriptor);
        if (preflightError is not null)
        {
            DownloadError = preflightError;
            return;
        }

        _downloadCts = new CancellationTokenSource();
        IsDownloading = true;
        DownloadProgress = 0;

        try
        {
            // Progress<T> captures the UI context, so the callback runs on the UI thread.
            var progress = new Progress<double>(p => DownloadProgress = Math.Clamp(p, 0, 1));
            var result = await Task.Run(
                () => _modelManager.EnsureInstalledAsync(descriptor, progress, _downloadCts.Token),
                _downloadCts.Token).ConfigureAwait(true);

            if (result.Success)
            {
                DownloadProgress = 1;
                OnPropertyChanged(nameof(IsSelectedModelInstalled));
                OnPropertyChanged(nameof(SelectedModelStatusText));
                OnPropertyChanged(nameof(ShowDownloadButton));
                StatusMessage = $"模型已下载 / downloaded：{descriptor.DisplayName}";
                _log.Info($"Downloaded model {descriptor.Id} into {_modelManager.GetModelDirectory(descriptor)}.");
            }
            else
            {
                DownloadError = result.Message;
                _log.Warn($"Downloading {descriptor.Id} failed: {result.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            DownloadError = "已取消下载 / download cancelled.";
        }
        catch (Exception ex)
        {
            DownloadError = "下载失败 / download failed：" + ex.Message;
            _log.Error($"Downloading {descriptor.Id} failed", ex);
        }
        finally
        {
            IsDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private void CancelDownload()
    {
        try
        {
            _downloadCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already finished; nothing to cancel.
        }
    }

    /// <summary>Returns a user-facing reason when the model cannot be downloaded to disk, else null.</summary>
    private string? CheckDownloadTarget(ModelDescriptor descriptor)
    {
        var targetDir = _modelManager.GetModelDirectory(descriptor);
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(targetDir));
            if (!string.IsNullOrEmpty(root))
            {
                // The manager downloads to a ".download" temp file in the same directory, so require
                // the model size plus headroom before starting a 190 MB transfer.
                var free = new DriveInfo(root).AvailableFreeSpace;
                if (free < descriptor.ApproxSizeBytes + (64L * 1024 * 1024))
                {
                    return $"磁盘空间不足 / not enough disk space：需要 / need {FormatMegabytes(descriptor.ApproxSizeBytes)}。";
                }
            }

            Directory.CreateDirectory(targetDir);
            var probe = Path.Combine(targetDir, ".subtitlejun-write-test");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex)
        {
            return $"模型目录不可写 / models directory is not writable：{targetDir}（{ex.Message}）";
        }
    }

    private static string FormatMegabytes(long bytes) => $"{bytes / 1_000_000.0:F0} MB";

    private List<Hotword> ParseHotwords()
    {
        var result = new List<Hotword>();
        foreach (var raw in HotwordsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var text = raw.Trim();
            if (text.Length == 0) continue;
            result.Add(new Hotword { Text = text, Enabled = true, Score = 1.5f });
        }
        return result;
    }
}

/// <summary>One selectable post-meeting recording mode for the settings combo box.</summary>
public sealed record RecordingModeOption(RecordingMode Mode, string Label);
