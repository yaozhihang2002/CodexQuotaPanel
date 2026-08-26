using System.Diagnostics;
using System.Text.Json;
using CodexQuota.Application;
using CodexQuota.Domain;

namespace CodexQuota.Infrastructure;

/// <summary>
/// Reads an official rate-limit snapshot from Codex app-server. The adapter is
/// deliberately isolated because the local JSON-RPC surface is not a public API.
/// </summary>
public sealed class CodexAppServerQuotaSource : IQuotaSource
{
    private readonly string _codexHome;
    private readonly string _executable;

    public CodexAppServerQuotaSource(string? codexHome = null, string? executable = null)
    {
        _codexHome = codexHome ?? new CodexHomeResolver().Resolve();
        _executable = executable ?? ResolveExecutable();
    }

    public async Task<OfficialQuotaSnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = StartProcess();
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await WriteAsync(process, new
            {
                method = "initialize",
                id = 1,
                @params = new
                {
                    clientInfo = new { name = "codex_quota_panel_vnext", title = "Codex Quota Panel", version = "vNext" }
                }
            }, timeout.Token).ConfigureAwait(false);
            await ReadResultAsync(process, 1, timeout.Token).ConfigureAwait(false);
            await WriteAsync(process, new { method = "initialized" }, timeout.Token).ConfigureAwait(false);
            await WriteAsync(process, new { method = "account/rateLimits/read", id = 2 }, timeout.Token)
                .ConfigureAwait(false);
            var result = await ReadResultAsync(process, 2, timeout.Token).ConfigureAwait(false);
            return QuotaPayloadParser.ParseAppServerResult(result);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or
                                   OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
        finally
        {
            TryStop(process);
            try { await stderr.ConfigureAwait(false); } catch { }
        }
    }

    private Process StartProcess()
    {
        var info = new ProcessStartInfo
        {
            FileName = _executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        info.ArgumentList.Add("app-server");
        info.ArgumentList.Add("--listen");
        info.ArgumentList.Add("stdio://");
        info.Environment["CODEX_HOME"] = _codexHome;
        info.Environment["RUST_LOG"] = "error";
        var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException("Codex app-server did not start.");
        return process;
    }

    private static async Task WriteAsync(Process process, object value, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonElement> ReadResultAsync(
        Process process,
        long requestId,
        CancellationToken cancellationToken)
    {
        while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var id) || !Matches(id, requestId)) continue;
            if (root.TryGetProperty("error", out var error))
                throw new IOException(error.GetRawText());
            if (root.TryGetProperty("result", out var result)) return result.Clone();
        }
        throw new IOException("Codex app-server closed before returning a result.");
    }

    private static bool Matches(JsonElement value, long expected) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number == expected ||
        value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number) && number == expected;

    private static void TryStop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static string ResolveExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = Path.Combine(local, "OpenAI", "Codex", "bin");
            if (Directory.Exists(root))
            {
                try
                {
                    var candidate = new DirectoryInfo(root).EnumerateFiles("codex.exe", SearchOption.AllDirectories)
                        .OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();
                    if (candidate is not null) return candidate.FullName;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        return OperatingSystem.IsWindows() ? "codex.exe" : "codex";
    }
}
