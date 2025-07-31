namespace Anela.Heblo.Adapters.Flexi.ProductAttributes;

public class SeasonalDataParser : ISeasonalDataParser
{
    public int[] GetSeasonalMonths(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return Array.Empty<int>();

        var months = new List<int>();

        var sections = s.Split(',');
        foreach (var section in sections)
        {
            months.AddRange(ParseSection(section));
        }

        return months.Distinct().OrderBy(o => o).ToArray();
    }

    private IEnumerable<int> ParseSection(string s)
    {
        if (int.TryParse(s, out var single))
        {
            if (single is < 1 or > 12)
                return Array.Empty<int>();
            return new[] { single };
        }

        var range = s.Split('-');
        if (range.Length != 2)
            return Array.Empty<int>();

        if (!int.TryParse(range[0], out var first) || !int.TryParse(range[1], out var second))
            return Array.Empty<int>();

        if (first < 1 || first > 12 || second < 1 || second > 12)
            return Array.Empty<int>();

        if (first > second)
        {
            var retVal = new List<int>();
            retVal.AddRange(Enumerable.Range(first, 12 - first + 1));
            retVal.AddRange(Enumerable.Range(1, second));
            return retVal;
        }
        else
        {
            return Enumerable.Range(first, second - first + 1);
        }
    }
}