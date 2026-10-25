using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Snip;

// One grab of the scrolling area, with a fingerprint per pixel row. Rows are fingerprinted on
// the middle of the area only: the edges can hold a scrollbar, a sliver of whatever window is
// behind, or other things that change on their own.
sealed class Frame
{
    public readonly int Width, Height;
    public readonly int[] Pixels;
    public readonly long[] Rows;

    public Frame(int[] pixels, int width, int height, int hashFrom, int hashTo)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        Rows = new long[height];
        for (int y = 0; y < height; y++)
        {
            ulong h = 14695981039346656037;
            for (int i = y * width + hashFrom; i < y * width + hashTo; i++)
            {
                h ^= (uint)pixels[i];
                h *= 1099511628211;
            }
            Rows[y] = (long)h;
        }
    }

    public int StillRows(Frame other)
    {
        int n = 0;
        for (int y = 0; y < Height; y++)
            if (Rows[y] == other.Rows[y]) n++;
        return n;
    }
}

enum ShiftKind { Still, Matched, Guessed, Lost }

// Builds one tall image from frames grabbed after successive scroll steps.
//
// Each page row is taken from the frame where it sat nearest the middle of the screen: the seam
// between two overlapping frames is halfway through their overlap. Sticky headers, sticky
// footers and fixed corner buttons live at the screen edges, so they only survive from the first
// frame (top) and the last one (bottom), where the page really shows them.
sealed class PageStitcher
{
    const int MaxGuessesInARow = 6;

    readonly int width, height, scrollbarStrip;
    readonly long maxPixels;
    readonly List<int[]> slices = new();
    Frame previous;
    int start, taken, cutRight, guesses, stills;

    public PageStitcher(Frame first, int scrollbarStrip, long maxPixels)
    {
        previous = first;
        width = first.Width;
        height = first.Height;
        this.scrollbarStrip = scrollbarStrip;
        this.maxPixels = maxPixels;
    }

    // Wheel notches to send for the next step, planned from the fastest rate seen so far. The
    // first step is one notch, which leaves overlap even when Windows scrolls a lot per notch.
    public int Notches { get; private set; } = 1;
    public double PixelsPerNotch { get; private set; }

    // Set when the capture stopped before the page did.
    public string Note { get; private set; }

    public int StitchedHeight => taken + height - start;
    public int Frames => slices.Count + 1;

    // True if the frame moved the page and the capture should go on.
    public bool Add(Frame current)
    {
        int offset = Offset(previous, current, PixelsPerNotch, Notches, out var kind);
        // Nothing to match but nearly every row in place: only an animation (a video, a
        // spinner) changed, and the page itself stayed put.
        if (kind == ShiftKind.Lost && previous.StillRows(current) * 10 >= height * 9) kind = ShiftKind.Still;
        // A guess is only worth making if the page did scroll, which moves the scrollbar thumb.
        if (kind == ShiftKind.Guessed && !ScrollbarMoved(previous, current)) kind = ShiftKind.Still;
        switch (kind)
        {
            // A browser now and then drops a whole burst of wheel notches, so the page has to
            // stay put twice before it counts as the end.
            case ShiftKind.Still:
                if (ScrollbarMoved(previous, current))
                {
                    Note = "Stopped at a blank stretch of the page.";
                    return false;
                }
                return ++stills < 2;
            case ShiftKind.Lost:
                Note = "Lost track of the page, so it ends early.";
                return false;
            case ShiftKind.Guessed when ++guesses > MaxGuessesInARow:
                Note = "Stopped after several screens of blank page.";
                return false;
            case ShiftKind.Matched:
                guesses = 0;
                break;
        }
        if (offset < 0)
        {
            Note = "The page scrolled back up, so it ends early.";
            return false;
        }
        if ((long)(StitchedHeight + offset) * width > maxPixels)
        {
            Note = $"Stopped at {StitchedHeight:N0} px, the size limit.";
            return false;
        }

        stills = 0;
        // Browsers sometimes drop a notch from a burst, so plan with the fastest rate seen: a
        // step of under half the screen then always leaves plenty of overlap.
        PixelsPerNotch = Math.Max(PixelsPerNotch, (double)offset / Notches);
        Notches = Math.Max(1, Math.Min(60, (int)(height * 0.45 / PixelsPerNotch)));

        int seam = (height + offset) / 2;
        Take(previous, start, seam);
        start = seam - offset;
        cutRight = Math.Max(cutRight, ScrollbarWidth(previous, current, offset, scrollbarStrip));
        previous = current;
        return true;
    }

    public Bitmap Compose()
    {
        Take(previous, start, height);
        start = height;
        int outWidth = width - cutRight;
        var bitmap = new Bitmap(outWidth, taken, PixelFormat.Format32bppRgb);
        var bits = bitmap.LockBits(new Rectangle(0, 0, outWidth, taken), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            int row = 0;
            for (int i = 0; i < slices.Count; i++)
            {
                for (int y = 0; y < slices[i].Length / width; y++, row++)
                    Marshal.Copy(slices[i], y * width, bits.Scan0 + row * bits.Stride, outWidth);
                slices[i] = null;
            }
        }
        finally { bitmap.UnlockBits(bits); }
        slices.Clear();
        return bitmap;
    }

