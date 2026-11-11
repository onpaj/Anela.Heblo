using System.Text.Json;

namespace Anela.Heblo.Domain.Features.Smartsupp;

public class SmartsuppAttachment
{
    public string Type { get; set; } = "file";
    public string FileName { get; set; } = "";
    public string? FileType { get; set; }
    public string Url { get; set; } = "";
    public long? Size { get; set; }
    public string? ThumbUrl { get; set; }
}

/// <summary>
/// Reads and writes SmartsuppMessage.AttachmentsJson, a JSON array in Smartsupp's own
/// snake_case attachment format. Legacy rows (Smartsupp's raw array) share this format.
/// </summary>
public static class SmartsuppAttachments
{
    private const string UploadContentType = "upload";
    private static readonly string[] KnownTypes = { "image", "file" };

    public static string BuildJson(JsonElement? attachments, string? contentType, JsonElement? contentData)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();

            if (attachments is { ValueKind: JsonValueKind.Array } array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    WriteItem(writer, item);
                }
            }

            if (contentType == UploadContentType && contentData is { } data)
            {
                WriteItem(writer, data);
            }

            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public static bool TryParse(string? json, out IReadOnlyList<SmartsuppAttachment> attachments)
    {
        attachments = Array.Empty<SmartsuppAttachment>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var result = new List<SmartsuppAttachment>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var parsed = ParseItem(item);
                if (parsed != null)
                {
                    result.Add(parsed);
                }
            }

            attachments = result;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void WriteItem(Utf8JsonWriter writer, JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var type = GetString(item, "type");
        if (type == null || !KnownTypes.Contains(type))
        {
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("type", type);
        WriteIfPresent(writer, item, "file_name");
        WriteIfPresent(writer, item, "file_type");
        WriteIfPresent(writer, item, "url");
        if (item.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number)
        {
            writer.WritePropertyName("size");
            size.WriteTo(writer);
        }

        if (item.TryGetProperty("thumb_400", out var thumb) && thumb.ValueKind == JsonValueKind.Object)
        {
            writer.WritePropertyName("thumb_400");
            thumb.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static void WriteIfPresent(Utf8JsonWriter writer, JsonElement item, string name)
    {
        var value = GetString(item, name);
        if (value != null)
        {
            writer.WriteString(name, value);
        }
    }

    private static SmartsuppAttachment? ParseItem(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var url = GetString(item, "url");
        if (!IsHttps(url))
        {
            return null;
        }

        string? thumbUrl = null;
        if (item.TryGetProperty("thumb_400", out var thumb) && thumb.ValueKind == JsonValueKind.Object)
        {
            var candidate = GetString(thumb, "url");
            thumbUrl = IsHttps(candidate) ? candidate : null;
        }

        return new SmartsuppAttachment
        {
            Type = GetString(item, "type") == "image" ? "image" : "file",
            FileName = GetString(item, "file_name") ?? "",
            FileType = GetString(item, "file_type"),
            Url = url!,
            Size = item.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number
                && size.TryGetInt64(out var bytes) ? bytes : null,
            ThumbUrl = thumbUrl,
        };
    }

    private static bool IsHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
