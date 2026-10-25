using System;
using System.Drawing;

namespace Snip;

// Smoothed zoom camera in recorded-area coordinates. A click zooms in on the cursor; the view
// then follows the cursor and eases back out after a quiet spell.
sealed class Camera
{
    const double ZoomSeconds = 0.35, PanSeconds = 0.25, HoldSeconds = 2.5;

    readonly Size area;
    readonly float zoomedIn;
    float zoom = 1;
    PointF center;
    double lastClick = double.NegativeInfinity;

    public Camera(Size area)
    {
        this.area = area;
        zoomedIn = area.Width >= 1200 ? 2f : 1.5f;
        center = new PointF(area.Width / 2f, area.Height / 2f);
    }

    public void Click(double now) => lastClick = now;

    public void Update(double now, double dt, PointF cursor)
    {
        float targetZoom = now - lastClick < HoldSeconds ? zoomedIn : 1f;
        zoom += (targetZoom - zoom) * (float)(1 - Math.Exp(-dt / ZoomSeconds));
        if (Math.Abs(zoom - 1) < 0.002f) zoom = 1;
        float follow = (float)(1 - Math.Exp(-dt / PanSeconds));
        center = new PointF(center.X + (cursor.X - center.X) * follow, center.Y + (cursor.Y - center.Y) * follow);
    }

    public RectangleF Viewport
    {
        get
        {
            float w = area.Width / zoom, h = area.Height / zoom;
            float x = Math.Max(0, Math.Min(area.Width - w, center.X - w / 2));
            float y = Math.Max(0, Math.Min(area.Height - h, center.Y - h / 2));
            return new RectangleF(x, y, w, h);
        }
    }
}
