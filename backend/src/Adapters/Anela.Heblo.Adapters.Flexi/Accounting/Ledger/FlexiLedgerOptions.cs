namespace Anela.Heblo.Adapters.Flexi.Accounting.Ledger;

public class FlexiLedgerOptions
{
    public const string SectionName = "FlexiLedger";

    public int ClosedPeriodCacheMinutes { get; set; } = 360;

    public int OpenPeriodCacheMinutes { get; set; } = 15;
}
