namespace Anela.Heblo.Domain.Features.Logistics.Picking;

public class PrintPickingListResult
{
    public IList<string> ExportedFiles { get; set; } = new List<string>();
    public int TotalCount { get; set; }
    public IList<int> OrderIds { get; set; } = new List<int>();
}