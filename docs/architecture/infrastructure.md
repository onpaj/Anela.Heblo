# 📘 Application Infrastructure Design Document

> **Stack**: Monorepo (`.NET 8` + `React`), Vertical Slice Architecture with MediatR + Controllers, Single Docker image deployment, Azure Web App for Containers, GitHub + GitHub Actions, EF Core, Docker Hub.

---

## 1. 📁 Directory Structure

> **Note**: Detailed filesystem structure documentation has been moved to [`filesystem.md`](./filesystem.md).

---

## 2. 🌍 Environment Definition

> **Note**: Detailed environment configuration documentation has been moved to [`environments.md`](./environments.md).


---

## 3. 🔁 CI/CD Rules & Workflow

- **CI runs on all branches**: build, unit tests, UI tests (Playwright), Docker image build.
- **Feature branches**:
  - Optional deployment to `test` environment via GitHub Actions manual trigger.
- **Main branch**:
  - Merge allowed only if CI succeeds (including Playwright tests).
  - Automatic deployment to production Azure Web App for Containers.

### Deployment Architecture:

> **Note**: Detailed deployment architecture is documented in [`environments.md`](./environments.md).
- Docker images are pushed to **Docker Hub** with semantic versioning tags.
- Deployment implemented via **GitHub Actions** to Azure Web App for Containers.
- Environment-specific configuration via Azure App Settings.
- **CI validation removed**: Deployment workflows no longer wait for CI to pass (as of current implementation).
- **Container versioning**: Production deployments use **specific version tags** (e.g., `v1.2.3`) instead of `latest` tag for reliable, traceable deployments.

---

## 4. 🐳 Docker Build Strategy

### Single Image Architecture:
- **Base Image**: `mcr.microsoft.com/dotnet/aspnet:8.0` (production) / `mcr.microsoft.com/dotnet/sdk:8.0` (build)
- **Multi-stage build**:
  1. **Frontend Build Stage**: Node.js to build React app (`npm run build`)
  2. **Backend Build Stage**: .NET SDK to build ASP.NET Core app
  3. **Runtime Stage**: Copy built React files to `wwwroot/` and ASP.NET Core binaries
- **Final Container**: ASP.NET Core serves React static files + API endpoints
- **Port**: Container exposes port `80` (internal), Azure maps to `443` (HTTPS)

### Build Process:
```dockerfile
# Multi-stage Dockerfile example structure:
FROM node:18 AS frontend-build
# Build React app -> /app/build/

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build  
# Build .NET app -> /app/publish/

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
# Copy React build to wwwroot/
# Copy .NET app
# Configure to serve static files + API
```

### Static File Serving (Test/Production only):
- **Development**: NOT used - React dev server handles all frontend serving with hot reload
- **Test/Production**: ASP.NET Core configured with `app.UseStaticFiles()` and `app.UseSpaStaticFiles()`
- React Router handled via fallback to `index.html`
- API routes prefixed (e.g., `/api/*`, `/WeatherForecast`)
- Frontend routes handled by React Router

### Development vs Production Architecture:
```
Development:           Production/Test:
┌─────────────┐       ┌─────────────────────┐
│React Dev    │       │Single Container     │
│Server :3000 │ CORS  │┌─────────┬─────────┐│
│(Hot Reload) │◄─────►││React    │ASP.NET  ││
└─────────────┘       ││Static   │Core API ││
                      ││Files    │:80      ││
┌─────────────┐       │└─────────┴─────────┘│
│ASP.NET Core │       │Azure Web App        │
│API :44390   │       └─────────────────────┘
└─────────────┘
```

---

## 5. 🗃️ Database Versioning

- Database migrations are managed via **EF Core**.
- Migrations are stored in `backend/src/Anela.Heblo.Persistence/Migrations/`.
- Single `ApplicationDbContext` in `Anela.Heblo.Persistence` (initially).
- Migration naming convention: `AddXyzTable`, `AddIndexToProductName`, etc.
- **Migrations apply automatically on Production startup; Development, Test, and Staging remain manual.**
- Future evolution: Can move to module-specific DbContexts as needed.

### Applied Migrations

| Migration | Date | Description | Apply command |
|---|---|---|---|
| `20260430170922_AddLeafletStore` | 2026-04-30 | Adds `LeafletDocuments` and `LeafletChunks` tables with `vector(1536)` embedding column and HNSW index (m=16, ef_construction=64). Requires pgvector extension (already enabled). | `dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API` |

---

## 6. 🔌 Dependency Injection & Module Registration

### Module Registration Pattern

