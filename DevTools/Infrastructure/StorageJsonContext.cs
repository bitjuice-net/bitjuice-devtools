using System.Collections.Generic;
using System.Text.Json.Serialization;
using DevTools.Catalog;
using DevTools.Installation;
using DevTools.Sources;

namespace DevTools.Infrastructure;

// Metadata generated at build time, so reading cache.json does not pay for reflection on every start.
[JsonSerializable(typeof(Dictionary<string, List<ToolDefinition>>))]
[JsonSerializable(typeof(Dictionary<string, ToolSettings>))]
[JsonSerializable(typeof(ToolManifest))]
[JsonSerializable(typeof(ToolSource))]
[JsonSerializable(typeof(SourcesState))]
[JsonSerializable(typeof(RepositoryIndex))]
[JsonSerializable(typeof(Dictionary<string, RepositoryIndex>))]
internal partial class StorageJsonContext : JsonSerializerContext;
