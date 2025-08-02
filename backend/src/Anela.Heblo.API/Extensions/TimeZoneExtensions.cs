namespace Anela.Heblo.API.Extensions;

public static class TimeZoneExtensions
{
    public static void ConfigureApplicationTimeZone(this IConfiguration configuration)
    {
        // Get timezone ID from configuration or environment variable
        var timeZoneId = Environment.GetEnvironmentVariable("TIMEZONE")
                        ?? configuration["Application:TimeZone"]
                        ?? "Central Europe Standard Time";

        // Set system timezone environment variable for consistent behavior
        var systemTimeZoneId = GetSystemTimeZoneId(timeZoneId);
        Environment.SetEnvironmentVariable("TZ", systemTimeZoneId);
        TimeZoneInfo.ClearCachedData(); // Clear any cached timezone data to force reload
    }

    private static string GetSystemTimeZoneId(string windowsTimeZoneId)
    {
        // Map Windows timezone IDs to IANA timezone IDs for cross-platform compatibility
        return windowsTimeZoneId switch
        {
            "Central Europe Standard Time" => "Europe/Prague",
            "Central European Standard Time" => "Europe/Prague",
            _ => "Europe/Prague" // Default fallback
        };
    }
}