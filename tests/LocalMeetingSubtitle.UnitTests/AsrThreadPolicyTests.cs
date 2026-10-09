using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class AsrThreadPolicyTests
{
    [Fact]
    public void Explicit_thread_count_is_used_as_is()
    {
        Assert.Equal(6, AsrThreadPolicy.Resolve(6));
        Assert.Equal(1, AsrThreadPolicy.Resolve(1));
        Assert.Equal(64, AsrThreadPolicy.Resolve(64));
    }

    [Fact]
    public void Auto_thread_count_is_capped()
    {
        // 0 (unset) and negative both mean "auto".
        foreach (var configured in new[] { 0, -1, int.MinValue })
        {
            var resolved = AsrThreadPolicy.Resolve(configured);

            Assert.InRange(resolved, 1, AsrThreadPolicy.MaxAutoThreads);
        }
    }

    [Fact]
    public void Auto_never_exceeds_the_cap_even_on_a_large_machine()
    {
        // The dev host has 64 logical processors; the old rule (ProcessorCount/2 = 32) is what the
        // measured 2x RTF regression came from, so the cap must hold regardless of the host.
        Assert.True(AsrThreadPolicy.Resolve(0) <= AsrThreadPolicy.MaxAutoThreads);
    }
}
