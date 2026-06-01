## Module
Photobank

## Finding
`GetPhotosHandler` has a direct dependency on the Npgsql infrastructure library:

```csharp
// backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetPhotos/GetPhotosHandler.cs
using Npgsql;   // line 9

// ...
catch (PostgresException ex) when (request.UseRegex && ex.SqlState == "2201B")   // line 43
{
    return new GetPhotosResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.PhotobankInvalidRegexPattern,
        // ...
    };
}
```

The handler catches `PostgresException` with SQL state `2201B` (invalid regular expression) to return a structured error response when the user supplies a bad regex.

## Why it matters
- Clean Architecture requires the Application layer to be infrastructure-agnostic. Importing `Npgsql` in the Application layer means the Application project now has a hard compile-time dependency on a specific database driver.
- The intent — converting an infrastructure exception into a domain-level error response — is correct, but the *location* is wrong.
- If the database is ever swapped or the repository is tested with an in-memory provider, this catch clause silently stops working without any compile error.

## Suggested fix
Define a domain exception in the repository contract (Domain layer) and throw it from the repository when the invalid-regex error is detected:

```csharp
// Domain/Features/Photobank/ — new file
public class InvalidPhotoSearchPatternException : Exception
{
    public string Pattern { get; }
    public InvalidPhotoSearchPatternException(string pattern)
        : base($"Invalid search pattern: {pattern}") => Pattern = pattern;
}
```

In `PhotobankRepository.BuildFilterQuery` (or `GetPhotosAsync`), catch `PostgresException` with `SqlState == "2201B"` and rethrow as `InvalidPhotoSearchPatternException`.

`GetPhotosHandler` then catches `InvalidPhotoSearchPatternException` — no `Npgsql` import needed:
```csharp
catch (InvalidPhotoSearchPatternException ex)
{
    return new GetPhotosResponse { Success = false, ErrorCode = ErrorCodes.PhotobankInvalidRegexPattern, ... };
}
```

Remove `using Npgsql;` from the handler entirely.

---
_Filed by daily arch-review routine on 2026-05-21._