using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using PeripheralBattery;

internal static class TooltipRegression
{
    private delegate bool WindowCallback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    private static void Invoke(Popup popup, string method, params object[] args)
    {
        typeof(Popup).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, args);
    }
    private static void Move(Popup popup, int x, int y)
    {
        Invoke(popup, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, x, y, 0));
    }
    private static bool TipVisible()
    {
        bool visible = false;
        EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr window, IntPtr state)
        {
            var name = new StringBuilder(64); GetClassName(window, name, name.Capacity);
            if (name.ToString().Contains("tooltips_class32") && IsWindowVisible(window)) visible = true;
            return true;
        }, IntPtr.Zero);
        return visible;
    }
    private static void Pump(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); }
    }
    private static void ShowTip(Popup popup)
    {
        Invoke(popup, "OnMouseLeave", EventArgs.Empty);
        Move(popup, 90, 90);
        Invoke(popup, "TooltipTick", null, EventArgs.Empty);
        Pump(50);
        Check(TipVisible(), "Native tooltip shown for dismissal check");
    }
    [STAThread] private static int Main()
    {
        Native.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        int result = 1;
        using (var context = new ApplicationContext())
        {
            EventHandler run = null;
            run = delegate
            {
                Application.Idle -= run;
                try { Run(); result = 0; }
                catch (Exception error) { Console.Error.WriteLine(error.Message); }
                context.ExitThread();
            };
            Application.Idle += run; Application.Run(context);
        }
        return result;
    }
    private static void Run()
    {
        var rows = new List<BatteryDevice> {
            new BatteryDevice { Name = "测试耳机", Percent = 47, Transport = DeviceTransport.Bluetooth },
            new BatteryDevice { Name = "测试鼠标", Percent = 73, Transport = DeviceTransport.UsbReceiver }
        };
        using (var popup = new Popup { TopMost = true, Location = new Point(80, 80) })
        {
            popup.Render(rows, new List<string>()); popup.Show(); Application.DoEvents();
            Move(popup, 90, 90); Pump(600);
            Check(TipVisible(), "Tooltip appears after hover delay");
            Pump(2700);
            Check(!TipVisible(), "Tooltip expires while pointer remains inside the card");
            Move(popup, 100, 95); Pump(600);
            Check(!TipVisible(), "Moving within expired target does not reopen it");
            ShowTip(popup); Move(popup, 100, 144);
            Check(!TipVisible(), "Gap between cards dismisses tooltip");
            ShowTip(popup); Move(popup, 100, popup.Height - 20);
            Check(!TipVisible(), "Window footer dismisses tooltip");
            ShowTip(popup); Move(popup, 100, 170);
            Check(!TipVisible(), "Entering another card immediately hides old tooltip");
            ShowTip(popup); Invoke(popup, "OnMouseLeave", EventArgs.Empty);
            Check(!TipVisible(), "Leaving window dismisses tooltip");
            ShowTip(popup); SendMessage(popup.Handle, 0x0231, IntPtr.Zero, IntPtr.Zero);
            Check(!TipVisible(), "Dragging dismisses tooltip");
            SendMessage(popup.Handle, 0x0232, IntPtr.Zero, IntPtr.Zero);
            ShowTip(popup); Invoke(popup, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 90, 90, -120));
            Check(!TipVisible(), "Scrolling dismisses tooltip");
            ShowTip(popup); popup.Render(rows, new List<string>());
            Check(!TipVisible(), "Refreshing dismisses stale tooltip");
            ShowTip(popup); popup.Hide(); Pump(600);
            Check(!TipVisible(), "Hidden popup has no remaining tooltip");
            popup.Show(); ShowTip(popup); popup.Dispose(); Pump(100);
            Check(!TipVisible(), "Disposing visible popup removes tooltip");
        }
    }
}
