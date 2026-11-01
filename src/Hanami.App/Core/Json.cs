using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Hanami.Core;

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static readonly JsonSerializerOptions FileOptions = new(Options) { WriteIndented = true };

    public static JsonNode? ToNode<T>(T value) => JsonSerializer.SerializeToNode(value, Options);

    public static T FromNode<T>(JsonNode? node) =>
        node.Deserialize<T>(Options) ?? throw new JsonException($"Could not read {typeof(T).Name}");

    /// <summary>Recursively merges <paramref name="patch"/> into <paramref name="target"/>.
    /// Objects merge key by key; anything else (values, arrays, null) replaces.</summary>
    public static void DeepMerge(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch)
        {
            if (value is JsonObject patchObj && target[key] is JsonObject targetObj)
                DeepMerge(targetObj, patchObj);
            else
                target[key] = value?.DeepClone();
        }
    }

    /// <summary>Writes via a temp file so a crash mid-write never leaves a truncated file.</summary>
    public static void WriteFileAtomic(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        File.Move(tmp, path, overwrite: true);
    }
}
