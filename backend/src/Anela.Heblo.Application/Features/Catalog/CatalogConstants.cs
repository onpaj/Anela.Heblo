namespace Anela.Heblo.Application.Features.Catalog;

public static class CatalogConstants
{
    /// <summary>
    /// Magic number used to indicate "all history" when requesting historical data.
    /// When MonthsBack >= ALL_HISTORY_MONTHS_THRESHOLD, all available historical records are returned without date filtering.
    /// </summary>
    public const int ALL_HISTORY_MONTHS_THRESHOLD = 999;
}