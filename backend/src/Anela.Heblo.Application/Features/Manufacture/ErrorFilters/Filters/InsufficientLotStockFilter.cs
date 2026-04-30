namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientLotStockFilter : IManufactureErrorFilter
{
    private const string FlexiSignal = "Na skladě není dostatek zboží pro vyskladnění požadované šarže";

    public bool CanHandle(Exception exception) =>
        exception is IHasFailedConsumptionItems &&
        exception.Message.Contains(FlexiSignal, StringComparison.OrdinalIgnoreCase);

    public string Transform(Exception exception)
    {
        // Use ". Požadované šarže " to skip the lowercase "požadované šarže" that appears
        // earlier in the same Flexi error sentence ("pro vyskladnění požadované šarže nebo expirace").
        var lotNumber = ManufactureErrorParsingHelpers.ExtractBetween(
            exception.Message,
            ". Požadované šarže ",
            " s ");

        // Try bracket-terminated form first ("… jen 6.59 G. [DoklSklad -1]");
        // fall back to sentence-terminated form in case Flexi omits the bracket.
        var available = ManufactureErrorParsingHelpers.ExtractBetween(exception.Message, "máte na skladě jen ", ". [");
        if (available == "?")
            available = ManufactureErrorParsingHelpers.ExtractBetween(exception.Message, "máte na skladě jen ", ".");

        var failedItems = (exception as IHasFailedConsumptionItems)?.FailedItems ?? [];
        var match = FindMatch(failedItems, lotNumber, exception.Message);

        if (match is not null)
        {
            var expiration = match.Expiration.HasValue
                ? match.Expiration.Value.ToString("dd.MM.yyyy")
                : "?";

            return $"Na skladě chybí materiál {match.ProductName} ({match.ProductCode}) – šarže {match.LotNumber}, expirace {expiration}." +
                   $" K dispozici {available}. Doplňte šarži na sklad nebo upravte šarži ve výrobě.";
        }

        return $"Na skladě chybí šarže {lotNumber}. Materiál podle šarže se nepodařilo dohledat.";
    }

    private static FailedConsumptionItem? FindMatch(
        IReadOnlyList<FailedConsumptionItem> items,
        string lotNumber,
        string rawMessage)
    {
        if (items.Count == 0 || lotNumber == "?")
            return null;

        var candidates = items
            .Where(i => string.Equals(i.LotNumber, lotNumber, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 1)
            return candidates[0];

        // Multiple candidates with same lot — disambiguate by expiration date in the raw message.
        foreach (var candidate in candidates)
        {
            if (candidate.Expiration.HasValue &&
                rawMessage.Contains(candidate.Expiration.Value.ToString("dd.MM.yyyy"), StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return candidates.FirstOrDefault();
    }
}
