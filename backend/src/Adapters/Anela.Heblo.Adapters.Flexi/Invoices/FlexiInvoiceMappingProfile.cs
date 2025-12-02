using Anela.Heblo.Adapters.Flexi.Common;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using Rem.FlexiBeeSDK.Model.Invoices;
using Rem.FlexiBeeSDK.Model;

namespace Anela.Heblo.Adapters.Flexi.Invoices;

public class FlexiInvoiceMappingProfile : BaseFlexiProfile
{
    public FlexiInvoiceMappingProfile()
    {
        

        // Map domain IssuedInvoiceDetail to FlexiBee IssuedInvoiceDetailFlexiDto
        CreateMap<IssuedInvoiceDetail, IssuedInvoiceDetailFlexiDto>()
            .ForMember(dest => dest.Id, opt => opt.MapFrom(src => $"code:{src.Code}"))
            .ForMember(dest => dest.Code, opt => opt.MapFrom(src => src.Code))
            .ForMember(dest => dest.VarSymbol, opt => opt.MapFrom(src => src.VarSymbol))
            .ForMember(dest => dest.DateCreated, opt => opt.MapFrom(src => src.CreationTime.ToString("yyyy-MM-dd")))
            .ForMember(dest => dest.DateDue, opt => opt.MapFrom(src => src.DueDate.ToString("yyyy-MM-dd")))
            .ForMember(dest => dest.DateTaxOrig, opt => opt.MapFrom(src => src.CreationTime.ToString("yyyy-MM-dd")))
            .ForMember(dest => dest.DateTaxAcc, opt => opt.MapFrom(src => src.CreationTime.ToString("yyyy-MM-dd")))
            .ForMember(dest => dest.Currency, opt => opt.MapFrom(src => $"code:{src.Price.CurrencyCode}"))
            .ForMember(dest => dest.DocumentType, opt => opt.MapFrom(src => "code:FAKTURA"))
            .ForMember(dest => dest.CompanyName, opt => opt.MapFrom(src => src.Customer.Name))
            .ForMember(dest => dest.CompanyStreet, opt => opt.MapFrom(src => src.BillingAddress.Street))
            .ForMember(dest => dest.CompanyCity, opt => opt.MapFrom(src => src.BillingAddress.City))
            .ForMember(dest => dest.CompanyState, opt => opt.MapFrom(src => "code:CZ"))
            .ForMember(dest => dest.CIN, opt => opt.MapFrom(src => src.Customer.CompanyId ?? ""))
            .ForMember(dest => dest.VATIN, opt => opt.MapFrom(src => src.Customer.VatId ?? ""))
            .ForMember(dest => dest.DeliveryType, opt => opt.MapFrom(src => MapShippingMethod(src.ShippingMethod)))
            .ForMember(dest => dest.PaymentType, opt => opt.MapFrom(src => MapBillingMethod(src.BillingMethod)))
            .ForMember(dest => dest.RoundingTotalC, opt => opt.MapFrom(src => "zaokrNa.zadne"))
            .ForMember(dest => dest.RoundingTaxC, opt => opt.MapFrom(src => "zaokrNa.zadne"))
            .ForMember(dest => dest.Items, opt => opt.MapFrom(src => src.Items))
            ;

        // Map domain invoice items to FlexiBee items
        CreateMap<IssuedInvoiceDetailItem, IssuedInvoiceItemFlexiDto>()
            .ForMember(dest => dest.Code, opt => opt.MapFrom(src => src.Code))
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
            .ForMember(dest => dest.Amount, opt => opt.MapFrom(src => src.Amount))
            .ForMember(dest => dest.PricePerUnit, opt => opt.MapFrom(src => src.ItemPrice.WithoutVat))
            .ForMember(dest => dest.SumBase, opt => opt.MapFrom(src => Math.Round(src.ItemPrice.WithoutVat * src.Amount, 2)))
            .ForMember(dest => dest.SumTotal, opt => opt.MapFrom(src => Math.Round(src.ItemPrice.WithVat * src.Amount, 2)))
            .ForMember(dest => dest.Currency, opt => opt.MapFrom(src => $"code:{src.ItemPrice.CurrencyCode}"))
            .ForMember(dest => dest.PriceList, opt => opt.MapFrom(src => src.Code.StartsWith("SHIPPING") || src.Code.StartsWith("BILLING") ? null : $"code:{src.Code}"))
            .ForMember(dest => dest.Store, opt => opt.MapFrom(src => src.Code.StartsWith("SHIPPING") || src.Code.StartsWith("BILLING") ? null : "code:ZBOZI"))
            .ForMember(dest => dest.VatRateType, opt => opt.MapFrom(src => "typSzbDph.dphZakl"))
            .ForMember(dest => dest.PriceVatType, opt => opt.MapFrom(src => "typCeny.bezDph"))
            .ForMember(dest => dest.CopyCategoryVatReport, opt => opt.MapFrom(src => true))
            .ForMember(dest => dest.CopyCategoryVat, opt => opt.MapFrom(src => true));
    }

    private static string? MapShippingMethod(ShippingMethod shippingMethod)
    {
        return shippingMethod switch
        {
            ShippingMethod.PPL => "code:PPL",
            ShippingMethod.PPLParcelShop => "code:PPL",
            ShippingMethod.Zasilkovna => "code:ZASILKOVNA",
            ShippingMethod.ZasilkovnaDoRuky => "code:ZASILKOVNA",
            ShippingMethod.GLS => "code:GLS",
            ShippingMethod.PickUp => "code:OSOBNÍ ODBĚR",
            _ => null
        };
    }

    private static string? MapBillingMethod(BillingMethod billingMethod)
    {
        return billingMethod switch
        {
            BillingMethod.BankTransfer => "code:PREVOD",
            BillingMethod.CoD => "code:DOBIRKA",
            BillingMethod.Cash => "code:HOTOVE",
            BillingMethod.CreditCard => "code:KARTA",
            BillingMethod.Comgate => "code:KARTA",
            _ => null
        };
    }
}