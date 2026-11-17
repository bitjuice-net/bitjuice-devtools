using System;
using System.Collections.Generic;
using System.Linq;

namespace DevTools.Infrastructure;

// Plain columns without a frame, they stay readable when a cell is long and in narrow consoles.
public class TextTable(params string[] headers)
{
    private readonly List<string[]> rows = new();

    public void AddRow(params string[] cells)
    {
        rows.Add(cells);
    }

    public void Write()
    {
        var all = rows.Prepend(headers).ToList();
        var widths = Enumerable.Range(0, headers.Length).Select(i => all.Max(row => (row[i] ?? "").Length)).ToArray();

        foreach (var row in all)
            Console.WriteLine(row.Select((cell, i) => (cell ?? "").PadRight(widths[i])).Join("  ").TrimEnd());
    }
}
