using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevTools.Catalog;

// SHA-256 of a .app without its origin. The JSON is written again first, so formatting does not count as a change.
public static class ManifestHash
{
    public const string OriginProperty = "origin";

    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Compute(JsonObject manifest)
    {
        var content = (JsonObject)manifest.DeepClone();
        content.Remove(OriginProperty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToJsonString(Compact))));
    }

    // True when a fetched .app no longer has the content it was fetched with.
    public static bool IsModified(string fileName)
    {
        if (JsonNode.Parse(File.ReadAllText(fileName)) is not JsonObject manifest)
            return true;

        var expected = manifest[OriginProperty]?["sha256"]?.GetValue<string>();
        return expected == null || !string.Equals(expected, Compute(manifest), StringComparison.OrdinalIgnoreCase);
    }
}
