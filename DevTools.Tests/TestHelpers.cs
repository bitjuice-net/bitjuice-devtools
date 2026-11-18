using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevTools.Catalog;
using DevTools.Infrastructure;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DevTools.Tests;

internal class MemoryStorage : IStorage
{
    private readonly Dictionary<string, object> files = new();

    public T Load<T>(string fileName) where T : new()
    {
        if (Path.GetFileName(fileName) is ".app" or ".source")
            return new JsonStorage().Load<T>(fileName);

        return files.TryGetValue(Path.GetFileName(fileName), out var value) ? (T)value : new T();
    }

    public bool Exists(string fileName)
    {
        return files.ContainsKey(Path.GetFileName(fileName));
    }

    public void Save<T>(string fileName, T obj, bool changesState = true) where T : new()
    {
        files[Path.GetFileName(fileName)] = obj;
    }
}

internal static class TestTools
{
    public const string CacheFile = "cache.json";

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
        storage.Save(CacheFile, tools.ToDictionary(i => i.Name, i => i.Versions.Select(v => Definition(i.Name, v)).ToList()));
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
// A clock the test moves by hand.
internal class FakeTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        return Now;
    }
}
