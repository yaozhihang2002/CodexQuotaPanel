using CodexQuota.Application;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace CodexQuota.Platform.macOS;

public sealed class MacOSPlatformShell : IPlatformShell
{
    private const string LaunchAgentName = "io.github.yaozhihang2002.codexquotapanel.plist";

    public string PlatformName => "macOS";
    public bool SupportsClickThrough => true;
    public bool SupportsMenuBarOrTray => true;
    public bool SupportsGlobalShortcut => true;

    private static string LaunchAgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", LaunchAgentName);

    public bool GetStartWithSystem() => File.Exists(LaunchAgentPath);

    public AppLanguage? GetInitialLanguage() => null;

    public void SetStartWithSystem(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(LaunchAgentPath);
            return;
        }

        var executable = SecurityElement.Escape(Environment.ProcessPath ??
            throw new InvalidOperationException("Executable path is unavailable."));
        Directory.CreateDirectory(Path.GetDirectoryName(LaunchAgentPath)!);
        var plist = new StringBuilder()
            .AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">")
            .AppendLine("<plist version=\"1.0\"><dict>")
            .AppendLine("<key>Label</key><string>io.github.yaozhihang2002.codexquotapanel</string>")
            .AppendLine($"<key>ProgramArguments</key><array><string>{executable}</string><string>--startup</string></array>")
            .AppendLine("<key>RunAtLoad</key><true/>")
            .AppendLine("</dict></plist>")
            .ToString();
        File.WriteAllText(LaunchAgentPath, plist, new UTF8Encoding(false));
    }

    public void SetClickThrough(nint nativeWindowHandle, bool enabled)
    {
        var window = ResolveNativeWindow(nativeWindowHandle, "setIgnoresMouseEvents:");
        if (window == 0) return;
        objc_msgSend_bool(window, sel_registerName("setIgnoresMouseEvents:"), enabled);
    }

    public void SetWindowTopMost(nint nativeWindowHandle, bool enabled)
    {
        var window = ResolveNativeWindow(nativeWindowHandle, "setLevel:");
        if (window == 0) return;
        // NSStatusWindowLevel (25) stays above normal app windows without
        // entering the screen-saver level. Zero restores a normal window.
        objc_msgSend_nint(window, sel_registerName("setLevel:"), enabled ? 25 : 0);
    }

    public void SetWindowDarkMode(nint nativeWindowHandle, bool enabled) { }

    public IGlobalShortcutRegistration? RegisterRecoveryShortcut(Action callback) => new RecoveryHotkey(callback);

    public void PlayAlertSound() => Process.Start("/usr/bin/afplay", "/System/Library/Sounds/Glass.aiff");

    public void OpenUri(Uri uri) => Process.Start("/usr/bin/open", uri.AbsoluteUri);

    public void RestartApplication()
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.");
        Process.Start(new ProcessStartInfo(path, $"--restart-after {Environment.ProcessId}") { UseShellExecute = true });
    }

    private static nint ResolveNativeWindow(nint handle, string requiredSelector)
    {
        if (handle == 0) return 0;
        var required = sel_registerName(requiredSelector);
        var responds = sel_registerName("respondsToSelector:");
        if (objc_msgSend_bool_result(handle, responds, required)) return handle;
        var windowSelector = sel_registerName("window");
        if (!objc_msgSend_bool_result(handle, responds, windowSelector)) return 0;
        var window = objc_msgSend_result(handle, windowSelector);
        return window != 0 && objc_msgSend_bool_result(window, responds, required) ? window : 0;
    }

    private sealed class RecoveryHotkey : IGlobalShortcutRegistration
    {
        private const uint EventClassKeyboard = 0x6B657962;
        private const uint EventHotKeyPressed = 6;
        private const uint ControlKey = 1u << 12;
        private const uint OptionKey = 1u << 11;
        private const uint ShiftKey = 1u << 9;
        private const uint KeyCodeQ = 12;
        private readonly EventHandlerDelegate _handler;
        private nint _handlerRef;
        private nint _hotKeyRef;

        public bool IsRegistered => _hotKeyRef != nint.Zero;

        public RecoveryHotkey(Action callback)
        {
            _handler = (_, _, _) =>
            {
                try { callback(); } catch { }
                return 0;
            };
            var target = GetApplicationEventTarget();
            var type = new EventTypeSpec { EventClass = EventClassKeyboard, EventKind = EventHotKeyPressed };
            if (InstallEventHandler(target, _handler, 1, ref type, nint.Zero, out _handlerRef) != 0) return;
            var id = new EventHotKeyId { Signature = 0x4351504C, Id = 1 };
            if (RegisterEventHotKey(KeyCodeQ, ControlKey | OptionKey | ShiftKey, id, target, 0, out _hotKeyRef) == 0) return;
            RemoveEventHandler(_handlerRef);
            _handlerRef = nint.Zero;
        }

        public void Dispose()
        {
            if (_hotKeyRef != nint.Zero) UnregisterEventHotKey(_hotKeyRef);
            if (_handlerRef != nint.Zero) RemoveEventHandler(_handlerRef);
            _hotKeyRef = _handlerRef = nint.Zero;
            GC.KeepAlive(_handler);
        }
    }

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern nint sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_bool(nint receiver, nint selector, [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_nint(nint receiver, nint selector, nint value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_bool_result(nint receiver, nint selector, nint argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_result(nint receiver, nint selector);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EventHandlerDelegate(nint nextHandler, nint eventRef, nint userData);

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec { public uint EventClass; public uint EventKind; }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId { public uint Signature; public uint Id; }

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern nint GetApplicationEventTarget();

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int InstallEventHandler(nint target, EventHandlerDelegate handler, uint count,
        ref EventTypeSpec eventTypes, nint userData, out nint handlerRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId hotKeyId,
        nint target, uint options, out nint hotKeyRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int UnregisterEventHotKey(nint hotKeyRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int RemoveEventHandler(nint handlerRef);
}
