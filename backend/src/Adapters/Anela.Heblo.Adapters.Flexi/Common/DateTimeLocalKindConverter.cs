using AutoMapper;

namespace Anela.Heblo.Adapters.Flexi.Common;

/// <summary>
/// AutoMapper type converter that ensures all DateTime values are treated as Local kind
/// </summary>
public class DateTimeLocalKindConverter : ITypeConverter<DateTime, DateTime>
{
    public DateTime Convert(DateTime source, DateTime destination, ResolutionContext context)
    {
        return DateTime.SpecifyKind(source, DateTimeKind.Local);
    }
}