Each feature module in `Anela.Heblo.Application` must provide a registration extension method:

```csharp
// In Application/features/orders/OrdersModule.cs
public static class OrdersModule
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services)
    {
        // Register module services
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        
        // Register use cases
        services.AddScoped<CreateOrderUseCase>();
        services.AddScoped<GetOrderUseCase>();
        
        return services;
    }
}
```

### API Composition (Program.cs)

The API project aggregates all modules:

```csharp
// Feature modules
services
    .AddCatalogModule()
    .AddInvoicesModule()
    .AddManufactureModule()
    .AddPurchaseModule()
    .AddTransportModule()
    .AddApplicationServices() // All feature modules
    .AddPersistence(Configuration.GetConnectionString("DefaultConnection")); // Database

// FastEndpoints
services.AddFastEndpoints();
```

### Service Lifetimes
- **Scoped**: Default for services, repositories, use cases
- **Transient**: For lightweight, stateless services
- **Singleton**: For configuration, caching, and cross-cutting services

---

## 7. 🔧 Technology Stack & Tools

### Core Technologies
| Technology | Purpose | Version |
|------------|---------|---------|
| **.NET 8** | Backend runtime | 8.0+ |
| **FastEndpoints** | HTTP API framework | Latest |
| **EF Core** | ORM and migrations | 8.0+ |
| **PostgreSQL** | Database | 15+ |
| **React** | Frontend framework | 18+ |
| **Docker** | Containerization | Latest |

### Recommended Libraries
| Library | Purpose | Usage |
|---------|---------|--------|
| **AutoMapper** | DTO mapping | Optional for complex mappings |
| **MediatR** | CQRS pattern | Optional for command/query separation |
| **FluentValidation** | Input validation | Integrated with FastEndpoints |
| **Polly** | Resilience patterns | For external API calls |
| **Serilog** | Structured logging | Enhanced logging capabilities |
| **Hangfire** | Background jobs | Scheduled tasks and processing |

### Development Tools
- **EF Core CLI**: Database migrations management
- **Docker Desktop**: Local container development
- **Azure CLI**: Cloud resource management
- **GitHub Actions**: CI/CD automation

---

## 8. 🚀 Azure Web App for Containers Deployment

> **Note**: Azure Web App deployment configuration is documented in [`environments.md`](./environments.md).

---

## 9. 🌿 Branching Strategy

- **Main branch** is the releasable production code.
- **Feature branches**: `feature/*`
- **Bugfix branches**: `fix/*`
- Merge to `main`:
  - Must pass CI pipeline
  - Uses **merge commit** (squash in PR, merge = merge commit)
- **AI PR reviewer** is used to validate PRs (solo developer setup)

---

## 10. 🔖 Project Versioning

- Follows **Semantic Versioning**: `MAJOR.MINOR.PATCH`
- Version bump is done automatically based on **conventional commit messages**
  - e.g. `feat:`, `fix:`, `chore:`, etc.
- Version is included in:
  - Docker image tags (both versioned and `latest`)
  - `AssemblyInfo.cs`
  - `package.json`
- Tagging `vX.Y.Z` on `main` triggers production release

### Container Tag Strategy:
- **Production deployments**: Use **specific version tags** (e.g., `remiiik/heblo:v1.2.3`)
  - Ensures reliable, reproducible deployments
  - Prevents issues with stale `latest` tags
  - Enables easy rollback to previous versions
- **Development/Test**: May use `latest` for convenience
- **Fallback mechanism**: Deployment workflow falls back to versioned tag if build output is unavailable

---

## 11. 🧠 AI Review Agent

- Since the project is developed by a single developer:
  - PR review is handled by an **AI agent**.
  - PRs must include CI success and optionally auto-reviewed comments.

---

## 12. 🔐 GitHub Secrets Configuration

### Required Secrets for CI/CD Pipeline

The following secrets must be configured in GitHub repository settings (Settings → Secrets and variables → Actions):

#### 🐳 Docker Hub Authentication
- **`DOCKER_USERNAME`**: Your Docker Hub username
- **`DOCKER_PASSWORD`**: Docker Hub access token (not password!)
  - Create at: Docker Hub → Account Settings → Security → New Access Token
  - Recommended permissions: Read, Write, Delete

