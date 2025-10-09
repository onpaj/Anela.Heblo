using Anela.Heblo.Application.Features.Purchase;
using Anela.Heblo.Domain.Features.Purchase;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Contacts;
using Rem.FlexiBeeSDK.Model.Contacts;

namespace Anela.Heblo.Adapters.Flexi.Purchase;

public class FlexiSupplierRepository : ISupplierRepository
{
    private readonly IContactListClient _contactListClient;
    private readonly ILogger<FlexiSupplierRepository> _logger;
    private readonly IMemoryCache _cache;

    public FlexiSupplierRepository(
        IContactListClient contactListClient,
        ILogger<FlexiSupplierRepository> logger,
        IMemoryCache cache)
    {
        _contactListClient = contactListClient;
        _logger = logger;
        _cache = cache;
    }

    public async Task<List<Supplier>> SearchSuppliersAsync(string searchTerm, int limit = 10, CancellationToken cancellationToken = default)
    {
        var allSuppliers = await GetAllSuppliersFromCacheAsync(cancellationToken);

        var filteredSuppliers = allSuppliers.AsQueryable();

        if (!string.IsNullOrEmpty(searchTerm))
        {
            filteredSuppliers = filteredSuppliers
                .Where(s => s.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                           s.Code.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        return filteredSuppliers
            .Take(limit)
            .ToList();
    }

    public async Task<Supplier?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var allSuppliers = await GetAllSuppliersFromCacheAsync(cancellationToken);
        return allSuppliers.FirstOrDefault(s => s.Id == id);
    }

    public async Task<Supplier?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var allSuppliers = await GetAllSuppliersFromCacheAsync(cancellationToken);
        return allSuppliers.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<Supplier>> GetAllSuppliersFromCacheAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = "all_suppliers";

        if (_cache.TryGetValue(cacheKey, out List<Supplier>? cachedSuppliers))
        {
            return cachedSuppliers ?? new List<Supplier>();
        }

        var contactsResult =
            await _contactListClient.GetAsync([ContactType.Supplier, ContactType.SupplierAndCustomer], 0, 0);

        var suppliers = contactsResult
            .Select(MapToSupplier)
            .ToList();

        if (suppliers.Any())
        {
            // Cache for 2 hours with sliding expiration
            var cacheEntryOptions = new MemoryCacheEntryOptions()
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2),
                SlidingExpiration = TimeSpan.FromHours(1)
            };

            _cache.Set(cacheKey, suppliers, cacheEntryOptions);
        }

        return suppliers;

    }

    private static Supplier MapToSupplier(ContactFlexiDto contact)
    {
        return new Supplier
        {
            Id = contact.Id ?? 0L,
            Name = contact.Name ?? string.Empty,
            Code = contact.Code ?? string.Empty,
            Note = contact.Note,
            Description = contact.Description,
            Email = contact.Email,
            Phone = contact.Phone,
            Url = contact.Website
        };
    }
}