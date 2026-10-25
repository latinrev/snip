using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;

namespace Snip;

enum CaptureMode { Screenshot, Scrolling, Video }

// Full-virtual-screen window showing a frozen screenshot. Coordinates inside are client pixels,
// which equal physical screen pixels offset by the virtual screen origin.
sealed class Overlay : Form
{
    readonly Bitmap shot;
    readonly Bitmap dimmed;
    readonly List<Rectangle> windows;
    readonly Rectangle hintMonitor;
    readonly float scale;
    readonly Rectangle toolbar;
    readonly (CaptureMode mode, string label, Rectangle bounds)[] modeButtons;
    CaptureMode mode;
    Point? dragStart;
    Rectangle selection;
    Rectangle hover;

    // The chosen mode, the area in screen coordinates, and the frozen pixels inside it.
    public event Action<CaptureMode, Rectangle, Bitmap> Selected;

    public static Overlay FromScreen(CaptureMode mode)
    {
        var bounds = SystemInformation.VirtualScreen;
        var windows = VisibleWindows(bounds.Location);
        var shot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(shot))
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        return new Overlay(shot, bounds, windows, mode);
    }

    Overlay(Bitmap shot, Rectangle bounds, List<Rectangle> windows, CaptureMode mode)
    {
        this.mode = mode;
        this.shot = shot;
        this.windows = windows;
        dimmed = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(dimmed))
        {
            g.DrawImageUnscaled(shot, 0, 0);
            using var shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
            g.FillRectangle(shade, 0, 0, shot.Width, shot.Height);
        }

        var cursor = Cursor.Position;
        hintMonitor = ToClient(Screen.FromPoint(cursor).Bounds, bounds.Location);
        scale = Native.ScaleAt(cursor);

        int pad = S(4), buttonW = S(128), buttonH = S(36);
        var modes = new[] { (CaptureMode.Screenshot, "Screenshot"), (CaptureMode.Scrolling, "Scrolling"), (CaptureMode.Video, "Video") };
        int toolbarW = buttonW * modes.Length + pad * (modes.Length + 1);
        toolbar = new Rectangle(hintMonitor.X + (hintMonitor.Width - toolbarW) / 2, hintMonitor.Y + S(20), toolbarW, buttonH + pad * 2);
        modeButtons = new (CaptureMode, string, Rectangle)[modes.Length];
        for (int i = 0; i < modes.Length; i++)
            modeButtons[i] = (modes[i].Item1, modes[i].Item2, new Rectangle(toolbar.X + pad + i * (buttonW + pad), toolbar.Y + pad, buttonW, buttonH));

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        BackColor = Color.Black;
        Bounds = bounds;
        Text = "Snip";
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, Bounds.X, Bounds.Y, shot.Width, shot.Height, 0x0040);
        Native.ForceForeground(Handle);
        hover = WindowAt(PointToClient(Cursor.Position));
        Invalidate();
        Log.Write($"overlay bounds={Bounds} shot={shot.Size} mode={mode} buttons={string.Join(";", Array.ConvertAll(modeButtons, b => $"{b.mode}={b.bounds}"))}");
    }

    protected override void WndProc(ref Message m)
    {
        // The window spans monitors; letting WinForms react to DPI changes would resize it.
        if (m.Msg == Native.WM_DPICHANGED) return;
        base.WndProc(ref m);
    }

    int S(float v) => (int)(v * scale);

    void SetMode(CaptureMode value)
    {
        mode = value;
        Invalidate(new Rectangle(hintMonitor.X, toolbar.Y, hintMonitor.Width, toolbar.Height + S(60)));
    }

    // Keys arrive from the keyboard hook, queued, so the overlay may have closed meanwhile.
    public void HandleKey(Keys key)
    {
        if (IsDisposed) return;
        if (key == Keys.Escape) Close();
        else if (key == Keys.Tab) SetMode((CaptureMode)(((int)mode + 1) % modeButtons.Length));
        else if (key == Keys.Enter) Finish(MonitorAt(PointToClient(Cursor.Position)));
    }

    // Used only if the keyboard hook has stopped receiving keys.
    protected override void OnKeyDown(KeyEventArgs e) => HandleKey(e.KeyCode);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && toolbar.Contains(e.Location))
        {
            foreach (var button in modeButtons)
                if (button.bounds.Contains(e.Location)) SetMode(button.mode);
        }
        else if (e.Button == MouseButtons.Left)
        {
            Repaint(hover, Rectangle.Empty);
            dragStart = e.Location;
            selection = Rectangle.Empty;
        }
        else if (e.Button == MouseButtons.Right)
        {
            if (dragStart == null) { Close(); return; }
            dragStart = null;
            Repaint(selection, hover);
            selection = Rectangle.Empty;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool onToolbar = dragStart == null && toolbar.Contains(e.Location);
        Cursor = onToolbar ? Cursors.Hand : Cursors.Cross;
        if (onToolbar) return;

        var before = Highlight;
        if (dragStart is Point start)
        {
            selection = Rectangle.FromLTRB(
                Math.Min(start.X, e.X), Math.Min(start.Y, e.Y),
                Math.Max(start.X, e.X), Math.Max(start.Y, e.Y));
        }
        else
        {
            hover = WindowAt(e.Location);
        }
        if (Highlight != before) Repaint(before, Highlight);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || dragStart == null) return;
        dragStart = null;
        bool dragged = selection.Width >= 4 && selection.Height >= 4;
        Log.Write(dragged ? $"overlay drag {selection}" : $"overlay click {hover}");
        Finish(dragged ? selection : hover);
    }

    Rectangle Highlight => dragStart != null ? selection : hover;

    void Finish(Rectangle area)
    {
        area.Intersect(new Rectangle(Point.Empty, shot.Size));
        if (area.Width < 1 || area.Height < 1) return;
        var crop = new Bitmap(area.Width, area.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(crop))
            g.DrawImage(shot, new Rectangle(Point.Empty, area.Size), area, GraphicsUnit.Pixel);
        var screenArea = new Rectangle(area.X + Bounds.X, area.Y + Bounds.Y, area.Width, area.Height);
        Hide();
        Close();
        Selected?.Invoke(mode, screenArea, crop);
    }

    void Repaint(Rectangle a, Rectangle b)
    {
        var dirty = a.IsEmpty ? b : b.IsEmpty ? a : Rectangle.Union(a, b);
        dirty.Inflate((int)(48 * scale), (int)(48 * scale));
        Invalidate(dirty);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(dimmed, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);

        var r = Highlight;
        if (!r.IsEmpty)
        {
            g.DrawImage(shot, r, r, GraphicsUnit.Pixel);
            g.CompositingMode = CompositingMode.SourceOver;
            using var pen = new Pen(Theme.Accent, Math.Max(1, (int)(2 * scale)));
            g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            DrawPill(g, $"{r.Width} × {r.Height}", new Point(r.X, r.Y), aboveAnchor: true);
        }

        g.CompositingMode = CompositingMode.SourceOver;
        DrawToolbar(g);
        var action = mode switch
        {
            CaptureMode.Scrolling => "Drag over the page to capture all of it",
            CaptureMode.Video => "Drag to record",
            _ => "Drag to snip",
        };
        DrawPill(g, $"{action}  ·  Click a window  ·  Enter for full screen  ·  Tab to switch  ·  Esc to cancel",
            new Point(toolbar.X + toolbar.Width / 2, toolbar.Bottom + S(8)), centered: true);
    }

    void DrawToolbar(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using (var path = Theme.RoundedRect(toolbar, S(10)))
        using (var fill = new SolidBrush(Color.FromArgb(240, 32, 32, 32)))
            g.FillPath(fill, path);

        using var font = new Font("Segoe UI Semibold", 13 * scale, GraphicsUnit.Pixel);
        using var dim = new SolidBrush(Color.FromArgb(185, 185, 185));
        using var selectedFill = new SolidBrush(Color.FromArgb(68, 68, 68));
        foreach (var (buttonMode, label, r) in modeButtons)
        {
            bool selected = buttonMode == mode;
            if (selected)
                using (var path = Theme.RoundedRect(r, S(7)))
                    g.FillPath(selectedFill, path);

            int icon = S(14);
            var text = g.MeasureString(label, font);
            float contentW = icon + S(8) + text.Width;
            float x = r.X + (r.Width - contentW) / 2, cy = r.Y + r.Height / 2f;
            var iconRect = new RectangleF(x, cy - icon / 2f, icon, icon);
            if (buttonMode == CaptureMode.Video)
            {
                using var red = new SolidBrush(Color.FromArgb(selected ? 255 : 170, Theme.Red));
                g.FillEllipse(red, iconRect);
            }
            else if (buttonMode == CaptureMode.Scrolling)
            {
                using var pen = new Pen(selected ? Theme.Accent : Color.FromArgb(185, 185, 185), Math.Max(1.5f, 1.5f * scale));
                g.DrawRectangle(pen, iconRect.X + icon * 0.15f, iconRect.Y, icon * 0.7f, icon);
                float ax = iconRect.X + icon / 2f;
                g.DrawLine(pen, ax, iconRect.Y + icon * 0.25f, ax, iconRect.Y + icon * 0.75f);
                g.DrawLine(pen, ax - icon * 0.18f, iconRect.Y + icon * 0.55f, ax, iconRect.Y + icon * 0.75f);
                g.DrawLine(pen, ax + icon * 0.18f, iconRect.Y + icon * 0.55f, ax, iconRect.Y + icon * 0.75f);
            }
            else
            {
                using var pen = new Pen(selected ? Theme.Accent : Color.FromArgb(185, 185, 185), Math.Max(1.5f, 1.5f * scale)) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(pen, iconRect.X, iconRect.Y, iconRect.Width, iconRect.Height);
            }
            g.DrawString(label, font, selected ? Brushes.White : dim, x + icon + S(8), cy - text.Height / 2);
        }
        g.SmoothingMode = SmoothingMode.None;
    }

    void DrawPill(Graphics g, string text, Point anchor, bool aboveAnchor = false, bool centered = false)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var font = new Font("Segoe UI", 13 * scale, GraphicsUnit.Pixel);
        var size = g.MeasureString(text, font);
        float padX = 10 * scale, padY = 5 * scale;
        var box = new RectangleF(anchor.X, anchor.Y, size.Width + padX * 2, size.Height + padY * 2);
        if (centered) box.X -= box.Width / 2;
        if (aboveAnchor)
        {
            box.Y -= box.Height + 6 * scale;
            if (box.Y < 0) box.Y = anchor.Y + 6 * scale;
        }
        using var path = Theme.RoundedRect(box, box.Height / 2);
        using var fill = new SolidBrush(Color.FromArgb(230, 32, 32, 32));
        g.FillPath(fill, path);
        g.DrawString(text, font, Brushes.White, box.X + padX, box.Y + padY);
        g.SmoothingMode = SmoothingMode.None;
    }


    Rectangle WindowAt(Point p)
    {
        foreach (var w in windows)
            if (w.Contains(p)) return w;
        return MonitorAt(p);
    }

    Rectangle MonitorAt(Point clientPoint)
    {
        var origin = Bounds.Location;
        var screen = Screen.FromPoint(new Point(clientPoint.X + origin.X, clientPoint.Y + origin.Y));
        return ToClient(screen.Bounds, origin);
    }

    static Rectangle ToClient(Rectangle screenRect, Point origin) =>
        new(screenRect.X - origin.X, screenRect.Y - origin.Y, screenRect.Width, screenRect.Height);

    // Top-level windows in z-order (topmost first), in overlay client coordinates.
    static List<Rectangle> VisibleWindows(Point origin)
    {
        var result = new List<Rectangle>();
        uint self = (uint)Process.GetCurrentProcess().Id;
        var className = new StringBuilder(64);
        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == self) return true;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TRANSPARENT) != 0) return true;
            className.Clear();
            Native.GetClassName(h, className, className.Capacity);
            var cls = className.ToString();
            if (cls == "Progman" || cls == "WorkerW") return true;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT r, 16) != 0)
                Native.GetWindowRect(h, out r);
            var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            rect = TrimOverhang(rect);
            if (rect.Width < 40 || rect.Height < 40) return true;
            result.Add(ToClient(rect, origin));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    // Maximized and edge-to-edge windows overhang their monitor by a pixel or two.
    static Rectangle TrimOverhang(Rectangle r)
    {
        const int slack = 8;
        var m = Screen.FromRectangle(r).Bounds;
        int left = r.Left < m.Left && m.Left - r.Left <= slack ? m.Left : r.Left;
        int top = r.Top < m.Top && m.Top - r.Top <= slack ? m.Top : r.Top;
        int right = r.Right > m.Right && r.Right - m.Right <= slack ? m.Right : r.Right;
        int bottom = r.Bottom > m.Bottom && r.Bottom - m.Bottom <= slack ? m.Bottom : r.Bottom;
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            shot.Dispose();
            dimmed.Dispose();
        }
        base.Dispose(disposing);
    }
}
