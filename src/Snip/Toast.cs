using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Snip;

// Bottom-right preview after a capture. Click opens the saved file, if there is one. It closes
// on its own after a few seconds.
sealed class Toast : Popup
{
    readonly Image thumb;
    readonly string title, body;
    readonly float scale;
    readonly int bodyHeight;
    readonly Timer timer = new() { Interval = 5000 };

    public Toast(Bitmap capture, string path, string title, string body)
    {
        this.title = title;
        this.body = body;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        scale = Native.ScaleAt(Cursor.Position);

        int maxW = (int)(280 * scale), maxH = (int)(160 * scale);
        float fit = Math.Min(1f, Math.Min((float)maxW / capture.Width, (float)maxH / capture.Height));
        var thumbSize = new Size(Math.Max(1, (int)(capture.Width * fit)), Math.Max(1, (int)(capture.Height * fit)));
        thumb = new Bitmap(thumbSize.Width, thumbSize.Height);
        using (var g = Graphics.FromImage(thumb))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(capture, new Rectangle(Point.Empty, thumbSize));
        }

        int pad = (int)(12 * scale);
        int width = Math.Max(thumbSize.Width, (int)(220 * scale)) + pad * 2;
        using (var g = Graphics.FromImage(thumb))
        using (var bodyFont = BodyFont())
            bodyHeight = (int)Math.Ceiling(g.MeasureString(body, bodyFont, width - pad * 2).Height);
        int height = thumbSize.Height + pad * 3 + (int)(20 * scale) + bodyHeight;

        Bounds = new Rectangle(area.Right - width - pad * 2, area.Bottom - height - pad * 2, width, height);

        timer.Tick += (_, _) => Close();
        timer.Start();
        if (path == null) return;
        Cursor = Cursors.Hand;
        Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception e) { Log.Write("open failed: " + e.Message); }
            Close();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.RoundCorners(this);
    }

    protected override void OnMouseEnter(EventArgs e) => timer.Stop();
    protected override void OnMouseLeave(EventArgs e) => timer.Start();

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int pad = (int)(12 * scale);
        g.DrawImage(thumb, pad + (ClientSize.Width - pad * 2 - thumb.Width) / 2, pad);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var titleFont = new Font("Segoe UI Semibold", 13 * scale, GraphicsUnit.Pixel);
        using var bodyFont = BodyFont();
        using var dim = new SolidBrush(Color.FromArgb(180, 180, 180));
        int y = pad * 2 + thumb.Height;
        g.DrawString(title, titleFont, Brushes.White, pad, y);
        g.DrawString(body, bodyFont, dim, new RectangleF(pad, y + 20 * scale, ClientSize.Width - pad * 2, bodyHeight));
    }

    Font BodyFont() => new("Segoe UI", 12 * scale, GraphicsUnit.Pixel);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            thumb.Dispose();
        }
        base.Dispose(disposing);
    }
}
