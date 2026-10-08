using System.Collections.ObjectModel;
using LocalMeetingSubtitle.App.Infrastructure;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.App.ViewModels;

/// <summary>Backs the settings window: subtitle fonts, floating-subtitle options, ASR threads and hotwords.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private static readonly string[] FontFamilies =
    {
        "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "SimHei", "SimSun", "Arial"
    };

    private readonly ISettingsRepository _settingsRepository;
    private readonly IHotwordRepository _hotwordRepository;
    private readonly MainViewModel _main;
    private readonly IAppLogger _log;

    private AppSettings _settings = new();

    public SettingsViewModel(
        ISettingsRepository settingsRepository,
        IHotwordRepository hotwordRepository,
        MainViewModel main,
        IAppLogger log)
    {
        _settingsRepository = settingsRepository;
        _hotwordRepository = hotwordRepository;
        _main = main;
        _log = log;

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    public ObservableCollection<string> AvailableFontFamilies { get; } = new(FontFamilies);

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CloseCommand { get; }

    public event EventHandler? CloseRequested;

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

    private string _hotwordsText = "";
    public string HotwordsText { get => _hotwordsText; set => SetProperty(ref _hotwordsText, value); }

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

            await _settingsRepository.SaveAsync(_settings).ConfigureAwait(true);

            var hotwords = ParseHotwords();
            await _hotwordRepository.ReplaceHotwordsAsync(hotwords).ConfigureAwait(true);
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
