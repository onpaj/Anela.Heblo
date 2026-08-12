using System.Text.Json;

namespace Anela.Heblo.Application.Features.MindMaps.Model;

public static class MindMapJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize(MindMapDocument document) =>
        JsonSerializer.Serialize(document, Options);

    public static MindMapDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<MindMapDocument>(json, Options)
            ?? throw new JsonException("Mind map document deserialized to null.");

    public static MindMapDocument Clone(MindMapDocument document) =>
        Deserialize(Serialize(document));
}
