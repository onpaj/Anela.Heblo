namespace Anela.Heblo.Domain.Features.Analytics;

/// <summary>
/// Enum defining which date field to use for bank statement import statistics
/// </summary>
public enum BankStatementDateType
{
    /// <summary>
    /// Use StatementDate (date of the bank statement)
    /// </summary>
    StatementDate,

    /// <summary>
    /// Use ImportDate (when statement was imported into our system)
    /// </summary>
    ImportDate
}
