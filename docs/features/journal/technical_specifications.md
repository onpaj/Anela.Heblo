# Journal Feature - Technical Specifications

## Architecture Overview

The Journal feature follows the Vertical Slice Architecture pattern established in the application, with all journal-related functionality contained within a single module. The implementation spans across Domain, Application, and API layers with proper separation of concerns.

## Module Structure

```
backend/src/Anela.Heblo.Application/Features/Journal/
├── contracts/                 # Public interfaces and DTOs
│   ├── IJournalService.cs
│   ├── IJournalRepository.cs
│   ├── CreateJournalEntryRequest.cs
│   ├── CreateJournalEntryResponse.cs
│   ├── UpdateJournalEntryRequest.cs
│   ├── GetJournalEntriesRequest.cs
│   ├── GetJournalEntriesResponse.cs
│   ├── SearchJournalEntriesRequest.cs
│   ├── SearchJournalEntriesResponse.cs
│   ├── GetJournalEntriesByProductRequest.cs
│   ├── GetJournalIndicatorsRequest.cs
│   ├── JournalEntryDto.cs
│   ├── JournalEntryProductAssociationDto.cs
│   └── JournalIndicatorDto.cs
├── Application/              # MediatR handlers and services
│   ├── CreateJournalEntryHandler.cs
│   ├── UpdateJournalEntryHandler.cs
│   ├── DeleteJournalEntryHandler.cs
│   ├── GetJournalEntryHandler.cs
│   ├── GetJournalEntriesHandler.cs
│   ├── SearchJournalEntriesHandler.cs
│   ├── GetJournalEntriesByProductHandler.cs
│   ├── GetJournalIndicatorsHandler.cs
│   └── JournalService.cs
├── domain/                   # Domain entities and business logic
│   ├── JournalEntry.cs
│   ├── JournalEntryProduct.cs
│   ├── JournalEntryProductFamily.cs
│   ├── JournalEntryTag.cs
│   ├── JournalEntryTagAssignment.cs
│   └── JournalDomainService.cs
└── JournalModule.cs         # DI registration
```

## Domain Model

### Core Entities

#### JournalEntry
```csharp
namespace Anela.Heblo.Domain.Features.Journal
{
    public class JournalEntry : BaseEntity
    {
        public int Id { get; set; }
        
        [MaxLength(200)]
        public string? Title { get; set; }
        
        [Required]
        [MaxLength(10000)]
        public string Content { get; set; } = null!;
        
        [Required]
        public DateTime EntryDate { get; set; }
        
        [Required]
        public DateTime CreatedAt { get; set; }
        
        [Required]
        public DateTime ModifiedAt { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string CreatedByUserId { get; set; } = null!;
        
        [MaxLength(100)]
        public string? ModifiedByUserId { get; set; }
        
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        [MaxLength(100)]
        public string? DeletedByUserId { get; set; }
        
        // Navigation properties
        public virtual ICollection<JournalEntryProduct> ProductAssociations { get; set; } = new List<JournalEntryProduct>();
        public virtual ICollection<JournalEntryProductFamily> ProductFamilyAssociations { get; set; } = new List<JournalEntryProductFamily>();
        public virtual ICollection<JournalEntryTagAssignment> TagAssignments { get; set; } = new List<JournalEntryTagAssignment>();
        
        // Domain methods
        public void AssociateWithProduct(string productCode)
        {
            if (string.IsNullOrWhiteSpace(productCode))
                throw new ArgumentException("Product code cannot be empty", nameof(productCode));
                
            if (ProductAssociations.Any(pa => pa.ProductCode == productCode))
                return; // Already associated
                
            ProductAssociations.Add(new JournalEntryProduct
            {
                JournalEntryId = Id,
                ProductCode = productCode.Trim().ToUpperInvariant()
            });
        }
        
        public void AssociateWithProductFamily(string productCodePrefix)
        {
            if (string.IsNullOrWhiteSpace(productCodePrefix))
                throw new ArgumentException("Product code prefix cannot be empty", nameof(productCodePrefix));
                
            if (ProductFamilyAssociations.Any(pfa => pfa.ProductCodePrefix == productCodePrefix))
                return; // Already associated
                
            ProductFamilyAssociations.Add(new JournalEntryProductFamily
            {
                JournalEntryId = Id,
                ProductCodePrefix = productCodePrefix.Trim().ToUpperInvariant()
            });
        }
        
        public void AssignTag(int tagId)
        {
            if (TagAssignments.Any(ta => ta.TagId == tagId))
                return; // Already assigned
                
            TagAssignments.Add(new JournalEntryTagAssignment
            {
                JournalEntryId = Id,
                TagId = tagId
            });
        }
        
        public void SoftDelete(string userId)
        {
            IsDeleted = true;
            DeletedAt = DateTime.UtcNow;
            DeletedByUserId = userId;
            ModifiedAt = DateTime.UtcNow;
            ModifiedByUserId = userId;
        }
        
        public bool IsAssociatedWithProduct(string productCode)
        {
            return ProductAssociations.Any(pa => pa.ProductCode == productCode) ||
                   ProductFamilyAssociations.Any(pfa => productCode.StartsWith(pfa.ProductCodePrefix));
        }
    }
}
```

