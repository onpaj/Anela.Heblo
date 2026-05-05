using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;

public class GenerateLeafletResponse : BaseResponse
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}
