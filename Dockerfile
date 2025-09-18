# Multi-stage Dockerfile for Anela Heblo
# Builds React frontend and ASP.NET Core backend into single container

# Stage 1: Build React Frontend
FROM node:18-alpine AS frontend-build
WORKDIR /app/frontend

# Accept build arguments for React environment variables
ARG REACT_APP_API_URL=http://localhost:8080
ARG REACT_APP_USE_MOCK_AUTH=true
ARG REACT_APP_AZURE_CLIENT_ID=
ARG REACT_APP_AZURE_AUTHORITY=
ARG REACT_APP_AZURE_BACKEND_CLIENT_ID=
ARG REACT_APP_AZURE_TENANT_ID=

# Set environment variables for React build
ENV REACT_APP_API_URL=$REACT_APP_API_URL
ENV REACT_APP_USE_MOCK_AUTH=$REACT_APP_USE_MOCK_AUTH
ENV REACT_APP_AZURE_CLIENT_ID=$REACT_APP_AZURE_CLIENT_ID
ENV REACT_APP_AZURE_AUTHORITY=$REACT_APP_AZURE_AUTHORITY
ENV REACT_APP_AZURE_BACKEND_CLIENT_ID=$REACT_APP_AZURE_BACKEND_CLIENT_ID
ENV REACT_APP_AZURE_TENANT_ID=$REACT_APP_AZURE_TENANT_ID

# Copy package files and install all dependencies (needed for build)
COPY frontend/package*.json ./
RUN npm install --legacy-peer-deps

# Copy frontend source and build
COPY frontend/ ./
RUN npm run build

# Stage 2: Build ASP.NET Core Backend
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
WORKDIR /src

# Copy all backend files at once
COPY Anela.Heblo.sln ./
COPY backend/ ./backend/

# Clear NuGet cache and restore with rebuild
RUN dotnet nuget locals all --clear
RUN dotnet restore Anela.Heblo.sln \
    --verbosity normal \
    --force \
    --no-cache

# Build and publish (with restore to be safe)
RUN dotnet publish backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj \
    -c Release \
    -o /app/publish \
    --force

# Install Playwright and browsers in the build stage
RUN dotnet tool install --global Microsoft.Playwright.CLI \
    && export PATH="$PATH:/root/.dotnet/tools" \
    && playwright install chromium \
    && playwright install-deps

# Stage 3: Runtime - ASP.NET Core serving React + API
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install system dependencies for Playwright
RUN apt-get update && apt-get install -y \
    tzdata \
    curl \
    wget \
    libnss3 \
    libatk-bridge2.0-0 \
    libdrm2 \
    libxcomposite1 \
    libxdamage1 \
    libxrandr2 \
    libgbm1 \
    libgtk-3-0 \
    libglib2.0-0 \
    libasound2 \
    && ln -sf /usr/share/zoneinfo/Europe/Prague /etc/localtime \
    && echo "Europe/Prague" > /etc/timezone \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

# Copy published backend
COPY --from=backend-build /app/publish ./

# Copy built React frontend to wwwroot (ASP.NET Core serves static files)
COPY --from=frontend-build /app/frontend/build ./wwwroot

# Copy Playwright browsers from build stage
COPY --from=backend-build /root/.cache/ms-playwright /opt/ms-playwright

# Configure ASP.NET Core
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV TZ=Europe/Prague
ENV PLAYWRIGHT_BROWSERS_PATH=/opt/ms-playwright

# Create non-root user for security
RUN adduser --disabled-password --gecos "" --uid 1001 appuser
RUN chown -R appuser:appuser /app

# Make Playwright browsers accessible to appuser
RUN chown -R appuser:appuser /opt/ms-playwright

USER appuser

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Expose port
EXPOSE 8080

# Start the application
ENTRYPOINT ["dotnet", "Anela.Heblo.API.dll"]