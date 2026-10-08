using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class SqliteSubtitleRepositoryTests
{
    [Fact]
    public async Task CreateSession_ThenReadBack()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateSubtitleRepository();

        var session = TestData.NewSession(title: "Design Review");
        session.ModelId = "sherpa-zipformer";
        session.AudioDeviceId = "device-1";
        session.AudioDeviceName = "Speakers (Realtek)";

        await repository.CreateSessionAsync(session);
        var loaded = await repository.GetSessionAsync(session.SessionId);

        Assert.NotNull(loaded);
        Assert.Equal(session.SessionId, loaded!.SessionId);
        Assert.Equal("Design Review", loaded.Title);
        Assert.Equal(session.StartTime, loaded.StartTime);
        Assert.Equal(SessionStatus.Recording, loaded.Status);
        Assert.Equal("sherpa-zipformer", loaded.ModelId);
        Assert.Equal("device-1", loaded.AudioDeviceId);
        Assert.Equal("Speakers (Realtek)", loaded.AudioDeviceName);
        Assert.Null(loaded.EndTime);
    }

    [Fact]
    public async Task AppendSegments_ReadBackInSequenceOrder()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateSubtitleRepository();
        var session = await repository.CreateSessionAsync(TestData.NewSession());

        await repository.AppendSegmentAsync(TestData.NewSegment(session.SessionId, 2, "third"));
        await repository.AppendSegmentAsync(TestData.NewSegment(session.SessionId, 0, "first"));
        await repository.AppendSegmentAsync(TestData.NewSegment(session.SessionId, 1, "second"));

        var segments = await repository.GetSegmentsAsync(session.SessionId);

        Assert.Equal(new[] { "first", "second", "third" }, segments.Select(s => s.OriginalText));
        Assert.Equal(new[] { 0, 1, 2 }, segments.Select(s => s.SequenceNumber));
        Assert.All(segments, s => Assert.True(s.SegmentId > 0));
        Assert.All(segments, s => Assert.Equal(TimeSpan.FromSeconds(1), s.EndOffset));
    }

    [Fact]
    public async Task AppendSegment_DuplicateSequence_IsIgnoredWithoutThrowing()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateSubtitleRepository();
        var session = await repository.CreateSessionAsync(TestData.NewSession());

        await repository.AppendSegmentAsync(TestData.NewSegment(session.SessionId, 0, "original"));
        await repository.AppendSegmentAsync(TestData.NewSegment(session.SessionId, 0, "duplicate"));

        var segments = await repository.GetSegmentsAsync(session.SessionId);

        Assert.Single(segments);
        Assert.Equal("original", segments[0].OriginalText);
    }

    [Fact]
    public async Task Segments_FromDifferentSessions_AreIsolated()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateSubtitleRepository();
        var first = await repository.CreateSessionAsync(TestData.NewSession(title: "A"));
        var second = await repository.CreateSessionAsync(TestData.NewSession(title: "B"));

        await repository.AppendSegmentAsync(TestData.NewSegment(first.SessionId, 0, "a0"));
        await repository.AppendSegmentAsync(TestData.NewSegment(first.SessionId, 1, "a1"));
        await repository.AppendSegmentAsync(TestData.NewSegment(second.SessionId, 0, "b0"));

        var firstSegments = await repository.GetSegmentsAsync(first.SessionId);
        var secondSegments = await repository.GetSegmentsAsync(second.SessionId);

        Assert.Equal(new[] { "a0", "a1" }, firstSegments.Select(s => s.OriginalText));
        Assert.Equal(new[] { "b0" }, secondSegments.Select(s => s.OriginalText));
    }

    [Fact]
    public async Task GetNextSequence_AdvancesPastExistingAndIgnoresDuplicates()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateSubtitleRepository();
        var session = await repository.CreateSessionAsync(TestData.NewSession());

        Assert.Equal(0, await repository.GetNextSequenceAsync(session.SessionId));

        await repository.AppendSegmentAsync(TestData.NewSegment(session.SessionId, 0, "first"));
        Assert.Equal(1, await repository.GetNextSequenceAsync(session.SessionId));

        await repository.AppendSegmentAsync(TestData.NewSegment(session.SessionId, 0, "duplicate"));
        Assert.Equal(1, await repository.GetNextSequenceAsync(session.SessionId));
    }

    [Fact]
    public async Task UpdateSegmentText_MarksEdited_AndDisplayTextUsesIt()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateSubtitleRepository();
        var session = await repository.CreateSessionAsync(TestData.NewSession());

        var segment = TestData.NewSegment(session.SessionId, 0, "original");
        await repository.AppendSegmentAsync(segment);

        await repository.UpdateSegmentTextAsync(segment.SegmentId, "edited by user", isEdited: true);

        var reloaded = (await repository.GetSegmentsAsync(session.SessionId)).Single();
        Assert.Equal("edited by user", reloaded.CorrectedText);
        Assert.True(reloaded.IsEdited);
        Assert.Equal("edited by user", reloaded.DisplayText);
        Assert.Equal("original", reloaded.OriginalText);
    }

    [Fact]
    public async Task UpdateSessionStatus_SetsStatusAndEndTime()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateSubtitleRepository();
        var session = await repository.CreateSessionAsync(TestData.NewSession());

        var endTime = new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.Zero);
        await repository.UpdateSessionStatusAsync(session.SessionId, SessionStatus.Completed, endTime);

        var loaded = await repository.GetSessionAsync(session.SessionId);
        Assert.Equal(SessionStatus.Completed, loaded!.Status);
        Assert.Equal(endTime, loaded.EndTime);
    }

    [Fact]
    public async Task RecoverAbortedSessions_MarksRecordingAndPausedAsAborted()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateSubtitleRepository();
        var recording = await repository.CreateSessionAsync(TestData.NewSession(status: SessionStatus.Recording));
        var paused = await repository.CreateSessionAsync(TestData.NewSession(status: SessionStatus.Paused));
        var completed = await repository.CreateSessionAsync(TestData.NewSession(status: SessionStatus.Completed));

        var recovered = await repository.RecoverAbortedSessionsAsync();

        Assert.Equal(2, recovered);
        Assert.Equal(SessionStatus.Aborted, (await repository.GetSessionAsync(recording.SessionId))!.Status);
        Assert.Equal(SessionStatus.Aborted, (await repository.GetSessionAsync(paused.SessionId))!.Status);
        Assert.Equal(SessionStatus.Completed, (await repository.GetSessionAsync(completed.SessionId))!.Status);
    }

    [Fact]
    public async Task GetRecentSessions_OrdersByStartTimeDescending()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateSubtitleRepository();

        var older = TestData.NewSession(title: "older");
        older.StartTime = TestData.FixedStart.AddDays(-2);
        var newest = TestData.NewSession(title: "newest");
        newest.StartTime = TestData.FixedStart.AddDays(1);
        var middle = TestData.NewSession(title: "middle");

        await repository.CreateSessionAsync(older);
        await repository.CreateSessionAsync(newest);
        await repository.CreateSessionAsync(middle);

        var sessions = await repository.GetRecentSessionsAsync();

        Assert.Equal(new[] { "newest", "middle", "older" }, sessions.Select(s => s.Title));
    }

    [Fact]
    public async Task Metrics_AppendAndReadBack()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateSubtitleRepository();
        var session = await repository.CreateSessionAsync(TestData.NewSession());

        var metric = new PerformanceMetric
        {
            SessionId = session.SessionId,
            Timestamp = TestData.FixedStart,
            CpuPercent = 12.5,
            WorkingSetMb = 256.75,
            Rtf = 0.42,
            QueueLength = 3,
            MaxQueueLength = 8,
            DroppedAudioSeconds = 0.5,
            EndToEndLatencyMs = 180.25
        };

        await repository.AppendMetricAsync(metric);
        var metrics = await repository.GetMetricsAsync(session.SessionId);

        var loaded = Assert.Single(metrics);
        Assert.True(loaded.Id > 0);
        Assert.Equal(metric.SessionId, loaded.SessionId);
        Assert.Equal(metric.Timestamp, loaded.Timestamp);
        Assert.Equal(12.5, loaded.CpuPercent, precision: 3);
        Assert.Equal(256.75, loaded.WorkingSetMb, precision: 3);
        Assert.Equal(0.42, loaded.Rtf, precision: 3);
        Assert.Equal(3, loaded.QueueLength);
        Assert.Equal(8, loaded.MaxQueueLength);
        Assert.Equal(0.5, loaded.DroppedAudioSeconds, precision: 3);
        Assert.Equal(180.25, loaded.EndToEndLatencyMs, precision: 3);
    }
}
