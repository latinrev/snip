using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace Snip;

// Scrolls the window under an area from top to bottom with the mouse wheel, grabbing a settled
// full-resolution frame after each step, and hands the frames to a PageStitcher.
sealed class ScrollCapture
{
    // About 240 MB per copy of the image; the final image and the clipboard each hold one.
    const long MaxPixels = 60_000_000;
    const int MaxHeight = 40_000;
    const int SettleTimeoutMs = 2000;

    readonly Rectangle area;
    readonly int width, height;
    readonly int scrollbarStrip, hashFrom, hashTo;
    readonly Point wheelAt;
    volatile bool stopping, cancelled;
    volatile string status = "Scrolling to the top…";

    // The stitched image, or null with a reason, or null with no reason if the user stopped
    // before the capture began. A non-null image can still carry a note saying why it stopped
    // short.
    public event Action<Bitmap, string> Finished;

    public ScrollCapture(Rectangle screenArea)
    {
        area = screenArea;
        width = area.Width;
        height = area.Height;
        float scale = Native.ScaleAt(new Point(area.X + width / 2, area.Y + height / 2));
        scrollbarStrip = Math.Min(width / 4, (int)(22 * scale));
        hashFrom = width / 12;
        hashTo = width - Math.Max(width / 12, scrollbarStrip);
        wheelAt = new Point(area.X + width / 2, area.Y + height / 2);
    }

    public string Status => status;

    public void Start() => new Thread(Run) { IsBackground = true, Name = "Scroll capture" }.Start();

    // Stop keeps what has been captured so far; Cancel throws it away.
    public void Stop() => stopping = true;

    public void Cancel()
    {
        cancelled = true;
        stopping = true;
    }

    void Run()
    {
        var home = System.Windows.Forms.Cursor.Position;
        try
        {
            using var surface = new DibSurface(area.Size);

            // Up to 4,000 notches up, enough for a 100,000 px page at any common wheel setting.
            var previous = Capture(surface);
            bool atTop = false;
            for (int i = 0; i < 160 && !stopping && !atTop; i++)
            {
                Wheel(25);
                var next = Settle(surface);
                atTop = !PageStitcher.ScrolledUp(previous, next);
                previous = next;
            }
            if (stopping)
            {
                Finished?.Invoke(null, null);
                return;
            }

            var page = new PageStitcher(previous, scrollbarStrip, Math.Min(MaxPixels, (long)MaxHeight * width));
            status = $"{page.StitchedHeight:N0} px";
            while (!stopping)
            {
                Wheel(-page.Notches);
                if (!page.Add(Settle(surface))) break;
                status = $"{page.StitchedHeight:N0} px";
            }

            if (cancelled)
            {
                Finished?.Invoke(null, null);
                return;
            }
            int frames = page.Frames;
            var image = page.Compose();
            Log.Write($"scroll capture {image.Width}x{image.Height} from {frames} frames, notches={page.Notches} px/notch={page.PixelsPerNotch:0.0} scrollbar={page.ScrollbarCut}{(page.Note != null ? " " + page.Note : "")}");
            Finished?.Invoke(image, page.Note ?? (atTop ? null : "Could not reach the top of the page, so it starts partway down."));
        }
        catch (Exception e)
        {
            Log.Write("scroll capture failed: " + e);
            Finished?.Invoke(null, e.Message);
        }
        finally
        {
            Native.SetCursorPos(home.X, home.Y);
        }
    }

    // The wheel goes to the window under the cursor, so park it over the area every time in
    // case the user nudged the mouse.
    void Wheel(int notches)
    {
        Native.SetCursorPos(wheelAt.X, wheelAt.Y);
        int step = notches > 0 ? 120 : -120;
        for (int i = 0; i < Math.Abs(notches); i++)
        {
            Native.mouse_event(Native.MOUSEEVENTF_WHEEL, 0, 0, step, IntPtr.Zero);
            Thread.Sleep(15);
        }
    }

    Frame Capture(DibSurface surface)
    {
        if (!surface.CaptureScreen(area, Point.Empty))
            throw new Win32Exception("Could not read the screen. Is it locked?");
        var pixels = new int[width * height];
        Marshal.Copy(surface.Bits, pixels, 0, pixels.Length);
        return new Frame(pixels, width, height, hashFrom, hashTo);
    }

    // Smooth scrolling works through queued wheel notches with short pauses between them, and
    // browsers redraw text sharper a moment after it stops. So the page counts as settled only
    // once three grabs in a row are identical. The timeout covers pages that never stop animating.
    Frame Settle(DibSurface surface)
    {
        Thread.Sleep(120);
        var last = Capture(surface);
        int quiet = 0;
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < SettleTimeoutMs)
        {
            Thread.Sleep(80);
            var next = Capture(surface);
            quiet = last.StillRows(next) == height ? quiet + 1 : 0;
            last = next;
            if (quiet >= 2) break;
        }
        return last;
    }
}
