using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("CodexQuotaPanel Setup")]
[assembly: AssemblyProduct("CodexQuotaPanel")]
[assembly: AssemblyDescription("Bilingual setup launcher for CodexQuotaPanel")]
[assembly: AssemblyCompany("CodexQuotaPanel")]
[assembly: AssemblyVersion("0.5.2.0")]
[assembly: AssemblyFileVersion("0.5.2.0")]

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
        // Replaced with the ProductCode read from the embedded MSI by
        // Build-LanguageSetupLauncher.ps1. This prevents a stale launcher
        // from treating a major upgrade as a same-version repair.
        private const string ProductCode = "__MSI_PRODUCT_CODE__";
        private const string ChineseMsiResource = "CodexQuotaPanel.Installer.zh-cn.msi";
        private const string EnglishTransformResource = "CodexQuotaPanel.Installer.en-us.mst";
        private static readonly bool RequiresDesktopRuntime = __REQUIRES_DESKTOP_RUNTIME__;
        private static readonly string RuntimeDownloadUrl = "__RUNTIME_DOWNLOAD_URL__";
        private static readonly string RuntimeSha512 = "__RUNTIME_SHA512__";
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
                Text = RequiresDesktopRuntime
                    ? "CODEX · V0.5.2 WEB SETUP"
                    : "CODEX · V0.5.2 OFFLINE SETUP",
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
                Text = RequiresDesktopRuntime
                    ? "Small installer · downloads Microsoft .NET only when required."
                    : "Complete offline installer · no separate runtime download required.",
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
                EnsureDesktopRuntime(temporaryDirectory, english);
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
                    (_english.Checked ? "Setup could not continue.\n\n" : "安装无法继续。\n\n") + ex.Message,
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

        private void EnsureDesktopRuntime(string temporaryDirectory, bool english)
        {
            if (!RequiresDesktopRuntime || HasDesktopRuntime9()) return;

            string runtimeInstaller = Path.Combine(
                temporaryDirectory,
                "windowsdesktop-runtime-9-win-x64.exe");
            _status.Text = english
                ? "Downloading Microsoft .NET Desktop Runtime…"
                : "正在下载 Microsoft .NET 桌面运行库…";
            Application.DoEvents();

            Exception downloadError = null;
            using (WebClient client = new WebClient())
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                client.Headers.Add(HttpRequestHeader.UserAgent, "CodexQuotaPanel-Setup/0.5.2");
                client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs args)
                {
                    _status.Text = english
                        ? "Downloading Microsoft .NET… " + args.ProgressPercentage + "%"
                        : "正在下载 Microsoft .NET… " + args.ProgressPercentage + "%";
                };
                client.DownloadFileCompleted += delegate(object sender, System.ComponentModel.AsyncCompletedEventArgs args)
                {
                    if (args.Error != null) downloadError = args.Error;
                    else if (args.Cancelled) downloadError = new OperationCanceledException("Runtime download was cancelled.");
                };
                client.DownloadFileAsync(new Uri(RuntimeDownloadUrl), runtimeInstaller);
                while (client.IsBusy)
                {
                    Application.DoEvents();
                    Thread.Sleep(25);
                }
            }

            if (downloadError != null)
            {
                throw new InvalidOperationException(
                    english
                        ? "The Microsoft .NET runtime could not be downloaded. Use the Offline Setup when the network is unavailable."
                        : "无法下载 Microsoft .NET 运行库。网络不可用时请改用完整离线安装包。",
                    downloadError);
            }

            VerifySha512(runtimeInstaller, RuntimeSha512);
            _status.Text = english
                ? "Installing Microsoft .NET Desktop Runtime…"
                : "正在安装 Microsoft .NET 桌面运行库…";
            Application.DoEvents();

            using (Process process = Process.Start(new ProcessStartInfo
            {
                FileName = runtimeInstaller,
                Arguments = "/install /passive /norestart",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = temporaryDirectory
            }))
            {
                if (process == null) throw new InvalidOperationException("Unable to start the Microsoft .NET installer.");
                process.WaitForExit();
                if (process.ExitCode != 0 && process.ExitCode != 1641 && process.ExitCode != 3010)
                {
                    throw new InvalidOperationException("Microsoft .NET installer returned code " + process.ExitCode + ".");
                }
            }

            if (!HasDesktopRuntime9())
            {
                throw new InvalidOperationException(
                    english
                        ? "Microsoft .NET Desktop Runtime 9 was not detected after installation."
                        : "安装结束后仍未检测到 Microsoft .NET 9 桌面运行库。");
            }
        }

        private static bool HasDesktopRuntime9()
        {
            string registryLocation = null;
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = baseKey.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64"))
                {
                    if (key != null) registryLocation = key.GetValue("InstallLocation") as string;
                }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }

            string[] roots =
            {
                Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"),
                registryLocation,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet")
            };
            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                string sharedFramework = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
                if (!Directory.Exists(sharedFramework)) continue;
                try
                {
                    if (Directory.GetDirectories(sharedFramework, "9.*").Length > 0) return true;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return false;
        }

        private static void VerifySha512(string path, string expectedHash)
        {
            byte[] digest;
            using (SHA512 sha = SHA512.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                digest = sha.ComputeHash(stream);
            }
            string actualHash = BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Microsoft .NET runtime SHA-512 verification failed.");
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
