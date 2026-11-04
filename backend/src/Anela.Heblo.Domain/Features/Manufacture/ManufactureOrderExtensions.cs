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

    /// <summary>
    /// True while lot and expiration still follow the planned date: before the semi-product is
    /// confirmed (Draft, Planned). From SemiProductManufactured on they are frozen.
    /// </summary>
    public static bool CanRecomputeLotAndExpiration(this ManufactureOrder manufactureOrder)
    {
        return manufactureOrder.State is ManufactureOrderState.Draft or ManufactureOrderState.Planned;
    }

    /// <summary>
    /// Sets the default lot and expiration, derived from <see cref="ManufactureOrder.PlannedDate"/>,
    /// on the semi-product and every product row. No-op for an order without a semi-product.
    /// </summary>
    public static void ApplyDefaultLotAndExpiration(this ManufactureOrder manufactureOrder)
    {
        if (manufactureOrder.SemiProduct == null)
        {
            return;
        }

        var lotNumber = GetDefaultLot(manufactureOrder.PlannedDate);
        var expirationDate = GetDefaultExpiration(manufactureOrder.PlannedDate, manufactureOrder.SemiProduct.ExpirationMonths);

        manufactureOrder.SemiProduct.LotNumber = lotNumber;
        manufactureOrder.SemiProduct.ExpirationDate = expirationDate;
        foreach (var product in manufactureOrder.Products)
        {
            product.LotNumber = lotNumber;
            product.ExpirationDate = expirationDate;
        }
    }

    public static DateOnly GetDefaultExpiration(DateTime manufactureDate, int months)
    {
        return GetDefaultExpiration(DateOnly.FromDateTime(manufactureDate), months);
    }

    /// <summary>
    /// Expiration = last day of the month that is <paramref name="months"/> + 1 months after the
    /// manufacture month. Works from the first of the month so no day is ever clamped
    /// (30 Oct + 30 months → 31 May, not 30 May).
    /// </summary>
    public static DateOnly GetDefaultExpiration(DateOnly manufactureDate, int months)
    {
        var expirationMonth = new DateOnly(manufactureDate.Year, manufactureDate.Month, 1).AddMonths(months + 1);
        return expirationMonth.AddMonths(1).AddDays(-1);
    }

    public static string GetDefaultLot(DateTime manufactureDate)
    {
        return GetDefaultLot(DateOnly.FromDateTime(manufactureDate));
    }

    public static string GetDefaultLot(DateOnly manufactureDate)
    {
        // Calculate lot number in wwyy format (ISO week + 2-digit year)
        var year = (manufactureDate.Year % 100).ToString("D2");
        var week = GetWeekNumber(manufactureDate.ToDateTime(TimeOnly.MinValue)).ToString("D2");
        return $"{week}{year}";
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