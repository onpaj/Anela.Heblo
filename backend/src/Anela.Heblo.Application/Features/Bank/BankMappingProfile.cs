using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Domain.Features.Bank;
using AutoMapper;

namespace Anela.Heblo.Application.Features.Bank;

public class BankMappingProfile : Profile
{
    public BankMappingProfile()
    {
        CreateMap<BankStatementImport, BankStatementImportDto>()
            .ForMember(dest => dest.ErrorType,
                opt => opt.MapFrom(src =>
                    src.ImportResult != ImportStatus.Success ? src.ImportResult : null));
    }
}
