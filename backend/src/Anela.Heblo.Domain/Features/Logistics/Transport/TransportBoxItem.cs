using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Logistics.Transport;

public class TransportBoxItem : Entity<int>
{
    public string ProductCode { get; private set; }

    public string ProductName { get; private set; }
    public double Amount { get; private set; }

    public DateTime DateAdded { get; private set; }

    public string UserAdded { get; private set; }

    public TransportBoxItem(string productCode, string productName, double amount, DateTime dateAdded, string userAdded)
    {
        ProductCode = productCode;
        ProductName = productName;
        Amount = amount;
        DateAdded = dateAdded;
        UserAdded = userAdded;
    }
}