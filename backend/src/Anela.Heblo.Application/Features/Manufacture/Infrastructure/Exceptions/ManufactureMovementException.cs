using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.Infrastructure.Exceptions;

/// <summary>
/// Base exception for manufacture stock movement errors.
/// Contains FlexiBee API error details and specific error codes.
/// </summary>
public abstract class ManufactureMovementException : Exception
{
    public ErrorCodes ErrorCode { get; }
    public string? FlexiBeeErrorMessage { get; }
    public string? ManufactureOrderCode { get; }

    protected ManufactureMovementException(
        string message,
        ErrorCodes errorCode,
        string? flexiBeeErrorMessage = null,
        string? manufactureOrderCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        FlexiBeeErrorMessage = flexiBeeErrorMessage;
        ManufactureOrderCode = manufactureOrderCode;
    }
}

/// <summary>
/// Exception thrown when consumption movement creation fails.
/// Prevents production movement from being attempted.
/// </summary>
public class ConsumptionMovementFailedException : ManufactureMovementException
{
    public ConsumptionMovementFailedException(
        string flexiBeeErrorMessage,
        string manufactureOrderCode,
        Exception? innerException = null)
        : base(
            $"Failed to create consumption stock movement: {flexiBeeErrorMessage}",
            ErrorCodes.ConsumptionMovementCreationFailed,
            flexiBeeErrorMessage,
            manufactureOrderCode,
            innerException)
    {
    }
}

/// <summary>
/// Exception thrown when production movement creation fails after consumption succeeds.
/// Includes reference to the created consumption movement for manual cleanup.
/// </summary>
public class ProductionMovementFailedException : ManufactureMovementException
{
    public string ConsumptionMovementReference { get; }

    public ProductionMovementFailedException(
        string flexiBeeErrorMessage,
        string manufactureOrderCode,
        string consumptionMovementReference,
        Exception? innerException = null)
        : base(
            $"Failed to create production stock movement: {flexiBeeErrorMessage}. Consumption movement created: {consumptionMovementReference}",
            ErrorCodes.ProductionMovementCreationFailed,
            flexiBeeErrorMessage,
            manufactureOrderCode,
            innerException)
    {
        ConsumptionMovementReference = consumptionMovementReference;
    }
}

/// <summary>
/// Exception thrown when manufacture submission fails due to validation or other errors.
/// </summary>
public class ManufactureSubmissionFailedException : ManufactureMovementException
{
    public ManufactureSubmissionFailedException(
        string message,
        string manufactureOrderCode,
        Exception? innerException = null)
        : base(
            message,
            ErrorCodes.ManufactureSubmissionFailed,
            null,
            manufactureOrderCode,
            innerException)
    {
    }
}