#### JournalEntryProduct
```csharp
public class JournalEntryProduct
{
    public int JournalEntryId { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string ProductCode { get; set; } = null!;
    
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public virtual JournalEntry JournalEntry { get; set; } = null!;
    
    // Configure composite key in entity configuration
}
```

#### JournalEntryProductFamily
```csharp
public class JournalEntryProductFamily
{
    public int JournalEntryId { get; set; }
    
    [Required]
    [MaxLength(20)]
    public string ProductCodePrefix { get; set; } = null!;
    
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public virtual JournalEntry JournalEntry { get; set; } = null!;
}
```

#### JournalEntryTag
```csharp
public class JournalEntryTag
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;
    
    [MaxLength(7)]
    public string Color { get; set; } = "#6B7280"; // Default gray
    
    public DateTime CreatedAt { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string CreatedByUserId { get; set; } = null!;
    
    // Navigation properties
    public virtual ICollection<JournalEntryTagAssignment> TagAssignments { get; set; } = new List<JournalEntryTagAssignment>();
}
```

#### JournalEntryTagAssignment
```csharp
public class JournalEntryTagAssignment
{
    public int JournalEntryId { get; set; }
    public int TagId { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public virtual JournalEntry JournalEntry { get; set; } = null!;
    public virtual JournalEntryTag Tag { get; set; } = null!;
}
```

## Application Layer

### Repository Interface

```csharp
namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public interface IJournalRepository
    {
        Task<JournalEntry?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<JournalEntry> CreateAsync(JournalEntry entry, CancellationToken cancellationToken = default);
        Task<JournalEntry> UpdateAsync(JournalEntry entry, CancellationToken cancellationToken = default);
        Task DeleteAsync(int id, string userId, CancellationToken cancellationToken = default);
        
        Task<PagedResult<JournalEntry>> GetEntriesAsync(
            GetJournalEntriesRequest request, 
            CancellationToken cancellationToken = default);
            
        Task<PagedResult<JournalEntry>> SearchEntriesAsync(
            SearchJournalEntriesRequest request, 
            CancellationToken cancellationToken = default);
            
        Task<List<JournalEntry>> GetEntriesByProductAsync(
            string productCode, 
            CancellationToken cancellationToken = default);
            
        Task<Dictionary<string, JournalIndicatorDto>> GetJournalIndicatorsAsync(
            IEnumerable<string> productCodes, 
            CancellationToken cancellationToken = default);
            
        Task<List<JournalEntryTag>> GetTagsAsync(CancellationToken cancellationToken = default);
        Task<JournalEntryTag> CreateTagAsync(JournalEntryTag tag, CancellationToken cancellationToken = default);
    }
}
```

### Request/Response DTOs

#### CreateJournalEntryRequest
```csharp
public class CreateJournalEntryRequest : IRequest<CreateJournalEntryResponse>
{
    [MaxLength(200)]
    public string? Title { get; set; }
    
    [Required]
    [MaxLength(10000)]
    public string Content { get; set; } = null!;
    
    [Required]
    public DateTime EntryDate { get; set; }
    
    public List<string>? AssociatedProductCodes { get; set; }
    public List<string>? AssociatedProductFamilies { get; set; }
    public List<int>? TagIds { get; set; }
}

public class CreateJournalEntryResponse
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Message { get; set; } = "Journal entry created successfully";
}
```

