namespace Anela.Heblo.Application.Domain.Logistics.Picking;

public class PrintPickingListResult
{
    public IList<string> ExportedFiles { get; set; } = new List<string>();
    public int TotalCount { get; set; }
}