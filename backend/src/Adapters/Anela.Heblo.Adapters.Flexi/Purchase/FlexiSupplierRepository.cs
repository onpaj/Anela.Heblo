using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Entities;
using Anela.Heblo.Xcc.Audit;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Contacts;
using Rem.FlexiBeeSDK.Model.Contacts;

namespace Anela.Heblo.Adapters.Flexi.Purchase;

public class FlexiSupplierRepository : ISupplierRepository
{
    private readonly IContactListClient _contactListClient;
    private readonly IDataLoadAuditService _auditService;
    private readonly ILogger<FlexiSupplierRepository> _logger;
    private readonly IMemoryCache _cache;

    public FlexiSupplierRepository(
        IContactListClient contactListClient,
        IDataLoadAuditService auditService,
        ILogger<FlexiSupplierRepository> logger,
        IMemoryCache cache)
    {
        _contactListClient = contactListClient;
        _auditService = auditService;
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

        var startTime = DateTime.UtcNow;

        try
        {
            var contactsResult = await _contactListClient.GetAsync(ContactType.Supplier, 0, 0);

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

            var duration = DateTime.UtcNow - startTime;
            await _auditService.LogDataLoadAsync(
                dataType: "All Suppliers Load",
                source: "Flexi ERP",
                recordCount: suppliers.Count,
                success: suppliers.Count > 0,
                duration: duration);

            return suppliers;
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            await _auditService.LogDataLoadAsync(
                dataType: "All Suppliers Load",
                source: "Flexi ERP",
                recordCount: 0,
                success: false,
                errorMessage: ex.Message,
                duration: duration);

            _logger.LogError(ex, "Error loading all suppliers");
            throw;
        }
    }

    private static Supplier MapToSupplier(ContactFlexiDto contact)
    {
        return new Supplier
        {
            Id = contact.Id ?? 0L,
            Name = contact.Name ?? string.Empty,
            Code = contact.Code ?? string.Empty,
            Note = contact.Note,
            Email = contact.Email,
            Phone = contact.Phone,
            Url = contact.Website
        };
    }
}