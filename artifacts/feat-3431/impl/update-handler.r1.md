# Implementation: update-handler

## What was implemented

Removed the `IHostEnvironment` dependency from `GetConfigurationHandler` and replaced the `_environment.EnvironmentName` read with a direct `IConfiguration["ASPNETCORE_ENVIRONMENT"]` read. A null-coalescing fallback to `ConfigurationConstants.DEFAULT_ENVIRONMENT` (previously absent) is now applied explicitly, preserving the same runtime behaviour while eliminating the hosting-layer dependency.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — removed `using Microsoft.Extensions.Hosting`, removed `IHostEnvironment _environment` field and constructor parameter, replaced `_environment.EnvironmentName` with `_configuration["ASPNETCORE_ENVIRONMENT"] ?? ConfigurationConstants.DEFAULT_ENVIRONMENT`

## Tests

N/A (no test changes in this task)

## How to verify

dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj

## Notes

The previous implementation depended on `IHostEnvironment` (an infrastructure/hosting concern) purely to read the environment name, which is already exposed via `IConfiguration["ASPNETCORE_ENVIRONMENT"]`. Removing it keeps the Application layer free from hosting abstractions and makes unit testing simpler — only `IConfiguration` and `ILogger` mocks are needed.

## PR Summary

Removed `IHostEnvironment` dependency from `GetConfigurationHandler`. The environment name is now read directly from `IConfiguration["ASPNETCORE_ENVIRONMENT"]` with a fallback to `ConfigurationConstants.DEFAULT_ENVIRONMENT`, matching the existing runtime behaviour while eliminating the hosting-layer coupling.

## Status

DONE
