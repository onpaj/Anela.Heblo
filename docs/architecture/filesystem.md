# 📁 Filesystem Structure Documentation

This document defines the project's directory structure and filesystem organization following **Vertical Slice Architecture** with FastEndpoints.

---

## 📁 Directory Structure

```
/                  # Monorepo root
├── backend/       # Backend – ASP.NET Core application
│   ├── src/       # Application code
│   │   ├── Anela.Heblo.API/           # Host/Composition project (FastEndpoints + serves React)
│   │   │   ├── Endpoints/             # FastEndpoints organized by features
│   │   │   │   ├── Orders/
│   │   │   │   │   ├── CreateOrderEndpoint.cs
│   │   │   │   │   └── GetOrderEndpoint.cs
│   │   │   │   ├── Products/
│   │   │   │   └── Invoices/
│   │   │   ├── Extensions/            # Service registration & configuration
│   │   │   │   ├── ServiceCollectionExtensions.cs
│   │   │   │   ├── LoggingExtensions.cs
│   │   │   │   └── AuthenticationExtensions.cs
│   │   │   ├── Authentication/        # Authentication handlers
│   │   │   │   └── MockAuthenticationHandler.cs
│   │   │   └── Program.cs             # Application entry point
│   │   ├── Anela.Heblo.App/           # Feature modules and business logic
│   │   │   ├── features/              # Vertical slice feature modules
│   │   │   │   ├── orders/
│   │   │   │   │   ├── contracts/
│   │   │   │   │   │   ├── CreateOrderRequest.cs
│   │   │   │   │   │   ├── CreateOrderResponse.cs
│   │   │   │   │   │   └── IOrderService.cs
│   │   │   │   │   ├── application/
│   │   │   │   │   │   └── CreateOrderUseCase.cs
│   │   │   │   │   ├── domain/
│   │   │   │   │   │   ├── Order.cs
│   │   │   │   │   │   └── OrderItem.cs
│   │   │   │   │   ├── infrastructure/
│   │   │   │   │   │   └── OrderRepository.cs
│   │   │   │   │   └── OrdersModule.cs
│   │   │   │   ├── products/
│   │   │   │   ├── invoices/
│   │   │   │   └── catalog/
│   │   │   ├── Shared/               # Shared kernel
│   │   │   │   └── Kernel/
│   │   │   │       ├── Result.cs
│   │   │   │       ├── IAggregateRoot.cs
│   │   │   │       └── DomainEvent.cs
│   │   │   └── ModuleRegistration.cs  # Central module registration
│   │   ├── Anela.Heblo.Persistence/   # Shared database infrastructure
│   │   │   ├── ApplicationDbContext.cs # Single DbContext (initially)
│   │   │   ├── Configurations/        # EF Core entity configurations
│   │   │   └── Migrations/            # EF Core migrations
│   │   ├── Anela.Heblo.Xcc/           # Cross-cutting infrastructure
│   │   │   ├── Repository/            # Generic repository pattern
│   │   │   │   ├── IRepository.cs    # Generic repository interface
│   │   │   │   └── Repository.cs     # Generic repository implementation
│   │   │   ├── Time/
│   │   │   │   └── ITimeProvider.cs
│   │   │   ├── Logging/
│   │   │   │   └── ITelemetryService.cs
│   │   │   └── Messaging/
│   │   │       └── IMessageDispatcher.cs
│   │   └── Anela.Heblo.API.Client/    # Auto-generated OpenAPI client
│   ├── test/      # Unit/integration tests
│   │   ├── Anela.Heblo.API.Tests/
│   │   ├── Anela.Heblo.App.Tests/
│   │   └── Anela.Heblo.Xcc.Tests/
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
## 🏗️ Vertical Slice Architecture Implementation

**The backend follows Vertical Slice Architecture with FastEndpoints:**

### Project Structure:
- **Anela.Heblo.API**: Host/Composition layer - FastEndpoints, DI composition, serves React app
- **Anela.Heblo.App**: Feature modules with vertical slices containing all layers
- **Anela.Heblo.Persistence**: Shared database infrastructure (single DbContext initially)
- **Anela.Heblo.Xcc**: Cross-cutting concerns - generic repository, time, logging, messaging

### Feature Module Structure:
Each feature in `Anela.Heblo.App/features/` contains:
- **contracts/**: Public interfaces, DTOs (Request/Response)
- **application/**: Use cases, orchestration, service implementations
- **domain/**: Entities, aggregates, value objects, business rules
- **infrastructure/**: Repository implementations (using generic repository from Xcc)
- **Module.cs**: DI registration for the feature

### Key Principles:
- **Vertical organization**: Each feature contains all its layers
- **Module isolation**: Features communicate only through contracts
- **FastEndpoints**: Thin HTTP layer that delegates to use cases
- **Generic Repository**: Base implementation in Xcc, extended in features as needed
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
  features/orders/infrastructure/
  ├── OrdersDbContext.cs
  ├── Migrations/
  │   └── [timestamp]_InitialOrdersSchema.cs
  └── Configurations/
      └── OrderConfiguration.cs
  ```
- Migration command: `dotnet ef migrations add InitOrders --context OrdersDbContext --output-dir App/features/orders/infrastructure/Migrations`
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