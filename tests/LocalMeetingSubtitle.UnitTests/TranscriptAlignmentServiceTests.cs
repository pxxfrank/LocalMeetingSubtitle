using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Speakers;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>
/// V0.5 Phase 3: consecutive segments by the same speaker merge into one dialogue turn; a speaker
/// change, a long pause or a character cap starts a new one.
/// </summary>
public sealed class TranscriptAlignmentServiceTests
{
    private readonly TranscriptAlignmentService _service = new();

    [Fact]
    public void ConsecutiveSegmentsByTheSameSpeaker_MergeIntoOneTurn()
    {
        var segments = new[]
        {
            Fact(1, 0.0, 1.0, "你好，"),
            Fact(2, 1.2, 2.0, "世界。"),
            Fact(3, 2.2, 3.0, "再说一句。")
        };

        var dialogue = _service.Align("s", segments, Assignments((1, "spk-a"), (2, "spk-a"), (3, "spk-a")), Speakers("spk-a", "A"));

        var turn = Assert.Single(dialogue.Turns);
        Assert.Equal("spk-a", turn.SpeakerId);
        Assert.Equal("A", turn.SpeakerName);
        Assert.Equal("你好，世界。再说一句。", turn.Text);
        Assert.Equal(0.0, turn.Start.TotalSeconds, 3);
        Assert.Equal(3.0, turn.End.TotalSeconds, 3);
        Assert.Equal(new long[] { 1, 2, 3 }, turn.SegmentIds);
    }

    [Fact]
    public void SpeakerChange_StartsANewTurn()
    {
        var segments = new[]
        {
            Fact(1, 0.0, 1.0, "甲说话。"),
            Fact(2, 1.1, 2.0, "乙说话。"),
            Fact(3, 2.1, 3.0, "甲又说。")
        };

        var dialogue = _service.Align("s", segments,
            Assignments((1, "spk-a"), (2, "spk-b"), (3, "spk-a")), Speakers("spk-a", "A", "spk-b", "B"));

        Assert.Equal(3, dialogue.Turns.Count);
        Assert.Equal(new[] { "spk-a", "spk-b", "spk-a" }, dialogue.Turns.Select(t => t.SpeakerId).ToArray());
    }

    [Fact]
    public void LongPause_StartsANewTurnEvenForTheSameSpeaker()
    {
        var segments = new[]
        {
            Fact(1, 0.0, 1.0, "第一段。"),
            Fact(2, 5.0, 6.0, "很久以后。")
        };

        var dialogue = _service.Align("s", segments, Assignments((1, "spk-a"), (2, "spk-a")), Speakers("spk-a", "A"));

        Assert.Equal(2, dialogue.Turns.Count);
        Assert.All(dialogue.Turns, t => Assert.Equal("spk-a", t.SpeakerId));
    }

    [Fact]
    public void SameSpeakerPauseWithinTheThreshold_StillMerges()
    {
        var segments = new[]
        {
            Fact(1, 0.0, 1.0, "第一段。"),
            Fact(2, 2.0, 3.0, "紧接着。")   // 1.0 s gap < the 2 s default
        };

        var dialogue = _service.Align("s", segments, Assignments((1, "spk-a"), (2, "spk-a")), Speakers("spk-a", "A"));

        Assert.Single(dialogue.Turns);
    }

    [Fact]
    public void MaxTurnChars_SplitsButNeverLosesText()
    {
        var options = new DialogueAssemblyOptions { MaxTurnChars = 10 };
        var segments = new[]
        {
            Fact(1, 0.0, 1.0, "一二三四五六"),
            Fact(2, 1.0, 2.0, "七八九十甲乙")
        };

        var dialogue = _service.Align("s", segments, Assignments((1, "spk-a"), (2, "spk-a")), Speakers("spk-a", "A"), options);

        Assert.Equal(2, dialogue.Turns.Count);
        Assert.Equal("一二三四五六七八九十甲乙", string.Concat(dialogue.Turns.Select(t => t.Text)));
    }

    [Fact]
    public void UnknownSpeaker_BecomesAnUnknownTurnAndIsExcludedFromParticipants()
    {
        var segments = new[]
        {
            Fact(1, 0.0, 1.0, "我说。"),
            Fact(2, 1.1, 2.0, "谁说的。")
        };

        // Segment 2 has no assignment at all.
        var dialogue = _service.Align("s", segments, Assignments((1, "spk-a")), Speakers("spk-a", "A"));

        Assert.Equal(2, dialogue.Turns.Count);
        Assert.False(dialogue.Turns[0].IsUnknownSpeaker);
        Assert.True(dialogue.Turns[1].IsUnknownSpeaker);
        Assert.Null(dialogue.Turns[1].SpeakerId);
        Assert.Equal("未知发言人", dialogue.Turns[1].SpeakerName);

        var participant = Assert.Single(dialogue.Participants);
        Assert.Equal("spk-a", participant.SpeakerId);
    }

