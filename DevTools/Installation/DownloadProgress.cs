using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace DevTools.Installation;

// One line rewritten in place, a few times a second.
public class DownloadProgress(long? total, TextWriter output)
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private readonly Stopwatch watch = Stopwatch.StartNew();
    private TimeSpan next = TimeSpan.Zero;
    private int width;

    public void Report(long done, bool finished)
    {
        if (!finished && watch.Elapsed < next)
            return;

        next = watch.Elapsed + Interval;
        var text = total is > 0
            ? $"  {Megabytes(done)} / {Megabytes(total.Value)} MB ({done * 100 / total.Value}%)"
            : $"  {Megabytes(done)} MB";

        output.Write("\r" + text.PadRight(width));
        width = text.Length;

        if (finished)
            output.WriteLine();
    }

    private static string Megabytes(long bytes)
    {
        return (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture);
    }
}
