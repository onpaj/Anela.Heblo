namespace Anela.Heblo.Application.Features.Manufacture;

public static class ManufactureConstants
{
    /// <summary>
    /// Minimum number of months that can be requested for manufacture output analysis.
    /// </summary>
    public const int MIN_MONTHS_BACK = 1;

    /// <summary>
    /// Maximum number of months that can be requested for manufacture output analysis.
    /// This limit prevents excessive memory usage and long processing times.
    /// </summary>
    public const int MAX_MONTHS_BACK = 60;

    /// <summary>
    /// Default manufacture difficulty when product difficulty is not specified in catalog.
    /// This ensures consistent calculation even when catalog data is incomplete.
    /// </summary>
    public const double DEFAULT_MANUFACTURE_DIFFICULTY = 1.0;

    /// <summary>
    /// Minimum allowed page size for paginated results.
    /// </summary>
    public const int MIN_PAGE_SIZE = 1;

    /// <summary>
    /// Maximum allowed page size for paginated results.
    /// This limit prevents excessive memory usage and long processing times.
    /// </summary>
    public const int MAX_PAGE_SIZE = 100;

    /// <summary>
    /// Minimum allowed page number for pagination.
    /// Page numbers start from 1 (not 0-based).
    /// </summary>
    public const int MIN_PAGE_NUMBER = 1;
}