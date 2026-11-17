using System;
using System.IO;
using DevTools.Management;
using Xunit;

namespace DevTools.Tests;

public class ManifestVariablesTests
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string AppDir = Path.Combine(Path.GetTempPath(), "tools", "go", "1.0.0");

    [Theory]
    [InlineData(@"%USERPROFILE%\go")]
    [InlineData(@"%HOME%\go")]
    [InlineData(@"%userprofile%\go")]
    public void Expand_HomeIsTheUserProfileOnEverySystem(string value)
    {
        Assert.Equal(Path.Combine(Home, "go"), ManifestVariables.Expand(value, AppDir));
    }

    [Fact]
    public void Expand_AppDirAndSystemSeparator()
    {
        Assert.Equal(Path.Combine(AppDir, "bin", "x"), ManifestVariables.Expand(@"%APPDIR%\bin\x", AppDir));
    }

    [Fact]
    public void Expand_OtherVariablesComeFromTheEnvironment()
    {
        Environment.SetEnvironmentVariable("DEVTOOLS_TEST_VARIABLE", "value");
        try
        {
            Assert.Equal("value-x", ManifestVariables.Expand("%DEVTOOLS_TEST_VARIABLE%-x", AppDir));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEVTOOLS_TEST_VARIABLE", null);
        }
    }

    [Fact]
    public void Expand_KeepsUnknownVariables()
    {
        Assert.Equal("%DEVTOOLS_NOT_SET%", ManifestVariables.Expand("%DEVTOOLS_NOT_SET%", AppDir));
    }
}
