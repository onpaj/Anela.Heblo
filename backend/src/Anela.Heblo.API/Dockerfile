FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj", "backend/src/Anela.Heblo.API/"]
RUN dotnet restore "backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj"
COPY . .
WORKDIR "/src/backend/src/Anela.Heblo.API"
RUN dotnet build "./Anela.Heblo.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Anela.Heblo.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Anela.Heblo.API.dll"]
