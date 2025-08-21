using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Journal.Infrastructure
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
                .Include(x => x.ProductFamilyAssociations)
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
            GetJournalEntriesRequest request,
            CancellationToken cancellationToken = default)
        {
            var query = Context.Set<JournalEntry>()
                .Include(x => x.ProductAssociations)
                .Include(x => x.ProductFamilyAssociations)
                .Include(x => x.TagAssignments)
                    .ThenInclude(x => x.Tag)
                .Where(x => !x.IsDeleted)
                .AsQueryable();

            // Sorting
            query = request.SortBy?.ToLower() switch
            {
                "title" => request.SortDirection == "ASC"
                    ? query.OrderBy(x => x.Title)
                    : query.OrderByDescending(x => x.Title),
                "createdat" => request.SortDirection == "ASC"
                    ? query.OrderBy(x => x.CreatedAt)
                    : query.OrderByDescending(x => x.CreatedAt),
                _ => request.SortDirection == "ASC"
                    ? query.OrderBy(x => x.EntryDate)
                    : query.OrderByDescending(x => x.EntryDate)
            };

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<JournalEntry>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            };
        }

        public async Task<PagedResult<JournalEntry>> SearchEntriesAsync(
            SearchJournalEntriesRequest request,
            CancellationToken cancellationToken = default)
        {
            var query = Context.Set<JournalEntry>()
                .Include(x => x.ProductAssociations)
                .Include(x => x.ProductFamilyAssociations)
                .Include(x => x.TagAssignments)
                    .ThenInclude(x => x.Tag)
                .Where(x => !x.IsDeleted)
                .AsQueryable();

            // Text search (simple contains for now, can be improved with full-text search)
            if (!string.IsNullOrEmpty(request.SearchText))
            {
                var searchTerm = request.SearchText.Trim().ToLower();
                query = query.Where(x =>
                    (x.Title != null && x.Title.ToLower().Contains(searchTerm)) ||
                    x.Content.ToLower().Contains(searchTerm));
            }

            // Date filtering
            if (request.DateFrom.HasValue)
            {
                query = query.Where(x => x.EntryDate >= request.DateFrom.Value.Date);
            }

            if (request.DateTo.HasValue)
            {
                query = query.Where(x => x.EntryDate <= request.DateTo.Value.Date);
            }

            // Product filtering
            if (request.ProductCodes?.Any() == true)
            {
                query = query.Where(x => x.ProductAssociations
                    .Any(pa => request.ProductCodes.Contains(pa.ProductCode)));
            }

            // Product family filtering
            if (request.ProductCodePrefixes?.Any() == true)
            {
                query = query.Where(x => x.ProductFamilyAssociations
                    .Any(pfa => request.ProductCodePrefixes.Contains(pfa.ProductCodePrefix)));
            }

            // Tag filtering
            if (request.TagIds?.Any() == true)
            {
                query = query.Where(x => x.TagAssignments
                    .Any(ta => request.TagIds.Contains(ta.TagId)));
            }

            // User filtering
            if (!string.IsNullOrEmpty(request.CreatedByUserId))
            {
                query = query.Where(x => x.CreatedByUserId == request.CreatedByUserId);
            }

            // Sorting
            query = request.SortBy?.ToLower() switch
            {
                "title" => request.SortDirection == "ASC"
                    ? query.OrderBy(x => x.Title)
                    : query.OrderByDescending(x => x.Title),
                "createdat" => request.SortDirection == "ASC"
                    ? query.OrderBy(x => x.CreatedAt)
                    : query.OrderByDescending(x => x.CreatedAt),
                _ => request.SortDirection == "ASC"
                    ? query.OrderBy(x => x.EntryDate)
                    : query.OrderByDescending(x => x.EntryDate)
            };

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<JournalEntry>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            };
        }

        public async Task<List<JournalEntry>> GetEntriesByProductAsync(
            string productCode,
            CancellationToken cancellationToken = default)
        {
            return await Context.Set<JournalEntry>()
                .Include(x => x.ProductAssociations)
                .Include(x => x.ProductFamilyAssociations)
                .Include(x => x.TagAssignments)
                    .ThenInclude(x => x.Tag)
                .Where(x => !x.IsDeleted &&
                    (x.ProductAssociations.Any(pa => pa.ProductCode == productCode) ||
                     x.ProductFamilyAssociations.Any(pfa => productCode.StartsWith(pfa.ProductCodePrefix))))
                .OrderByDescending(x => x.EntryDate)
                .ThenByDescending(x => x.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<Dictionary<string, JournalIndicatorDto>> GetJournalIndicatorsAsync(
            IEnumerable<string> productCodes,
            CancellationToken cancellationToken = default)
        {
            var productCodeList = productCodes.ToList();
            var result = new Dictionary<string, JournalIndicatorDto>();

            // Initialize all product codes
            foreach (var productCode in productCodeList)
            {
                result[productCode] = new JournalIndicatorDto { ProductCode = productCode };
            }

            // Get direct associations
            var directAssociations = await Context.Set<JournalEntryProduct>()
                .Where(jep => productCodeList.Contains(jep.ProductCode))
                .Join(Context.Set<JournalEntry>().Where(je => !je.IsDeleted),
                    jep => jep.JournalEntryId,
                    je => je.Id,
                    (jep, je) => new { jep.ProductCode, je.EntryDate, je.CreatedAt })
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

            // Get family associations
            var familyAssociations = await Context.Set<JournalEntryProductFamily>()
                .Join(Context.Set<JournalEntry>().Where(je => !je.IsDeleted),
                    jepf => jepf.JournalEntryId,
                    je => je.Id,
                    (jepf, je) => new { jepf.ProductCodePrefix, je.EntryDate, je.CreatedAt })
                .ToListAsync(cancellationToken);

            foreach (var productCode in productCodeList)
            {
                var matchingFamilies = familyAssociations
                    .Where(fa => productCode.StartsWith(fa.ProductCodePrefix))
                    .ToList();

                if (matchingFamilies.Any())
                {
                    result[productCode].FamilyEntries = matchingFamilies.Count;

                    var familyLastDate = matchingFamilies.Max(x => x.EntryDate);
                    if (!result[productCode].LastEntryDate.HasValue ||
                        familyLastDate > result[productCode].LastEntryDate)
                    {
                        result[productCode].LastEntryDate = familyLastDate;
                    }
                }
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