namespace Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;

public sealed class BankImportWatermarkOptions
{
    public const string SectionName = "BankImportWatermark";

    /// <summary>Maximum number of days a stale watermark may look back. Older data is not imported.</summary>
    public int MaxBackfillDays { get; set; } = 14;

    /// <summary>Watermark lag (in days) above which a Warning is logged.</summary>
    public int StaleWarningDays { get; set; } = 3;
}
