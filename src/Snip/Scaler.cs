using System;
using System.Drawing;
using System.Threading.Tasks;

namespace Snip;

static class Scaler
{
    // Bilinear resample of the view rectangle of src into all of dst, both 32bpp with the given
    // strides. GDI+ does this on one core and takes ~45 ms per 1440p frame; rows here run in
    // parallel with 8-bit fixed point.
    public static unsafe void Bilinear(IntPtr src, int srcStride, Size srcSize, RectangleF view, IntPtr dst, int dstStride, Size dstSize)
    {
        int dw = dstSize.Width, dh = dstSize.Height, sw = srcSize.Width, sh = srcSize.Height;
        var x0 = new int[dw];
        var x1 = new int[dw];
        var fx = new uint[dw];
        float stepX = view.Width / dw, stepY = view.Height / dh;
        for (int x = 0; x < dw; x++)
        {
            float sx = Math.Max(0, Math.Min(sw - 1, view.X + (x + 0.5f) * stepX - 0.5f));
            x0[x] = (int)sx;
            x1[x] = Math.Min(x0[x] + 1, sw - 1);
            fx[x] = (uint)((sx - x0[x]) * 256);
        }
        byte* srcBits = (byte*)src, dstBits = (byte*)dst;

        Parallel.For(0, dh, y =>
        {
            float sy = Math.Max(0, Math.Min(sh - 1, view.Y + (y + 0.5f) * stepY - 0.5f));
            int y0 = (int)sy;
            uint fy = (uint)((sy - y0) * 256), gy = 256 - fy;
            uint* row0 = (uint*)(srcBits + (long)y0 * srcStride);
            uint* row1 = (uint*)(srcBits + (long)Math.Min(y0 + 1, sh - 1) * srcStride);
            uint* outRow = (uint*)(dstBits + (long)y * dstStride);
            for (int x = 0; x < dw; x++)
            {
                uint f = fx[x], g = 256 - f;
                uint top = Lerp(row0[x0[x]], row0[x1[x]], f, g);
                uint bottom = Lerp(row1[x0[x]], row1[x1[x]], f, g);
                outRow[x] = Lerp(top, bottom, fy, gy);
            }
        });
    }

    // Blends red+blue and green in two packed operations.
    static uint Lerp(uint a, uint b, uint f, uint g) =>
        ((((a & 0xFF00FF) * g + (b & 0xFF00FF) * f) >> 8) & 0xFF00FF)
        | ((((a & 0x00FF00) * g + (b & 0x00FF00) * f) >> 8) & 0x00FF00);
}
