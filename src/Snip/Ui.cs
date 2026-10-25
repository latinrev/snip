using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Snip;

static class Theme
{
    public static readonly Color Accent = Color.FromArgb(76, 194, 255);
    public static readonly Color Red = Color.FromArgb(232, 17, 35);
    public static readonly Color Background = Color.FromArgb(32, 32, 32);

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // Windows 11 rounds the window's corners; Windows 10 ignores the attribute.
    public static void RoundCorners(Form form)
    {
        int round = 2;
        Native.DwmSetWindowAttribute(form.Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
    }
}

// Borderless topmost window that never takes focus from the app the user is in, and never
// shows up in screenshots, scrolling captures or recordings.
abstract class Popup : Form
{
    protected Popup()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Theme.Background;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
    }
}
