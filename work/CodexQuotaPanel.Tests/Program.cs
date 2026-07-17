using CodexQuotaPanel;
using System.Collections.Concurrent;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.Json;

TestProcessGuard.Install();

if (args.Length == 1 && args[0] is "--targeted-check" or "--v020-targeted-check")
{
    L10n.SetLanguage(AppLanguage.SimplifiedChinese);
    if (L10n.RestartApp != "重启应用")
        throw new InvalidOperationException("The Chinese restart menu label is missing.");
    L10n.SetLanguage(AppLanguage.English);
    if (L10n.RestartApp != "Restart app")
        throw new InvalidOperationException("The English restart menu label is missing.");
    L10n.SetLanguage(AppLanguage.SimplifiedChinese);

    var panel = QuotaForm.ScaleLogicalBounds(new Rectangle(0, 0, 368, 500), 168);
    var primaryRow = QuotaForm.ScaleLogicalBounds(new Rectangle(18, 224, 332, 70), 168);
    var secondaryRow = QuotaForm.ScaleLogicalBounds(new Rectangle(18, 302, 332, 70), 168);
    var primaryButton = QuotaForm.ScaleLogicalBounds(new Rectangle(108, 462, 242, 28), 168);
    if (panel != new Rectangle(0, 0, 644, 875) ||
        primaryRow.Bottom >= secondaryRow.Top || primaryButton.Bottom >= panel.Bottom)
        throw new InvalidOperationException("The deterministic 175% DPI layout check failed.");

    var negativeArea = new Rectangle(-1920, 0, 1920, 1080);
    var clamped = DisplayPlacement.ClampToArea(new Rectangle(-2100, 1000, 132, 132), negativeArea);
    if (clamped != new Rectangle(-1920, 948, 132, 132) ||
        DisplayPlacement.ScaleLogicalPixels(88, 144) != 132 ||
        !QuotaForm.IsOrbDragGesture(new Size(5, 0), Size.Empty, new Size(8, 8)) ||
        QuotaForm.IsOrbDragGesture(new Size(2, 2), Size.Empty, new Size(8, 8)))
        throw new InvalidOperationException("The native-drag or DPI-aware monitor placement check failed.");

    Console.WriteLine($"PASS targeted check | restart labels zh/en | 175% panel={panel.Width}x{panel.Height} | DPI-aware negative-screen placement");
    return;
}

if ((args.Length >= 2 && args[0] is "--preview" or "--settings-overlap-preview" or "--settings-save-preview" or "--alert-layout-preview" or "--alert-editor-preview" or "--reminder-preview" or "--data-about-preview" or "--tray-icon-preview" or "--settings-header-preview" or "--flame-style-preview" or "--flame-state-preview" or "--flame-motion-preview" or "--motion-performance-preview" or "--layered-runtime-preview" or "--startup-orb-preview" or "--hover-preview" or "--detail-preview" or "--theme-preview" or "--menu-preview" or "--animation-preview" or "--collapse-animation-preview") ||
    args.Contains("--stability", StringComparer.OrdinalIgnoreCase))
{
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
}

