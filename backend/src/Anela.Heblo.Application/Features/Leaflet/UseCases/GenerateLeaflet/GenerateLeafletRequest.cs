using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;

public class GenerateLeafletRequest : IRequest<GenerateLeafletResponse>
{
    [Required, MinLength(1), MaxLength(200)]
    [JsonPropertyName("topic")]
    public string Topic { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("audience")]
    public AudienceType Audience { get; set; }

    [Required]
    [JsonPropertyName("length")]
    public LeafletLength Length { get; set; }
}
