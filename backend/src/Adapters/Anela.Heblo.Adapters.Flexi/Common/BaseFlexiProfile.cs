using AutoMapper;

namespace Anela.Heblo.Adapters.Flexi.Common;

/// <summary>
/// Base AutoMapper profile that sets up common DateTime handling for all Flexi adapters.
/// Converts FlexiBee DateTime values from Prague timezone to UTC for application consistency.
/// </summary>
public abstract class BaseFlexiProfile : Profile
{
    protected BaseFlexiProfile()
    {
        // Global DateTime converter - converts FlexiBee Prague time to UTC
        CreateMap<DateTime, DateTime>().ConvertUsing<DateTimeLocalKindConverter>();
    }
}