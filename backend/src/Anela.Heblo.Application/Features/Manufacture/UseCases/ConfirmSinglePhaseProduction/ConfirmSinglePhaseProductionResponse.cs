using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.ConfirmSinglePhaseProduction;

public class ConfirmSinglePhaseProductionResponse : BaseResponse
{
    public string? ErrorMessage { get; set; }
    public int OrderId { get; set; }
    public DateTime CompletedAt { get; set; }

    public static ConfirmSinglePhaseProductionResponse Successful(int orderId, DateTime completedAt)
    {
        return new ConfirmSinglePhaseProductionResponse
        {
            Success = true,
            OrderId = orderId,
            CompletedAt = completedAt
        };
    }

    public static ConfirmSinglePhaseProductionResponse Failed(string errorMessage)
    {
        return new ConfirmSinglePhaseProductionResponse
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}