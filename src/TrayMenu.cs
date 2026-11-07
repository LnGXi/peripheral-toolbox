using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PeripheralBattery
{
    internal sealed class TrayMenu : ContextMenuStrip
    {
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        private readonly Font menuFont = new Font("Microsoft YaHei UI", 9.5F);
        internal Palette Palette { get { return Appearance.Current; } }
        internal bool Acrylic { get; private set; }
        internal TrayMenu()
        {
            Font = menuFont;
            ShowImageMargin = false;
            ShowCheckMargin = false;
            Padding = new Padding(6);
            AutoSize = false;
            Renderer = new MenuRenderer(() => Palette);
            DoubleBuffered = true;
        }
        protected override CreateParams CreateParams
        {
            get { var parameters = base.CreateParams; parameters.Style |= WindowBackdrop.FrameStyle; return parameters; }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTheme();
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0083 && WindowBackdrop.Supported) { message.Result = IntPtr.Zero; return; }
            // Menus do not take keyboard focus. Keep their frame visually active
            // while open so the compositor also applies Acrylic to child menus.
            if (message.Msg == 0x0086 && Acrylic && Visible) message.WParam = new IntPtr(1);
            base.WndProc(ref message);
            if (message.Msg == 0x0084) message.Result = new IntPtr(1);
            if (message.Msg == 0x031E) ApplyTheme();
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (!Acrylic) base.OnPaintBackground(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Acrylic) WindowBackdrop.Paint(e.Graphics, ClientSize, PaintContent);
            else base.OnPaint(e);
        }
        private void PaintContent(Graphics graphics)
        {
            Renderer.DrawToolStripBackground(new ToolStripRenderEventArgs(graphics, this));
            foreach (ToolStripItem item in Items)
            {
                if (!item.Available) continue;
                var state = graphics.Save();
                graphics.TranslateTransform(item.Bounds.X, item.Bounds.Y);
                var separator = item as ToolStripSeparator;
                if (separator != null) Renderer.DrawSeparator(new ToolStripSeparatorRenderEventArgs(graphics, separator, false));
                else
                {
                    Renderer.DrawMenuItemBackground(new ToolStripItemRenderEventArgs(graphics, item));
                    Renderer.DrawItemText(new ToolStripItemTextRenderEventArgs(graphics, item, item.Text, item.ContentRectangle, item.ForeColor, item.Font, TextFormatFlags.Default));
                    var menu = item as ToolStripMenuItem;
                    if (menu != null && menu.HasDropDownItems) Renderer.DrawArrow(new ToolStripArrowRenderEventArgs(graphics, item, item.ContentRectangle, Palette.Muted, ArrowDirection.Right));
                }
                graphics.Restore(state);
            }
            Renderer.DrawToolStripBorder(new ToolStripRenderEventArgs(graphics, this));
        }
        internal ToolStripMenuItem AddAction(string text, string glyph, EventHandler clicked)
        {
            var item = new ToolStripMenuItem(text) { Tag = glyph, AutoSize = false, Size = new Size(220, 39) };
            if (clicked != null) item.Click += clicked;
            Items.Add(item);
            return item;
        }
        internal void ApplyTheme()
        {
            ApplyColors(this);
            if (IsHandleCreated) { Acrylic = WindowBackdrop.Apply(Handle, Palette); UpdateRegion(); }
            Invalidate(true);
        }
        private void ApplyColors(ToolStrip strip)
        {
            strip.BackColor = Palette.Surface;
            strip.ForeColor = Palette.Text;
            foreach (ToolStripItem item in strip.Items)
            {
                item.ForeColor = Palette.Text;
                var menu = item as ToolStripMenuItem;
                if (menu == null || !menu.HasDropDownItems) continue;
                menu.DropDown.Font = menuFont;
                menu.DropDown.Padding = new Padding(6);
                menu.DropDown.Renderer = Renderer;
                ApplyColors(menu.DropDown);
            }
        }
        private void UpdateMenuSize()
        {
            foreach (ToolStripItem item in Items) item.Width = 220;
            Size = GetPreferredSize(Size.Empty);
        }
        public override Size GetPreferredSize(Size constrainingSize)
        {
            int height = Padding.Vertical;
            foreach (ToolStripItem item in Items) if (item.Available) height += item.Height;
            return new Size(220 + Padding.Horizontal, height);
        }
        protected override void OnOpening(CancelEventArgs e)
        {
            UpdateMenuSize();
            ApplyTheme();
            base.OnOpening(e);
        }
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            UpdateMenuSize();
            if (Acrylic) SendMessage(Handle, 0x0086, new IntPtr(1), IntPtr.Zero);
            BeginInvoke(new Action(AlignWithOwner));
        }
        private void AlignWithOwner()
        {
            if (IsDisposed || !Visible || OwnerItem == null || OwnerItem.Owner == null) return;
            Rectangle parent = OwnerItem.Owner.RectangleToScreen(OwnerItem.Owner.ClientRectangle);
            Rectangle item = OwnerItem.Owner.RectangleToScreen(OwnerItem.Bounds);
            Rectangle screen = Screen.FromRectangle(item).WorkingArea;
            int x = parent.Right - 1;
            if (x + Width > screen.Right) x = parent.Left - Width + 1;
            x = Math.Max(screen.Left, Math.Min(x, screen.Right - Width));
            int y = Math.Max(screen.Top, Math.Min(item.Top - Padding.Top, screen.Bottom - Height));
            SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, 0x0015);
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRegion();
        }
        private void UpdateRegion()
        {
            if (Width < 24 || Height < 24) return;
            Region previous = Region;
            if (Acrylic) Region = null;
            else using (var path = UiDrawing.Rounded(ClientRectangle, 12)) Region = new Region(path);
            if (previous != null) previous.Dispose();
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) menuFont.Dispose();
        }
    }

    internal sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Func<Palette> palette;
        internal MenuRenderer(Func<Palette> palette) { this.palette = palette; RoundedEdges = false; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var menu = e.ToolStrip as TrayMenu;
            bool acrylic = menu != null && menu.Acrylic;
            Color background = acrylic ? WindowBackdrop.Tint(palette()) : palette().Surface;
            UiDrawing.FillRounded(e.Graphics, e.ToolStrip.ClientRectangle, 12, background);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var path = UiDrawing.Rounded(new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 12))
            using (var pen = new Pen(palette().Border)) e.Graphics.DrawPath(pen, path);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled) UiDrawing.FillRounded(e.Graphics, new Rectangle(0, 1, e.Item.Width, e.Item.Height - 2), 7, palette().Hover);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(palette().Border)) e.Graphics.DrawLine(pen, 10, e.Item.Height / 2, e.Item.Width - 10, e.Item.Height / 2);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            int x = e.Item.Width - 24, y = e.Item.Height / 2;
            using (var pen = new Pen(palette().Muted, 1.5F)) e.Graphics.DrawLines(pen, new[] { new Point(x, y - 3), new Point(x + 3, y), new Point(x, y + 3) });
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Palette colors = palette();
            Color textColor = e.Item.Enabled ? colors.Text : colors.Muted;
            UiDrawing.Text(e.Graphics, e.Text, e.TextFont, textColor, new Rectangle(39, 0, e.Item.Width - 72, e.Item.Height), StringAlignment.Near);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            string glyph = e.Item.Tag as string;
            int left = 12, top = e.Item.Height / 2 - 8;
            using (var pen = new Pen(colors.Glyph, 1.5F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (glyph == "devices")
                {
                    using (var path = UiDrawing.Rounded(new Rectangle(left, top + 2, 16, 11), 2)) e.Graphics.DrawPath(pen, path);
                    e.Graphics.DrawLine(pen, left + 8, top + 13, left + 8, top + 16);
                    e.Graphics.DrawLine(pen, left + 4, top + 16, left + 12, top + 16);
                }
                else if (glyph == "refresh")
                {
                    e.Graphics.DrawArc(pen, left + 1, top + 1, 14, 14, 35, 290);
                    e.Graphics.DrawLines(pen, new[] { new Point(left + 14, top), new Point(left + 15, top + 5), new Point(left + 10, top + 5) });
                }
                else if (glyph == "startup")
                {
                    e.Graphics.DrawLines(pen, new[] { new Point(left + 3, top + 8), new Point(left + 8, top + 3), new Point(left + 13, top + 8) });
                    e.Graphics.DrawLine(pen, left + 8, top + 3, left + 8, top + 14);
                }
                else if (glyph == "theme")
                {
                    e.Graphics.DrawEllipse(pen, left + 2, top + 2, 12, 12);
                    using (var brush = new SolidBrush(colors.Glyph)) e.Graphics.FillPie(brush, left + 2, top + 2, 12, 12, 90, 180);
                }
                else if (glyph == "exit")
                {
                    e.Graphics.DrawArc(pen, left + 1, top + 2, 14, 14, -50, 280);
                    e.Graphics.DrawLine(pen, left + 8, top, left + 8, top + 8);
                }
                var menuItem = e.Item as ToolStripMenuItem;
                if (menuItem != null && menuItem.Checked)
                {
                    using (var check = new Pen(colors.Accent, 1.8F))
                        e.Graphics.DrawLines(check, new[] { new Point(e.Item.Width - 29, top + 8), new Point(e.Item.Width - 25, top + 12), new Point(e.Item.Width - 19, top + 5) });
                }
            }
        }
    }
}
