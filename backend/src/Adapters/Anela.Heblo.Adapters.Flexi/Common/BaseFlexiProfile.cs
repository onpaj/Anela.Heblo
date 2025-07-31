using AutoMapper;

namespace Anela.Heblo.Adapters.Flexi.Common;

/// <summary>
/// Base AutoMapper profile that sets up common DateTime handling for all Flexi adapters.
/// Ensures FlexiBee DateTime values remain as Unspecified to avoid timezone conversion issues.
/// </summary>
public abstract class BaseFlexiProfile : Profile
{
    protected BaseFlexiProfile()
    {
        // Global DateTime converter - preserves FlexiBee dates without timezone conversion
        CreateMap<DateTime, DateTime>().ConvertUsing<DateTimeLocalKindConverter>();
    }
}