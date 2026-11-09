using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Win2DS5.App.Theming;

/// <summary>
/// The header logo: a rounded accent tile with the Segoe gamepad glyph.
/// The same artwork is the window icon, the tray icon, and the embedded executable icon.
/// </summary>
internal static class LogoArt
{
    public static readonly byte[] Brand = [0x34, 0xD3, 0x99];

    public static IntPtr CreateIcon(byte r, byte g, byte b, int size)
    {
        using var bitmap = Render(size, r, g, b);
        var data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * size * 4, size * 4);
            return FromPixels(size, pixels);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public static void Destroy(IntPtr icon)
    {
        if (icon != IntPtr.Zero)
            DestroyIcon(icon);
    }

    public static void WriteIco(string path, byte r, byte g, byte b)
    {
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
        var images = sizes.Select(size => ToIcoImage(Render(size, r, g, b))).ToArray();
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Length);
        int offset = 6 + (16 * images.Length);
        for (int i = 0; i < images.Length; i++)
        {
            int side = sizes[i] >= 256 ? 0 : sizes[i];
            writer.Write((byte)side);
            writer.Write((byte)side);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }

        foreach (var image in images)
            writer.Write(image);
    }

    public static Bitmap Render(int size, byte r, byte g, byte b)
    {
        var accent = Color.FromArgb(255, r, g, b);
        var top = Mix(accent, Color.White, 0.16);
        var bottom = Mix(accent, Color.Black, 0.08);
        double luminance = ((0.2126 * r) + (0.7152 * g) + (0.0722 * b)) / 255.0;
        var ink = luminance > 0.45 ? Color.FromArgb(255, 0x0A, 0x0C, 0x10) : Color.White;
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
        graphics.Clear(Color.Transparent);
        float radius = size * 9f / 30f;
        using (var shape = Rounded(0, 0, size - 0.5f, size - 0.5f, radius))
        using (var fill = new LinearGradientBrush(new Rectangle(0, 0, size, size), top, bottom, LinearGradientMode.Vertical))
            graphics.FillPath(fill, shape);
        DrawGlyph(graphics, size, ink);
        return bitmap;
    }

    private static void DrawGlyph(Graphics graphics, int size, Color ink)
    {
        using var family = ResolveIconFont();
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.NoWrap,
        };
        using var path = new GraphicsPath();
        float em = size * 0.50f;
        path.AddString("\uE7FC", family, 0, em, new PointF(0, 0), format);
        var bounds = path.GetBounds();
        if (bounds.Width < 1 || bounds.Height < 1)
            return;
        float dx = ((size - bounds.Width) / 2f) - bounds.X;
        float dy = ((size - bounds.Height) / 2f) - bounds.Y;
        using var shift = new Matrix();
        shift.Translate(dx, dy + (size * 0.01f));
        path.Transform(shift);
        using var brush = new SolidBrush(ink);
        graphics.FillPath(brush, path);
    }

    private static FontFamily ResolveIconFont()
    {
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            try
            {
                return new FontFamily(name);
            }
            catch (ArgumentException)
            {
            }
        }

        return FontFamily.GenericSansSerif;
    }

    private static byte[] ToIcoImage(Bitmap bitmap)
    {
        int size = bitmap.Width;
        var data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int maskStride = ((size + 31) / 32) * 4;
        var blob = new byte[40 + (size * size * 4) + (maskStride * size)];
        try
        {
            WriteInt(blob, 0, 40);
            WriteInt(blob, 4, size);
            WriteInt(blob, 8, size * 2);
            blob[12] = 1;
            blob[14] = 32;
            WriteInt(blob, 20, size * size * 4);
            for (int y = 0; y < size; y++)
                Marshal.Copy(data.Scan0 + ((size - 1 - y) * data.Stride), blob, 40 + (y * size * 4), size * 4);
            return blob;
        }
        finally
        {
            bitmap.UnlockBits(data);
            bitmap.Dispose();
        }
    }

    private static GraphicsPath Rounded(float x, float y, float width, float height, float radius)
    {
        float d = Math.Min(radius * 2f, Math.Min(width, height));
        var path = new GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
        path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        255,
        (byte)Math.Round(a.R + ((b.R - a.R) * t)),
        (byte)Math.Round(a.G + ((b.G - a.G) * t)),
        (byte)Math.Round(a.B + ((b.B - a.B) * t)));

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static IntPtr FromPixels(int size, byte[] pixels)
    {
        var header = new BitmapInfoHeader
        {
            Size = Marshal.SizeOf<BitmapInfoHeader>(),
            Width = size,
            Height = -size,
            Planes = 1,
            BitCount = 32,
        };
        var screen = GetDC(IntPtr.Zero);
        var color = CreateDIBSection(screen, ref header, 0, out var bits, IntPtr.Zero, 0);
        Marshal.Copy(pixels, 0, bits, pixels.Length);
        var mask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
        var info = new IconInfo { Icon = 1, Color = color, Mask = mask };
        var icon = CreateIconIndirect(ref info);
        DeleteObject(color);
        DeleteObject(mask);
        ReleaseDC(IntPtr.Zero, screen);
        return icon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int Icon;
        public int XHotspot;
        public int YHotspot;
        public IntPtr Mask;
        public IntPtr Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int ImageSize;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ColorsUsed;
        public int ImportantColors;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bits, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref IconInfo info);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
