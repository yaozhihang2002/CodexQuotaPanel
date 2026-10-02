using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CodexQuota.Application;
using CodexQuota.Platform.Windows;
using CodexQuota.UI.Avalonia;

if (!OperatingSystem.IsWindows()) return;

AppBuilder.Configure<ThemeProbeApp>().UsePlatformDetect().SetupWithoutStarting();
if (args.Length == 2 && args[0] is "--edge-frames" or "--edge-frames-dual")
{
    EdgeFrameProbe.Run(args[1], dual: args[0] == "--edge-frames-dual");
    return;
}
var settings = new SettingsWindow(AppSettings.Default with { Theme = AppTheme.Dark }, systemDark: true);
settings.WindowStartupLocation = WindowStartupLocation.Manual;
settings.Position = new PixelPoint(30000, 30000);
settings.ShowActivated = false;
var handle = settings.TryGetPlatformHandle()?.Handle ?? 0;
if (handle == 0) throw new InvalidOperationException("Settings HWND is unavailable before Show.");

new WindowsPlatformShell().SetWindowDarkMode(handle, true);
var dark = 0;
var result = DwmGetWindowAttribute(handle, 20, ref dark, sizeof(int));
if (result != 0 || dark != 1)
    throw new InvalidOperationException($"Pre-show dark title bar was not applied: HRESULT={result:X8}, value={dark}.");

settings.Show();
dark = 0;
result = DwmGetWindowAttribute(handle, 20, ref dark, sizeof(int));
if (result != 0 || dark != 1)
    throw new InvalidOperationException($"Show reset the dark title bar: HRESULT={result:X8}, value={dark}.");

settings.ClosePermanently();
Console.WriteLine("PASS: Settings HWND accepts dark title-bar mode before Show and retains it after Show.");

var orb = new OrbWindow();
orb.ApplySettings(new AppSettings { OrbSize = 96, EdgeAutoHide = true, ReducedMotion = true });
var screen = orb.Screens.Primary ?? throw new InvalidOperationException("No native display.");
orb.Position = new PixelPoint(screen.WorkingArea.X, screen.WorkingArea.Y + 150);
orb.Show();
Avalonia.Threading.Dispatcher.UIThread.RunJobs();
var anchor = orb.Position;
var orbHandle = orb.TryGetPlatformHandle()!.Handle;
if (!orb.TryCollapseToEdge()) throw new InvalidOperationException("Native edge collapse failed.");
Avalonia.Threading.Dispatcher.UIThread.RunJobs();
GetWindowRect(orbHandle, out var rect);
Console.WriteLine($"Edge geometry: native={rect.Right-rect.Left}x{rect.Bottom-rect.Top}, screenScale={screen.Scaling}, renderScale={orb.RenderScaling}, client={orb.ClientSize}, desired={orb.Width}x{orb.Height}");
if (Math.Abs(rect.Right - rect.Left - Math.Ceiling(OrbWindow.EdgeWidth * screen.Scaling)) > 2)
    throw new InvalidOperationException($"Native collapsed window too wide: {rect.Right - rect.Left}.");
var outside = new NativePoint(anchor.X + (int)(60 * screen.Scaling), anchor.Y + (int)(48 * screen.Scaling));
if (WindowFromPoint(outside) == orbHandle) throw new InvalidOperationException("Collapsed orb still intercepts the old exterior area.");
orb.ExpandFromEdge();
Avalonia.Threading.Dispatcher.UIThread.RunJobs();
GetWindowRect(orbHandle, out rect);
if (Math.Abs(rect.Right - rect.Left - Math.Ceiling(96 * screen.Scaling)) > 2 || orb.Position != anchor)
    throw new InvalidOperationException("Native orb did not restore its size and anchor.");
