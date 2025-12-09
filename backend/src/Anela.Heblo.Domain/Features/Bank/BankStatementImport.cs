using Anela.Heblo.Xcc.Domain;
using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Domain.Features.Bank;

public class BankStatementImport : IEntity<int>
{
    public int Id { get; private set; }
    public string TransferId { get; private set; } = null!;
    public DateTime StatementDate { get; private set; }
    public DateTime ImportDate { get; private set; }
    public string Account 
    { 
        get => _account;
        set => _account = value ?? throw new ArgumentNullException(nameof(Account));
    }
    
    public CurrencyCode Currency 
    { 
        get => _currency;
        set => _currency = Enum.IsDefined(typeof(CurrencyCode), value) ? value : throw new ArgumentException($"Invalid currency value: {value}", nameof(Currency));
    }
    
    public int ItemCount 
    { 
        get => _itemCount;
        set => _itemCount = value >= 0 ? value : throw new ArgumentException("ItemCount must be non-negative", nameof(ItemCount));
    }
    
    public string ImportResult 
    { 
        get => _importResult;
        set => _importResult = value ?? throw new ArgumentNullException(nameof(ImportResult));
    }

    private string _account = null!;
    private CurrencyCode _currency;
    private int _itemCount;
    private string _importResult = null!;

    protected BankStatementImport()
    {
    }

    public BankStatementImport(string transferId, DateTime statementDate)
    {
        TransferId = transferId ?? throw new ArgumentNullException(nameof(transferId));
        StatementDate = statementDate.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(statementDate, DateTimeKind.Utc) : statementDate.ToUniversalTime();
        ImportDate = DateTime.UtcNow;
        _account = string.Empty;
        _currency = CurrencyCode.CZK;
        _itemCount = 0;
        _importResult = string.Empty;
    }
}