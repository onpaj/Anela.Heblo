namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public enum StockUpOperationState
{
    Pending = 0,      // Vytvořeno, čeká na odeslání
    Submitted = 1,    // Odesláno do Shoptet
    Completed = 2,    // Úspěšně dokončeno
    Failed = 3        // Selhalo, vyžaduje manuální review
}
