using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public class StockUpOperation : Entity<int>
{
    public string DocumentNumber { get; private set; }
    public string ProductCode { get; private set; }
    public int Amount { get; private set; }
    public StockUpSourceType SourceType { get; private set; }
    public int SourceId { get; private set; }
    public StockUpOperationState State { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? ErrorMessage { get; private set; }

    // Private constructor for EF Core
    private StockUpOperation()
    {
        DocumentNumber = string.Empty;
        ProductCode = string.Empty;
    }

    public StockUpOperation(
        string documentNumber,
        string productCode,
        int amount,
        StockUpSourceType sourceType,
        int sourceId)
    {
        if (string.IsNullOrWhiteSpace(documentNumber))
            throw new ValidationException("DocumentNumber is required");
        if (string.IsNullOrWhiteSpace(productCode))
            throw new ValidationException("ProductCode is required");
        if (amount == 0)
            throw new ValidationException("Amount cannot be zero");

        DocumentNumber = documentNumber;
        ProductCode = productCode;
        Amount = amount;
        SourceType = sourceType;
        SourceId = sourceId;
        State = StockUpOperationState.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    public void MarkAsSubmitted(DateTime timestamp)
    {
        if (State != StockUpOperationState.Pending)
            throw new InvalidOperationException($"Cannot mark as Submitted from {State} state");

        State = StockUpOperationState.Submitted;
        SubmittedAt = timestamp;
    }

    public void MarkAsVerified(DateTime timestamp)
    {
        if (State != StockUpOperationState.Submitted)
            throw new InvalidOperationException($"Cannot mark as Verified from {State} state");

        State = StockUpOperationState.Verified;
        VerifiedAt = timestamp;
    }

    public void MarkAsCompleted(DateTime timestamp)
    {
        if (State != StockUpOperationState.Verified && State != StockUpOperationState.Pending)
            throw new InvalidOperationException($"Cannot mark as Completed from {State} state");

        State = StockUpOperationState.Completed;
        CompletedAt = timestamp;
    }

    public void MarkAsFailed(DateTime timestamp, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new ValidationException("Error message is required when marking operation as failed");

        State = StockUpOperationState.Failed;
        CompletedAt = timestamp;
        ErrorMessage = errorMessage;
    }

    public void Reset()
    {
        if (State != StockUpOperationState.Failed)
            throw new InvalidOperationException($"Can only reset Failed operations, current state: {State}");

        State = StockUpOperationState.Pending;
        SubmittedAt = null;
        VerifiedAt = null;
        CompletedAt = null;
        ErrorMessage = null;
    }
}
