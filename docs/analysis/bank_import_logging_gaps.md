# Analýza logování procesu importu bankovních výpisů

## Executive Summary

Při analýze procesu importu bankovních výpisů bylo identifikováno **7 kritických mezer v logování**, které znemožňují efektivní diagnostiku problémů v produkčním prostředí. Zejména **chybí logování HTTP komunikace**, **request/response payload**, a **diagnostika serializace**.

**Production Error Context:**
```
No MediaTypeFormatter is available to read an object of type 'List`1'
from content with media type 'application/x-www-form-urlencoded'.
```

Tento error naznačuje problém se serializací requestu, ale **současné logování neumožňuje diagnostikovat**:
- Jaký přesně request přišel na server (headers, content-type, body)
- Jaký endpoint byl volán
- Kde přesně došlo k chybě v request pipeline

---

## Flow importu bankovních výpisů

```
┌─────────────────────────────────────────────────────────────────────┐
│ 1. Frontend (ImportTab.tsx)                                        │
│    - User clicks "Import" → handleImportSubmit()                   │
│    - Volá: useBankStatementImport.mutateAsync()                    │
│    ❌ MISSING: Request payload logging                             │
│    ❌ MISSING: HTTP request details (URL, headers, method)         │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│ 2. API Client (useBankStatements.ts)                               │
│    - Sestaví request: { accountName, statementDate }               │
│    - Pošle: POST /api/bank-statements/import                       │
│    - Content-Type: application/json                                │
│    ❌ MISSING: Request serialization logging                       │
│    ❌ MISSING: Response status/error logging                       │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│ 3. ASP.NET Core Pipeline                                           │
│    - Model binding: [FromBody] BankImportRequestDto                │
│    ❌ MISSING: Model binding diagnostics                           │
│    ❌ MISSING: Content-Type validation logging                     │
│    ❌ MISSING: Deserialization error details                       │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│ 4. Controller (BankStatementsController.ImportStatements)          │
│    ✅ HAS: Log import start (AccountName, StatementDate)           │
│    ❌ MISSING: Request headers logging                             │
│    ❌ MISSING: Request body raw content                            │
│    ❌ MISSING: Request validation details                          │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│ 5. Handler (ImportBankStatementHandler)                            │
│    ✅ HAS: Log start of import                                     │
│    ✅ HAS: Log processing statement                                │
│    ❌ MISSING: Account configuration details                       │
│    ❌ MISSING: Number of statements found                          │
│    ❌ MISSING: Performance timing                                  │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│ 6. Comgate Client (ComgateBankClient)                              │
│    ❌ MISSING: HTTP request URL                                    │
│    ❌ MISSING: HTTP request method/headers                         │
│    ❌ MISSING: HTTP response status                                │
│    ❌ MISSING: HTTP response headers                               │
│    ❌ MISSING: Response content (success/error)                    │
│    ❌ MISSING: Parsing errors                                      │
│    ❌ MISSING: Filtering logic (statements found vs returned)      │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│ 7. Flexi Import Service (FlexiBankStatementImportService)          │
│    ✅ HAS: Import start/success/failure                            │
│    ❌ MISSING: Statement data size (lines, bytes)                  │
│    ❌ MISSING: Flexi API response details                          │
│    ❌ MISSING: Performance timing                                  │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│ 8. Repository (BankStatementImportRepository)                      │
│    ❌ MISSING: Database operation logging                          │
│    ❌ MISSING: Saved entity ID                                     │
│    ❌ MISSING: Database errors                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Kritické mezery v logování

### 🔴 CRITICAL: HTTP Request Pipeline (Priority 1)

**Problém:** Nelze diagnostikovat chyby typu "No MediaTypeFormatter available" nebo "application/x-www-form-urlencoded vs application/json"

**Chybějící informace:**
1. **Incoming request details**
   - Raw HTTP headers (especially `Content-Type`, `Accept`)
   - Request body (first 1000 chars for diagnostics)
   - Request path and query string
   - HTTP method

2. **Model binding diagnostics**
   - Model binding source (FromBody, FromQuery, FromForm)
   - Model binding result (success/failure)
   - Deserialization errors with details

3. **ASP.NET Core middleware diagnostics**
   - Which middleware handled the request
   - Any transformations applied to request

**Dopad:** **Nemožné diagnostikovat production error** bez přístupu k Application Insights nebo server logs

**Kde implementovat:**
- `BankStatementsController.cs:30-57` - přidat middleware/action filter
- `Program.cs` - global exception handler s request logging

---

### 🟠 HIGH: Comgate API Communication (Priority 2)

**Problém:** Při selhání Comgate API není jasné, co se pokazilo

**File:** `ComgateBankClient.cs` - **ŽÁDNÉ logování HTTP komunikace**

**Chybějící informace:**
1. **Request logging:**
   ```
   Line 26: var response = await _httpClient.GetStreamAsync(url);
   Line 44: var response = await _httpClient.SendAsync(request);
   ```
   - Request URL (anonymizovaná - bez secret)
   - HTTP method (GET/POST)
   - Request timestamp

