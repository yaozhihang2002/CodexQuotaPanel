using CodexQuota.Application;
using CodexQuota.Domain;
using Microsoft.Data.Sqlite;

namespace CodexQuota.Infrastructure;

public sealed class SqliteUsageHistoryStore : IUsageHistoryStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _connectionString;

    public SqliteUsageHistoryStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS quota_history (
                observed_utc_ms INTEGER NOT NULL,
                window_id TEXT NOT NULL,
                window_minutes INTEGER NOT NULL,
                remaining_percent REAL NOT NULL,
                PRIMARY KEY (observed_utc_ms, window_id, window_minutes)
            );
            CREATE INDEX IF NOT EXISTS ix_quota_history_time ON quota_history(observed_utc_ms);
            CREATE TABLE IF NOT EXISTS usage_events (
                fingerprint TEXT PRIMARY KEY,
                observed_utc_ms INTEGER NOT NULL,
                model TEXT NOT NULL,
                service_tier TEXT NOT NULL,
                service_tier_explicit INTEGER NOT NULL,
                total_tokens INTEGER NOT NULL,
                input_tokens INTEGER NOT NULL,
                cached_input_tokens INTEGER NOT NULL,
                output_tokens INTEGER NOT NULL,
                reasoning_output_tokens INTEGER NOT NULL,
                cache_write_input_tokens INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_usage_events_time ON usage_events(observed_utc_ms);
            """, cancellationToken).ConfigureAwait(false);

        var quotaCutoff = DateTimeOffset.UtcNow.AddHours(-25).ToUnixTimeMilliseconds();
        // The UI reports the active reset cycle (currently at most seven days).
        // Ten days retain a complete cycle plus a safety margin without turning
        // this lightweight panel into an unbounded transcript index.
        var usageCutoff = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        await using (var prune = connection.CreateCommand())
        {
            prune.CommandText = "DELETE FROM quota_history WHERE observed_utc_ms < $quota; " +
                                "DELETE FROM usage_events WHERE observed_utc_ms < $usage;";
            prune.Parameters.AddWithValue("$quota", quotaCutoff);
            prune.Parameters.AddWithValue("$usage", usageCutoff);
            await prune.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var version = connection.CreateCommand();
        version.CommandText = "INSERT INTO metadata(key, value) VALUES('schema_version', $version) " +
                              "ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        version.Parameters.AddWithValue("$version", CurrentSchemaVersion.ToString());
        await version.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendQuotaAsync(OfficialQuotaSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var window in snapshot.VisibleWindows)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO quota_history(observed_utc_ms, window_id, window_minutes, remaining_percent)
                VALUES($time, $id, $minutes, $remaining)
                ON CONFLICT(observed_utc_ms, window_id, window_minutes)
                DO UPDATE SET remaining_percent = excluded.remaining_percent;
                """;
            command.Parameters.AddWithValue("$time", snapshot.ObservedAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$id", window.Id);
            command.Parameters.AddWithValue("$minutes", window.WindowMinutes);
            command.Parameters.AddWithValue("$remaining", window.ClampedRemainingPercent);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendUsageAsync(ObservedUsage usage, CancellationToken cancellationToken)
        => await AppendUsageBatchAsync([usage], cancellationToken).ConfigureAwait(false);

    public async Task AppendUsageBatchAsync(
        IReadOnlyList<ObservedUsage> usages,
        CancellationToken cancellationToken)
    {
        if (usages.Count == 0) return;
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var usage in usages)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
            INSERT INTO usage_events(
                fingerprint, observed_utc_ms, model, service_tier, service_tier_explicit,
                total_tokens, input_tokens, cached_input_tokens, output_tokens,
                reasoning_output_tokens, cache_write_input_tokens)
            VALUES($fingerprint, $time, $model, $tier, $explicit,
                $total, $input, $cached, $output, $reasoning, $cacheWrite)
            ON CONFLICT(fingerprint) DO UPDATE SET
                model = excluded.model,
                service_tier = excluded.service_tier,
                service_tier_explicit = MAX(service_tier_explicit, excluded.service_tier_explicit),
                total_tokens = excluded.total_tokens,
                input_tokens = excluded.input_tokens,
                cached_input_tokens = excluded.cached_input_tokens,
                output_tokens = excluded.output_tokens,
                reasoning_output_tokens = excluded.reasoning_output_tokens,
                cache_write_input_tokens = excluded.cache_write_input_tokens;
            """;
            command.Parameters.AddWithValue("$fingerprint", usage.Fingerprint);
            command.Parameters.AddWithValue("$time", usage.ObservedAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$model", usage.Model);
            command.Parameters.AddWithValue("$tier", usage.ServiceTier);
            command.Parameters.AddWithValue("$explicit", usage.IsServiceTierExplicit ? 1 : 0);
            command.Parameters.AddWithValue("$total", usage.Usage.TotalTokens);
            command.Parameters.AddWithValue("$input", usage.Usage.InputTokens);
            command.Parameters.AddWithValue("$cached", usage.Usage.CachedInputTokens);
            command.Parameters.AddWithValue("$output", usage.Usage.OutputTokens);
            command.Parameters.AddWithValue("$reasoning", usage.Usage.ReasoningOutputTokens);
            command.Parameters.AddWithValue("$cacheWrite", usage.Usage.CacheWriteInputTokens);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<QuotaHistoryPoint>> ReadQuotaAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var result = new List<QuotaHistoryPoint>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT observed_utc_ms, window_id, window_minutes, remaining_percent
            FROM quota_history WHERE observed_utc_ms >= $since
            ORDER BY observed_utc_ms, window_minutes;
            """;
        command.Parameters.AddWithValue("$since", since.ToUnixTimeMilliseconds());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new QuotaHistoryPoint(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.GetString(1), reader.GetInt32(2), reader.GetDouble(3)));
        return result;
    }

    public async Task<IReadOnlyList<ObservedUsage>> ReadUsageAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var result = new List<ObservedUsage>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT observed_utc_ms, model, service_tier, total_tokens, input_tokens,
                   cached_input_tokens, output_tokens, reasoning_output_tokens,
                   cache_write_input_tokens, fingerprint, service_tier_explicit
            FROM usage_events WHERE observed_utc_ms >= $since
            ORDER BY observed_utc_ms, fingerprint;
            """;
        command.Parameters.AddWithValue("$since", since.ToUnixTimeMilliseconds());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new ObservedUsage(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.GetString(1), reader.GetString(2),
                new TokenUsageBreakdown(reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5),
                    reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8)),
                reader.GetString(9), reader.GetInt32(10) != 0));
        return result;
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "DELETE FROM quota_history; DELETE FROM usage_events;", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
