# 🛠️ Development Guidelines & Best Practices

This document defines development rules, conventions, and best practices for the Anela Heblo project following Vertical Slice Architecture.

---

## 📬 Contracts and DTOs Rules

### Mandatory Rules:
- DTO objects for API (`Request`, `Response`) **live in `contracts/` of the specific module**
- DTOs are **never shared or global**
- Communication between modules **exclusively through `contracts/`** (e.g., `IProductQueryService`)
- `API` project **never defines or owns DTOs** – it only uses them
- Each module owns its contract interfaces and DTOs
- **Module-specific bootstrap values belong to that module's own anonymous endpoint**, not to the cross-cutting `/api/configuration` response. The `Configuration` module exposes only application-wide values (version, environment, mock-auth flag). Module-specific values (e.g. Manufacture's responsible-person Entra group ID) live on a module-owned anonymous endpoint such as `/api/manufacture/settings`.

### Example:
```csharp
// ✅ CORRECT: In App/features/orders/contracts/
public class CreateOrderRequest { }
public class CreateOrderResponse { }
public interface IOrderService { }

// ❌ WRONG: In API project or shared location
public class OrderDto { } // Never in API or Xcc!
```

---

## ❌ Forbidden Practices

| Forbidden Practice | Reason |
|-------------------|---------|
| **Shared DbContext** | Violates separation, creates coupling |
| **Direct access to another module's entities** | Violates boundaries, tight coupling |
| **DTOs defined in API or Xcc** | Breaks ownership, violates boundaries |
| **Shared EF entities between modules** | Risk of inconsistency, domain logic leakage |
| **Business logic in Controller class** | Business logic should be in MediatR handlers |
| **Cross-module database joins** | Breaks module independence |
| **Shared repositories across modules** | Violates module isolation |
| **Backend constructing frontend URLs** | Violates separation of concerns, couples backend to frontend routing |

---

## ✅ Required Practices

### Module Independence
- Each module must be deployable as a separate microservice (future-ready)
- No direct references between feature modules
- Communication only through contracts/interfaces
- Each module has its own test project

### Code Organization
- **Vertical slices**: Keep all layers of a feature together
- **No layer-based folders**: Don't organize by Controllers/, Services/, Repositories/
- **Feature cohesion**: All code for a feature in one place

### Frontend-Backend Separation
- **Backend provides semantic data**: Return filter parameters, not constructed URLs
- **Frontend owns routing**: URL construction happens in frontend based on its routing structure
- **Backward compatibility**: Support both new filter-based and legacy URL approaches during transition

### User Identity Resolution

There is exactly **one** way to obtain the current user. See **ADR-005** for the full decision.

| Concern | Lives in |
| --- | --- |
| `ICurrentUserService` interface | `Anela.Heblo.Domain/Features/Users/` |
| `CurrentUserService` implementation (depends on `IHttpContextAccessor`) | `Anela.Heblo.API/Features/Users/` (outer ring, **not** Application) |
| DI registration | `Anela.Heblo.API/Features/Users/UsersModule.cs` (`AddUsersModule()`) |
| Resolution call-site | **inside MediatR handlers** that inject `ICurrentUserService` |

**Rules:**
- **Resolve identity inside the handler**, not the controller. Handlers inject `ICurrentUserService`.
- **Controllers never resolve identity** — no `GetCurrentUserId()` helper, no `ICurrentUserService` injection, no stamping `UserId`/`ModifiedBy` onto requests.
- **Request DTOs must not carry client-settable `UserId` / `ModifiedBy`** — these are server-resolved, never trusted from the client (spoofing hole).
- **Never inject `IHttpContextAccessor` into a handler** — always go through `ICurrentUserService`.
- When a GUID audit value is needed, use `Guid.TryParse(user.Id, out var id) ? id : null` — nullable, **no sentinel GUID** (see the `TransportBox` pattern in `CreateNewTransportBoxHandler`). Do **not** add a shared `UserIdResolver` helper unless a real consumer exists.