2. **Response logging:**
   - HTTP status code
   - Response headers
   - Response content (success/error)
   - Response timestamp
   - Duration

3. **Error handling:**
   - HTTP errors (4xx, 5xx)
   - Network errors (timeouts, DNS)
   - Parsing errors (AboFile.Parse)

**Příklad chybějící diagnostiky:**
```csharp
// Current (line 26):
var response = await _httpClient.GetStreamAsync(url);

// Missing:
// - Co když Comgate vrátí 500?
// - Co když vrátí jiný formát než ABO?
// - Co když timeout?
// - Co když invalid transferId?
```

**Dopad:** Při selhání Comgate API není jasné, jestli problém je:
- Na straně Comgate (jejich error)
- V síti (timeout, DNS)
- V parsování (invalid ABO format)

---

### 🟠 HIGH: Account Configuration Resolution (Priority 2)

**File:** `ImportBankStatementHandler.cs:42-46`

**Problém:** Když account není nalezen, není logováno jaké accounts jsou dostupné

**Chybějící informace:**
```csharp
// Line 42-46:
var accountSetting = _bankSettings.Accounts?.SingleOrDefault(a => a.Name == request.AccountName);
if (accountSetting == null)
{
    throw new ArgumentException($"Account name {request.AccountName} not found...");
}
```

**Mělo by logovat:**
- Requested account name: `request.AccountName`
- Available accounts: `string.Join(", ", _bankSettings.Accounts.Select(a => a.Name))`
- Account configuration: `accountSetting` (when found)

---

### 🟡 MEDIUM: Statement Processing Details (Priority 3)

**File:** `ImportBankStatementHandler.cs:52-93`

**Chybějící informace:**
1. **GetStatementsAsync result:**
   - Kolik statements bylo nalezeno celkem
   - Kolik jich matchuje account number
   - Statement IDs found

2. **GetStatementAsync result:**
   - Velikost ABO dat (bytes, lines)
   - ABO header details
   - Parsing success/failure

3. **ImportStatementAsync result:**
   - FlexiBeeId použité
   - Velikost odeslaných dat
   - Flexi response details (ne jen success/error)

---

### 🟡 MEDIUM: Performance Timing (Priority 3)

**Chybí v celém flow:**

Není měřeno, kolik trvají jednotlivé operace:
- Comgate API call 1 (GetStatementsAsync)
- Comgate API call 2 (GetStatementAsync) - per statement
- Flexi import (ImportStatementAsync) - per statement
- Database save - per statement
- **Celkový čas importu**

**Proč je to důležité:**
- Diagnostika timeout issues
- Identifikace slow endpoints
- Performance optimization

**Kde implementovat:**
- Handler: celkový čas `Handle()` metody
- Comgate client: čas HTTP requestů
- Flexi service: čas import operace

---

### 🟢 LOW: Database Operations (Priority 4)

**File:** `BankStatementImportRepository.cs`

**Chybějící informace:**
- Database operation start/completion
- Saved entity ID
- Database errors (constraint violations, etc.)

---

## Návrh implementace

### 1. HTTP Request Logging Middleware

**Kde:** `backend/src/Anela.Heblo.API/Middleware/RequestLoggingMiddleware.cs`

**Co logovat:**
```csharp
// Before request processing:
_logger.LogInformation(
    "HTTP {Method} {Path} - ContentType: {ContentType}, ContentLength: {ContentLength}",
    context.Request.Method,
    context.Request.Path,
    context.Request.ContentType,
    context.Request.ContentLength
);

// On model binding error:
_logger.LogError(
    "Model binding failed for {Path}. ContentType: {ContentType}. Error: {Error}. Body: {Body}",
    context.Request.Path,
    context.Request.ContentType,
    modelBindingError,
    bodySnapshot
);
```

**Registration:** `Program.cs` - `app.UseMiddleware<RequestLoggingMiddleware>()`

---

### 2. Comgate Client Logging

**Kde:** `ComgateBankClient.cs`

**Změny:**
```csharp
public class ComgateBankClient : IBankClient
{
    private readonly ILogger<ComgateBankClient> _logger;  // ADD

    // GetStatementAsync - ADD logging:
    public async Task<BankStatementData> GetStatementAsync(string transferId)
    {
        var url = string.Format(...); // ANONYMIZE SECRET
        var anonymizedUrl = url.Replace(_settings.Secret, "***");

        _logger.LogInformation("Comgate API: GET statement {TransferId} from {Url}",
            transferId, anonymizedUrl);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _httpClient.GetStreamAsync(url);
            // ... parsing ...

            sw.Stop();
            _logger.LogInformation(
                "Comgate API: GET statement {TransferId} SUCCESS - {LineCount} lines, {Duration}ms",
                transferId, abo.Lines.Count, sw.ElapsedMilliseconds
            );

            return new BankStatementData() { ... };
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Comgate API: GET statement {TransferId} FAILED - HTTP error after {Duration}ms",
                transferId, sw.ElapsedMilliseconds
            );
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Comgate API: GET statement {TransferId} FAILED - Parsing error after {Duration}ms",
                transferId, sw.ElapsedMilliseconds
            );
            throw;
        }
    }

    // GetStatementsAsync - similar logging
}
```

