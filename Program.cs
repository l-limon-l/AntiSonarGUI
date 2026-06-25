using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using Microsoft.Win32;
using System.IO;
using System.Net;
using System.IO.Compression;

namespace AntiSonar
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

            bool startHidden = false;
            if (args.Length > 0 && args[0] == "-hidden")
                startHidden = true;

            Application.Run(new MainForm(startHidden));
        }
    }

    public class MainForm : Form
    {
        private NotifyIcon trayIcon;
        private ContextMenu trayMenu;
        private Timer workTimer;
        
        private CheckBox chkGaming;
        private CheckBox chkChat;
        private CheckBox chkMedia;
        private CheckBox chkAux;
        private CheckBox chkMic;
        private CheckBox chkAutorun;

        private string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AntiSonar");
        private string svvPath;
        private string regPath = @"Software\AntiSonar";
        private bool isHiddenRun;
        private bool hasEverSuccessfullyKilled = false;
        private int consecutiveFailuresAfterSuccess = 0;
        private string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public MainForm(bool startHidden)
        {
            isHiddenRun = startHidden;
            svvPath = Path.Combine(appDataPath, "SoundVolumeView.exe");
            EnsureDependencies();
            // Setup Form
            this.Text = "AntiSonar (Custom Edition)";
            this.Size = new Size(300, 280);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = SystemIcons.Shield; // default icon

            // Tray Icon
            trayMenu = new ContextMenu();
            trayMenu.MenuItems.Add("Show", OnShow);
            trayMenu.MenuItems.Add("Exit", OnExit);
            
            trayIcon = new NotifyIcon();
            trayIcon.Text = "AntiSonar is running";
            trayIcon.Icon = SystemIcons.Shield;
            trayIcon.ContextMenu = trayMenu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += OnShow;

            // UI Elements
            Label lblTitle = new Label() { Text = "Select Sonar devices to auto-disable:", Location = new Point(15, 15), AutoSize = true, Font = new Font(this.Font, FontStyle.Bold) };
            this.Controls.Add(lblTitle);

            chkGaming = new CheckBox() { Text = "SteelSeries Sonar - Gaming", Location = new Point(20, 40), AutoSize = true };
            chkChat = new CheckBox() { Text = "SteelSeries Sonar - Chat", Location = new Point(20, 65), AutoSize = true };
            chkMedia = new CheckBox() { Text = "SteelSeries Sonar - Media", Location = new Point(20, 90), AutoSize = true };
            chkAux = new CheckBox() { Text = "SteelSeries Sonar - Aux", Location = new Point(20, 115), AutoSize = true };
            chkMic = new CheckBox() { Text = "SteelSeries Sonar - Microphone", Location = new Point(20, 140), AutoSize = true };
            
            chkAutorun = new CheckBox() { Text = "Start with Windows (Hidden)", Location = new Point(20, 180), AutoSize = true, ForeColor = Color.Blue };
            
            this.Controls.Add(lblTitle);
            this.Controls.Add(chkGaming);
            this.Controls.Add(chkChat);
            this.Controls.Add(chkMedia);
            this.Controls.Add(chkAux);
            this.Controls.Add(chkMic);
            this.Controls.Add(chkAutorun);

            LoadSettings();

            // Events
            chkGaming.CheckedChanged += SaveSettings;
            chkChat.CheckedChanged += SaveSettings;
            chkMedia.CheckedChanged += SaveSettings;
            chkAux.CheckedChanged += SaveSettings;
            chkMic.CheckedChanged += SaveSettings;
            chkAutorun.CheckedChanged += OnAutorunChanged;

            this.FormClosing += OnFormClosing;

            // Background Timer (every 10 seconds)
            workTimer = new Timer();
            workTimer.Interval = 10000;
            workTimer.Tick += OnTick;
            workTimer.Start();
            
            // Initial run immediately
            OnTick(null, null);

            if (startHidden)
            {
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
                trayIcon.Visible = false;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
            }
        }

        private void EnsureDependencies()
        {
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            if (!File.Exists(svvPath))
            {
                string zipPath = Path.Combine(appDataPath, "svv.zip");
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                    using (WebClient client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                        client.DownloadFile("https://www.nirsoft.net/utils/soundvolumeview-x64.zip", zipPath);
                    }
                    if (File.Exists(zipPath))
                    {
                        ZipFile.ExtractToDirectory(zipPath, appDataPath);
                        File.Delete(zipPath);
                    }
                }
                catch { }
            }
        }

        private void LoadSettings()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(regPath))
            {
                chkGaming.Checked = Convert.ToInt32(key.GetValue("KillGaming", 1)) == 1;
                chkChat.Checked = Convert.ToInt32(key.GetValue("KillChat", 1)) == 1;
                chkMedia.Checked = Convert.ToInt32(key.GetValue("KillMedia", 0)) == 1;
                chkAux.Checked = Convert.ToInt32(key.GetValue("KillAux", 0)) == 1;
                chkMic.Checked = Convert.ToInt32(key.GetValue("KillMic", 0)) == 1;
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks", "/query /tn AntiSonarGUI");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    chkAutorun.Checked = (p.ExitCode == 0);
                }
            }
            catch { }
        }

        private void SaveSettings(object sender, EventArgs e)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(regPath))
            {
                key.SetValue("KillGaming", chkGaming.Checked ? 1 : 0);
                key.SetValue("KillChat", chkChat.Checked ? 1 : 0);
                key.SetValue("KillMedia", chkMedia.Checked ? 1 : 0);
                key.SetValue("KillAux", chkAux.Checked ? 1 : 0);
                key.SetValue("KillMic", chkMic.Checked ? 1 : 0);
            }
        }

        private void OnAutorunChanged(object sender, EventArgs e)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "schtasks";
                if (chkAutorun.Checked)
                {
                    psi.Arguments = "/create /tn \"AntiSonarGUI\" /tr \"\\\"" + Application.ExecutablePath + "\\\" -hidden\" /sc onlogon /rl highest /f";
                }
                else
                {
                    psi.Arguments = "/delete /tn \"AntiSonarGUI\" /f";
                }
                psi.Verb = "runas"; 
                psi.UseShellExecute = true; 
                psi.WindowStyle = ProcessWindowStyle.Hidden;

                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                }
            }
            catch (Exception)
            {
                chkAutorun.CheckedChanged -= OnAutorunChanged;
                chkAutorun.Checked = !chkAutorun.Checked;
                chkAutorun.CheckedChanged += OnAutorunChanged;
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!File.Exists(svvPath)) return;

            bool killedThisTick = false;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(svvPath, "/scomma \"\"");
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                
                using (Process p = Process.Start(psi))
                {
                    string csv = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();

                    if (chkGaming.Checked && CheckAndDisable(csv, "SteelSeries Sonar - Gaming")) killedThisTick = true;
                    if (chkChat.Checked && CheckAndDisable(csv, "SteelSeries Sonar - Chat")) killedThisTick = true;
                    if (chkMedia.Checked && CheckAndDisable(csv, "SteelSeries Sonar - Media")) killedThisTick = true;
                    if (chkAux.Checked && CheckAndDisable(csv, "SteelSeries Sonar - Aux")) killedThisTick = true;
                    if (chkMic.Checked && CheckAndDisable(csv, "SteelSeries Sonar - Microphone")) killedThisTick = true;
                }
            }
            catch { }

            if (isHiddenRun)
            {
                if (killedThisTick)
                {
                    hasEverSuccessfullyKilled = true;
                    consecutiveFailuresAfterSuccess = 0;
                }
                else if (hasEverSuccessfullyKilled)
                {
                    consecutiveFailuresAfterSuccess++;
                    if (consecutiveFailuresAfterSuccess >= 5)
                    {
                        Application.Exit();
                        return;
                    }
                }
            }
        }

        private bool CheckAndDisable(string csv, string deviceName)
        {
            string searchStr = deviceName + ",Device";
            int idx = csv.IndexOf(searchStr);
            if (idx >= 0)
            {
                int newlineIdx = csv.IndexOf('\n', idx);
                string line = newlineIdx > 0 ? csv.Substring(idx, newlineIdx - idx) : csv.Substring(idx);
                
                if (line.Contains(",Active,") || line.Contains(",Inactive,"))
                {
                    DisableDevice(deviceName);
                    return true;
                }
            }
            return false;
        }

        private void DisableDevice(string deviceName)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.CreateNoWindow = true;
                startInfo.UseShellExecute = false;
                startInfo.FileName = svvPath;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.Arguments = "/Disable \"" + deviceName + "\"";
                
                using (Process exeProcess = Process.Start(startInfo))
                {
                    exeProcess.WaitForExit();
                }
            }
            catch { }
        }

        private void OnShow(object sender, EventArgs e)
        {
            isHiddenRun = false; // Stop auto-close if user opens the UI
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                this.ShowInTaskbar = false;
                trayIcon.Visible = false;
            }
            else
            {
                trayIcon.Visible = false;
            }
        }

        private void OnExit(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            Application.Exit();
        }
    }
}
