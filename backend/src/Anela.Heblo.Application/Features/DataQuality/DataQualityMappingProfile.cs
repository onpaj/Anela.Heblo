using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;
using AutoMapper;

namespace Anela.Heblo.Application.Features.DataQuality;

public class DataQualityMappingProfile : Profile
{
    public DataQualityMappingProfile()
    {
        CreateMap<DqtRun, DqtRunDto>()
            .ForMember(dest => dest.TestType, opt => opt.MapFrom(src => src.TestType.ToString()))
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status.ToString()))
            .ForMember(dest => dest.TriggerType, opt => opt.MapFrom(src => src.TriggerType.ToString()));

        CreateMap<InvoiceDqtResult, InvoiceDqtResultDto>()
            .ForMember(dest => dest.MismatchType, opt => opt.MapFrom(src => (int)src.MismatchType))
            .ForMember(dest => dest.MismatchFlags, opt => opt.MapFrom(src => GetMismatchFlags(src.MismatchType)));
    }

    private static List<string> GetMismatchFlags(InvoiceMismatchType mismatchType)
    {
        var flags = new List<string>();
        foreach (InvoiceMismatchType flag in Enum.GetValues<InvoiceMismatchType>())
        {
            if (flag == InvoiceMismatchType.None) continue;
            if (mismatchType.HasFlag(flag))
                flags.Add(flag.ToString());
        }
        return flags;
    }
}
