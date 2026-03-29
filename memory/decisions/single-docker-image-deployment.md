# Decision: Single Docker Image Deployment

**Decision:** Backend (.NET) and frontend (React) are packaged into a single Docker image and deployed to Azure Web App for Containers.

**Why:** Simplifies deployment — no separate frontend CDN or static hosting needed. The .NET API serves the React SPA directly. Single image means single deployment unit.

**How to apply:**
- React build output is embedded into the .NET API container at build time
- Azure Web App for Containers hosts the single image
- All Docker images pushed to Docker Hub
- See `docs/architecture/application_infrastructure.md` for CI/CD details
