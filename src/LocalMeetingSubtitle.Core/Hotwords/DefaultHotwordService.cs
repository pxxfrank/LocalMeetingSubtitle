using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Hotwords;

/// <summary>
/// Default implementation: caches hotwords + correction rules from the repository and exposes
/// the current <see cref="HotwordMode"/> so the UI can state truthfully whether model-level
/// boosting is actually in effect. Never claims boosting works when the engine cannot do it.
/// </summary>
public sealed class DefaultHotwordService : IHotwordService
{
    private readonly IHotwordRepository _repository;
    private readonly TextCorrectionEngine _engine;
    private readonly IAppLogger _log;

    private IReadOnlyList<Hotword> _hotwords = Array.Empty<Hotword>();
    private IReadOnlyList<TextCorrectionRule> _rules = Array.Empty<TextCorrectionRule>();

    private bool _engineSupportsModelHotwords;
    private bool _engineCapabilityKnown;

    public DefaultHotwordService(IHotwordRepository repository, TextCorrectionEngine? engine = null, IAppLogger? log = null)
    {
        _repository = repository;
        _engine = engine ?? new TextCorrectionEngine();
        _log = log ?? NullLogger.Instance;
    }

    public IReadOnlyList<Hotword> AllHotwords => _hotwords;
    public IReadOnlyList<Hotword> ActiveHotwords => _hotwords.Where(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Text)).ToList();
    public IReadOnlyList<TextCorrectionRule> ActiveRules => _rules.Where(r => r.Enabled).ToList();

    public HotwordMode Mode
    {
        get
        {
            if (!_engineCapabilityKnown)
            {
                return HotwordMode.TextCorrectionOnly;
            }
            if (!_engineSupportsModelHotwords)
            {
                return ActiveHotwords.Count > 0 ? HotwordMode.NotSupported : HotwordMode.TextCorrectionOnly;
            }
            return ActiveHotwords.Count > 0 ? HotwordMode.ModelLevel : HotwordMode.TextCorrectionOnly;
        }
    }

    public string? ModeDetail => Mode switch
    {
        HotwordMode.ModelLevel => $"{ActiveHotwords.Count} hotword(s) boosting the model ({_rules.Count(r => r.Enabled)} correction rule(s)).",
        HotwordMode.NotSupported => "Current model does not support model-level hotwords; only text correction applies.",
        HotwordMode.TextCorrectionOnly => "No model-level hotwords active; text correction only.",
        HotwordMode.LoadFailed => "Hotwords failed to load.",
        _ => null
    };

    public void SetEngineCapability(bool supportsModelHotwords, bool modelCanBeRebuiltWithoutDataLoss)
    {
        _engineSupportsModelHotwords = supportsModelHotwords;
        _engineCapabilityKnown = true;
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        _hotwords = await _repository.GetHotwordsAsync(cancellationToken).ConfigureAwait(false);
        _rules = await _repository.GetCorrectionRulesAsync(cancellationToken).ConfigureAwait(false);
        _log.Info($"Hotwords reloaded: {_hotwords.Count} hotword(s), {_rules.Count} correction rule(s).");
    }

    public HotwordValidationResult Validate(string text)
    {
        var check = HotwordValidator.Validate(HotwordValidator.Normalize(text));
        if (!check.IsValid) return check;

        var normalized = HotwordValidator.Normalize(text);
        if (_hotwords.Any(h => string.Equals(h.Text, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return HotwordValidationResult.Invalid("Duplicate hotword.");
        }
        return HotwordValidationResult.Ok;
    }

    public string? WriteModelHotwordFile(string destinationPath)
    {
        var active = ActiveHotwords;
        if (Mode != HotwordMode.ModelLevel || active.Count == 0)
        {
            return null;
        }

        var content = ModelHotwordFile.Build(active.Select(h => h.Text));
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(destinationPath, content, new System.Text.UTF8Encoding(false));
        return destinationPath;
    }

    public string ApplyCorrections(string text) => _engine.Apply(text, ActiveRules).Text;

    public bool ApplyCorrections(SubtitleSegment segment)
    {
        var corrected = ApplyCorrections(segment.OriginalText);
        bool changed = !string.Equals(corrected, segment.CorrectedText, StringComparison.Ordinal);
        segment.CorrectedText = corrected;
        return changed;
    }
}
