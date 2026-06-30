using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Bank;

/// <summary>
/// Per-account watermark for incremental bank statement import.
/// LastValidImportDate is the date (inclusive) through which all statements for the
/// account have been successfully imported. A run importing zero documents still
/// advances it. Null until the first successful run.
/// </summary>
public class BankImportState : Entity<string>
{
    public const string StatusOk = "OK";
    public const string StatusError = "ERROR";

    [Required]
    [MaxLength(100)]
    public string Account { get; private set; }

    public DateTime? LastValidImportDate { get; private set; }
    public DateTime? LastRunStartedAt { get; private set; }
    public DateTime? LastRunFinishedAt { get; private set; }

    [MaxLength(20)]
    public string? LastRunStatus { get; private set; }

    [MaxLength(2000)]
    public string? LastErrorMessage { get; private set; }

    public int ConsecutiveFailureCount { get; private set; }

    // Private constructor for EF Core
    private BankImportState()
    {
        Account = string.Empty;
    }

    public BankImportState(string account)
    {
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("Account is required", nameof(account));

        Account = account;
        Id = account; // Account is the primary key
    }

    public void RecordSuccess(DateTime watermark, DateTime runStartedAt, DateTime runFinishedAt)
    {
        LastValidImportDate = watermark.Date;
        LastRunStartedAt = runStartedAt;
        LastRunFinishedAt = runFinishedAt;
        LastRunStatus = StatusOk;
        LastErrorMessage = null;
        ConsecutiveFailureCount = 0;
    }

    public void RecordFailure(string error, DateTime runStartedAt, DateTime runFinishedAt)
    {
        LastRunStartedAt = runStartedAt;
        LastRunFinishedAt = runFinishedAt;
        LastRunStatus = StatusError;
        LastErrorMessage = error.Length > 2000 ? error[..2000] : error;
        ConsecutiveFailureCount += 1;
    }
}
