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

        CreateMap<AccountingTemplate, AccountingTemplateDto>();
        CreateMap<ReceivedInvoiceItem, ReceivedInvoiceItemDto>();
        CreateMap<ReceivedInvoice, ReceivedInvoiceDto>();
    }
}