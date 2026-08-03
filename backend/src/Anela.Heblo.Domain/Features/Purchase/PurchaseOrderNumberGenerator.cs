namespace Anela.Heblo.Domain.Features.Purchase;

public interface IPurchaseOrderNumberGenerator
{
    string GenerateCandidate(DateTime orderDate, DateTimeOffset now, int attempt);
}

public class PurchaseOrderNumberGenerator : IPurchaseOrderNumberGenerator
{
    public string GenerateCandidate(DateTime orderDate, DateTimeOffset now, int attempt)
    {
        var suffix = attempt <= 1 ? string.Empty : $"-{attempt}";

        // Format: POyyyyMMdd-HHmmss[-attempt]
        return $"PO{orderDate.Year:D4}{orderDate.Month:D2}{orderDate.Day:D2}-{now.Hour:D2}{now.Minute:D2}{now.Second:D2}{suffix}";
    }
}
