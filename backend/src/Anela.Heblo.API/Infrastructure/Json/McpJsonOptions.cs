using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anela.Heblo.API.Infrastructure.Json;

public static class McpJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
