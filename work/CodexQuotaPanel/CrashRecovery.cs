using System.Reflection;
using System.Text.Json;

namespace CodexQuotaPanel;

internal sealed record CrashSessionState(
    string Schema,
    int SchemaVersion,
    string State,
    string AppVersion,
    DateTimeOffset StartedUtc,
    DateTimeOffset? EndedUtc,
    string? CrashId,
    string? ExceptionType,
    int? HResult);

/// <summary>
/// Tracks only the minimum information required to detect an unclean shutdown.
/// Exception messages, paths, environment variables, and account information are
/// intentionally never persisted.
/// </summary>
internal sealed class CrashRecoverySession
{
    private const string Schema = "codex-quota-panel.session";
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private readonly object _gate = new();
    private readonly string _path;
    private readonly DateTimeOffset _startedUtc;
    private readonly string _appVersion;
    private bool _crashed;
    private bool _completed;

    private CrashRecoverySession(string path, bool previousSessionUnclean)
    {
        _path = path;
        PreviousSessionUnclean = previousSessionUnclean;
        _startedUtc = DateTimeOffset.UtcNow;
        _appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
        WriteState("running", null, null);
    }

    internal bool PreviousSessionUnclean { get; }

    internal static CrashRecoverySession Begin(string? path = null)
    {
        path ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexQuotaPanel",
            "session-state.json");
        var previous = TryLoad(path);
        var unclean = previous is not null &&
                      !string.Equals(previous.State, "clean", StringComparison.OrdinalIgnoreCase);
        return new CrashRecoverySession(path, unclean);
    }

    internal void RecordCrash(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_gate)
        {
            if (_completed || _crashed) return;
            _crashed = true;
            WriteState(
                "crashed",
                Guid.NewGuid().ToString("N")[..12],
                exception);
        }
    }

    internal void CompleteClean()
    {
        lock (_gate)
        {
            if (_completed || _crashed) return;
            _completed = true;
            WriteState("clean", null, null);
        }
    }

    private void WriteState(string state, string? crashId, Exception? exception)
    {
        var model = new CrashSessionState(
            Schema,
            CurrentSchemaVersion,
            state,
            _appVersion,
            _startedUtc,
            string.Equals(state, "running", StringComparison.Ordinal) ? null : DateTimeOffset.UtcNow,
            crashId,
            exception?.GetType().FullName,
            exception?.HResult);
        AtomicJsonFile.TryWrite(_path, JsonSerializer.Serialize(model, JsonOptions), createBackup: false);
    }

    private static CrashSessionState? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > 32 * 1024) return null;
            var state = JsonSerializer.Deserialize<CrashSessionState>(File.ReadAllText(path), JsonOptions);
            if (state is null ||
                !string.Equals(state.Schema, Schema, StringComparison.Ordinal) ||
                state.SchemaVersion != CurrentSchemaVersion)
                return null;
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or JsonException or
                                   NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
