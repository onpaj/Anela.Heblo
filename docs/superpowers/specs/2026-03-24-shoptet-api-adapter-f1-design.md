# Design: ShoptetApi Adapter — F1 ShoptetPay Payout Downloads

**Date:** 2026-03-24
**Scope:** Phase 1 — Automatic downloading of ShoptetPay payout reports (ABO format) into existing bank import pipeline
**Source document:** `docs/features/shoptet-api-integrace-podklady.md`

---

## 1. Goal

Replace manual downloading of ShoptetPay payout reports from the admin UI with automatic API-based retrieval. The downloaded ABO files are imported into FlexiBee via the existing `ImportBankStatementHandler` pipeline — unchanged in business logic.

---

## 2. Relation to Existing Shoptet Adapter

> **Important:** `Anela.Heblo.Adapters.Shoptet` already exists at `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/`. That project is a **Playwright browser-automation console tool** (`OutputType=Exe`) used for invoice scraping and stock operations. It is completely unrelated to this feature.
>
> The new project `Anela.Heblo.Adapters.ShoptetApi` is a **separate class library** that calls the Shoptet REST API directly.

---

## 3. Scope of Changes

### 3.1 Existing code changes

| Component | Change |
|---|---|
| `AboFile`, `AboHeader`, `AboLine` | Move from `Anela.Heblo.Adapters.Comgate` to `Anela.Heblo.Xcc/Abo/` |
| `BankClientProvider` | New enum in Domain: `Comgate`, `ShoptetPay` |
| `IBankClient` | Add `Provider` property; change `GetStatementsAsync` signature to `dateFrom`/`dateTo` |
| `BankAccountConfiguration` | Add `Provider: BankClientProvider`, `Currency: CurrencyCode`; `FlexiBeeId` stays `int` |
| `ComgateBankClient` | Implement updated interface; iterate date range and aggregate results |
| `IBankClientFactory` + `BankClientFactory` | New types in Application layer (see Section 6 for why this is correct) |
| `BankModule` | Gains `IConfiguration` param; registers `IBankClientFactory` and `BankAccountSettings` |
| `ComgateAdapterServiceCollectionExtensions` | Remove `BankAccountSettings` registration (moved to `BankModule`) |
| `ImportBankStatementRequest` | Replace `StatementDate` with `DateFrom` / `DateTo`; update constructor |
| `BankImportRequestDto` | Replace `StatementDate` with `DateFrom` / `DateTo` |
| `ImportBankStatementHandler` | Inject factory; resolve client; pass `DateFrom`/`DateTo` |
| `BankStatementsController` | Update construction of `ImportBankStatementRequest` from `BankImportRequestDto` |
| `ComgateCzkImportJob` | Update `ImportBankStatementRequest` constructor call |
| `ComgateEurImportJob` | Update `ImportBankStatementRequest` constructor call |
| `Program.cs` | Add `AddShoptetApiAdapter`; update `AddBankModule` call to pass `configuration` |
| `appsettings.json` | Add `Provider` to each `BankAccounts.Accounts` entry; add `ShoptetPay` section |
| Solution file | Add new `.csproj` to `.sln` |
| `Anela.Heblo.API.csproj` | Add `ProjectReference` to `Anela.Heblo.Adapters.ShoptetApi` |

### 3.2 New project

`Anela.Heblo.Adapters.ShoptetApi` — class library at `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/`

---

## 4. Domain Changes (`Anela.Heblo.Domain`)

### `BankClientProvider` enum
```csharp
// Domain/Features/Bank/BankClientProvider.cs
public enum BankClientProvider
{
    Comgate,
    ShoptetPay
}
```

### Updated `IBankClient`
```csharp
public interface IBankClient
{
    BankClientProvider Provider { get; }
    Task<BankStatementData> GetStatementAsync(string statementId);
    Task<IList<BankStatementHeader>> GetStatementsAsync(string accountNumber, DateTime dateFrom, DateTime dateTo);
}
```

### Updated `BankAccountConfiguration`
```csharp
public class BankAccountConfiguration
{
    public string Name { get; set; }
    public BankClientProvider Provider { get; set; }
    public string AccountNumber { get; set; }
    public int FlexiBeeId { get; set; }     // int — unchanged
    public CurrencyCode Currency { get; set; }
}
```

