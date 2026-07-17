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

        ApplicationRestart.WaitForPreviousInstance(args);

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
        var recovery = CrashRecoverySession.Begin();

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
            using (var context = new QuotaApplicationContext(showSignal, preferences))
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

    private static void ShowFatalError()
    {
        try
        {
            MessageBox.Show(
                L10n.Pick(
                    "程序遇到异常并将关闭。诊断记录不会包含账号、路径或会话内容；下次启动将照常恢复已保存的设置。",
                    "The application encountered an error and will close. The diagnostic record contains no account, path, or session content; the next launch will restore your saved settings normally."),
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
