using System.Globalization;

namespace LocalMeetingSubtitle.Storage;

/// <summary>Conversions between CLR values and their SQLite storage representation.</summary>
internal static class SqliteConvert
{
    /// <summary>Formats a timestamp as a round-trippable ISO-8601 string (culture invariant).</summary>
    public static string ToIso(DateTimeOffset value) =>
        value.ToString("o", CultureInfo.InvariantCulture);

    public static object ToIsoOrNull(DateTimeOffset? value) =>
        value is null ? DBNull.Value : value.Value.ToString("o", CultureInfo.InvariantCulture);

    public static DateTimeOffset ParseIso(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);

    public static DateTimeOffset? ParseIsoOrNull(object? value) =>
        value is null or DBNull ? null : ParseIso((string)value);

    public static long ToMs(TimeSpan value) => (long)value.TotalMilliseconds;

    public static TimeSpan FromMs(long value) => TimeSpan.FromMilliseconds(value);
}
