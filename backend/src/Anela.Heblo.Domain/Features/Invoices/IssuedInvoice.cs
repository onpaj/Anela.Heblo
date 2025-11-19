using System.Text.Json;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Invoices;

/// <summary>
/// Entity representing an issued invoice from Shoptet
/// Used for monitoring import statistics and tracking sync history
/// </summary>
public class IssuedInvoice : IEntity<string>
{
    public string Id { get; set; }

    public DateTime InvoiceDate { get; set; }

    public DateTime DueDate { get; set; }

    public DateTime TaxDate { get; set; }

    public long? VarSymbol { get; set; }

    public BillingMethod BillingMethod { get; set; }

    public ShippingMethod ShippingMethod { get; set; }

    public bool? VatPayer { get; set; }

    public int ItemsCount { get; set; }

    public decimal Price { get; set; } = 0;
    public decimal PriceC { get; set; } = 0;
    public string Currency { get; set; } = "CZK";

    // Denormalized to support sorting & filtering
    public bool IsSynced { get; private set; }
    public DateTime? LastSyncTime { get; private set; }
    public string? ErrorMessage { get; private set; }
    public IssuedInvoiceErrorType? ErrorType { get; private set; }

    public string? CustomerName { get; set; }

    // Audit fields matching database schema
    public string ExtraProperties { get; set; } = "{}";
    public string? ConcurrencyStamp { get; set; }
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }

    public IList<IssuedInvoiceSyncData> SyncHistory { get; set; } = new List<IssuedInvoiceSyncData>();
    public int SyncHistoryCount { get; set; } = 0;

    public bool IsCriticalError => ErrorType != null && ErrorType != IssuedInvoiceErrorType.InvoicePaired;

    public void SyncSucceeded(object syncedInvoice)
    {
        var lastSync = new IssuedInvoiceSyncData()
        {
            IsSuccess = true,
            Error = null,
            SyncTime = DateTime.Now.ToUniversalTime(),
            Data = JsonSerializer.Serialize(syncedInvoice)
        };

        SetLastSync(lastSync);
    }



    public void SyncFailed(object syncedInvoice, string error)
    {
        SyncFailed(syncedInvoice, new IssuedInvoiceError() { Message = error });
    }

    public void SyncFailed(object syncedInvoice, IssuedInvoiceError error)
    {
        var lastSync = new IssuedInvoiceSyncData()
        {
            IsSuccess = false,
            Error = error,
            SyncTime = DateTime.Now.ToUniversalTime(),
            Data = JsonSerializer.Serialize(syncedInvoice)
        };

        SetLastSync(lastSync);
    }

    private void SetLastSync(IssuedInvoiceSyncData lastSync)
    {
        SyncHistory.Add(lastSync);

        IsSynced = lastSync.IsSuccess;
        SyncHistoryCount = SyncHistory.Count;
        LastSyncTime = lastSync.SyncTime;
        ErrorType = lastSync.Error?.ErrorType;
        ErrorMessage = lastSync.Error?.Message;
    }

}


public class IssuedInvoiceSyncData : IEntity<int>
{
    public int Id { get; set; }

    public string? Data { get; set; }
    public bool IsSuccess { get; set; } = true;
    public IssuedInvoiceError? Error { get; set; }
    public DateTime SyncTime { get; set; }

    // Foreign key to IssuedInvoice
    public string IssuedInvoiceId { get; set; } = null!;
}

public record IssuedInvoiceError
{
    public IssuedInvoiceErrorType ErrorType { get; set; } = IssuedInvoiceErrorType.General;
    public string Message { get; set; } = "?";
}

public enum IssuedInvoiceErrorType
{
    General,

    InvoicePaired,
    ProductNotFound
}