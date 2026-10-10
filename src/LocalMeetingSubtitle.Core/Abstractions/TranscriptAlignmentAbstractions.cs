using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Abstractions;

/// <summary>
/// The file-job seam between a transcript and its speakers: turns the persisted transcript segments
/// plus their speaker assignments into a role-tagged <see cref="DialogueTranscript"/>.
///
/// It consumes the <b>persisted assignments</b> produced by diarization rather than re-running the
/// overlap analysis: the raw-cluster-index → speaker mapping is not stored anywhere, whereas
/// <c>speaker_assignments</c> already holds the final speaker per segment. Pure and idempotent, so
/// the dialogue editor (Phase 5) can re-assemble after a rename / merge / reassignment.
/// </summary>
public interface ITranscriptAlignmentService
{
    /// <param name="segments">Transcript segments in spoken order.</param>
    /// <param name="assignmentsBySegmentId">Speaker assignment per database segment id (may be empty).</param>
    /// <param name="speakersById">Known speakers, keyed by speaker id.</param>
    DialogueTranscript Align(
        string sessionId,
        IReadOnlyList<TranscriptSegmentFact> segments,
        IReadOnlyDictionary<long, SpeakerAssignment> assignmentsBySegmentId,
        IReadOnlyDictionary<string, Speaker> speakersById,
        DialogueAssemblyOptions? options = null);
}
