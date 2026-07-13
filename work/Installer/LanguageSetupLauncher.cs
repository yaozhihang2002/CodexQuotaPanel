using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("CodexQuotaPanel Setup")]
[assembly: AssemblyProduct("CodexQuotaPanel")]
[assembly: AssemblyDescription("Bilingual setup launcher for CodexQuotaPanel")]
[assembly: AssemblyCompany("CodexQuotaPanel")]
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]

namespace CodexQuotaPanelSetup
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length == 2 && string.Equals(args[0], "--preview", StringComparison.OrdinalIgnoreCase))
            {
                SavePreview(args[1]);
                return;
            }

            Application.Run(new LanguageForm());
        }

        private static void SavePreview(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using (LanguageForm form = new LanguageForm())
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(80, 80);
                form.Show();
                Application.DoEvents();
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                form.Hide();
            }
        }
    }

    internal sealed class LanguageForm : Form
    {
        private const string ProductCode = "{9F782366-0DFA-4DE4-881B-5FA92F0BCF6C}";
        private const string ChineseMsiResource = "CodexQuotaPanel.Installer.zh-cn.msi";
        private const string EnglishTransformResource = "CodexQuotaPanel.Installer.en-us.mst";
        private static readonly Color Background = Color.FromArgb(18, 23, 21);
        private static readonly Color Surface = Color.FromArgb(27, 34, 31);
        private static readonly Color Border = Color.FromArgb(50, 62, 57);
        private static readonly Color TextPrimary = Color.FromArgb(244, 241, 231);
        private static readonly Color TextMuted = Color.FromArgb(163, 176, 169);
        private static readonly Color Accent = Color.FromArgb(106, 228, 176);
        private static readonly Font UiFont = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular);

        private readonly RadioButton _chinese;
        private readonly RadioButton _english;
        private readonly Button _continueButton;
        private readonly Label _status;

        public LanguageForm()
        {
            Text = "Codex 额度面板安装 / Setup";
            ClientSize = new Size(458, 290);
            MinimumSize = MaximumSize = new Size(474, 329);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = UiFont;
            ShowIcon = true;

            Panel accentBar = new Panel
            {
                BackColor = Accent,
                Location = new Point(0, 0),
                Size = new Size(5, ClientSize.Height),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
            };
            Controls.Add(accentBar);

            Label eyebrow = new Label
            {
                AutoSize = true,
                Text = "CODEX · V0.2.0 PRE-RELEASE",
                ForeColor = Accent,
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
                Location = new Point(32, 24)
            };
            Controls.Add(eyebrow);

            Label title = new Label
            {
                AutoSize = true,
                Text = "选择安装语言",
                ForeColor = TextPrimary,
                Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold),
                Location = new Point(28, 49)
            };
            Controls.Add(title);

            Label subtitle = new Label
            {
                AutoSize = true,
                Text = "Choose the language used by Setup and the app on first launch.",
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(31, 88)
            };
            Controls.Add(subtitle);

            RoundedPanel choices = new RoundedPanel
            {
                BackColor = Surface,
                BorderColor = Border,
                CornerRadius = 12,
                Location = new Point(31, 119),
                Size = new Size(396, 82)
            };
            Controls.Add(choices);

            _chinese = MakeChoice("简体中文（推荐）", new Point(18, 14));
            _english = MakeChoice("English", new Point(212, 14));
            _chinese.Checked = true;
            choices.Controls.Add(_chinese);
            choices.Controls.Add(_english);

            Label hint = new Label
            {
                AutoSize = true,
                Text = "安装后仍可在设置中切换 / You can change this later in Settings",
                ForeColor = TextMuted,
                Font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Regular),
                Location = new Point(18, 50)
            };
            choices.Controls.Add(hint);

            _status = new Label
            {
                AutoSize = false,
                Text = "默认：简体中文",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextMuted,
                Location = new Point(31, 219),
                Size = new Size(245, 40)
            };
            Controls.Add(_status);

            _continueButton = new Button
            {
                Text = "继续安装",
                BackColor = Accent,
                ForeColor = Color.FromArgb(15, 33, 26),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                Location = new Point(296, 221),
                Size = new Size(131, 38),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            _continueButton.FlatAppearance.BorderSize = 0;
            _continueButton.Click += ContinueInstallation;
            Controls.Add(_continueButton);
            AcceptButton = _continueButton;

            _chinese.CheckedChanged += LanguageChanged;
            _english.CheckedChanged += LanguageChanged;
        }

        private static RadioButton MakeChoice(string text, Point location)
        {
            return new RadioButton
            {
                AutoSize = true,
                Text = text,
                ForeColor = TextPrimary,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
                Location = location,
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
        }

        private void LanguageChanged(object sender, EventArgs e)
        {
            if (_english.Checked)
            {
                _continueButton.Text = "Continue";
                _status.Text = "Selected: English";
            }
            else
            {
                _continueButton.Text = "继续安装";
                _status.Text = "默认：简体中文";
            }
        }

        private void ContinueInstallation(object sender, EventArgs e)
        {
            bool english = _english.Checked;
            bool reinstall = IsProductInstalled();
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "CodexQuotaPanelSetup-" + Guid.NewGuid().ToString("N"));

            _continueButton.Enabled = false;
            _status.Text = reinstall
                ? (english ? "Updating the installed version…" : "正在覆盖已安装版本…")
                : (english ? "Starting Setup…" : "正在启动安装程序…");
            Cursor = Cursors.WaitCursor;

            try
            {
                Directory.CreateDirectory(temporaryDirectory);
                string msiPath = Path.Combine(temporaryDirectory, "CodexQuotaPanel.msi");
                string transformPath = Path.Combine(temporaryDirectory, "en-us.mst");
                ExtractResource(ChineseMsiResource, msiPath);
                ExtractResource(EnglishTransformResource, transformPath);

                string arguments = "/i " + Quote(msiPath);
                if (english) arguments += " TRANSFORMS=" + Quote(transformPath);
                if (reinstall) arguments += " REINSTALL=ALL REINSTALLMODE=amusv";

                Hide();
                using (Process process = Process.Start(new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = arguments,
                    UseShellExecute = true,
                    WorkingDirectory = temporaryDirectory
                }))
                {
                    if (process == null) throw new InvalidOperationException("Unable to start Windows Installer.");
                    process.WaitForExit();
                    int exitCode = process.ExitCode;
                    if (exitCode == 0 || exitCode == 1641 || exitCode == 3010)
                    {
                        SetInitialLanguageIfAbsent(english ? 1 : 0);
                        Close();
                        return;
                    }

                    Show();
                    Activate();
                    if (exitCode == 1602)
                    {
                        _status.Text = english ? "Installation cancelled" : "安装已取消";
                    }
                    else
                    {
                        _status.Text = (english ? "Setup ended with code " : "安装程序返回代码 ") + exitCode;
                    }
                }
            }
            catch (Exception ex)
            {
                Show();
                Activate();
                MessageBox.Show(
                    this,
                    (_english.Checked ? "Setup could not start.\n\n" : "无法启动安装程序。\n\n") + ex.Message,
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                _status.Text = _english.Checked ? "Please try again" : "请重试";
            }
            finally
            {
                Cursor = Cursors.Default;
                _continueButton.Enabled = true;
                TryDeleteDirectory(temporaryDirectory);
            }
        }

        private static void ExtractResource(string resourceName, string destination)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream input = assembly.GetManifestResourceStream(resourceName))
            {
                if (input == null) throw new InvalidOperationException("Missing installer resource: " + resourceName);
                using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                }
            }
        }

        private static void SetInitialLanguageIfAbsent(int language)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\CodexQuotaPanel"))
            {
                if (key != null && key.GetValue("Language") == null)
                {
                    key.SetValue("Language", language, RegistryValueKind.DWord);
                }
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static bool IsProductInstalled()
        {
            int state = MsiQueryProductState(ProductCode);
            return state == 1 || state == 3 || state == 4 || state == 5;
        }

        [System.Runtime.InteropServices.DllImport("msi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int MsiQueryProductState(string productCode);

        private static void TryDeleteDirectory(string path)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                    return;
                }
                catch (IOException) { Thread.Sleep(120); }
                catch (UnauthorizedAccessException) { Thread.Sleep(120); }
            }
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        public Color BorderColor { get; set; }
        public int CornerRadius { get; set; }

        public RoundedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedRectangle(bounds, CornerRadius))
            using (SolidBrush fill = new SolidBrush(BackColor))
            using (Pen border = new Pen(BorderColor))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = Math.Max(2, radius * 2);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