#### ☁️ Azure Service Principal Credentials
- **`AZURE_CREDENTIALS_TEST`**: Service principal for test environment
- **`AZURE_CREDENTIALS_PROD`**: Service principal for production environment
  
  Create with Azure CLI:
  ```bash
  # Test environment
  az ad sp create-for-rbac \
    --name "github-actions-anela-heblo-test" \
    --role contributor \
    --scopes /subscriptions/{subscription-id}/resourceGroups/rg-anela-heblo-test \
    --sdk-auth

  # Production environment  
  az ad sp create-for-rbac \
    --name "github-actions-anela-heblo-prod" \
    --role contributor \
    --scopes /subscriptions/{subscription-id}/resourceGroups/rg-anela-heblo-prod \
    --sdk-auth
  ```
  
  The output JSON should be stored as the secret value.

#### 🔑 Microsoft Entra ID (Azure AD) Authentication
- **`AZURE_CLIENT_ID_PROD`**: Azure AD application client ID for production
- **`AZURE_AUTHORITY_PROD`**: Azure AD authority URL
  - Format: `https://login.microsoftonline.com/{tenant-id}`
  - Get from: Azure Portal → App registrations → Your app → Overview

#### 🤖 Automatically Provided
- **`GITHUB_TOKEN`**: Automatically provided by GitHub Actions
  - Has permissions to create releases, push tags, etc.
  - Requires `contents: write` permission in workflow file

### CI/CD Pipeline Configuration

#### Workflow Permissions
Production deployment workflow requires:
```yaml
permissions:
  contents: write  # For creating git tags
  packages: write  # For pushing Docker images
```

#### Environment-Specific App Settings

> **Note**: Environment-specific settings are detailed in [`environments.md`](./environments.md).

---

## Database connection health alerts

### Action Group

The alert receiver is the `ag-heblo-prod-default` Azure Monitor Action Group in resource group `rgHeblo`. Single email receiver: `ondra@anela.cz`.

Verify or create:

```bash
az monitor action-group show -g rgHeblo -n ag-heblo-prod-default || \
az monitor action-group create \
  -g rgHeblo -n ag-heblo-prod-default \
  --short-name HebloProd \
  --email-receivers name=primary email=ondra@anela.cz useCommonAlertSchema=true
```

### Alert rule — Npgsql exception spike

Fires when Npgsql-originated exceptions exceed 10/hour for 2 consecutive hours.

```bash
az monitor scheduled-query create \
  -g rgHeblo -n alert-heblo-db-npgsql-spike \
  --scopes /subscriptions/<sub>/resourceGroups/rgHeblo/providers/microsoft.insights/components/aiHeblo \
  --description "Npgsql / SocketException spike originating in the database driver" \
  --severity 2 \
  --evaluation-frequency 1h \
  --window-size 1h \
  --condition "count 'exceptions | where type startswith \"Npgsql.\" or (type == \"System.Net.Sockets.SocketException\" and outerAssembly startswith \"Npgsql\") | count' > 10" \
  --auto-mitigate true
```

Number of evaluation periods required to fire: 2.

### Alert rule — pool exhaustion

Fires when pool exhaustion waits exceed 5 in any 5-minute window.

```bash
az monitor scheduled-query create \
  -g rgHeblo -n alert-heblo-db-pool-exhaustion \
  --scopes /subscriptions/<sub>/resourceGroups/rgHeblo/providers/microsoft.insights/components/aiHeblo \
  --description "Npgsql connection pool exhaustion waits > 5/5m" \
  --severity 2 \
  --evaluation-frequency 5m \
  --window-size 5m \
  --condition "count 'customMetrics | where name == \"npgsql.pool.exhaustion_wait_seconds\" | count' > 5" \
  --auto-mitigate true
```

Replace `<sub>` with the production Azure subscription ID (`az account show --query id -o tsv`).

See `docs/integrations/db-connection-health-kql.md` for the underlying KQL.

---

## ✅ Summary

This document defines the project's infrastructure practices and expectations:

- **Monorepo layout** with **Vertical Slice Architecture** and FastEndpoints
- **Backend architecture**: Modular monolith with feature-based organization
  - `Anela.Heblo.API` - Host/Composition with FastEndpoints
  - `Anela.Heblo.Application` - Feature modules (vertical slices)
  - `Anela.Heblo.Persistence` - Shared database infrastructure with generic repository
  - `Anela.Heblo.Domain` - Shared domain entities and constants
- **Development**: Separate servers (React dev server + ASP.NET Core) for **hot reload**
- **Test/Production**: Single Docker container on **Azure Web App for Containers**
- **Environment separation**: Development (separate for hot reload) vs Test/Production (single container)
- **Consistent versioning** and automated release triggers
- **GitHub Actions CI/CD** with Docker Hub registry and Azure deployment
- **Mock authentication** for development and test environments
- **Manual database management** with EF Core migrations
- **AI-assisted code reviews** for solo developer workflow

