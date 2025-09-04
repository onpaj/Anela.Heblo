# 📁 Filesystem Structure Documentation

This document defines the project's directory structure and filesystem organization following **Vertical Slice Architecture** with MediatR + Controllers.

---

## 📁 Directory Structure Overview

```
/                  # Monorepo root
├── backend/       # Backend – ASP.NET Core application
│   ├── src/       # Application code
│   │   ├── Anela.Heblo.API/           # Host/Composition layer
│   │   │   ├── Controllers/           # MVC Controllers for API endpoints
│   │   │   │   └── {Feature}Controller.cs # One controller per feature
│   │   │   ├── Extensions/            # Service registration & configuration
│   │   │   ├── Authentication/        # Authentication handlers
│   │   │   └── Program.cs             # Application entry point
│   │   ├── Anela.Heblo.Domain/        # Domain layer
│   │   │   ├── Features/              # Feature-specific domain objects
│   │   │   │   └── {Feature}/         # Feature domain folder
│   │   │   │       ├── {Entity}.cs    # Domain entities
│   │   │   │       ├── I{Entity}Repository.cs # Repository interfaces
│   │   │   │       └── {Subdomain}/   # Optional subdomains for complex features
│   │   │   └── Shared/               # Cross-cutting domain utilities
│   │   ├── Anela.Heblo.Application/   # Application layer
│   │   │   ├── Features/              # Feature-specific application services
│   │   │   │   └── {Feature}/         # Feature application folder
│   │   │   │       ├── UseCases/      # MediatR handlers (for complex features)
│   │   │   │       │   └── {UseCase}/ # Use case folder: Handler.cs, Request.cs, Response.cs
│   │   │   │       ├── Contracts/     # Shared DTOs across use cases
│   │   │   │       ├── Services/      # Domain services and business logic
│   │   │   │       ├── Infrastructure/ # Feature infrastructure
│   │   │   │       ├── Validators/    # FluentValidation request validators
│   │   │   │       ├── {Feature}Repository.cs # Repository implementation
│   │   │   │       ├── {Feature}MappingProfile.cs # AutoMapper profile
│   │   │   │       ├── {Feature}Constants.cs # Feature constants
│   │   │   │       └── {Feature}Module.cs # DI registration
│   │   │   └── ApplicationModule.cs   # Central module registration
│   │   ├── Anela.Heblo.Persistence/   # Infrastructure layer
│   │   │   ├── ApplicationDbContext.cs # Single DbContext (initially)
│   │   │   ├── {Feature}/             # Feature-specific persistence (complex features)
│   │   │   │   ├── {Entity}Configuration.cs # EF Core entity configurations
│   │   │   │   └── {Entity}Repository.cs    # Feature-specific repositories
│   │   │   ├── Repositories/          # Generic/shared repositories
│   │   │   ├── Migrations/            # EF Core migrations
│   │   │   └── PersistenceModule.cs   # DI registration
│   │   └── Anela.Heblo.API.Client/    # Auto-generated OpenAPI client
│   ├── test/      # Unit/integration tests
│   └── scripts/   # Utility scripts
│
├── frontend/      # React PWA
│   ├── src/
│   │   ├── components/    # React components with co-located __tests__/
│   │   ├── pages/         # Page components with co-located __tests__/
│   │   ├── api/           # API client and services with co-located __tests__/
│   │   └── [other areas] # Other frontend areas with co-located __tests__/
│   ├── test/       # UI automation tests (Playwright)
│   │   ├── ui/          # UI/Layout tests
│   │   ├── integration/ # Integration tests
│   │   └── e2e/         # End-to-end tests
│   └── package.json
│
├── docs/          # Project documentation
├── scripts/       # Development and deployment scripts
├── .github/       # GitHub Actions workflows
└── [configuration files]
```

---

## 🏗️ Clean Architecture Implementation

**The backend follows Clean Architecture with Vertical Slice organization and MediatR + Controllers:**

