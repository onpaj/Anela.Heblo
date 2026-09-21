namespace Anela.Heblo.Application.Common;

public class DataSourceOptions
{
    public const string ConfigKey = "DataSourceOptions";
    public int SalesHistoryDays { get; set; } = 400;
    public int PurchaseHistoryDays { get; set; } = 400;
    public int ConsumedHistoryDays { get; set; } = 720;
    public int ManufactureHistoryDays { get; set; } = 400;

    public int ManufactureCostHistoryDays { get; set; } = 400;

    /// <summary>
    /// FlexiBee typ-skladovy-pohyb ids whose receipts count as manufacture output.
    /// Kept in configuration because the ERP has already renumbered these once: on
    /// 2026-03-24 production switched from 54/56 to 65/67, and the hardcoded reader
    /// silently dropped six months of history before anyone noticed.
    ///   54 = VYROBA-POLOTOVAR     (semi-product, retired 2026-03-24)
    ///   56 = VYROBA-PRODUKT       (product, retired 2026-03-24)
    ///   65 = V-PRIJEM-POLOTOVAR   (semi-product, current)
    ///   67 = V-PRIJEM-VYROBEK     (product, current)
    ///
    /// Deliberately defaults to empty: ConfigurationBinder APPENDS to an existing array
    /// rather than replacing it, so a non-empty default plus the appsettings values would
    /// bind to {54,56,65,67,54,56,65,67} and double every manufactured amount at the
    /// group-by. The live values belong in appsettings.json; an empty set is rejected at
    /// read time rather than silently returning no history.
    /// </summary>
    public int[] ManufactureDocumentTypeIds { get; set; } = Array.Empty<int>();

    // Low stock alert tile configuration
    public double ResupplyThresholdMultiplier { get; set; } = 1.3;
    public int InvoiceClassificationDaysBack { get; set; } = 30;
    public string InvoiceClassificationTriggerLabel { get; set; } = "KLASIFIKACE";
    public string InvoiceClassificationManualReviewLabel { get; set; } = "MANUAL-KLASIF";
}