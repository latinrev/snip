using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Xunit;

namespace Snip.Tests;

public class PageStitcherTests
{
    const int Width = 300, Height = 400, Header = 30, Footer = 20, Scrollbar = 12, Strip = 24;
    const int Content = Height - Header - Footer;

    // A browser-like window: a sticky header and footer, a page that scrolls between them, and a
    // scrollbar down the right edge whose thumb tracks the scroll position.
    sealed class Window
    {
        readonly int[] page;
        public readonly int PageHeight;
        public int Scroll;
        // A fixed animated box in the middle of the window that changes on every grab.
        public bool Spinner;
        int grabs;

        public Window(int pageHeight, Func<int, int, int> pagePixel)
        {
            PageHeight = pageHeight;
            page = new int[pageHeight * Width];
            for (int y = 0; y < pageHeight; y++)
                for (int x = 0; x < Width; x++)
                    page[y * Width + x] = pagePixel(x, y);
        }

        public int MaxScroll => PageHeight - Content;

        public void ScrollBy(int pixels) => Scroll = Math.Max(0, Math.Min(MaxScroll, Scroll + pixels));

        public Frame Grab()
        {
            var pixels = new int[Width * Height];
            grabs++;
            int thumbTop = Scroll * Height / PageHeight, thumbHeight = Content * Height / PageHeight;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int value;
                    if (x >= Width - Scrollbar) value = y >= thumbTop && y < thumbTop + thumbHeight ? 0x808080 : 0xF0F0F0;
                    else if (y < Header) value = 0x203040 + x;
                    else if (y >= Height - Footer) value = 0x405060 + x;
                    else if (Spinner && y >= 180 && y < 200 && x >= 100 && x < 140) value = Noise(x + grabs * 1000, y);
                    else value = page[(Scroll + y - Header) * Width + x];
                    pixels[y * Width + x] = value;
                }
            return new Frame(pixels, Width, Height, Width / 12, Width - Math.Max(Width / 12, Strip));
        }

