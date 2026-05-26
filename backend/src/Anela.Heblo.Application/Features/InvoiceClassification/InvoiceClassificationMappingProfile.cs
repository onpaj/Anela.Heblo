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

        CreateMap<ClassificationHistory, ClassificationHistoryDto>()
            .ForMember(dest => dest.InvoiceId, opt => opt.MapFrom(src => src.AbraInvoiceId))
            .ForMember(dest => dest.RuleName, opt => opt.MapFrom(src => src.ClassificationRule != null ? src.ClassificationRule.Name : null));

        CreateMap<ClassificationStatistics, ClassificationStatisticsDto>();
        CreateMap<RuleUsageStatistic, RuleUsageStatisticDto>();

        // Domain → Application contracts (Step A of the InvoiceClassification DTO separation).
        // Source types use their current Domain names; they will be renamed in Task 8
        // and the source side of these maps will be updated then.
        CreateMap<Domain.Features.InvoiceClassification.AccountingTemplateDto,
                  Contracts.AccountingTemplateDto>();
        CreateMap<Domain.Features.InvoiceClassification.ReceivedInvoiceItemDto,
                  Contracts.ReceivedInvoiceItemDto>();
        CreateMap<Domain.Features.InvoiceClassification.ReceivedInvoiceDto,
                  Contracts.ReceivedInvoiceDto>();
    }
}