### Testing
- Unit tests for domain logic
- Integration tests for use cases
- Contract tests for module interfaces
- Each module tested in isolation

---

## 🧬 Persistence Guidelines

### Current State (Phase 1)
- Single `ApplicationDbContext` in `Anela.Heblo.Persistence`
- Shared migrations in `Persistence/Migrations/`
- Generic repository abstraction in `Xcc`, implementation in Persistence layer

### Future State (Phase 2)
- **Each module will have its own `DbContext`**
- **Separate migrations per module**
- **Module-specific migration history tables**
- **Optional: Separate schemas per module**

### Migration Commands (Future):
```bash
# Module-specific migrations
dotnet ef migrations add InitOrders \
  --context OrdersDbContext \
  --output-dir App/features/orders/infrastructure/Migrations

# Each DbContext configured with unique history table:
optionsBuilder.UseSqlServer(connection, x => 
    x.MigrationsHistoryTable("__EFMigrationsHistory_Orders"));
```

---

## 🔌 Dependency Injection Patterns

### Module Registration
Each module must have a `Module.cs` file:

```csharp
public static class OrdersModule
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services)
    {
        // Register module-specific services
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        
        // MediatR handlers are auto-registered
        
        return services;
    }
}
```

### Repository bindings belong to the slice, never to `PersistenceModule`

A repository's **implementation** lives in `Anela.Heblo.Persistence` (single `ApplicationDbContext`,
Phase 1 — see ADR-001), but its **DI binding** must be written in the owning module's
`{Feature}Module.cs`, exactly like the `IOrderRepository` line above. `PersistenceModule.cs` owns
**only shared infrastructure**: the `DbContext`, `NpgsqlDataSource`, interceptors, telemetry, and the
material-container code generator.

Registering a repo centrally in `PersistenceModule.cs` splits one slice's wiring across two layers,
turns `PersistenceModule` into a multi-module coupling/merge-conflict hotspot, and has already caused
a duplicate registration (`IDqtRunRepository`). The rule is enforced by
`PersistenceModuleTests.AddPersistenceServices_RegistersNoRepositoryBindings`.

### API Composition (Program.cs):
```csharp
// Aggregate all modules
services
    .AddCatalogModule()
    .AddOrdersModule()
    .AddInvoicesModule()
    .AddManufactureModule()
    .AddTransportModule()
    .AddPurchaseModule()
    .AddXccInfrastructure();
```

---

## 🔧 Recommended Tools & Libraries

| Tool | Purpose | Usage |
|------|---------|--------|
| **MediatR** | Request/Response pattern with handlers | Required for all business logic |
| **EF Core** | ORM and migrations | Database access |
| **AutoMapper** | DTO ↔ Domain mapping | Optional, for complex mappings |
| **MediatR** | Decoupling orchestration and handlers | Optional, for CQRS pattern |
| **FluentValidation** | Request validation | Integrated with FastEndpoints |
| **Polly** | Resilience and transient fault handling | External API calls |

---

## 📝 Coding Standards

### Naming Conventions
- **Features**: PascalCase plural (e.g., `Orders`, `Invoices`)
- **Handlers**: Verb + Noun + Handler (e.g., `CreateOrderHandler`)
- **Controllers**: Noun + Controller (e.g., `OrdersController`)
- **DTOs**: Purpose + Request/Response (e.g., `CreateOrderRequest`)

### File Organization
```
Anela.Heblo.Application/Features/
└── Orders/                          # Feature name (PascalCase)
    ├── Contracts/
    │   ├── CreateOrderRequest.cs    # MediatR request DTO
    │   ├── CreateOrderResponse.cs   # MediatR response DTO
    │   └── GetOrderRequest.cs       # Query request DTO
    ├── Application/
    │   ├── CreateOrderHandler.cs    # MediatR handler (Application Service)
    │   └── GetOrderHandler.cs       # Query handler
    ├── Domain/
    │   ├── Order.cs                 # Aggregate root
    │   ├── OrderItem.cs             # Entity
    │   └── OrderStatus.cs           # Value object
    └── Infrastructure/
        └── OrderRepository.cs       # Data access (using Persistence base repository)
```

