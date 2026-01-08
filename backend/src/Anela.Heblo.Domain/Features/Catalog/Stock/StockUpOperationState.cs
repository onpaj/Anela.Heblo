namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public enum StockUpOperationState
{
    Pending = 0,      // Vytvořeno, čeká na odeslání
    Submitted = 1,    // Odesláno do Shoptet
    Verified = 2,     // Ověřeno v Shoptet historii
    Completed = 3,    // Úspěšně dokončeno
    Failed = 4        // Selhalo, vyžaduje manuální review
}