if (args.Length >= 2 && args[0] == "--settings-overlap-preview")
{
    FocusedSettingsOverlapPreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--alert-layout-preview")
{
    AlertLayoutPreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--alert-editor-preview")
{
    AlertEditorPreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--reminder-preview")
{
    ReminderPromptPreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--settings-save-preview")
{
    SettingsSavePreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--data-about-preview")
{
    DataAboutPreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--tray-icon-preview")
{
    TrayIconPreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--settings-header-preview")
{
    SettingsHeaderPreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--flame-style-preview")
{
    FlameStylePreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--flame-state-preview")
{
    FlameStatePreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--flame-motion-preview")
{
    FlameMotionPreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--motion-performance-preview")
{
    MotionPerformancePreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--layered-runtime-preview")
{
    LayeredRuntimePreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--startup-orb-preview")
{
    StartupOrbPreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--hover-preview")
{
    HoverPreviewScreenshot.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--detail-preview")
{
    DetailPanelPreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--theme-preview")
{
    ThemePreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--menu-preview")
{
    MenuContrastPreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--animation-preview")
{
    AnimationPreview.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--collapse-animation-preview")
{
    AnimationPreview.Run(args[1], collapsing: true);
    return;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static QuotaSnapshot Snapshot(
    double primaryRemaining,
    double secondaryRemaining,
    DateTimeOffset observedAt,
    DateTimeOffset? primaryReset = null,
    DateTimeOffset? secondaryReset = null,
    string source = "App Server",
    string? reachedType = null,
    int primaryWindow = 300,
    int secondaryWindow = 10080) => new(
        "codex",
        null,
        new LimitBucket(100d - primaryRemaining, primaryWindow, primaryReset),
        new LimitBucket(100d - secondaryRemaining, secondaryWindow, secondaryReset),
        null,
        "pro",
        reachedType,
        observedAt,
        source);

static DateTimeOffset LocalTodayAt(int hour, int minute) =>
    new(DateTime.Now.Date.AddHours(hour).AddMinutes(minute));

var syntheticLine = """
{"timestamp":"2026-07-12T05:34:25.392Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"codex","primary":{"used_percent":48.0,"window_minutes":300,"resets_at":1783839611},"secondary":{"used_percent":8.0,"window_minutes":10080,"resets_at":1784426411},"credits":null,"plan_type":"pro","rate_limit_reached_type":null}}}
""";
var rollout = QuotaParser.ParseRolloutLine(syntheticLine);
Assert(rollout is not null, "Synthetic rollout snapshot was not parsed.");
Assert(rollout!.Primary?.UsedPercent == 48d, "Primary rollout usage mismatch.");
Assert(rollout.Secondary?.WindowMinutes == 10080, "Secondary rollout window mismatch.");
Assert(rollout.RemainingPercent == 52d, "Tightest rollout remaining percentage mismatch.");

using var rpcJson = JsonDocument.Parse("""
{
  "rateLimitResetCredits": {
    "availableCount": 4,
    "credits": [
      {"id":"reset-2","title":"Full reset","description":null,"status":"available","grantedAt":1783900000,"expiresAt":1785110400},
      {"id":"reset-1","title":"Full reset","description":null,"status":"available","grantedAt":1783900000,"expiresAt":1784332800},
      {"id":"reset-4","title":"Full reset","description":null,"status":"available","grantedAt":1783900000,"expiresAt":1786492800},
      {"id":"reset-3","title":"Full reset","description":null,"status":"available","grantedAt":1783900000,"expiresAt":1785542400}
    ]
  },
  "rateLimitsByLimitId": {
    "codex_other": {
      "limitId": "codex_other",
      "primary": {"usedPercent": 22, "windowDurationMins": 60, "resetsAt": 1783839611},
      "planType": "pro"
    },
    "codex": {
      "limitId": "codex",
      "primary": {"usedPercent": 48, "windowDurationMins": 300, "resetsAt": 1783839611},
      "secondary": {"usedPercent": 8, "windowDurationMins": 10080, "resetsAt": 1784426411},
      "credits": {"hasCredits": true, "unlimited": false, "balance": "125.5"},
      "planType": "pro"
    }
  }
}
""");
var rpc = QuotaParser.ParseAppServerResult(rpcJson.RootElement);
Assert(rpc is not null, "Synthetic RPC snapshot was not parsed.");
Assert(rpc!.LimitId == "codex", "RPC parser did not prefer the core codex limit.");
Assert(rpc.AdditionalLimitCount == 1, "RPC additional limit count mismatch.");
Assert(rpc.Credits?.Balance == "125.5", "RPC credits mismatch.");
Assert(rpc.ResetCredits?.AvailableCount == 4, "RPC reset-credit count mismatch.");
Assert(rpc.ResetCredits?.SoonestExpiring?.Id == "reset-1", "RPC parser did not select the earliest expiring reset credit.");
Assert(PanelPreferenceManager.NormalizeOpacity(83) == 83, "Exact opacity preference was not preserved.");
Assert(PanelPreferenceManager.NormalizeOpacity(1) == 30, "Opacity minimum clamp mismatch.");
Assert(PanelPreferenceManager.NormalizeOpacity(120) == 100, "Opacity maximum clamp mismatch.");

var normalizedPreferences = PanelPreferenceManager.Normalize(new PanelPreferences
{
    OrbOpacityPercent = 81,
    OuterWindowMinutes = -1,
    InnerWindowMinutes = 0,
    OuterWindowRole = 99,
    InnerWindowRole = -9,
    OuterColorArgb = Color.FromArgb(0, 1, 2, 3).ToArgb(),
    InnerColorArgb = Color.FromArgb(80, 11, 22, 33).ToArgb(),
    WarningThreshold = 1,
    CriticalThreshold = 100,
    QuietStartMinutes = -4,
    QuietEndMinutes = 9999,
    StartupViewMode = 99,
    LastViewMode = -4,
    OrbSize = 77,
    Language = 42
});
Assert(normalizedPreferences.OrbOpacityPercent == 81, "Preference normalization lost exact opacity.");
Assert(normalizedPreferences.OuterWindowMinutes == 300 && normalizedPreferences.InnerWindowMinutes == 10080,
    "Invalid ring windows did not fall back to defaults.");
Assert(normalizedPreferences.OuterWindowRole == 1 && normalizedPreferences.InnerWindowRole == 0,
    "Ring roles were not clamped.");
Assert(normalizedPreferences.WarningThreshold == 2 && normalizedPreferences.CriticalThreshold == 1,
    "Alert thresholds were not normalized to distinct bands.");
Assert(Color.FromArgb(normalizedPreferences.OuterColorArgb).A == 255 &&
       Color.FromArgb(normalizedPreferences.InnerColorArgb).A == 255,
    "Ring colors were not normalized to opaque colors.");
Assert(normalizedPreferences.QuietStartMinutes == 0 && normalizedPreferences.QuietEndMinutes == 1439 &&
       normalizedPreferences.Language == 1,
    "Time or language preferences were not clamped.");
Assert(normalizedPreferences.StartupViewMode == 3 && normalizedPreferences.LastViewMode == 0 &&
       normalizedPreferences.OrbSize == 77,
    "Startup view or orb-size preferences were not normalized.");
Assert(PanelPreferenceManager.NormalizeOrbSize(55) == PanelPreferenceManager.MinimumOrbSize &&
       PanelPreferenceManager.NormalizeOrbSize(56) == 56 &&
       PanelPreferenceManager.NormalizeOrbSize(97) == 97 &&
       PanelPreferenceManager.NormalizeOrbSize(192) == 192 &&
       PanelPreferenceManager.NormalizeOrbSize(193) == PanelPreferenceManager.MaximumOrbSize,
    "Continuous orb-size clamping is incorrect.");
Assert(PanelPreferenceManager.Default.AlwaysOnTop && !PanelPreferenceManager.Default.SnapToEdge &&
       PanelPreferenceManager.Default.TrendRecordingEnabled && PanelPreferenceManager.Default.ConsumptionFlameEnabled &&
       PanelPreferenceManager.Default.GlobalHotKeyEnabled,
    "Safe utility defaults were not preserved.");

var preferenceTestKey = $@"Software\CodexQuotaPanel.Tests\{Guid.NewGuid():N}";
try
{
    using (var legacyKey = Registry.CurrentUser.CreateSubKey(preferenceTestKey, writable: true))
        legacyKey.SetValue("SnapToEdge", 1, RegistryValueKind.DWord);
    Assert(!PanelPreferenceManager.Load(Registry.CurrentUser, preferenceTestKey).SnapToEdge,
        "The old opt-out snap default was not migrated to free positioning.");

    var persistedPreferences = PanelPreferenceManager.Default with
    {
        OrbOpacityPercent = 79,
        OrbSize = 97,
        SnapToEdge = true,
        ConsumptionFlameEnabled = false,
        Language = 1
    };
    PanelPreferenceManager.Save(Registry.CurrentUser, preferenceTestKey, persistedPreferences);
    var reloadedPreferences = PanelPreferenceManager.Load(Registry.CurrentUser, preferenceTestKey);
    Assert(reloadedPreferences.OrbOpacityPercent == 79 && reloadedPreferences.OrbSize == 97 &&
           reloadedPreferences.SnapToEdge && !reloadedPreferences.ConsumptionFlameEnabled &&
           reloadedPreferences.Language == 1,
        "Isolated registry preference round-trip lost a setting.");
}
finally
{
    Registry.CurrentUser.DeleteSubKeyTree(preferenceTestKey, throwOnMissingSubKey: false);
}

await RecoveryUpdateChecks.RunAsync();

var customConfiguration = new RingDisplayConfiguration(
    new RingWindowSelection(60, RingWindowRole.Primary),
    new RingWindowSelection(43200, RingWindowRole.Secondary),
    Color.FromArgb(255, 12, 155, 210),
    Color.FromArgb(255, 230, 90, 170));
var ringOptions = RingWindowCatalog.GetOptions(rpc, customConfiguration.Outer, customConfiguration.Inner);
Assert(ringOptions.Count == 4 && ringOptions.Count(option => !option.Available) == 2,
    "Ring catalog did not retain unavailable custom windows.");
Assert(RingWindowCatalog.FindBucket(rpc, new RingWindowSelection(300, RingWindowRole.Primary)) == rpc.Primary &&
       RingWindowCatalog.FindBucket(rpc, new RingWindowSelection(10080, RingWindowRole.Secondary)) == rpc.Secondary,
    "Ring catalog did not match the configured quota buckets.");
using (var orb = new QuotaOrbControl())
{
    orb.ConfigureRings(customConfiguration);
    orb.SetSnapshot(rpc, live: true);
    Assert(orb.OuterLabel == "1H" && orb.InnerLabel == "30D", "Custom ring labels are incorrect.");
    Assert(orb.OuterColor.ToArgb() == customConfiguration.OuterColor.ToArgb() &&
           orb.InnerColor.ToArgb() == customConfiguration.InnerColor.ToArgb(),
        "Custom ring colors were not preserved exactly.");
    Assert(!orb.OuterAvailable && !orb.InnerAvailable, "Unavailable custom ring windows were reported as available.");
    orb.SetConsumptionIntensity(0.82d);
    Assert(Math.Abs(orb.ConsumptionIntensity - 0.82d) < 0.001d,
        "Consumption-flame intensity was not preserved.");
    orb.SetFlameAnimationEnabled(false);
    Assert(!orb.FlameAnimationEnabled && !orb.FlameTimerRunning,
        "Disabling the consumption flame did not stop its animation timer.");
}

L10n.SetLanguage(AppLanguage.SimplifiedChinese);
Assert(L10n.AppTitle.Contains("额度", StringComparison.Ordinal) && L10n.FormatWindow(300).Contains("5 小时", StringComparison.Ordinal),
    "Simplified Chinese localization is incomplete.");
L10n.SetLanguage(AppLanguage.English);
Assert(L10n.AppTitle == "Codex Quota Signal" && L10n.FormatWindow(300) == "5-hour window" &&
       L10n.TranslateStatus("App Server 不可用，使用本地快照").StartsWith("App Server unavailable", StringComparison.Ordinal),
    "English localization or coordinator-status translation is incomplete.");
Assert(L10n.FormatWindow(61) == "61-minute window" && L10n.FormatWindow(8 * 1440) == "8-day window",
    "Non-divisible quota windows were rounded to an inaccurate unit.");
using (var englishBodyFont = UiPalette.Body(9f))
using (var numberFont = UiPalette.Mono(8f))
{
    Assert(!englishBodyFont.Name.Contains("Bahnschrift", StringComparison.OrdinalIgnoreCase) &&
           !numberFont.Name.Contains("Cascadia", StringComparison.OrdinalIgnoreCase),
        "Legacy condensed or developer fonts remained in the UI typography stack.");
}
L10n.SetLanguage(AppLanguage.SimplifiedChinese);
using (var chineseBodyFont = UiPalette.Body(9f))
{
    Assert(!chineseBodyFont.Name.Contains("Bahnschrift", StringComparison.OrdinalIgnoreCase),
        "Chinese UI still selected the condensed display font.");
}

var flameNow = DateTimeOffset.UtcNow;
var flameMinute = flameNow.ToUnixTimeSeconds() / 60;
Assert(FlameActivity.Classify(0d) == FlameActivityLevel.Frozen &&
       FlameActivity.Classify(0.12d) == FlameActivityLevel.Cool &&
       FlameActivity.Classify(0.4d) == FlameActivityLevel.Warm &&
       FlameActivity.Classify(0.7d) == FlameActivityLevel.Hot &&
       FlameActivity.Classify(0.9d) == FlameActivityLevel.Inferno,
    "Consumption intensity did not map to the five visual activity states.");
Assert(QuotaForm.CalculateConsumptionIntensity([], flameNow) == 0d,
    "Empty history produced a non-idle consumption flame.");
Assert(QuotaForm.CalculateConsumptionIntensity([
           new QuotaHistoryPoint(flameMinute - 30, 0, 300, 600),
           new QuotaHistoryPoint(flameMinute, 0, 300, 700)
       ], flameNow) == 0d,
    "A quota reset or rising balance was misread as consumption.");
var fastConsumption = QuotaConsumptionRate.Evaluate([
    new QuotaHistoryPoint(flameMinute - 60, 0, 300, 900),
    new QuotaHistoryPoint(flameMinute - 30, 0, 300, 840),
    new QuotaHistoryPoint(flameMinute, 0, 300, 780)
], flameNow);
Assert(fastConsumption.PercentPerHour >= 11.9d && fastConsumption.Intensity >= 0.8d,
    "Fast recent consumption did not produce a vigorous flame.");
Assert(fastConsumption.Activity == FlameActivityLevel.Inferno,
    "Fast recent consumption did not select the inferno visual state.");

var lightDefaultRings = RingDisplayConfiguration.FromPreferences(new PanelPreferences { ThemeMode = 2 });
var lightThemeColors = UiPalette.ResolveColors(2);
Assert(lightDefaultRings.OuterColor.ToArgb() == lightThemeColors.Mint.ToArgb() &&
       lightDefaultRings.InnerColor.ToArgb() == lightThemeColors.Sky.ToArgb(),
    "Default ring colours did not adapt to the light theme.");

var alertNow = DateTimeOffset.Now;
var alertReset = alertNow.AddHours(3);
var alertPreferences = new PanelPreferences
{
    AlertsEnabled = true,
    WarningThreshold = 20,
    CriticalThreshold = 10
};
var alertState = new AlertDedupState();
var warningDecision = QuotaAlertEvaluator.Evaluate(
    Snapshot(20, 80, alertNow, alertReset, alertNow.AddDays(6)), alertPreferences, alertNow, alertState);
Assert(warningDecision?.Level == QuotaAlertLevel.Warning, "Warning threshold boundary did not notify.");
var cycleMuteState = new AlertDedupState();
var cycleMuteDecision = QuotaAlertEvaluator.Evaluate(
    Snapshot(20, 80, alertNow, alertReset, alertNow.AddDays(6)), alertPreferences, alertNow, cycleMuteState);
Assert(cycleMuteDecision is not null, "Cycle-mute setup did not produce a warning.");
cycleMuteState.SuppressCycle(cycleMuteDecision!.BucketKey, cycleMuteDecision.CycleKey);
Assert(QuotaAlertEvaluator.Evaluate(
    Snapshot(5, 80, alertNow, alertReset, alertNow.AddDays(6)), alertPreferences, alertNow, cycleMuteState) is null,
    "A muted quota cycle still escalated to another alert.");
Assert(QuotaAlertEvaluator.Evaluate(
    Snapshot(5, 80, alertNow, alertReset.AddHours(1), alertNow.AddDays(6)), alertPreferences, alertNow, cycleMuteState)?.Level == QuotaAlertLevel.Critical,
    "Cycle mute did not expire when the reset cycle changed.");
Assert(QuotaAlertEvaluator.Evaluate(
    Snapshot(20, 80, alertNow, alertReset, alertNow.AddDays(6)), alertPreferences, alertNow, alertState) is null,
    "Duplicate warning was not suppressed.");
var criticalDecision = QuotaAlertEvaluator.Evaluate(
    Snapshot(10, 80, alertNow, alertReset, alertNow.AddDays(6)), alertPreferences, alertNow, alertState);
Assert(criticalDecision?.Level == QuotaAlertLevel.Critical, "Warning-to-critical escalation did not notify.");
Assert(QuotaAlertEvaluator.Evaluate(
    Snapshot(30, 80, alertNow, alertReset, alertNow.AddDays(6)), alertPreferences, alertNow, alertState) is null,
    "Recovered quota unexpectedly notified.");
Assert(QuotaAlertEvaluator.Evaluate(
    Snapshot(20, 80, alertNow, alertReset, alertNow.AddDays(6)), alertPreferences, alertNow, alertState)?.Level == QuotaAlertLevel.Warning,
    "Recovery did not reset alert deduplication.");
Assert(QuotaAlertEvaluator.Evaluate(
    Snapshot(50, 80, alertNow, alertReset.AddHours(3), alertNow.AddDays(6), reachedType: "primary"),
    alertPreferences, alertNow, new AlertDedupState())?.Level == QuotaAlertLevel.Critical,
    "Blocked quota did not force a critical alert.");

var quietPreferences = alertPreferences with
{
    QuietHoursEnabled = true,
    QuietStartMinutes = 23 * 60,
    QuietEndMinutes = 8 * 60
};
Assert(QuotaAlertEvaluator.IsQuietTime(LocalTodayAt(23, 0), quietPreferences), "Quiet hours did not include the start boundary.");
Assert(QuotaAlertEvaluator.IsQuietTime(LocalTodayAt(7, 59), quietPreferences), "Cross-midnight quiet hours ended too early.");
Assert(!QuotaAlertEvaluator.IsQuietTime(LocalTodayAt(8, 0), quietPreferences), "Quiet hours included the end boundary.");
var quietState = new AlertDedupState();
Assert(QuotaAlertEvaluator.Evaluate(
    Snapshot(15, 80, alertNow, alertReset, alertNow.AddDays(6)), quietPreferences, LocalTodayAt(23, 30), quietState) is null &&
       quietState.Snapshot().Count == 0,
    "Quiet hours consumed the alert deduplication state.");
Assert(QuotaAlertEvaluator.Evaluate(
    Snapshot(15, 80, alertNow, alertReset, alertNow.AddDays(6)), quietPreferences, LocalTodayAt(12, 0), quietState) is not null,
    "Alert did not resume after quiet hours.");
Assert(!QuotaAlertEvaluator.IsQuietTime(LocalTodayAt(12, 0), quietPreferences with
{
    QuietStartMinutes = 120,
    QuietEndMinutes = 120
}), "Equal quiet-hours endpoints caused permanent silence.");
var unknownCycleState = new AlertDedupState();
var unknownResetSnapshot = Snapshot(15, 80, alertNow);
var unknownCycleStart = DateTimeOffset.FromUnixTimeSeconds(
    alertNow.ToUniversalTime().ToUnixTimeSeconds() / (6 * 60 * 60) * (6 * 60 * 60) + 60);
Assert(QuotaAlertEvaluator.Evaluate(unknownResetSnapshot, alertPreferences, unknownCycleStart, unknownCycleState) is not null &&
       QuotaAlertEvaluator.Evaluate(unknownResetSnapshot, alertPreferences, unknownCycleStart.AddHours(1), unknownCycleState) is null &&
       QuotaAlertEvaluator.Evaluate(unknownResetSnapshot, alertPreferences, unknownCycleStart.AddHours(6), unknownCycleState) is not null,
    "Unknown reset cycles were either noisy or permanently suppressed.");

var historyDirectory = Path.Combine(Path.GetTempPath(), "CodexQuotaPanel.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(historyDirectory);
var historyPath = Path.Combine(historyDirectory, "history-v1.json");
try
{
    var historyTime = DateTimeOffset.UtcNow.AddMinutes(-12);
    var history = new QuotaHistoryStore(historyPath);
    Assert(history.Record(Snapshot(90, 80, historyTime)), "Initial history sample was not recorded.");
    Assert(history.Enabled && history.Count == 2 &&
           history.LastRecordedAt?.ToUnixTimeSeconds() / 60 == historyTime.ToUnixTimeSeconds() / 60,
        "History status did not report its enabled state, point count, or last sample time.");
    Assert(!history.Record(Snapshot(89.8, 80, historyTime.AddMinutes(2))),
        "History sampled a sub-0.5% change before five minutes.");
    Assert(history.Record(Snapshot(89.5, 80, historyTime.AddMinutes(2))),
        "History did not sample the exact 0.5% change boundary.");
    Assert(history.Record(Snapshot(88, 79, historyTime.AddMinutes(7))), "Five-minute history sample was not recorded.");
    var reloadedHistory = new QuotaHistoryStore(historyPath).GetRecent();
    Assert(reloadedHistory.Count >= 4 && reloadedHistory.All(point => point.Slot is 0 or 1),
        "History did not survive reload or mixed its slots.");
    var historyJson = File.ReadAllText(historyPath);
    Assert(!historyJson.Contains("codex", StringComparison.OrdinalIgnoreCase) &&
           !historyJson.Contains("pro", StringComparison.OrdinalIgnoreCase) &&
           !historyJson.Contains("App Server", StringComparison.OrdinalIgnoreCase) &&
           !historyJson.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase) &&
           !historyJson.Contains(historyDirectory, StringComparison.OrdinalIgnoreCase),
        "History file leaked identity, plan, source, or filesystem details.");

    var retainedCount = history.Count;
    Assert(history.SetEnabled(false) && !history.Enabled,
        "History could not be disabled while retaining existing samples.");
    Assert(!history.Record(Snapshot(40, 30, historyTime.AddMinutes(10))) &&
           history.Count == retainedCount && File.ReadAllText(historyPath) == historyJson,
        "Disabled history changed memory or its persisted data.");

    var diagnostics = SanitizedDiagnostics.Create(
        Path.Combine(historyDirectory, Environment.UserName, "session.jsonl"),
        historyTime,
        history);
    Assert(diagnostics.Contains("Application version:", StringComparison.Ordinal) &&
           diagnostics.Contains("Windows version:", StringComparison.Ordinal) &&
           diagnostics.Contains("Process:", StringComparison.Ordinal) &&
           diagnostics.Contains("Data source: Redacted", StringComparison.Ordinal) &&
           diagnostics.Contains("History: Disabled", StringComparison.Ordinal) &&
           diagnostics.Contains($"History points: {retainedCount}", StringComparison.Ordinal),
        "Sanitized diagnostics omitted required neutral support fields.");
    Assert(!diagnostics.Contains(historyDirectory, StringComparison.OrdinalIgnoreCase) &&
           !diagnostics.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase) &&
           !diagnostics.Contains("session.jsonl", StringComparison.OrdinalIgnoreCase),
        "Sanitized diagnostics leaked a user name, full path, or session detail.");

    Assert(history.SetEnabled(true) && history.Enabled &&
           history.Record(Snapshot(88, 79, historyTime.AddMinutes(12))),
        "Re-enabled history did not resume recording.");
    Assert(history.Clear() && history.Count == 0 && history.LastRecordedAt is null && !File.Exists(historyPath),
        "History clear did not remove both in-memory and persisted data.");
    Assert(history.Record(Snapshot(77, 66, DateTimeOffset.UtcNow)) && File.Exists(historyPath),
        "History did not remain usable after being cleared.");
    Assert(history.SetEnabled(false, retainExisting: false) && history.Count == 0 && !File.Exists(historyPath),
        "Disabling history with retention off did not delete existing data.");

    var futureMinute = DateTimeOffset.UtcNow.AddDays(5).ToUnixTimeSeconds() / 60;
    File.WriteAllText(historyPath, JsonSerializer.Serialize(new HistoryFileModel(1,
        [[checked((int)(futureMinute - 28_000_000L)), 0, 300, 500]])));
    var prunedHistory = new QuotaHistoryStore(historyPath);
    Assert(prunedHistory.GetRecent().Count == 0 && prunedHistory.Record(Snapshot(77, 66, DateTimeOffset.UtcNow)),
        "A future history point blocked current samples.");
}
finally
{
    Directory.Delete(historyDirectory, recursive: true);
}

var highDpiPanel = QuotaForm.ScaleLogicalBounds(new Rectangle(0, 0, 368, 500), 168);
var highDpiPrimaryRow = QuotaForm.ScaleLogicalBounds(new Rectangle(18, 224, 332, 70), 168);
var highDpiSecondaryRow = QuotaForm.ScaleLogicalBounds(new Rectangle(18, 302, 332, 70), 168);
var highDpiPrimaryButton = QuotaForm.ScaleLogicalBounds(new Rectangle(108, 462, 242, 28), 168);
Assert(highDpiPanel == new Rectangle(0, 0, 644, 875),
    "175% DPI panel size did not scale from the logical layout.");
Assert(highDpiPrimaryRow.Bottom < highDpiSecondaryRow.Top,
    "175% DPI quota rows overlap after scaling.");
Assert(highDpiPrimaryButton.Bottom < highDpiPanel.Bottom,
    "175% DPI primary action escaped the detail panel.");
var negativeMonitorArea = new Rectangle(-1920, 0, 1920, 1080);
Assert(DisplayPlacement.ClampToArea(new Rectangle(-2100, 1000, 132, 132), negativeMonitorArea) ==
       new Rectangle(-1920, 948, 132, 132),
    "Negative-coordinate monitor placement did not remain fully visible.");
Assert(DisplayPlacement.ScaleLogicalPixels(88, 144) == 132,
    "Target-monitor DPI scaling did not preserve the logical orb size.");
Assert(QuotaForm.IsOrbDragGesture(new Size(5, 0), Size.Empty, new Size(8, 8)) &&
       !QuotaForm.IsOrbDragGesture(new Size(2, 2), Size.Empty, new Size(8, 8)),
    "Native orb drag gesture detection did not preserve the Windows drag threshold.");

Console.WriteLine("PASS pre-release logic | DPI layout, native drag, reset credits, themes, transitions, preferences, typography, rings, flame, alerts, history, diagnostics, localization");

if (args.Contains("--stability", StringComparer.OrdinalIgnoreCase))
{
    var stabilityArgument = Array.FindIndex(args,
        argument => string.Equals(argument, "--stability", StringComparison.OrdinalIgnoreCase));
    var stabilityOutput = stabilityArgument >= 0 && stabilityArgument + 1 < args.Length &&
                          !args[stabilityArgument + 1].StartsWith("--", StringComparison.Ordinal)
        ? Path.GetFullPath(args[stabilityArgument + 1])
        : Path.Combine(Environment.CurrentDirectory, "work", "stability-qa", "root");
    StabilitySuite.Run(stabilityOutput);
}

// Creating WinForms controls installs a WindowsFormsSynchronizationContext.
// Live integration tests run without Application.Run, so release the context
// before awaiting background sources to avoid posting continuations to a UI
// message loop that does not exist in this test process.
SynchronizationContext.SetSynchronizationContext(null);

if (args.Contains("--live-rpc", StringComparer.OrdinalIgnoreCase))
{
    var liveRpcSnapshot = new TaskCompletionSource<QuotaSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    var liveStatuses = new ConcurrentQueue<string>();
    await using var liveRpc = new AppServerQuotaSource();
    liveRpc.SnapshotAvailable += snapshot => liveRpcSnapshot.TrySetResult(snapshot);
    liveRpc.StatusChanged += liveStatuses.Enqueue;
    using var liveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var liveStarted = await liveRpc.TryStartAsync(liveTimeout.Token);
    Console.WriteLine($"TRACE live RPC | start returned {liveStarted}");
    foreach (var status in liveStatuses) Console.WriteLine($"TRACE live RPC | {status}");
    if (!liveStarted)
    {
        Console.WriteLine("SKIP live RPC | app-server unavailable in this execution context");
    }
    else
    {
        var live = await liveRpcSnapshot.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert(live.Buckets.Any(), "Live RPC returned no rate-limit buckets.");
        Console.WriteLine($"PASS live RPC | plan={live.PlanType} | limits={live.Buckets.Count()}");
    }
}
else
{
    Console.WriteLine("SKIP live RPC | pass --live-rpc for the external integration check");
}

var sessionsRoot = Path.Combine(CodexPaths.Home, "sessions");
var latestFiles = Directory.EnumerateFiles(sessionsRoot, "*.jsonl", SearchOption.AllDirectories)
    .Select(path => new FileInfo(path))
    .OrderByDescending(file => file.LastWriteTimeUtc)
    .Take(20);

QuotaSnapshot? real = null;
foreach (var file in latestFiles)
{
    using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    stream.Seek(Math.Max(0, stream.Length - 2 * 1024 * 1024), SeekOrigin.Begin);
    using var reader = new StreamReader(stream);
    var lines = reader.ReadToEnd().Split('\n');
    foreach (var line in lines.Reverse())
    {
        real = QuotaParser.ParseRolloutLine(line.TrimEnd('\r'));
        if (real is not null) break;
    }
    if (real is not null) break;
}

Assert(real is not null, "No real local quota snapshot was found.");
Assert(real!.Buckets.All(bucket => bucket.UsedPercent is >= 0 and <= 100), "Real usage is out of range.");
Console.WriteLine($"PASS synthetic + local | plan={real.PlanType} | primary={real.Primary?.UsedPercent:0}% | secondary={real.Secondary?.UsedPercent:0}%");

if (args.Contains("--integration", StringComparer.OrdinalIgnoreCase) ||
    args.Contains("--live-rpc", StringComparer.OrdinalIgnoreCase))
{
    var firstSnapshot = new TaskCompletionSource<QuotaSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    await using var coordinator = new QuotaCoordinator();
    coordinator.SnapshotChanged += snapshot => firstSnapshot.TrySetResult(snapshot);
    await coordinator.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
    var coordinated = await firstSnapshot.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(coordinated.Primary is not null, "Coordinator did not publish a primary limit.");
    Console.WriteLine($"PASS coordinator fallback | source={coordinated.Source}");
}
else
{
    Console.WriteLine("SKIP coordinator integration | pass --integration to exercise external sources");
}

if (args.Length >= 2 && args[0] == "--preview")
{
    L10n.SetLanguage(AppLanguage.SimplifiedChinese);
    var previewDirectory = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
    var previewStem = Path.GetFileNameWithoutExtension(args[1]);
    var previewExtension = Path.GetExtension(args[1]);
    Directory.CreateDirectory(previewDirectory);
    string PreviewPath(string suffix) => Path.Combine(previewDirectory, $"{previewStem}-{suffix}{previewExtension}");

    using var form = new QuotaForm();
    var previewNowMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
    QuotaHistoryPoint[] previewHistory = [
        new QuotaHistoryPoint(previewNowMinute - 60, 0, 300, 680),
        new QuotaHistoryPoint(previewNowMinute - 45, 0, 300, 610),
        new QuotaHistoryPoint(previewNowMinute - 30, 0, 300, 570),
        new QuotaHistoryPoint(previewNowMinute - 15, 0, 300, 540),
        new QuotaHistoryPoint(previewNowMinute - 60, 1, 10080, 940),
        new QuotaHistoryPoint(previewNowMinute - 45, 1, 10080, 930),
        new QuotaHistoryPoint(previewNowMinute - 30, 1, 10080, 925),
        new QuotaHistoryPoint(previewNowMinute - 15, 1, 10080, 920)
    ];
    form.SetHistory(previewHistory);
    var previewConfiguration = new RingDisplayConfiguration(
        new RingWindowSelection(300, RingWindowRole.Primary),
        new RingWindowSelection(10080, RingWindowRole.Secondary),
        Color.FromArgb(255, 80, 230, 170),
        Color.FromArgb(255, 145, 125, 255));
    form.ConfigureRings(previewConfiguration);
    form.ApplySnapshot(rpc!);
    Assert(form.ConsumptionFlameEnabled && form.ConsumptionIntensity >= 0.8d,
        "Recent fast consumption did not reach the orb flame.");
    Assert(form.IsCollapsed && form.ClientSize == new Size(88, 88), "Form did not start in compact orb mode.");
    form.ShowDetails(animate: false);
    Assert(!form.IsCollapsed && !form.IsAnimating && form.ClientSize == new Size(368, 500),
        "Form did not expand to the detail card.");
    form.Show();
    Application.DoEvents();
    form.SavePreview(args[1]);
    var orbPath = PreviewPath("orb");
    form.CollapseToOrb(animate: false);
    Assert(form.IsCollapsed && !form.IsAnimating && form.ClientSize == new Size(88, 88),
        "Form did not collapse back to the quota orb.");
    Application.DoEvents();
    Assert(form.ClientSize.Width == form.ClientSize.Height && form.OrbBounds == form.ClientRectangle,
        "Collapsed form and orb geometry were not normalized to the same square.");
    using (var regionGraphics = form.CreateGraphics())
    {
        var formRegion = form.Region ?? throw new InvalidOperationException("Collapsed form region was not created.");
        var regionBounds = Rectangle.Round(formRegion.GetBounds(regionGraphics));
        Assert(regionBounds.Width == form.ClientSize.Width && Math.Abs(regionBounds.Height - form.ClientSize.Height) <= 1,
            "Collapsed circular region did not match the client square.");
        Assert(formRegion.IsVisible(form.ClientSize.Width / 2, form.ClientSize.Height / 2) &&
               !formRegion.IsVisible(0, 0) &&
               !formRegion.IsVisible(form.ClientSize.Width - 1, 0),
            "Collapsed region was square-bounded but not actually circular.");
    }
    form.SavePreview(orbPath);
    form.SetConsumptionFlameEnabled(false);
    form.SetHistory([]);
    form.SetConsumptionFlameEnabled(true);
    Assert(form.ConsumptionIntensity == 0d, "Idle history did not cool the flame.");
    form.SavePreview(PreviewPath("orb-cool-zh"));
    form.SetConsumptionFlameEnabled(false);
    form.SetHistory(previewHistory);
    form.SetConsumptionFlameEnabled(true);

    form.SetOrbSize(PanelPreferenceManager.MinimumOrbSize);
    Assert(form.OrbLogicalSize == PanelPreferenceManager.MinimumOrbSize && form.ClientSize.Width == form.ClientSize.Height &&
           form.ClientSize.Width == form.OrbPixelSize,
        "Minimum custom orb size did not preserve square geometry.");
    form.SavePreview(PreviewPath("orb-small-zh"));
    form.SetOrbSize(97);
    Assert(form.OrbLogicalSize == 97 && form.ClientSize.Width == form.ClientSize.Height &&
           form.ClientSize.Width == form.OrbPixelSize,
        "Exact custom orb size did not apply immediately.");
    form.SavePreview(PreviewPath("orb-custom-zh"));
    form.SetOrbSize(PanelPreferenceManager.MaximumOrbSize);
    Assert(form.OrbLogicalSize == PanelPreferenceManager.MaximumOrbSize && form.ClientSize.Width == form.ClientSize.Height &&
           form.ClientSize.Width == form.OrbPixelSize,
        "Maximum custom orb size did not preserve square geometry.");
    form.SavePreview(PreviewPath("orb-large-zh"));
    form.SetOrbSize(88);
    form.SetConsumptionFlameEnabled(false);
    Assert(!form.ConsumptionFlameEnabled && !form.OrbControl.FlameTimerRunning,
        "Turning off the flame did not stop animation immediately.");
    form.SavePreview(PreviewPath("orb-no-flame-zh"));
    form.SetConsumptionFlameEnabled(true);
    Application.DoEvents();
    Assert(form.OrbControl.FlameAnimationEnabled && form.OrbControl.FlameTimerRunning,
        "Turning the flame back on did not resume its lightweight animation.");
    form.SetPositionLocked(true);
    form.SetSnapToEdge(false);
    Assert(form.PositionLocked && !form.SnapToEdge, "Position lock or edge-snap setting did not apply.");
    form.SetPositionLocked(false);
    form.SetSnapToEdge(true);
    var snapArea = Screen.FromControl(form).WorkingArea;
    var snapY = snapArea.Top + Math.Min(80, Math.Max(0, snapArea.Height - form.ClientSize.Height));
    var nearLeft = new Point(snapArea.Left + Math.Max(1, form.OrbSnapThresholdPixels - 1), snapY);
    var awayFromLeft = new Point(snapArea.Left + form.OrbSnapThresholdPixels + 8, snapY);
    Assert(form.ResolveReleasedOrbLocation(nearLeft).X == snapArea.Left,
        "Near-edge release did not snap within the configured threshold.");
    Assert(form.ResolveReleasedOrbLocation(awayFromLeft).X == awayFromLeft.X,
        "Orb snapped even though it was outside the edge threshold.");
    Assert(form.ResolveReleasedOrbLocation(nearLeft, bypassSnap: true).X == nearLeft.X,
        "Shift-style snap bypass did not preserve the freely chosen position.");
    form.HidePanel();
    Assert(form.IsHidden && !form.Visible, "Tray-only mode did not hide the orb.");
    form.ShowOrb(animate: false);
    Assert(form.IsOrb && form.Visible, "Hidden orb did not restore from tray-only mode.");

    var opacityEditorPath = PreviewPath("opacity-zh");
    using (var editor = new OpacityEditorForm(73))
    {
        var previewValue = -1;
        editor.PreviewChanged += value => previewValue = value;
        editor.SetOpacityForTest(64);
        Assert(editor.SelectedOpacity == 64 && previewValue == 64,
            "Precise opacity editor did not preserve an exact percentage.");
        editor.Show();
        Application.DoEvents();
        editor.SavePreview(opacityEditorPath);
        editor.Hide();
    }

    var oddSnapshot = Snapshot(51, 74, DateTimeOffset.Now,
        DateTimeOffset.Now.AddMinutes(42), DateTimeOffset.Now.AddDays(21),
        primaryWindow: 60, secondaryWindow: 43200);
    var oddConfiguration = new RingDisplayConfiguration(
        new RingWindowSelection(60, RingWindowRole.Primary),
        new RingWindowSelection(43200, RingWindowRole.Secondary),
        Color.FromArgb(255, 25, 180, 235),
        Color.FromArgb(255, 235, 110, 155));
    using (var ringEditor = new RingSettingsForm(oddSnapshot, oddConfiguration))
    {
        ringEditor.SetColorsForTest(Color.FromArgb(12, 155, 210), Color.FromArgb(230, 90, 170));
        Assert(ringEditor.SelectedConfiguration.OuterColor == Color.FromArgb(255, 12, 155, 210) &&
               ringEditor.SelectedConfiguration.InnerColor == Color.FromArgb(255, 230, 90, 170),
            "Ring settings preview did not preserve arbitrary RGB colors.");
        ringEditor.RestoreDefaultsForTest();
        Assert(ringEditor.SelectedConfiguration.Outer == new RingWindowSelection(300, RingWindowRole.Primary) &&
               ringEditor.SelectedConfiguration.Inner == new RingWindowSelection(10080, RingWindowRole.Secondary),
            "Restore defaults failed when the live snapshot had non-default windows.");
        ringEditor.Show();
        Application.DoEvents();
        ringEditor.SavePreview(PreviewPath("rings-zh"));
        ringEditor.Hide();
    }

    using (var alertEditor = new AlertSettingsForm(new PanelPreferences
    {
        AlertsEnabled = true,
        WarningThreshold = 23,
        CriticalThreshold = 9,
        QuietHoursEnabled = true,
        QuietStartMinutes = 23 * 60,
        QuietEndMinutes = 8 * 60
    }))
    {
        alertEditor.SetThresholdsForTest(10, 10);
        Assert(!alertEditor.InputsValid, "Alert settings accepted equal warning and critical thresholds.");
        alertEditor.SetThresholdsForTest(23, 9);
        Assert(alertEditor.InputsValid, "Valid alert thresholds were rejected.");
        alertEditor.Show();
        Application.DoEvents();
        alertEditor.SavePreview(PreviewPath("alerts-zh"));
        alertEditor.Hide();
    }

    using (var hover = new HoverPeekForm())
    {
        hover.SetData(rpc!, previewConfiguration);
        Assert(hover.UsesPassiveWindowStyles, "Hover preview can steal activation or mouse input.");
        hover.SavePreview(PreviewPath("hover-zh"));
    }

    var settingsPreviewPreferences = new PanelPreferences
    {
        OrbOpacityPercent = 73,
        AlwaysOnTop = true,
        StartupViewMode = 0,
        OrbSize = 88,
        SnapToEdge = true,
        GlobalHotKeyEnabled = true,
        AlertsEnabled = true,
        WarningThreshold = 23,
        CriticalThreshold = 9,
        QuietHoursEnabled = true,
        QuietStartMinutes = 23 * 60,
        QuietEndMinutes = 8 * 60
    };
    using (var settingsZh = new SettingsForm(settingsPreviewPreferences, startupEnabled: true, rpc,
               "Codex Quota Panel diagnostics\r\nApplication version: 1.6.1\r\nData source: App Server"))
    {
        var centerArea = Screen.PrimaryScreen!.WorkingArea;
        settingsZh.CenterOnDisplay(new Point(centerArea.Left + centerArea.Width / 2, centerArea.Top + centerArea.Height / 2));
        Assert(Math.Abs(settingsZh.Bounds.Left + settingsZh.Width / 2 - (centerArea.Left + centerArea.Width / 2)) <= 1 &&
               Math.Abs(settingsZh.Bounds.Top + settingsZh.Height / 2 - (centerArea.Top + centerArea.Height / 2)) <= 1,
            "Settings did not center on the selected display.");
        PanelPreferences? liveSettingsPreview = null;
        settingsZh.PreviewPreferencesChanged += preferences => liveSettingsPreview = preferences;
        settingsZh.Show();
        Application.DoEvents();
        Assert(settingsZh.SaveButtonVisible && !settingsZh.IsDirty,
            "Settings footer was missing or a new settings form started dirty.");
        settingsZh.SetOrbSizeForTest(97);
        Application.DoEvents();
        Assert(settingsZh.SelectedOrbSize == 97 && settingsZh.SelectedPreferences.OrbSize == 97 &&
               settingsZh.IsDirty && liveSettingsPreview?.OrbSize == 97 && settingsZh.SaveButtonVisible,
            "Precise orb size did not update the settings preview, dirty state, or visible save action.");
        settingsZh.SetLanguageForTest(1);
        Application.DoEvents();
        Assert(settingsZh.Text == "Codex Quota Panel settings" && settingsZh.SelectedPreferences.Language == 1 &&
               settingsZh.IsDirty && settingsZh.SaveButtonVisible,
            "Open settings did not relocalize immediately after switching to English.");
        settingsZh.SetLanguageForTest(0);
        Application.DoEvents();
        Assert(settingsZh.Text.Contains("额度面板设置", StringComparison.Ordinal) &&
               settingsZh.SelectedPreferences.Language == 0,
            "Open settings did not switch immediately back to Chinese.");
        for (var page = 0; page < 5; page++)
        {
            settingsZh.SelectPageForTest(page);
            Application.DoEvents();
            settingsZh.SavePreview(PreviewPath($"settings-{page + 1}-zh"));
        }
        settingsZh.Hide();
    }

    using (var settingsSaveCheck = new SettingsForm(settingsPreviewPreferences, startupEnabled: true, rpc))
    {
        settingsSaveCheck.Show();
        Application.DoEvents();
        settingsSaveCheck.SetOrbSizeForTest(101);
        settingsSaveCheck.SaveForTest();
        Assert(settingsSaveCheck.DialogResult == DialogResult.None && settingsSaveCheck.Visible &&
               !settingsSaveCheck.IsDirty && settingsSaveCheck.SelectedPreferences.OrbSize == 101,
            "Save & apply did not keep the settings window open with a clean saved baseline.");
        settingsSaveCheck.SetOrbSizeForTest(103);
        Assert(settingsSaveCheck.IsDirty,
            "Settings could not continue editing after an in-place save.");
        settingsSaveCheck.Close();
    }

    using (var settingsCancelCheck = new SettingsForm(settingsPreviewPreferences, startupEnabled: true, rpc))
    {
        PanelPreferences? restoredPreview = null;
        settingsCancelCheck.PreviewPreferencesChanged += preferences => restoredPreview = preferences;
        settingsCancelCheck.Show();
        Application.DoEvents();
        settingsCancelCheck.SetOrbSizeForTest(113);
        settingsCancelCheck.Close();
        Assert(settingsCancelCheck.DialogResult == DialogResult.Cancel && restoredPreview == settingsPreviewPreferences,
            "Cancel did not restore the original live-preview preferences.");
    }

    var workArea = Screen.FromControl(form).WorkingArea;
    var originalOrbLocation = new Point(workArea.Left + 12, workArea.Top + 12);
    form.Location = originalOrbLocation;
    form.ShowDetails(animate: false);
    form.CollapseToOrb(animate: false);
    Assert(form.Location == originalOrbLocation, "Orb did not return to its original dragged location.");

    var collapsedFrame = new Rectangle(280, 412, 88, 88);
    var expandedFrame = new Rectangle(0, 0, 368, 500);
    var lampQuarter = QuotaForm.InterpolateLampTransition(collapsedFrame, expandedFrame, 0.25d, expanding: true);
    var widthProgress = (lampQuarter.Width - collapsedFrame.Width) / (double)(expandedFrame.Width - collapsedFrame.Width);
    var heightProgress = (lampQuarter.Height - collapsedFrame.Height) / (double)(expandedFrame.Height - collapsedFrame.Height);
    Assert(Math.Abs(heightProgress - widthProgress) < 0.02d && widthProgress > 0.6d,
        "Material transition should grow width and height together without producing a narrow empty column.");

    form.ShowDetails();
    Assert(form.IsAnimating, "Default detail transition should animate.");
    var defaultExpandDeadline = DateTime.UtcNow.AddSeconds(2);
    while (form.IsAnimating && DateTime.UtcNow < defaultExpandDeadline)
    {
        Application.DoEvents();
        Thread.Sleep(10);
    }
    Assert(!form.IsCollapsed && !form.IsAnimating, "Default detail animation did not settle.");
    form.CollapseToOrb();
    Assert(form.IsAnimating, "Default orb transition should animate.");
    var defaultCollapseDeadline = DateTime.UtcNow.AddSeconds(2);
    while (form.IsAnimating && DateTime.UtcNow < defaultCollapseDeadline)
    {
        Application.DoEvents();
        Thread.Sleep(10);
    }
    Assert(form.IsCollapsed && form.OrbBounds == form.ClientRectangle,
        "Default orb animation did not settle into a square orb.");

    form.ShowDetails(animate: true);
    Application.DoEvents();
    form.CollapseToOrb(animate: true);
    var animationDeadline = DateTime.UtcNow.AddSeconds(2);
    while (form.IsAnimating && DateTime.UtcNow < animationDeadline)
    {
        Application.DoEvents();
        Thread.Sleep(10);
    }
    Assert(form.IsCollapsed && !form.IsAnimating, "Reversed expand/collapse animation did not settle in orb mode.");

    form.SetOrbOpacityPercent(73);
    Assert(form.OrbOpacityPercent == 73 && Math.Abs(form.Opacity - 0.73d) < 0.01d,
        "Collapsed orb opacity was not applied.");
    form.SetOrbClickThroughPreference(true);
    Application.DoEvents();
    Assert(form.IsClickThroughActive && form.HasClickThroughWindowStyle,
        "Collapsed orb did not receive the click-through window styles.");
    Assert(form.NativeLayeredAlpha is >= 185 and <= 187,
        "Native layered alpha did not match the 73% orb preference.");

    form.SetOrbOpacityPercent(100);
    Application.DoEvents();
    Assert(form.NativeLayeredAlpha == byte.MaxValue,
        "Native layered alpha remained stale after switching to 100% opacity.");
    form.SetOrbOpacityPercent(73);
    Application.DoEvents();

    form.ShowDetails(animate: false);
    Application.DoEvents();
    Assert(!form.IsClickThroughActive && !form.HasClickThroughWindowStyle && Math.Abs(form.Opacity - 1d) < 0.001d,
        "Expanded detail card did not restore full opacity and interactivity.");

    form.CollapseToOrb(animate: false);
    Application.DoEvents();
    Assert(form.HasClickThroughWindowStyle && Math.Abs(form.Opacity - 0.73d) < 0.01d,
        "Click-through and opacity preferences were not restored after collapsing.");
    form.SetOrbClickThroughPreference(false);
    Application.DoEvents();
    Assert(!form.HasClickThroughWindowStyle, "Click-through style was not removed immediately.");

    form.SetStatus("App Server 不可用，使用本地快照");
    L10n.SetLanguage(AppLanguage.English);
    form.ApplyLanguage();
    form.ShowDetails(animate: false);
    Application.DoEvents();
    Assert(form.BrandText == "CODEX · QUOTA" && form.SectionText == "WINDOWS" &&
           form.StatusText.StartsWith("App Server unavailable", StringComparison.Ordinal) &&
           form.CreditsText.StartsWith("CREDITS", StringComparison.Ordinal),
        "Runtime language switch left stale text or lost the disconnected state.");
    form.SavePreview(PreviewPath("main-en"));

    using (var settingsEn = new SettingsForm(settingsPreviewPreferences with { Language = 1 }, startupEnabled: true, rpc,
               "Codex Quota Panel diagnostics\r\nApplication version: 1.6.1\r\nData source: App Server"))
    {
        settingsEn.Show();
        Application.DoEvents();
        Assert(settingsEn.SaveButtonVisible, "English settings footer hid the Save & apply action.");
        for (var page = 0; page < 5; page++)
        {
            settingsEn.SelectPageForTest(page);
            Application.DoEvents();
            settingsEn.SavePreview(PreviewPath($"settings-{page + 1}-en"));
        }
        settingsEn.Hide();
    }

    using (var ringEditorEn = new RingSettingsForm(rpc, previewConfiguration))
    {
        ringEditorEn.Show();
        Application.DoEvents();
        ringEditorEn.SavePreview(PreviewPath("rings-en"));
        ringEditorEn.Hide();
    }
    using (var alertEditorEn = new AlertSettingsForm(alertPreferences))
    {
        alertEditorEn.Show();
        Application.DoEvents();
        alertEditorEn.SavePreview(PreviewPath("alerts-en"));
        alertEditorEn.Hide();
    }
    using (var opacityEditorEn = new OpacityEditorForm(73))
    {
        opacityEditorEn.Show();
        Application.DoEvents();
        opacityEditorEn.SavePreview(PreviewPath("opacity-en"));
        opacityEditorEn.Hide();
    }
    using (var hoverEn = new HoverPeekForm())
    {
        hoverEn.SetData(rpc!, previewConfiguration);
        hoverEn.SavePreview(PreviewPath("hover-en"));
    }

    form.CollapseToOrb(animate: false);
    form.SavePreview(PreviewPath("orb-en"));
    form.Hide();
    Console.WriteLine($"PASS preview + bilingual UI | {previewDirectory}");
}

internal static class TestProcessGuard
{
    private const uint SemFailCriticalErrors = 0x0001;
    private const uint SemNoGpFaultErrorBox = 0x0002;
    private static int _isExiting;

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);

    internal static void Install()
    {
        // Automated UI checks must report failures through stderr instead of
        // interrupting the desktop with Windows Error Reporting dialogs.
        SetErrorMode(SemFailCriticalErrors | SemNoGpFaultErrorBox);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => ExitCleanly(eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            ExitCleanly(eventArgs.ExceptionObject as Exception ?? new Exception("Unknown test-process failure."));
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            eventArgs.SetObserved();
            ExitCleanly(eventArgs.Exception);
        };
    }

    private static void ExitCleanly(Exception exception)
    {
        if (Interlocked.Exchange(ref _isExiting, 1) != 0)
            return;

        Console.Error.WriteLine($"FAIL {exception.GetType().Name}: {exception.Message}");
        Console.Error.WriteLine(exception.StackTrace);
        Environment.Exit(1);
    }
}
