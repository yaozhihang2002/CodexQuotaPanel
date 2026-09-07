using System.Text.Json;
using Avalonia;
using Avalonia.Threading;
using CodexQuota.Platform.Windows;
using CodexQuota.UI.Avalonia;

namespace CodexQuota.App;

internal sealed partial class RuntimeCoordinator
{
    private DisplaySessionMonitor? _displayMonitor;
    private DispatcherTimer? _displayDebounce;
    private DispatcherTimer? _displayPoll;
    private bool _displaySurfaceInvalid;
    private bool _displaySuspended;
    private bool _displayRebuilding;
    private int _displayEpoch;
    private string _displayFingerprint = "";
    private DateTimeOffset _nextMismatchRepair;

    private bool IsRemoteDesktop => OperatingSystem.IsWindows() && DisplaySessionMonitor.IsRemoteSession;

    private void StartDisplayRecovery()
    {
        if (!OperatingSystem.IsWindows() || _orb is null) return;
        _displayDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _displayDebounce.Tick += async (_, _) =>
        {
            _displayDebounce.Stop();
            await RecoverDisplaySurfaceAsync();
        };
        try
        {
            _displayMonitor = new DisplaySessionMonitor((message, value) => Dispatcher.UIThread.Post(() =>
            {
                if (message == 0x02B1)
                {
                    if (value is 2 or 4 or 7) _displaySuspended = true;
                    if (value is 1 or 3 or 8 or 15) _displaySuspended = false;
                }
                InvalidateDisplaySurface($"native-{message:X}-{value}");
            }));
        }
        catch (Exception e) { LogSurface("monitor-unavailable-" + e.GetType().Name); }
        _orb.Screens.Changed += OnScreenEnvironmentChanged;
        _displayFingerprint = DisplayFingerprint();
        // Fallback if the session service was not ready or a broadcast was missed.
        _displayPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _displayPoll.Tick += (_, _) =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var current = DisplayFingerprint();
            if (current != _displayFingerprint)
            {
                _displayFingerprint = current;
                InvalidateDisplaySurface("display-fingerprint");
            }
            if (!_dashboardOpening && !_dashboardHiding && !_displayRebuilding &&
                _dashboard?.IsPresented == true && DateTimeOffset.UtcNow >= _nextMismatchRepair &&
                _dashboard.TryGetPlatformHandle()?.Handle is { } handle &&
                DisplaySessionMonitor.ReadGeometry(handle) is { Dpi: > 0 } native &&
                Math.Abs(native.Dpi / 96d - _dashboard.RenderScaling) > .01)
            {
                _nextMismatchRepair = DateTimeOffset.UtcNow.AddSeconds(30);
                InvalidateDisplaySurface("native-render-dpi-mismatch");
            }
        };
        _displayPoll.Start();
        LogSurface("monitor-started");
    }

    private string DisplayFingerprint() => $"{IsRemoteDesktop}:" + string.Join(";",
        _orb?.Screens.All.Select(s => $"{s.Bounds}/{s.WorkingArea}/{s.Scaling:R}") ?? []);

    private void OnScreenEnvironmentChanged(object? sender, EventArgs e) => InvalidateDisplaySurface("screens-changed");

    private void InvalidateDisplaySurface(string reason)
    {
        if (_lifetime.IsCancellationRequested) return;
        _displaySurfaceInvalid = true;
        _displayEpoch++;
        LogSurface(reason);
        _displayDebounce?.Stop();
        if (!_displaySuspended) _displayDebounce?.Start();
    }

    private async Task RecoverDisplaySurfaceAsync()
    {
        if (!_displaySurfaceInvalid || _displaySuspended || _lifetime.IsCancellationRequested) return;
        if (_dashboardOpening || _dashboardHiding || _displayRebuilding)
        {
            _displayDebounce?.Start();
            return;
        }
        if (_dashboard?.IsPresented != true)
        {
            LogSurface("discard-hidden");
            _dashboard?.ClosePermanently();
            _dashboard = null;
            _displaySurfaceInvalid = false;
            return;
        }

        _displayRebuilding = true;
        var old = _dashboard;
        var epoch = _displayEpoch;
        try
        {
            LogSurface("replace-before");
            var oldPosition = old.Position;
            // The invisible resize border may extend into the neighbouring
            // monitor. Select by the whole window, not its native top-left.
            var screen = old.Screens.ScreenFromWindow(old);
            var displayId = screen is null ? null :
                $"{screen.Bounds.X},{screen.Bounds.Y},{screen.Bounds.Width},{screen.Bounds.Height}";
            _dashboard = null;
            EnsureDashboard();
            var replacement = _dashboard!;
            replacement.RestorePosition(oldPosition.X, oldPosition.Y, displayId);
            replacement.ApplyPresentation(_presentation);
            await replacement.PrepareNativeSurfaceAsync();
            // DWM insets only become measurable after native surface creation.
            replacement.RestorePosition(oldPosition.X, oldPosition.Y, displayId);
            await replacement.AnimateInAsync(animate: false);
            old.ClosePermanently();
            // Do not call OpenDashboardAsync: that would overwrite the hidden
            // orb's restore anchor and persist a different user view.
            ApplyNativeOrbSettings();
            _displaySurfaceInvalid = epoch != _displayEpoch;
            LogSurface("replace-after");
        }
        catch (Exception e)
        {
            if (!ReferenceEquals(_dashboard, old)) _dashboard?.ClosePermanently();
            _dashboard = old;
            LogSurface("replace-failed-" + e.GetType().Name);
            // Keep the existing window usable, and retry on the next real event/open.
        }
        finally
        {
            _displayRebuilding = false;
            if (_displaySurfaceInvalid && epoch != _displayEpoch) _displayDebounce?.Start();
        }
    }

    private void LogSurface(string reason, Point? pointer = null)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var window = _dashboard;
            var handle = window?.TryGetPlatformHandle()?.Handle ?? 0;
            var entry = new
            {
                At = DateTimeOffset.Now, Reason = reason, Epoch = _displayEpoch,
                Remote = IsRemoteDesktop, Suspended = _displaySuspended,
                SessionNotifications = _displayMonitor?.SessionNotificationsRegistered,
                Handle = handle.ToString("X"), Presented = window?.IsPresented,
                Scaling = window?.RenderScaling, Client = window?.ClientSize.ToString(),
                Pointer = pointer?.ToString(),
                Native = DisplaySessionMonitor.ReadGeometry(handle),
                Orb = _orb?.Position.ToString(), Anchor = _orbPositionBeforeDashboard?.ToString()
            };
            var path = Path.Combine(_dataRoot, "display-recovery.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                File.Move(path, path + ".1", true);
            File.AppendAllText(path, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch { /* Diagnostics must not disrupt input or recovery. */ }
    }

    private void StopDisplayRecovery()
    {
        if (!OperatingSystem.IsWindows()) return;
        _displayDebounce?.Stop();
        _displayPoll?.Stop();
        if (_orb is not null) _orb.Screens.Changed -= OnScreenEnvironmentChanged;
        _displayMonitor?.Dispose();
        _displayMonitor = null;
    }
}
