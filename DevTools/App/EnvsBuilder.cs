using System;
using System.Collections.Generic;

namespace DevTools.App
{
    public class EnvsBuilder
    {
        private readonly List<string> values = new();

        public void AddApplication(ToolDefinition version)
        {
            if (version == null)
                throw new ArgumentNullException(nameof(version));

            if (version.Manifest.Envs == null)
                return;

            Environment.SetEnvironmentVariable("APPDIR", version.Path);

            foreach (var (key, value) in version.Manifest.Envs) 
                values.Add($"{key}={Environment.ExpandEnvironmentVariables(value)}");
        }

        public string Build()
        {
            return string.Join(Environment.NewLine, values);
        }
    }
}