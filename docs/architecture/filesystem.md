# 📁 Filesystem Structure Documentation

This document defines the project's directory structure and filesystem organization following **Vertical Slice Architecture** with MediatR + Controllers.

---

## 📁 Directory Structure

```
/                  # Monorepo root
├── backend/       # Backend – ASP.NET Core application
│   ├── src/       # Application code
│   │   ├── Anela.Heblo.API/           # Host/Composition project (Controllers + serves React)
│   │   │   ├── Controllers/           # MVC Controllers for API endpoints
│   │   │   │   ├── PurchaseOrdersController.cs
│   │   │   │   ├── CatalogController.cs
│   │   │   │   └── WeatherController.cs
│   │   │   ├── Extensions/            # Service registration & configuration
│   │   │   │   ├── ServiceCollectionExtensions.cs
│   │   │   │   ├── LoggingExtensions.cs
│   │   │   │   └── AuthenticationExtensions.cs
│   │   │   ├── Authentication/        # Authentication handlers
│   │   │   │   └── MockAuthenticationHandler.cs
│   │   │   └── Program.cs             # Application entry point
│   │   ├── Anela.Heblo.Domain/        # Domain layer - domain entities and repository interfaces
│   │   │   ├── Features/              # Feature-specific domain objects
│   │   │   │   ├── Audit/             # Audit domain (empty - contracts in Application)
│   │   │   │   ├── Bank/              # Banking domain objects
│   │   │   │   │   ├── BankAccountConfiguration.cs
│   │   │   │   │   ├── BankStatementData.cs
│   │   │   │   │   └── IBankClient.cs
│   │   │   │   ├── CashRegister/      # Cash register domain objects
│   │   │   │   │   ├── CashRegister.cs
│   │   │   │   │   ├── CashRegisterOrder.cs
│   │   │   │   │   └── ICashRegisterOrdersSource.cs
│   │   │   │   ├── Catalog/           # Product catalog domain (complex)
│   │   │   │   │   ├── CatalogAggregate.cs
│   │   │   │   │   ├── ProductType.cs
│   │   │   │   │   ├── ICatalogRepository.cs
│   │   │   │   │   ├── Stock/         # Stock management subdomain
│   │   │   │   │   │   ├── ErpStock.cs
│   │   │   │   │   │   ├── EshopStock.cs
│   │   │   │   │   │   └── IStockTakingRepository.cs
│   │   │   │   │   ├── Sales/         # Sales tracking subdomain
│   │   │   │   │   │   ├── CatalogSaleRecord.cs
│   │   │   │   │   │   └── ICatalogSalesClient.cs
│   │   │   │   │   ├── Price/         # Pricing subdomain
│   │   │   │   │   │   ├── ProductPriceErp.cs
│   │   │   │   │   │   └── IProductPriceErpClient.cs
│   │   │   │   │   └── PurchaseHistory/ # Purchase history subdomain
│   │   │   │   │       ├── CatalogPurchaseRecord.cs
│   │   │   │   │       └── IPurchaseHistoryClient.cs
│   │   │   │   ├── Configuration/     # Application configuration
│   │   │   │   │   ├── ApplicationConfiguration.cs
│   │   │   │   │   └── ConfigurationConstants.cs
│   │   │   │   ├── Invoices/          # Invoice processing domain
│   │   │   │   │   ├── IssuedInvoiceDetail.cs
│   │   │   │   │   ├── InvoiceCustomer.cs
│   │   │   │   │   └── IIssuedInvoiceSource.cs
│   │   │   │   ├── Logistics/         # Logistics and transport
│   │   │   │   │   ├── Carriers.cs
│   │   │   │   │   ├── Warehouses.cs
│   │   │   │   │   ├── Transport/     # Transport box management
│   │   │   │   │   │   ├── TransportBox.cs
│   │   │   │   │   │   └── ITransportBoxRepository.cs
│   │   │   │   │   └── Picking/       # Picking list functionality
│   │   │   │   │       └── IPickingListSource.cs
│   │   │   │   ├── Manufacture/       # Manufacturing domain
│   │   │   │   │   ├── Ingredient.cs
│   │   │   │   │   └── IManufactureRepository.cs
│   │   │   │   ├── Purchase/          # Purchase order domain
│   │   │   │   │   ├── PurchaseOrder.cs
│   │   │   │   │   ├── PurchaseOrderLine.cs
│   │   │   │   │   ├── IPurchaseOrderRepository.cs
│   │   │   │   │   └── Supplier.cs
│   │   │   │   └── Weather/           # Weather forecast domain
│   │   │   │       ├── WeatherForecast.cs
│   │   │   │       └── WeatherConstants.cs
│   │   │   └── Shared/               # Cross-cutting domain utilities
│   │   │       ├── Kernel/           # Domain base classes
│   │   │       │   ├── Result.cs
│   │   │       │   ├── IAggregateRoot.cs
│   │   │       │   └── DomainEvent.cs
│   │   │       └── Users/            # User management utilities
│   │   │           ├── CurrentUser.cs
│   │   │           ├── ICurrentUserService.cs
│   │   │           └── CurrentUserExtensions.cs
│   │   ├── Anela.Heblo.Application/   # Application services and handlers
│   │   │   ├── Features/              # Feature-specific application services
│   │   │   │   ├── Audit/              # Audit log feature
│   │   │   │   │   ├── GetAuditLogsHandler.cs
│   │   │   │   │   ├── GetAuditSummaryHandler.cs
│   │   │   │   │   ├── Model/         # Request/Response DTOs
│   │   │   │   │   │   ├── GetAuditLogsRequest.cs
│   │   │   │   │   │   └── GetAuditLogsResponse.cs
│   │   │   │   │   └── AuditModule.cs
│   │   │   │   ├── Catalog/           # Product catalog feature (complex)
│   │   │   │   │   ├── GetCatalogListHandler.cs
│   │   │   │   │   ├── GetCatalogDetailHandler.cs
│   │   │   │   │   ├── CatalogRefreshBackgroundService.cs
│   │   │   │   │   ├── Refresh*DataHandler.cs (14+ handlers)
│   │   │   │   │   ├── Model/         # Catalog DTOs
│   │   │   │   │   │   ├── CatalogItemDto.cs
│   │   │   │   │   │   ├── GetCatalogListRequest.cs
│   │   │   │   │   │   └── RefreshDataRequests.cs
│   │   │   │   │   ├── Fakes/         # Test implementations
│   │   │   │   │   │   └── EmptyTransportBoxRepository.cs
│   │   │   │   │   └── CatalogModule.cs
│   │   │   │   ├── Configuration/     # App configuration feature
│   │   │   │   │   ├── GetConfigurationHandler.cs
│   │   │   │   │   ├── Model/
│   │   │   │   │   │   ├── GetConfigurationRequest.cs
│   │   │   │   │   │   └── GetConfigurationResponse.cs
│   │   │   │   │   └── ConfigurationModule.cs
│   │   │   │   ├── Purchase/          # Purchase order feature
│   │   │   │   │   ├── CreatePurchaseOrderHandler.cs
│   │   │   │   │   ├── GetPurchaseOrdersHandler.cs
│   │   │   │   │   ├── GetPurchaseOrderByIdHandler.cs
│   │   │   │   │   ├── UpdatePurchaseOrderHandler.cs
│   │   │   │   │   ├── UpdatePurchaseOrderStatusHandler.cs
│   │   │   │   │   ├── Model/         # Purchase DTOs
│   │   │   │   │   │   ├── CreatePurchaseOrderRequest.cs
│   │   │   │   │   │   ├── CreatePurchaseOrderResponse.cs
│   │   │   │   │   │   └── GetPurchaseOrdersRequest.cs
│   │   │   │   │   ├── Infrastructure/
│   │   │   │   │   │   └── PurchaseOrderRepository.cs
│   │   │   │   │   └── PurchaseModule.cs
│   │   │   │   └── Weather/           # Weather forecast feature
│   │   │   │       ├── GetWeatherForecastHandler.cs
│   │   │   │       ├── Model/
│   │   │   │       │   ├── GetWeatherForecastRequest.cs
│   │   │   │       │   └── GetWeatherForecastResponse.cs
│   │   │   │       └── WeatherModule.cs
│   │   │   └── ApplicationModule.cs  # Central module registration
│   │   ├── Anela.Heblo.Persistence/   # Shared database infrastructure
│   │   │   ├── ApplicationDbContext.cs # Single DbContext (initially)
│   │   │   ├── Repository/            # Generic repository pattern
│   │   │   │   ├── IRepository.cs    # Generic repository interface
│   │   │   │   └── Repository.cs     # Concrete EF repository implementation
│   │   │   ├── Configurations/        # EF Core entity configurations
│   │   │   ├── Migrations/            # EF Core migrations
│   │   │   └── Services/              # Infrastructure services
│   │   │       └── TelemetryService.cs
│   │   ├── Anela.Heblo.Domain/        # Domain layer (shared entities)
│   │   │   ├── Entities/              # Domain entities
│   │   │   └── Constants/             # Domain constants
│   │   └── Anela.Heblo.API.Client/    # Auto-generated OpenAPI client
│   ├── test/      # Unit/integration tests
│   │   ├── Anela.Heblo.API.Tests/
│   │   ├── Anela.Heblo.Application.Tests/
│   │   └── Anela.Heblo.Persistence.Tests/
│   └── scripts/   # Utility scripts (e.g. DB tools, backups)
│
├── frontend/      # React PWA (builds into backend wwwroot)
│   ├── public/     # Static assets (index.html, favicon, etc.)
│   ├── src/
│   │   ├── components/
│   │   │   └── __tests__/    # Component unit tests
│   │   ├── pages/
│   │   │   └── __tests__/    # Page component tests
│   │   ├── api/         # API client and services
│   │   │   └── __tests__/    # API client unit tests
│   │   ├── auth/        # Authentication logic
│   │   │   └── __tests__/    # Authentication tests
│   │   ├── config/      # Configuration management
│   │   │   └── __tests__/    # Configuration tests
│   │   └── ...
│   ├── test/       # UI automation tests (Playwright only)
│   │   ├── ui/          # UI/Layout tests (Playwright)
│   │   │   └── layout/  # Layout component UI tests
│   │   ├── integration/ # Integration tests
│   │   └── e2e/         # End-to-end tests
│   └── package.json # Node.js dependencies and scripts
│
├── docs/          # Project documentation
│   ├── architecture/       # Architecture documentation
│   │   ├── filesystem.md
│   │   ├── environments.md
│   │   ├── application_infrastructure.md
│   │   └── observability.md
│   ├── design/            # UI/UX design documentation
│   │   ├── ui_design_document.md
│   │   ├── layout_definition.md
│   │   └── styleguide.md
│   ├── features/          # Feature-specific documentation
│   │   └── Authentication.md
│   └── tasks/             # Reusable task definitions
│       ├── backend-clean-architecture-refactoring.md
│       └── AUTHENTICATION_TESTING.md
├── scripts/       # Development and deployment scripts
│   ├── build-and-push.sh
│   ├── deploy-azure.sh
│   └── run-playwright-tests.sh
├── .github/        # GitHub Actions workflows
├── .env            # Dev environment variables
├── Dockerfile      # Single image for backend + frontend
├── docker-compose.yml # For local dev/test if needed
├── CLAUDE.md       # AI assistant instructions
└── .dockerignore   # Docker build optimization
```
## 🏗️ Clean Architecture Implementation

