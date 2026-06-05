using Anela.Heblo.Application.Features.Bank.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementById;

public class GetBankStatementByIdRequest : IRequest<BankStatementImportDto?>
{
    public int Id { get; set; }
}
