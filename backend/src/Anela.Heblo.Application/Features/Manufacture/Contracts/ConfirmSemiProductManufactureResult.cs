namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ConfirmSemiProductManufactureResult
{
    public bool Success { get; }
    public string Message { get; }

    public ConfirmSemiProductManufactureResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }
}