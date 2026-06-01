using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ConfirmSemiProductManufactureResult
{
    public bool Success { get; }
    public string Message { get; }
    public ErrorCodes? ErrorCode { get; }

    public ConfirmSemiProductManufactureResult(bool success, string message, ErrorCodes? errorCode = null)
    {
        Success = success;
        Message = message;
        ErrorCode = errorCode;
    }
}
