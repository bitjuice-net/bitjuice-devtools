using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevTools.Catalog.Models;
using Xunit;

namespace DevTools.Tests;

internal static class TestTools
{
    public const string CacheFile = "cache/apps.json";

    public static ToolDefinition Definition(string name, string version)
    {
        return new ToolDefinition
        {
            Path = $@"C:\Tools\{name}\{version}",
            Manifest = new ToolManifest { Name = name, Version = version, Paths = new List<string> { "%APPDIR%" } }
        };
    }

    public static MemoryStorage Storage(params (string Name, string[] Versions)[] tools)
    {
        var storage = new MemoryStorage();
        storage.Save(CacheFile, new ScanResult { Apps = new SortedDictionary<string, ScannedApp>(tools.ToDictionary(i => i.Name, i => new ScannedApp { Versions = i.Versions.Select(v => Definition(i.Name, v)).ToList() })) });
        return storage;
    }

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
