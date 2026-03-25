namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static ReprintExpeditionListResponse Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
