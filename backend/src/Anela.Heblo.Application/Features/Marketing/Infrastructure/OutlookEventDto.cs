using System.Globalization;
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Marketing.Infrastructure
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

        [JsonPropertyName("isAllDay")]
        public bool IsAllDay { get; set; }

        [JsonPropertyName("categories")]
        public string[] Categories { get; set; } = Array.Empty<string>();

        public DateTime StartUtc => ToUtc(Start);

        public DateTime EndUtc => ToUtc(End);

        /// <summary>
        /// Graph sends <c>dateTime</c> as a zone-less wall-clock string and names the zone in the
        /// sibling <c>timeZone</c> field, so parsing alone yields <see cref="DateTimeKind.Unspecified"/>.
        /// Read requests ask for UTC via a <c>Prefer: outlook.timezone</c> header, so a zone-less
        /// value is designated UTC; a value that does carry an offset is converted rather than
        /// relabelled, or the instant would be wrong by that offset.
        /// </summary>
        private static DateTime ToUtc(GraphEventDateTime? value)
        {
            if (value is null)
            {
                return DateTime.MinValue;
            }

            var parsed = DateTime.Parse(
                value.DateTimeString,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);

            return parsed.Kind switch
            {
                DateTimeKind.Utc => parsed,
                DateTimeKind.Local => parsed.ToUniversalTime(),
                _ => DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            };
        }
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

}
