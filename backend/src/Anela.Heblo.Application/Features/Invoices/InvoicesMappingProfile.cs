using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Xcc.Application.Dtos;
using AutoMapper;
using Rem.FlexiBeeSDK.Model.Invoices;
using Rem.FlexiBeeSDK.Model;
using Rem.FlexiBeeSDK.Model.Response;

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

        // FlexiBee mappings
        CreateMap<IssuedInvoiceDetail, IssuedInvoiceDetailFlexiDto>()
            .ForMember(m => m.Id, u => u.MapFrom(e => "code:" + e.Code))
            .ForMember(m => m.Code, u => u.MapFrom(e => e.Code))
            .ForMember(m => m.VarSymbol, u => u.MapFrom(e => e.VarSymbol))
            .ForMember(m => m.Currency, u => u.MapFrom((ai, fi, val) => "code:" + ai.Price.CurrencyCode ?? "CZK"))
            .ForMember(m => m.OrderNumber, u => u.MapFrom(e => e.OrderCode))
            .ForMember(m => m.DocumentType, u => u.MapFrom(e => "code:FAKTURA"))
            .ForMember(m => m.DateCreated, u => u.MapFrom(e => e.CreationTime.ToString("yyyy-MM-dd")))
            .ForMember(m => m.DateTaxOrig, u => u.MapFrom(e => e.TaxDate.ToString("yyyy-MM-dd")))
            .ForMember(m => m.DateTaxAcc, u => u.MapFrom(e => e.TaxDate.ToString("yyyy-MM-dd")))
            .ForMember(m => m.DateDue, u => u.MapFrom(e => e.DueDate.ToString("yyyy-MM-dd")))
            .ForMember(m => m.CompanyName, u => u.MapFrom(e => e.Customer.DisplayName))
            .ForMember(m => m.CompanyStreet, u => u.MapFrom(e => e.BillingAddress.Street))
            .ForMember(m => m.CompanyCity, u => u.MapFrom(e => e.BillingAddress.City))
            .ForMember(m => m.CompanyState, u => u.MapFrom(e => "code:" + e.BillingAddress.CountryCode))
            .ForMember(m => m.CIN, u => u.MapFrom(e => e.Customer.CompanyId))
            .ForMember(m => m.VATIN, u => u.MapFrom(e => e.Customer.VatId))
            .ForMember(m => m.RoundingTotalC, u => u.MapFrom(e => "zaokrNa.zadne"))
            .ForMember(m => m.RoundingTaxC, u => u.MapFrom(e => "zaokrNa.zadne"))
            .ForMember(m => m.WithoutItems, u => u.MapFrom(e => false))
            .ForMember(m => m.DeliveryType, u => u.MapFrom((ai, fi) =>
            {
                return ai.ShippingMethod switch
                {
                    ShippingMethod.PPL => "code:PPL",
                    ShippingMethod.GLS => "code:GLS",
                    ShippingMethod.Zasilkovna => "code:ZASILKOVNA",
                    ShippingMethod.PPLParcelShop => "code:PPL PARCELSHOP",
                    ShippingMethod.PickUp => "code:OSOBNÍ ODBĚR",
                    _ => "code:PPL"
                };
            }))
            .ForMember(m => m.PaymentType, u => u.MapFrom((ai, fi) =>
            {
                return ai.BillingMethod switch
                {
                    BillingMethod.BankTransfer => "code:PREVOD",
                    BillingMethod.Cash => "code:HOTOVE",
                    BillingMethod.CoD => "code:DOBIRKA",
                    BillingMethod.Comgate => "code:KARTA",
                    BillingMethod.CreditCard => "code:KARTA",
                    _ => "code:PREVOD"
                };
            }))
            .ForMember(m => m.Items, u => u.MapFrom(e => e.Items))
            .AfterMap((ii, fv) =>
            {
                fv.Items.ForEach((f =>
                {
                    f.Currency = fv.Currency;
                    if (f.Currency != "code:CZK")
                    {
                        f.SumTotal = null;
                        f.SumBase = null;
                    }
                }));
                
                fv.Validate(); 
            });

        CreateMap<IssuedInvoiceDetailItem, IssuedInvoiceItemFlexiDto>()
            .ForMember(m => m.PriceList, u => u.MapFrom(e => ShouldHaveCode(e) ? "code:"+e.Code : null))
            .ForMember(m => m.Store, u => u.MapFrom(e => ShouldHaveStoreCode(e) ? "code:ZBOZI" : null))
            .ForMember(m => m.Code, u => u.MapFrom(e => string.IsNullOrEmpty(e.Code) ? null : e.Code))
            .ForMember(m => m.MeasureUnit, u => u.MapFrom(e => e.Code == null ? null : "code:" + e.AmountUnit.ToUpper()))
            .ForMember(m => m.Name, u => u.MapFrom(e => e.Name))
            .ForMember(m => m.Amount, u => u.MapFrom(e => e.Amount))
            .ForMember(m => m.PricePerUnit, u => u.MapFrom(e => e.ItemPrice.WithoutVat / Convert.ToDecimal(e.Amount)))
            .ForMember(m => m.SumTotalC, u => u.MapFrom(e => e.ItemPrice.CurrencyCode == "CZK" ? null : (decimal?)e.ItemPrice.WithVat))
            .ForMember(m => m.SumBaseC, u => u.MapFrom(e => e.ItemPrice.CurrencyCode == "CZK" ? null : (decimal?)e.ItemPrice.WithoutVat))
            .ForMember(m => m.SumTotal, u => u.MapFrom(e => e.ItemPrice.CurrencyCode == "CZK" ? (decimal?)e.ItemPrice.WithVat : null))
            .ForMember(m => m.SumBase, u => u.MapFrom(e => e.ItemPrice.CurrencyCode == "CZK" ? (decimal?)e.ItemPrice.WithoutVat : null))
            .ForMember(m => m.VatRateType, u => u.MapFrom(e => GetVatRate(e.ItemPrice.VatRate)))
            .ForMember(m => m.PriceVatType, u => u.MapFrom(e => "typCeny.bezDph"));

        // Error mappings
        CreateMap<Error, IssuedInvoiceError>()
            .ForMember(m => m.Message, e => e.MapFrom(f => f.Message))
            .ForMember(m => m.ErrorType, e => e.MapFrom((f, d) => MapErrorType(f.ErrorType)));

        // Legacy entity mappings
        CreateMap<IssuedInvoiceDetail, IssuedInvoice>()
            .Ignore(m => m.CreationTime)
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

        CreateMap<OperationResult<OperationResultDetail>, OperationResultDto>()
            .ForMember(m => m.HttpStatusCode, e => e.MapFrom(f => f.StatusCode))
            .ForMember(m => m.IsSuccess, e => e.MapFrom(f => f.IsSuccess))
            .ForMember(m => m.ErrorMessage, e => e.MapFrom(f => f.ErrorMessage));

        CreateMap<IssuedInvoiceSyncData, IssuedInvoiceSyncDataDto>()
            .ForMember(m => m.ErrorMessage, e => e.MapFrom(f => f.Error.Message))
            .ForMember(m => m.ErrorType, e => e.MapFrom(f => f.Error.ErrorType));

        CreateMap<ImportInvoiceRequestDto, IssuedInvoiceSourceQuery>();
    }

    private string GetVatRate(string vatRate)
    {
        return vatRate switch  
        {  
            "high" => "typSzbDph.dphZakl",  
            "low" => "typSzbDph.dphSniz",  
            "none" => "typSzbDph.dphOsv",  
            _ => throw new NotSupportedException($"VAT rate {vatRate} is unknown")  
        };  
    }
    
    private bool ShouldHaveStoreCode(IssuedInvoiceDetailItem e) => !string.IsNullOrEmpty(e.Code) && !e.Code.StartsWith("SHIPPING") && !e.Code.StartsWith("BILLING");
    
    private bool ShouldHaveCode(IssuedInvoiceDetailItem e) => !string.IsNullOrEmpty(e.Code) && !e.Code.StartsWith("SHIPPING") && !e.Code.StartsWith("BILLING");

    private IssuedInvoiceErrorType MapErrorType(ErrorType errorType)
    {
        if (Enum.TryParse(typeof(IssuedInvoiceErrorType), errorType.ToString(), out var parsed))
            return (IssuedInvoiceErrorType)parsed;
        return IssuedInvoiceErrorType.General;
    }
}