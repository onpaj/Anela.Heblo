using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    public class OutlookEventDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public GraphEventBody? Body { get; set; }

        public string? BodyText => Body?.Content;

        [JsonPropertyName("start")]
        public GraphEventDateTime? Start { get; set; }

        [JsonPropertyName("end")]
        public GraphEventDateTime? End { get; set; }

        [JsonPropertyName("categories")]
        public string[] Categories { get; set; } = Array.Empty<string>();

        public DateTime StartUtc => Start is not null
            ? DateTime.Parse(Start.DateTimeString, null, System.Globalization.DateTimeStyles.RoundtripKind)
            : DateTime.MinValue;

        public DateTime EndUtc => End is not null
            ? DateTime.Parse(End.DateTimeString, null, System.Globalization.DateTimeStyles.RoundtripKind)
            : DateTime.MinValue;
    }

    public class GraphEventBody
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("contentType")]
        public string ContentType { get; set; } = "text";
    }

    public class GraphEventDateTime
    {
        [JsonPropertyName("dateTime")]
        public string DateTimeString { get; set; } = string.Empty;

        [JsonPropertyName("timeZone")]
        public string TimeZone { get; set; } = string.Empty;
    }

    internal class GraphEventCollection
    {
        [JsonPropertyName("value")]
        public List<OutlookEventDto> Value { get; set; } = new();
    }

    internal class OutlookEventIdPayload
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }
}