### Project Layers:
- **Anela.Heblo.API**: Host/Composition layer - MVC Controllers, MediatR integration, serves React app
- **Anela.Heblo.Domain**: Domain layer - entities, domain services, repository interfaces
- **Anela.Heblo.Application**: Application layer - MediatR handlers, business logic, feature implementations
- **Anela.Heblo.Persistence**: Infrastructure layer - database contexts, configurations, shared repository implementations

---

## 📋 Feature Organization Patterns

### Simple Features (1-3 use cases):
```
Features/{Feature}/
├── Get{Entity}Handler.cs       # MediatR handler
├── Create{Entity}Handler.cs    # MediatR handler
├── Model/                      # Request/Response DTOs
│   ├── Get{Entity}Request.cs
│   ├── Get{Entity}Response.cs
│   ├── Create{Entity}Request.cs
│   └── Create{Entity}Response.cs
└── {Feature}Module.cs          # DI registration
```

### Complex Features (4+ use cases):
```
Features/{Feature}/
├── UseCases/                   # Use case handlers organized by functionality
│   ├── Get{Entity}List/
│   │   ├── Get{Entity}ListHandler.cs
│   │   ├── Get{Entity}ListRequest.cs
│   │   └── Get{Entity}ListResponse.cs
│   ├── Get{Entity}Detail/
│   ├── Create{Entity}/
│   ├── Update{Entity}/
│   └── Delete{Entity}/
├── Contracts/                  # Shared DTOs across use cases
│   ├── {Entity}Dto.cs
│   └── [Other shared DTOs]
├── Services/                   # Domain services and business logic
│   ├── I{Entity}Service.cs
│   └── {Entity}Service.cs
├── Infrastructure/             # Feature infrastructure
│   ├── {Entity}Scheduler.cs
│   ├── {Entity}FeatureFlags.cs
│   └── Exceptions/
├── Validators/                 # Request validation
│   ├── Create{Entity}RequestValidator.cs
│   └── Update{Entity}RequestValidator.cs
├── {Feature}Repository.cs      # Feature repository
├── {Feature}MappingProfile.cs  # AutoMapper profile
├── {Feature}Constants.cs       # Feature constants
└── {Feature}Module.cs         # DI registration
```

---

## 🎯 Component Placement Rules

