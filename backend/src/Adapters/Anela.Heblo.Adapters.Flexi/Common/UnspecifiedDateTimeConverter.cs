using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Anela.Heblo.Adapters.Flexi.Common;

/// <summary>
/// Custom JSON converter that converts FlexiBee DateTime values from local timezone to UTC.
/// FlexiBee returns dates in local timezone, but the application uses UTC internally.
/// The application timezone is configured via TZ environment variable at startup.
/// </summary>
public class UnspecifiedDateTimeConverter : IsoDateTimeConverter
{
    public UnspecifiedDateTimeConverter()
    {
        // IsoDateTimeConverter doesn't have DateTimeZoneHandling property
        // We handle timezone conversion in ReadJson method instead
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            if (objectType == typeof(DateTime?))
                return null;

            throw new JsonSerializationException($"Cannot convert null value to {objectType}.");
        }

        DateTime parsedDateTime;

        if (reader.TokenType == JsonToken.Date)
        {
            parsedDateTime = (DateTime)reader.Value;
        }
        else if (reader.TokenType == JsonToken.String)
        {
            var dateString = reader.Value.ToString();
            if (!DateTime.TryParse(dateString, out parsedDateTime))
            {
                throw new JsonSerializationException($"Cannot parse DateTime from string: {dateString}");
            }
        }
        else
        {
            throw new JsonSerializationException($"Unexpected token {reader.TokenType} when parsing DateTime.");
        }

        // Treat the parsed DateTime as local time and convert to UTC
        // TimeZoneInfo.Local automatically uses the TZ environment variable set at startup
        var localDateTime = DateTime.SpecifyKind(parsedDateTime, DateTimeKind.Unspecified);
        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, TimeZoneInfo.Local);

        return utcDateTime;
    }
}