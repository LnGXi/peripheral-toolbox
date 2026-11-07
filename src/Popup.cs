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
        private readonly Font titleFont = new Font("Microsoft YaHei UI", 13, FontStyle.Bold);
        private readonly Font nameFont = new Font("Microsoft YaHei UI", 9.5F);
        private readonly Font bodyFont = new Font("Microsoft YaHei UI", 9);
        private readonly Font smallFont = new Font("Microsoft YaHei UI", 8);
        private readonly Font numberFont = new Font("Segoe UI", 27, FontStyle.Bold);
        private readonly Rectangle refreshButton = new Rectangle(312, 20, 30, 30);
        private readonly Rectangle closeButton = new Rectangle(352, 20, 30, 30);
        private const int ListTop = 68;
        private const int RowHeight = 80;
        private int ListHeight { get { return Math.Min(Math.Max(1, devices.Count), 5) * RowHeight; } }
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
            ClientSize = new Size(400, 192);
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
            UpdateRegion();
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (IsHandleCreated) UpdateRegion();
        }
        private void UpdateRegion()
        {
            Region previous = Region;
            using (var path = Rounded(new Rectangle(0, 0, Width, Height), 18)) Region = new Region(path);
            if (previous != null) previous.Dispose();
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
            int bottom = Bottom;
            ClientSize = new Size(400, ListHeight + 112);
            if (Visible) Top = Math.Max(Screen.FromControl(this).WorkingArea.Top, bottom - Height);
            scroll = Math.Min(scroll, MaxScroll());
            Invalidate();
        }
        private int MaxScroll() { return Math.Max(0, devices.Count * RowHeight - ListHeight); }
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
            int index = (pointer.Y - ListTop + scroll) / RowHeight;
            string text = pointer.Y >= ListTop && pointer.Y < ListTop + ListHeight && index >= 0 && index < devices.Count
                ? devices[index].Name + "\n" + devices[index].Connection + " · " + devices[index].Detail : "";
            if (refreshButton.Contains(pointer)) text = "刷新电量";
            if (closeButton.Contains(pointer)) text = "收起";
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
            TextAt(g, "设备电量", titleFont, Ink, new Rectangle(24, 18, 248, 34), false);
            if (refreshButton.Contains(pointer)) FillRounded(g, refreshButton, 9, Color.FromArgb(229, 234, 239));
            if (closeButton.Contains(pointer)) FillRounded(g, closeButton, 9, Color.FromArgb(229, 234, 239));
            using (var pen = new Pen(Color.FromArgb(101, 113, 126), 1.5F))
            {
                g.DrawArc(pen, 320, 28, 13, 13, 35, 285);
                g.DrawLines(pen, new[] { new Point(331, 26), new Point(333, 32), new Point(327, 32) });
                g.DrawLine(pen, 363, 31, 371, 39); g.DrawLine(pen, 371, 31, 363, 39);
            }
            var state = g.Save();
            g.SetClip(new Rectangle(0, ListTop, Width, ListHeight));
            for (int i = 0; i < devices.Count; i++) DrawDevice(g, devices[i], ListTop + i * RowHeight - scroll);
            if (devices.Count == 0)
            {
                TextAt(g, updated.HasValue ? "暂无设备" : "正在读取…", bodyFont, Muted, new Rectangle(24, ListTop, 352, ListHeight), false);
            }
            g.Restore(state);
            if (MaxScroll() > 0)
            {
                int height = Math.Max(28, ListHeight * ListHeight / (devices.Count * RowHeight));
                int top = ListTop + scroll * (ListHeight - height) / MaxScroll();
                FillRounded(g, new Rectangle(393, top, 3, height), 1, Color.FromArgb(200, 208, 215));
            }
            TextAt(g, errors.Count > 0 ? "部分读取失败" : updated.HasValue ? updated.Value.ToString("HH:mm") + " 更新" : "正在读取", smallFont, Muted, new Rectangle(24, Height - 36, 352, 24), true);
        }
        private void DrawDevice(Graphics g, BatteryDevice device, int top)
        {
            FillRounded(g, new Rectangle(20, top, 360, 72), 14, Color.White);
            FillRounded(g, new Rectangle(34, top + 16, 36, 36), 11, Color.FromArgb(239, 244, 247));
            DrawDeviceIcon(g, device, 40, top + 22);
            TextAt(g, device.Name, nameFont, Ink, new Rectangle(82, top + 14, 166, 26), false);
            if (device.Percent.HasValue)
            {
                Color color = device.Percent.Value <= 20 ? Color.FromArgb(218, 133, 52) : Accent;
                TextAt(g, device.Percent.Value.ToString(), numberFont, color, new Rectangle(250, top + 9, 96, 46), true);
                TextAt(g, "%", bodyFont, color, new Rectangle(348, top + 28, 17, 21), false);
                FillRounded(g, new Rectangle(83, top + 48, 175, 4), 2, Color.FromArgb(235, 240, 243));
                int width = (int)Math.Round(175 * device.Percent.Value / 100.0);
                if (width >= 4) FillRounded(g, new Rectangle(83, top + 48, width, 4), 2, color);
            }
            else
            {
                TextAt(g, device.Level ?? "未读取", bodyFont, Muted, new Rectangle(276, top + 22, 84, 26), true);
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
