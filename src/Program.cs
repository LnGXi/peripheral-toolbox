using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PeripheralBattery
{
    internal static class Install
    {
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
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) key.SetValue("PeripheralBattery", "\"" + ExePath + "\" --background");
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", "外设工具箱 Peripheral Toolbox"); key.SetValue("DisplayVersion", "0.1.1");
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
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 60000 };
        private bool refreshing, exiting;
        internal TrayApp(bool welcome)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("查看设备", null, delegate { popup.ShowAtTray(); Refresh(); });
            menu.Items.Add("刷新", null, delegate { Refresh(); });
            var startup = new ToolStripMenuItem("开机启动") { CheckOnClick = true, Checked = Install.Startup };
            startup.Click += delegate { try { Install.Startup = startup.Checked; } catch (Exception e) { startup.Checked = Install.Startup; MessageBox.Show(e.Message, "无法修改开机启动"); } };
            menu.Items.Add(startup); menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Text = "外设工具箱", Icon = CreateIcon(), ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { if (popup.Visible) popup.Hide(); else { popup.ShowAtTray(); Refresh(); } } };
            popup.RefreshRequested += delegate { Refresh(); };
            timer.Tick += delegate { Refresh(); }; timer.Start(); Refresh();
            if (welcome) popup.ShowAtTray();
        }
        private static Icon CreateIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bitmap))
            using (var pen = new Pen(Color.FromArgb(37, 170, 135), 3))
            using (var brush = new SolidBrush(Color.FromArgb(37, 170, 135)))
            {
                g.Clear(Color.Transparent); g.DrawRectangle(pen, 3, 7, 23, 18); g.FillRectangle(brush, 27, 12, 3, 8); g.FillRectangle(brush, 7, 11, 15, 10);
                IntPtr handle = bitmap.GetHicon(); try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { Native.DestroyIcon(handle); }
            }
        }
        private async void Refresh()
        {
            if (refreshing || exiting) return;
            refreshing = true;
            try
            {
                List<string> errors = null;
                var devices = await Task.Run(() => Scanner.Scan(out errors));
                if (exiting) return;
                popup.Render(devices, errors);
                string summary = string.Join(" | ", devices.Select(d => d.Name + " " + d.Display));
                tray.Text = summary.Length == 0 ? "外设工具箱 · 暂无设备" : summary.Substring(0, Math.Min(63, summary.Length));
            }
            catch (Exception error) { if (!exiting) tray.Text = "外设工具箱 · 刷新失败"; System.Diagnostics.Debug.WriteLine(error); }
            finally { refreshing = false; }
        }
        protected override void ExitThreadCore()
        {
            exiting = true; timer.Stop(); timer.Dispose(); tray.Visible = false;
            Icon icon = tray.Icon; tray.Dispose(); icon.Dispose(); popup.Dispose(); base.ExitThreadCore();
        }
    }
    internal static class Program
    {
        [STAThread] private static void Main(string[] args)
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


