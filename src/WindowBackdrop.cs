using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PeripheralBattery
{
    internal static class WindowBackdrop
    {
        [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, Bits;
            public uint Compression, ImageSize;
            public int XPels, YPels;
            public uint Used, Important;
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint flags);

        internal static bool Supported { get { return Environment.OSVersion.Version.Build >= 22621; } }
        internal static int FrameStyle { get { return Supported ? 0x00C40000 : 0; } }
        internal static bool Apply(IntPtr window, Palette palette)
        {
            if (!Supported) return false;
            int dark = palette.Dark ? 1 : 0, rounded = 2;
            DwmSetWindowAttribute(window, 20, ref dark, 4);
            DwmSetWindowAttribute(window, 33, ref rounded, 4);
            int material = SystemInformation.HighContrast ? 1 : 3;
            bool enabled = DwmSetWindowAttribute(window, 38, ref material, 4) == 0 && material == 3;
            var margins = new Margins { Left = enabled ? -1 : 0 };
            if (DwmExtendFrameIntoClientArea(window, ref margins) != 0) return false;
            return enabled;
        }
        internal static Color Tint(Palette palette)
        {
            return Color.FromArgb(palette.Dark ? 90 : 70, palette.Background);
        }

        // Copy a premultiplied-alpha DIB intact. Ordinary GDI text and background
        // painting lose alpha on extended frames, producing opaque blocks or holes.
        internal static void Paint(Graphics target, Size size, Action<Graphics> draw)
        {
            if (size.Width <= 0 || size.Height <= 0) return;
            var info = new BitmapInfo { Size = 40, Width = size.Width, Height = -size.Height, Planes = 1, Bits = 32 };
            IntPtr pixels, bitmap = CreateDIBSection(IntPtr.Zero, ref info, 0, out pixels, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero) throw new Win32Exception();
            IntPtr source = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                source = CreateCompatibleDC(IntPtr.Zero);
                if (source == IntPtr.Zero) throw new Win32Exception();
                previous = SelectObject(source, bitmap);
                using (var image = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, pixels))
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    draw(graphics);
                    graphics.Flush();
                    IntPtr destination = target.GetHdc();
                    try { BitBlt(destination, 0, 0, size.Width, size.Height, source, 0, 0, 0x00CC0020); }
                    finally { target.ReleaseHdc(destination); }
                }
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(source, previous);
                if (source != IntPtr.Zero) DeleteDC(source);
                DeleteObject(bitmap);
            }
        }
    }
}
