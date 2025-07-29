using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Logistics.Picking.Model;

namespace Anela.Heblo.Logistics.Picking;

public interface IPickingListSource
{
    Task<PrintPickingListResult> CreatePickingList(PrintPickingListRequest request, CancellationToken cancellationToken = default);
}