using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Xunit;

namespace Snip.Tests;

public class ScalerTests
{
    static int[] Scale(int[] source, Size sourceSize, RectangleF view, Size outputSize)
    {
        var output = new int[outputSize.Width * outputSize.Height];
        var src = GCHandle.Alloc(source, GCHandleType.Pinned);
        var dst = GCHandle.Alloc(output, GCHandleType.Pinned);
        try
        {
            Scaler.Bilinear(src.AddrOfPinnedObject(), sourceSize.Width * 4, sourceSize, view,
                dst.AddrOfPinnedObject(), outputSize.Width * 4, outputSize);
        }
        finally
        {
            src.Free();
            dst.Free();
        }
        return output;
    }

    [Fact]
    public void FullViewAtTheSameSizeCopiesThePixels()
    {
        var size = new Size(7, 5);
        var source = new int[35];
        var random = new Random(1);
        for (int i = 0; i < source.Length; i++) source[i] = random.Next(0x1000000);

        var output = Scale(source, size, new RectangleF(0, 0, 7, 5), size);

        for (int i = 0; i < source.Length; i++) Assert.Equal(source[i], output[i] & 0xFFFFFF);
    }

    [Fact]
    public void OnePixelWideSourceStretchesWithoutReadingPastIt()
    {
        var source = new[] { 0x112233, 0x445566 };
        var output = Scale(source, new Size(1, 2), new RectangleF(0, 0, 1, 2), new Size(4, 2));
        for (int i = 0; i < 8; i++)
            Assert.Equal(i < 4 ? 0x112233 : 0x445566, output[i] & 0xFFFFFF);
    }

    [Fact]
    public void HalfwayBetweenTwoPixelsBlendsThem()
    {
        var source = new[] { 0x000000, 0xC8C8C8 };
        var output = Scale(source, new Size(2, 1), new RectangleF(0.5f, 0, 1, 1), new Size(1, 1));
        Assert.Equal(0x646464, output[0] & 0xFFFFFF);
    }
}
