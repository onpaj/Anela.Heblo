using AutoMapper;

namespace Anela.Heblo.Adapters.Flexi.Common;

/// <summary>
/// AutoMapper type converter that preserves DateTime values without timezone conversion.
/// FlexiBee returns dates in Prague timezone, so we keep them as Unspecified to avoid 
/// timezone conversion issues between local development and UTC build servers.
/// </summary>
public class DateTimeLocalKindConverter : ITypeConverter<DateTime, DateTime>
{
    public DateTime Convert(DateTime source, DateTime destination, ResolutionContext context)
    {
        // Keep as Unspecified to avoid timezone conversion - FlexiBee data is already in Prague time
        return DateTime.SpecifyKind(source, DateTimeKind.Unspecified);
    }
}