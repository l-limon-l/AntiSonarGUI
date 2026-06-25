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
            
            // Выкручиваем приоритет на максимум для автозапуска
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
        private CheckBox chkAutorun;

        private string svvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SoundVolumeView.exe");
        private string regPath = @"Software\AntiSonar";
        private string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public MainForm(bool startHidden)
        {
            EnsureDependencies();
            // Setup Form
            this.Text = "AntiSonar (Custom Edition)";
            this.Size = new Size(300, 250);
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
            
            chkAutorun = new CheckBox() { Text = "Start with Windows (Hidden)", Location = new Point(20, 155), AutoSize = true, ForeColor = Color.Blue };
            
            this.Controls.Add(lblTitle);
            this.Controls.Add(chkGaming);
            this.Controls.Add(chkChat);
            this.Controls.Add(chkMedia);
            this.Controls.Add(chkAux);
            this.Controls.Add(chkAutorun);

            LoadSettings();

            // Events
            chkGaming.CheckedChanged += SaveSettings;
            chkChat.CheckedChanged += SaveSettings;
            chkMedia.CheckedChanged += SaveSettings;
            chkAux.CheckedChanged += SaveSettings;
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
                trayIcon.Visible = false; // Прячем из трея при автозапуске
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
            if (!File.Exists(svvPath))
            {
                string zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "svv.zip");
                try
                {
                    using (WebClient client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                        client.DownloadFile("https://www.nirsoft.net/utils/soundvolumeview-x64.zip", zipPath);
                    }
                    if (File.Exists(zipPath))
                    {
                        ZipFile.ExtractToDirectory(zipPath, AppDomain.CurrentDomain.BaseDirectory);
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
            }
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runPath))
            {
                if (key != null && key.GetValue("AntiSonarGUI") != null)
                {
                    chkAutorun.Checked = true;
                }
            }
        }

        private void SaveSettings(object sender, EventArgs e)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(regPath))
            {
                key.SetValue("KillGaming", chkGaming.Checked ? 1 : 0);
                key.SetValue("KillChat", chkChat.Checked ? 1 : 0);
                key.SetValue("KillMedia", chkMedia.Checked ? 1 : 0);
                key.SetValue("KillAux", chkAux.Checked ? 1 : 0);
            }
        }

        private void OnAutorunChanged(object sender, EventArgs e)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runPath, true))
            {
                if (chkAutorun.Checked)
                {
                    string exePath = "\"" + Application.ExecutablePath + "\" -hidden";
                    key.SetValue("AntiSonarGUI", exePath);
                }
                else
                {
                    key.DeleteValue("AntiSonarGUI", false);
                }
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!File.Exists(svvPath)) return;

            if (chkGaming.Checked) DisableDevice("SteelSeries Sonar - Gaming");
            if (chkChat.Checked) DisableDevice("SteelSeries Sonar - Chat");
            if (chkMedia.Checked) DisableDevice("SteelSeries Sonar - Media");
            if (chkAux.Checked) DisableDevice("SteelSeries Sonar - Aux");
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
                trayIcon.Visible = false; // Прячем из трея при закрытии
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
