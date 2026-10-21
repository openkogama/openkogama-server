using System.Text.Json;

namespace OpenKogama.World;

public static class RegamaFile
{
    public static readonly string[] Planet = ["editBuffer", "playBuffer"];
    public static readonly string[] Avatar = ["playBuffer", "originalBuffer", "shopBuffer", "buffer"];
    public static readonly string[] Model = ["playBuffer", "shopBuffer", "buffer"];

    static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public static (string? Name, Snapshot Snapshot)? Read(byte[] file, string[] fields)
    {
        if (KgmapFile.IsKgmap(file)) return null;
        ReadOnlyMemory<byte> text = file.AsMemory(file.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0);
        if (text.Span.TrimStart(" \t\r\n"u8) is not [(byte)'{', ..])
            return WorldSerializer.ReadExact(file, runtime: false) is Snapshot raw ? (null, raw) : null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string? name = root.TryGetProperty("name", out JsonElement title) && title.ValueKind == JsonValueKind.String
                && title.GetString() is string given && !string.IsNullOrWhiteSpace(given) ? given.Trim() : null;
            foreach (string field in fields)
                if (root.TryGetProperty(field, out JsonElement value) && Bytes(value) is byte[] data
                    && WorldSerializer.ReadExact(data, runtime: false) is Snapshot snapshot)
                    return (name, snapshot);
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static byte[]? Bytes(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.TryGetBytesFromBase64(out byte[]? bytes) ? bytes : null,
        JsonValueKind.Array => value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Number && item.TryGetByte(out _))
            ? [.. value.EnumerateArray().Select(item => item.GetByte())]
            : null,
        JsonValueKind.Object when value.TryGetProperty("$binary", out JsonElement binary) =>
            Bytes(binary.ValueKind == JsonValueKind.Object && binary.TryGetProperty("base64", out JsonElement base64) ? base64 : binary),
        JsonValueKind.Object when value.TryGetProperty("data", out JsonElement data) => Bytes(data),
        _ => null,
    };
}