> `CurrencyCode` is an existing domain enum. The handler uses `accountSetting.Currency` directly instead of the `AccountName.EndsWith("EUR")` string heuristic.

> **Enum binding:** `Microsoft.Extensions.Configuration` binder resolves enum values by name (case-insensitive) by default. No custom converter needed. Config value `"ShoptetPay"` maps to `BankClientProvider.ShoptetPay` automatically.

---

## 5. Xcc Changes (`Anela.Heblo.Xcc`)

Move `AboFile`, `AboHeader`, `AboLine` from `Anela.Heblo.Adapters.Comgate` to:

```
Anela.Heblo.Xcc/Abo/AboFile.cs
```

Namespace: `Anela.Heblo.Xcc.Abo`

> Note: In the current codebase `AboFile`, `AboHeader`, and `AboLine` are defined after line 157 of `ComgateBankClient.cs` with no namespace declaration (global scope). Add `namespace Anela.Heblo.Xcc.Abo;` when moving them.

No csproj changes needed for Comgate — it gets Xcc transitively via Application.

---

## 6. Application Changes (`Anela.Heblo.Application`)

### Why `BankClientFactory` belongs in Application

`BankClientFactory` depends only on `IBankClient` (a Domain interface). It does **not** reference `ComgateBankClient` or `ShoptetPayBankClient` by name — it receives them via `IEnumerable<IBankClient>` injected by the DI container. There is no dependency from Application to any adapter assembly, so no Clean Architecture violation occurs. This mirrors the existing pattern where `Application/Features/Bank/Infrastructure/Jobs/` already contains infrastructure glue classes.

### `IBankClientFactory`
```csharp
// Application/Features/Bank/Infrastructure/IBankClientFactory.cs
public interface IBankClientFactory
{
    IBankClient GetClient(BankAccountConfiguration accountSettings);
}
```

### `BankClientFactory`
```csharp
// Application/Features/Bank/Infrastructure/BankClientFactory.cs
public class BankClientFactory : IBankClientFactory
{
    private readonly IEnumerable<IBankClient> _clients;

    public BankClientFactory(IEnumerable<IBankClient> clients)
        => _clients = clients;

    public IBankClient GetClient(BankAccountConfiguration accountSettings)
    {
        return _clients.SingleOrDefault(c => c.Provider == accountSettings.Provider)
            ?? throw new InvalidOperationException(
                $"No bank client registered for provider '{accountSettings.Provider}'");
    }
}
```

### Updated `BankModule`
```csharp
public static IServiceCollection AddBankModule(this IServiceCollection services, IConfiguration configuration)
{
    services.AddAutoMapper(typeof(BankMappingProfile));
    services.AddTransient<IBankClientFactory, BankClientFactory>();

    // BankAccountSettings owned here — neutral location, not inside any adapter
    services.Configure<BankAccountSettings>(configuration.GetSection(BankAccountSettings.ConfigurationKey));

    return services;
}
```

> `BankModule.AddBankModule` gains an `IConfiguration` parameter. The call site in `Program.cs` (via `AddApplicationServices`) must pass `configuration`.

### Updated `BankImportRequestDto`
```csharp
public class BankImportRequestDto
{
    [Required]
    public string AccountName { get; set; } = null!;

    [Required]
    public DateTime DateFrom { get; set; }

    [Required]
    public DateTime DateTo { get; set; }
}
```

### Updated `ImportBankStatementRequest`
```csharp
public class ImportBankStatementRequest : IRequest<ImportBankStatementResponse>
{
    public string AccountName { get; set; } = null!;
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }

    public ImportBankStatementRequest(string accountName, DateTime dateFrom, DateTime dateTo)
    {
        AccountName = accountName;
        DateFrom = dateFrom;
        DateTo = dateTo;
    }
}
```

### Updated `BankStatementsController`
```csharp
var importRequest = new ImportBankStatementRequest(request.AccountName, request.DateFrom, request.DateTo);
```

### Updated `ImportBankStatementHandler`
- Inject `IBankClientFactory` instead of `IBankClient`
- Resolve: `var client = _factory.GetClient(accountSetting);`
- Call: `await client.GetStatementsAsync(accountSetting.AccountNumber, request.DateFrom, request.DateTo)`
- Date passed to `BankStatementImport` entity: use `statement.Date` from `BankStatementHeader` per individual statement
- Currency: use `accountSetting.Currency` instead of `request.AccountName.EndsWith("EUR")` heuristic

