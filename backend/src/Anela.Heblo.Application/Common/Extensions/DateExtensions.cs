namespace Anela.Heblo.Application.Common.Extensions;

public static class DateExtensions
{
    public static DateTime ToUtcDateTime(this DateOnly dateOnly)
    {
        return dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    }
}