---

## 🚀 Development Workflow

### Adding a New Feature
1. Create feature folder in `Application/features/`
2. Define contracts (interfaces and DTOs)
3. Implement domain entities
4. Create use cases in application layer
5. Implement infrastructure (repository)
6. Add Controller action with MediatR request in API project
7. Register module in Program.cs
8. Add tests

### Module Communication
When module A needs data from module B:
1. Define interface in module B's contracts
2. Implement interface in module B
3. Inject interface in module A
4. Never access module B's domain or infrastructure directly

### Cross-Module Communication Example: ILeafletKnowledgeSource

When module A needs **read-only access** to data in module B, the dependency must **invert**: the consumer owns the contract, the provider implements an adapter.

**Pattern:**
1. **Consumer (A) defines the contract.** Module A declares an interface in its own `Contracts/` folder, exposing only the operations it actually consumes (no speculative methods).
2. **Provider (B) implements the contract via an adapter.** Module B writes an adapter class that delegates to its existing internal services. The adapter lives in module B's `Infrastructure/`.
3. **Provider (B) registers the DI binding.** Module B's `{Module}.cs` registers `services.AddScoped<IConsumerContract, ProviderAdapter>();`. The consumer module never touches this registration.

**Concrete example in this codebase:**
- **Consumer contract**: `Anela.Heblo.Application.Features.Leaflet.Contracts.ILeafletKnowledgeSource` (Leaflet-owned)
- **Provider adapter**: `Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure.KnowledgeBaseLeafletSourceAdapter` (KnowledgeBase-owned)
- **DI registration**: `KnowledgeBaseModule.AddKnowledgeBaseModule` registers the binding
- **Validation**: A reflection-based test in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` enforces that Leaflet types contain no references to KnowledgeBase-owned namespaces. Future regressions fail CI.

---

## 🏗️ Architecture Decision Records (ADRs)

### ADR-001: Single DbContext Initially
- **Status**: Accepted
- **Context**: Starting with single DbContext for simplicity
- **Decision**: Use shared `ApplicationDbContext` in Phase 1
- **Consequences**: Easier initial development, migration to multiple contexts later

### ADR-002: Generic Repository Pattern
- **Status**: Accepted
- **Context**: Need consistent data access patterns
- **Decision**: Generic repository in Xcc, extended per feature
- **Consequences**: Reduced boilerplate, consistent API

### ADR-003: MediatR + Controllers Architecture
- **Status**: Accepted
- **Context**: Need clean separation between HTTP layer and business logic
- **Decision**: Use standard ASP.NET Core Controllers with MediatR for request handling
- **Consequences**: Clean architecture, testable handlers, standard /api/{controller} endpoints

### ADR-004: Repository DI Bindings Live in the Feature Module, Not `PersistenceModule`
- **Status**: Accepted (2026-06-05)
- **Context**: Repository **implementations** live in `Anela.Heblo.Persistence` because of the single
  shared `ApplicationDbContext` (ADR-001). But the **DI binding**
  (`services.AddScoped<IRepo, RepoImpl>()`) was written in two different places depending on the
  module: some feature modules registered their own repos in `{Feature}Module.cs` (correct), while
  ~15 modules had their repos registered centrally in `Anela.Heblo.Persistence/PersistenceModule.cs`.
  This split a single vertical slice's wiring across two layers, turned `PersistenceModule` into a
  multi-module coupling/merge-conflict hotspot, contradicted the documented DI pattern, and had
  already produced a duplicate registration (`IDqtRunRepository`).
- **Decision**: A repository's DI binding is **always** declared in its owning module's
  `{Feature}Module.cs`. `PersistenceModule.cs` registers **only shared infrastructure**: the
  `DbContext`, `NpgsqlDataSource`, interceptors, telemetry, and the material-container code generator
  — never a repository. Repository implementation classes are `public` so the Application-layer module
  can bind them. The implementation file still lives under `Persistence/{Feature}/`.
- **Consequences**:
  - Each vertical slice owns its full wiring in one file; adding a feature touches one module, not two.
  - `PersistenceModule` stays small and stable (no per-feature churn).
  - Enforced by `PersistenceModuleTests.AddPersistenceServices_RegistersNoRepositoryBindings`, which
    fails CI if any `*Repository` binding reappears in `PersistenceModule`.
  - Module-wiring tests that mock a repository must register the mock **after** the
    `Add{Feature}Module` call so the mock overrides the module's real binding.
- **Supersedes**: the previous mixed convention where `PersistenceModule` centrally registered
  repositories. All future implementations must follow this pattern.

### ADR-005: User Identity Resolution
- **Status**: Accepted
- **Context**: Identity resolution had drifted into two coexisting patterns — a `BaseApiController.GetCurrentUserId()` helper that duplicated `CurrentUserService`'s claim-priority chain (used by `Dashboard`, `CarrierCooling`, `GiftSettings`), alongside the handler-side `ICurrentUserService` pattern used by 60+ other handlers. A daily arch-review routine kept re-filing fragments of the same concern (impl location, missing `UsersModule`, dead controller injections, direct `IHttpContextAccessor` use) because the canonical rule was written down nowhere.
- **Decision**: There is exactly one way to obtain the current user. The `ICurrentUserService` interface lives in `Domain/Features/Users/`; the `CurrentUserService` implementation lives in `API/Features/Users/` (outer ring, since it depends on the web-only `IHttpContextAccessor`); DI is wired in `UsersModule.cs`. **Identity is resolved inside MediatR handlers** via injected `ICurrentUserService` — never in controllers, never via a controller helper, never via direct `IHttpContextAccessor`. Request DTOs carry no client-settable `UserId`/`ModifiedBy`. GUID audit values use `Guid.TryParse(user.Id, out var id) ? id : null` with no sentinel. See the *User Identity Resolution* practices section above.
- **Consequences**: `BaseApiController.GetCurrentUserId()`, the three outlier controllers' identity assignments, and the spoofable DTO fields are removed (PR #2602, the final convergence of five arch-review findings). This ADR is the single source of truth — arch-review should treat any controller-side identity resolution or handler-side `IHttpContextAccessor` use as a violation of an accepted decision, not a new finding.

---

## ⚠️ Common Pitfalls to Avoid

1. **Don't create shared services** - Each module should be independent
2. **Don't use EF navigation properties across modules** - Use IDs and contracts
3. **Don't put business logic in endpoints** - Use use cases
4. **Don't create "Common" or "Shared" projects** - Use Xcc for technical concerns only
5. **Don't bypass contracts** - Always communicate through interfaces
6. **Don't create anemic domain models** - Put behavior in entities
7. **Don't ignore module boundaries** - Respect the architecture
8. **Don't resolve user identity in controllers** - No `GetCurrentUserId()` helper, no `IHttpContextAccessor` in handlers; resolve via `ICurrentUserService` inside the handler (ADR-005)

---

## 📚 Further Reading

- [Vertical Slice Architecture](https://jimmybogard.com/vertical-slice-architecture/)
- [FastEndpoints Documentation](https://fast-endpoints.com/)
- [Domain-Driven Design](https://martinfowler.com/tags/domain%20driven%20design.html)
- [Feature Folders](https://scottsauber.com/2016/04/25/feature-folder-structure-in-asp-net-core/)

## Feature Flags

Use `IFeatureFlagChecker` for all flag evaluation in business code — never call OpenFeature SDK directly. Always use `FeatureFlagKeys` constants (never raw strings). See [docs/development/feature-flags.md](../development/feature-flags.md) for the full guide.