### Updated jobs

Both jobs pass yesterday as a single-day range:

```csharp
// ComgateCzkImportJob
var yesterday = DateTime.Today.AddDays(-1);
var request = new ImportBankStatementRequest("ComgateCZK", yesterday, yesterday);

// ComgateEurImportJob
var yesterday = DateTime.Today.AddDays(-1);
var request = new ImportBankStatementRequest("ComgateEUR", yesterday, yesterday);
```

> Account name strings `"ComgateCZK"` / `"ComgateEUR"` are preserved. Config `Name` values must match exactly (see Section 9).

---

## 7. Comgate Adapter Changes (`Anela.Heblo.Adapters.Comgate`)

### `ComgateAdapterServiceCollectionExtensions`
- Remove `services.Configure<BankAccountSettings>(...)` — now owned by `BankModule`
- `IBankClient` registration stays `Transient`:
```csharp
services.AddTransient<IBankClient, ComgateBankClient>();
```

### `ComgateBankClient`
- Add `public BankClientProvider Provider => BankClientProvider.Comgate;`
- Update signature: `GetStatementsAsync(string accountNumber, DateTime dateFrom, DateTime dateTo)`
- Iterate each calendar day in `[dateFrom, dateTo]` (inclusive), call Comgate API once per day, aggregate all results into one list

### `AboFile` removal
- Delete `AboFile`, `AboHeader`, `AboLine` from this file
- Add `using Anela.Heblo.Xcc.Abo;`

---

## 8. New Project: `Anela.Heblo.Adapters.ShoptetApi`

### Project file
```
backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/
├── Anela.Heblo.Adapters.ShoptetApi.csproj
├── ShoptetApiAdapterServiceCollectionExtensions.cs
└── ShoptetPay/
    ├── ShoptetPayBankClient.cs
    ├── ShoptetPaySettings.cs
    └── Model/
        ├── PayoutReportDto.cs
        └── PayoutReportListResponse.cs
```

`.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Anela.Heblo.Adapters.ShoptetApi</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />
  </ItemGroup>
</Project>
```

### `ShoptetPaySettings`
```csharp
public class ShoptetPaySettings
{
    public static string ConfigurationKey => "ShoptetPay";

    [Required]
    public string ApiToken { get; set; } = null!;

    [Required]
    public string BaseUrl { get; set; } = "https://api.shoptetpay.com";
}
```

### `PayoutReportDto`
```csharp
public class PayoutReportDto
{
    public string Id { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public string Type { get; set; } = null!;
    public int SerialNumber { get; set; }
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### `PayoutReportListResponse`
```csharp
public class PayoutReportListResponse
{
    public List<PayoutReportDto> Data { get; set; } = new();
    public int Total { get; set; }
}
```

### `ShoptetPayBankClient : IBankClient`

```csharp
public class ShoptetPayBankClient : IBankClient
{
    public BankClientProvider Provider => BankClientProvider.ShoptetPay;

