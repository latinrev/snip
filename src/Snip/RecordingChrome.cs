using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Snip;

// Click-through red outline just outside the recorded area.
sealed class RecordingFrame : Popup
{
    public RecordingFrame(Rectangle area)
    {
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        var outline = area;
        outline.Inflate(3, 3);
        Bounds = outline;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var pen = new Pen(Theme.Red, 2);
        e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
    }
}

// A live status line and a Stop button, placed below the area when the monitor has room.
sealed class RecordingBar : Popup
{
    readonly Timer tick = new() { Interval = 250 };
    readonly float scale;
    readonly Func<string> status;
    readonly Color dotColor;
    Rectangle stopButton;

    public event Action StopClicked;

    public static RecordingBar ForVideo(Rectangle area, Recorder recorder) =>
        new(area, Theme.Red, () => $"{(int)recorder.Elapsed.TotalMinutes}:{recorder.Elapsed.Seconds:00}", 176);

    public static RecordingBar ForScrolling(Rectangle area, Func<string> status) =>
        new(area, Theme.Accent, status, 270);

    RecordingBar(Rectangle area, Color dotColor, Func<string> status, int width)
    {
        this.dotColor = dotColor;
        this.status = status;
        scale = Native.ScaleAt(new Point(area.X + area.Width / 2, area.Y + area.Height / 2));
        var monitor = Screen.FromRectangle(area).WorkingArea;
        int w = S(width), h = S(44), gap = S(10);
        int x = Math.Max(monitor.Left, Math.Min(monitor.Right - w, area.X + (area.Width - w) / 2));
        int y = area.Bottom + gap + h <= monitor.Bottom ? area.Bottom + gap
            : area.Top - gap - h >= monitor.Top ? area.Top - gap - h
            : area.Bottom - h - S(16);
        Bounds = new Rectangle(x, y, w, h);
        stopButton = new Rectangle(w - S(78), S(7), S(70), h - S(14));
        tick.Tick += (_, _) => Invalidate();
        tick.Start();
    }

    int S(float v) => (int)(v * scale);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.RoundCorners(this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        bool blink = DateTime.Now.Millisecond < 600;
        using (var dot = new SolidBrush(blink ? dotColor : Color.FromArgb(120, dotColor)))
            g.FillEllipse(dot, S(14), Height / 2 - S(6), S(12), S(12));

        using var font = new Font("Segoe UI Semibold", 14 * scale, GraphicsUnit.Pixel);
        g.DrawString(status(), font, Brushes.White, S(34), Height / 2 - S(10));

        using var button = new SolidBrush(Theme.Red);
        using var path = Theme.RoundedRect(stopButton, stopButton.Height / 2f);
        g.FillPath(button, path);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("Stop", font, Brushes.White, stopButton, format);
    }

    protected override void OnMouseMove(MouseEventArgs e) =>
        Cursor = stopButton.Contains(e.Location) ? Cursors.Hand : Cursors.Default;

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && stopButton.Contains(e.Location)) StopClicked?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tick.Dispose();
        base.Dispose(disposing);
    }
}