        // What a perfect capture holds: the header, the whole page and the footer, without the
        // scrollbar.
        public int Expected(int x, int y) =>
            y < Header ? 0x203040 + x
            : y >= Header + PageHeight ? 0x405060 + x
            : page[(y - Header) * Width + x];
    }

    static int Noise(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            return (int)((h ^ (h >> 15)) & 0xFFFFFF);
        }
    }

    // Drives the stitcher the way ScrollCapture does. Each step scrolls by the planned notches,
    // minus any the "browser" drops.
    static Bitmap Capture(Window window, int pixelsPerNotch, Func<int, int> dropped = null, long maxPixels = long.MaxValue)
    {
        var page = new PageStitcher(window.Grab(), Strip, maxPixels);
        for (int step = 0; step < 500; step++)
        {
            window.ScrollBy(Math.Max(0, page.Notches - (dropped?.Invoke(step) ?? 0)) * pixelsPerNotch);
            if (!page.Add(window.Grab())) return page.Compose();
        }
        throw new InvalidOperationException("The capture never reached the end of the page.");
    }

    static void AssertMatches(Window window, Bitmap image)
    {
        Assert.Equal(Width - Scrollbar, image.Width);
        Assert.Equal(Header + window.PageHeight + Footer, image.Height);
        var bits = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            var row = new int[image.Width];
            for (int y = 0; y < image.Height; y++)
            {
                Marshal.Copy(bits.Scan0 + y * bits.Stride, row, 0, row.Length);
                for (int x = 0; x < row.Length; x++)
                    if ((row[x] & 0xFFFFFF) != window.Expected(x, y))
                        Assert.Fail($"Pixel ({x}, {y}) is {row[x] & 0xFFFFFF:X6}, expected {window.Expected(x, y):X6}.");
            }
        }
        finally { image.UnlockBits(bits); }
    }

    [Fact]
    public void ReproducesALongPageExactly()
    {
        var window = new Window(3000, Noise);
        using var image = Capture(window, 40);
        AssertMatches(window, image);
    }

    [Fact]
    public void ReproducesAPageThatFitsTheSizeLimitExactly()
    {
        var window = new Window(1000, Noise);
        using var image = Capture(window, 40, maxPixels: Width * (long)(Header + 1000 + Footer));
        AssertMatches(window, image);
    }

    [Fact]
    public void KeepsGoingWhenTheBrowserDropsAWholeBurst()
    {
        var window = new Window(3000, Noise);
        using var image = Capture(window, 40, step => step == 4 ? int.MaxValue : 0);
        AssertMatches(window, image);
    }

    [Fact]
    public void KeepsGoingWhenTheBrowserDropsNotches()
    {
        var window = new Window(3000, Noise);
        using var image = Capture(window, 40, step => step % 3 == 1 ? 1 : 0);
        AssertMatches(window, image);
    }

    [Fact]
    public void CrossesBlankStretchesWithFewDistinctRows()
    {
        // A busy first screen sets the scroll rate. Below it is a white stretch with a thin rule
        // every 250 rows, too few distinct rows to vote on, so the shift has to be guessed.
        var window = new Window(2500, (x, y) => y < 400 || y >= 1200 || y % 250 < 2 ? Noise(x, y) : 0xFFFFFF);
        using var image = Capture(window, 40);
        AssertMatches(window, image);
    }

    [Fact]
    public void GivesUpOnScreensOfBlankPageWithANote()
    {
        var window = new Window(6000, (x, y) => y < 400 || y % 250 < 2 ? Noise(x, y) : 0xFFFFFF);
        var page = new PageStitcher(window.Grab(), Strip, long.MaxValue);
        while (true)
        {
            window.ScrollBy(page.Notches * 40);
            if (!page.Add(window.Grab())) break;
        }
        Assert.Equal("Stopped after several screens of blank page.", page.Note);
        Assert.True(window.Scroll < window.MaxScroll);
    }

    [Fact]
    public void ReachesTheEndOfAPageWithAnAnimationOnIt()
    {
        var window = new Window(3000, Noise) { Spinner = true };
        var page = new PageStitcher(window.Grab(), Strip, long.MaxValue);
        while (true)
        {
            window.ScrollBy(page.Notches * 40);
            if (!page.Add(window.Grab())) break;
        }
        Assert.Null(page.Note);
        Assert.Equal(window.MaxScroll, window.Scroll);
        using var image = page.Compose();
        Assert.Equal(Header + window.PageHeight + Footer, image.Height);
    }

    [Fact]
    public void FindsTheTopOfAPageWithAnAnimationOnIt()
    {
        var window = new Window(3000, Noise) { Spinner = true, Scroll = 2000 };
        var previous = window.Grab();
        int steps = 0;
        while (steps < 100)
        {
            steps++;
            window.ScrollBy(-25 * 40);
            var next = window.Grab();
            bool scrolled = PageStitcher.ScrolledUp(previous, next);
            previous = next;
            if (!scrolled) break;
        }
        Assert.Equal(0, window.Scroll);
        Assert.Equal(3, steps);
    }

    [Fact]
    public void ABlankScreenEndsTheCaptureWithANote()
    {
        var window = new Window(3000, (x, y) => y < 400 || y >= 1400 ? Noise(x, y) : 0xFFFFFF);
        var page = new PageStitcher(window.Grab(), Strip, long.MaxValue);
        while (true)
        {
            window.ScrollBy(page.Notches * 40);
            if (!page.Add(window.Grab())) break;
        }
        Assert.Equal("Stopped at a blank stretch of the page.", page.Note);
    }

    [Fact]
    public void StopsWhenThePageDoesNotMove()
    {
        var window = new Window(3000, Noise);
        var page = new PageStitcher(window.Grab(), Strip, long.MaxValue);
        Assert.True(page.Add(window.Grab()));
        Assert.False(page.Add(window.Grab()));
        Assert.Null(page.Note);
        using var image = page.Compose();
        Assert.Equal(Height, image.Height);
    }

    [Fact]
    public void SizeLimitCapsTheImage()
    {
        var window = new Window(3000, Noise);
        using var image = Capture(window, 40, maxPixels: Width * 1200L);
        Assert.InRange(image.Height, Height, 1200);
        Assert.True(window.Scroll < window.MaxScroll);
    }

    [Fact]
    public void SizeLimitSetsTheNote()
    {
        var window = new Window(3000, Noise);
        var page = new PageStitcher(window.Grab(), Strip, Width * 1200L);
        while (true)
        {
            window.ScrollBy(page.Notches * 40);
            if (!page.Add(window.Grab())) break;
        }
        Assert.StartsWith("Stopped at", page.Note);
    }

    [Fact]
    public void OffsetGuessesAShiftOfWholeNotchesOnABlankPage()
    {
        var before = Blank();
        var after = Blank();
        DrawRule(before, 300);
        DrawRule(after, 220);
        int offset = PageStitcher.Offset(Make(before), Make(after), 40, 3, out var kind);
        Assert.Equal(ShiftKind.Guessed, kind);
        Assert.Equal(80, offset);
    }

    [Fact]
    public void OffsetTrustsThePlanWhenTheOldFrameShowedNothing()
    {
        // The old frame is blank because the next section was still fading in. It appears in
        // the new frame, so no shift explains it, and only the planned step is a fair guess.
        var before = Blank();
        var after = Blank();
        DrawRule(after, 250);
        int offset = PageStitcher.Offset(Make(before), Make(after), 40, 3, out var kind);
        Assert.Equal(ShiftKind.Guessed, kind);
        Assert.Equal(120, offset);
    }

    [Fact]
    public void OffsetMatchesRowsOnABusyPage()
    {
        var window = new Window(3000, Noise);
        var before = window.Grab();
        window.ScrollBy(137);
        int offset = PageStitcher.Offset(before, window.Grab(), 0, 3, out var kind);
        Assert.Equal(ShiftKind.Matched, kind);
        Assert.Equal(137, offset);
    }

    static int[] Blank()
    {
        var pixels = new int[Width * Height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = 0xFFFFFF;
        return pixels;
    }

    static void DrawRule(int[] pixels, int y)
    {
        for (int row = y; row < y + 2; row++)
            for (int x = 0; x < Width; x++)
                pixels[row * Width + x] = Noise(x, row - y);
    }

    static Frame Make(int[] pixels) => new(pixels, Width, Height, Width / 12, Width - Width / 12);
}
