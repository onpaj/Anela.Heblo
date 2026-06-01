namespace Anela.Heblo.Domain.Features.Manufacture;

public static class ManufactureOrderExtensions
{
    public static DateOnly GetDefaultExpiration(this ManufactureOrderSemiProduct manufactureOrderSemiProduct, DateTime manufactureDate)
    {
        return GetDefaultExpiration(manufactureDate, manufactureOrderSemiProduct.ExpirationMonths);
    }

    public static void SetDefaultExpiration(this ManufactureOrderSemiProduct manufactureOrderSemiProduct, DateTime manufactureDate)
    {
        manufactureOrderSemiProduct.ExpirationDate = GetDefaultExpiration(manufactureDate, manufactureOrderSemiProduct.ExpirationMonths);
    }

    public static void SetDefaultExpiration(this ManufactureOrderProduct manufactureOrderProduct, DateTime manufactureDate, int expirationMonths)
    {
        manufactureOrderProduct.ExpirationDate = GetDefaultExpiration(manufactureDate, expirationMonths);
    }

    public static void SetDefaultExpiration(this ManufactureOrder manufactureOrder, DateTime manufactureDate)
    {
        var expirationDate = GetDefaultExpiration(manufactureDate, manufactureOrder.SemiProduct.ExpirationMonths);
        manufactureOrder.SemiProduct.ExpirationDate = expirationDate;
        manufactureOrder.Products.ForEach(p => p.ExpirationDate = expirationDate);
    }


    public static string GetDefaultLot(this ManufactureOrderSemiProduct manufactureOrderSemiProduct, DateTime manufactureDate)
    {
        return GetDefaultLot(manufactureDate);
    }

    public static void SetDefaultLot(this ManufactureOrderSemiProduct manufactureOrderSemiProduct, DateTime manufactureDate)
    {
        manufactureOrderSemiProduct.LotNumber = GetDefaultLot(manufactureDate);
    }

    public static void SetDefaultLot(this ManufactureOrderProduct manufactureOrderProduct, DateTime manufactureDate)
    {
        manufactureOrderProduct.LotNumber = GetDefaultLot(manufactureDate);
    }

    public static void SetDefaultLot(this ManufactureOrder manufactureOrder, DateTime manufactureDate)
    {
        var lotNumber = GetDefaultLot(manufactureDate);
        manufactureOrder.SemiProduct.LotNumber = lotNumber;
        manufactureOrder.Products.ForEach(p => p.LotNumber = lotNumber);
    }

    public static DateOnly GetDefaultExpiration(DateTime manufactureDate, int months)
    {
        // Calculate lot number in wwyyyyMM format
        var year = manufactureDate.Year;
        var month = manufactureDate.Month.ToString("D2");
        var week = GetWeekNumber(manufactureDate).ToString("D2");
        var lotNumber = $"{week}{year}{month}";

        // Calculate expiration date (last day of month after adding expiration months)
        var expirationDate = manufactureDate.AddMonths(months);
        var lastDayOfExpirationMonth = DateOnly.FromDateTime(new DateTime(expirationDate.Year, expirationDate.Month, 1)
                .AddMonths(1) // Last day of month
                .AddDays(-1)
                .AddMonths(1) // Expiration is adjusted by one more month 
        );

        return lastDayOfExpirationMonth;
    }

    public static string GetDefaultLot(DateTime manufactureDate)
    {
        // Calculate lot number in wwyyyyMM format
        var year = manufactureDate.Year;
        var month = manufactureDate.Month.ToString("D2");
        var week = GetWeekNumber(manufactureDate).ToString("D2");
        return $"{week}{year}{month}";
    }

    private static int GetWeekNumber(DateTime date)
    {
        var d = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);
        var dayNum = (int)d.DayOfWeek;
        if (dayNum == 0) dayNum = 7; // Sunday should be 7, not 0
        d = d.AddDays(4 - dayNum);
        var yearStart = new DateTime(d.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (int)Math.Ceiling(((d - yearStart).TotalDays + 1) / 7);
    }
}