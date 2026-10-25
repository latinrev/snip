using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Snip;

// Win+V-style picker for recent screenshots. Picking one copies it and pastes it into the
// window that was in front when the panel opened.
sealed class HistoryPanel : Form
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    readonly IntPtr previous;
    readonly float scale;
    readonly int headerHeight;
    readonly int count;
    readonly ShotList list;
    readonly Native.WinEventProc onForegroundChanged;
    IntPtr foregroundHook;
    bool dismissed;

    public HistoryPanel(string folder, IntPtr previous)
    {
        this.previous = previous;
        var cursor = Cursor.Position;
        scale = Native.ScaleAt(cursor);
        var files = RecentScreenshots(folder);
        count = files.Count;
        Log.Write($"history open: {count} screenshots in the last {Window.TotalHours:0}h");

        var area = Screen.FromPoint(cursor).WorkingArea;
        int width = S(440), height = Math.Min(S(760), area.Height - S(80));
        headerHeight = S(56);

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Theme.Background;
        Text = "Snip history";
        Bounds = new Rectangle(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);

        list = new ShotList(files, scale)
        {
            Bounds = new Rectangle(S(8), headerHeight, width - S(10), height - headerHeight - S(8)),
            BackColor = Theme.Background,
        };
        list.Picked += Pick;
        Controls.Add(list);
        onForegroundChanged = (_, _, hwnd, _, _, _, _) => { if (hwnd != Handle) Dismiss(); };
    }

    int S(float v) => (int)(v * scale);

    static List<FileInfo> RecentScreenshots(string folder)
    {
        if (!Directory.Exists(folder)) return new List<FileInfo>();
        var cutoff = DateTime.Now - Window;
        return new DirectoryInfo(folder).EnumerateFiles("*.png")
            .Where(f => f.LastWriteTime >= cutoff)
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.RoundCorners(this);
        Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
    }

    // Form.Deactivate is unreliable after taking the foreground from another process with
    // AttachThreadInput, so watch foreground changes system-wide: any other window coming to
    // the front means the user clicked away.
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Native.ForceForeground(Handle);
        list.Focus();
        foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, onForegroundChanged, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        Log.Write($"history shown foreground={Native.GetForegroundWindow() == Handle}");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Native.UnhookWinEvent(foregroundHook);
        base.OnFormClosed(e);
    }

    public void Dismiss()
    {
        if (dismissed) return;
        dismissed = true;
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var title = new Font("Segoe UI Semibold", 16 * scale, GraphicsUnit.Pixel);
        using var sub = new Font("Segoe UI", 12 * scale, GraphicsUnit.Pixel);
        using var dim = new SolidBrush(Color.FromArgb(170, 170, 170));
        g.DrawString("Screenshots", title, Brushes.White, S(16), S(16));
        var note = $"Last 24 hours  ·  {count}";
        var size = g.MeasureString(note, sub);
        g.DrawString(note, sub, dim, ClientSize.Width - size.Width - S(18), S(20));
    }

    void Pick(FileInfo file, int index)
    {
        try
        {
            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var image = Image.FromStream(stream))
            using (var copy = new Bitmap(image))
                Clipboard.SetDataObject(copy, true, 10, 50);
        }
        catch (Exception e)
        {
            Log.Write($"history pick failed: {file.Name}: {e.Message}");
            return;
        }
        Log.Write($"history pick index={index} name={file.Name}");
        Dismiss();

        if (previous == IntPtr.Zero) return;
        Native.SetForegroundWindow(previous);
        var paste = new System.Windows.Forms.Timer { Interval = 120 };
        paste.Tick += (_, _) =>
        {
            paste.Dispose();
            if (Native.GetForegroundWindow() == previous) Native.SendKeys(0x11, (ushort)'V');
            else Log.Write("history paste skipped: the previous window did not come back");
        };
        paste.Start();
    }

    // Owner-drawn list that grows a page at a time as the user nears the bottom and decodes
    // thumbnails only for cards that have been on screen.
    sealed class ShotList : ScrollableControl
    {
        const int PageSize = 12;
        static readonly Color CardColor = Color.FromArgb(44, 44, 44);

        readonly List<FileInfo> files;
        readonly float scale;
        readonly Dictionary<string, Thumb> thumbs = new();
        readonly SemaphoreSlim decoders = new(2);
        int shown;
        int active;

        public event Action<FileInfo, int> Picked;

        sealed class Thumb
        {
            public Bitmap Image;
            public Size Original;
            public bool Failed;
        }

        public ShotList(List<FileInfo> files, float scale)
        {
            this.files = files;
            this.scale = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            AutoScroll = true;
            ShowMore();
        }

        int S(float v) => (int)(v * scale);
        int ThumbHeight => S(200);
        int CaptionHeight => S(30);
        int Pitch => ThumbHeight + CaptionHeight + S(10);
        int ScrollTop => -AutoScrollPosition.Y;

        void ShowMore()
        {
            if (shown >= files.Count) return;
            shown = Math.Min(files.Count, shown + PageSize);
            AutoScrollMinSize = new Size(0, shown * Pitch + S(56));
            Log.Write($"history page: showing {shown}/{files.Count}");
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetWindowTheme(Handle, "DarkMode_Explorer", null);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var caption = new Font("Segoe UI", 12 * scale, GraphicsUnit.Pixel);
            using var dim = new SolidBrush(Color.FromArgb(170, 170, 170));

            if (files.Count == 0)
            {
                DrawCentered(g, "No screenshots in the last 24 hours.\nPress Win+Shift+S to take one.", caption, dim, new Rectangle(0, 0, ClientSize.Width, S(160)));
                return;
            }

            int first = ScrollTop / Pitch;
            int last = Math.Min(shown - 1, (ScrollTop + ClientSize.Height) / Pitch);
            for (int i = first; i <= last; i++)
                DrawCard(g, i, CardRect(i), caption, dim);

            var footer = new Rectangle(0, shown * Pitch - ScrollTop, ClientSize.Width, S(48));
            var footerText = shown < files.Count ? "Loading more…" : "That's everything from the last 24 hours";
            DrawCentered(g, footerText, caption, dim, footer);

            if (shown < files.Count && ScrollTop + ClientSize.Height > shown * Pitch - Pitch * 2)
                BeginInvoke(new Action(ShowMore));
        }

        Rectangle CardRect(int i) => new(0, i * Pitch - ScrollTop, ClientSize.Width - S(4), ThumbHeight + CaptionHeight);

        void DrawCard(Graphics g, int i, Rectangle r, Font font, Brush dim)
        {
            var file = files[i];
            using (var path = Theme.RoundedRect(r, S(8)))
            using (var fill = new SolidBrush(CardColor))
            {
                g.FillPath(fill, path);
                if (i == active)
                {
                    using var pen = new Pen(Theme.Accent, S(2));
                    g.DrawPath(pen, path);
                }
            }

            var thumbArea = new Rectangle(r.X + S(8), r.Y + S(8), r.Width - S(16), ThumbHeight - S(8));
            if (!thumbs.TryGetValue(file.FullName, out var thumb))
            {
                Decode(file, thumbArea.Size);
                DrawCentered(g, "Loading…", font, dim, thumbArea);
            }
            else if (thumb == null)
                DrawCentered(g, "Loading…", font, dim, thumbArea);
            else if (thumb.Failed)
                DrawCentered(g, "Can't preview this file", font, dim, thumbArea);
            else
                g.DrawImage(thumb.Image,
                    thumbArea.X + (thumbArea.Width - thumb.Image.Width) / 2,
                    thumbArea.Y + (thumbArea.Height - thumb.Image.Height) / 2,
                    thumb.Image.Width, thumb.Image.Height);

            int y = r.Bottom - CaptionHeight + S(6);
            g.DrawString($"{file.LastWriteTime:h:mm tt}  ·  {Ago(file.LastWriteTime)}", font, Brushes.White, r.X + S(10), y);
            if (thumb != null && !thumb.Failed)
            {
                var dims = $"{thumb.Original.Width} × {thumb.Original.Height}";
                var size = g.MeasureString(dims, font);
                g.DrawString(dims, font, dim, r.Right - size.Width - S(10), y);
            }
        }

        void Decode(FileInfo file, Size box)
        {
            thumbs[file.FullName] = null;
            Task.Run(async () =>
            {
                await decoders.WaitAsync();
                Thumb thumb;
                try { thumb = LoadThumb(file.FullName, box); }
                catch (Exception e)
                {
                    Log.Write($"thumbnail failed: {file.Name}: {e.Message}");
                    thumb = new Thumb { Failed = true };
                }
                finally { decoders.Release(); }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        thumbs[file.FullName] = thumb;
                        Invalidate();
                    }));
                }
                catch (InvalidOperationException) { thumb.Image?.Dispose(); }
            });
        }

        static Thumb LoadThumb(string path, Size box)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var image = Image.FromStream(stream, false, false);
            float fit = Math.Min(1f, Math.Min((float)box.Width / image.Width, (float)box.Height / image.Height));
            var bmp = new Bitmap(Math.Max(1, (int)(image.Width * fit)), Math.Max(1, (int)(image.Height * fit)));
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, 0, 0, bmp.Width, bmp.Height);
            }
            return new Thumb { Image = bmp, Original = image.Size };
        }

        int IndexAt(Point p)
        {
            int contentY = p.Y + ScrollTop;
            int i = contentY / Pitch;
            bool onCard = contentY >= 0 && contentY % Pitch < ThumbHeight + CaptionHeight;
            return onCard && i < shown ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Location);
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
            if (i >= 0 && i != active)
            {
                active = i;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = IndexAt(e.Location);
            if (e.Button == MouseButtons.Left && i >= 0) Picked?.Invoke(files[i], i);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Up or Keys.Down or Keys.Enter || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (files.Count == 0) return;
            if (e.KeyCode == Keys.Enter) { Picked?.Invoke(files[active], active); return; }
            int step = e.KeyCode == Keys.Down ? 1 : e.KeyCode == Keys.Up ? -1 : 0;
            if (step == 0) return;
            active = Math.Max(0, Math.Min(files.Count - 1, active + step));
            while (active >= shown) ShowMore();
            ScrollIntoView(active);
            Invalidate();
        }

        void ScrollIntoView(int i)
        {
            int top = i * Pitch;
            if (top < ScrollTop) AutoScrollPosition = new Point(0, top);
            else if (top + Pitch > ScrollTop + ClientSize.Height) AutoScrollPosition = new Point(0, top + Pitch - ClientSize.Height);
        }

        static string Ago(DateTime t)
        {
            var span = DateTime.Now - t;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min ago";
            return $"{(int)span.TotalHours} h ago";
        }

        static void DrawCentered(Graphics g, string text, Font font, Brush brush, Rectangle r)
        {
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, brush, r, format);
        }


        protected override void Dispose(bool disposing)
        {
            if (disposing)
                foreach (var t in thumbs.Values) t?.Image?.Dispose();
            base.Dispose(disposing);
        }
    }
}
