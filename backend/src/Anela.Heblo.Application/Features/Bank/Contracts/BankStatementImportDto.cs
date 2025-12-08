namespace Anela.Heblo.Application.Features.Bank.Contracts;

public class BankStatementImportDto
{
    public int Id { get; set; }
    public string TransferId { get; set; } = null!;
    public DateTime StatementDate { get; set; }
    public DateTime ImportDate { get; set; }
    public string Account { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public int ItemCount { get; set; }
    public string ImportResult { get; set; } = null!;
    public string? ErrorType => ImportResult != "OK" ? ImportResult : null;
}