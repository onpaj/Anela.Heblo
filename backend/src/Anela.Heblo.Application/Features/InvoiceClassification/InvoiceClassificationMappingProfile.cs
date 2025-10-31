using AutoMapper;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

namespace Anela.Heblo.Application.Features.InvoiceClassification;

public class InvoiceClassificationMappingProfile : Profile
{
    public InvoiceClassificationMappingProfile()
    {
        CreateMap<ClassificationRule, ClassificationRuleDto>()
            .ForMember(dest => dest.RuleTypeIdentifier, opt => opt.MapFrom(src => src.RuleTypeIdentifier));
        CreateMap<ClassificationHistory, ClassificationHistoryDto>();
        CreateMap<ClassificationStatistics, ClassificationStatisticsDto>();
        CreateMap<RuleUsageStatistic, RuleUsageStatisticDto>();
    }
}