orb.ApplySettings(new AppSettings { OrbSize = 96, EdgeAutoHide = true });
foreach (var edge in new[] { DockEdge.Left, DockEdge.Right, DockEdge.Top, DockEdge.Bottom })
{
    var area = screen.WorkingArea;
    var size = (int)Math.Ceiling(96 * screen.Scaling);
    orb.Position = edge switch
    {
        DockEdge.Left => new PixelPoint(area.X, area.Y + 150),
        DockEdge.Right => new PixelPoint(area.Right - size, area.Y + 150),
        DockEdge.Top => new PixelPoint(area.X + 150, area.Y),
        _ => new PixelPoint(area.X + 150, area.Bottom - size)
    };
    anchor = orb.Position;
    if (!orb.TryCollapseToEdge()) throw new InvalidOperationException($"Native {edge} animation did not start.");
    PumpUntil(() => orb.EdgeMotionProgress >= .15);
    if (!orb.IsEdgeAnimating) throw new InvalidOperationException("Native animation skipped intermediate frames.");
    GetWindowRect(orbHandle, out rect);
    if (Math.Abs(rect.Right - rect.Left - size) > 2 || orb.Position != anchor || orb.Opacity is <= 0 or >= 1)
        throw new InvalidOperationException("Native orb must fade without moving or resizing.");
    PumpUntil(() => orb.EdgeMotionProgress >= .65);
    if (orb.Opacity is <= 0 or >= 1) throw new InvalidOperationException("Native arc did not fade in.");
    PumpUntil(() => !orb.IsEdgeAnimating);
    GetWindowRect(orbHandle, out rect);
    var horizontal = edge is DockEdge.Top or DockEdge.Bottom;
    if (Math.Abs(rect.Right - rect.Left - Math.Ceiling((horizontal ? 76 : 32) * screen.Scaling)) > 2 ||
        Math.Abs(rect.Bottom - rect.Top - Math.Ceiling((horizontal ? OrbWindow.EdgeHorizontalHeight : 76) * screen.Scaling)) > 2)
        throw new InvalidOperationException($"Native {edge} geometry incorrect: {rect.Right - rect.Left}x{rect.Bottom - rect.Top}.");
    outside = edge switch
    {
        DockEdge.Left => new NativePoint(anchor.X + (int)(80 * screen.Scaling), anchor.Y + (int)(48 * screen.Scaling)),
        DockEdge.Right => new NativePoint(anchor.X + (int)(10 * screen.Scaling), anchor.Y + (int)(48 * screen.Scaling)),
        DockEdge.Top => new NativePoint(anchor.X + (int)(48 * screen.Scaling), anchor.Y + (int)(80 * screen.Scaling)),
        _ => new NativePoint(anchor.X + (int)(48 * screen.Scaling), anchor.Y + (int)(10 * screen.Scaling))
    };
    if (WindowFromPoint(outside) == orbHandle) throw new InvalidOperationException($"Native {edge} retains an invisible input area.");
    var collapsedPosition = orb.Position;
    var collapsedWidth = rect.Right - rect.Left;
    orb.ExpandFromEdge(animate: true);
    PumpUntil(() => orb.EdgeMotionProgress >= .15);
    GetWindowRect(orbHandle, out rect);
    if (rect.Right - rect.Left != collapsedWidth || orb.Position != collapsedPosition || orb.Opacity is <= 0 or >= 1)
        throw new InvalidOperationException("Native arc must fade without moving or resizing.");
    PumpUntil(() => !orb.IsEdgeAnimating);
    GetWindowRect(orbHandle, out rect);
    if (Math.Abs(rect.Right - rect.Left - size) > 2 || orb.Position != anchor)
        throw new InvalidOperationException($"Native {edge} animation failed to restore anchor.");
}
orb.Close();
Console.WriteLine("PASS: Native four-edge fades keep geometry fixed while visible, restore anchors and release former orb areas.");

static void PumpUntil(Func<bool> finished)
{
    var deadline = Environment.TickCount64 + 2500;
    var frame = new DispatcherFrame();
    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
    timer.Tick += (_, _) => { if (finished() || Environment.TickCount64 >= deadline) frame.Continue = false; };
    timer.Start();
    try { Dispatcher.UIThread.PushFrame(frame); }
    finally { timer.Stop(); }
    if (!finished()) throw new TimeoutException("Native edge animation timed out.");
}

[DllImport("dwmapi.dll")]
static extern int DwmGetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

[DllImport("user32.dll")]
static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
[DllImport("user32.dll")]
static extern nint WindowFromPoint(NativePoint point);

[StructLayout(LayoutKind.Sequential)]
struct NativeRect { public int Left, Top, Right, Bottom; }
[StructLayout(LayoutKind.Sequential)]
readonly struct NativePoint(int x, int y) { public readonly int X = x; public readonly int Y = y; }

sealed class ThemeProbeApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
