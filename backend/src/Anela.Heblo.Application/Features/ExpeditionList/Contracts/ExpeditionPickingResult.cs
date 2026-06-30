namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public class ExpeditionPickingResult
{
    public IList<string> ExportedFiles { get; set; } = new List<string>();
    public int TotalCount { get; set; }
    public int SkippedCount { get; set; }
}
