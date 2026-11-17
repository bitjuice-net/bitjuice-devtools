using System.IO;
using DevTools.Installation;
using Xunit;

namespace DevTools.Tests;

public class DownloadProgressTests
{
    [Fact]
    public void Report_WritesOneLineThatEndsWithTheTotal()
    {
        var output = new StringWriter();
        var progress = new DownloadProgress(10 * 1024 * 1024, output);

        for (var done = 0L; done <= 10 * 1024 * 1024; done += 1024 * 1024)
            progress.Report(done, false);
        progress.Report(10 * 1024 * 1024, true);

        var text = output.ToString();
        Assert.StartsWith("\r  0.0 / 10.0 MB (0%)", text);
        Assert.EndsWith("\r  10.0 / 10.0 MB (100%)" + output.NewLine, text);
    }

    [Fact]
    public void Report_WithoutTotalShowsMegabytes()
    {
        var output = new StringWriter();

        new DownloadProgress(null, output).Report(3 * 1024 * 1024, true);

        Assert.Equal("\r  3.0 MB" + output.NewLine, output.ToString());
    }
}
