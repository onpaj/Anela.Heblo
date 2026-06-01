using System.Globalization;
using Anela.Heblo.Adapters.Flexi.Common;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;

namespace Anela.Heblo.Adapters.Flexi.Materials;

public class ConsumedMaterialsFlexiDtoProfile : BaseFlexiProfile
{
    public ConsumedMaterialsFlexiDtoProfile()
    {
        var pragueTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");

        CreateMap<ConsumedMaterialsFlexiDto, ConsumedMaterialRecord>()
            .ForMember(dest => dest.Date, opt => opt.MapFrom(src =>
                TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(DateTime.Parse(src.Date, CultureInfo.InvariantCulture), DateTimeKind.Unspecified),
                    pragueTimeZone)));
        // Other properties map directly, DateTime conversion handled by BaseFlexiProfile
    }
}