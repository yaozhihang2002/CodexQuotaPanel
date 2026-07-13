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

        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        using var context = new QuotaApplicationContext(showSignal);
        Application.Run(context);
        GC.KeepAlive(mutex);
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
