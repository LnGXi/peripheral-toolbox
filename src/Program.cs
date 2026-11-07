using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PeripheralBattery
{
    internal static class Install
    {
        [DllImport("shell32.dll", CharSet=CharSet.Unicode)] private static extern void SHChangeNotify(uint change, uint flags, string path, IntPtr item);
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PeripheralBattery";
        internal static string DirectoryPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PeripheralBattery"); } }
        internal static string ExePath { get { return Path.Combine(DirectoryPath, "PeripheralBattery.exe"); } }
        internal static bool Startup
        {
            get { using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) return key != null && key.GetValue("PeripheralBattery") != null; }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) key.SetValue("PeripheralBattery", "\"" + Application.ExecutablePath + "\" --background");
                    else key.DeleteValue("PeripheralBattery", false);
                }
            }
        }
        internal static void Setup()
        {
            Directory.CreateDirectory(DirectoryPath);
            File.Copy(Application.ExecutablePath, ExePath, true);
            SHChangeNotify(0x2000, 0x0005, ExePath, IntPtr.Zero);
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) key.SetValue("PeripheralBattery", "\"" + ExePath + "\" --background");
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", "外设工具箱 Peripheral Toolbox"); key.SetValue("DisplayVersion", "0.4.0");
                key.SetValue("UninstallString", "\"" + ExePath + "\" --uninstall"); key.SetValue("DisplayIcon", ExePath);
                key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
            }
        }
        internal static void UninstallApp()
        {
            if (MessageBox.Show("卸载外设工具箱并移除开机启动？", "外设工具箱", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) key.DeleteValue("PeripheralBattery", false);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            // A running image can be renamed. The OS temporary-file lifecycle
            // handles the renamed file, without launching a cleanup shell.
            if (string.Equals(Application.ExecutablePath, ExePath, StringComparison.OrdinalIgnoreCase))
                File.Move(ExePath, Path.Combine(Path.GetTempPath(), "PeripheralBattery-removed-" + Guid.NewGuid() + ".exe"));
            if (Directory.Exists(DirectoryPath) && Directory.GetFileSystemEntries(DirectoryPath).Length == 0) Directory.Delete(DirectoryPath);
            MessageBox.Show("已卸载并关闭开机启动。", "外设工具箱");
        }
    }
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon tray;
        private readonly Popup popup = new Popup();
        private readonly TrayMenu menu = new TrayMenu();
        private readonly ToolStripMenuItem startup;
        private readonly ToolStripMenuItem[] themeItems = new ToolStripMenuItem[3];
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 60000 };
        private readonly ScanWorker scanner = new ScanWorker();
        private bool refreshing, exiting, applyingTheme;
        private Color trayColor;
        private int traySize;
        internal TrayApp(bool welcome)
        {
            menu.AddAction("查看设备", "devices", delegate { popup.ShowAtTray(); Refresh(); });
            menu.AddAction("刷新电量", "refresh", delegate { Refresh(); });
            startup = menu.AddAction("开机启动", "startup", null);
            startup.CheckOnClick = true;
            startup.Checked = Install.Startup;
            startup.Click += delegate { try { Install.Startup = startup.Checked; } catch (Exception e) { startup.Checked = Install.Startup; MessageBox.Show(e.Message, "无法修改开机启动"); } };
            var themeMenu = new TrayMenu();
            string[] themeNames = { "跟随系统", "浅色", "深色" };
            for (int i = 0; i < themeNames.Length; i++)
            {
                ThemeMode mode = (ThemeMode)i;
                themeItems[i] = themeMenu.AddAction(themeNames[i], null, delegate
                {
                    try { Appearance.Select(mode); ApplyTheme(); }
                    catch (Exception error) { MessageBox.Show(error.Message, "无法修改外观"); }
                });
            }
            menu.AddAction("外观", "theme", null).DropDown = themeMenu;
            menu.Items.Add(new ToolStripSeparator { AutoSize = false, Size = new Size(220, 12) });
            menu.AddAction("退出", "exit", delegate { ExitThread(); });
            tray = new NotifyIcon { Text = "外设工具箱", Icon = CreateIcon(), ContextMenuStrip = menu, Visible = true };
            trayColor = Appearance.TrayColor;
            traySize = SystemInformation.SmallIconSize.Width;
            menu.Opening += delegate { ApplyTheme(); startup.Checked = Install.Startup; };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ApplyTheme(); if (popup.Visible) popup.Hide(); else { popup.ShowAtTray(); Refresh(); } } };
            popup.ThemeChanged += delegate { ApplyTheme(); };
            IntPtr windowHandle = popup.Handle;
            ApplyTheme();
            popup.RefreshRequested += delegate { Refresh(); };
            timer.Tick += delegate { Refresh(); }; timer.Start(); Refresh();
            if (welcome) popup.ShowAtTray();
        }
        private static Icon CreateIcon()
        {
            int size = SystemInformation.SmallIconSize.Width;
            using (var bitmap = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                UiDrawing.DrawBatteryRing(g, new Rectangle(0, 0, size, size), Appearance.TrayColor);
                IntPtr handle = bitmap.GetHicon(); try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { Native.DestroyIcon(handle); }
            }
        }
        private void ApplyTheme()
        {
            if (exiting || applyingTheme) return;
            applyingTheme = true;
            try
            {
                Appearance.Refresh();
                popup.ApplyTheme();
                for (int i = 0; i < themeItems.Length; i++) themeItems[i].Checked = Appearance.Mode == (ThemeMode)i;
                menu.ApplyTheme();
                if (trayColor != Appearance.TrayColor || traySize != SystemInformation.SmallIconSize.Width)
                {
                    Icon previous = tray.Icon;
                    tray.Icon = CreateIcon();
                    if (previous != null) previous.Dispose();
                    trayColor = Appearance.TrayColor;
                    traySize = SystemInformation.SmallIconSize.Width;
                }
            }
            finally { applyingTheme = false; }
        }
        private async void Refresh()
        {
            if (refreshing || exiting) return;
            refreshing = true;
            try
            {
                var snapshot = await Task.Run(() => scanner.Scan());
                if (exiting) return;
                var devices = snapshot.Devices;
                popup.Render(devices, snapshot.Errors);
                string summary = string.Join(" | ", devices.Select(d => d.Name + " " + d.Display));
                tray.Text = summary.Length == 0 ? "外设工具箱 · 暂无设备" : summary.Substring(0, Math.Min(63, summary.Length));
            }
            catch (Exception error) { if (!exiting) tray.Text = "外设工具箱 · 刷新失败"; System.Diagnostics.Debug.WriteLine(error); }
            finally { refreshing = false; }
        }
        protected override void ExitThreadCore()
        {
            exiting = true; timer.Stop(); timer.Dispose(); tray.Visible = false;
            scanner.Dispose();
            Icon icon = tray.Icon; tray.Dispose(); icon.Dispose(); menu.Dispose(); popup.Dispose(); base.ExitThreadCore();
        }
    }
    internal static class Program
    {
        [STAThread] private static void Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--scan-worker")
            {
                try { ScanWorker.Run(); }
                catch (Exception) { Environment.ExitCode = 1; }
                return;
            }
            RunApplication(args);
        }
        private static void RunApplication(string[] args)
        {
            Native.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length == 2 && args[0] == "--diagnose")
                {
                    List<string> errors; var rows = Scanner.Scan(out errors);
                    File.WriteAllLines(args[1], rows.Select(d => d.Name + " | " + d.Connection + " | " + d.Display + " | " + d.Detail).Concat(errors), System.Text.Encoding.UTF8); return;
                }
                bool created, installed = false;
                using (var mutex = new Mutex(true, @"Local\PeripheralBattery.0.1", out created))
                {
                    if (!created) { MessageBox.Show("请先从托盘退出正在运行的外设工具箱。", "外设工具箱"); return; }
                    try
                    {
                        if (args.Contains("--uninstall")) { Install.UninstallApp(); return; }
                        bool portable = args.Contains("--portable");
                        if (!portable && !string.Equals(Application.ExecutablePath, Install.ExePath, StringComparison.OrdinalIgnoreCase)) { Install.Setup(); installed = true; }
                        else Application.Run(new TrayApp(!args.Contains("--background")));
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                if (installed) System.Diagnostics.Process.Start(Install.ExePath, "--welcome");
            }
            catch (Exception error) { MessageBox.Show(error.Message, "外设工具箱"); Environment.ExitCode = 1; }
        }
    }
}