---

### 3. Handler Enhanced Logging

**Kde:** `ImportBankStatementHandler.cs`

**Změny:**
```csharp
public async Task<ImportBankStatementResponse> Handle(...)
{
    var sw = Stopwatch.StartNew();

    _logger.LogInformation(
        "Bank import START - Account: {AccountName}, Date: {StatementDate}",
        request.AccountName, request.StatementDate
    );

    // After account resolution:
    _logger.LogInformation(
        "Account config resolved - FlexiBeeId: {FlexiBeeId}, AccountNumber: {AccountNumber}",
        accountSetting.FlexiBeeId, accountSetting.AccountNumber
    );

    // After GetStatementsAsync:
    _logger.LogInformation(
        "Comgate returned {Count} statements for processing",
        statements.Count
    );

    // ... processing ...

    sw.Stop();
    _logger.LogInformation(
        "Bank import COMPLETED - Account: {AccountName}, Processed: {Count}/{Total}, Duration: {Duration}ms",
        request.AccountName, imports.Count, statements.Count, sw.ElapsedMilliseconds
    );

    return new ImportBankStatementResponse { Statements = imports };
}
```

---

### 4. Flexi Service Enhanced Logging

**Kde:** `FlexiBankStatementImportService.cs`

**Změny:**
```csharp
public async Task<Result<bool>> ImportStatementAsync(int accountId, string statementData)
{
    var lineCount = statementData.Split('\n').Length;
    var dataSize = statementData.Length;

    _logger.LogInformation(
        "Flexi import START - AccountId: {AccountId}, Lines: {LineCount}, Size: {SizeKB}KB",
        accountId, lineCount, dataSize / 1024
    );

    var sw = Stopwatch.StartNew();
    var flexiResult = await _flexiBankAccountClient.ImportStatementAsync(accountId, statementData);
    sw.Stop();

    if (flexiResult.IsSuccess)
    {
        _logger.LogInformation(
            "Flexi import SUCCESS - AccountId: {AccountId}, Duration: {Duration}ms",
            accountId, sw.ElapsedMilliseconds
        );
    }
    else
    {
        _logger.LogWarning(
            "Flexi import FAILED - AccountId: {AccountId}, Error: {Error}, Duration: {Duration}ms",
            accountId, flexiResult.ErrorMessage, sw.ElapsedMilliseconds
        );
    }

    // ... return ...
}
```

---

## Structured Logging Best Practices

### ✅ DO:
- Používat strukturované property names (PascalCase): `{AccountName}`, `{Duration}`
- Logovat timing pro external API calls
- Logovat request/response sizes
- Anonymizovat secrets v URL (replace secret s `***`)
- Používat Log Levels správně:
  - `LogInformation` - normální flow
  - `LogWarning` - expected errors (account not found, import failed)
  - `LogError` - unexpected exceptions

### ❌ DON'T:
- Nelogovat full secrets nebo credentials
- Nelogovat celé large payloads (limit 1000 chars)
- Nelogovat PII data (osobní údaje)
- Nepoužívat string interpolation místo structured logging:
  ```csharp
  // ❌ BAD:
  _logger.LogInformation($"Import for {accountName}");

  // ✅ GOOD:
  _logger.LogInformation("Import for {AccountName}", accountName);
  ```

---

## Implementation Priority

### Phase 1: CRITICAL (Production Bug Fix)
1. ✅ **HTTP Request Logging Middleware** - diagnostika Content-Type problémů
2. ✅ **Controller request/response logging** - vidět co přichází/odchází

### Phase 2: HIGH (Observability)
3. ✅ **Comgate Client logging** - diagnostika external API failures
4. ✅ **Account configuration logging** - diagnostika config issues

### Phase 3: MEDIUM (Performance)
5. ✅ **Performance timing** - identifikace bottlenecks
6. ✅ **Statement processing details** - debugging import logic

### Phase 4: LOW (Nice to have)
7. ✅ **Database operation logging** - audit trail

---

## Testing Logging

Po implementaci otestovat:

1. **Happy path:**
   - Spustit import s validními daty
   - Zkontrolovat, že všechny log messages jsou přítomny
   - Ověřit structured properties

2. **Error scenarios:**
   - Invalid account name → log available accounts
   - Comgate API error → log HTTP status, error response
   - Flexi import error → log error details
   - Model binding error → log Content-Type, request body

3. **Production simulation:**
   - Replikovat production error (x-www-form-urlencoded)
   - Ověřit, že logs obsahují diagnostic info pro fix

---

## Conclusion

**Current state:** ❌ **Nedostatečné logování** pro production diagnostiku

**Required improvements:** **7 kritických oblastí** identifikováno

**Impact:**
- ✅ Možnost diagnostikovat production errors bez access k serveru
- ✅ Rychlejší resolution času
- ✅ Lepší observability
- ✅ Performance insights

**Next steps:**
1. Implementovat Phase 1 (CRITICAL) - request pipeline logging
2. Deploy a otestovat s production-like scenario
3. Implementovat Phase 2-4 postupně
