using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Snip;

sealed class RecordingResult
{
    public string Path;
    public TimeSpan Duration;
    public int Frames;
    public Bitmap LastFrame;
    public Exception Error;
    // Stopped before the first frame; no file was written.
    public bool Cancelled;
}

// Captures a screen area on its own thread and encodes it with the camera applied.
sealed class Recorder
{
    const int Fps = 30;
    const double RippleSeconds = 0.45;

    readonly Rectangle area;
    readonly string path;
    readonly Size output;
    readonly Thread thread;
    readonly Stopwatch clock = new();
    volatile bool stopping;

    public event Action<RecordingResult> Finished;

    public Recorder(Rectangle screenArea, string path)
    {
        area = screenArea;
        this.path = path;
        output = OutputSize(screenArea.Size);
        // A foreground thread keeps the process alive after Exit until the MP4 is finalized.
        thread = new Thread(Run) { IsBackground = false, Name = "Recorder" };
        thread.SetApartmentState(ApartmentState.MTA);
    }

    // Snip's own Stop button, in screen coordinates. Clicking it shouldn't zoom the last frames.
    public Rectangle IgnoreClicksIn;

    // Time recorded so far, for the on-screen timer.
    public TimeSpan Elapsed => clock.Elapsed;

    // The area must be at least MinSide pixels on each side.
    public const int MinSide = 64;

    public void Start() => thread.Start();

    public void Stop() => stopping = true;

    // Used on exit. The thread is a foreground thread, so the process outlives this wait if
    // finalizing the MP4 takes longer.
    public void StopAndWait()
    {
        stopping = true;
        if (thread.IsAlive && !thread.Join(TimeSpan.FromSeconds(10)))
            Log.Write("recording: still finalizing at exit");
    }

    // H.264 needs even dimensions, and encoders top out around 4K. One factor for both sides
    // keeps the aspect ratio, so cursor positions scale the same way as the picture.
    static Size OutputSize(Size s)
    {
        float fit = Math.Min(1f, Math.Min(3840f / s.Width, 2160f / s.Height));
        return new Size((int)(s.Width * fit) & ~1, (int)(s.Height * fit) & ~1);
    }

