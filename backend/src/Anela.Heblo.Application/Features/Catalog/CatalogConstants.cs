namespace Anela.Heblo.Application.Features.Catalog;

public static class CatalogConstants
{
    /// <summary>
    /// Magic number used to indicate "all history" when requesting historical data.
    /// When MonthsBack >= ALL_HISTORY_MONTHS_THRESHOLD, all available historical records are returned without date filtering.
    /// </summary>
    public const int ALL_HISTORY_MONTHS_THRESHOLD = 999;

    /// <summary>
    /// Earliest date used as the lower bound when "all history" is requested (MonthsBack >= ALL_HISTORY_MONTHS_THRESHOLD).
    /// Paired with ALL_HISTORY_MONTHS_THRESHOLD to define what "all history" means in one place.
    /// </summary>
    public static readonly DateTime HISTORY_FLOOR_DATE = new(2020, 1, 1);
}
