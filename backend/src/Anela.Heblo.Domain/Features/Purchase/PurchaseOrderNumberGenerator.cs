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

        // Format: POyyyyMMdd-HHmmssfff[-attempt]. Millisecond resolution keeps
        // same-second bursts (bulk creation, fast automated tests) from exhausting
        // the bounded attempt-suffix retry; the suffix remains a fallback for
        // genuinely simultaneous requests.
        return $"PO{orderDate.Year:D4}{orderDate.Month:D2}{orderDate.Day:D2}-{now.Hour:D2}{now.Minute:D2}{now.Second:D2}{now.Millisecond:D3}{suffix}";
    }
}
