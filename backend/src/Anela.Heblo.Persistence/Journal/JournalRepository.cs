using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Persistence.Repositories;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Persistence.Journal
{
    public class JournalRepository : BaseRepository<JournalEntry, int>, IJournalRepository
    {
        private readonly ILogger<JournalRepository> _logger;
        private const int RecentEntriesDays = 30;

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
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        }

        public async Task<PagedResult<JournalEntry>> GetEntriesAsync(
            int pageNumber,
            int pageSize,
            string sortBy,
            string sortDirection,
            CancellationToken cancellationToken = default)
        {
            var query = Context.Set<JournalEntry>()
                .Include(x => x.ProductAssociations)
                .Include(x => x.TagAssignments)
                    .ThenInclude(x => x.Tag)
                .AsQueryable();

            // Sorting
            query = ApplySort(query, sortBy, sortDirection, _logger);

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<JournalEntry>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<PagedResult<JournalEntry>> SearchEntriesAsync(
            string? searchText,
            DateTime? dateFrom,
            DateTime? dateTo,
            string? productCodePrefix,
            IReadOnlyCollection<int>? tagIds,
            string? createdByUserId,
            int pageNumber,
            int pageSize,
            string sortBy,
            string sortDirection,
            CancellationToken cancellationToken = default)
        {
            var query = Context.Set<JournalEntry>()
                .Include(x => x.ProductAssociations)
                .Include(x => x.TagAssignments)
                    .ThenInclude(x => x.Tag)
                .AsQueryable();

            // Text search (simple contains for now, can be improved with full-text search)
            if (!string.IsNullOrEmpty(searchText))
            {
                var searchTerm = searchText.Trim().ToLower();
                query = query.Where(x =>
                    (x.Title != null && x.Title.ToLower().Contains(searchTerm)) ||
                    x.Content.ToLower().Contains(searchTerm));
            }

            // Date filtering
            if (dateFrom.HasValue)
            {
                query = query.Where(x => x.EntryDate >= dateFrom.Value.Date);
            }

            if (dateTo.HasValue)
            {
                query = query.Where(x => x.EntryDate <= dateTo.Value.Date);
            }

            // Product filtering - check if requested product code prefix starts with any stored prefix
            if (!string.IsNullOrEmpty(productCodePrefix))
            {
                query = query.Where(x => x.ProductAssociations
                    .Any(pa => productCodePrefix.StartsWith(pa.ProductCodePrefix)));
            }


            // Tag filtering
            if (tagIds?.Any() == true)
            {
                query = query.Where(x => x.TagAssignments
                    .Any(ta => tagIds.Contains(ta.TagId)));
            }

            // User filtering
            if (!string.IsNullOrEmpty(createdByUserId))
            {
                query = query.Where(x => x.CreatedByUserId == createdByUserId);
            }

            // Sorting
            query = ApplySort(query, sortBy, sortDirection, _logger);

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<JournalEntry>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
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
                .Where(x => x.ProductAssociations.Any(pa => productCode.StartsWith(pa.ProductCodePrefix)))
                .OrderByDescending(x => x.EntryDate)
                .ThenByDescending(x => x.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<Dictionary<string, JournalIndicatorSnapshot>> GetJournalIndicatorsAsync(
            IEnumerable<string> productCodes,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(productCodes);
            var productCodeList = productCodes.ToList();

            // Aggregate direct associations into a per-product accumulator.
            var directAssociations = await Context.Set<JournalEntryProduct>()
                .Where(jep => productCodeList.Contains(jep.ProductCodePrefix))
                .Join(Context.Set<JournalEntry>(),
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

            var aggregatesByProduct = directAssociations.ToDictionary(x => x.ProductCode);

            var thirtyDaysAgo = DateTime.Today.AddDays(-RecentEntriesDays);
            var result = new Dictionary<string, JournalIndicatorSnapshot>(productCodeList.Count);

            foreach (var productCode in productCodeList)
            {
                if (aggregatesByProduct.TryGetValue(productCode, out var aggregate))
                {
                    var hasRecentEntries = aggregate.LastEntryDate >= thirtyDaysAgo;
                    result[productCode] = new JournalIndicatorSnapshot(
                        DirectEntries: aggregate.Count,
                        LastEntryDate: aggregate.LastEntryDate,
                        HasRecentEntries: hasRecentEntries);
                }
                else
                {
                    result[productCode] = new JournalIndicatorSnapshot(
                        DirectEntries: 0,
                        LastEntryDate: null,
                        HasRecentEntries: false);
                }
            }

            return result;
        }

        private static IQueryable<JournalEntry> ApplySort(
            IQueryable<JournalEntry> query,
            string? sortBy,
            string sortDirection,
            ILogger logger)
        {
            var ascending = string.Equals(sortDirection, "ASC", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(sortBy))
            {
                return ApplyDefaultSort(query, ascending);
            }

            return sortBy.ToLowerInvariant() switch
            {
                "title" => ascending
                    ? query.OrderBy(x => x.Title)
                    : query.OrderByDescending(x => x.Title),

                "createdbyusername" => ascending
                    ? query.OrderBy(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate)
                    : query.OrderByDescending(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate),

                _ => ApplyDefaultSortWithWarning(query, ascending, sortBy, logger),
            };
        }

        private static IQueryable<JournalEntry> ApplyDefaultSort(
            IQueryable<JournalEntry> query,
            bool ascending)
        {
            return ascending
                ? query.OrderBy(x => x.EntryDate)
                : query.OrderByDescending(x => x.EntryDate);
        }

        private static IQueryable<JournalEntry> ApplyDefaultSortWithWarning(
            IQueryable<JournalEntry> query,
            bool ascending,
            string sortBy,
            ILogger logger)
        {
            logger.LogWarning(
                "Unknown sort key {SortBy} requested on {Repository}",
                sortBy,
                nameof(JournalRepository));

            return ApplyDefaultSort(query, ascending);
        }
    }
}
