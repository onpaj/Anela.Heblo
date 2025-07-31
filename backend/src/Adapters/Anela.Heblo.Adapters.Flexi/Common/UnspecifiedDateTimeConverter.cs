using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Anela.Heblo.Adapters.Flexi.Common;

/// <summary>
/// Custom JSON converter that ensures DateTime values are deserialized as DateTimeKind.Unspecified
/// to avoid timezone conversion issues between local development and UTC build servers.
/// </summary>
public class UnspecifiedDateTimeConverter : IsoDateTimeConverter
{
    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        var dateTime = (DateTime)base.ReadJson(reader, objectType, existingValue, serializer);
        return DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
    }
}