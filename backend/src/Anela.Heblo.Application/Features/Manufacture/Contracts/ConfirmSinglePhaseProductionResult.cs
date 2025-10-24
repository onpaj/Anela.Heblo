namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ConfirmSinglePhaseProductionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int OrderId { get; set; }
    public DateTime CompletedAt { get; set; }

    public static ConfirmSinglePhaseProductionResult Successful(int orderId, DateTime completedAt)
    {
        return new ConfirmSinglePhaseProductionResult
        {
            Success = true,
            OrderId = orderId,
            CompletedAt = completedAt
        };
    }

    public static ConfirmSinglePhaseProductionResult Failed(string errorMessage)
    {
        return new ConfirmSinglePhaseProductionResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}