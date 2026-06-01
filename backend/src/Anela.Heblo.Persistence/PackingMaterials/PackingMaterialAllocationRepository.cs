using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Persistence.Repositories;

namespace Anela.Heblo.Persistence.PackingMaterials;

public class PackingMaterialAllocationRepository : BaseRepository<PackingMaterialAllocation, int>, IPackingMaterialAllocationRepository
{
    public PackingMaterialAllocationRepository(ApplicationDbContext context) : base(context)
    {
    }
}
