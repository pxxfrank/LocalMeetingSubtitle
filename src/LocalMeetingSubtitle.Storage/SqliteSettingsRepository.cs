using System.Text.Json;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Storage;

/// <summary>
/// SQLite-backed <see cref="ISettingsRepository"/>. The whole <see cref="AppSettings"/> graph is
/// serialized to JSON and stored as a single row in a key/value table (System.Text.Json, no extra
/// package).
/// </summary>
public sealed class SqliteSettingsRepository : ISettingsRepository
{
    private const string SettingsKey = "app_settings";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly SqliteDatabase _database;

    public SqliteSettingsRepository(SqliteDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => _database.InitializeAsync(cancellationToken);

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var value = await SqliteDatabase.ExecuteScalarAsync(
            connection,
            "SELECT Value FROM settings WHERE Key = $key;",
            cancellationToken,
            null,
            ("$key", SettingsKey)).ConfigureAwait(false);

        if (value is not string json || string.IsNullOrWhiteSpace(json))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Corrupt or incompatible payload: fall back to defaults rather than failing startup.
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var json = JsonSerializer.Serialize(settings, JsonOptions);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            "INSERT INTO settings (Key, Value) VALUES ($key, $value) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;",
            cancellationToken,
            null,
            ("$key", SettingsKey),
            ("$value", json)).ConfigureAwait(false);
    }
}
