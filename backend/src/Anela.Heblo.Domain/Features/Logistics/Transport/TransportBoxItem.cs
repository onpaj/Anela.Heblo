using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Logistics.Transport;

public class TransportBoxItem : Entity<int>
{
    public string ProductCode { get; private set; }
    public string ProductName { get; private set; }
    public double Amount { get; private set; }
    public DateTime DateAdded { get; private set; }
    public string UserAdded { get; private set; }
    public string? LotNumber { get; private set; }
    public DateOnly? ExpirationDate { get; private set; }
    public int? SourceInventoryId { get; private set; }

    public TransportBoxItem(
        string productCode,
        string productName,
        double amount,
        DateTime dateAdded,
        string userAdded,
        string? lotNumber = null,
        DateOnly? expirationDate = null,
        int? sourceInventoryId = null)
    {
        ProductCode = productCode;
        ProductName = productName;
        Amount = amount;
        DateAdded = dateAdded;
        UserAdded = userAdded;
        LotNumber = lotNumber;
        ExpirationDate = expirationDate;
        SourceInventoryId = sourceInventoryId;
    }
}
