📘 Architecture Documentation – MVP Workspace App for Cosmetics Company

🎯 Purpose

A web-based workspace application used by a small manufacturing and purchase team at a cosmetics company. It integrates information from ERP (ABRA Flexi) and e-commerce (Shoptet) platforms and provides a unified interface for production planning, stock management, transport tracking, and automation of administrative processes.

⸻

🏗️ Technical Summary

Stack Overview

Layer	Tech Choices
Frontend	React PWA, i18next, MSAL (MS Entra ID)
Backend API	ASP.NET Core (.NET 8), MediatR + Controllers, REST
Auth	MS Entra ID (OIDC), Claims-based roles
Database	PostgreSQL (EF Core Migrations)
Background Tasks	Hangfire
Integration	ABRA (custom API client), Shoptet (REST API via `Anela.Heblo.Adapters.ShoptetApi`)
Observability	Application Insights
Deployment	Docker, GitHub Environments, .NET Environments (on-prem now, Azure later)


⸻

🧱 Architectural Pattern
	•	Vertical Slice Architecture with MediatR + Controllers
	•	Modular monolith with feature-based organization
	•	Projects: Anela.Heblo.API (host), Anela.Heblo.Application (features), Anela.Heblo.Persistence (DB), Anela.Heblo.Infrastructure (cross-cutting)
	•	Features use MediatR handlers as Application Services
	•	Generic repository pattern with concrete implementation in Persistence

⸻

🚀 MVP Functional Modules

📚 Catalog Module

Unifies product and material data across systems. Provides:
	•	Stock snapshot & history
	•	Purchase, sales, and consumption history
	•	Price & description metadata
Sources:
	•	Products/goods → Shoptet (via REST API)
	•	Materials → ABRA (via custom client)
	•	Descriptions/local data → App DB

⸻

🏭 Manufacture Module
	•	2-step production: Materials → Semi-products → Products
	•	Evaluates batch feasibility from stock & BOM
	•	Syncable via Hangfire or UI
	•	Allows partial production logic

⸻

🛒 Purchase Module
	•	Detects material shortages
	•	Displays supplier and pricing history
	•	Allows planned purchases, tracked internally
	•	Manual and automated workflows supported

⸻

🚚 Transport Overview
	•	Tracks box-level packaging (EANs) of finished goods
	•	Internal shipments registered and confirmed
	•	Triggers stock updates in Shoptet upon receipt
	•	All flows visible in Admin Dashboard

⸻

🧾 Invoice Automation
	•	Periodically fetches Shoptet invoices via REST API
	•	Pushes data into ABRA Flexi via custom API
	•	Errors shown in Admin dashboard
	•	Manual re-trigger supported

⸻

🧑‍💼 Auth & Role Access
	•	MS Entra ID (OIDC)
	•	Claims-based access, no DB roles
	•	Role checks in both .NET middleware and React (MSAL)

⸻

🔁 Background Processing (Hangfire)

Job	Description
Stock Sync	Refresh unified catalog every 10 mins or on-demand
Invoice Sync	Pull new Shoptet invoices and push to ABRA
Transport Sync	Confirm EANs and update Shoptet
Batch Planning	Periodic manufacturing evaluation


⸻

📊 Observability & Operations
	•	Application Insights for logs, errors, traces
	•	Admin Dashboard displays:
	•	Last run status for jobs
	•	Errors for manual re-run
	•	Manual sync buttons for admin users

⸻

🌐 Localization
	•	i18next used in React app
	•	Initial language: Czech
	•	All UI strings localized
	•	Future language expansion supported

⸻

🚢 Deployment Strategy

Environment	Current	Future
Runtime	Docker (on-prem NAS)	Docker (Azure App Service / Container App)
Secrets	GitHub Environments	Azure KeyVault / GitHub
Config	.NET environments	.NET environments


⸻

## 📁 Module Structure (Vertical Slices)

Each feature module in `Anela.Heblo.Application/Features/` follows this structure:

```
Features/
├── Catalog/
│   ├── Contracts/
│   │   ├── GetCatalogRequest.cs
│   │   ├── GetCatalogResponse.cs
│   │   └── CatalogDto.cs
│   ├── Application/
│   │   ├── GetCatalogHandler.cs (MediatR Handler)
│   │   └── SyncCatalogHandler.cs
│   ├── Domain/
│   │   ├── Product.cs
│   │   ├── Material.cs
│   │   └── StockSnapshot.cs
│   └── Infrastructure/
│       ├── CatalogRepository.cs
│       └── ExternalApiClients/
├── Invoices/
├── Manufacture/
├── Purchase/
└── Transport/
```

**Key Principles:**
- Each feature is self-contained with all layers
- Controllers use MediatR to send requests to handlers
- Handlers are the Application Services containing business logic
- API endpoints follow /api/{controller} pattern
- Repository implementations use generic repository from Persistence

⸻

Let me know if you'd like:
	•	PlantUML/Mermaid diagram version
	•	Backend folder structure recommendation
	•	i18n string loader example
	•	Deployment YAMLs (Docker Compose or Azure App Service config) ￼