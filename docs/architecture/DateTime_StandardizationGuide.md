# DateTime Standardization Guide

This document defines the unified approach to DateTime handling across the Anela Heblo application to prevent PostgreSQL timezone conflicts and ensure consistent data storage.

## The Problem

PostgreSQL's `timestamp with time zone` type requires `DateTime` values with `Kind = Utc`, but C# application code often creates `DateTime` values with `Kind = Unspecified` (especially when using `new DateTime()` or parsing from strings), resulting in errors like:

```
Microsoft.EntityFrameworkCore.DbUpdateException: Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone', only UTC is supported.
```

## Unified Solution

### 1. Database Schema Standard

**ALL DateTime columns use PostgreSQL `timestamp` (without time zone):**
```sql
-- ✅ CORRECT - All DateTime columns
"CreatedAt" timestamp
"ModifiedAt" timestamp  
"EntryDate" timestamp
"DeletedAt" timestamp
```

**Never use `timestamp with time zone`:**
```sql
-- ❌ WRONG - Causes PostgreSQL Kind conflicts
"CreatedAt" timestamp with time zone
```

### 2. Entity Configuration Standard

**Use the standardized extension method in all entity configurations:**

```csharp
using Anela.Heblo.Persistence.Extensions;

builder.Property(x => x.CreatedAt)
    .IsRequired()
    .AsUtcTimestamp();

builder.Property(x => x.DeletedAt)
    .IsRequired(false)
    .AsUtcTimestamp();
```

The `AsUtcTimestamp()` extension method ensures consistent `timestamp` column type configuration.

### 3. Application Code Standard

**ALWAYS use `DateTime.UtcNow` for storing timestamps:**

```csharp
// ✅ CORRECT - Always use UTC
entity.CreatedAt = DateTime.UtcNow;
entity.ModifiedAt = DateTime.UtcNow;
entity.DeletedAt = DateTime.UtcNow;

// ❌ WRONG - Never use local time
entity.CreatedAt = DateTime.Now;  // Kind = Local, causes issues
entity.CreatedAt = new DateTime(2024, 1, 1); // Kind = Unspecified, causes issues
```

### 4. Existing Entities - Migration Status

#### ✅ **Fixed Entities** (Using `timestamp` standard):
- **PurchaseOrder** - Fixed via migration `20250905090242_FixPurchaseOrderDateTimeColumns`
- **TransportBox** - Fixed via migration `20250903182738_FixTransportBoxDateTimeColumnTypes`  
- **Journal** - Fixed via migration `20250911084753_FixJournalDateTimeColumns`

#### 🔍 **Entities to Review**:
Future entity configurations should follow this standard from creation.

### 5. Development Guidelines

#### For New Entities:
1. **Configure DateTime properties** using `AsUtcTimestamp()`
2. **Use DateTime.UtcNow** in business logic
3. **Test with actual PostgreSQL** database to verify configuration

#### For Existing Entities:
1. **Check entity configuration** - ensure `AsUtcTimestamp()` is used
2. **Review business logic** - verify `DateTime.UtcNow` usage
3. **Create migration** if column type needs updating from `timestamp with time zone`

#### Code Review Checklist:
- [ ] DateTime properties use `AsUtcTimestamp()` in entity configuration
- [ ] Business logic uses `DateTime.UtcNow` (never `DateTime.Now`)
- [ ] No hardcoded `DateTime` constructors in entities
- [ ] Migration correctly changes `timestamp with time zone` to `timestamp`

### 6. Testing Strategy

**Integration tests should verify:**
```csharp
[Test]
public async Task DateTimeStorage_ShouldHandleUtcValues()
{
    // Arrange
    var entity = new JournalEntry 
    { 
        CreatedAt = DateTime.UtcNow, // ✅ UTC
        ModifiedAt = DateTime.UtcNow, // ✅ UTC
        EntryDate = DateTime.UtcNow   // ✅ UTC
    };
    
    // Act & Assert - Should not throw PostgreSQL Kind exception
    await _context.JournalEntries.AddAsync(entity);
    await _context.SaveChangesAsync();
}
```

## Benefits of This Standard

1. **Eliminates PostgreSQL timezone conflicts** - No more `Kind=Unspecified` errors
2. **Consistent data storage** - All timestamps stored as UTC across database
3. **Simplified development** - One clear rule: use UTC everywhere
4. **Future-proof** - New entities automatically follow the standard
5. **Easy maintenance** - Clear migration path for existing entities

## Extension Method Reference

### DateTimeConfigurationExtensions.AsUtcTimestamp()

**Location:** `/backend/src/Anela.Heblo.Persistence/Extensions/DateTimeConfigurationExtensions.cs`

**Usage:**
```csharp
// For required DateTime properties
builder.Property(x => x.CreatedAt)
    .IsRequired()
    .AsUtcTimestamp();

// For nullable DateTime properties  
builder.Property(x => x.DeletedAt)
    .IsRequired(false)
    .AsUtcTimestamp();
```

**What it does:**
- Configures the database column as PostgreSQL `timestamp` (without time zone)
- Ensures consistent configuration across all entities
- Prevents PostgreSQL timezone conflicts

## Migration History

| Date | Migration | Purpose |
|------|-----------|---------|
| 2024-09-03 | `FixTransportBoxDateTimeColumnTypes` | Fixed TransportBox DateTime columns |
| 2024-09-05 | `FixPurchaseOrderDateTimeColumns` | Fixed PurchaseOrder DateTime columns |
| 2024-09-11 | `FixJournalDateTimeColumns` | Fixed Journal DateTime columns |

This standardization ensures reliable DateTime handling across the entire Anela Heblo application.