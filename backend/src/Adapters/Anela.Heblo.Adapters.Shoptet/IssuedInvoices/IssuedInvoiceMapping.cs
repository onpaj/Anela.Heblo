using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;

namespace Anela.Heblo.Adapters.Shoptet.IssuedInvoices
{
    public class IssuedInvoiceMapping : Profile
    {
        public IssuedInvoiceMapping()
        {
            CreateMap<Invoice, IssuedInvoiceDetail>()
                .ForMember(m => m.BillingMethod, u => u.MapFrom((si, ai, sbm) =>
                {
                    if (si.InvoiceHeader?.PaymentType?.PaymentType == "creditcard")
                        return BillingMethod.CreditCard;
                    if (si.InvoiceHeader?.PaymentType?.PaymentType == "cash")
                        return BillingMethod.Cash;

                    if (si.InvoiceDetail.InvoiceItems.Any(a => a.Text == "Převodem"))
                        return BillingMethod.BankTransfer;
                    if (si.InvoiceDetail.InvoiceItems.Any(a => a.Text == "Hotově"))
                        return BillingMethod.Cash;
                    if (si.InvoiceDetail.InvoiceItems.Any(a => a.Text.Contains("kart", StringComparison.InvariantCultureIgnoreCase)))
                        return BillingMethod.Comgate;
                    if (si.InvoiceDetail.InvoiceItems.Any(a => a.Text == "Dobírkou"))
                        return BillingMethod.CoD;

                    return BillingMethod.BankTransfer;
                }))
                .ForMember(m => m.ShippingMethod, u => u.MapFrom((si, ai, sbm) =>
                {
                    if (si.InvoiceDetail.InvoiceItems.Any(a => a.Text == "PPL - ParcelShop"))
                        return ShippingMethod.PPLParcelShop;

                    if (si.InvoiceDetail.InvoiceItems.Any(a => a.Text.Contains("PPL", StringComparison.InvariantCultureIgnoreCase)))
                        return ShippingMethod.PPL;

                    if (si.InvoiceDetail.InvoiceItems.Any(a => a.Text.Contains("Osobn", StringComparison.InvariantCultureIgnoreCase)))
                        return ShippingMethod.PickUp;

                    if (si.InvoiceDetail.InvoiceItems.Any(a => a.Text.Contains("GLS", StringComparison.InvariantCultureIgnoreCase)))
                        return ShippingMethod.GLS;

                    if (si.InvoiceDetail.InvoiceItems.Any(a => a.Text.Contains("zásilk", StringComparison.InvariantCultureIgnoreCase)))
                        return ShippingMethod.Zasilkovna;

                    return ShippingMethod.PickUp;
                }))
                .ForMember(m => m.Customer, u => u.MapFrom(f => f.InvoiceHeader.PartnerIdentity.Address))
                .ForMember(m => m.Items, u => u.MapFrom(f => f.InvoiceDetail.InvoiceItems))
                .ForMember(m => m.BillingAddress, u => u.MapFrom(f => f.InvoiceHeader.PartnerIdentity.Address))
                .ForMember(m => m.DeliveryAddress, u => u.MapFrom(f => f.InvoiceHeader.PartnerIdentity.ShipToAddress))
                .ForMember(m => m.DueDate, u => u.MapFrom(f => f.InvoiceHeader.DateDue))
                .ForMember(m => m.TaxDate, u => u.MapFrom(f => f.InvoiceHeader.DateTax))
                .ForMember(m => m.VarSymbol, u => u.MapFrom(f => f.InvoiceHeader.SymVar))
                .ForMember(m => m.CreationTime, u => u.MapFrom(f => f.InvoiceHeader.Date))
                .ForMember(m => m.Code, u => u.MapFrom(f => f.InvoiceHeader.NumberOrder))
                .AfterMap((i, ii) =>
                {
                    if (ii.DeliveryAddress == null)
                        ii.DeliveryAddress = ii.BillingAddress;

                    ii.VatPayer = !string.IsNullOrEmpty(i.InvoiceHeader.PartnerIdentity.Address.Dic);

                    if (i.InvoiceSummary.ForeignCurrency != null)
                    {
                        ii.Price = new InvoicePrice()
                        {
                            WithVat = i.InvoiceSummary.ForeignCurrency.PriceSum,
                            Vat = ii.Items.Sum(s => s.ItemPrice.Vat),
                            WithoutVat = ii.Items.Sum(s => s.ItemPrice.WithoutVat),
                            CurrencyCode = i.InvoiceSummary.ForeignCurrency.Currency.Ids,
                            ExchangeRate = i.InvoiceSummary.ForeignCurrency.Rate
                        };
                    }
                    else
                    {
                        ii.Price = new InvoicePrice()
                        {
                            WithVat = i.InvoiceSummary.HomeCurrency.PriceHighSum,
                            Vat = ii.Items.Sum(s => s.ItemPrice.Vat),
                            WithoutVat = ii.Items.Sum(s => s.ItemPrice.WithoutVat),
                            CurrencyCode = "CZK",
                            ExchangeRate = 1,
                        };
                    }

                    ii.Items.ForEach(f => f.ItemPrice.CurrencyCode = ii.Price.CurrencyCode);
                })
                ;


            CreateMap<InvoiceItem, IssuedInvoiceDetailItem>()
                .ForMember(m => m.Name, c => c.MapFrom(f => f.Text))
                .ForMember(m => m.Amount, c => c.MapFrom(f => f.Quantity))
                .ForMember(m => m.Code, c => c.MapFrom(f => f.Code))
                .AfterMap(((item, invoiceItem, ctx) =>
                {
                    if (item.ForeignCurrency != null)
                        invoiceItem.ItemPrice = ctx.Mapper.Map<ForeignCurrency, InvoicePrice>(item.ForeignCurrency);
                    else
                        invoiceItem.ItemPrice = ctx.Mapper.Map<HomeCurrency, InvoicePrice>(item.HomeCurrency);


                    invoiceItem.ItemPrice.VatRate = item.RateVAT;
                }))
                ;


            CreateMap<HomeCurrency, InvoicePrice>()
                .AfterMap((hc, p) =>
                    {
                        p.WithVat = hc.PriceVAT + hc.Price;
                        p.WithoutVat = hc.Price;
                        p.Vat = hc.PriceVAT;
                    })
                ;

            CreateMap<ForeignCurrency, InvoicePrice>()
                .AfterMap((hc, p) =>
                {
                    p.WithVat = hc.PriceVAT + hc.Price;
                    p.WithoutVat = hc.Price;
                    p.Vat = hc.PriceVAT;
                })
                ;

            CreateMap<PartnerIdentity, InvoiceCustomer>()
                .ForMember(m => m.Company, c => c.MapFrom(f => f.Address.Company))
                .ForMember(m => m.Name, c => c.MapFrom(f => f.Address.Name))
                .ForMember(m => m.VatId, c => c.MapFrom(f => f.Address.Dic))
                .ForMember(m => m.CompanyId, c => c.MapFrom(f => f.Address.Ico))
                ;

            CreateMap<Address, InvoiceAddress>()
                .ForMember(m => m.Company, c => c.MapFrom(f => f.Company))
                .ForMember(m => m.FullName, c => c.MapFrom(f => f.Name))
                .ForMember(m => m.Street, c => c.MapFrom(f => f.Street))
                .ForMember(m => m.Zip, c => c.MapFrom(f => f.Zip))
                .ForMember(m => m.CountryCode, c => c.MapFrom(f => f.Country.Ids))
                .ForMember(m => m.City, c => c.MapFrom(f => f.City))
                ;

            CreateMap<ShipToAddress, InvoiceAddress>()
                .ForMember(m => m.Company, c => c.MapFrom(f => f.Company))
                .ForMember(m => m.FullName, c => c.MapFrom(f => f.Name))
                .ForMember(m => m.Street, c => c.MapFrom(f => f.Street))
                .ForMember(m => m.Zip, c => c.MapFrom(f => f.Zip))
                .ForMember(m => m.CountryCode, c => c.MapFrom(f => f.Country.Ids))
                .ForMember(m => m.City, c => c.MapFrom(f => f.City))
                ;

            CreateMap<Address, InvoiceCustomer>()
                .ForMember(m => m.Company, c => c.MapFrom(f => f.Company))
                .ForMember(m => m.DisplayName, c => c.MapFrom(f => f.Name))
                .ForMember(m => m.CompanyId, c => c.MapFrom(f => f.Ico))
                .ForMember(m => m.VatId, c => c.MapFrom(f => f.Dic))
                ;
        }
    }
}