namespace LocalMeetingSubtitle.PerformanceTests;

/// <summary>
/// Long-running stability placeholders. These are intentionally excluded from the normal run
/// (see <see cref="LongRunHarness"/> for how to run the long scenarios by hand).
/// </summary>
public sealed class SoakTests
{
    /// <summary>
    /// Documents the intended 3-hour stability soak: run the capture -> preprocess -> ASR ->
    /// persist pipeline against synthetic audio for three continuous hours and assert bounded
    /// memory (working set growth &lt; 200 MB), a bounded audio queue, no dropped audio once the
    /// consumer keeps up, monotonically increasing sequence numbers, and zero unhandled
    /// exceptions. It is skipped by default because it needs ~3 hours of wall-clock time.
    /// Remove the <c>Skip</c> argument (or run through the CI soak job) to execute it.
    /// </summary>
    [Fact(Skip = "3-hour soak; run manually (see LongRunHarness.cs). Remove this Skip to execute.")]
    [Trait("Category", "Soak")]
    public void ThreeHourSoak()
    {
        // Intentionally empty: the scenario is documented above. It is NOT executed by default.
    }
}
