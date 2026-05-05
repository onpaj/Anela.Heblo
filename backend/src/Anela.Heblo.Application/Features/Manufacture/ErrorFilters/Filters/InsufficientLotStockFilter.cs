using System.Text.RegularExpressions;

namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientLotStockFilter : IManufactureErrorFilter
{
    private const string FlexiSignal = "Na skladě není dostatek zboží pro vyskladnění požadované šarže";

    private static readonly Regex _whitespaceRun = new(@"\s+", RegexOptions.Compiled);

    public bool CanHandle(Exception exception) =>
        exception is IHasFailedConsumptionItems &&
        exception.Message.Contains(FlexiSignal, StringComparison.OrdinalIgnoreCase);

    public string Transform(Exception exception)
    {
        // Normalize whitespace first so ". Požadované šarže " reliably matches even when
        // Flexi inserts a newline (or \r\n) between the two sentences.
        var normalized = _whitespaceRun.Replace(exception.Message, " ");

        // Use ". Požadované šarže " to skip the lowercase "požadované šarže" that appears
        // earlier in the same Flexi error sentence ("pro vyskladnění požadované šarže nebo expirace").
        var lotNumber = ManufactureErrorParsingHelpers.ExtractBetween(
            normalized,
            ". Požadované šarže ",
            " s ");

        // Try bracket-terminated form first ("… jen 6.59 G. [DoklSklad -1]");
        // fall back to sentence-terminated form in case Flexi omits the bracket.
        var available = ManufactureErrorParsingHelpers.ExtractBetween(normalized, "máte na skladě jen ", ". [");
        if (available == "?")
            available = ManufactureErrorParsingHelpers.ExtractBetween(normalized, "máte na skladě jen ", ".");

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

        return BuildFallback(failedItems, lotNumber);
    }

    private static string BuildFallback(IReadOnlyList<FailedConsumptionItem> items, string lotNumber)
    {
        if (items.Count == 0)
        {
            return lotNumber == "?"
                ? "Na skladě chybí šarže. Materiál podle šarže se nepodařilo dohledat."
                : $"Na skladě chybí šarže {lotNumber}. Materiál podle šarže se nepodařilo dohledat.";
        }

        if (items.Count == 1)
        {
            var item = items[0];
            var exp = item.Expiration?.ToString("dd.MM.yyyy") ?? "?";
            var lot = string.IsNullOrWhiteSpace(item.LotNumber) ? "bez šarže" : $"šarže {item.LotNumber}";
            var code = item.ProductCode ?? "?";
            return $"Na skladě chybí materiál {item.ProductName} ({code}) – {lot}, expirace {exp}." +
                   " Šarži se nepodařilo přesně určit z chybové zprávy. Doplňte šarži na sklad nebo upravte šarži ve výrobě.";
        }

        var itemList = string.Join(", ", items.Select(FormatItem));
        return $"Na skladě chybí šarže pro některý z materiálů: {itemList}. Šarži se nepodařilo přesně určit z chybové zprávy.";
    }

    private static string FormatItem(FailedConsumptionItem item)
    {
        var lot = string.IsNullOrWhiteSpace(item.LotNumber) ? "bez šarže" : $"šarže {item.LotNumber}";
        var exp = item.Expiration?.ToString("dd.MM.yyyy") ?? "?";
        var code = item.ProductCode ?? "?";
        return $"{item.ProductName} ({code}, {lot}, expirace {exp})";
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
