using Anela.Heblo.Adapters.Flexi.Common;
using Anela.Heblo.Domain.Accounting.Ledger;
using AutoMapper;
using Rem.FlexiBeeSDK.Model.Accounting.Ledger;

namespace Anela.Heblo.Adapters.Flexi.Accounting.Ledger;

public class LedgerMappingProfile : BaseFlexiProfile
{
    public LedgerMappingProfile()
    {
        // Map from FlexiBee SDK type to domain model
        CreateMap<Rem.FlexiBeeSDK.Model.Accounting.Ledger.LedgerItemFlexiDto, LedgerItem>()
            .ForMember(dest => dest.Date, opt => opt.MapFrom(src => src.AccountingDate))
            .ForMember(dest => dest.DocumentNumber, opt => opt.MapFrom(src => src.Document ?? string.Empty))
            .ForMember(dest => dest.ClientName, opt => opt.MapFrom(src => src.CompanyName ?? string.Empty))
            .ForMember(dest => dest.VariableSymbol, opt => opt.MapFrom(src => src.ParSymbol ?? string.Empty))
            .ForMember(dest => dest.DebitAccountNumber, opt => opt.MapFrom(src => src.DebitAccount != null ? src.DebitAccount.Code : string.Empty))
            .ForMember(dest => dest.DebitAccountName, opt => opt.MapFrom(src => src.DebitAccount != null ? src.DebitAccount.Name : string.Empty))
            .ForMember(dest => dest.CreditAccountNumber, opt => opt.MapFrom(src => src.CreditAccount != null ? src.CreditAccount.Code : string.Empty))
            .ForMember(dest => dest.CreditAccountName, opt => opt.MapFrom(src => src.CreditAccount != null ? src.CreditAccount.Name : string.Empty))
            .ForMember(dest => dest.Department, opt => opt.MapFrom(src => src.Department != null ? src.Department.Code : string.Empty))
            .ForMember(dest => dest.Amount, opt => opt.MapFrom(src => (decimal)src.AmountLocal));
    }
}