    public int ScrollbarCut => cutRight;

    void Take(Frame frame, int from, int to)
    {
        var rows = new int[(to - from) * width];
        Array.Copy(frame.Pixels, from * width, rows, 0, rows.Length);
        slices.Add(rows);
        taken += to - from;
    }

    // The page looked the same, but the scrollbar thumb moved: it scrolled over a screen with
    // nothing on it.
    bool ScrollbarMoved(Frame before, Frame after)
    {
        for (int y = 0; y < height; y++)
            for (int x = width - scrollbarStrip; x < width; x++)
                if (before.Pixels[y * width + x] != after.Pixels[y * width + x]) return true;
        return false;
    }

    // True if the page scrolled up: the content matched further down, or nearly every row
    // changed, which a jump too big to overlap does. An animation in part of the page changes
    // some rows but leaves most in place.
    public static bool ScrolledUp(Frame before, Frame after)
    {
        if (before.StillRows(after) * 2 < before.Height) return true;
        int offset = Offset(before, after, 0, 1, out var kind);
        return kind == ShiftKind.Matched && offset < 0;
    }

    // How far the content moved up between frames, negative when it moved down.
    //
    // Each changed row of the new frame votes for the shift that lines it up with an identical
    // row of the old one. Rows that stayed put are fixed UI (a sticky bar, a chat button) and
    // rows that repeat a lot are blank background; neither says anything about the shift. A
    // stretch of blank page leaves no votes; then shifts a whole number of notches long are tried
    // against the rows that did change, the planned step first and then ever further from it,
    // since browsers drop or add a notch now and then.
    public static int Offset(Frame before, Frame after, double pixelsPerNotch, int notches, out ShiftKind kind)
    {
        int height = before.Height;
        var where = new Dictionary<long, List<int>>();
        int changed = 0;
        for (int y = 0; y < height; y++)
        {
            if (before.Rows[y] == after.Rows[y]) continue;
            changed++;
            if (!where.TryGetValue(before.Rows[y], out var list)) where[before.Rows[y]] = list = new List<int>();
            list.Add(y);
        }
        if (changed * 200 < height)
        {
            kind = ShiftKind.Still;
            return 0;
        }

        var votes = new int[height * 2];
        for (int y = 0; y < height; y++)
        {
            if (before.Rows[y] == after.Rows[y]) continue;
            if (!where.TryGetValue(after.Rows[y], out var list) || list.Count > 4) continue;
            foreach (int p in list)
                votes[p - y + height]++;
        }
        int best = 0;
        for (int i = 1; i < votes.Length; i++)
            if (votes[i] > votes[best]) best = i;
        if (votes[best] >= 5)
        {
            kind = ShiftKind.Matched;
            return best - height;
        }

        // Rows the old frame never showed are new content scrolling in, possibly from under a
        // sticky footer. No shift explains them, so they don't count against any.
        var seen = new HashSet<long>(before.Rows);
        for (int distance = 0; distance <= notches + 2; distance++)
        {
            foreach (int k in distance == 0 ? new[] { notches } : new[] { notches + distance, notches - distance })
            {
                int shift = (int)Math.Round(pixelsPerNotch * k);
                if (k < 1 || shift <= 0 || shift >= height) continue;
                int moved = 0, explained = 0;
                for (int y = 0; y + shift < height; y++)
                {
                    if (after.Rows[y] == before.Rows[y] || !seen.Contains(after.Rows[y])) continue;
                    moved++;
                    if (after.Rows[y] == before.Rows[y + shift]) explained++;
                }
                // A shift needs rows that vouch for it and almost none against it. With no
                // rows either way (the old frame was blank, or showed content still fading in),
                // only the planned step is a fair guess.
                if (moved > 0 ? explained * 10 >= moved * 9 : distance == 0)
                {
                    kind = ShiftKind.Guessed;
                    return shift;
                }
            }
        }
        kind = ShiftKind.Lost;
        return 0;
    }

    // A scrollbar column fits "stayed put" better than "moved with the page": only its thumb
    // changes, a little, between frames. Everything from the leftmost such column in the right
    // strip to the edge (window border included) is cut. Only rows where the page moved count;
    // sticky bars stay put across the whole width and would make a plain white column next to
    // the scrollbar look like part of it.
    public static int ScrollbarWidth(Frame before, Frame after, int offset, int strip)
    {
        int width = before.Width, overlap = before.Height - offset;
        if (overlap < 20) return 0;
        for (int x = width - strip; x < width; x++)
        {
            int staticMismatch = 0, scrollMismatch = 0;
            for (int y = 0; y < overlap; y++)
            {
                if (before.Rows[y] == after.Rows[y]) continue;
                int now = after.Pixels[y * width + x];
                if (now != before.Pixels[y * width + x]) staticMismatch++;
                if (now != before.Pixels[(y + offset) * width + x]) scrollMismatch++;
            }
            if (scrollMismatch >= 3 && staticMismatch < scrollMismatch) return width - x;
        }
        return 0;
    }
}