#### SearchJournalEntriesRequest
```csharp
public class SearchJournalEntriesRequest : IRequest<SearchJournalEntriesResponse>
{
    [MaxLength(200)]
    public string? SearchText { get; set; }
    
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    
    public List<string>? ProductCodes { get; set; }
    public List<string>? ProductCodePrefixes { get; set; }
    public List<int>? TagIds { get; set; }
    
    [MaxLength(100)]
    public string? CreatedByUserId { get; set; }
    
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    
    public string SortBy { get; set; } = "EntryDate";
    public string SortDirection { get; set; } = "DESC"; // DESC or ASC
}

public class SearchJournalEntriesResponse
{
    public List<JournalEntryDto> Entries { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}
```

#### JournalEntryDto
```csharp
public class JournalEntryDto
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; } = null!;
    public DateTime EntryDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string CreatedByUserId { get; set; } = null!;
    public string? ModifiedByUserId { get; set; }
    
    public List<string> AssociatedProductCodes { get; set; } = new();
    public List<string> AssociatedProductFamilies { get; set; } = new();
    public List<JournalEntryTagDto> Tags { get; set; } = new();
    
    // For search results
    public string? ContentPreview { get; set; }
    public List<string> HighlightedTerms { get; set; } = new();
}

public class JournalEntryTagDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Color { get; set; } = null!;
}
```

#### GetJournalIndicatorsRequest
```csharp
public class GetJournalIndicatorsRequest : IRequest<GetJournalIndicatorsResponse>
{
    [Required]
    public List<string> ProductCodes { get; set; } = new();
}

public class GetJournalIndicatorsResponse
{
    public Dictionary<string, JournalIndicatorDto> Indicators { get; set; } = new();
}

public class JournalIndicatorDto
{
    public string ProductCode { get; set; } = null!;
    public int DirectEntries { get; set; }
    public int FamilyEntries { get; set; }
    public int TotalEntries => DirectEntries + FamilyEntries;
    public DateTime? LastEntryDate { get; set; }
    public bool HasRecentEntries { get; set; } // Within last 30 days
}
```

### MediatR Handlers

#### CreateJournalEntryHandler
```csharp
namespace Anela.Heblo.Application.Features.Journal.Application
{
    public class CreateJournalEntryHandler : IRequestHandler<CreateJournalEntryRequest, CreateJournalEntryResponse>
    {
        private readonly IJournalRepository _journalRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly IHtmlSanitizer _htmlSanitizer;
        private readonly ILogger<CreateJournalEntryHandler> _logger;

        public CreateJournalEntryHandler(
            IJournalRepository journalRepository,
            ICurrentUserService currentUserService,
            IHtmlSanitizer htmlSanitizer,
            ILogger<CreateJournalEntryHandler> logger)
        {
            _journalRepository = journalRepository;
            _currentUserService = currentUserService;
            _htmlSanitizer = htmlSanitizer;
            _logger = logger;
        }

        public async Task<CreateJournalEntryResponse> Handle(
            CreateJournalEntryRequest request, 
            CancellationToken cancellationToken)
        {
            var userId = _currentUserService.UserId;
            var now = DateTime.UtcNow;
            
            // Sanitize HTML content
            var sanitizedContent = _htmlSanitizer.Sanitize(request.Content);
            
            var entry = new JournalEntry
            {
                Title = request.Title?.Trim(),
                Content = sanitizedContent,
                EntryDate = request.EntryDate.Date,
                CreatedAt = now,
                ModifiedAt = now,
                CreatedByUserId = userId
            };

            // Associate products
            if (request.AssociatedProductCodes?.Any() == true)
            {
                foreach (var productCode in request.AssociatedProductCodes.Distinct())
                {
                    entry.AssociateWithProduct(productCode);
                }
            }

            // Associate product families
            if (request.AssociatedProductFamilies?.Any() == true)
            {
                foreach (var prefix in request.AssociatedProductFamilies.Distinct())
                {
                    entry.AssociateWithProductFamily(prefix);
                }
            }

            // Assign tags
            if (request.TagIds?.Any() == true)
            {
                foreach (var tagId in request.TagIds.Distinct())
                {
                    entry.AssignTag(tagId);
                }
            }

            var createdEntry = await _journalRepository.CreateAsync(entry, cancellationToken);

            _logger.LogInformation(
                "Journal entry {EntryId} created by user {UserId}", 
                createdEntry.Id, 
                userId);

            return new CreateJournalEntryResponse
            {
                Id = createdEntry.Id,
                CreatedAt = createdEntry.CreatedAt
            };
        }
    }
}
```

