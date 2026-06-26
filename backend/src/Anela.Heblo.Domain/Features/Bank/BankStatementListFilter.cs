namespace Anela.Heblo.Domain.Features.Bank;

public sealed record BankStatementListFilter(
    int? Id = null,
    string? TransferId = null,
    string? Account = null,
    DateTime? StatementDate = null,
    DateTime? ImportDate = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    bool? ErrorsOnly = null);
