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
| **Business logic in FastEndpoint class** | Orchestration should only be in App/application/ layer |
| **Cross-module database joins** | Breaks module independence |
| **Shared repositories across modules** | Violates module isolation |

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
        
        // Register use cases
        services.AddScoped<CreateOrderUseCase>();
        services.AddScoped<GetOrderUseCase>();
        
        return services;
    }
}
```

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
| **FastEndpoints** | HTTP orchestration without controllers | Required for all endpoints |
| **EF Core** | ORM and migrations | Database access |
| **AutoMapper** | DTO ↔ Domain mapping | Optional, for complex mappings |
| **MediatR** | Decoupling orchestration and handlers | Optional, for CQRS pattern |
| **FluentValidation** | Request validation | Integrated with FastEndpoints |
| **Polly** | Resilience and transient fault handling | External API calls |

---

## 📝 Coding Standards

### Naming Conventions
- **Features**: Lowercase plural (e.g., `orders`, `invoices`)
- **Use Cases**: Verb + Noun + UseCase (e.g., `CreateOrderUseCase`)
- **Endpoints**: Verb + Noun + Endpoint (e.g., `CreateOrderEndpoint`)
- **DTOs**: Purpose + Request/Response (e.g., `CreateOrderRequest`)

### File Organization
```
Anela.Heblo.Application/features/
└── orders/                          # Feature name (plural, lowercase)
    ├── contracts/
    │   ├── IOrderService.cs         # Interface for other modules
    │   ├── CreateOrderRequest.cs    # Input DTO
    │   └── CreateOrderResponse.cs   # Output DTO
    ├── application/
    │   ├── CreateOrderUseCase.cs    # Business logic orchestration
    │   └── OrderService.cs          # Contract implementation
    ├── domain/
    │   ├── Order.cs                 # Aggregate root
    │   ├── OrderItem.cs             # Entity
    │   └── OrderStatus.cs           # Value object
    ├── infrastructure/
    │   └── OrderRepository.cs       # Data access (using Persistence base repository)
    └── OrdersModule.cs              # DI registration
```

---

## 🚀 Development Workflow

### Adding a New Feature
1. Create feature folder in `Application/features/`
2. Define contracts (interfaces and DTOs)
3. Implement domain entities
4. Create use cases in application layer
5. Implement infrastructure (repository)
6. Add FastEndpoint in API project
7. Register module in Program.cs
8. Add tests

### Module Communication
When module A needs data from module B:
1. Define interface in module B's contracts
2. Implement interface in module B
3. Inject interface in module A
4. Never access module B's domain or infrastructure directly

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

### ADR-003: FastEndpoints over Controllers
- **Status**: Accepted
- **Context**: Need thin HTTP layer focused on orchestration
- **Decision**: Use FastEndpoints for all HTTP endpoints
- **Consequences**: Better separation of concerns, less ceremony

---

## ⚠️ Common Pitfalls to Avoid

1. **Don't create shared services** - Each module should be independent
2. **Don't use EF navigation properties across modules** - Use IDs and contracts
3. **Don't put business logic in endpoints** - Use use cases
4. **Don't create "Common" or "Shared" projects** - Use Xcc for technical concerns only
5. **Don't bypass contracts** - Always communicate through interfaces
6. **Don't create anemic domain models** - Put behavior in entities
7. **Don't ignore module boundaries** - Respect the architecture

---

## 📚 Further Reading

- [Vertical Slice Architecture](https://jimmybogard.com/vertical-slice-architecture/)
- [FastEndpoints Documentation](https://fast-endpoints.com/)
- [Domain-Driven Design](https://martinfowler.com/tags/domain%20driven%20design.html)
- [Feature Folders](https://scottsauber.com/2016/04/25/feature-folder-structure-in-asp-net-core/)