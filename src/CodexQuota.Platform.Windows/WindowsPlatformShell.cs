using CodexQuota.Application;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CodexQuota.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformShell : IPlatformShell
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExLayered = 0x00080000L;
    private const int HwndTopmost = -1;
    private const int HwndNotTopmost = -2;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValue = "CodexQuotaPanel";
    private const string PreferencesKey = @"Software\CodexQuotaPanel";

    public string PlatformName => "Windows";
    public bool SupportsClickThrough => true;
    public bool SupportsMenuBarOrTray => true;
    public bool SupportsGlobalShortcut => true;

    public bool GetStartWithSystem()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupKey, false);
        return key?.GetValue(StartupValue) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public AppLanguage? GetInitialLanguage()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PreferencesKey, false);
        return key?.GetValue("Language") is int value
            ? value == 1 ? AppLanguage.English : AppLanguage.SimplifiedChinese
            : null;
    }

    public void SetStartWithSystem(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupKey, true);
        if (enabled)
            key.SetValue(StartupValue, $"\"{Environment.ProcessPath}\" --startup");
        else
            key.DeleteValue(StartupValue, false);
    }

    public void SetClickThrough(nint nativeWindowHandle, bool enabled)
    {
        if (nativeWindowHandle == 0) return;
        var style = GetWindowLongPtr(nativeWindowHandle, GwlExStyle).ToInt64();
        style = enabled
            ? style | WsExTransparent | WsExLayered
            : style & ~WsExTransparent;
        SetWindowLongPtr(nativeWindowHandle, GwlExStyle, new IntPtr(style));
    }

    public void SetWindowTopMost(nint nativeWindowHandle, bool enabled)
    {
        if (nativeWindowHandle == 0) return;
        SetWindowPos(nativeWindowHandle, enabled ? new IntPtr(HwndTopmost) : new IntPtr(HwndNotTopmost),
            0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    public void SetWindowDarkMode(nint nativeWindowHandle, bool enabled)
    {
        if (nativeWindowHandle == 0) return;
        var value = enabled ? 1 : 0;
        if (DwmSetWindowAttribute(nativeWindowHandle, 20, ref value, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(nativeWindowHandle, 19, ref value, sizeof(int));
    }

    public IGlobalShortcutRegistration? RegisterRecoveryShortcut(Action callback) => new RecoveryHotkey(callback);

    public void PlayAlertSound() => MessageBeep(0x00000030);

    public void OpenUri(Uri uri) => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });

    public void RestartApplication()
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.");
        Process.Start(new ProcessStartInfo(path, $"--restart-after {Environment.ProcessId}") { UseShellExecute = true });
    }

    private sealed class RecoveryHotkey : IGlobalShortcutRegistration
    {
        private const int HotkeyId = 0x4351;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint WmHotkey = 0x0312;
        private const uint WmQuit = 0x0012;
        private const uint VkQ = 0x51;
        private readonly Action _callback;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new(false);
        private uint _threadId;
        private bool _registered;

        public bool IsRegistered => _registered;

        public RecoveryHotkey(Action callback)
        {
            _callback = callback;
            _thread = new Thread(Run) { IsBackground = true, Name = "CodexQuota recovery hotkey" };
            _thread.Start();
            _ready.Wait(TimeSpan.FromSeconds(2));
        }

        private void Run()
        {
            _threadId = GetCurrentThreadId();
            _registered = RegisterHotKey(IntPtr.Zero, HotkeyId, ModAlt | ModControl | ModShift, VkQ);
            _ready.Set();
            if (!_registered) return;
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
                if (message.Message == WmHotkey && message.WParam == new IntPtr(HotkeyId))
                    try { _callback(); } catch { }
            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        }

        public void Dispose()
        {
            if (_registered && _threadId != 0) PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            if (_thread.IsAlive) _thread.Join(1_000);
            _ready.Dispose();
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr value);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, value) : SetWindowLong32(hWnd, nIndex, value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint type);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, IntPtr hWnd, uint min, uint max);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr HWnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
        public uint Private;
    }
}
