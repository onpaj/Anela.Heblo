using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Persistence.Catalog.Journal
{
    public class JournalRepository : BaseRepository<JournalEntry, int>, IJournalRepository
    {
        private readonly ILogger<JournalRepository> _logger;

        public JournalRepository(ApplicationDbContext context, ILogger<JournalRepository> logger)
            : base(context)
        {
            _logger = logger;
        }

        public override async Task<JournalEntry?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return await Context.Set<JournalEntry>()
                .Include(x => x.ProductAssociations)
                .Include(x => x.TagAssignments)
                    .ThenInclude(x => x.Tag)
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        }

        public async Task DeleteSoftAsync(int id, string userId, CancellationToken cancellationToken = default)
        {
            var entry = await GetByIdAsync(id, cancellationToken);
            if (entry != null)
            {
                entry.SoftDelete(userId);
                await UpdateAsync(entry, cancellationToken);
                await SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<PagedResult<JournalEntry>> GetEntriesAsync(
            JournalQueryCriteria criteria,
            CancellationToken cancellationToken = default)
        {
            var query = Context.Set<JournalEntry>()
                .Include(x => x.ProductAssociations)
                .Include(x => x.TagAssignments)
                    .ThenInclude(x => x.Tag)
                .Where(x => !x.IsDeleted)
                .AsQueryable();

            // Sorting
            query = criteria.SortBy?.ToLower() switch
            {
                "title" => criteria.SortDirection == "ASC"
                    ? query.OrderBy(x => x.Title)
                    : query.OrderByDescending(x => x.Title),
                "createdat" => criteria.SortDirection == "ASC"
                    ? query.OrderBy(x => x.CreatedAt)
                    : query.OrderByDescending(x => x.CreatedAt),
                _ => criteria.SortDirection == "ASC"
                    ? query.OrderBy(x => x.EntryDate)
                    : query.OrderByDescending(x => x.EntryDate)
            };

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .Skip((criteria.PageNumber - 1) * criteria.PageSize)
                .Take(criteria.PageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<JournalEntry>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = criteria.PageNumber,
                PageSize = criteria.PageSize
            };
        }

        public async Task<PagedResult<JournalEntry>> SearchEntriesAsync(
            JournalSearchCriteria criteria,
            CancellationToken cancellationToken = default)
        {
            var query = Context.Set<JournalEntry>()
                .Include(x => x.ProductAssociations)
                .Include(x => x.TagAssignments)
                    .ThenInclude(x => x.Tag)
                .Where(x => !x.IsDeleted)
                .AsQueryable();

            // Text search (simple contains for now, can be improved with full-text search)
            if (!string.IsNullOrEmpty(criteria.SearchText))
            {
                var searchTerm = criteria.SearchText.Trim().ToLower();
                query = query.Where(x =>
                    (x.Title != null && x.Title.ToLower().Contains(searchTerm)) ||
                    x.Content.ToLower().Contains(searchTerm));
            }

            // Date filtering
            if (criteria.DateFrom.HasValue)
            {
                query = query.Where(x => x.EntryDate >= criteria.DateFrom.Value.Date);
            }

            if (criteria.DateTo.HasValue)
            {
                query = query.Where(x => x.EntryDate <= criteria.DateTo.Value.Date);
            }

            // Product filtering - check if requested product code prefix starts with any stored prefix
            if (!string.IsNullOrEmpty(criteria.ProductCodePrefix))
            {
                query = query.Where(x => x.ProductAssociations
                    .Any(pa => criteria.ProductCodePrefix.StartsWith(pa.ProductCodePrefix)));
            }


            // Tag filtering
            if (criteria.TagIds?.Any() == true)
            {
                query = query.Where(x => x.TagAssignments
                    .Any(ta => criteria.TagIds.Contains(ta.TagId)));
            }

            // User filtering
            if (!string.IsNullOrEmpty(criteria.CreatedByUserId))
            {
                query = query.Where(x => x.CreatedByUserId == criteria.CreatedByUserId);
            }

            // Sorting
            query = criteria.SortBy?.ToLower() switch
            {
                "title" => criteria.SortDirection == "ASC"
                    ? query.OrderBy(x => x.Title)
                    : query.OrderByDescending(x => x.Title),
                "createdat" => criteria.SortDirection == "ASC"
                    ? query.OrderBy(x => x.CreatedAt)
                    : query.OrderByDescending(x => x.CreatedAt),
                _ => criteria.SortDirection == "ASC"
                    ? query.OrderBy(x => x.EntryDate)
                    : query.OrderByDescending(x => x.EntryDate)
            };

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .Skip((criteria.PageNumber - 1) * criteria.PageSize)
                .Take(criteria.PageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<JournalEntry>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = criteria.PageNumber,
                PageSize = criteria.PageSize
            };
        }

        public async Task<List<JournalEntry>> GetEntriesByProductAsync(
            string productCode,
            CancellationToken cancellationToken = default)
        {
            return await Context.Set<JournalEntry>()
                .Include(x => x.ProductAssociations)
                .Include(x => x.TagAssignments)
                    .ThenInclude(x => x.Tag)
                .Where(x => !x.IsDeleted && (x.ProductAssociations.Any(pa => productCode.StartsWith(pa.ProductCodePrefix))))
                .OrderByDescending(x => x.EntryDate)
                .ThenByDescending(x => x.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<Dictionary<string, JournalIndicator>> GetJournalIndicatorsAsync(
            IEnumerable<string> productCodes,
            CancellationToken cancellationToken = default)
        {
            var productCodeList = productCodes.ToList();
            var result = new Dictionary<string, JournalIndicator>();

            // Initialize all product codes
            foreach (var productCode in productCodeList)
            {
                result[productCode] = new JournalIndicator { ProductCode = productCode };
            }

            // Get direct associations
            var directAssociations = await Context.Set<JournalEntryProduct>()
                .Where(jep => productCodeList.Contains(jep.ProductCodePrefix))
                .Join(Context.Set<JournalEntry>().Where(je => !je.IsDeleted),
                    jep => jep.JournalEntryId,
                    je => je.Id,
                    (jep, je) => new { ProductCode = jep.ProductCodePrefix, je.EntryDate, je.CreatedAt })
                .GroupBy(x => x.ProductCode)
                .Select(g => new
                {
                    ProductCode = g.Key,
                    Count = g.Count(),
                    LastEntryDate = g.Max(x => x.EntryDate)
                })
                .ToListAsync(cancellationToken);

            foreach (var da in directAssociations)
            {
                result[da.ProductCode].DirectEntries = da.Count;
                result[da.ProductCode].LastEntryDate = da.LastEntryDate;
            }


            // Calculate recent entries (within last 30 days)
            var thirtyDaysAgo = DateTime.Today.AddDays(-30);
            foreach (var indicator in result.Values)
            {
                indicator.HasRecentEntries = indicator.LastEntryDate.HasValue &&
                                           indicator.LastEntryDate.Value >= thirtyDaysAgo;
            }

            return result;
        }
    }
}