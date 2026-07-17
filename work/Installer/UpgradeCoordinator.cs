using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

[assembly: System.Reflection.AssemblyTitle("CodexQuotaPanel Upgrade Coordinator")]
[assembly: System.Reflection.AssemblyProduct("CodexQuotaPanel")]
[assembly: System.Reflection.AssemblyCompany("CodexQuotaPanel")]
[assembly: System.Reflection.AssemblyVersion("0.3.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.3.1.0")]

namespace CodexQuotaPanelUpgrade
{
    internal static class Program
    {
        private const string ExitEventName = @"Local\CodexQuotaPanel.Exit.v1";
        private const string MarkerFileName = "restart-after-install.flag";

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length >= 1 && string.Equals(args[0], "--close-before-install", StringComparison.OrdinalIgnoreCase))
                    return CloseBeforeInstall();
                if (args.Length >= 1 && string.Equals(args[0], "--restart-after-install", StringComparison.OrdinalIgnoreCase))
                    return RestartAfterInstall(args.Length >= 2 ? args[1] : null);
                if (args.Length >= 2 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
                    return IsProductExecutable(args[1]) ? 0 : 3;
                return 2;
            }
            catch
            {
                return 1;
            }
        }

        private static int CloseBeforeInstall()
        {
            List<Process> applications = FindApplications();
            try
            {
                if (applications.Count == 0)
                {
                    DeleteMarker();
                    return 0;
                }

                string originalExecutable = GetExecutablePath(applications[0]);
                WriteMarker(originalExecutable);
                SignalGracefulExit();
                if (WaitForExit(applications, 6000)) return 0;

                foreach (Process application in applications)
                {
                    if (HasExited(application)) continue;
                    try { application.CloseMainWindow(); }
                    catch (InvalidOperationException) { }
                }
                if (WaitForExit(applications, 1500)) return 0;

                // v0.3.0 and earlier do not listen for ExitEventName while
                // hidden in the tray. Only processes whose executable metadata
                // identifies this product are eligible for this fallback.
                foreach (Process application in applications)
                {
                    if (HasExited(application)) continue;
                    try { application.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
                if (WaitForExit(applications, 4000)) return 0;
                DeleteMarker();
                return 10;
            }
            finally
            {
                foreach (Process application in applications) application.Dispose();
            }
        }

        private static int RestartAfterInstall(string installedExecutable)
        {
            string marker = MarkerPath();
            if (!File.Exists(marker)) return 0;

            string previousExecutable = null;
            try { previousExecutable = File.ReadAllText(marker).Trim(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            DeleteMarker();

            string executable = IsProductExecutable(installedExecutable)
                ? installedExecutable
                : IsProductExecutable(previousExecutable) ? previousExecutable : null;
            if (string.IsNullOrEmpty(executable)) return 0;

            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable),
                UseShellExecute = true
            });
            return 0;
        }

        private static List<Process> FindApplications()
        {
            Process[] candidates = Process.GetProcessesByName("CodexQuotaPanel");
            List<Process> applications = new List<Process>();
            foreach (Process candidate in candidates)
            {
                if (IsProductProcess(candidate)) applications.Add(candidate);
                else candidate.Dispose();
            }
            return applications;
        }

        private static bool IsProductProcess(Process process)
        {
            try { return IsProductExecutable(GetExecutablePath(process)); }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch (NotSupportedException) { return false; }
        }

        private static string GetExecutablePath(Process process)
        {
            return process.MainModule.FileName;
        }

        private static bool IsProductExecutable(string executable)
        {
            if (string.IsNullOrEmpty(executable) || !File.Exists(executable) ||
                !string.Equals(Path.GetFileName(executable), "CodexQuotaPanel.exe", StringComparison.OrdinalIgnoreCase))
                return false;
            try
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(executable);
                return string.Equals(version.ProductName, "Codex Quota Panel", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(version.CompanyName, "CodexQuotaPanel", StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static void SignalGracefulExit()
        {
            EventWaitHandle signal = null;
            try
            {
                signal = EventWaitHandle.OpenExisting(ExitEventName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            catch (UnauthorizedAccessException) { }
            finally
            {
                if (signal != null) signal.Dispose();
            }
        }

        private static bool WaitForExit(IEnumerable<Process> applications, int timeoutMilliseconds)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            foreach (Process application in applications)
            {
                if (HasExited(application)) continue;
                int remaining = Math.Max(0, timeoutMilliseconds - (int)stopwatch.ElapsedMilliseconds);
                if (remaining == 0) return false;
                try
                {
                    if (!application.WaitForExit(remaining)) return false;
                }
                catch (InvalidOperationException) { }
            }
            foreach (Process application in applications)
            {
                if (!HasExited(application)) return false;
            }
            return true;
        }

        private static bool HasExited(Process process)
        {
            try { return process.HasExited; }
            catch (InvalidOperationException) { return true; }
        }

        private static void WriteMarker(string executable)
        {
            string marker = MarkerPath();
            Directory.CreateDirectory(Path.GetDirectoryName(marker));
            File.WriteAllText(marker, executable ?? string.Empty);
        }

        private static void DeleteMarker()
        {
            try
            {
                string marker = MarkerPath();
                if (File.Exists(marker)) File.Delete(marker);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string MarkerPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexQuotaPanel",
                MarkerFileName);
        }
    }
}
