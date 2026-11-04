using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevTools.App;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DevTools.Tests
{
    internal class MemoryStorage : IStorage
    {
        private readonly Dictionary<string, object> files = new();

        public T Load<T>(string fileName) where T : new()
        {
            if (Path.GetFileName(fileName) == ".app")
                return new JsonStorage().Load<T>(fileName);

            return files.TryGetValue(Path.GetFileName(fileName), out var value) ? (T)value : new T();
        }

        public void Save<T>(string fileName, T obj) where T : new()
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
    }
}
