using AutoMapper;

namespace Anela.Heblo.Adapters.Flexi.Common;

/// <summary>
/// AutoMapper type converter that converts FlexiBee DateTime values from local timezone to UTC.
/// FlexiBee returns dates in local timezone, but the application uses UTC internally.
/// The application timezone is configured via TZ environment variable at startup.
/// </summary>
public class DateTimeLocalKindConverter : ITypeConverter<DateTime, DateTime>
{
    public DateTime Convert(DateTime source, DateTime destination, ResolutionContext context)
    {
        // If source is already UTC, return as-is
        if (source.Kind == DateTimeKind.Utc)
            return source;

        // If source is Unspecified or Local, treat as local timezone and convert to UTC
        // TimeZoneInfo.Local automatically uses the TZ environment variable set at startup
        return TimeZoneInfo.ConvertTimeToUtc(source, TimeZoneInfo.Local);
    }
}