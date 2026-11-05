using DevTools.Infrastructure;
using Xunit;

namespace DevTools.Tests;

public class VersionNumberTests
{
    [Theory]
    [InlineData("12.2", "12.2.0")]
    [InlineData("103.0", "103.0.0")]
    [InlineData("12.2.0", "12.2.0")]
    [InlineData("1.6.0.641", "1.6.0.641")]
    [InlineData("latest", "latest")]
    [InlineData("default", "default")]
    [InlineData(null, null)]
    public void Normalize_AddsOnlyMissingThirdPart(string version, string expected)
    {
        Assert.Equal(expected, VersionNumber.Normalize(version));
    }

    [Theory]
    [InlineData("12.2", "12.2.0", true)]
    [InlineData("12.2.0", "12.2", true)]
    [InlineData("12.2", "12.2", true)]
    [InlineData("12.2", "12.2.1", false)]
    [InlineData("latest", "latest", true)]
    [InlineData("latest", "1.0.0", false)]
    public void AreEqual_TreatsMissingThirdPartAsZero(string first, string second, bool expected)
    {
        Assert.Equal(expected, VersionNumber.AreEqual(first, second));
    }
}
