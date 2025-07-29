using System.Collections.Generic;

namespace Anela.Heblo.Logistics.Picking.Model;

public class PrintPickingListResult
{
    public IList<string> ExportedFiles { get; set; } = new List<string>();
    public int TotalCount { get; set; }
}