using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Bank;

public class BankStatementImport : IEntity<int>
{
    public int Id { get; private set; }
    public string TransferId { get; private set; } = null!;
    public DateTime StatementDate { get; private set; }
    public DateTime ImportDate { get; private set; }
    public string Account { get; private set; } = null!;
    public int Currency { get; private set; }
    public int ItemCount { get; private set; }
    public string ImportResult { get; private set; } = null!;

    protected BankStatementImport()
    {
    }

    public BankStatementImport(string transferId, DateTime statementDate)
    {
        TransferId = transferId ?? throw new ArgumentNullException(nameof(transferId));
        StatementDate = statementDate.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(statementDate, DateTimeKind.Utc) : statementDate.ToUniversalTime();
        ImportDate = DateTime.UtcNow;
        Account = string.Empty;
        Currency = 0;
        ItemCount = 0;
        ImportResult = string.Empty;
    }

    public void Update(string? account = null, int? currency = null, int? itemCount = null, string? importResult = null)
    {
        if (account != null)
            Account = account;
        
        if (currency.HasValue)
            Currency = currency.Value;
        
        if (itemCount.HasValue)
            ItemCount = itemCount.Value;
        
        if (importResult != null)
            ImportResult = importResult;
    }
}