#### SearchJournalEntriesHandler
```csharp
public class SearchJournalEntriesHandler : IRequestHandler<SearchJournalEntriesRequest, SearchJournalEntriesResponse>
{
    private readonly IJournalRepository _journalRepository;
    private readonly IMapper _mapper;

    public SearchJournalEntriesHandler(IJournalRepository journalRepository, IMapper mapper)
    {
        _journalRepository = journalRepository;
        _mapper = mapper;
    }

    public async Task<SearchJournalEntriesResponse> Handle(
        SearchJournalEntriesRequest request, 
        CancellationToken cancellationToken)
    {
        var result = await _journalRepository.SearchEntriesAsync(request, cancellationToken);
        
        var entryDtos = result.Items.Select(entry => _mapper.Map<JournalEntryDto>(entry)).ToList();
        
        // Add content previews for search results
        if (!string.IsNullOrEmpty(request.SearchText))
        {
            foreach (var dto in entryDtos)
            {
                dto.ContentPreview = CreateContentPreview(dto.Content, request.SearchText);
                dto.HighlightedTerms = ExtractHighlightTerms(request.SearchText);
            }
        }

        return new SearchJournalEntriesResponse
        {
            Entries = entryDtos,
            TotalCount = result.TotalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalPages = (int)Math.Ceiling((double)result.TotalCount / request.PageSize),
            HasNextPage = request.PageNumber * request.PageSize < result.TotalCount,
            HasPreviousPage = request.PageNumber > 1
        };
    }

    private static string CreateContentPreview(string content, string searchText, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(searchText))
            return content.Length <= maxLength ? content : content[..maxLength] + "...";

        var index = content.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
        if (index == -1)
            return content.Length <= maxLength ? content : content[..maxLength] + "...";

        var start = Math.Max(0, index - maxLength / 2);
        var length = Math.Min(maxLength, content.Length - start);
        
        var preview = content.Substring(start, length);
        if (start > 0) preview = "..." + preview;
        if (start + length < content.Length) preview += "...";
        
        return preview;
    }

    private static List<string> ExtractHighlightTerms(string searchText)
    {
        return searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(term => term.Length > 2)
                        .ToList();
    }
}
```

## Persistence Layer

### Entity Configuration

#### JournalEntryConfiguration
```csharp
namespace Anela.Heblo.Persistence.Configurations.Features.Journal
{
    public class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
    {
        public void Configure(EntityTypeBuilder<JournalEntry> builder)
        {
            builder.ToTable("JournalEntries");
            
            builder.HasKey(x => x.Id);
            
            builder.Property(x => x.Title)
                .HasMaxLength(200)
                .IsRequired(false);
                
            builder.Property(x => x.Content)
                .HasMaxLength(10000)
                .IsRequired();
                
            builder.Property(x => x.EntryDate)
                .IsRequired();
                
            builder.Property(x => x.CreatedAt)
                .IsRequired();
                
            builder.Property(x => x.ModifiedAt)
                .IsRequired();
                
            builder.Property(x => x.CreatedByUserId)
                .HasMaxLength(100)
                .IsRequired();
                
            builder.Property(x => x.ModifiedByUserId)
                .HasMaxLength(100)
                .IsRequired(false);
                
            builder.Property(x => x.DeletedByUserId)
                .HasMaxLength(100)
                .IsRequired(false);
                
            // Soft delete filter
            builder.HasQueryFilter(x => !x.IsDeleted);
            
            // Indexes for performance
            builder.HasIndex(x => x.EntryDate)
                .HasDatabaseName("IX_JournalEntries_EntryDate");
                
            builder.HasIndex(x => x.CreatedByUserId)
                .HasDatabaseName("IX_JournalEntries_CreatedByUserId");
                
            builder.HasIndex(x => new { x.IsDeleted, x.EntryDate })
                .HasDatabaseName("IX_JournalEntries_IsDeleted_EntryDate");
            
            // Full-text search index (PostgreSQL specific)
            builder.HasIndex(x => new { x.Title, x.Content })
                .HasMethod("gin")
                .HasDatabaseName("IX_JournalEntries_FullText");
                
            // Navigation properties
            builder.HasMany(x => x.ProductAssociations)
                .WithOne(x => x.JournalEntry)
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);
                
            builder.HasMany(x => x.ProductFamilyAssociations)
                .WithOne(x => x.JournalEntry)
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);
                
            builder.HasMany(x => x.TagAssignments)
                .WithOne(x => x.JournalEntry)
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
```

