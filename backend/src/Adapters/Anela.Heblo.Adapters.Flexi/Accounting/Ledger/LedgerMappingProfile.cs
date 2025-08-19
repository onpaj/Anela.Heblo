using Anela.Heblo.Domain.Accounting.Ledger;
using AutoMapper;

namespace Anela.Heblo.Adapters.Flexi.Accounting.Ledger;

public class LedgerMappingProfile : Profile
{
    public LedgerMappingProfile()
    {
        CreateMap<LedgerItemFlexiDto, LedgerItem>()
            .ForMember(dest => dest.Date, opt => opt.MapFrom(src => src.Datum ?? DateTime.MinValue))
            .ForMember(dest => dest.DocumentNumber, opt => opt.MapFrom(src => src.CisloDokladu ?? string.Empty))
            .ForMember(dest => dest.ClientName, opt => opt.MapFrom(src => src.NazevFirmy ?? string.Empty))
            .ForMember(dest => dest.VariableSymbol, opt => opt.MapFrom(src => src.VariabilniSymbol ?? string.Empty))
            .ForMember(dest => dest.DebitAccountNumber, opt => opt.MapFrom(src => src.MaDatiUcet ?? string.Empty))
            .ForMember(dest => dest.DebitAccountName, opt => opt.MapFrom(src => src.MaDatiUcetNazev ?? string.Empty))
            .ForMember(dest => dest.CreditAccountNumber, opt => opt.MapFrom(src => src.DalUcet ?? string.Empty))
            .ForMember(dest => dest.CreditAccountName, opt => opt.MapFrom(src => src.DalUcetNazev ?? string.Empty))
            .ForMember(dest => dest.Department, opt => opt.MapFrom(src => src.Stredisko ?? string.Empty))
            .ForMember(dest => dest.Amount, opt => opt.MapFrom(src => src.Castka ?? 0m));
    }
}