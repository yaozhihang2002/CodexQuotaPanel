using CodexQuota.Application;
using CodexQuota.Platform.macOS;
using CodexQuota.Platform.Windows;
using System.Runtime.InteropServices;

IPlatformShell shell = OperatingSystem.IsWindows() ? new WindowsPlatformShell() : new MacOSPlatformShell();
shell.SetWindowOpacity(0, .5);
Check.True(shell.SupportsClickThrough, "click-through capability");
Check.True(shell.SupportsMenuBarOrTray, "tray capability");
Check.True(shell.SupportsGlobalShortcut, "global shortcut capability");
shell.SetClickThrough(0, true);
shell.SetWindowTopMost(0, true);
shell.SetWindowDarkMode(0, true);
shell.SetWindowTaskbarVisibility(0, false);
_ = shell.GetStartWithSystem();
_ = shell.GetInitialLanguage();
if (OperatingSystem.IsWindows())
{
    using var registration = shell.RegisterRecoveryShortcut(() => { });
    Check.True(registration is not null, "Windows recovery hotkey registration");
    using var nativeWindow = NativeWindow.Create();
    shell.SetClickThrough(nativeWindow.Handle, true);
    Check.True(nativeWindow.HasExtendedStyle(NativeWindow.WsExTransparent),
        "Windows click-through style enable");
    shell.SetClickThrough(nativeWindow.Handle, false);
    Check.True(!nativeWindow.HasExtendedStyle(NativeWindow.WsExTransparent),
        "Windows click-through style disable");
    shell.SetWindowTaskbarVisibility(nativeWindow.Handle, false);
    Check.True(nativeWindow.HasExtendedStyle(NativeWindow.WsExToolWindow),
        "Windows tool-window style enable");
    Check.True(!nativeWindow.HasExtendedStyle(NativeWindow.WsExAppWindow),
        "Windows app-window style disable");
}
var checkCount = OperatingSystem.IsWindows() ? 8 : 4;
if (string.Equals(Environment.GetEnvironmentVariable("CODEXQUOTA_PLATFORM_MUTATION_TESTS"), "1",
        StringComparison.Ordinal))
{
    var priorStartup = shell.GetStartWithSystem();
    try
    {
        shell.SetStartWithSystem(true);
        Check.True(shell.GetStartWithSystem(), "startup integration enable");
        shell.SetStartWithSystem(false);
        Check.True(!shell.GetStartWithSystem(), "startup integration disable");
        using var recovery = shell.RegisterRecoveryShortcut(() => { });
        Check.True(recovery?.IsRegistered == true, "platform recovery shortcut registration");
        checkCount += 3;
    }
    finally
    {
        shell.SetStartWithSystem(priorStartup);
    }
}
Console.WriteLine($"Platform checks passed: {shell.PlatformName} ({checkCount})");

sealed class NativeWindow : IDisposable
{
    public const long WsExTransparent = 0x00000020L;
    public const long WsExToolWindow = 0x00000080L;
    public const long WsExAppWindow = 0x00040000L;
    private const int GwlExStyle = -20;
    private const uint WsPopup = 0x80000000;

    public nint Handle { get; }

    private NativeWindow(nint handle) => Handle = handle;

    public static NativeWindow Create()
    {
        var handle = CreateWindowExW(0, "STATIC", "CodexQuotaClickThroughTest", WsPopup,
            0, 0, 8, 8, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (handle == 0) throw new InvalidOperationException("Unable to create native click-through test window.");
        return new NativeWindow(handle);
    }

    public bool HasExtendedStyle(long style) => (GetWindowLongPtrW(Handle, GwlExStyle).ToInt64() & style) != 0;

    public void Dispose() => _ = DestroyWindow(Handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint extendedStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);
}

static class Check
{
    public static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException($"{name}: expected true");
    }
}
