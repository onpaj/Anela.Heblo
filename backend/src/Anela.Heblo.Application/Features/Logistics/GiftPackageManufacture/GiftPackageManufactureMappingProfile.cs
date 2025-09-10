using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Contracts;
using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using AutoMapper;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture;

public class GiftPackageManufactureMappingProfile : Profile
{
    public GiftPackageManufactureMappingProfile()
    {
        CreateMap<GiftPackageManufactureLog, GiftPackageManufactureDto>()
            .ForMember(dest => dest.ConsumedItems, opt => opt.MapFrom(src => src.ConsumedItems));

        CreateMap<GiftPackageManufactureItem, GiftPackageManufactureItemDto>();
    }
}