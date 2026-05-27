using AutoMapper;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Application.Features.Manufacture.Contracts;

namespace Anela.Heblo.Application.Features.Manufacture;

public class ManufactureOrderMappingProfile : Profile
{
    public ManufactureOrderMappingProfile()
    {
        CreateMap<ManufactureOrder, ManufactureOrderDto>();
        CreateMap<ManufactureOrderSemiProduct, ManufactureOrderSemiProductDto>();
        CreateMap<ManufactureOrderProduct, ManufactureOrderProductDto>();
        CreateMap<ManufactureOrderNote, ManufactureOrderNoteDto>();
        CreateMap<ManufactureOrderConditionsReading, ManufactureOrderConditionsReadingDto>()
            .ForMember(dest => dest.Source, opt => opt.MapFrom(src => (int)src.Source));
        CreateMap<ResidueDistribution, ResidueDistributionDto>();
        CreateMap<ProductConsumptionDistribution, ProductConsumptionDistributionDto>();
    }

}