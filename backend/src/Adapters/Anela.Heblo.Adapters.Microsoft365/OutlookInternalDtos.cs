using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.Marketing.Services;

namespace Anela.Heblo.Adapters.Microsoft365
{
    internal class GraphEventCollection
    {
        [JsonPropertyName("value")]
        public List<OutlookEventDto> Value { get; set; } = new();

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    internal class OutlookEventIdPayload
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }
}
