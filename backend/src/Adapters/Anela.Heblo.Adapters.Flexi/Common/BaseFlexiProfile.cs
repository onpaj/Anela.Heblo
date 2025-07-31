using AutoMapper;

namespace Anela.Heblo.Adapters.Flexi.Common;

/// <summary>
/// Base AutoMapper profile that sets up common DateTime handling for all Flexi adapters
/// </summary>
public abstract class BaseFlexiProfile : Profile
{
    protected BaseFlexiProfile()
    {
        // Global DateTime converter - ensures all DateTime fields use Local kind
        CreateMap<DateTime, DateTime>().ConvertUsing<DateTimeLocalKindConverter>();
    }
}