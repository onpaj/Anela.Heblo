using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ProcessReceivedBoxes;

public class ProcessReceivedBoxesResponse : BaseResponse
{
    public int ProcessedBoxesCount { get; set; }
    public int OperationsCreatedCount { get; set; }  // Changed from SuccessfulBoxesCount
    public int FailedBoxesCount { get; set; }
    public string BatchId { get; set; } = string.Empty;
    public List<string> FailedBoxCodes { get; set; } = new();
}