    void Run()
    {
        var result = new RecordingResult { Path = path };
        if (stopping)
        {
            result.Cancelled = true;
            Finished?.Invoke(result);
            return;
        }

        var camera = new Camera(area.Size);
        var ripples = new List<(PointF at, double time)>();
        long frameTicks = TimeSpan.TicksPerSecond / Fps;
        bool wasDown = false, warnedCapture = false;
        double previous = 0;
        var stages = new double[4];
        var stage = new Stopwatch();
        void Mark(int i) { stages[i] += stage.Elapsed.TotalMilliseconds; stage.Restart(); }

        Mp4Writer writer = null;
        // Sleep() otherwise wakes on 15.6 ms ticks, which spaces frames unevenly.
        Native.timeBeginPeriod(1);
        try
        {
            Log.Write($"recording start area={area} output={output} path={path}");
            writer = new Mp4Writer(path, output.Width, output.Height, Fps);
            using var frame = new DibSurface(output);
            using var source = new DibSurface(area.Size);
            using var g = Graphics.FromImage(frame.Bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var fullView = new RectangleF(PointF.Empty, area.Size);
            clock.Start();

            // At least one frame, so a recording stopped at once still makes a valid file.
            do
            {
                double now = clock.Elapsed.TotalSeconds;
                var cursor = Cursor.Get();
                var local = new PointF(cursor.Position.X - area.X, cursor.Position.Y - area.Y);
                bool inside = area.Contains(cursor.Position);

                bool down = Native.IsDown(0x01) || Native.IsDown(0x02);
                if (down && !wasDown && inside && !IgnoreClicksIn.Contains(cursor.Position))
                {
                    camera.Click(now);
                    ripples.Add((local, now));
                }
                wasDown = down;
                ripples.RemoveAll(r => now - r.time > RippleSeconds);

                camera.Update(now, Math.Max(0.001, now - previous), local);
                previous = now;

                // A failed grab (locked screen) leaves the last frame in place, so the video
                // holds still instead of stopping.
                stage.Restart();
                var view = camera.Viewport;
                bool grabbed;
                if (view == fullView && output == area.Size)
                {
                    grabbed = frame.CaptureScreen(area, Point.Empty);
                    Mark(0);
                }
                else
                {
                    // Bilinear samples reach up to a pixel past the view on every side.
                    var grab = Rectangle.FromLTRB(Math.Max(0, (int)view.Left - 1), Math.Max(0, (int)view.Top - 1),
                        Math.Min(area.Width, (int)Math.Ceiling(view.Right) + 1), Math.Min(area.Height, (int)Math.Ceiling(view.Bottom) + 1));
                    grabbed = source.CaptureScreen(new Rectangle(area.X + grab.X, area.Y + grab.Y, grab.Width, grab.Height), grab.Location);
                    Mark(0);
                    if (grabbed) Scaler.Bilinear(source.Bits, source.Stride, area.Size, view, frame.Bits, frame.Stride, output);
                }
                Mark(1);
                if (!grabbed && !warnedCapture)
                {
                    Log.Write("recording: screen capture failed, holding the last frame");
                    warnedCapture = true;
                }

                if (grabbed)
                {
                    float scale = output.Width / view.Width;
                    PointF ToFrame(PointF p) => new((p.X - view.X) * scale, (p.Y - view.Y) * scale);
                    foreach (var (at, time) in ripples)
                    {
                        float t = (float)((now - time) / RippleSeconds);
                        float radius = (8 + 22 * t) * scale;
                        var c = ToFrame(at);
                        using var pen = new Pen(Color.FromArgb((int)(200 * (1 - t)), Theme.Accent), 3 * scale);
                        g.DrawEllipse(pen, c.X - radius, c.Y - radius, radius * 2, radius * 2);
                    }
                    g.Flush(FlushIntention.Sync);
                    if (inside && cursor.Visible) cursor.Draw(frame.Dc, ToFrame(local), scale);
                    DibSurface.Flush();
                }
                Mark(2);

                long time100ns = result.Frames == 0 ? 0 : clock.Elapsed.Ticks;
                writer.Write(frame.Bits, frame.Stride, time100ns, frameTicks);
                Mark(3);
                result.Frames++;

                long next = (long)result.Frames * frameTicks;
                int wait = (int)((next - clock.Elapsed.Ticks) / TimeSpan.TicksPerMillisecond);
                if (wait > 0) Thread.Sleep(wait);
            }
            while (!stopping);

            result.Duration = clock.Elapsed;
            result.LastFrame = new Bitmap(frame.Bitmap);
            writer.Complete();
        }
        catch (Exception e)
        {
            result.Error = e;
            Log.Write("recording failed: " + e);
        }
        finally
        {
            writer?.Dispose();
            Native.timeEndPeriod(1);
        }

        if (result.Error != null)
        {
            result.LastFrame?.Dispose();
            result.LastFrame = null;
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        int n = Math.Max(1, result.Frames);
        Log.Write($"recording ms/frame capture={stages[0] / n:0.0} scale={stages[1] / n:0.0} overlays={stages[2] / n:0.0} encode={stages[3] / n:0.0}");
        Log.Write($"recording stop frames={result.Frames} duration={result.Duration.TotalSeconds:0.0}s fps={result.Frames / Math.Max(0.001, result.Duration.TotalSeconds):0.0}");
        Finished?.Invoke(result);
    }

    // The screen capture leaves the cursor out, so it is drawn back in at the camera's scale.
    readonly struct Cursor
    {
        public readonly Point Position;
        public readonly bool Visible;
        readonly IntPtr handle;

        Cursor(Point position, bool visible, IntPtr handle)
        {
            Position = position;
            Visible = visible;
            this.handle = handle;
        }

        public static Cursor Get()
        {
            var info = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
            if (!GetCursorInfo(ref info)) return default;
            return new Cursor(new Point(info.x, info.y), (info.flags & 1) != 0, info.hCursor);
        }

        public void Draw(IntPtr hdc, PointF at, float scale)
        {
            if (!GetIconInfo(handle, out var icon)) return;
            try
            {
                var bm = new BITMAP();
                bool color = icon.hbmColor != IntPtr.Zero;
                GetObject(color ? icon.hbmColor : icon.hbmMask, Marshal.SizeOf(typeof(BITMAP)), ref bm);
                int w = bm.bmWidth, h = color ? bm.bmHeight : bm.bmHeight / 2;
                DrawIconEx(hdc, (int)(at.X - icon.xHotspot * scale), (int)(at.Y - icon.yHotspot * scale),
                    handle, (int)(w * scale), (int)(h * scale), 0, IntPtr.Zero, 3);
            }
            finally
            {
                if (icon.hbmColor != IntPtr.Zero) DeleteObject(icon.hbmColor);
                if (icon.hbmMask != IntPtr.Zero) DeleteObject(icon.hbmMask);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public int x, y; }
        [StructLayout(LayoutKind.Sequential)]
        struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public short bmPlanes, bmBitsPixel; public IntPtr bmBits; }

        [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO info);
        [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
        [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr obj, int size, ref BITMAP bm);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    }
}
