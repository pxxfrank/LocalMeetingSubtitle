using System.Collections.ObjectModel;
using LocalMeetingSubtitle.App.Infrastructure;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.App.ViewModels;

/// <summary>
/// Backs the speaker-management window: rename speakers, merge two speakers (with a one-level undo),
/// reassign individual subtitle segments and refresh. All persistence goes through
/// <see cref="ISpeakerRepository"/>; the main window is pushed a refresh after every change.
/// </summary>
public sealed class SpeakerManagementViewModel : ObservableObject
{
    private readonly ISpeakerRepository _speakers;
    private readonly ISubtitleRepository _subtitles;
    private readonly MainViewModel _main;
    private readonly IAppLogger _log;

    // A load may be triggered from both the shell and the window's Loaded event; the flag makes the
    // second (concurrent) call a no-op instead of racing two collection rebuilds.
    private bool _isLoading;
    private string? _sessionId;
    private List<Speaker> _allSpeakers = new();
    private long _analysisDurationMs;

    // One-level undo snapshot for the last merge.
    private string? _undoFromSpeakerId;
    private string? _undoIntoSpeakerId;
    private IReadOnlyList<long> _undoSegmentIds = Array.Empty<long>();

    public SpeakerManagementViewModel(
        ISpeakerRepository speakers,
        ISubtitleRepository subtitles,
        MainViewModel main,
        IAppLogger log)
    {
        _speakers = speakers;
        _subtitles = subtitles;
        _main = main;
        _log = log;

        SaveNamesCommand = new AsyncRelayCommand(SaveNamesAsync);
        MergeCommand = new AsyncRelayCommand(MergeAsync, parameter => parameter is SpeakerRowViewModel row && row.MergeTarget is not null);
        UndoMergeCommand = new AsyncRelayCommand(UndoMergeAsync, () => CanUndo);
        ApplyReassignmentsCommand = new AsyncRelayCommand(ApplyReassignmentsAsync);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Speakers of the current session (non-merged only).</summary>
    public ObservableCollection<SpeakerRowViewModel> Speakers { get; } = new();

    /// <summary>Every persisted subtitle segment of the current session.</summary>
    public ObservableCollection<SegmentRowViewModel> Segments { get; } = new();

    /// <summary>Speakers offered in the per-segment reassignment combo (non-merged + "未知 / Unknown").</summary>
    public ObservableCollection<SpeakerChoice> SpeakerChoices { get; } = new();

    public AsyncRelayCommand SaveNamesCommand { get; }
    public AsyncRelayCommand MergeCommand { get; }
    public AsyncRelayCommand UndoMergeCommand { get; }
    public AsyncRelayCommand ApplyReassignmentsCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand CloseCommand { get; }

    /// <summary>Raised when the window should close itself.</summary>
    public event EventHandler? CloseRequested;

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    private bool _canUndo;
    /// <summary>True when a merge can still be undone.</summary>
    public bool CanUndo
    {
        get => _canUndo;
        private set
        {
            if (SetProperty(ref _canUndo, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>True before a session has started (drives the empty-state hint).</summary>
    public bool HasNoSession => _sessionId is null;

    /// <summary>The analyzed-audio duration of the latest run, formatted, or empty when unknown.</summary>
    public string AnalysisDurationText
    {
        get
        {
            if (_analysisDurationMs <= 0)
            {
                return "";
            }

            var total = TimeSpan.FromMilliseconds(_analysisDurationMs);
            return $"分析时长 / analyzed: {(int)total.TotalMinutes:00}:{total.Seconds:00}";
        }
    }

    /// <summary>Loads speakers, assignments, the latest run's intervals and the session segments.</summary>
    public async Task LoadAsync()
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        IsBusy = true;
        try
        {
            var sessionId = _main.CurrentSessionId;
            if (string.IsNullOrEmpty(sessionId))
            {
                _sessionId = null;
                _allSpeakers = new List<Speaker>();
                _analysisDurationMs = 0;
                Speakers.Clear();
                Segments.Clear();
                SpeakerChoices.Clear();
                NotifyCollectionsChanged();
                StatusMessage = "尚无会议会话 / no session yet — 请先开始并结束一次会议。";
                return;
            }

            _sessionId = sessionId;

            var speakers = await _speakers.GetSpeakersAsync(sessionId).ConfigureAwait(true);
            var assignments = await _speakers.GetAssignmentsAsync(sessionId).ConfigureAwait(true);
            var run = await _speakers.GetLatestRunAsync(sessionId).ConfigureAwait(true);
            var intervals = run is null
                ? Array.Empty<SpeakerInterval>()
                : (await _speakers.GetIntervalsAsync(run.RunId).ConfigureAwait(true)).ToArray();
            var segments = await _subtitles.GetSegmentsAsync(sessionId).ConfigureAwait(true);

            _allSpeakers = speakers.ToList();
            _analysisDurationMs = run?.DurationMs ?? 0;

            var activeSpeakers = speakers
                .Where(s => !s.IsMerged)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Label, StringComparer.Ordinal)
                .ToList();

            BuildSpeakerChoices(activeSpeakers);
            BuildSpeakerRows(activeSpeakers, assignments, segments, intervals);
            BuildSegmentRows(segments, assignments);

            NotifyCollectionsChanged();
            StatusMessage = $"已加载 / loaded：{Speakers.Count} 位发言人，{Segments.Count} 条字幕。";
        }
        catch (Exception ex)
        {
            _log.Error("Loading the speaker-management data failed", ex);
            StatusMessage = "加载失败 / load failed：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            _isLoading = false;
        }
    }

    private void BuildSpeakerChoices(IReadOnlyList<Speaker> activeSpeakers)
    {
        SpeakerChoices.Clear();
        foreach (var speaker in activeSpeakers)
        {
            SpeakerChoices.Add(new SpeakerChoice(speaker.SpeakerId, DisplayNameOf(speaker)));
        }

        SpeakerChoices.Add(new SpeakerChoice(null, "未知 / Unknown"));
    }

    private void BuildSpeakerRows(
        IReadOnlyList<Speaker> activeSpeakers,
        IReadOnlyList<SpeakerAssignment> assignments,
        IReadOnlyList<SubtitleSegment> segments,
        IReadOnlyList<SpeakerInterval> intervals)
    {
        var speakerIdBySegment = assignments.ToDictionary(a => a.SegmentId, a => a.SpeakerId);
        var mergeTargets = activeSpeakers
            .Select(s => new SpeakerChoice(s.SpeakerId, DisplayNameOf(s)))
            .ToList();

        Speakers.Clear();
        foreach (var speaker in activeSpeakers)
        {
            var assigned = segments
                .Where(s => speakerIdBySegment.TryGetValue(s.SegmentId, out var id) && id == speaker.SpeakerId)
                .ToList();

            var seconds = EstimateSpeechSeconds(intervals, assigned);
            if (seconds <= 0)
            {
                // No intervals (or none overlapping): fall back to the assigned subtitle durations.
                seconds = assigned.Sum(s => Math.Max(0, (s.EndOffset - s.StartOffset).TotalSeconds));
            }

            var targets = mergeTargets.Where(c => c.SpeakerId != speaker.SpeakerId);
            Speakers.Add(new SpeakerRowViewModel(speaker, assigned.Count, seconds, targets));
        }
    }

    private void BuildSegmentRows(IReadOnlyList<SubtitleSegment> segments, IReadOnlyList<SpeakerAssignment> assignments)
    {
        var speakerIdBySegment = assignments.ToDictionary(a => a.SegmentId, a => a.SpeakerId);

        Segments.Clear();
        foreach (var segment in segments.OrderBy(s => s.SequenceNumber))
        {
            speakerIdBySegment.TryGetValue(segment.SegmentId, out var speakerId);
            Segments.Add(new SegmentRowViewModel(segment, speakerId, SpeakerChoices));
        }
    }

    private async Task SaveNamesAsync()
    {
        if (_sessionId is null)
        {
            StatusMessage = "尚无会议会话 / no session yet";
            return;
        }

        IsBusy = true;
        try
        {
            var saved = 0;
            foreach (var row in Speakers.Where(r => r.IsDirty))
            {
                row.Speaker.DisplayName = row.DisplayName?.Trim() ?? "";
                await _speakers.UpsertSpeakerAsync(row.Speaker).ConfigureAwait(true);
                row.AcceptChanges();
                saved++;
            }

            await LoadAsync().ConfigureAwait(true);
            await _main.RefreshSpeakersAsync().ConfigureAwait(true);
            StatusMessage = saved == 0 ? "没有名称修改 / no name changes" : $"已保存 {saved} 个名称 / saved {saved}";
        }
        catch (Exception ex)
        {
            _log.Error("Saving speaker names failed", ex);
            StatusMessage = "保存失败 / save failed：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Merges the source speaker (the command parameter's row) into its selected target. The affected
    /// segment ids and the (from, into) pair are kept as a one-level undo snapshot.
    /// </summary>
    private async Task MergeAsync(object? parameter)
    {
        if (_sessionId is null || parameter is not SpeakerRowViewModel row)
        {
            return;
        }

        var fromId = row.SpeakerId;
        var toId = row.MergeTarget?.SpeakerId;
        if (string.IsNullOrEmpty(toId))
        {
            StatusMessage = "请选择要合并到的目标 / choose a target speaker";
            return;
        }

        var fromName = row.Speaker.EffectiveName;
        var toName = row.MergeTarget!.Name;

        IsBusy = true;
        try
        {
            var affected = await _speakers.MergeSpeakersAsync(_sessionId, fromId, toId).ConfigureAwait(true);

            _undoFromSpeakerId = fromId;
            _undoIntoSpeakerId = toId;
            _undoSegmentIds = affected;
            CanUndo = true;

            await LoadAsync().ConfigureAwait(true);
            await _main.RefreshSpeakersAsync().ConfigureAwait(true);
            StatusMessage = $"已合并 / merged：{fromName} → {toName}（{affected.Count} 条字幕 / segments）。可撤销。";
        }
        catch (Exception ex)
        {
            _log.Error("Merging speakers failed", ex);
            StatusMessage = "合并失败 / merge failed：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Re-points the segments affected by the last merge back and un-merges the source speaker.</summary>
    private async Task UndoMergeAsync()
    {
        if (!CanUndo || _sessionId is null || _undoFromSpeakerId is null)
        {
            return;
        }

        var fromId = _undoFromSpeakerId;
        var segmentIds = _undoSegmentIds;

        IsBusy = true;
        try
        {
            foreach (var segmentId in segmentIds)
            {
                await _speakers.SetAssignmentSpeakerAsync(_sessionId, segmentId, fromId, SpeakerAssignmentSource.Manual)
                    .ConfigureAwait(true);
            }

            var from = _allSpeakers.FirstOrDefault(s => s.SpeakerId == fromId);
            if (from is null)
            {
                StatusMessage = "撤销失败：找不到原发言人 / undo failed: source speaker missing";
                return;
            }

            from.IsMerged = false;
            from.MergedIntoSpeakerId = null;
            await _speakers.UpsertSpeakerAsync(from).ConfigureAwait(true);

            var intoName = _allSpeakers.FirstOrDefault(s => s.SpeakerId == _undoIntoSpeakerId)?.EffectiveName ?? "?";

            CanUndo = false;
            _undoFromSpeakerId = null;
            _undoIntoSpeakerId = null;
            _undoSegmentIds = Array.Empty<long>();

            await LoadAsync().ConfigureAwait(true);
            await _main.RefreshSpeakersAsync().ConfigureAwait(true);
            StatusMessage = $"已撤销合并 / merge undone（{from.EffectiveName} 从 {intoName} 还原，{segmentIds.Count} 条字幕）。";
        }
        catch (Exception ex)
        {
            _log.Error("Undoing the speaker merge failed", ex);
            StatusMessage = "撤销失败 / undo failed：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Persists every per-segment speaker change made in the segments list.</summary>
    private async Task ApplyReassignmentsAsync()
    {
        if (_sessionId is null)
        {
            StatusMessage = "尚无会议会话 / no session yet";
            return;
        }

        IsBusy = true;
        try
        {
            var changed = 0;
            foreach (var row in Segments.Where(r => r.IsChanged))
            {
                await _speakers.SetAssignmentSpeakerAsync(
                        _sessionId, row.Segment.SegmentId, row.SelectedSpeakerId, SpeakerAssignmentSource.Manual)
                    .ConfigureAwait(true);
                row.AcceptChanges();
                changed++;
            }

            await LoadAsync().ConfigureAwait(true);
            await _main.RefreshSpeakersAsync().ConfigureAwait(true);
            StatusMessage = changed == 0 ? "没有需要应用的调整 / nothing to apply" : $"已应用 {changed} 处调整 / applied {changed}";
        }
        catch (Exception ex)
        {
            _log.Error("Applying speaker reassignments failed", ex);
            StatusMessage = "应用失败 / apply failed：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyCollectionsChanged()
    {
        OnPropertyChanged(nameof(HasNoSession));
        OnPropertyChanged(nameof(AnalysisDurationText));
    }

    /// <summary>
    /// Estimated speaking time for one speaker: the overlap between the diarization intervals and the
    /// subtitle segments assigned to that speaker. The raw interval → speaker mapping is not persisted,
    /// so this is an approximation (the UI labels it "≈") rather than an exact total.
    /// </summary>
    private static double EstimateSpeechSeconds(
        IReadOnlyList<SpeakerInterval> intervals,
        IReadOnlyList<SubtitleSegment> assignedSegments)
    {
        var ranges = assignedSegments
            .Where(s => s.EndOffset > s.StartOffset)
            .Select(s => (Start: s.StartOffset.TotalSeconds, End: s.EndOffset.TotalSeconds))
            .ToList();
        if (ranges.Count == 0)
        {
            return 0;
        }

        double total = 0;
        foreach (var interval in intervals)
        {
            var intervalStart = interval.Start.TotalSeconds;
            var intervalEnd = interval.End.TotalSeconds;
            if (intervalEnd <= intervalStart)
            {
                continue;
            }

            // Count each interval once, by its best overlap with any one assigned segment.
            double best = 0;
            foreach (var range in ranges)
            {
                var overlap = Math.Min(intervalEnd, range.End) - Math.Max(intervalStart, range.Start);
                if (overlap > best)
                {
                    best = overlap;
                }
            }

            total += best;
        }

        return total;
    }

    private static string DisplayNameOf(Speaker speaker) =>
        string.IsNullOrWhiteSpace(speaker.DisplayName) ? "发言人 " + speaker.Label : speaker.DisplayName;
}
