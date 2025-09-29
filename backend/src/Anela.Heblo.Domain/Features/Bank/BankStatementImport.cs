using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Bank;

public class BankStatementImport : IEntity<int>
{
    public int Id { get; private set; }
    public DateTime StatementDate { get; private set; }
    public DateTime ImportDate { get; private set; }
    public string Account { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public int ItemCount { get; private set; }
    public string ImportResult { get; private set; } = null!;
    public string? ExtraProperties { get; private set; }
    public string? ConcurrencyStamp { get; private set; }
    public DateTime CreationTime { get; private set; }
    public string? CreatorId { get; private set; }
    public DateTime? LastModificationTime { get; private set; }
    public string? LastModifierId { get; private set; }

    protected BankStatementImport()
    {
    }

    public BankStatementImport(
        DateTime statementDate,
        string account,
        string currency,
        int itemCount,
        string importResult,
        string? creatorId = null)
    {
        StatementDate = statementDate.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(statementDate, DateTimeKind.Utc) : statementDate.ToUniversalTime();
        ImportDate = DateTime.UtcNow;
        Account = account ?? throw new ArgumentNullException(nameof(account));
        Currency = currency ?? throw new ArgumentNullException(nameof(currency));
        ItemCount = itemCount;
        ImportResult = importResult ?? throw new ArgumentNullException(nameof(importResult));
        CreationTime = DateTime.UtcNow;
        CreatorId = creatorId;
        ConcurrencyStamp = Guid.NewGuid().ToString();
    }

    public void UpdateImportResult(string importResult, string? modifierId = null)
    {
        ImportResult = importResult ?? throw new ArgumentNullException(nameof(importResult));
        LastModificationTime = DateTime.UtcNow;
        LastModifierId = modifierId;
        ConcurrencyStamp = Guid.NewGuid().ToString();
    }

    public void UpdateItemCount(int itemCount, string? modifierId = null)
    {
        ItemCount = itemCount;
        LastModificationTime = DateTime.UtcNow;
        LastModifierId = modifierId;
        ConcurrencyStamp = Guid.NewGuid().ToString();
    }
}