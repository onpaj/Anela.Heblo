using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

public class GetBankStatementListRequest : IRequest<GetBankStatementListResponse>
{
    public int? Id { get; set; }
    public string? StatementDate { get; set; }
    public string? ImportDate { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 10;
    public string? OrderBy { get; set; } = "ImportDate";
    public bool Ascending { get; set; } = false;
}