### API Layer (`Anela.Heblo.API`):
- **Controllers/**: MVC Controllers that expose REST endpoints
  - One controller per feature: `{Feature}Controller.cs`
  - Controllers only orchestrate MediatR requests
  - Follow `/api/{controller}` routing pattern

### Domain Layer (`Anela.Heblo.Domain`):
- **Features/{Feature}/**: Domain entities, aggregates, repository interfaces
  - Domain entities: `{Entity}.cs`
  - Repository contracts: `I{Entity}Repository.cs`
  - Domain services interfaces
  - For complex domains, use subfolders: `{Feature}/{Subdomain}/`

### Application Layer (`Anela.Heblo.Application`):
- **Features/{Feature}/UseCases/**: MediatR handlers (business operations)
  - Each use case in separate folder with Handler, Request, Response
  - Use case naming: `Get{Entity}List`, `Create{Entity}`, `Update{Entity}`
- **Features/{Feature}/Contracts/**: Shared DTOs across multiple use cases
- **Features/{Feature}/Services/**: Domain services, background services
- **Features/{Feature}/Infrastructure/**: Feature-specific infrastructure
- **Features/{Feature}/Validators/**: FluentValidation request validators
- **Features/{Feature}/{Feature}Repository.cs**: Repository implementations
- **Features/{Feature}/{Feature}Module.cs**: DI container registration

### Infrastructure Layer (`Anela.Heblo.Persistence`):
- **ApplicationDbContext.cs**: Single DbContext (initially)
- **{Feature}/{Subdomain}/**: Feature-specific persistence (complex features)
  - Entity configurations: `{Entity}Configuration.cs`
  - Repository implementations: `{Entity}Repository.cs`
- **Repositories/**: Generic/shared repositories (`BaseRepository.cs`)
- **Mapping/**: Database-specific mappers for external systems
- **Migrations/**: EF Core migrations
- **PersistenceModule.cs**: DI container registration

---

## 🔧 Key Principles

- **Vertical organization**: Each feature contains all its layers
- **MediatR pattern**: Controllers send requests to handlers via MediatR
- **Handlers as Application Services**: Business logic resides in MediatR handlers
- **Standard endpoints**: All endpoints follow `/api/{controller}` pattern
- **Feature autonomy**: Each feature manages its own contracts, services, and infrastructure
- **SOLID principles**: Applied within each vertical slice

---

## 🧪 Test Organization Structure

**Frontend tests follow standard React patterns:**

### **Unit & Integration Tests (Jest + React Testing Library)**
**Tests are located in `__tests__/` folders next to the components they test:**
- **`src/api/__tests__/`** - API client unit tests
- **`src/components/__tests__/`** - React component tests
- **`src/pages/__tests__/`** - Page component tests
- **`src/auth/__tests__/`** - Authentication logic tests
- **`src/config/__tests__/`** - Configuration management tests

### **UI Automation Tests (Playwright)**
**UI tests are in separate `/frontend/test/` directory:**
- **`test/ui/layout/{component}/`** - Visual and interaction tests
- **`test/integration/`** - Component interaction testing
- **`test/e2e/`** - Full user journey testing

**CRITICAL Test Environment Rules:**
- **Unit/Integration Tests**: Use Jest with mocked dependencies, co-located with components
- **UI/Playwright Tests**: MUST use automation environment (ports 3001/5001), located in `/frontend/test/`

---

## 🔧 OpenAPI Client Generation

### Backend C# Client
- **Location**: `backend/src/Anela.Heblo.API.Client/`
- **Auto-generation**: PostBuild event in API project (Debug mode only)
- **Tool**: NSwag with System.Text.Json
- **Output**: `Generated/AnelaHebloApiClient.cs`

### Frontend TypeScript Client
- **Location**: `frontend/src/api/generated/api-client.ts`
- **Auto-generation**: Via backend PostBuild event or frontend prebuild script
- **Tool**: NSwag with Fetch API template
- **Build Integration**: Automatically generated before frontend build

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
- **Entity Configurations**: `backend/src/Anela.Heblo.Persistence/{Feature}/` (feature-specific)
- **Scripts**: `backend/scripts/` (utility tools)

### Generated Code
- **Backend Client**: `backend/src/Anela.Heblo.API.Client/Generated/`
- **Frontend Client**: `frontend/src/api/generated/`

---

## 🚀 Implementation Guidelines

### When Creating New Features:
1. **Start with Domain**: Define entities and repository interfaces in `Domain/Features/{Feature}/`
2. **Add Application Logic**: Create handlers in `Application/Features/{Feature}/`
3. **Configure Persistence**: Create entity configurations in `Persistence/{Feature}/` and repository implementations
4. **Expose via API**: Create controller in `API/Controllers/{Feature}Controller.cs`
5. **Choose Pattern**: Simple (flat handlers) vs Complex (UseCases/ structure) based on feature size
6. **Register Dependencies**: Update `{Feature}Module.cs` and `PersistenceModule.cs` for proper DI registration

### Naming Conventions:
- **Controllers**: `{Feature}Controller` (e.g., `CatalogController`)
- **Handlers**: `{Action}{Entity}Handler` (e.g., `GetCatalogListHandler`)
- **Requests/Responses**: `{Action}{Entity}Request/Response` (e.g., `GetCatalogListRequest`)
- **DTOs**: `{Entity}Dto` (e.g., `CatalogItemDto`)
- **Services**: `{Entity}Service` and `I{Entity}Service`
- **Entity Configurations**: `{Entity}Configuration` (e.g., `PurchaseOrderConfiguration`)
- **Repository Implementations**: `{Entity}Repository` (e.g., `TransportBoxRepository`)

### Evolution Path:
- **Simple → Complex**: Start with flat structure, migrate to UseCases/ when feature grows
- **Shared → Feature-specific**: Move shared concerns into feature-specific implementations as needed
- **Single → Multiple DbContexts**: Eventually split database contexts per feature for better isolation