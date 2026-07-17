using CodexQuotaPanel;

internal static class ReminderPromptPreview
{
    internal static void Run(string outputPath)
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        var clickThrough = Render(
            themeMode: 1,
            "鼠标穿透已开启",
            "悬浮球现在不会响应点击或拖动。可从托盘菜单关闭穿透，也可按 Ctrl+Alt+Q 快速找回。",
            "不再提醒");
        var quotaAlert = Render(
            themeMode: 2,
            "Codex 额度提醒",
            "5 小时窗口还剩 20%，预计 7月17日 18:20 重置。",
            "本额度周期不再提醒");

        using (clickThrough)
        using (quotaAlert)
        using (var composite = new Bitmap(clickThrough.Width + quotaAlert.Width + 24,
                   Math.Max(clickThrough.Height, quotaAlert.Height)))
        using (var graphics = Graphics.FromImage(composite))
        {
            graphics.Clear(Color.FromArgb(225, 229, 227));
            graphics.DrawImageUnscaled(clickThrough, 0, 0);
            graphics.DrawImageUnscaled(quotaAlert, clickThrough.Width + 24, 0);
            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            composite.Save(fullPath);
            Console.WriteLine($"PASS reminder preview | click-through opt-out + per-cycle quota mute | {fullPath}");
        }
    }

    private static Bitmap Render(int themeMode, string title, string message, string suppressText)
    {
        UiPalette.SetTheme(themeMode);
        using var prompt = new ReminderPromptForm(
            title,
            message,
            suppressText,
            "知道了",
            110,
            topMost: true);
        prompt.Show();
        Application.DoEvents();
        var bitmap = new Bitmap(prompt.ClientSize.Width, prompt.ClientSize.Height);
        prompt.DrawToBitmap(bitmap, prompt.ClientRectangle);
        prompt.Hide();
        return bitmap;
    }
}
