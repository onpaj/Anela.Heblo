using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

public class GetBankStatementListRequest : IRequest<GetBankStatementListResponse>
{
    public int? Id { get; set; }
    public string? TransferId { get; set; }
    public string? Account { get; set; }
    public DateTime? StatementDate { get; set; }
    public DateTime? ImportDate { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool? ErrorsOnly { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 10;
    public string? OrderBy { get; set; } = "ImportDate";
    public bool Ascending { get; set; } = false;
}