    [Fact]
    public void NullSpeakerIdAssignment_IsTreatedAsUnknown()
    {
        var segments = new[] { Fact(1, 0.0, 1.0, "不确定。") };
        var assignments = new Dictionary<long, SpeakerAssignment>
        {
            [1] = new() { SegmentId = 1, SessionId = "s", SpeakerId = null, NeedsConfirmation = true }
        };

        var dialogue = _service.Align("s", segments, assignments, Speakers());

        var turn = Assert.Single(dialogue.Turns);
        Assert.Null(turn.SpeakerId);
        Assert.True(turn.NeedsConfirmation);
    }

    [Fact]
    public void NeedsConfirmation_IsPropagatedToTheMergedTurn()
    {
        var segments = new[]
        {
            Fact(1, 0.0, 1.0, "第一段。"),
            Fact(2, 1.0, 2.0, "第二段。")
        };
        var assignments = new Dictionary<long, SpeakerAssignment>
        {
            [1] = new() { SegmentId = 1, SessionId = "s", SpeakerId = "spk-a", NeedsConfirmation = false },
            [2] = new() { SegmentId = 2, SessionId = "s", SpeakerId = "spk-a", NeedsConfirmation = true }
        };

        var dialogue = _service.Align("s", segments, assignments, Speakers("spk-a", "A"));

        Assert.True(Assert.Single(dialogue.Turns).NeedsConfirmation);
    }

    [Fact]
    public void Participants_AggregateSpeakingTimeTurnsAndSegments()
    {
        var segments = new[]
        {
            Fact(1, 0.0, 2.0, "甲一。"),
            Fact(2, 2.0, 4.0, "乙一。"),
            Fact(3, 10.0, 11.0, "甲二。")   // a long pause -> a second turn for A
        };

        var dialogue = _service.Align("s", segments,
            Assignments((1, "spk-a"), (2, "spk-b"), (3, "spk-a")), Speakers("spk-a", "A", "spk-b", "B"));

        Assert.Equal(2, dialogue.Participants.Count);
        var a = dialogue.Participants.Single(p => p.SpeakerId == "spk-a");
        Assert.Equal(3.0, a.SpeakingTime.TotalSeconds, 3);
        Assert.Equal(2, a.TurnCount);
        Assert.Equal(2, a.SegmentCount);

        var b = dialogue.Participants.Single(p => p.SpeakerId == "spk-b");
        Assert.Equal(2.0, b.SpeakingTime.TotalSeconds, 3);
        Assert.Equal(1, b.TurnCount);
    }

    [Fact]
    public void DisplayNameIsPreferredOverTheAnonymousLabel()
    {
        var segments = new[] { Fact(1, 0.0, 1.0, "嗯。") };
        var speaker = new Speaker { SpeakerId = "spk-a", SessionId = "s", Label = "A", DisplayName = "张三", ColorArgb = unchecked((int)0xFF112233) };

        var dialogue = _service.Align("s", segments, Assignments((1, "spk-a")),
            new Dictionary<string, Speaker> { ["spk-a"] = speaker });

        var turn = Assert.Single(dialogue.Turns);
        Assert.Equal("张三", turn.SpeakerName);
        Assert.Equal(unchecked((int)0xFF112233), turn.SpeakerColorArgb);
        Assert.Equal("张三", Assert.Single(dialogue.Participants).Name);
    }

    [Fact]
    public void EmptyInput_YieldsAnEmptyTranscript()
    {
        var dialogue = _service.Align("s", Array.Empty<TranscriptSegmentFact>(),
            new Dictionary<long, SpeakerAssignment>(), new Dictionary<string, Speaker>());

        Assert.Empty(dialogue.Turns);
        Assert.Empty(dialogue.Participants);
        Assert.Equal("s", dialogue.SessionId);
    }

    private static TranscriptSegmentFact Fact(long id, double startSeconds, double endSeconds, string text) =>
        new(id, TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(endSeconds), text);

    private static IReadOnlyDictionary<long, SpeakerAssignment> Assignments(params (long SegmentId, string SpeakerId)[] pairs) =>
        pairs.ToDictionary(p => p.SegmentId, p => new SpeakerAssignment
        {
            SessionId = "s",
            SegmentId = p.SegmentId,
            SpeakerId = p.SpeakerId
        });

    private static IReadOnlyDictionary<string, Speaker> Speakers(params string[] pairs)
    {
        var map = new Dictionary<string, Speaker>();
        for (int i = 0; i < pairs.Length; i += 2)
        {
            map[pairs[i]] = new Speaker
            {
                SpeakerId = pairs[i],
                SessionId = "s",
                Label = pairs[i + 1],
                SortOrder = i / 2
            };
        }

        return map;
    }
}
