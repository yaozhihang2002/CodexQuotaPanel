using System.Diagnostics;

namespace CodexQuotaPanel;

internal static class ApplicationRestart
{
    internal const string WaitArgument = "--restart-wait";

    internal static bool TryGetPreviousProcessId(string[] args, out int processId)
    {
        processId = 0;
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (!string.Equals(args[index], WaitArgument, StringComparison.OrdinalIgnoreCase)) continue;
            return int.TryParse(args[index + 1], out processId) && processId > 0;
        }
        return false;
    }

    internal static void WaitForPreviousInstance(string[] args)
    {
        if (!TryGetPreviousProcessId(args, out var processId) || processId == Environment.ProcessId) return;
        try
        {
            using var previous = Process.GetProcessById(processId);
            if (!IsSameExecutable(previous)) return;
            if (previous.WaitForExit(8000)) return;

            // The PID is accepted only after its executable path matches this
            // application, so a stale/forged argument cannot terminate another app.
            previous.Kill(entireProcessTree: false);
            previous.WaitForExit(3000);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
                                   System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
    }

    internal static bool TryLaunch(out string? error)
    {
        error = null;
        try
        {
            var executable = Environment.ProcessPath ?? Application.ExecutablePath;
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"{WaitArgument} {Environment.ProcessId}",
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            });
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or
                                   FileNotFoundException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool IsSameExecutable(Process process)
    {
        var current = Environment.ProcessPath ?? Application.ExecutablePath;
        var other = process.MainModule?.FileName;
        return !string.IsNullOrWhiteSpace(other) &&
               string.Equals(Path.GetFullPath(current), Path.GetFullPath(other), StringComparison.OrdinalIgnoreCase);
    }
}
