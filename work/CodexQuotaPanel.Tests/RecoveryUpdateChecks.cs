using CodexQuotaPanel;
using System.Net;
using System.Text;
using System.Text.Json;

internal static class RecoveryUpdateChecks
{
    internal static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CodexQuotaPanel.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckLegacyPreferences(directory);
            CheckPortableTransfer(directory);
            CheckCrashRecovery(directory);
            CheckRestartArguments();
            await CheckUpdatesAsync(directory);
            Console.WriteLine("PASS settings migration + portable transfer + recovery + update checks");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static void CheckLegacyPreferences(string directory)
    {
        var legacyPath = Path.Combine(directory, "legacy-preferences.json");
        File.WriteAllText(legacyPath, """
        {
          "OrbOpacityPercent": 73,
          "OrbX": -1520,
          "OrbY": 244,
          "OrbSize": 127,
          "OuterWindowMinutes": 60,
          "InnerWindowMinutes": 43200,
          "OuterColorArgb": -14535868,
          "InnerColorArgb": -3644366,
          "ConsumptionFlameStyle": 2,
          "ThemeMode": 2,
          "Language": 1
        }
        """, Encoding.UTF8);
        var loaded = PanelPreferenceManager.LoadFromFile(legacyPath);
        Require(loaded is not null && loaded.OrbOpacityPercent == 73 && loaded.OrbX == -1520 &&
                loaded.OrbY == 244 && loaded.OrbSize == 127 && loaded.OuterWindowMinutes == 60 &&
                loaded.InnerWindowMinutes == 43200 && loaded.ConsumptionFlameStyle == 2 &&
                loaded.ThemeMode == 2 && loaded.Language == 1 && !loaded.CheckForUpdatesOnStartup,
            "Legacy bare JSON did not preserve old fields or add the new default.");

        var versionedPath = Path.Combine(directory, "versioned-preferences.json");
        Require(PanelPreferenceManager.SaveToFile(versionedPath, loaded!), "Versioned preferences could not be written.");
        var reloaded = PanelPreferenceManager.LoadFromFile(versionedPath);
        Require(reloaded == PanelPreferenceManager.Normalize(loaded!), "Versioned preference round-trip was not lossless.");
        var versionedJson = File.ReadAllText(versionedPath);
        Require(versionedJson.Contains("codex-quota-panel.preferences", StringComparison.Ordinal),
            "Versioned local preference schema marker is missing.");

        // Simulate the exact v0.2.x reader. It knows nothing about schema fields
        // and must still see every old property at the JSON root.
        var legacyReader = JsonSerializer.Deserialize<PanelPreferences>(versionedJson);
        Require(legacyReader is not null && legacyReader.OrbX == -1520 && legacyReader.OrbY == 244 &&
                legacyReader.OrbSize == 127 && legacyReader.ThemeMode == 2 && legacyReader.Language == 1 &&
                legacyReader.OuterColorArgb == loaded!.OuterColorArgb &&
                legacyReader.InnerColorArgb == loaded.InnerColorArgb,
            "A v0.2.x rollback would reset settings written by the new version.");

        // Retain compatibility with the nested envelope briefly used by an
        // internal preview before the rollback-safe root format was adopted.
        var previewEnvelopePath = Path.Combine(directory, "preview-envelope.json");
        File.WriteAllText(previewEnvelopePath, JsonSerializer.Serialize(new
        {
            Schema = "codex-quota-panel.preferences",
            SchemaVersion = 1,
            Preferences = loaded
        }), Encoding.UTF8);
        var previewEnvelope = PanelPreferenceManager.LoadFromFile(previewEnvelopePath);
        Require(previewEnvelope == PanelPreferenceManager.Normalize(loaded!),
            "The temporary preview envelope can no longer be migrated.");

        var emptyPath = Path.Combine(directory, "empty-object.json");
        File.WriteAllText(emptyPath, "{}", Encoding.UTF8);
        Require(PanelPreferenceManager.LoadFromFile(emptyPath) is null,
            "An empty object was accepted as valid settings instead of allowing backup recovery.");
    }

    private static void CheckPortableTransfer(string directory)
    {
        var path = Path.Combine(directory, "portable.json");
        var source = PanelPreferenceManager.Default with
        {
            OrbX = -1800,
            OrbY = 300,
            LastViewMode = 2,
            OrbOpacityPercent = 68,
            OrbSize = 136,
            OuterWindowMinutes = 60,
            ThemeMode = 2,
            Language = 1,
            CheckForUpdatesOnStartup = true,
            ShowClickThroughReminder = false
        };
        Require(SettingsTransferService.TryExport(path, source, out var exportFailure) &&
                exportFailure == SettingsTransferFailure.None,
            "Portable settings export failed.");
        var json = File.ReadAllText(path);
        Require(!json.Contains("orbX", StringComparison.OrdinalIgnoreCase) &&
                !json.Contains("orbY", StringComparison.OrdinalIgnoreCase) &&
                !json.Contains("lastViewMode", StringComparison.OrdinalIgnoreCase) &&
                !json.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase),
            "Portable settings leaked device coordinates, view state, or identity.");

