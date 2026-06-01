using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using AutoMapper;

namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ManufacturedProductInventoryMappingProfile : Profile
{
    public ManufacturedProductInventoryMappingProfile()
    {
        CreateMap<ManufacturedProductInventoryItem, ManufacturedProductInventoryItemDto>();
        CreateMap<ManufacturedProductInventoryLog, ManufacturedProductInventoryLogDto>();
    }
}
