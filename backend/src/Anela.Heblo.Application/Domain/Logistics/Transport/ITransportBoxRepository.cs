using System.Linq.Expressions;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Application.Domain.Logistics.Transport;

public interface ITransportBoxRepository : IRepository<TransportBox, int>
{
}