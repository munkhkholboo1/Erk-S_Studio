using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Erk-S Studio Setup")]
[assembly: AssemblyDescription("Erk-S Studio per-user installer")]
[assembly: AssemblyCompany("Erk-S")]
[assembly: AssemblyProduct("Erk-S Studio")]
[assembly: AssemblyCopyright("Copyright (c) Erk-S")]
[assembly: AssemblyVersion("0.0.1.0")]
[assembly: AssemblyFileVersion("0.0.1.0")]
[assembly: AssemblyInformationalVersion("Demo V0.001")]

namespace ErkS.Studio.Setup
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            InstallerOptions options = InstallerOptions.Parse(args);
            if (options.Quiet)
            {
                try
                {
                    InstallerEngine.Install(options, null);
                    return 0;
                }
                catch (Exception exception)
                {
                    WriteErrorLog(exception);
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (SetupForm form = new SetupForm(options))
            {
                Application.Run(form);
                return form.ExitCode;
            }
        }

        private static void WriteErrorLog(Exception exception)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "ErkS-Studio-Setup.log");
                File.AppendAllText(path, DateTimeOffset.Now.ToString("O") + Environment.NewLine + exception + Environment.NewLine);
            }
            catch
            {
            }
        }
    }

    internal sealed class InstallerOptions
    {
        public string InstallRoot { get; private set; }
        public bool Quiet { get; private set; }
        public bool NoLaunch { get; set; }
        public bool SkipShortcuts { get; private set; }
        public bool SkipRegistration { get; private set; }
        public bool UpdateHandoff { get; private set; }
        public int WaitForProcessId { get; private set; }

        public static InstallerOptions Parse(IEnumerable<string> args)
        {
            string defaultRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Erk-S Studio");
            InstallerOptions options = new InstallerOptions
            {
                InstallRoot = ReadEnvironment("ERKS_STUDIO_INSTALL_ROOT", defaultRoot),
                NoLaunch = ReadFlag("ERKS_STUDIO_NO_LAUNCH"),
                SkipShortcuts = ReadFlag("ERKS_STUDIO_SKIP_SHORTCUTS"),
                SkipRegistration = ReadFlag("ERKS_STUDIO_SKIP_REGISTRATION")
            };

            foreach (string rawArgument in args ?? Enumerable.Empty<string>())
            {
                string argument = (rawArgument ?? string.Empty).Trim();
                if (EqualsOption(argument, "/quiet", "--quiet", "/q"))
                    options.Quiet = true;
                else if (EqualsOption(argument, "/update", "--update"))
                    options.UpdateHandoff = true;
                else if (EqualsOption(argument, "/nolaunch", "--no-launch"))
                    options.NoLaunch = true;
                else if (EqualsOption(argument, "/skipshortcuts", "--skip-shortcuts"))
                    options.SkipShortcuts = true;
                else if (EqualsOption(argument, "/skipregistration", "--skip-registration"))
                    options.SkipRegistration = true;
                else if (argument.StartsWith("/installroot=", StringComparison.OrdinalIgnoreCase))
                    options.InstallRoot = argument.Substring("/installroot=".Length).Trim('"');
                else if (argument.StartsWith("--install-root=", StringComparison.OrdinalIgnoreCase))
                    options.InstallRoot = argument.Substring("--install-root=".Length).Trim('"');
                else if (argument.StartsWith("/waitforpid=", StringComparison.OrdinalIgnoreCase))
                    options.WaitForProcessId = ParseProcessId(argument.Substring("/waitforpid=".Length));
                else if (argument.StartsWith("--wait-for-pid=", StringComparison.OrdinalIgnoreCase))
                    options.WaitForProcessId = ParseProcessId(argument.Substring("--wait-for-pid=".Length));
            }

            options.InstallRoot = Path.GetFullPath(options.InstallRoot);
            return options;
        }

        private static string ReadEnvironment(string name, string fallback)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static bool ReadFlag(string name)
        {
            return string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);
        }

        private static bool EqualsOption(string value, params string[] options)
        {
            return options.Any(option => string.Equals(value, option, StringComparison.OrdinalIgnoreCase));
        }

        private static int ParseProcessId(string value)
        {
            int processId;
            if (!int.TryParse((value ?? string.Empty).Trim().Trim('"'), out processId) || processId <= 0)
                throw new ArgumentException("Update handoff process id is invalid.");
            return processId;
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly InstallerOptions options;
        private readonly Label statusLabel;
        private readonly ProgressBar progressBar;
        private readonly Button installButton;
        private readonly Button cancelButton;
        private readonly CheckBox launchCheckBox;

        public SetupForm(InstallerOptions options)
        {
            this.options = options;
            ExitCode = 1;

            Text = "Erk-S Studio Setup";
            ClientSize = new Size(540, 390);
            BackColor = Color.FromArgb(18, 21, 27);
            ForeColor = Color.FromArgb(239, 243, 248);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowIcon = true;

            Icon applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (applicationIcon != null)
                Icon = applicationIcon;

            Panel brandBand = new Panel
            {
                Dock = DockStyle.Top,
                Height = 8,
                BackColor = Color.FromArgb(46, 144, 250)
            };
            Controls.Add(brandBand);

            PictureBox logo = new PictureBox
            {
                Location = new Point(36, 40),
                Size = new Size(52, 52),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = applicationIcon == null ? null : applicationIcon.ToBitmap()
            };
            Controls.Add(logo);

            Label title = new Label
            {
                AutoSize = true,
                Location = new Point(104, 40),
                Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold, GraphicsUnit.Point),
                Text = "Erk-S Studio"
            };
            Controls.Add(title);

            Label version = new Label
            {
                AutoSize = true,
                Location = new Point(107, 78),
                ForeColor = Color.FromArgb(164, 175, 190),
                Text = "Demo V0.001"
            };
            Controls.Add(version);

            Label freeBadge = new Label
            {
                AutoSize = false,
                Location = new Point(425, 45),
                Size = new Size(76, 28),
                BackColor = Color.FromArgb(24, 99, 68),
                ForeColor = Color.FromArgb(219, 255, 237),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Text = "FREE"
            };
            Controls.Add(freeBadge);

            Label locationCaption = new Label
            {
                AutoSize = true,
                Location = new Point(36, 137),
                ForeColor = Color.FromArgb(164, 175, 190),
                Text = "Install location"
            };
            Controls.Add(locationCaption);

            TextBox location = new TextBox
            {
                Location = new Point(36, 161),
                Size = new Size(465, 28),
                ReadOnly = true,
                BackColor = Color.FromArgb(27, 32, 40),
                ForeColor = Color.FromArgb(223, 230, 239),
                BorderStyle = BorderStyle.FixedSingle,
                Text = options.InstallRoot
            };
            Controls.Add(location);

            launchCheckBox = new CheckBox
            {
                AutoSize = true,
                Location = new Point(36, 214),
                Checked = !options.NoLaunch,
                Text = "Launch Erk-S Studio after setup"
            };
            Controls.Add(launchCheckBox);

            progressBar = new ProgressBar
            {
                Location = new Point(36, 258),
                Size = new Size(465, 6),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 0
            };
            Controls.Add(progressBar);

            statusLabel = new Label
            {
                AutoEllipsis = true,
                Location = new Point(36, 277),
                Size = new Size(465, 28),
                ForeColor = Color.FromArgb(164, 175, 190),
                Text = "Ready to install"
            };
            Controls.Add(statusLabel);

            cancelButton = new Button
            {
                Location = new Point(310, 326),
                Size = new Size(90, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(35, 41, 51),
                ForeColor = Color.FromArgb(226, 232, 240),
                Text = "Cancel"
            };
            cancelButton.FlatAppearance.BorderColor = Color.FromArgb(68, 78, 92);
            cancelButton.Click += delegate { Close(); };
            Controls.Add(cancelButton);

            installButton = new Button
            {
                Location = new Point(410, 326),
                Size = new Size(91, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(46, 144, 250),
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Text = "Install"
            };
            installButton.FlatAppearance.BorderSize = 0;
            installButton.Click += InstallButtonOnClick;
            Controls.Add(installButton);

            AcceptButton = installButton;
            CancelButton = cancelButton;

            if (options.UpdateHandoff)
            {
                Text = "Erk-S Studio Update";
                installButton.Text = "Updating...";
                Shown += delegate
                {
                    BeginInvoke((MethodInvoker)delegate { InstallButtonOnClick(this, EventArgs.Empty); });
                };
            }
        }

        public int ExitCode { get; private set; }

        private async void InstallButtonOnClick(object sender, EventArgs eventArgs)
        {
            installButton.Enabled = false;
            cancelButton.Enabled = false;
            launchCheckBox.Enabled = false;
            progressBar.MarqueeAnimationSpeed = 22;
            statusLabel.Text = "Installing...";
            options.NoLaunch = !launchCheckBox.Checked;

            try
            {
                IProgress<string> progress = new Progress<string>(message => statusLabel.Text = message);
                await Task.Run(() => InstallerEngine.Install(options, message => progress.Report(message)));
                ExitCode = 0;
                progressBar.MarqueeAnimationSpeed = 0;
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 100;
                statusLabel.ForeColor = Color.FromArgb(94, 211, 145);
                statusLabel.Text = "Erk-S Studio is ready.";
                if (options.UpdateHandoff)
                {
                    Close();
                    return;
                }
                installButton.Text = "Close";
                installButton.Enabled = true;
                installButton.Click -= InstallButtonOnClick;
                installButton.Click += delegate { Close(); };
                cancelButton.Visible = false;
            }
            catch (Exception exception)
            {
                progressBar.MarqueeAnimationSpeed = 0;
                statusLabel.ForeColor = Color.FromArgb(255, 132, 132);
                statusLabel.Text = "Setup could not finish.";
                MessageBox.Show(this, exception.Message, "Erk-S Studio Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                installButton.Enabled = true;
                cancelButton.Enabled = true;
                launchCheckBox.Enabled = true;
            }
        }
    }

    internal static class InstallerEngine
    {
        private const string PayloadResourceName = "ErkS.Studio.Payload";
        private const string ProductName = "Erk-S Studio";
        private const string DisplayVersion = "Demo V0.001";

        public static void Install(InstallerOptions options, Action<string> report)
        {
            ValidateInstallRoot(options.InstallRoot);
            string workRoot = Path.Combine(Path.GetTempPath(), "ErkS-Studio-Install-" + Guid.NewGuid().ToString("N"));
            string payloadRoot = Path.Combine(workRoot, "payload");

            try
            {
                Report(report, "Preparing files...");
                Directory.CreateDirectory(payloadRoot);
                ExtractPayload(payloadRoot);
                ValidatePayload(payloadRoot);

                Report(report, "Waiting for Erk-S Studio to close...");
                WaitForHandoffProcess(options.WaitForProcessId, TimeSpan.FromMinutes(2));
                WaitForInstalledApplication(options.InstallRoot, TimeSpan.FromSeconds(60));

                Report(report, "Installing Erk-S Studio...");
                Directory.CreateDirectory(options.InstallRoot);
                RemoveLegacyDirectory(options.InstallRoot);
                CopyDirectory(payloadRoot, options.InstallRoot);
                RemoveDevelopmentMarkers(options.InstallRoot);

                string installedExe = Path.Combine(options.InstallRoot, "ErkS.Studio.exe");
                if (!File.Exists(installedExe))
                    throw new InvalidOperationException("Erk-S Studio installation could not be verified.");

                if (!options.SkipShortcuts)
                    CreateShortcuts(installedExe, options.InstallRoot);
                if (!options.SkipRegistration)
                    RegisterUninstaller(installedExe, options.InstallRoot);

                Report(report, "Finalizing...");
                if (!options.NoLaunch)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = installedExe,
                        WorkingDirectory = options.InstallRoot,
                        UseShellExecute = true
                    });
                }
            }
            finally
            {
                TryDeleteDirectory(workRoot);
            }
        }

        private static void ExtractPayload(string payloadRoot)
        {
            Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName);
            if (resource == null)
                throw new InvalidOperationException("Installer payload is missing.");

            string normalizedRoot = Path.GetFullPath(payloadRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (resource)
            using (ZipArchive archive = new ZipArchive(resource, ZipArchiveMode.Read, false))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    string targetPath = Path.GetFullPath(Path.Combine(payloadRoot, relativePath));
                    if (!targetPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Installer payload contains an invalid path.");

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(targetPath);
                        continue;
                    }

                    string parent = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        input.CopyTo(output);
                }
            }
        }

        private static void ValidatePayload(string payloadRoot)
        {
            if (!File.Exists(Path.Combine(payloadRoot, "ErkS.Studio.exe")))
                throw new InvalidDataException("ErkS.Studio.exe is missing from the installer payload.");

            HashSet<string> forbiddenExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".erksproject", ".erksalbum", ".rvt", ".dwg"
            };
            foreach (string file in Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".devroot", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".static", StringComparison.OrdinalIgnoreCase) ||
                    forbiddenExtensions.Contains(Path.GetExtension(file)))
                    throw new InvalidDataException("Installer payload contains development or project data.");
            }
        }

        private static void WaitForInstalledApplication(string installRoot, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline)
            {
                Process[] running = FindInstalledProcesses(installRoot).ToArray();
                if (running.Length == 0)
                    return;
                foreach (Process process in running)
                {
                    try { process.WaitForExit(800); } catch { }
                    process.Dispose();
                }
                Thread.Sleep(200);
            }

            if (FindInstalledProcesses(installRoot).Any())
                throw new InvalidOperationException("Close Erk-S Studio and run setup again.");
        }

        private static void WaitForHandoffProcess(int processId, TimeSpan timeout)
        {
            if (processId <= 0)
                return;

            Process process;
            try
            {
                process = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return;
            }

            using (process)
            {
                if (process.HasExited)
                    return;
                if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                    throw new InvalidOperationException("Erk-S Studio did not close automatically. Restart Windows and try the update again.");
            }
        }

        private static IEnumerable<Process> FindInstalledProcesses(string installRoot)
        {
            string prefix = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            List<Process> result = new List<Process>();
            foreach (Process process in Process.GetProcessesByName("ErkS.Studio"))
            {
                bool isInstalledProcess = false;
                try
                {
                    string processPath = process.MainModule == null ? null : process.MainModule.FileName;
                    isInstalledProcess = !string.IsNullOrEmpty(processPath) &&
                        processPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                }

                if (isInstalledProcess)
                    result.Add(process);
                else
                    process.Dispose();
            }
            return result;
        }

        private static void RemoveLegacyDirectory(string installRoot)
        {
            string legacy = Path.GetFullPath(Path.Combine(installRoot, "app"));
            string expected = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "app";
            if (legacy.Equals(expected, StringComparison.OrdinalIgnoreCase) && Directory.Exists(legacy))
                Directory.Delete(legacy, true);
        }

        private static void CopyDirectory(string sourceRoot, string destinationRoot)
        {
            foreach (string directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string relative = directory.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
            }

            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar);
                string destination = Path.Combine(destinationRoot, relative);
                string parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                File.Copy(file, destination, true);
            }
        }

        private static void RemoveDevelopmentMarkers(string installRoot)
        {
            foreach (string marker in new[] { "ErkS.Studio.devroot", "ErkS.Studio.static" })
            {
                string path = Path.Combine(installRoot, marker);
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static void CreateShortcuts(string installedExe, string installRoot)
        {
            string startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs",
                ProductName);
            Directory.CreateDirectory(startMenu);
            CreateShortcut(Path.Combine(startMenu, ProductName + ".lnk"), installedExe, installRoot);

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            CreateShortcut(Path.Combine(desktop, ProductName + ".lnk"), installedExe, installRoot);
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return;

            object shellObject = Activator.CreateInstance(shellType);
            try
            {
                dynamic shell = shellObject;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = workingDirectory;
                shortcut.IconLocation = targetPath + ",0";
                shortcut.Save();
                Marshal.FinalReleaseComObject(shortcut);
            }
            finally
            {
                if (shellObject != null && Marshal.IsComObject(shellObject))
                    Marshal.FinalReleaseComObject(shellObject);
            }
        }

        private static void RegisterUninstaller(string installedExe, string installRoot)
        {
            string uninstallScript = Path.Combine(installRoot, "Uninstall-ErkS-Studio.ps1");
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Erk-S Studio"))
            {
                if (key == null)
                    return;
                key.SetValue("DisplayName", ProductName, RegistryValueKind.String);
                key.SetValue("DisplayVersion", DisplayVersion, RegistryValueKind.String);
                key.SetValue("Publisher", "Erk-S", RegistryValueKind.String);
                key.SetValue("InstallLocation", installRoot, RegistryValueKind.String);
                key.SetValue("DisplayIcon", installedExe, RegistryValueKind.String);
                key.SetValue("URLInfoAbout", "https://erk-s.mn/products/studio", RegistryValueKind.String);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue(
                    "UninstallString",
                    "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + uninstallScript + "\"",
                    RegistryValueKind.String);
            }
        }

        private static void ValidateInstallRoot(string installRoot)
        {
            if (string.IsNullOrWhiteSpace(installRoot))
                throw new ArgumentException("Install location is required.");
            string fullPath = Path.GetFullPath(installRoot);
            string root = Path.GetPathRoot(fullPath);
            if (string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar), (root ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The drive root cannot be used as the install location.");
        }

        private static void Report(Action<string> report, string message)
        {
            if (report != null)
                report(message);
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
            }
        }
    }
}
