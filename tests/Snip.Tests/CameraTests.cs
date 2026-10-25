using System.Drawing;
using Xunit;

namespace Snip.Tests;

public class CameraTests
{
    static readonly Size Area = new(1920, 1080);

    static void Run(Camera camera, double from, double to, PointF cursor)
    {
        for (double t = from; t < to; t += 1 / 30.0) camera.Update(t, 1 / 30.0, cursor);
    }

    [Fact]
    public void StartsShowingTheWholeArea()
    {
        Assert.Equal(new RectangleF(0, 0, 1920, 1080), new Camera(Area).Viewport);
    }

    [Fact]
    public void ClickZoomsInOnTheCursor()
    {
        var camera = new Camera(Area);
        var cursor = new PointF(500, 400);
        camera.Click(0);
        Run(camera, 0, 2, cursor);

        var view = camera.Viewport;
        Assert.InRange(view.Width, 1920 / 2f * 0.98f, 1920 / 2f * 1.02f);
        Assert.True(view.Contains(cursor));
    }

    [Fact]
    public void ZoomedViewStaysInsideTheArea()
    {
        var camera = new Camera(Area);
        camera.Click(0);
        Run(camera, 0, 2, new PointF(1910, 1075));

        var view = camera.Viewport;
        Assert.True(view.Left >= 0 && view.Top >= 0);
        Assert.True(view.Right <= 1920.01f && view.Bottom <= 1080.01f);
    }

    [Fact]
    public void ZoomsBackOutAfterAQuietSpell()
    {
        var camera = new Camera(Area);
        camera.Click(0);
        Run(camera, 0, 6, new PointF(500, 400));
        Assert.Equal(new RectangleF(0, 0, 1920, 1080), camera.Viewport);
    }

    [Fact]
    public void SmallAreasZoomLess()
    {
        var camera = new Camera(new Size(800, 600));
        camera.Click(0);
        Run(camera, 0, 2, new PointF(400, 300));
        Assert.InRange(camera.Viewport.Width, 800 / 1.5f * 0.98f, 800 / 1.5f * 1.02f);
    }
}