#### JournalEntryProductConfiguration
```csharp
public class JournalEntryProductConfiguration : IEntityTypeConfiguration<JournalEntryProduct>
{
    public void Configure(EntityTypeBuilder<JournalEntryProduct> builder)
    {
        builder.ToTable("JournalEntryProducts");
        
        // Composite primary key
        builder.HasKey(x => new { x.JournalEntryId, x.ProductCode });
        
        builder.Property(x => x.ProductCode)
            .HasMaxLength(50)
            .IsRequired();
            
        builder.Property(x => x.CreatedAt)
            .IsRequired();
            
        // Index for product code lookups
        builder.HasIndex(x => x.ProductCode)
            .HasDatabaseName("IX_JournalEntryProducts_ProductCode");
    }
}
```

### Repository Implementation

```csharp
namespace Anela.Heblo.Persistence.Repository.Features.Journal
{
    public class JournalRepository : IJournalRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<JournalRepository> _logger;

        public JournalRepository(ApplicationDbContext context, ILogger<JournalRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<JournalEntry?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return await _context.JournalEntries
                .Include(x => x.ProductAssociations)
                .Include(x => x.ProductFamilyAssociations)
                .Include(x => x.TagAssignments)
                    .ThenInclude(x => x.Tag)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        }

        public async Task<PagedResult<JournalEntry>> SearchEntriesAsync(
            SearchJournalEntriesRequest request, 
            CancellationToken cancellationToken = default)
        {
            var query = _context.JournalEntries
                .Include(x => x.ProductAssociations)
                .Include(x => x.ProductFamilyAssociations)
                .Include(x => x.TagAssignments)
                    .ThenInclude(x => x.Tag)
                .AsQueryable();

            // Full-text search
            if (!string.IsNullOrEmpty(request.SearchText))
            {
                var searchTerm = request.SearchText.Trim();
                query = query.Where(x => 
                    EF.Functions.ToTsVector("english", x.Title + " " + x.Content)
                        .Matches(EF.Functions.PlainToTsQuery("english", searchTerm)));
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
            return await _context.JournalEntries
                .Include(x => x.ProductAssociations)
                .Include(x => x.ProductFamilyAssociations)
                .Include(x => x.TagAssignments)
                    .ThenInclude(x => x.Tag)
                .Where(x => 
                    x.ProductAssociations.Any(pa => pa.ProductCode == productCode) ||
                    x.ProductFamilyAssociations.Any(pfa => productCode.StartsWith(pfa.ProductCodePrefix)))
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
            var directAssociations = await _context.JournalEntryProducts
                .Where(jep => productCodeList.Contains(jep.ProductCode))
                .Join(_context.JournalEntries,
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
            var familyAssociations = await _context.JournalEntryProductFamilies
                .Join(_context.JournalEntries,
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
```

## API Layer

### Controller

