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
    /// The live values belong in appsettings.json; an empty set is rejected at read time
    /// rather than silently returning no history.
    ///
    /// Defaults to empty because ConfigurationBinder MERGES arrays index-wise instead of
    /// replacing them, so a non-empty default plus the appsettings values would bind to
    /// {54,56,65,67,54,56,65,67}. That same merge also applies to any environment or Key
    /// Vault override, which an empty default cannot prevent, so the reader de-duplicates
    /// before fetching — duplicates would otherwise double every manufactured amount at
    /// the group-by, with no error anywhere.
    /// </summary>
    public int[] ManufactureDocumentTypeIds { get; set; } = Array.Empty<int>();

    // Low stock alert tile configuration
    public double ResupplyThresholdMultiplier { get; set; } = 1.3;
    public int InvoiceClassificationDaysBack { get; set; } = 30;
    public string InvoiceClassificationTriggerLabel { get; set; } = "KLASIFIKACE";
    public string InvoiceClassificationManualReviewLabel { get; set; } = "MANUAL-KLASIF";
}