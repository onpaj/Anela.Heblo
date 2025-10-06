namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ConfirmProductCompletionResult
{
    public bool Success { get; }
    public string? ErrorMessage { get; }

    public ConfirmProductCompletionResult(string errorMessage)
    {
        Success = false;
        ErrorMessage = errorMessage;
    }

    public ConfirmProductCompletionResult()
    {
        Success = true;
        ErrorMessage = null;
    }
}