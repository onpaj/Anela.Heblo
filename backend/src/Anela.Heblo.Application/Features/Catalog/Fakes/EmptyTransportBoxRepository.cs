using Anela.Heblo.Application.Domain.Logistics.Transport;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Application.features.catalog;

public class EmptyTransportBoxRepository : EmptyRepository<TransportBox, int>, ITransportBoxRepository
{
}