using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace PeripheralBattery
{
    internal enum ThemeMode { System, Light, Dark }

    internal sealed class Palette
    {
        internal readonly bool Dark;
        internal readonly Color Background, Surface, Text, Muted, Accent, Hover, IconBackground, Track, Glyph, Border, LowBattery;
        internal Palette(bool dark)
        {
            Dark = dark;
            Background = dark ? Color.FromArgb(22, 25, 31) : Color.FromArgb(246, 248, 250);
            Surface = dark ? Color.FromArgb(31, 36, 44) : Color.White;
            Text = dark ? Color.FromArgb(235, 240, 246) : Color.FromArgb(27, 34, 44);
            Muted = dark ? Color.FromArgb(145, 158, 174) : Color.FromArgb(115, 128, 140);
            Accent = dark ? Color.FromArgb(64, 206, 164) : Color.FromArgb(27, 159, 121);
            Hover = dark ? Color.FromArgb(45, 53, 64) : Color.FromArgb(229, 234, 239);
            IconBackground = dark ? Color.FromArgb(42, 49, 59) : Color.FromArgb(239, 244, 247);
            Track = dark ? Color.FromArgb(55, 65, 77) : Color.FromArgb(235, 240, 243);
            Glyph = dark ? Color.FromArgb(153, 170, 188) : Color.FromArgb(103, 122, 140);
            Border = dark ? Color.FromArgb(58, 66, 78) : Color.FromArgb(218, 225, 232);
            LowBattery = dark ? Color.FromArgb(245, 176, 91) : Color.FromArgb(218, 133, 52);
        }
    }

    internal static class Appearance
    {
        private const string SettingsKey = @"Software\PeripheralBattery";
        private const string WindowsThemeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private static readonly Palette Light = new Palette(false);
        private static readonly Palette Dark = new Palette(true);
        internal static ThemeMode Mode { get; private set; }
        internal static Palette Current { get; private set; }
        internal static Color TrayColor { get; private set; }
        static Appearance()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
            {
                ThemeMode saved;
                if (key != null && Enum.TryParse<ThemeMode>(key.GetValue("Theme", "System").ToString(), out saved) && Enum.IsDefined(typeof(ThemeMode), saved)) Mode = saved;
            }
            Refresh();
        }
        internal static void Select(ThemeMode mode)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey)) key.SetValue("Theme", mode.ToString());
            Mode = mode;
            Refresh();
        }
        internal static void Refresh()
        {
            bool appsLight = true, systemLight = true;
            using (var key = Registry.CurrentUser.OpenSubKey(WindowsThemeKey))
            {
                if (key != null)
                {
                    appsLight = Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) != 0;
                    systemLight = Convert.ToInt32(key.GetValue("SystemUsesLightTheme", 1)) != 0;
                }
            }
            Current = ForMode(Mode == ThemeMode.Dark || (Mode == ThemeMode.System && !appsLight));
            TrayColor = systemLight ? Light.Text : Dark.Text;
        }
        internal static Palette ForMode(bool dark) { return dark ? Dark : Light; }
    }

    internal static class UiDrawing
    {
        internal static GraphicsPath Rounded(Rectangle rectangle, int radius)
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
        internal static void FillRounded(Graphics graphics, Rectangle rectangle, int radius, Color color)
        {
            using (var path = Rounded(rectangle, radius))
            using (var brush = new SolidBrush(color)) graphics.FillPath(brush, path);
        }
        internal static void DrawBatteryRing(Graphics graphics, Rectangle rectangle, Color color)
        {
            var state = graphics.Save();
            graphics.TranslateTransform(rectangle.X, rectangle.Y);
            graphics.ScaleTransform(rectangle.Width / 24F, rectangle.Height / 24F);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var brush = new SolidBrush(color))
            {
                graphics.DrawArc(pen, 3, 3, 18, 18, -62, 300);
                graphics.FillPolygon(brush, new[] {
                    new PointF(13.7F, 6.2F), new PointF(8.1F, 12.7F), new PointF(11.5F, 12.7F),
                    new PointF(10.3F, 18), new PointF(16, 10.8F), new PointF(12.5F, 10.8F)
                });
            }
            graphics.Restore(state);
        }
    }
}