    // GetStatementsAsync(accountNumber, dateFrom, dateTo):
    //   GET /v1/reports/payout?dateFrom={dateFrom:yyyy-MM-dd}&dateTo={dateTo:yyyy-MM-dd}&types=PAYOUT&limit=1000
    //   accountNumber param is ignored (no account number concept in ShoptetPay)
    //   Maps PayoutReportDto → BankStatementHeader:
    //     StatementId = report.Id
    //     Date        = report.DateTo   (end of the settlement period)
    //     Account     = report.Currency  (e.g. "CZK")
    //
    // GetStatementAsync(statementId):
    //   GET /v1/reports/payout/{statementId}/abo
    //   Reads raw ABO string
    //   Parses with AboFile.Parse() (from Anela.Heblo.Xcc.Abo)
    //   Returns BankStatementData { StatementId, Data = raw ABO, ItemCount = abo.Lines.Count }
    //
    // Auth: injected HttpClient has DefaultRequestHeaders.Authorization = Bearer {ApiToken}
}
```

> **Date format:** The API expects `yyyy-MM-dd` (confirmed from source document examples: `dateFrom=2026-01-01`). Do **not** use the `:O` round-trip format specifier.

### `ShoptetApiAdapterServiceCollectionExtensions`
```csharp
public static IServiceCollection AddShoptetApiAdapter(this IServiceCollection services, IConfiguration configuration)
{
    services.AddOptions<ShoptetPaySettings>()
        .Bind(configuration.GetSection(ShoptetPaySettings.ConfigurationKey))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    services.AddHttpClient<ShoptetPayBankClient>((sp, client) =>
    {
        var settings = sp.GetRequiredService<IOptions<ShoptetPaySettings>>().Value;
        client.BaseAddress = new Uri(settings.BaseUrl);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", settings.ApiToken);
    });

    // Register as IBankClient using the typed client already configured above
    services.AddTransient<IBankClient>(sp => sp.GetRequiredService<ShoptetPayBankClient>());

    return services;
}
```

> **HttpClient registration:** `AddHttpClient<ShoptetPayBankClient>` registers the typed client with the configured `HttpClient`. The separate `AddTransient<IBankClient>` resolves via `sp.GetRequiredService<ShoptetPayBankClient>()` to ensure the same configured instance is used — avoiding the double-registration bug where an un-configured `ShoptetPayBankClient` would be returned.

> **Both `IBankClient` registrations are `Transient`.** `BankClientFactory` (also `Transient`) receives `IEnumerable<IBankClient>` — DI resolves all registered implementations. No singleton-captures-transient issue.

---

## 9. `Program.cs` Changes

```csharp
// Existing (line 53):
builder.Services.AddComgateAdapter(builder.Configuration);

// Add after:
builder.Services.AddShoptetApiAdapter(builder.Configuration);
```

> **Registration order:** Both adapter registrations (`AddComgateAdapter`, `AddShoptetApiAdapter`) happen at startup, before any MediatR handler resolves. `BankClientFactory` receives `IEnumerable<IBankClient>` lazily at first resolution — order of adapter registration does not matter.

Update call to `AddBankModule` to pass `configuration`:
```csharp
// Inside AddApplicationServices (or wherever AddBankModule is called)
services.AddBankModule(configuration);
```

---

## 10. Configuration (`appsettings.json`)

```json
"BankAccounts": {
  "Accounts": [
    {
      "Name": "ComgateCZK",
      "Provider": "Comgate",
      "AccountNumber": "...",
      "FlexiBeeId": 123,
      "Currency": "CZK"
    },
    {
      "Name": "ComgateEUR",
      "Provider": "Comgate",
      "AccountNumber": "...",
      "FlexiBeeId": 456,
      "Currency": "EUR"
    },
    {
      "Name": "ShoptetPay-CZK",
      "Provider": "ShoptetPay",
      "AccountNumber": "",
      "FlexiBeeId": 789,
      "Currency": "CZK"
    }
  ]
},
"ShoptetPay": {
  "ApiToken": "YOUR_TOKEN_HERE",
  "BaseUrl": "https://api.shoptetpay.com"
}
```

> `Provider` values are resolved case-insensitively by `IConfiguration` binder to `BankClientProvider` enum.
> Comgate account `Name` values preserve existing strings `"ComgateCZK"` / `"ComgateEUR"` to match hardcoded job strings.
> `ApiToken` must be stored in user secrets / Azure Key Vault — not committed to source control.

---

## 11. Data Flow

```
ImportBankStatementRequest (AccountName, DateFrom, DateTo)
  → ImportBankStatementHandler
    → BankAccountConfiguration resolved by AccountName
    → IBankClientFactory.GetClient(accountSetting)  →  ShoptetPayBankClient
      → GET /v1/reports/payout?dateFrom=yyyy-MM-dd&dateTo=yyyy-MM-dd&types=PAYOUT&limit=1000
      → for each report: GET /v1/reports/payout/{id}/abo  →  AboFile.Parse()
      → BankStatementData { StatementId, Data (ABO string), ItemCount }
    → IBankStatementImportService.ImportStatementAsync(accountSetting.FlexiBeeId, aboData.Data)
    → new BankStatementImport(statement.StatementId, statement.Date)
    → IBankStatementImportRepository.AddAsync(import)
```

---

## 12. Out of Scope (F1)

- ShoptetPay webhooks (`cardPaymentState:change`) — future
- Card payment details (`/v1/card-payments`) — future
- Main Shoptet API (stocks, invoices) — F2, F3
- Token expiry / auto-refresh — API token managed manually in config
- Pagination beyond `limit=1000` — acceptable for F1; revisit if report volume exceeds limit
