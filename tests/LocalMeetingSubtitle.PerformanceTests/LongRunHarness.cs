namespace LocalMeetingSubtitle.PerformanceTests;

/// <summary>
/// Entry point / run-book for the long-running performance scenarios. Long scenarios are xunit
/// [Fact]s tagged <c>Trait("Category", "Performance")</c> (plus the skipped <c>Soak</c> placeholder)
/// so they can be selected with the xunit <c>--filter</c> option.
///
/// Run every performance test:
///   dotnet test tests/LocalMeetingSubtitle.PerformanceTests/LocalMeetingSubtitle.PerformanceTests.csproj -c Debug
///
/// Run only the long/performance scenarios by trait:
///   dotnet test tests/LocalMeetingSubtitle.PerformanceTests/LocalMeetingSubtitle.PerformanceTests.csproj -c Debug --filter "Category=Performance"
///
/// Run the skipped 3-hour soak explicitly (remove its Skip argument first):
///   dotnet test tests/LocalMeetingSubtitle.PerformanceTests/LocalMeetingSubtitle.PerformanceTests.csproj -c Debug --filter "FullyQualifiedName~ThreeHourSoak"
///
/// Run a single scenario by name:
///   dotnet test tests/LocalMeetingSubtitle.PerformanceTests/LocalMeetingSubtitle.PerformanceTests.csproj -c Debug --filter "FullyQualifiedName~Pipeline_TenMinuteRun"
/// </summary>
internal static class LongRunHarness
{
    /// <summary>Category applied to the long-running performance scenarios.</summary>
    public const string PerformanceCategory = "Performance";

    /// <summary>xunit filter selecting the long-running scenarios.</summary>
    public const string PerformanceFilter = "Category=" + PerformanceCategory;
}
