using System.Globalization;
using Anela.Heblo.Adapters.Flexi.Common;
using Anela.Heblo.Application.Domain.Catalog.ConsumedMaterials;

namespace Anela.Heblo.Adapters.Flexi.Materials;

public class ConsumedMaterialsFlexiDtoProfile : BaseFlexiProfile
{
    public ConsumedMaterialsFlexiDtoProfile()
    {
        CreateMap<ConsumedMaterialsFlexiDto, ConsumedMaterialRecord>()
            .ForMember(dest => dest.Date, opt => opt.MapFrom(src => 
                DateTime.SpecifyKind(DateTime.Parse(src.Date, CultureInfo.InvariantCulture), DateTimeKind.Unspecified)));
        // Other properties map directly, DateTime conversion handled by BaseFlexiProfile
    }
}