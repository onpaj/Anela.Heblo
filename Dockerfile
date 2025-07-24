# Multi-stage Dockerfile for Anela Heblo
# Builds React frontend and ASP.NET Core backend into single container

# Stage 1: Build React Frontend
FROM node:18-alpine AS frontend-build
WORKDIR /app/frontend

# Copy package files and install all dependencies (needed for build)
COPY frontend/package*.json ./
RUN npm install --legacy-peer-deps

# Copy frontend source and build
COPY frontend/ ./
RUN npm run build

# Stage 2: Build ASP.NET Core Backend
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
WORKDIR /src

# Copy solution and project files
COPY Anela.Heblo.sln ./
COPY backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj ./backend/src/Anela.Heblo.API/
COPY backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj ./backend/src/Anela.Heblo.Application/
COPY backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj ./backend/src/Anela.Heblo.Domain/
COPY backend/src/Anela.Heblo.Infrastructure/Anela.Heblo.Infrastructure.csproj ./backend/src/Anela.Heblo.Infrastructure/

# Restore dependencies
RUN dotnet restore backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj

# Copy source code and build
COPY backend/ ./backend/
RUN dotnet publish backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Stage 3: Runtime - ASP.NET Core serving React + API
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy published backend
COPY --from=backend-build /app/publish ./

# Copy built React frontend to wwwroot
COPY --from=frontend-build /app/frontend/build ./wwwroot

# Configure ASP.NET Core
ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=Production

# Create non-root user for security
RUN adduser --disabled-password --gecos "" --uid 1001 appuser
RUN chown -R appuser:appuser /app
USER appuser

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:80/health || exit 1

# Expose port
EXPOSE 80

# Start the application
ENTRYPOINT ["dotnet", "Anela.Heblo.API.dll"]