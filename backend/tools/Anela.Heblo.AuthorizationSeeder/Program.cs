using System.Reflection;
using System.Text.Json;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

try
{
    return await Run(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FATAL: {ex.Message}");
    return 1;
}

static async Task<int> Run(string[] args)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("Usage: dotnet run -- <Staging|Production> [--reset-group <Name>]");
        return 2;
    }

    var envArg = args[0];
    string? resetGroupName = null;
    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--reset-group" && i + 1 < args.Length)
        {
            resetGroupName = args[++i];
        }
        else
        {
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 2;
        }
    }

    var env = NormalizeEnv(envArg);
    if (env is null)
    {
        Console.Error.WriteLine($"Environment must be 'Staging' or 'Production', got '{envArg}'.");
        return 2;
    }

    // 1. Configuration: appsettings + KV
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{env}.json", optional: false)
        .Build();

    var keyVaultUri = configuration["KeyVault:Uri"]
        ?? throw new InvalidOperationException($"KeyVault:Uri missing in appsettings.{env}.json");

    var withKv = new ConfigurationBuilder()
        .AddConfiguration(configuration)
        .AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential())
        .Build();

    var connectionString = withKv.GetConnectionString(env)
        ?? throw new InvalidOperationException(
            $"ConnectionStrings:{env} not found via Key Vault at {keyVaultUri}.");

    // 2. Load embedded JSON manifest
    var manifest = LoadEmbeddedManifest();

    // 3. PROD safety gates
    if (env == "Production")
    {
        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine("  WARNING: TARGETING PRODUCTION DATABASE");
        Console.WriteLine($"  Key Vault: {keyVaultUri}");
        Console.WriteLine($"  Action:    {(resetGroupName is null ? "Bootstrap (insert-if-missing)" : $"RESET GROUP '{resetGroupName}'")}");
        Console.WriteLine("============================================================");
        Console.Write("Type PRODUCTION to continue: ");
        var input = Console.ReadLine();
        if (input != "PRODUCTION")
        {
            Console.Error.WriteLine("Confirmation failed; aborting.");
            return 3;
        }

        if (resetGroupName is not null)
        {
            Console.Write($"Confirm reset by typing the group name '{resetGroupName}': ");
            var confirm = Console.ReadLine();
            if (confirm != resetGroupName)
            {
                Console.Error.WriteLine("Group name confirmation failed; aborting.");
                return 3;
            }
        }
    }

    // 4. Build DbContext directly (no DI host needed)
    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    dataSourceBuilder.UseVector();
    await using var dataSource = dataSourceBuilder.Build();

    var dbOpts = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(dataSource)
        .Options;
    await using var db = new ApplicationDbContext(dbOpts);

    // 5. Execute
    if (resetGroupName is not null)
    {
        var seed = manifest.SeedGroups.FirstOrDefault(
            g => string.Equals(g.Name, resetGroupName, StringComparison.Ordinal));
        if (seed is null)
        {
            Console.Error.WriteLine($"Group '{resetGroupName}' is not defined in access-matrix.json seedGroups.");
            Console.Error.WriteLine($"Known groups: {string.Join(", ", manifest.SeedGroups.Select(g => g.Name))}");
            return 4;
        }

        await JsonGroupSeeder.ResetGroupAsync(db, seed, CancellationToken.None);
        Console.WriteLine($"OK — reset group '{resetGroupName}' against {env}.");
    }
    else
    {
        await JsonGroupSeeder.AddMissingGroupsAsync(db, manifest.SeedGroups, CancellationToken.None);
        Console.WriteLine($"OK — bootstrap complete against {env} ({manifest.SeedGroups.Count} groups in JSON; insert-if-missing).");
    }

    return 0;
}

static string? NormalizeEnv(string raw) =>
    raw.Trim().ToLowerInvariant() switch
    {
        "staging" or "stg" => "Staging",
        "production" or "prod" => "Production",
        _ => null,
    };

static AccessMatrixManifest LoadEmbeddedManifest()
{
    var asm = Assembly.GetExecutingAssembly();
    var resourceName = asm.GetManifestResourceNames()
        .FirstOrDefault(n => n.EndsWith("access-matrix.json", StringComparison.Ordinal))
        ?? throw new InvalidOperationException("Embedded resource access-matrix.json not found.");

    using var stream = asm.GetManifestResourceStream(resourceName)!;
    return JsonSerializer.Deserialize<AccessMatrixManifest>(stream, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    }) ?? throw new InvalidOperationException("Failed to deserialize embedded access-matrix.json.");
}
