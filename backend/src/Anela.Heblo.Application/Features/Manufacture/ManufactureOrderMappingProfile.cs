using AutoMapper;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;

namespace Anela.Heblo.Application.Features.Manufacture;

public class ManufactureOrderMappingProfile : Profile
{
    public ManufactureOrderMappingProfile()
    {
        CreateMap<ManufactureOrder, ManufactureOrderDto>()
            .ForMember(dest => dest.StateDisplayName, opt => opt.MapFrom(src => GetStateDisplayName(src.State)));

        CreateMap<ManufactureOrderSemiProduct, ManufactureOrderSemiProductDto>();
        
        CreateMap<ManufactureOrderProduct, ManufactureOrderProductDto>();
        
        CreateMap<ManufactureOrderNote, ManufactureOrderNoteDto>();
        
        CreateMap<ManufactureOrderAuditLog, ManufactureOrderAuditLogDto>()
            .ForMember(dest => dest.ActionDisplayName, opt => opt.MapFrom(src => GetAuditActionDisplayName(src.Action)));
    }

    private static string GetStateDisplayName(ManufactureOrderState state)
    {
        return state switch
        {
            ManufactureOrderState.Draft => "Návrh",
            ManufactureOrderState.SemiProductPlanned => "Plánování semi-produktů",
            ManufactureOrderState.SemiProductManufacture => "Výroba semi-produktů",
            ManufactureOrderState.ProductsPlanned => "Plánování produktů",
            ManufactureOrderState.ProductsManufacture => "Výroba produktů",
            ManufactureOrderState.Completed => "Dokončeno",
            ManufactureOrderState.Cancelled => "Zrušeno",
            _ => state.ToString()
        };
    }

    private static string GetAuditActionDisplayName(ManufactureOrderAuditAction action)
    {
        return action switch
        {
            ManufactureOrderAuditAction.StateChanged => "Změna stavu",
            ManufactureOrderAuditAction.QuantityChanged => "Změna množství",
            ManufactureOrderAuditAction.DateChanged => "Změna data",
            ManufactureOrderAuditAction.ResponsiblePersonAssigned => "Přiřazení odpovědné osoby",
            ManufactureOrderAuditAction.NoteAdded => "Přidání poznámky",
            ManufactureOrderAuditAction.OrderCreated => "Vytvoření zakázky",
            _ => action.ToString()
        };
    }
}