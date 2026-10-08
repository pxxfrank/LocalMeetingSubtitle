using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;
using Microsoft.Data.Sqlite;

namespace LocalMeetingSubtitle.Storage;

/// <summary>SQLite-backed <see cref="IHotwordRepository"/>.</summary>
public sealed class SqliteHotwordRepository : IHotwordRepository
{
    private readonly SqliteDatabase _database;

    public SqliteHotwordRepository(SqliteDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => _database.InitializeAsync(cancellationToken);

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    // ----- hotwords ---------------------------------------------------------------------

    public async Task<IReadOnlyList<Hotword>> GetHotwordsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Text, Enabled, GroupId, Score FROM hotwords ORDER BY Id;";

        var results = new List<Hotword>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadHotword(reader));
        }

        return results;
    }

    public async Task<long> UpsertHotwordAsync(Hotword hotword, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hotword);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        try
        {
            long id = hotword.Id;
            if (id > 0)
            {
                var affected = await SqliteDatabase.ExecuteNonQueryAsync(
                    connection,
                    "UPDATE hotwords SET Text = $text, Enabled = $enabled, GroupId = $groupId, Score = $score WHERE Id = $id;",
                    cancellationToken,
                    transaction,
                    ("$text", hotword.Text),
                    ("$enabled", hotword.Enabled ? 1 : 0),
                    ("$groupId", (object?)hotword.GroupId),
                    ("$score", hotword.Score),
                    ("$id", id)).ConfigureAwait(false);

                if (affected == 0)
                {
                    id = await InsertHotwordAsync(connection, transaction, hotword, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                id = await InsertHotwordAsync(connection, transaction, hotword, cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
            hotword.Id = id;
            return id;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task DeleteHotwordAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            "DELETE FROM hotwords WHERE Id = $id;",
            cancellationToken,
            null,
            ("$id", id)).ConfigureAwait(false);
    }

    public async Task ReplaceHotwordsAsync(IEnumerable<Hotword> hotwords, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hotwords);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        try
        {
            await SqliteDatabase.ExecuteNonQueryAsync(
                connection, "DELETE FROM hotwords;", cancellationToken, transaction).ConfigureAwait(false);

            foreach (var hotword in hotwords)
            {
                if (hotword is null)
                {
                    continue;
                }

                await InsertHotwordAsync(connection, transaction, hotword, cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // ----- groups -----------------------------------------------------------------------

    public async Task<IReadOnlyList<HotwordGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GroupId, Name, Enabled, SortOrder FROM hotword_groups ORDER BY SortOrder, GroupId;";

        var results = new List<HotwordGroup>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new HotwordGroup
            {
                GroupId = reader.GetInt64(0),
                Name = reader.GetString(1),
                Enabled = reader.GetInt64(2) != 0,
                SortOrder = reader.GetInt32(3)
            });
        }

        return results;
    }

    public async Task<long> UpsertGroupAsync(HotwordGroup group, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        long id = group.GroupId;
        if (id > 0)
        {
            var affected = await SqliteDatabase.ExecuteNonQueryAsync(
                connection,
                "UPDATE hotword_groups SET Name = $name, Enabled = $enabled, SortOrder = $sortOrder WHERE GroupId = $id;",
                cancellationToken,
                null,
                ("$name", group.Name),
                ("$enabled", group.Enabled ? 1 : 0),
                ("$sortOrder", group.SortOrder),
                ("$id", id)).ConfigureAwait(false);

            if (affected == 0)
            {
                id = await InsertGroupAsync(connection, null, group, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            id = await InsertGroupAsync(connection, null, group, cancellationToken).ConfigureAwait(false);
        }

        group.GroupId = id;
        return id;
    }

    public async Task DeleteGroupAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        try
        {
            await SqliteDatabase.ExecuteNonQueryAsync(
                connection,
                "UPDATE hotwords SET GroupId = NULL WHERE GroupId = $id;",
                cancellationToken,
                transaction,
                ("$id", id)).ConfigureAwait(false);

            await SqliteDatabase.ExecuteNonQueryAsync(
                connection,
                "DELETE FROM hotword_groups WHERE GroupId = $id;",
                cancellationToken,
                transaction,
                ("$id", id)).ConfigureAwait(false);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // ----- correction rules -------------------------------------------------------------

    public async Task<IReadOnlyList<TextCorrectionRule>> GetCorrectionRulesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Pattern, Replacement, Enabled, IsRegex, Priority, WholeTokenOnly, CaseSensitive " +
            "FROM correction_rules ORDER BY Priority DESC, Id;";

        var results = new List<TextCorrectionRule>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new TextCorrectionRule
            {
                Id = reader.GetInt64(0),
                Pattern = reader.GetString(1),
                Replacement = reader.GetString(2),
                Enabled = reader.GetInt64(3) != 0,
                IsRegex = reader.GetInt64(4) != 0,
                Priority = reader.GetInt32(5),
                WholeTokenOnly = reader.GetInt64(6) != 0,
                CaseSensitive = reader.GetInt64(7) != 0
            });
        }

        return results;
    }

    public async Task<long> UpsertCorrectionRuleAsync(TextCorrectionRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        long id = rule.Id;
        if (id > 0)
        {
            var affected = await SqliteDatabase.ExecuteNonQueryAsync(
                connection,
                """
                UPDATE correction_rules
                SET Pattern = $pattern, Replacement = $replacement, Enabled = $enabled, IsRegex = $isRegex,
                    Priority = $priority, WholeTokenOnly = $wholeTokenOnly, CaseSensitive = $caseSensitive
                WHERE Id = $id;
                """,
                cancellationToken,
                null,
                ("$pattern", rule.Pattern),
                ("$replacement", rule.Replacement),
                ("$enabled", rule.Enabled ? 1 : 0),
                ("$isRegex", rule.IsRegex ? 1 : 0),
                ("$priority", rule.Priority),
                ("$wholeTokenOnly", rule.WholeTokenOnly ? 1 : 0),
                ("$caseSensitive", rule.CaseSensitive ? 1 : 0),
                ("$id", id)).ConfigureAwait(false);

            if (affected == 0)
            {
                id = await InsertCorrectionRuleAsync(connection, null, rule, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            id = await InsertCorrectionRuleAsync(connection, null, rule, cancellationToken).ConfigureAwait(false);
        }

        rule.Id = id;
        return id;
    }

    public async Task DeleteCorrectionRuleAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            "DELETE FROM correction_rules WHERE Id = $id;",
            cancellationToken,
            null,
            ("$id", id)).ConfigureAwait(false);
    }

    // ----- helpers ----------------------------------------------------------------------

    private static async Task<long> InsertHotwordAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Hotword hotword,
        CancellationToken cancellationToken)
    {
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            "INSERT INTO hotwords (Text, Enabled, GroupId, Score) VALUES ($text, $enabled, $groupId, $score);",
            cancellationToken,
            transaction,
            ("$text", hotword.Text),
            ("$enabled", hotword.Enabled ? 1 : 0),
            ("$groupId", (object?)hotword.GroupId),
            ("$score", hotword.Score)).ConfigureAwait(false);

        return await SqliteDatabase.LastInsertRowIdAsync(connection, cancellationToken, transaction).ConfigureAwait(false);
    }

    private static async Task<long> InsertGroupAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        HotwordGroup group,
        CancellationToken cancellationToken)
    {
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            "INSERT INTO hotword_groups (Name, Enabled, SortOrder) VALUES ($name, $enabled, $sortOrder);",
            cancellationToken,
            transaction,
            ("$name", group.Name),
            ("$enabled", group.Enabled ? 1 : 0),
            ("$sortOrder", group.SortOrder)).ConfigureAwait(false);

        return await SqliteDatabase.LastInsertRowIdAsync(connection, cancellationToken, transaction).ConfigureAwait(false);
    }

    private static async Task<long> InsertCorrectionRuleAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        TextCorrectionRule rule,
        CancellationToken cancellationToken)
    {
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO correction_rules (Pattern, Replacement, Enabled, IsRegex, Priority, WholeTokenOnly, CaseSensitive)
            VALUES ($pattern, $replacement, $enabled, $isRegex, $priority, $wholeTokenOnly, $caseSensitive);
            """,
            cancellationToken,
            transaction,
            ("$pattern", rule.Pattern),
            ("$replacement", rule.Replacement),
            ("$enabled", rule.Enabled ? 1 : 0),
            ("$isRegex", rule.IsRegex ? 1 : 0),
            ("$priority", rule.Priority),
            ("$wholeTokenOnly", rule.WholeTokenOnly ? 1 : 0),
            ("$caseSensitive", rule.CaseSensitive ? 1 : 0)).ConfigureAwait(false);

        return await SqliteDatabase.LastInsertRowIdAsync(connection, cancellationToken, transaction).ConfigureAwait(false);
    }

    private static Hotword ReadHotword(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Text = reader.GetString(1),
        Enabled = reader.GetInt64(2) != 0,
        GroupId = reader.IsDBNull(3) ? null : reader.GetInt64(3),
        Score = reader.GetFloat(4)
    };
}
