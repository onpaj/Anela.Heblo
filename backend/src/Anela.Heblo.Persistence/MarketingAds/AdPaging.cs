namespace Anela.Heblo.Persistence.MarketingAds;

internal static class AdPaging
{
    /// <summary>Rows to skip for a 1-based page; computed in 64 bits and capped, so a huge page number cannot wrap negative.</summary>
    public static int Skip(int page, int size) => (int)Math.Min((Math.Max(1L, page) - 1) * size, int.MaxValue);
}
