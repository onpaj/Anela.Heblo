using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public class ServiceMarginCalculationResult
{
    public bool IsSuccess { get; private set; }
    public MarginData? Data { get; private set; }
    public ErrorCodes? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    private ServiceMarginCalculationResult(bool isSuccess, MarginData? data, ErrorCodes? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Data = data;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public static ServiceMarginCalculationResult Success(MarginData data)
    {
        return new ServiceMarginCalculationResult(true, data, null, null);
    }

    public static ServiceMarginCalculationResult Failure(ErrorCodes errorCode, string? errorMessage = null)
    {
        return new ServiceMarginCalculationResult(false, null, errorCode, errorMessage);
    }
}