**The backend follows Clean Architecture with Vertical Slice organization and MediatR + Controllers:**

### Project Structure:
- **Anela.Heblo.API**: Host/Composition layer - MVC Controllers, MediatR integration, serves React app
- **Anela.Heblo.Domain**: Domain layer - entities, domain services, contracts (MediatR DTOs), repository interfaces
- **Anela.Heblo.Application**: Application layer - MediatR handlers, infrastructure implementations, business logic
- **Anela.Heblo.Persistence**: Infrastructure layer - database contexts, configurations, shared repository implementations

### Feature Module Structure:
Each feature is organized as vertical slices across domain and application layers:

**Domain Layer** (`Anela.Heblo.Domain/Features/{Feature}/`):
- **Feature root**: Domain entities, aggregates, value objects, domain services, repository interfaces
- **Subdomains**: Complex features may have subdomain folders (e.g., Catalog/Stock/, Catalog/Sales/)

**Application Layer** (`Anela.Heblo.Application/Features/{Feature}/`):
- **Handler files**: MediatR handlers (Application Services) - directly in feature root
- **Model/**: MediatR request/response DTOs and interfaces  
- **Infrastructure/**: Repository implementations and other infrastructure services (if needed)
- **FeatureModule.cs**: Dependency injection registration

### Key Principles:
- **Vertical organization**: Each feature contains all its layers
- **MediatR pattern**: Controllers send requests to handlers via MediatR
- **Handlers as Application Services**: Business logic resides in MediatR handlers
- **Standard endpoints**: All endpoints follow /api/{controller} pattern
- **Generic Repository**: Concrete EF implementation in Persistence, used directly by features
- **Single DbContext**: Initially shared in Persistence project, designed to evolve to module-specific contexts
- **SOLID principles**: Applied within each vertical slice

### Database Evolution Path:

**Phase 1 (Current):**
- Single `ApplicationDbContext` in `Anela.Heblo.Persistence`
- All entities registered in one context
- Shared migrations in `Persistence/Migrations/`

**Phase 2 (Future):**
- Each module will have its own DbContext
- Module-specific migrations with unique history tables
- Example structure:
  ```
  Features/Orders/Infrastructure/
  ├── OrdersDbContext.cs
  ├── Migrations/
  │   └── [timestamp]_InitialOrdersSchema.cs
  └── Configurations/
      └── OrderConfiguration.cs
  ```
- Migration command: `dotnet ef migrations add InitOrders --context OrdersDbContext --output-dir Application/Features/Orders/Infrastructure/Migrations`
- Each context configured with: `optionsBuilder.UseSqlServer(connection, x => x.MigrationsHistoryTable("__EFMigrationsHistory_Orders"))`

---

## 🧪 Test Organization Structure

**Frontend tests follow standard React patterns:**

### **Unit & Integration Tests (Jest + React Testing Library)**
**Tests are located in `__tests__/` folders next to the components they test:**

- **`/frontend/src/api/__tests__/`** - API client unit tests
  - `api-client.test.ts` - Bearer token authentication, error handling
  - `client.test.ts` - Client factory and configuration tests
- **`/frontend/src/components/__tests__/`** - React component tests
  - Individual component test files (e.g., `Button.test.tsx`)
- **`/frontend/src/components/pages/__tests__/`** - Page component tests
  - `WeatherTest.test.tsx` - Page component integration tests
- **`/frontend/src/auth/__tests__/`** - Authentication logic tests
  - `useAuth.test.ts` - Real Azure AD authentication hook tests
  - `mockAuth.test.ts` - Mock authentication tests
- **`/frontend/src/config/__tests__/`** - Configuration management tests
  - `runtimeConfig.test.ts` - Runtime configuration loading tests

### **UI Automation Tests (Playwright)**
**UI tests are in separate `/frontend/test/` directory:**

- **`/frontend/test/ui/layout/{component}/`** - Visual and interaction tests
  - `sidebar/` - Sidebar collapse/expand, navigation, responsive behavior
  - `statusbar/` - Status bar positioning, content, responsiveness  
  - `auth/` - Authentication flows, login/logout UI behavior
  - `topbar/` - Top navigation, menu interactions
  - `general/` - Overall layout, responsive design, page structure
- **`/frontend/test/integration/`** - Component interaction testing
- **`/frontend/test/e2e/`** - Full user journey testing

**CRITICAL Test Environment Rules:**
- **Unit/Integration Tests**: Use Jest with mocked dependencies, located in `__tests__/` folders
- **UI/Playwright Tests**: MUST use automation environment (ports 3001/5001) with mock authentication, located in `/frontend/test/`
- **Test Co-location**: Unit tests are co-located with components for easy maintenance

---

## 🔧 OpenAPI Client Generation

### Backend C# Client

- **Location**: `backend/src/Anela.Heblo.API.Client/`
- **Auto-generation**: PostBuild event in API project (Debug mode only)
- **Tool**: NSwag with System.Text.Json
- **Output**: `Generated/AnelaHebloApiClient.cs`
- **Manual Generation**: Scripts available (`generate-client.ps1`, `generate-client.sh`)

  

### Frontend TypeScript Client

- **Location**: `frontend/src/api/generated/api-client.ts`
- **Auto-generation**: Via backend PostBuild event or frontend prebuild script
- **Tool**: NSwag with Fetch API template (currently placeholder implementation with bearer token support)
- **Manual Generation**: `npm run generate-client` in frontend directory
- **Build Integration**: Automatically generated before frontend build (`prebuild` script)

---

## 📦 Key File Locations

### Configuration Files
- **Environment Variables**: `.env` (project root)
- **Backend Settings**: `backend/src/Anela.Heblo.API/appsettings.{Environment}.json`
- **Frontend Settings**: `frontend/.env` (environment-specific)

### Build & Deployment
- **Docker**: `Dockerfile` (project root)
- **Compose**: `docker-compose.yml` (project root)
- **CI/CD**: `.github/workflows/` (GitHub Actions)

### Database
- **Migrations**: `backend/src/Anela.Heblo.Persistence/Migrations/` (EF Core)
- **Scripts**: `backend/scripts/` (utility tools)

### Generated Code
- **Backend Client**: `backend/src/Anela.Heblo.API.Client/Generated/`
- **Frontend Client**: `frontend/src/api/generated/`