using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Catalog.ProductIngredientOrder;

public class ProductIngredientOrderRepository
    : BaseRepository<Domain.Features.Catalog.ProductIngredientOrder, int>,
      IProductIngredientOrderRepository
{
    public ProductIngredientOrderRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<List<Domain.Features.Catalog.ProductIngredientOrder>> ListByParentAsync(
        string parentProductCode,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(x => x.ParentProductCode == parentProductCode)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<Domain.Features.Catalog.ProductIngredientOrder> CreateAsync(
        Domain.Features.Catalog.ProductIngredientOrder entity,
        CancellationToken cancellationToken = default)
    {
        return await AddAsync(entity, cancellationToken);
    }

    public async Task<Domain.Features.Catalog.ProductIngredientOrder> UpdateAsync(
        Domain.Features.Catalog.ProductIngredientOrder entity,
        CancellationToken cancellationToken = default)
    {
        var tracked = Context.ChangeTracker.Entries<Domain.Features.Catalog.ProductIngredientOrder>()
            .FirstOrDefault(e => e.Entity.Id == entity.Id && !ReferenceEquals(e.Entity, entity));
        if (tracked != null)
        {
            tracked.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        }

        await base.UpdateAsync(entity, cancellationToken);
        return entity;
    }
}
