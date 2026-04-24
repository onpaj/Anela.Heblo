using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Persistence.Marketing
{
    public class MarketingActionRepository : BaseRepository<MarketingAction, int>, IMarketingActionRepository
    {
        private readonly ILogger<MarketingActionRepository> _logger;

        public MarketingActionRepository(ApplicationDbContext context, ILogger<MarketingActionRepository> logger)
            : base(context)
        {
            _logger = logger;
        }

        public override async Task<MarketingAction?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return await Context.Set<MarketingAction>()
                .Include(x => x.ProductAssociations)
                .Include(x => x.FolderLinks)
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        }

        public async Task DeleteSoftAsync(int id, string userId, string username, CancellationToken cancellationToken = default)
        {
            var entity = await GetByIdAsync(id, cancellationToken);
            if (entity != null)
            {
                entity.SoftDelete(userId, username);
                await UpdateAsync(entity, cancellationToken);
                await SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<PagedResult<MarketingAction>> GetPagedAsync(
            MarketingActionQueryCriteria criteria,
            CancellationToken cancellationToken = default)
        {
            var query = Context.Set<MarketingAction>()
                .Include(x => x.ProductAssociations)
                .Include(x => x.FolderLinks)
                .AsQueryable();

            if (!criteria.IncludeDeleted)
            {
                query = query.Where(x => !x.IsDeleted);
            }

            // Text search
            if (!string.IsNullOrWhiteSpace(criteria.SearchTerm))
            {
                var searchTerm = criteria.SearchTerm.Trim().ToLower();
                query = query.Where(x =>
                    x.Title.ToLower().Contains(searchTerm) ||
                    (x.Description != null && x.Description.ToLower().Contains(searchTerm)));
            }

            // Action type filter
            if (criteria.ActionType.HasValue)
            {
                query = query.Where(x => x.ActionType == criteria.ActionType.Value);
            }

            // Date range filters
            if (criteria.StartDateFrom.HasValue)
            {
                query = query.Where(x => x.StartDate >= criteria.StartDateFrom.Value);
            }

            if (criteria.StartDateTo.HasValue)
            {
                query = query.Where(x => x.StartDate <= criteria.StartDateTo.Value);
            }

            if (criteria.EndDateFrom.HasValue)
            {
                query = query.Where(x => x.EndDate.HasValue && x.EndDate >= criteria.EndDateFrom.Value);
            }

            if (criteria.EndDateTo.HasValue)
            {
                query = query.Where(x => x.EndDate.HasValue && x.EndDate <= criteria.EndDateTo.Value);
            }

            // Product code prefix filter
            if (!string.IsNullOrWhiteSpace(criteria.ProductCodePrefix))
            {
                var prefix = criteria.ProductCodePrefix.Trim();
                query = query.Where(x => x.ProductAssociations
                    .Any(pa => prefix.StartsWith(pa.ProductCodePrefix)));
            }

            // Default sort: newest first by StartDate
            query = query.OrderByDescending(x => x.StartDate)
                .ThenByDescending(x => x.CreatedAt);

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .Skip((criteria.PageNumber - 1) * criteria.PageSize)
                .Take(criteria.PageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<MarketingAction>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = criteria.PageNumber,
                PageSize = criteria.PageSize
            };
        }

        public async Task<List<MarketingAction>> GetForCalendarAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default)
        {
            return await Context.Set<MarketingAction>()
                .Include(x => x.ProductAssociations)
                .Include(x => x.FolderLinks)
                .Where(x => !x.IsDeleted &&
                    x.StartDate <= to &&
                    (!x.EndDate.HasValue || x.EndDate >= from))
                .OrderBy(x => x.StartDate)
                .ToListAsync(cancellationToken);
        }
    }
}
