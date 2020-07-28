using System;
using System.Collections.Generic;

namespace DevTools.App
{
    public class PathBuilder
    {
        private readonly List<string> values = new List<string>();

        public void AddApplication(ToolDefinition version)
        {
            if (version == null) 
                throw new ArgumentNullException(nameof(version));

            if (version.Manifest.Paths == null)
                return;

            Environment.SetEnvironmentVariable("APPDIR", version.Path);

            foreach (var includePath in version.Manifest.Paths) 
                values.Add(Environment.ExpandEnvironmentVariables(includePath));
        }

        public string Build()
        {
            return string.Join(';', values);
        }
    }
}