```csharp
namespace Anela.Heblo.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class JournalController : ControllerBase
    {
        private readonly IMediator _mediator;

        public JournalController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Get journal entries with optional filtering and pagination
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(SearchJournalEntriesResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetJournalEntries([FromQuery] SearchJournalEntriesRequest request)
        {
            var response = await _mediator.Send(request);
            return Ok(response);
        }

        /// <summary>
        /// Get specific journal entry by ID
        /// </summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(JournalEntryDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetJournalEntry(int id)
        {
            var request = new GetJournalEntryRequest { Id = id };
            var response = await _mediator.Send(request);
            
            if (response == null)
                return NotFound($"Journal entry with ID {id} was not found.");
                
            return Ok(response);
        }

        /// <summary>
        /// Create new journal entry
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(CreateJournalEntryResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateJournalEntry([FromBody] CreateJournalEntryRequest request)
        {
            var response = await _mediator.Send(request);
            return CreatedAtAction(nameof(GetJournalEntry), new { id = response.Id }, response);
        }

        /// <summary>
        /// Update existing journal entry
        /// </summary>
        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(UpdateJournalEntryResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> UpdateJournalEntry(int id, [FromBody] UpdateJournalEntryRequest request)
        {
            request.Id = id;
            var response = await _mediator.Send(request);
            return Ok(response);
        }

        /// <summary>
        /// Delete journal entry (soft delete)
        /// </summary>
        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> DeleteJournalEntry(int id)
        {
            var request = new DeleteJournalEntryRequest { Id = id };
            await _mediator.Send(request);
            return NoContent();
        }

        /// <summary>
        /// Get journal entries associated with specific product
        /// </summary>
        [HttpGet("by-product/{productCode}")]
        [ProducesResponseType(typeof(GetJournalEntriesByProductResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetJournalEntriesByProduct(string productCode)
        {
            var request = new GetJournalEntriesByProductRequest { ProductCode = productCode };
            var response = await _mediator.Send(request);
            return Ok(response);
        }

        /// <summary>
        /// Get journal indicators for multiple products
        /// </summary>
        [HttpPost("indicators")]
        [ProducesResponseType(typeof(GetJournalIndicatorsResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetJournalIndicators([FromBody] GetJournalIndicatorsRequest request)
        {
            var response = await _mediator.Send(request);
            return Ok(response);
        }

        /// <summary>
        /// Get all available tags
        /// </summary>
        [HttpGet("tags")]
        [ProducesResponseType(typeof(GetJournalTagsResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetJournalTags()
        {
            var request = new GetJournalTagsRequest();
            var response = await _mediator.Send(request);
            return Ok(response);
        }

        /// <summary>
        /// Create new tag
        /// </summary>
        [HttpPost("tags")]
        [ProducesResponseType(typeof(CreateJournalTagResponse), StatusCodes.Status201Created)]
        public async Task<IActionResult> CreateJournalTag([FromBody] CreateJournalTagRequest request)
        {
            var response = await _mediator.Send(request);
            return CreatedAtAction(nameof(GetJournalTags), response);
        }
    }
}
```

## Database Migrations

### Initial Migration Script
```sql
-- Create JournalEntries table
CREATE TABLE "JournalEntries" (
    "Id" SERIAL PRIMARY KEY,
    "Title" VARCHAR(200) NULL,
    "Content" VARCHAR(10000) NOT NULL,
    "EntryDate" DATE NOT NULL,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL,
    "ModifiedAt" TIMESTAMP WITH TIME ZONE NOT NULL,
    "CreatedByUserId" VARCHAR(100) NOT NULL,
    "ModifiedByUserId" VARCHAR(100) NULL,
    "IsDeleted" BOOLEAN NOT NULL DEFAULT FALSE,
    "DeletedAt" TIMESTAMP WITH TIME ZONE NULL,
    "DeletedByUserId" VARCHAR(100) NULL
);

-- Create JournalEntryProducts table
CREATE TABLE "JournalEntryProducts" (
    "JournalEntryId" INTEGER NOT NULL,
    "ProductCode" VARCHAR(50) NOT NULL,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    PRIMARY KEY ("JournalEntryId", "ProductCode"),
    FOREIGN KEY ("JournalEntryId") REFERENCES "JournalEntries"("Id") ON DELETE CASCADE
);

-- Create JournalEntryProductFamilies table
CREATE TABLE "JournalEntryProductFamilies" (
    "JournalEntryId" INTEGER NOT NULL,
    "ProductCodePrefix" VARCHAR(20) NOT NULL,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    PRIMARY KEY ("JournalEntryId", "ProductCodePrefix"),
    FOREIGN KEY ("JournalEntryId") REFERENCES "JournalEntries"("Id") ON DELETE CASCADE
);

-- Create JournalEntryTags table
CREATE TABLE "JournalEntryTags" (
    "Id" SERIAL PRIMARY KEY,
    "Name" VARCHAR(50) NOT NULL UNIQUE,
    "Color" VARCHAR(7) NOT NULL DEFAULT '#6B7280',
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedByUserId" VARCHAR(100) NOT NULL
);

-- Create JournalEntryTagAssignments table
CREATE TABLE "JournalEntryTagAssignments" (
    "JournalEntryId" INTEGER NOT NULL,
    "TagId" INTEGER NOT NULL,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    PRIMARY KEY ("JournalEntryId", "TagId"),
    FOREIGN KEY ("JournalEntryId") REFERENCES "JournalEntries"("Id") ON DELETE CASCADE,
    FOREIGN KEY ("TagId") REFERENCES "JournalEntryTags"("Id") ON DELETE CASCADE
);

-- Create indexes
CREATE INDEX "IX_JournalEntries_EntryDate" ON "JournalEntries" ("EntryDate");
CREATE INDEX "IX_JournalEntries_CreatedByUserId" ON "JournalEntries" ("CreatedByUserId");
CREATE INDEX "IX_JournalEntries_IsDeleted_EntryDate" ON "JournalEntries" ("IsDeleted", "EntryDate");

-- Create full-text search index (PostgreSQL)
CREATE INDEX "IX_JournalEntries_FullText" ON "JournalEntries" 
USING gin(to_tsvector('english', COALESCE("Title", '') || ' ' || "Content"));

CREATE INDEX "IX_JournalEntryProducts_ProductCode" ON "JournalEntryProducts" ("ProductCode");

-- Insert default tags
INSERT INTO "JournalEntryTags" ("Name", "Color", "CreatedByUserId") VALUES
('Quality', '#EF4444', 'system'),
('Production', '#3B82F6', 'system'),
('Supplier', '#10B981', 'system'),
('Packaging', '#F59E0B', 'system'),
('Issue', '#DC2626', 'system'),
('Review', '#6366F1', 'system'),
('Meeting', '#8B5CF6', 'system'),
('Note', '#6B7280', 'system');
```

