using AutoMapper;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;

namespace Anela.Heblo.Application.Features.Manufacture;

public class ManufactureOrderMappingProfile : Profile
{
    public ManufactureOrderMappingProfile()
    {
        CreateMap<ManufactureOrder, ManufactureOrderDto>();
        CreateMap<ManufactureOrderSemiProduct, ManufactureOrderSemiProductDto>();
        CreateMap<ManufactureOrderProduct, ManufactureOrderProductDto>();
        CreateMap<ManufactureOrderNote, ManufactureOrderNoteDto>();
        CreateMap<ManufactureOrderAuditLog, ManufactureOrderAuditLogDto>();
    }

}