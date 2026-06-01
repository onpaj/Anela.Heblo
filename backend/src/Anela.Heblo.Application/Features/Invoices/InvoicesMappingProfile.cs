using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;

namespace Anela.Heblo.Application.Features.Invoices;

/// <summary>
/// AutoMapper profile for Invoice entities and DTOs
/// </summary>
public class InvoicesMappingProfile : Profile
{
    public InvoicesMappingProfile()
    {
        // IssuedInvoice mappings
        CreateMap<IssuedInvoice, IssuedInvoiceDto>()
            .ForMember(dest => dest.IsCriticalError, opt => opt.MapFrom(src => src.IsCriticalError));

        CreateMap<IssuedInvoice, IssuedInvoiceDetailDto>()
            .ForMember(dest => dest.IsCriticalError, opt => opt.MapFrom(src => src.IsCriticalError))
            .ForMember(dest => dest.SyncHistory, opt => opt.MapFrom(src => src.SyncHistory));

        // IssuedInvoiceSyncData mappings
        CreateMap<IssuedInvoiceSyncData, IssuedInvoiceSyncDataDto>();

        // IssuedInvoiceError mappings
        CreateMap<IssuedInvoiceError, IssuedInvoiceErrorDto>();

        // Domain entity mappings
        CreateMap<IssuedInvoiceDetail, IssuedInvoice>()
            .ForMember(m => m.Id, e => e.MapFrom(f => f.Code))
            .ForMember(m => m.Price, e => e.MapFrom(f => f.Price.WithVat))
            .ForMember(m => m.Currency, e => e.MapFrom(f => f.Price.CurrencyCode))
            .ForMember(m => m.InvoiceDate, e => e.MapFrom(f => f.CreationTime))
            .ForMember(m => m.CustomerName, e => e.MapFrom(f => f.Customer.DisplayName));

        // DTO mappings for compatibility - remove duplicate mapping as it's already defined above
        // CreateMap<IssuedInvoice, IssuedInvoiceDto>()
        //     .ForMember(m => m.SyncHistoryCount, e => e.MapFrom(f => f.SyncHistoryCount))
        //     .ForMember(m => m.BillingMethod, e => e.MapFrom(f => f.BillingMethod.ToString()))
        //     .ForMember(m => m.ShippingMethod, e => e.MapFrom(f => f.ShippingMethod.ToString()))
        //     .ForMember(m => m.SyncDate, e => e.MapFrom(f => f.LastSyncTime));

        CreateMap<ImportInvoiceRequestDto, IssuedInvoiceSourceQuery>();
    }
}