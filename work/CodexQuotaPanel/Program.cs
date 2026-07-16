namespace CodexQuotaPanel;

internal static class Program
{
    private const string MutexName = @"Local\CodexQuotaPanel.Singleton.v1";
    private const string ShowEventName = @"Local\CodexQuotaPanel.Show.v1";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Length >= 2 && string.Equals(args[0], "--preview", StringComparison.OrdinalIgnoreCase))
        {
            RenderPreview(args[1]);
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                using var existingSignal = EventWaitHandle.OpenExisting(ShowEventName);
                existingSignal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        var preferences = PanelPreferenceManager.Load();
        L10n.SetLanguage((AppLanguage)preferences.Language);
        var safeModeRequested = args.Any(argument =>
            string.Equals(argument, "--safe-mode", StringComparison.OrdinalIgnoreCase));
        var recovery = CrashRecoverySession.Begin();
        if (!safeModeRequested && recovery.PreviousSessionUnclean)
            safeModeRequested = AskToUseSafeMode();

        Exception? fatalUiException = null;
        ThreadExceptionEventHandler threadExceptionHandler = (_, eventArgs) =>
        {
            fatalUiException = eventArgs.Exception;
            recovery.RecordCrash(eventArgs.Exception);
            ShowFatalError();
            Application.ExitThread();
        };
        UnhandledExceptionEventHandler domainExceptionHandler = (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
                recovery.RecordCrash(exception);
        };
        Application.ThreadException += threadExceptionHandler;
        AppDomain.CurrentDomain.UnhandledException += domainExceptionHandler;

        try
        {
            using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            using (var context = new QuotaApplicationContext(showSignal, preferences, safeModeRequested))
                Application.Run(context);
            if (fatalUiException is null) recovery.CompleteClean();
        }
        catch (Exception exception)
        {
            recovery.RecordCrash(exception);
            ShowFatalError();
        }
        finally
        {
            Application.ThreadException -= threadExceptionHandler;
            AppDomain.CurrentDomain.UnhandledException -= domainExceptionHandler;
        }
        GC.KeepAlive(mutex);
    }

    private static bool AskToUseSafeMode()
    {
        var result = MessageBox.Show(
            L10n.Pick(
                "检测到上一次运行未正常结束。是否使用安全模式启动？\n\n安全模式只在本次运行中暂时关闭透明、穿透、动画火焰和位置锁定，不会覆盖原有设置。",
                "The previous session did not close normally. Start in safe mode?\n\nSafe mode temporarily disables transparency, click-through, animated flame, and position locking for this run only. Your saved settings will not be overwritten."),
            L10n.AppTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button1);
        return result == DialogResult.Yes;
    }

    private static void ShowFatalError()
    {
        try
        {
            MessageBox.Show(
                L10n.Pick(
                    "程序遇到异常并将关闭。下次启动时可以选择安全模式；诊断记录不会包含账号、路径或会话内容。",
                    "The application encountered an error and will close. You can choose safe mode next time; the recovery record contains no account, path, or session content."),
                L10n.AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
        }
    }

    private static void RenderPreview(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        using var form = new QuotaForm();
        var now = DateTimeOffset.Now;
        form.ApplySnapshot(new QuotaSnapshot(
            "codex",
            null,
            new LimitBucket(48, 300, now.AddHours(2).AddMinutes(17)),
            new LimitBucket(8, 10080, now.AddDays(6).AddHours(18)),
            null,
            "pro",
            null,
            now.AddSeconds(-2),
            "App Server"));
        form.ShowDetails(animate: false);
        form.SavePreview(path);
    }
}
