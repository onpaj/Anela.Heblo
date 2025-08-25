namespace Anela.Heblo.Application.Common.Extensions;

public static class DateExtensions
{
    public static DateTime ToUtcDateTime(this DateOnly dateOnly)
    {
        return dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    }

    public static DateTime ToUtcDateTime(this string dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString))
        {
            throw new ArgumentNullException(nameof(dateString), "Date string cannot be null or empty");
        }

        // Parse the date and ensure it's treated as UTC
        var date = DateTime.Parse(dateString);
        return DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
    }

    public static DateTime? ToUtcDateTimeOrNull(this string? dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString))
        {
            return null;
        }

        return dateString.ToUtcDateTime();
    }
}