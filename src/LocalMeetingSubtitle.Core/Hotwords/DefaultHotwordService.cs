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
    private IReadOnlyList<Hotword>? _activeHotwordsCache;
    private IReadOnlyList<TextCorrectionRule>? _activeRulesCache;
    private bool _useBuiltInLexicon = true;

    private bool _engineSupportsModelHotwords;
    private bool _engineCapabilityKnown;

    public DefaultHotwordService(IHotwordRepository repository, TextCorrectionEngine? engine = null, IAppLogger? log = null)
    {
        _repository = repository;
        _engine = engine ?? new TextCorrectionEngine();
        _log = log ?? NullLogger.Instance;
    }

    public bool UseBuiltInLexicon
    {
        get => _useBuiltInLexicon;
        set
        {
            if (_useBuiltInLexicon == value)
            {
                return;
            }

            _useBuiltInLexicon = value;
            _activeHotwordsCache = null;
            _activeRulesCache = null;
            _log.Info($"Built-in domain lexicon {(value ? "enabled" : "disabled")} ({BuiltInLexicon.HotwordCount} terms available).");
        }
    }

    public IReadOnlyList<Hotword> AllHotwords => _hotwords;

    /// <summary>
    /// The user's active hotwords, plus (unless disabled) the built-in domain lexicon. A user entry
    /// with the same text wins, so the user's own settings are never overridden by the built-in list.
    /// </summary>
    public IReadOnlyList<Hotword> ActiveHotwords => _activeHotwordsCache ??= BuildActiveHotwords();

    public IReadOnlyList<TextCorrectionRule> ActiveRules => _activeRulesCache ??= BuildActiveRules();

    private IReadOnlyList<Hotword> BuildActiveHotwords()
    {
        var user = _hotwords.Where(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Text)).ToList();
        if (!UseBuiltInLexicon)
        {
            return user;
        }

        var builtIn = BuiltInLexicon.Hotwords
            .Select(text => new Hotword { Text = text, Enabled = true, Score = 1.5f });

        return builtIn
            .Concat(user)
            .GroupBy(h => h.Text, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();
    }

    private IReadOnlyList<TextCorrectionRule> BuildActiveRules()
    {
        var user = _rules.Where(r => r.Enabled).ToList();
        if (!UseBuiltInLexicon)
        {
            return user;
        }

        return BuiltInLexicon.Rules
            .Concat(user)
            .GroupBy(r => r.Pattern, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();
    }

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
        _activeHotwordsCache = null;
        _activeRulesCache = null;
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
