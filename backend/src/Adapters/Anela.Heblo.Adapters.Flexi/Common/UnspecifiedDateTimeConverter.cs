using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Anela.Heblo.Adapters.Flexi.Common;

/// <summary>
/// Custom JSON converter that ensures DateTime values are deserialized as DateTimeKind.Unspecified
/// to avoid timezone conversion issues between local development and UTC build servers.
/// FlexiBee returns dates in Prague timezone, so we preserve them without conversion.
/// </summary>
public class UnspecifiedDateTimeConverter : IsoDateTimeConverter
{
    public UnspecifiedDateTimeConverter()
    {
        // IsoDateTimeConverter doesn't have DateTimeZoneHandling property
        // We handle timezone in ReadJson method instead
    }
    
    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            if (objectType == typeof(DateTime?))
                return null;
            
            throw new JsonSerializationException($"Cannot convert null value to {objectType}.");
        }

        if (reader.TokenType == JsonToken.Date)
        {
            var dateTime = (DateTime)reader.Value;
            return DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
        }

        if (reader.TokenType == JsonToken.String)
        {
            var dateString = reader.Value.ToString();
            if (DateTime.TryParse(dateString, out var parsedDate))
            {
                return DateTime.SpecifyKind(parsedDate, DateTimeKind.Unspecified);
            }
        }

        throw new JsonSerializationException($"Unexpected token {reader.TokenType} when parsing DateTime.");
    }
}