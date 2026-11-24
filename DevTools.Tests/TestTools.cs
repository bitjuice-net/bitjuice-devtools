using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevTools.Catalog.Models;
using Xunit;

namespace DevTools.Tests;

internal static class TestTools
{
    public static string CaptureConsole(Action action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return writer.ToString();
    }

    public static string CaptureError(Action action)
    {
        var original = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }
        return writer.ToString();
    }

    // Unix permissions do not exist on Windows, there is nothing to check.
    public static void AssertExecutable(string fileName)
    {
        if (OperatingSystem.IsWindows())
            return;

        Assert.True(File.GetUnixFileMode(fileName).HasFlag(UnixFileMode.UserExecute), $"{fileName} is not executable");
    }
}
