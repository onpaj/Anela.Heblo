namespace Anela.Heblo.Application.Domain.Users;

/// <summary>
/// Extension methods for TimeProvider to handle time display and localization
/// </summary>
public static class TimeProviderExtensions
{
    /// <summary>
    /// Gets the current local time for display purposes
    /// </summary>
    public static DateTime GetLocalTime(this TimeProvider timeProvider)
    {
        return timeProvider.GetLocalNow().DateTime;
    }

    /// <summary>
    /// Converts UTC DateTime to local time for display
    /// </summary>
    public static DateTime ToLocalTime(this TimeProvider timeProvider, DateTime utcDateTime)
    {
        if (utcDateTime.Kind == DateTimeKind.Utc)
        {
            return timeProvider.GetLocalNow().DateTime.AddTicks(utcDateTime.Ticks - timeProvider.GetUtcNow().Ticks);
        }
        
        // Assume UTC if kind is unspecified
        return timeProvider.GetLocalNow().DateTime.AddTicks(utcDateTime.Ticks - timeProvider.GetUtcNow().Ticks);
    }

    /// <summary>
    /// Formats UTC DateTime for display in local timezone
    /// </summary>
    public static string FormatForDisplay(this TimeProvider timeProvider, DateTime utcDateTime, string format = "yyyy-MM-dd HH:mm:ss")
    {
        return timeProvider.ToLocalTime(utcDateTime).ToString(format);
    }

    /// <summary>
    /// Gets display-friendly filename timestamp in local time
    /// </summary>
    public static string GetFilenameTimestamp(this TimeProvider timeProvider)
    {
        return timeProvider.GetLocalTime().ToString("yyyy-MM-ddTHHmmss");
    }
}