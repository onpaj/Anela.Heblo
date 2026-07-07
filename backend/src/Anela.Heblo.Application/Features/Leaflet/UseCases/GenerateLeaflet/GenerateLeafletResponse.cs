using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;

public class GenerateLeafletResponse : BaseResponse
{
    public GenerateLeafletResponse() { }

    public GenerateLeafletResponse(ErrorCodes errorCode, Dictionary<string, string>? details = null)
        : base(errorCode, details) { }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public Guid? Id { get; set; }

    [JsonPropertyName("kbSourceCount")]
    public int KbSourceCount { get; set; }

    [JsonPropertyName("leafletSourceCount")]
    public int LeafletSourceCount { get; set; }
}