        var device = PanelPreferenceManager.Default with { OrbX = 111, OrbY = 222, LastViewMode = 1 };
        Require(SettingsTransferService.TryImport(path, device, out var imported, out var importFailure) &&
                importFailure == SettingsTransferFailure.None,
            "Portable settings import failed.");
        Require(imported.OrbX == 111 && imported.OrbY == 222 && imported.LastViewMode == 1 &&
                imported.OrbOpacityPercent == 68 && imported.OrbSize == 136 &&
                imported.OuterWindowMinutes == 60 && imported.ThemeMode == 2 &&
                imported.Language == 1 && imported.CheckForUpdatesOnStartup &&
                !imported.ShowClickThroughReminder,
            "Import did not preserve device state or portable personalization.");

        var futurePath = Path.Combine(directory, "future.json");
        File.WriteAllText(futurePath, json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99", StringComparison.Ordinal));
        Require(!SettingsTransferService.TryImport(futurePath, device, out _, out var futureFailure) &&
                futureFailure == SettingsTransferFailure.UnsupportedVersion,
            "A future settings schema was not rejected safely.");
    }

    private static void CheckCrashRecovery(string directory)
    {
        var path = Path.Combine(directory, "session-state.json");
        var first = CrashRecoverySession.Begin(path);
        var secret = $"sensitive-{Environment.UserName}-{directory}";
        first.RecordCrash(new InvalidOperationException(secret));
        var state = File.ReadAllText(path);
        Require(!state.Contains(secret, StringComparison.Ordinal) &&
                !state.Contains(directory, StringComparison.OrdinalIgnoreCase) &&
                state.Contains(nameof(InvalidOperationException), StringComparison.Ordinal),
            "Crash state leaked exception text/path or omitted the safe exception type.");

        var second = CrashRecoverySession.Begin(path);
        var restartedState = File.ReadAllText(path);
        Require(restartedState.Contains("\"State\": \"running\"", StringComparison.Ordinal) &&
                !restartedState.Contains(nameof(InvalidOperationException), StringComparison.Ordinal),
            "A previous crash was not replaced by a normal startup session.");
        second.CompleteClean();
        var third = CrashRecoverySession.Begin(path);
        third.CompleteClean();
    }

    private static void CheckRestartArguments()
    {
        Require(ApplicationRestart.TryGetPreviousProcessId(["--restart-wait", "1234"], out var processId) &&
                processId == 1234,
            "The restart handoff argument was not parsed.");
        Require(!ApplicationRestart.TryGetPreviousProcessId(["--restart-wait", "invalid"], out _),
            "An invalid restart process id was accepted.");
    }

    private static async Task CheckUpdatesAsync(string directory)
    {
        Require(GitHubReleaseUpdateService.CurrentVersionText == "0.3.1",
            "Release version metadata is not synchronized with the update checker.");
        Require(SemanticVersion.TryParse("v0.3.0-preview.2", out var preview2) &&
                SemanticVersion.TryParse("0.3.0-preview.10", out var preview10) &&
                SemanticVersion.TryParse("0.3.0", out var stable) &&
                SemanticVersion.TryParse("v0.2.0", out var older) &&
                SemanticVersion.TryParse("v0.3.1", out var patch) &&
                SemanticVersion.TryParse("v0.4.0", out var minor) &&
                SemanticVersion.TryParse("v1.0.0", out var major) &&
                preview10.CompareTo(preview2) > 0 && stable.CompareTo(preview10) > 0 &&
                stable.CompareTo(new SemanticVersion(0, 3, 0, null)) == 0 &&
                older.CompareTo(stable) < 0 && patch.CompareTo(stable) > 0 &&
                minor.CompareTo(stable) > 0 && major.CompareTo(stable) > 0,
            "Semantic version ordering is incorrect.");
        Require(!GitHubReleaseUpdateService.TryValidateReleaseUri("https://example.com/evil", out _),
            "A non-GitHub update URL was accepted.");

        var handler = new ReleaseFeedHandler();
        using var service = new GitHubReleaseUpdateService(handler, Path.Combine(directory, "updates.json"));
        var first = await service.CheckAsync(force: true, CancellationToken.None);
        Require(first.Status == UpdateCheckStatus.UpdateAvailable && first.LatestTag == "v99.0.0-preview.1" &&
                first.ReleaseUri?.Host == "github.com",
            "The pre-release feed did not select the latest trusted release.");
        var second = await service.CheckAsync(force: true, CancellationToken.None);
        Require(second.Status == UpdateCheckStatus.UpdateAvailable && second.FromCache,
            "ETag/304 update cache was not reused.");

        var failureHandler = new FailingReleaseFeedHandler();
        using var failureService = new GitHubReleaseUpdateService(
            failureHandler,
            Path.Combine(directory, "updates-failed.json"));
        var failed = await failureService.CheckAsync(force: false, CancellationToken.None);
        var throttled = await failureService.CheckAsync(force: false, CancellationToken.None);
        Require(failed.Status == UpdateCheckStatus.Unavailable &&
                throttled.Status == UpdateCheckStatus.Unavailable &&
                throttled.FromCache && failureHandler.RequestCount == 1,
            "A failed automatic update check was not throttled for 24 hours.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ReleaseFeedHandler : HttpMessageHandler
    {
        private int _requests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests++;
            if (_requests > 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            const string payload = """
            [
              {"tag_name":"v99.0.0-preview.1","html_url":"https://github.com/yaozhihang2002/CodexQuotaPanel/releases/tag/v99.0.0-preview.1","draft":false,"prerelease":true},
              {"tag_name":"v100.0.0","html_url":"https://example.com/evil","draft":false,"prerelease":false}
            ]
            """;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"test-etag\"");
            return Task.FromResult(response);
        }
    }

    private sealed class FailingReleaseFeedHandler : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
