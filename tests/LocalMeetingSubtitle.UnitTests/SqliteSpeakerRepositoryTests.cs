using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class SqliteSpeakerRepositoryTests
{
    [Fact]
    public async Task Run_round_trips_and_latest_wins()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repo = temp.CreateSpeakerRepository();
        var sessionId = Guid.NewGuid().ToString("N");

        var first = new DiarizationRun { SessionId = sessionId, Status = DiarizationRunStatus.Succeeded, ResolvedSpeakerCount = 2, ClusteringThreshold = 0.5 };
        await repo.SaveRunAsync(first);
        var second = new DiarizationRun { SessionId = sessionId, Status = DiarizationRunStatus.Failed, ErrorMessage = "boom" };
        second.CreatedAt = first.CreatedAt.AddMinutes(1);
        await repo.SaveRunAsync(second);

        var latest = await repo.GetLatestRunAsync(sessionId);

        Assert.NotNull(latest);
        Assert.Equal(second.RunId, latest!.RunId);
        Assert.Equal(DiarizationRunStatus.Failed, latest.Status);
        Assert.Equal("boom", latest.ErrorMessage);
    }

    [Fact]
    public async Task Speakers_round_trip_ordered_by_sort_order()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repo = temp.CreateSpeakerRepository();
        var sessionId = Guid.NewGuid().ToString("N");

        await repo.UpsertSpeakerAsync(new Speaker { SessionId = sessionId, Label = "B", SortOrder = 1, ColorArgb = 2 });
        await repo.UpsertSpeakerAsync(new Speaker { SessionId = sessionId, Label = "A", SortOrder = 0, ColorArgb = 1 });

        var speakers = await repo.GetSpeakersAsync(sessionId);

        Assert.Equal(new[] { "A", "B" }, speakers.Select(s => s.Label).ToArray());
    }

    [Fact]
    public async Task Intervals_are_replaced_wholesale_per_run()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repo = temp.CreateSpeakerRepository();
        const string runId = "run-1";
        const string sessionId = "s1";

        await repo.ReplaceIntervalsAsync(runId, new[]
        {
            new SpeakerInterval { RunId = runId, SessionId = sessionId, Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(1), RawSpeakerIndex = 0 }
        });
        await repo.ReplaceIntervalsAsync(runId, new[]
        {
            new SpeakerInterval { RunId = runId, SessionId = sessionId, Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(2), RawSpeakerIndex = 0, Confidence = 0.7f },
            new SpeakerInterval { RunId = runId, SessionId = sessionId, Start = TimeSpan.FromSeconds(2), End = TimeSpan.FromSeconds(3), RawSpeakerIndex = 1 }
        });

        var intervals = await repo.GetIntervalsAsync(runId);

        Assert.Equal(2, intervals.Count);
        Assert.Equal(0.7f, intervals[0].Confidence);
    }

    [Fact]
    public async Task Manual_assignment_survives_reanalysis()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repo = temp.CreateSpeakerRepository();
        const string sessionId = "s1";

        // The user corrects segment 1 to spk-B.
        await repo.SetAssignmentSpeakerAsync(sessionId, 1, "spk-B", SpeakerAssignmentSource.Manual);

        // A later run proposes spk-A for the same segment and must not overwrite the manual choice.
        await repo.UpsertAssignmentsAsync(new[]
        {
            new SpeakerAssignment { SessionId = sessionId, SegmentId = 1, SpeakerId = "spk-A", Source = SpeakerAssignmentSource.Auto }
        }, overwriteManual: false);

        var assignments = await repo.GetAssignmentsAsync(sessionId);

        Assert.Equal("spk-B", Assert.Single(assignments).SpeakerId);
        Assert.Equal(SpeakerAssignmentSource.Manual, assignments[0].Source);
    }

    [Fact]
    public async Task Auto_assignment_is_updated_by_a_later_run()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repo = temp.CreateSpeakerRepository();
        const string sessionId = "s1";

        await repo.UpsertAssignmentsAsync(new[]
        {
            new SpeakerAssignment { SessionId = sessionId, SegmentId = 2, SpeakerId = "spk-A", Source = SpeakerAssignmentSource.Auto }
        }, overwriteManual: false);
        await repo.UpsertAssignmentsAsync(new[]
        {
            new SpeakerAssignment { SessionId = sessionId, SegmentId = 2, SpeakerId = "spk-B", Source = SpeakerAssignmentSource.Auto, NeedsConfirmation = true }
        }, overwriteManual: false);

        var assignment = Assert.Single(await repo.GetAssignmentsAsync(sessionId));

        Assert.Equal("spk-B", assignment.SpeakerId);
        Assert.True(assignment.NeedsConfirmation);
    }

    [Fact]
    public async Task Unknown_speaker_is_stored_as_null()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repo = temp.CreateSpeakerRepository();
        const string sessionId = "s1";

        await repo.UpsertAssignmentsAsync(new[]
        {
            new SpeakerAssignment { SessionId = sessionId, SegmentId = 3, SpeakerId = null, Source = SpeakerAssignmentSource.Auto }
        }, overwriteManual: false);

        Assert.Null(Assert.Single(await repo.GetAssignmentsAsync(sessionId)).SpeakerId);
    }

    [Fact]
    public async Task Merge_repoints_assignments_returns_affected_ids_and_marks_source()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repo = temp.CreateSpeakerRepository();
        const string sessionId = "s1";

        await repo.UpsertSpeakerAsync(new Speaker { SpeakerId = "spk-A", SessionId = sessionId, Label = "A" });
        await repo.UpsertSpeakerAsync(new Speaker { SpeakerId = "spk-C", SessionId = sessionId, Label = "C", SortOrder = 2 });
        await repo.UpsertAssignmentsAsync(new[]
        {
            new SpeakerAssignment { SessionId = sessionId, SegmentId = 10, SpeakerId = "spk-C", Source = SpeakerAssignmentSource.Auto },
            new SpeakerAssignment { SessionId = sessionId, SegmentId = 11, SpeakerId = "spk-C", Source = SpeakerAssignmentSource.Auto },
            new SpeakerAssignment { SessionId = sessionId, SegmentId = 12, SpeakerId = "spk-A", Source = SpeakerAssignmentSource.Auto }
        }, overwriteManual: false);

        var affected = await repo.MergeSpeakersAsync(sessionId, "spk-C", "spk-A");

        Assert.Equal(new long[] { 10, 11 }, affected.OrderBy(x => x).ToArray());

        var assignments = await repo.GetAssignmentsAsync(sessionId);
        Assert.All(assignments, a => Assert.Equal("spk-A", a.SpeakerId));

        var speakers = await repo.GetSpeakersAsync(sessionId);
        var merged = speakers.Single(s => s.SpeakerId == "spk-C");
        Assert.True(merged.IsMerged);
        Assert.Equal("spk-A", merged.MergedIntoSpeakerId);
    }

    [Fact]
    public async Task Expired_temporary_assets_are_returned()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repo = temp.CreateAudioAssetRepository();

        await repo.AddAsync(new AudioAsset { AudioAssetId = "a-expired", Path = "x.wav", Kind = AudioAssetKind.TempRecording, IsTemporary = true, DeleteAfterUtc = DateTimeOffset.UtcNow.AddMinutes(-1) });
        await repo.AddAsync(new AudioAsset { AudioAssetId = "a-future", Path = "y.wav", Kind = AudioAssetKind.TempRecording, IsTemporary = true, DeleteAfterUtc = DateTimeOffset.UtcNow.AddHours(1) });
        await repo.AddAsync(new AudioAsset { AudioAssetId = "a-kept", Path = "z.wav", Kind = AudioAssetKind.RetainedRecording, IsTemporary = false });

        var expired = await repo.GetExpiredTemporaryAsync(DateTimeOffset.UtcNow);

        Assert.Equal("a-expired", Assert.Single(expired).AudioAssetId);
    }
}
