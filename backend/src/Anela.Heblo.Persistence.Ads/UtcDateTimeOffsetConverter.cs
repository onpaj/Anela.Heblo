using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Anela.Heblo.Persistence.Ads;

/// <summary>
/// Npgsql only writes DateTimeOffset values with a zero offset to timestamptz. Ad platforms report
/// local offsets, so every DateTimeOffset in the ads schema is normalised to the same instant in UTC.
/// </summary>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    public UtcDateTimeOffsetConverter()
        : base(value => value.ToUniversalTime(), value => value)
    {
    }
}
