using System;
using System.IO;

namespace Snip;

// Plain-text diagnostics in %LOCALAPPDATA%\Snip\snip.log. Several threads write here, and the
// file rolls over to snip.old.log at 1 MB so it never grows without bound.
static class Log
{
    const long MaxBytes = 1024 * 1024;
    static readonly object gate = new();
    static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snip");
    static readonly string Current = Path.Combine(Folder, "snip.log");
    static readonly string Previous = Path.Combine(Folder, "snip.old.log");

    public static void Write(string line)
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var info = new FileInfo(Current);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Delete(Previous);
                    File.Move(Current, Previous);
                }
                File.AppendAllText(Current, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}\r\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
