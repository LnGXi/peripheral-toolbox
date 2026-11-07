using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PeripheralBattery
{
    internal sealed class Popup : Form
    {
        private static readonly Color Ink = Color.FromArgb(27, 34, 44);
        private static readonly Color Muted = Color.FromArgb(132, 140, 153);
        private static readonly Color Accent = Color.FromArgb(27, 159, 121);
        private readonly Font titleFont = new Font("Microsoft YaHei UI", 17, FontStyle.Bold);
        private readonly Font nameFont = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
        private readonly Font bodyFont = new Font("Microsoft YaHei UI", 9);
        private readonly Font smallFont = new Font("Microsoft YaHei UI", 8);
        private readonly Font numberFont = new Font("Segoe UI", 23, FontStyle.Bold);
        private readonly Rectangle refreshButton = new Rectangle(278, 25, 70, 34);
        private readonly Rectangle closeButton = new Rectangle(354, 25, 30, 34);
        private List<BatteryDevice> devices = new List<BatteryDevice>();
        private List<string> errors = new List<string>();
        private DateTime? updated;
        private int scroll;
        private Point pointer = new Point(-1, -1);
        private readonly ToolTip toolTip = new ToolTip { InitialDelay = 450 };
        private string tooltipText;
        internal event EventHandler RefreshRequested;

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        internal Popup()
        {
            Native.SetProcessDPIAware();
            Text = "外设工具箱";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(400, 544);
            BackColor = Color.FromArgb(246, 248, 250);
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            Deactivate += delegate { Hide(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { e.Cancel = true; Hide(); };
        }
        protected override CreateParams CreateParams
        {
            get { var parameters = base.CreateParams; parameters.ClassStyle |= 0x20000; return parameters; }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int rounded = 2;
            DwmSetWindowAttribute(Handle, 33, ref rounded, 4);
            using (var path = Rounded(new Rectangle(0, 0, Width, Height), 18)) Region = new Region(path);
        }
        internal void ShowAtTray()
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(Math.Max(area.Left, area.Right - Width - 14), Math.Max(area.Top, area.Bottom - Height - 14));
            Show(); Activate();
        }
        internal void Render(List<BatteryDevice> rows, List<string> failures)
        {
            devices = rows; errors = failures; updated = DateTime.Now;
            scroll = Math.Min(scroll, MaxScroll());
            Invalidate();
        }
        private int MaxScroll() { return Math.Max(0, devices.Count * 118 - 354); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            scroll = Math.Max(0, Math.Min(MaxScroll(), scroll - e.Delta / 3));
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); pointer = e.Location;
            Cursor = refreshButton.Contains(pointer) || closeButton.Contains(pointer) ? Cursors.Hand : Cursors.Default;
            int index = (pointer.Y - 103 + scroll) / 118;
            string text = pointer.Y >= 103 && pointer.Y < 457 && index >= 0 && index < devices.Count ? devices[index].Name + "\n" + devices[index].Detail : "";
            if (text != tooltipText) { tooltipText = text; toolTip.SetToolTip(this, text); }
            Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); pointer = new Point(-1, -1); Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            if (closeButton.Contains(e.Location)) Hide();
            if (refreshButton.Contains(e.Location) && RefreshRequested != null) RefreshRequested(this, EventArgs.Empty);
        }
        private static GraphicsPath Rounded(Rectangle rectangle, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
        private static void FillRounded(Graphics graphics, Rectangle rectangle, int radius, Color color)
        {
            using (var path = Rounded(rectangle, radius))
            using (var brush = new SolidBrush(color)) graphics.FillPath(brush, path);
        }
        private static void TextAt(Graphics graphics, string value, Font font, Color color, Rectangle rectangle, bool right)
        {
            TextRenderer.DrawText(graphics, value, font, rectangle, color, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping | (right ? TextFormatFlags.Right : TextFormatFlags.Left));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            TextAt(g, "外设工具箱", titleFont, Ink, new Rectangle(24, 21, 248, 36), false);
            TextAt(g, devices.Count + " 台设备  ·  每分钟自动更新", bodyFont, Muted, new Rectangle(25, 65, 330, 22), false);
            FillRounded(g, refreshButton, 10, refreshButton.Contains(pointer) ? Color.FromArgb(224, 230, 235) : Color.FromArgb(235, 239, 243));
            TextAt(g, "刷新", bodyFont, Ink, refreshButton, true);
            using (var pen = new Pen(Color.FromArgb(101, 113, 126), 1.5F))
            {
                g.DrawArc(pen, 288, 36, 11, 11, 35, 285);
                g.DrawLines(pen, new[] { new Point(298, 34), new Point(299, 39), new Point(294, 39) });
                g.DrawLine(pen, 365, 38, 373, 46); g.DrawLine(pen, 373, 38, 365, 46);
            }
            var state = g.Save();
            g.SetClip(new Rectangle(0, 103, Width, 354));
            for (int i = 0; i < devices.Count; i++) DrawDevice(g, devices[i], 103 + i * 118 - scroll);
            if (devices.Count == 0)
            {
                TextAt(g, updated.HasValue ? "连接外设后，在这里查看电量" : "正在发现你的设备…", bodyFont, Muted, new Rectangle(24, 232, 352, 40), false);
            }
            g.Restore(state);
            if (MaxScroll() > 0)
            {
                int height = Math.Max(28, 354 * 354 / (devices.Count * 118));
                int top = 103 + scroll * (354 - height) / MaxScroll();
                FillRounded(g, new Rectangle(393, top, 3, height), 1, Color.FromArgb(200, 208, 215));
            }
            using (var pen = new Pen(Color.FromArgb(229, 233, 238))) g.DrawLine(pen, 25, 476, 375, 476);
            using (var brush = new SolidBrush(errors.Count == 0 ? Accent : Color.FromArgb(221, 149, 55))) g.FillEllipse(brush, 25, 497, 6, 6);
            TextAt(g, updated.HasValue ? updated.Value.ToString("HH:mm") + " 更新" : "正在读取", smallFont, Muted, new Rectangle(39, 488, 125, 24), false);
            TextAt(g, errors.Count == 0 ? "右键托盘 · 更多设置" : "部分设备读取失败", smallFont, Muted, new Rectangle(170, 488, 205, 24), true);
        }
        private void DrawDevice(Graphics g, BatteryDevice device, int top)
        {
            FillRounded(g, new Rectangle(20, top, 360, 106), 14, Color.White);
            FillRounded(g, new Rectangle(34, top + 17, 40, 40), 12, Color.FromArgb(239, 244, 247));
            DrawDeviceIcon(g, device, 43, top + 25);
            TextAt(g, device.Name, nameFont, Ink, new Rectangle(86, top + 16, 188, 24), false);
            TextAt(g, device.Connection, smallFont, Muted, new Rectangle(86, top + 40, 180, 20), false);
            if (device.Percent.HasValue)
            {
                Color color = device.Percent.Value <= 20 ? Color.FromArgb(218, 133, 52) : Accent;
                TextAt(g, device.Percent.Value.ToString(), numberFont, Ink, new Rectangle(272, top + 13, 68, 39), true);
                TextAt(g, "%", smallFont, Muted, new Rectangle(343, top + 28, 17, 21), false);
                FillRounded(g, new Rectangle(35, top + 78, 330, 4), 2, Color.FromArgb(235, 240, 243));
                int width = (int)Math.Round(330 * device.Percent.Value / 100.0);
                if (width >= 4) FillRounded(g, new Rectangle(35, top + 78, width, 4), 2, color);
                TextAt(g, device.Detail, smallFont, Muted, new Rectangle(35, top + 85, 330, 17), false);
            }
            else
            {
                TextAt(g, device.Level ?? "—", bodyFont, Muted, new Rectangle(276, top + 24, 84, 26), true);
                TextAt(g, device.Detail, smallFont, Muted, new Rectangle(35, top + 74, 330, 22), false);
            }
        }
        private static void DrawDeviceIcon(Graphics g, BatteryDevice device, int left, int top)
        {
            string name = device.Name.ToLowerInvariant();
            using (var pen = new Pen(Color.FromArgb(103, 122, 140), 1.6F))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                if (name.Contains("mouse") || name.Contains("鼠标"))
                {
                    using (var path = Rounded(new Rectangle(left + 4, top, 15, 24), 7)) g.DrawPath(pen, path);
                    g.DrawLine(pen, left + 11, top + 5, left + 11, top + 10);
                }
                else if (name.Contains("keyboard") || name.Contains("键盘"))
                {
                    using (var path = Rounded(new Rectangle(left, top + 4, 23, 16), 3)) g.DrawPath(pen, path);
                    for (int row = 0; row < 2; row++) for (int column = 0; column < 4; column++) g.DrawLine(pen, left + 5 + column * 4, top + 9 + row * 4, left + 6 + column * 4, top + 9 + row * 4);
                }
                else if (name.Contains("controller") || name.Contains("手柄"))
                {
                    using (var path = Rounded(new Rectangle(left, top + 4, 23, 17), 6)) g.DrawPath(pen, path);
                    g.DrawLine(pen, left + 5, top + 12, left + 11, top + 12); g.DrawLine(pen, left + 8, top + 9, left + 8, top + 15);
                    g.DrawEllipse(pen, left + 16, top + 9, 2, 2);
                }
                else
                {
                    g.DrawLines(pen, new[] { new Point(left + 6, top + 5), new Point(left + 18, top + 17), new Point(left + 12, top + 23), new Point(left + 12, top + 1), new Point(left + 18, top + 7), new Point(left + 6, top + 19) });
                }
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { titleFont.Dispose(); nameFont.Dispose(); bodyFont.Dispose(); smallFont.Dispose(); numberFont.Dispose(); toolTip.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
