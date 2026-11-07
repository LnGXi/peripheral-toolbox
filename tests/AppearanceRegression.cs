using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PeripheralBattery;

internal static class AppearanceRegression
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    private static IntPtr Coordinates(Point point) { return new IntPtr((point.Y << 16) | (point.X & 0xFFFF)); }
    private static int Hit(Control control, Point point)
    {
        return SendMessage(control.Handle, 0x0084, IntPtr.Zero, Coordinates(control.PointToScreen(point))).ToInt32();
    }
    [STAThread] private static int Main()
    {
        Native.SetProcessDPIAware();
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
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
            Application.Idle += run;
            Application.Run(context);
        }
        return result;
    }
    private static void Run()
    {
        using (var popup = new Popup())
        using (var menu = new TrayMenu())
        {
            var device = new BatteryDevice { Name = "测试耳机", Percent = 48, Transport = DeviceTransport.Bluetooth, Detail = "厂商蓝牙协议直读 · 约值" };
            popup.Render(new List<BatteryDevice> { device }, new List<string>());
            IntPtr handle = popup.Handle;
            Check(popup.Size == new Size(400, 192) && popup.ClientSize == popup.Size, "Borderless popup keeps its client size");
            bool acrylic = WindowBackdrop.Supported && !SystemInformation.HighContrast;
            Check(popup.Acrylic == acrylic, "Supported system backdrop or solid fallback selected");
            Check(Hit(popup, new Point(40, 30)) == 2, "Header remains draggable");
            Check(Hit(popup, new Point(320, 30)) == 1 && Hit(popup, new Point(360, 30)) == 1, "Header buttons retain client clicks");
            Check(Hit(popup, new Point(200, popup.Height - 1)) == 1, "Native backdrop frame cannot resize the panel");
            typeof(Popup).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, new object[] { new MouseEventArgs(MouseButtons.None, 0, 90, 90, 0) });
            string tooltip = (string)typeof(Popup).GetField("tooltipText", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(popup);
            Check(tooltip == "测试耳机\n蓝牙 · 48%", "Card tooltip contains only name, connection and battery");
            int clicks = 0;
            var action = menu.AddAction("查看设备", "devices", delegate { clicks++; });
            var appearance = menu.AddAction("外观", "theme", null);
            var child = new TrayMenu(); child.AddAction("跟随系统", null, null); child.AddAction("浅色", null, null);
            appearance.DropDown = child;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            menu.Show(new Point(area.Left + 40, area.Top + 40));
            appearance.ShowDropDown(); Application.DoEvents();
            Check(menu.Size == menu.GetPreferredSize(Size.Empty) && menu.ClientSize == menu.Size, "Menu keeps its compact client size");
            Check(menu.Acrylic == acrylic && child.Acrylic == acrylic, "Parent and child use the same material");
            Check(child.Left == menu.Right - 1, "Child menu touches its parent");
            Check(Hit(menu, new Point(60, action.Bounds.Top + 16)) == 1, "Menu item receives client mouse clicks");
            child.Hide();
            IntPtr position = Coordinates(new Point(60, action.Bounds.Top + 16));
            SendMessage(menu.Handle, 0x0201, new IntPtr(1), position);
            SendMessage(menu.Handle, 0x0202, IntPtr.Zero, position);
            Check(clicks == 1, "Native mouse click dispatches menu action");
            menu.Close();
        }
    }
}
