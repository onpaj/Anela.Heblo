namespace Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;

internal sealed record BankImportJobParameters(
    string AccountName,
    DateTime DateFrom,
    DateTime DateTo);
