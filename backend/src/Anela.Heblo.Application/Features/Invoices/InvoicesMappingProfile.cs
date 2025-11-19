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
        CreateMap<IssuedInvoiceError, IssuedInvoiceErrorDto>()
            .ForMember(dest => dest.ErrorType, opt => opt.MapFrom(src => src.ErrorType.ToString()));
    }
}