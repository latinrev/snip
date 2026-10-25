using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Snip;

// A top-down 32bpp DIB section that GDI can BitBlt the screen into and GDI+ can draw on,
// sharing one block of pixels so a frame is never copied between the two.
sealed class DibSurface : IDisposable
{
    public readonly IntPtr Dc;
    public readonly IntPtr Bits;
    public readonly Bitmap Bitmap;
    public readonly int Stride;
    readonly IntPtr dib, previous;

    public DibSurface(Size size)
    {
        Stride = size.Width * 4;
        var info = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
            biWidth = size.Width,
            biHeight = -size.Height,
            biPlanes = 1,
            biBitCount = 32,
        };
        var screen = GetDC(IntPtr.Zero);
        Dc = CreateCompatibleDC(screen);
        ReleaseDC(IntPtr.Zero, screen);
        dib = CreateDIBSection(Dc, ref info, 0, out Bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero)
        {
            DeleteDC(Dc);
            throw new OutOfMemoryException($"Could not allocate a {size.Width}x{size.Height} capture buffer.");
        }
        previous = SelectObject(Dc, dib);
        Bitmap = new Bitmap(size.Width, size.Height, Stride, PixelFormat.Format32bppRgb, Bits);
    }

    // False when the screen can't be read, for example while the workstation is locked.
    public bool CaptureScreen(Rectangle screenRect, Point at)
    {
        var screen = GetDC(IntPtr.Zero);
        bool ok = BitBlt(Dc, at.X, at.Y, screenRect.Width, screenRect.Height, screen, screenRect.X, screenRect.Y, 0x00CC0020);
        ReleaseDC(IntPtr.Zero, screen);
        GdiFlush();
        return ok;
    }

    public static void Flush() => GdiFlush();

    public void Dispose()
    {
        Bitmap.Dispose();
        SelectObject(Dc, previous);
        DeleteObject(dib);
        DeleteDC(Dc);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, int usage, out IntPtr bits, IntPtr section, int offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] static extern bool GdiFlush();
}
