using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetBankStatementImportStatistics;

/// <summary>
/// Request for getting bank statement import statistics for monitoring
/// </summary>
public class GetBankStatementImportStatisticsRequest : IRequest<GetBankStatementImportStatisticsResponse>
{
    /// <summary>
    /// Start date for statistics (optional)
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// End date for statistics (optional)
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Type of date to use for grouping statistics
    /// </summary>
    public BankStatementDateType DateType { get; set; } = BankStatementDateType.ImportDate;
}

/// <summary>
/// Enum defining which date field to use for statistics
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