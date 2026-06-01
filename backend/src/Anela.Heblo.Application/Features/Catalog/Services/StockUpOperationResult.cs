using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public class StockUpOperationResult
{
    public StockUpResultStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public StockUpOperation? Operation { get; init; }
    public Exception? Exception { get; init; }
    public bool IsSuccess => Status == StockUpResultStatus.Success
                              || Status == StockUpResultStatus.AlreadyCompleted
                              || Status == StockUpResultStatus.AlreadyInShoptet;

    private StockUpOperationResult() { }

    public static StockUpOperationResult Success(StockUpOperation operation)
    {
        return new StockUpOperationResult
        {
            Status = StockUpResultStatus.Success,
            Message = "Stock up operation completed successfully",
            Operation = operation
        };
    }

    public static StockUpOperationResult AlreadyCompleted(StockUpOperation operation)
    {
        return new StockUpOperationResult
        {
            Status = StockUpResultStatus.AlreadyCompleted,
            Message = "Operation already completed",
            Operation = operation
        };
    }

    public static StockUpOperationResult PreviouslyFailed(StockUpOperation operation)
    {
        return new StockUpOperationResult
        {
            Status = StockUpResultStatus.PreviouslyFailed,
            Message = $"Operation previously failed: {operation.ErrorMessage}",
            Operation = operation
        };
    }

    public static StockUpOperationResult InProgress(StockUpOperation? operation)
    {
        return new StockUpOperationResult
        {
            Status = StockUpResultStatus.InProgress,
            Message = $"Operation already in progress (state: {operation?.State})",
            Operation = operation
        };
    }

    public static StockUpOperationResult AlreadyInShoptet(StockUpOperation operation)
    {
        return new StockUpOperationResult
        {
            Status = StockUpResultStatus.AlreadyInShoptet,
            Message = "Document already exists in Shoptet history",
            Operation = operation
        };
    }

    public static StockUpOperationResult SubmitFailed(StockUpOperation operation, Exception ex)
    {
        return new StockUpOperationResult
        {
            Status = StockUpResultStatus.Failed,
            Message = $"Submit failed: {ex.Message}",
            Operation = operation,
            Exception = ex
        };
    }

    public static StockUpOperationResult VerificationFailed(StockUpOperation operation)
    {
        return new StockUpOperationResult
        {
            Status = StockUpResultStatus.Failed,
            Message = "Verification failed: Record not found in Shoptet history after submission",
            Operation = operation
        };
    }

    public static StockUpOperationResult VerificationError(StockUpOperation operation, Exception ex)
    {
        return new StockUpOperationResult
        {
            Status = StockUpResultStatus.Failed,
            Message = $"Verification error: {ex.Message}",
            Operation = operation,
            Exception = ex
        };
    }
}

public enum StockUpResultStatus
{
    Success,
    AlreadyCompleted,
    AlreadyInShoptet,
    InProgress,
    PreviouslyFailed,
    Failed
}
