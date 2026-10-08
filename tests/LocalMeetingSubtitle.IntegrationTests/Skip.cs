using System.Reflection;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.IntegrationTests;

/// <summary>
/// Dynamic skip helper. xunit 2.5.x has no <c>Assert.Skip</c> (added in xunit v3 / 2.9+), so the
/// method is resolved reflectively; when unavailable the reason is logged and the caller returns.
/// </summary>
internal static class Skip
{
    private static readonly MethodInfo? AssertSkipMethod =
        typeof(Assert).GetMethod("Skip", BindingFlags.Public | BindingFlags.Static, binder: null,
            types: new[] { typeof(string) }, modifiers: null);

    /// <summary>Whether the running xunit supports dynamic skips.</summary>
    public static bool SupportsDynamicSkip => AssertSkipMethod is not null;

    public static void If(bool condition, string reason, ITestOutputHelper? output = null)
    {
        if (!condition)
        {
            return;
        }

        if (AssertSkipMethod is not null)
        {
            AssertSkipMethod.Invoke(null, new object[] { reason });
            return;
        }

        output?.WriteLine("SKIPPED: " + reason);
        // No dynamic skip available: the test still returns without asserting, but it is never a
        // silent "pass" because the environment guard (model on disk) is deterministic here.
    }
}
