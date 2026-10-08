using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Abstractions;

/// <summary>Sessions and subtitle segments. Writes must be durable and idempotent.</summary>
public interface ISubtitleRepository : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<MeetingSession> CreateSessionAsync(MeetingSession session, CancellationToken cancellationToken = default);

    /// <summary>Persists a final segment. Rejects duplicate (session, sequence) inserts.</summary>
    Task AppendSegmentAsync(SubtitleSegment segment, CancellationToken cancellationToken = default);

    Task UpdateSegmentTextAsync(long segmentId, string correctedText, bool isEdited, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubtitleSegment>> GetSegmentsAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<MeetingSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MeetingSession>> GetRecentSessionsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<int> GetNextSequenceAsync(string sessionId, CancellationToken cancellationToken = default);
    Task UpdateSessionStatusAsync(string sessionId, SessionStatus status, DateTimeOffset? endTime, CancellationToken cancellationToken = default);

    /// <summary>Marks sessions left in <see cref="SessionStatus.Recording"/>/<see cref="SessionStatus.Paused"/> as aborted (crash recovery).</summary>
    Task<int> RecoverAbortedSessionsAsync(CancellationToken cancellationToken = default);

    Task AppendMetricAsync(PerformanceMetric metric, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PerformanceMetric>> GetMetricsAsync(string sessionId, CancellationToken cancellationToken = default);
}

public interface IHotwordRepository : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Hotword>> GetHotwordsAsync(CancellationToken cancellationToken = default);
    Task<long> UpsertHotwordAsync(Hotword hotword, CancellationToken cancellationToken = default);
    Task DeleteHotwordAsync(long id, CancellationToken cancellationToken = default);
    Task ReplaceHotwordsAsync(IEnumerable<Hotword> hotwords, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HotwordGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);
    Task<long> UpsertGroupAsync(HotwordGroup group, CancellationToken cancellationToken = default);
    Task DeleteGroupAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TextCorrectionRule>> GetCorrectionRulesAsync(CancellationToken cancellationToken = default);
    Task<long> UpsertCorrectionRuleAsync(TextCorrectionRule rule, CancellationToken cancellationToken = default);
    Task DeleteCorrectionRuleAsync(long id, CancellationToken cancellationToken = default);
}

public interface ISettingsRepository : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
