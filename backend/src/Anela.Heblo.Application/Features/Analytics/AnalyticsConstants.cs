namespace Anela.Heblo.Application.Features.Analytics;

/// <summary>
/// Constants used throughout the Analytics module
/// </summary>
public static class AnalyticsConstants
{
    // Report period constraints
    public const int MAX_REPORT_PERIOD_DAYS = 730; // 2 years
    public const int MIN_REPORT_PERIOD_DAYS = 1;

    // Product limits
    public const int DEFAULT_MAX_PRODUCTS = 100;
    public const int ABSOLUTE_MAX_PRODUCTS = 1000;

    // Date format for error messages
    public const string DATE_FORMAT = "yyyy-MM-dd";

    // Default category name
    public const string DEFAULT_CATEGORY = "Uncategorized";

    // Validation messages
    public static class ValidationMessages
    {
        public const string INVALID_DATE_RANGE = "Start date must be before or equal to end date";
        public const string PERIOD_TOO_LONG = "Report period cannot exceed {0} days";
        public const string PERIOD_TOO_SHORT = "Report period must be at least {0} day(s)";
        public const string PRODUCT_ID_REQUIRED = "ProductId is required";
        public const string MAX_PRODUCTS_EXCEEDED = "MaxProducts cannot exceed {0}";
        public const string MAX_PRODUCTS_MINIMUM = "MaxProducts must be at least 1";
    }
}