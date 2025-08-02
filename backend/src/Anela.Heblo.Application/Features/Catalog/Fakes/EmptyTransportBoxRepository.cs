using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Application.Features.Catalog.Fakes;

public class EmptyTransportBoxRepository : EmptyRepository<TransportBox, int>, ITransportBoxRepository
{
}