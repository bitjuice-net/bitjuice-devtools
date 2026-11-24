using System.Collections.Generic;
using System.Text.Json.Serialization;
using DevTools.Catalog.Models;
using DevTools.Installation.Models;
using DevTools.Sources.Models;

namespace DevTools.Infrastructure;

// Metadata generated at build time, so reading cache/apps.json does not pay for reflection on every start.
[JsonSerializable(typeof(ScanResult))]
[JsonSerializable(typeof(Dictionary<string, ToolSettings>))]
[JsonSerializable(typeof(ToolManifest))]

[JsonSerializable(typeof(SourcesState))]
[JsonSerializable(typeof(RepositoryIndex))]
[JsonSerializable(typeof(CachedVersions))]
internal partial class StorageJsonContext : JsonSerializerContext;
