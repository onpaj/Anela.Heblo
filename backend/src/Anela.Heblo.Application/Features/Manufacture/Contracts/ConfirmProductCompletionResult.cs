namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ConfirmProductCompletionResult
{
    public bool Success { get; }
    public string Message { get; }

    public ConfirmProductCompletionResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }
}