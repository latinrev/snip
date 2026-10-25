# End-to-end scripts

These PowerShell scripts test Snip the way a person uses it. They press the real shortcuts, move and click the mouse, read the clipboard, and check the files Snip saves and the lines it writes to its log.

**They take over the desktop while they run.** Run them on a test machine or a VM, in a signed-in desktop session, with nothing else open that a stray click could hurt. Each script opens its own topmost window to click on and removes the files it creates.

## Run

1. Build Snip and start it.
2. From a PowerShell window on that desktop:

```powershell
powershell -ExecutionPolicy Bypass -File tests\e2e\run-all.ps1
```

Or run one script, such as `tests\e2e\scroll.ps1`. Each prints `PASS` or `FAIL` per check and exits with code 1 if any check failed.

## Scripts

| Script | What it checks |
| --- | --- |
| `screenshot.ps1` | <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> opens the overlay. A 400x300 drag saves a 400x300 PNG and copies it. <kbd>Esc</kbd> saves nothing. <kbd>Enter</kbd> captures the whole monitor. |
| `history.ps1` | <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>A</kbd> lists exactly the PNGs from the last 24 hours, scrolling loads more cards, <kbd>Esc</kbd> and clicking outside close the panel, and a pick copies the right image. |
| `video.ps1` | Video mode records the monitor at 20 fps or more, <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> stops it, and the MP4 lands on the clipboard as a file. |
| `scroll.ps1` | A scrolling capture of a window with a sticky header and footer matches the source image pixel for pixel. |
| `scroll-browser.ps1` | A scrolling capture of `page.html` in a throwaway Edge InPrivate window reaches the end of the page. It prints the path of the stitched image so you can look at it. |

The scripts need Windows PowerShell 5.1, which Windows includes, and `scroll-browser.ps1` also needs Microsoft Edge.
