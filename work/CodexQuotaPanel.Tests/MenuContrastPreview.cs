using CodexQuotaPanel;

internal static class MenuContrastPreview
{
    internal static void Run(string outputPath)
    {
        UiPalette.SetTheme(1);
        using var menu = new ContextMenuStrip
        {
            BackColor = UiPalette.Surface,
            ForeColor = UiPalette.Text,
            Font = UiPalette.Body(9f),
            Renderer = new AppToolStripRenderer(),
            ShowImageMargin = false,
            ShowCheckMargin = true
        };
        menu.Items.Add("展开额度详情");
        menu.Items.Add("隐藏悬浮球（仅托盘）");
        menu.Items.Add(new ToolStripMenuItem("立即刷新") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("悬浮球鼠标穿透") { Checked = true });
        menu.Items.Add("设置…");
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("官方额度说明");
        menu.Items.Add("退出");
        menu.CreateControl();
        menu.PerformLayout();
        var size = menu.GetPreferredSize(Size.Empty);
        menu.Size = size;
        using var bitmap = new Bitmap(size.Width, size.Height);
        menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"PASS readable dark tray menu | {fullPath}");
    }
}
