using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Snip;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\Snip.SingleInstance", out bool first);
        if (!first) return;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => Log.Write("error: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("fatal: " + e.ExceptionObject);

        SnipApp app;
        try { app = new SnipApp(); }
        catch (Exception e)
        {
            Log.Write("startup failed: " + e);
            MessageBox.Show($"Snip could not start: {e.Message}", "Snip", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Application.Run(app);
    }
}

sealed class SnipApp : ApplicationContext
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string PrefsKey = @"Software\Snip";

    static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    static readonly string RecordingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Screen Recordings");

    readonly Control invoker = new();
    readonly NotifyIcon tray;
    readonly KeyboardHook hook;
    readonly ToolStripMenuItem autostartItem;
    readonly ToolStripMenuItem printScreenItem;
    readonly ToolStripMenuItem recordItem;
    // Read by the keyboard hook thread.
    volatile Overlay overlay;
    volatile HistoryPanel history;
    Toast toast;
    Recording recording;
    Scrolling scrolling;

    sealed class Scrolling
    {
        public ScrollCapture Capture;
        public RecordingBar Bar;
    }

    sealed class Recording
    {
        public Recorder Recorder;
        public RecordingFrame Frame;
        public RecordingBar Bar;
    }

    public SnipApp()
    {
        invoker.CreateControl();
        _ = invoker.Handle;

        autostartItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => SetAutostart(!autostartItem.Checked));
        printScreenItem = new ToolStripMenuItem("Use the Print Screen key", null, (_, _) => SetPrintScreen(!printScreenItem.Checked));
        var menu = new ContextMenuStrip();
        // The menu fades out after a click; freezing the screen at once would catch it.
        menu.Items.Add("Snip  (Win+Shift+S)", null, (_, _) => After(250, () => OpenOverlay(CaptureMode.Screenshot)));
        recordItem = new ToolStripMenuItem("Record video", null, (_, _) => { if (recording != null) StopRecording(); else After(250, () => OpenOverlay(CaptureMode.Video)); });
        menu.Items.Add(recordItem);
        menu.Items.Add("Screenshot history  (Win+Shift+A)", null, (_, _) => ToggleHistory());
        menu.Items.Add("Open Screenshots folder", null, (_, _) => OpenFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(autostartItem);
        menu.Items.Add(printScreenItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        tray = new NotifyIcon { Icon = LoadIcon(), Text = "Snip (Win+Shift+S)", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Shortcut(); };

        hook = new KeyboardHook();
        hook.WinShiftChords[Keys.S] = () => invoker.BeginInvoke(new Action(Shortcut));
        hook.WinShiftChords[Keys.A] = () => invoker.BeginInvoke(new Action(ToggleHistory));
        hook.KeyDownFilter = key =>
        {
            if (overlay is { } snipping && key is Keys.Escape or Keys.Enter or Keys.Tab)
            {
                invoker.BeginInvoke(new Action(() => snipping.HandleKey(key)));
                return true;
            }
            if (scrolling is { } job && key == Keys.Escape)
            {
                job.Capture.Cancel();
                return true;
            }
            if (history is { } panel && key == Keys.Escape)
            {
                invoker.BeginInvoke(new Action(panel.Dismiss));
                return true;
            }
            return false;
        };
        hook.Start();

        bool firstRun;
        using (var prefs = Registry.CurrentUser.CreateSubKey(PrefsKey))
        {
            SetAutostart(prefs.GetValue("AutostartDisabled") == null);
            SetPrintScreen(prefs.GetValue("PrintScreenDisabled") == null);
            firstRun = prefs.GetValue("Welcomed") == null;
            prefs.SetValue("Welcomed", 1);
        }

        Log.Write($"started pid={Process.GetCurrentProcess().Id} version={Application.ProductVersion} hook=installed");
        if (firstRun)
            tray.ShowBalloonTip(3000, "Snip is running", "Win+Shift+S takes a screenshot or video. Win+Shift+A shows the last 24 hours.", ToolTipIcon.None);
    }

    bool Busy => overlay != null || recording != null || scrolling != null;

    // Win+Shift+S: open the picker, or stop the recording or scrolling capture in progress.
    void Shortcut()
    {
        if (recording != null) StopRecording();
        else if (scrolling != null) scrolling.Capture.Stop();
        else OpenOverlay(CaptureMode.Screenshot);
    }

    void OpenOverlay(CaptureMode mode)
    {
        if (Busy) return;
        history?.Dismiss();
        toast?.Close();
        overlay = Overlay.FromScreen(mode);
        overlay.Selected += (chosen, area, crop) =>
        {
            Log.Write($"overlay selected mode={chosen}");
            if (chosen == CaptureMode.Screenshot) { Deliver(crop, null); return; }
            crop.Dispose();
            if (area.Width < Recorder.MinSide || area.Height < Recorder.MinSide)
                tray.ShowBalloonTip(4000, "Area too small", $"Pick an area at least {Recorder.MinSide} pixels on each side.", ToolTipIcon.Info);
            else if (chosen == CaptureMode.Video) StartRecording(area);
            else StartScrolling(area);
        };
        overlay.FormClosed += (_, _) => overlay = null;
        overlay.Show();
    }

    // Not while a scrolling capture runs: the panel would sit in its frames and catch its wheel.
    void ToggleHistory()
    {
        if (overlay != null || scrolling != null) return;
        if (history != null) { history.Dismiss(); return; }
        toast?.Close();
        history = new HistoryPanel(Folder, Native.GetForegroundWindow());
        history.FormClosed += (_, _) => history = null;
        history.Show();
    }

    // Copies first, so a full disk or a missing folder still leaves the capture on the clipboard.
    void Deliver(Bitmap capture, string note)
    {
        try
        {
            bool copied = true;
            try { Clipboard.SetDataObject(capture, true, 10, 50); }
            catch (Exception e) { copied = false; Log.Write("clipboard failed: " + e.Message); }

            string path = null;
            try
            {
                path = UniquePath(Folder, "Screenshot", ".png");
                capture.Save(path, ImageFormat.Png);
                Log.Write($"saved {capture.Width}x{capture.Height} {path}");
            }
            catch (Exception e)
            {
                Log.Write("save failed: " + e);
                path = null;
            }

            if (!copied && path == null)
            {
                tray.ShowBalloonTip(5000, "Capture failed", "Snip could not copy or save the capture. See the log for details.", ToolTipIcon.Error);
                return;
            }
            string title = copied ? "Copied to clipboard" : "Saved, but not copied";
            string body = path == null ? "Could not save the file." : "Saved to Screenshots. Click to open.";
            if (note != null) body = $"{note} {body}";
            ShowToast(new Toast(capture, path, title, body));
        }
        finally { capture.Dispose(); }
    }

    // Snipping Tool names files by the second; a second capture in the same second gets " (2)".
    static string UniquePath(string folder, string prefix, string extension)
    {
        Directory.CreateDirectory(folder);
        string stem = $"{prefix} {DateTime.Now:yyyy-MM-dd HHmmss}";
        var path = Path.Combine(folder, stem + extension);
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{stem} ({i}){extension}");
        return path;
    }

    void ShowToast(Toast next)
    {
        toast?.Close();
        toast = next;
        next.FormClosed += (_, _) => { if (toast == next) toast = null; };
        next.Show();
    }

    void StartRecording(Rectangle area)
    {
        string path;
        try { path = UniquePath(RecordingsFolder, "Screen Recording", ".mp4"); }
        catch (Exception e)
        {
            Log.Write("recording folder failed: " + e);
            tray.ShowBalloonTip(5000, "Recording failed", e.Message, ToolTipIcon.Error);
            return;
        }
        var recorder = new Recorder(area, path);
        var current = new Recording
        {
            Recorder = recorder,
            Frame = new RecordingFrame(area),
            Bar = RecordingBar.ForVideo(area, recorder),
        };
        recording = current;
        current.Recorder.Finished += result => invoker.BeginInvoke(new Action(() => FinishRecording(current, result)));
        current.Bar.StopClicked += StopRecording;
        recorder.IgnoreClicksIn = current.Bar.Bounds;
        current.Frame.Show();
        current.Bar.Show();
        recordItem.Text = "Stop recording  (Win+Shift+S)";
        Log.Write($"recording bar={current.Bar.Bounds}");

        // Give the selection overlay time to leave the screen before the first frame.
        After(200, current.Recorder.Start);
    }

    static void After(int ms, Action action)
    {
        var delay = new System.Windows.Forms.Timer { Interval = ms };
        delay.Tick += (_, _) => { delay.Dispose(); action(); };
        delay.Start();
    }

    void StartScrolling(Rectangle area)
    {
        var capture = new ScrollCapture(area);
        var job = new Scrolling { Capture = capture, Bar = RecordingBar.ForScrolling(area, () => capture.Status) };
        scrolling = job;
        job.Bar.StopClicked += capture.Stop;
        capture.Finished += (image, note) => invoker.BeginInvoke(new Action(() =>
        {
            scrolling = null;
            job.Bar.Close();
            if (image != null) Deliver(image, note);
            else if (note != null) tray.ShowBalloonTip(5000, "Scrolling capture failed", note, ToolTipIcon.Error);
        }));
        job.Bar.Show();

        After(200, capture.Start);
    }

    void StopRecording()
    {
        if (recording == null) return;
        recording.Recorder.Stop();
        recording.Frame.Close();
        recording.Bar.Close();
    }

    void FinishRecording(Recording finished, RecordingResult result)
    {
        if (recording == finished) recording = null;
        finished.Frame.Close();
        finished.Bar.Close();
        recordItem.Text = "Record video";
        try
        {
            if (result.Error != null)
            {
                tray.ShowBalloonTip(5000, "Recording failed", result.Error.Message, ToolTipIcon.Error);
                return;
            }
            if (result.Cancelled || result.LastFrame == null) return;

            bool copied = true;
            try { Clipboard.SetFileDropList(new StringCollection { result.Path }); }
            catch (Exception e) { copied = false; Log.Write("clipboard failed: " + e.Message); }

            var length = $"{(int)result.Duration.TotalMinutes}:{result.Duration.Seconds:00}";
            ShowToast(new Toast(result.LastFrame, result.Path, $"Recording saved  ·  {length}",
                copied ? "Copied as a file. Click to open." : "Saved to Screen Recordings. Click to open."));
        }
        finally { result.LastFrame?.Dispose(); }
    }

    void OpenFolder()
    {
        Directory.CreateDirectory(Folder);
        Process.Start(new ProcessStartInfo(Folder) { UseShellExecute = true });
    }

    void SetAutostart(bool enabled)
    {
        // A copy run straight from a zip or the browser's temp folder would vanish, leaving a
        // startup entry that points nowhere.
        bool temporary = Native.LongPath(Application.ExecutablePath)
            .StartsWith(Native.LongPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase);
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (enabled && !temporary) run.SetValue("Snip", $"\"{Application.ExecutablePath}\"");
            else if (!enabled) run.DeleteValue("Snip", false);
        }
        using (var prefs = Registry.CurrentUser.CreateSubKey(PrefsKey))
        {
            if (enabled) prefs.DeleteValue("AutostartDisabled", false);
            else prefs.SetValue("AutostartDisabled", 1);
        }
        autostartItem.Checked = enabled;
    }

    // When off, Print Screen goes back to Windows (or whichever tool owns it).
    void SetPrintScreen(bool enabled)
    {
        hook.PrintScreen = enabled ? () => invoker.BeginInvoke(new Action(Shortcut)) : null;
        using (var prefs = Registry.CurrentUser.CreateSubKey(PrefsKey))
        {
            if (enabled) prefs.DeleteValue("PrintScreenDisabled", false);
            else prefs.SetValue("PrintScreenDisabled", 1);
        }
        printScreenItem.Checked = enabled;
    }

    static Icon LoadIcon()
    {
        using var stream = typeof(SnipApp).Assembly.GetManifestResourceStream("Snip.Snip.ico");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    protected override void ExitThreadCore()
    {
        recording?.Recorder.StopAndWait();
        scrolling?.Capture.Stop();
        tray.Visible = false;
        tray.Dispose();
        base.ExitThreadCore();
    }
}
