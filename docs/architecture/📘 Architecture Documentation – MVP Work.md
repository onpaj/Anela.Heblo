📘 Architecture Documentation – MVP Workspace App for Cosmetics Company

🎯 Purpose

A web-based workspace application used by a small manufacturing and purchase team at a cosmetics company. It integrates information from ERP (ABRA Flexi) and e-commerce (Shoptet) platforms and provides a unified interface for production planning, stock management, transport tracking, and automation of administrative processes.

⸻

🏗️ Technical Summary

Stack Overview

Layer	Tech Choices
Frontend	React PWA, i18next, MSAL (MS Entra ID)
Backend API	ASP.NET Core (.NET 8), FastEndpoints, REST
Auth	MS Entra ID (OIDC), Claims-based roles
Database	PostgreSQL (EF Core Migrations)
Background Tasks	Hangfire
Integration	ABRA (custom API client), Shoptet (Playwright-based)
Observability	Application Insights
Deployment	Docker, GitHub Environments, .NET Environments (on-prem now, Azure later)


⸻

🧱 Architectural Pattern
	•	Vertical Slice Architecture with FastEndpoints
	•	Modular monolith with feature-based organization
	•	Projects: Anela.Heblo.API (host), Anela.Heblo.App (features), Anela.Heblo.Persistence (DB), Anela.Heblo.Xcc (cross-cutting)
	•	Modules communicate via contracts interfaces, no direct dependencies
	•	Generic repository pattern with interface in Xcc

⸻

🚀 MVP Functional Modules

📚 Catalog Module

Unifies product and material data across systems. Provides:
	•	Stock snapshot & history
	•	Purchase, sales, and consumption history
	•	Price & description metadata
Sources:
	•	Products/goods → Shoptet (via Playwright)
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
	•	Periodically scrapes Shoptet for invoices (Playwright)
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

Each feature module in `Anela.Heblo.App/features/` follows this structure:

```
features/
├── catalog/
│   ├── contracts/
│   │   ├── ICatalogService.cs
│   │   ├── ProductDto.cs
│   │   └── MaterialDto.cs
│   ├── application/
│   │   ├── CatalogService.cs
│   │   └── SyncCatalogUseCase.cs
│   ├── domain/
│   │   ├── Product.cs
│   │   ├── Material.cs
│   │   └── StockSnapshot.cs
│   ├── infrastructure/
│   │   ├── CatalogRepository.cs
│   │   └── ExternalApiClients/
│   └── CatalogModule.cs
├── invoices/
├── manufacture/
├── purchase/
└── transport/
```

**Key Principles:**
- Each module is self-contained with all layers
- Communication between modules only via contracts
- Repository implementations use generic repository from Xcc
- FastEndpoints in API project delegate to use cases in App

⸻

Let me know if you'd like:
	•	PlantUML/Mermaid diagram version
	•	Backend folder structure recommendation
	•	i18n string loader example
	•	Deployment YAMLs (Docker Compose or Azure App Service config) ￼