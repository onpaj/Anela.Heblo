# Time Handling Best Practices

This application follows UTC-first time handling approach.

## ✅ Rules for Internal Time Usage

### Business Logic (ALWAYS UTC)
- All business logic calculations use UTC time
- All database storage uses UTC time  
- All API internal processing uses UTC time

```csharp
// ✅ CORRECT - Business logic uses UTC
result.Date = _timeProvider.GetUtcNow().DateTime;

// ❌ WRONG - Don't use local time in business logic  
result.Date = _timeProvider.GetLocalNow().DateTime;
```

### Display & User Interface (Local Time)
- Only convert to local time when displaying to users
- Use extension methods for consistent conversion

```csharp
// ✅ CORRECT - For user display
var displayTime = _timeProvider.FormatForDisplay(utcDateTime);
var localTime = _timeProvider.ToLocalTime(utcDateTime);

// ✅ CORRECT - For filenames (uses local time for user convenience)
var filename = $"{_timeProvider.GetFilenameTimestamp()}_report.pdf";
```

## Extension Methods Available

```csharp
// Convert UTC to local for display
_timeProvider.ToLocalTime(utcDateTime)

// Format UTC time for display with default format
_timeProvider.FormatForDisplay(utcDateTime)

// Format UTC time with custom format
_timeProvider.FormatForDisplay(utcDateTime, "dd.MM.yyyy HH:mm")

// Get current local time for display
_timeProvider.GetLocalTime()

// Get filename-friendly timestamp (local time)
_timeProvider.GetFilenameTimestamp()
```

## Database Entity Guidelines

All DateTime properties in entities should store UTC time:

```csharp
public class MyEntity 
{
    // ✅ CORRECT - Stored as UTC, converted for display as needed
    public DateTime CreatedAt { get; set; }
    
    // When setting values, always use UTC:
    // entity.CreatedAt = _timeProvider.GetUtcNow().DateTime;
}
```

## API Response Guidelines

- Internal APIs: Return UTC times
- External APIs: Consider returning ISO 8601 format with timezone info
- Frontend: Convert to local time in UI layer

## Testing

- Tests can use any time as appropriate for the test scenario
- Mock TimeProvider for consistent test results

---

**Remember**: Store UTC, display local!