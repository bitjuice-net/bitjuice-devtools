using System;
using System.Collections.Generic;
using System.IO;

namespace DevTools.New
{
    public class PathBuilder
    {
        private readonly List<string> paths = new List<string>();
        private readonly string basePath;

        public PathBuilder(string basePath)
        {
            this.basePath = basePath;
        }

        public void AddApplication(ToolDefinition version)
        {
            if (version == null) 
                throw new ArgumentNullException(nameof(version));

            var appPath = PathEx.GetRootedPath(version.Path, basePath);

            foreach (var includePath in version.Manifest.Paths)
            {
                if (Path.IsPathRooted(includePath))
                    paths.Add(includePath);
                paths.Add(PathEx.GetRootedPath(includePath, appPath));
            }
        }

        public string Build()
        {
            return string.Join(';', paths);
        }
    }
}
