using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ConfirmProductCompletionResult
{
    public bool Success { get; }
    public string? ErrorMessage { get; }
    public bool RequiresConfirmation { get; }
    public ResidueDistribution? Distribution { get; }
    public ErrorCodes? ErrorCode { get; }
    public Dictionary<string, string>? Params { get; }

    public ConfirmProductCompletionResult(string errorMessage)
    {
        Success = false;
        ErrorMessage = errorMessage;
        RequiresConfirmation = false;
        Distribution = null;
    }

    public ConfirmProductCompletionResult(string errorMessage, ErrorCodes errorCode, Dictionary<string, string>? parameters)
        : this(errorMessage)
    {
        ErrorCode = errorCode;
        Params = parameters;
    }

    public ConfirmProductCompletionResult()
    {
        Success = true;
        ErrorMessage = null;
        RequiresConfirmation = false;
        Distribution = null;
    }

    private ConfirmProductCompletionResult(ResidueDistribution distribution)
    {
        Success = false;
        ErrorMessage = null;
        RequiresConfirmation = true;
        Distribution = distribution;
    }

    public static ConfirmProductCompletionResult NeedsConfirmation(ResidueDistribution distribution)
    {
        return new ConfirmProductCompletionResult(distribution);
    }
}