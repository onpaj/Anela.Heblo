using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ConfirmProductCompletionResult
{
    public bool Success { get; }
    public string? ErrorMessage { get; }
    public bool RequiresConfirmation { get; }
    public ResidueDistribution? Distribution { get; }

    public ConfirmProductCompletionResult(string errorMessage)
    {
        Success = false;
        ErrorMessage = errorMessage;
        RequiresConfirmation = false;
        Distribution = null;
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