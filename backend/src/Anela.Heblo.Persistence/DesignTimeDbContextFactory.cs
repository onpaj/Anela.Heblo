using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Reflection;

namespace Anela.Heblo.Persistence;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        // Build configuration
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var builder = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../Anela.Heblo.API"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables();

        // Add user secrets for Development, Test, Staging, and Production environments (matches API project UserSecretsId)
        if (environment == "Development" || environment == "Test" || environment == "Staging" || environment == "Production")
        {
            builder.AddUserSecrets("f4e6382a-aefd-47ef-9cd7-7e12daac7e45");
        }

        IConfigurationRoot configuration = builder.Build();

        // Get connection string - try environment name first (from User Secrets), then fall back to DefaultConnection/Default
        var connectionString = configuration.GetConnectionString(environment)
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? configuration.GetConnectionString("Default");

        if (!string.IsNullOrEmpty(connectionString))
        {
            optionsBuilder.UseNpgsql(connectionString);
        }
        else
        {
            throw new InvalidOperationException($"No connection string found for environment '{environment}'. Tried: '{environment}', 'DefaultConnection', 'Default'. Please check your configuration.");
        }

        return new ApplicationDbContext(optionsBuilder.Options, TimeProvider.System);
    }
}