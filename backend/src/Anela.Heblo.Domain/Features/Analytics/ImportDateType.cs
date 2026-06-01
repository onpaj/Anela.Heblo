namespace Anela.Heblo.Domain.Features.Analytics;

/// <summary>
/// Enum defining which date field to use for invoice import statistics
/// </summary>
public enum ImportDateType
{
    /// <summary>
    /// Use InvoiceDate (when invoice was issued)
    /// </summary>
    InvoiceDate,

    /// <summary>
    /// Use LastSyncTime (when invoice was imported into our system)
    /// </summary>
    LastSyncTime
}
