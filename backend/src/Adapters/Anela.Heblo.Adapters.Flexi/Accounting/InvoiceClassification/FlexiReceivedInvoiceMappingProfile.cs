using AutoMapper;
using Anela.Heblo.Adapters.Flexi.Common;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Rem.FlexiBeeSDK.Model.Invoices;

namespace Anela.Heblo.Adapters.Flexi.InvoiceClassification;

public class FlexiReceivedInvoiceMappingProfile : BaseFlexiProfile
{
    public FlexiReceivedInvoiceMappingProfile()
    {
        CreateMap<ReceivedInvoiceFlexiDto, ReceivedInvoiceDto>()
            .ForMember(dest => dest.InvoiceNumber, opt => opt.MapFrom(src => src.Code))
            .ForMember(dest => dest.CompanyName, opt => opt.MapFrom(src => src.CompanyName))
            .ForMember(dest => dest.CompanyVat, opt => opt.MapFrom(src => src.CompanyId))
            .ForMember(dest => dest.InvoiceDate, opt => opt.MapFrom(src => src.IssueDate))
            .ForMember(dest => dest.DueDate, opt => opt.MapFrom(src => src.DueDate))
            .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
            .ForMember(dest => dest.TotalAmount, opt => opt.MapFrom(src => (decimal)src.TotalAmount))
            .ForMember(dest => dest.DepartmentCode, opt => opt.MapFrom(src => src.Department != null ? src.Department.Code : null))
            .ForMember(dest => dest.AccountingTemplateCode, opt => opt.MapFrom(src => src.AccountingTemplate != null ? src.AccountingTemplate.Code : null))
            .ForMember(dest => dest.Labels, opt => opt.MapFrom(src => src.Labels.Split(",", StringSplitOptions.RemoveEmptyEntries)))
            .ForMember(dest => dest.Items, opt => opt.MapFrom(src => src.Items));

        CreateMap<ReceivedInvoiceDetailFlexiDto, ReceivedInvoiceDto>()
            .ForMember(dest => dest.InvoiceNumber, opt => opt.MapFrom(src => src.Code))
            .ForMember(dest => dest.CompanyName, opt => opt.MapFrom(src => src.CompanyName))
            .ForMember(dest => dest.CompanyVat, opt => opt.MapFrom(src => src.CompanyId))
            .ForMember(dest => dest.InvoiceDate, opt => opt.MapFrom(src => src.DateCreated))
            .ForMember(dest => dest.DueDate, opt => opt.MapFrom(src => src.DateDue))
            .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
            .ForMember(dest => dest.TotalAmount, opt => opt.MapFrom(src => (decimal)src.SumTotal))
            .ForMember(dest => dest.Items, opt => opt.MapFrom(src => src.Items));


        
        CreateMap<ReceivedInvoiceItemFlexiDto, ReceivedInvoiceItemDto>()
            .ForMember(dest => dest.Code, opt => opt.MapFrom(src => src.Code))
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
            .ForMember(dest => dest.Amount, opt => opt.MapFrom(src => src.Amount));
    }
}