## Frontend Integration

### TypeScript API Client Types (Generated)
```typescript
// Generated by NSwag
export interface CreateJournalEntryRequest {
    title?: string | null;
    content: string;
    entryDate: Date;
    associatedProductCodes?: string[] | null;
    associatedProductFamilies?: string[] | null;
    tagIds?: number[] | null;
}

export interface JournalEntryDto {
    id: number;
    title?: string | null;
    content: string;
    entryDate: Date;
    createdAt: Date;
    modifiedAt: Date;
    createdByUserId: string;
    modifiedByUserId?: string | null;
    associatedProductCodes: string[];
    associatedProductFamilies: string[];
    tags: JournalEntryTagDto[];
    contentPreview?: string | null;
    highlightedTerms: string[];
}

export interface JournalIndicatorDto {
    productCode: string;
    directEntries: number;
    familyEntries: number;
    totalEntries: number;
    lastEntryDate?: Date | null;
    hasRecentEntries: boolean;
}
```

### React Hooks
```typescript
// frontend/src/api/hooks/useJournal.ts
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

export const useJournalEntries = (searchParams: SearchJournalEntriesRequest) => {
  return useQuery({
    queryKey: ['journal-entries', searchParams],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      return client.journal.getJournalEntries(searchParams);
    }
  });
};

export const useCreateJournalEntry = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (request: CreateJournalEntryRequest) => {
      const client = await getAuthenticatedApiClient();
      return client.journal.createJournalEntry(request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['journal-entries'] });
    }
  });
};

export const useJournalIndicators = (productCodes: string[]) => {
  return useQuery({
    queryKey: ['journal-indicators', productCodes],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      return client.journal.getJournalIndicators({ productCodes });
    },
    enabled: productCodes.length > 0
  });
};
```

## Performance Optimizations

### Database Indexes
- Full-text search index on title and content (PostgreSQL GIN index)
- Compound index on `(IsDeleted, EntryDate)` for efficient filtering
- Index on `ProductCode` for fast product association lookups
- Index on `EntryDate` for chronological sorting

### Query Optimizations
- Use `Include()` statements to eager load related data and avoid N+1 queries
- Implement server-side pagination to handle large datasets
- Use compiled queries for frequently executed searches
- Implement response caching for journal indicators

### Frontend Optimizations
- Implement virtual scrolling for large journal lists
- Use debounced search input to reduce API calls
- Cache journal indicators using React Query
- Lazy load journal content in product detail views

## Security Considerations

### Authentication & Authorization
- All endpoints require authentication
- Users can only edit/delete their own entries
- Admin users have full access to all entries
- Soft delete preserves audit trail

### Input Validation & Sanitization
- HTML content sanitization using HtmlSanitizer library
- Input validation on all request DTOs
- SQL injection protection through parameterized queries
- XSS protection through content sanitization

### Data Protection
- Personal data is limited to user IDs
- Soft delete ensures data recovery capabilities
- Audit trail tracks all modifications
- No sensitive business data in journal entries

## Monitoring & Logging

### Application Logging
- Entry creation/modification events
- Search performance metrics
- Error logging with correlation IDs
- User activity tracking

### Performance Monitoring
- API response times
- Database query performance
- Search operation metrics
- Memory usage for large content

### Health Checks
- Database connectivity
- Search index status
- Tag system health
- Repository operations

This comprehensive technical specification provides the foundation for implementing the Journal feature according to the established architecture patterns and quality standards.