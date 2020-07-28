using System;
using System.Collections.Generic;

namespace DevTools.App
{
    public class EnvsBuilder
    {
        private readonly List<string> envs = new List<string>();
        private readonly string basePath;

        public EnvsBuilder(string basePath)
        {
            this.basePath = basePath;
        }

        public void AddApplication(ToolDefinition version)
        {
            if (version == null)
                throw new ArgumentNullException(nameof(version));

            var appPath = PathEx.GetRootedPath(version.Path, basePath);

            if(version.Manifest.Envs == null)
                return;

            foreach (var (key, value) in version.Manifest.Envs)
            {
                var path = Environment.ExpandEnvironmentVariables(value);
                path = PathEx.GetRootedPath(path, appPath);
                envs.Add($"{key}={path}");
            }
        }

        public string Build()
        {
            return string.Join(Environment.NewLine, envs);
        }
    }
}