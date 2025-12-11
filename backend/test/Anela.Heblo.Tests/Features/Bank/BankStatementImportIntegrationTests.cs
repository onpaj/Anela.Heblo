using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using Anela.Heblo.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class BankStatementImportIntegrationTests : IClassFixture<BankStatementImportTestFactory>
{
    private readonly BankStatementImportTestFactory _factory;
    private readonly HttpClient _client;

    public BankStatementImportIntegrationTests(BankStatementImportTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ImportBankStatement_WithValidCZKAccount_ReturnsSuccess()
    {
        // Arrange
        var request = new ImportBankStatementRequest("CZK", DateTime.Today);

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Setup mock expectations
        var statement = new BankStatementHeader
        {
            StatementId = "T12345",
            Date = DateTime.Today,
            Account = "123456789"
        };

        var aboData = new BankStatementData
        {
            StatementId = "T12345",
            Data = "Header\nTransaction1\nTransaction2",
            ItemCount = 2
        };

        _factory.MockBankClient.Setup(x => x.GetStatementsAsync("123456789", request.StatementDate))
            .ReturnsAsync(new[] { statement });
        _factory.MockBankClient.Setup(x => x.GetStatementAsync("T12345"))
            .ReturnsAsync(aboData);
        _factory.MockImportService.Setup(x => x.ImportStatementAsync(It.IsAny<int>(), aboData.Data))
            .ReturnsAsync(Result<bool>.Success(true));

        // Act
        var response = await _client.PostAsync("/api/bank/import-statement", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ImportBankStatementResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Single(result.Statements);
        Assert.Equal("T12345", result.Statements[0].TransferId);
        Assert.Equal("OK", result.Statements[0].ImportResult);
        Assert.Equal("CZK", result.Statements[0].Currency);
        Assert.Equal(2, result.Statements[0].ItemCount);
    }

    [Fact]
    public async Task ImportBankStatement_WithValidEURAccount_ReturnsSuccess()
    {
        // Arrange
        var request = new ImportBankStatementRequest("EUR", DateTime.Today);

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Setup mock expectations
        var statement = new BankStatementHeader
        {
            StatementId = "T67890",
            Date = DateTime.Today,
            Account = "987654321"
        };

        var aboData = new BankStatementData
        {
            StatementId = "T67890",
            Data = "Header\nEURTransaction1",
            ItemCount = 1
        };

        _factory.MockBankClient.Setup(x => x.GetStatementsAsync("987654321", request.StatementDate))
            .ReturnsAsync(new[] { statement });
        _factory.MockBankClient.Setup(x => x.GetStatementAsync("T67890"))
            .ReturnsAsync(aboData);
        _factory.MockImportService.Setup(x => x.ImportStatementAsync(It.IsAny<int>(), aboData.Data))
            .ReturnsAsync(Result<bool>.Success(true));

        // Act
        var response = await _client.PostAsync("/api/bank/import-statement", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ImportBankStatementResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Single(result.Statements);
        Assert.Equal("EUR", result.Statements.First().Currency);
    }

    [Fact]
    public async Task ImportBankStatement_WithInvalidAccount_ReturnsBadRequest()
    {
        // Arrange
        var request = new ImportBankStatementRequest("INVALID", DateTime.Today);

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/bank-statements/import", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImportBankStatement_WithMultipleStatements_ProcessesAll()
    {
        // Arrange
        var request = new ImportBankStatementRequest("CZK", DateTime.Today);

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var statement1 = new BankStatementHeader { StatementId = "T001", Date = DateTime.Today, Account = "123456789" };
        var statement2 = new BankStatementHeader { StatementId = "T002", Date = DateTime.Today, Account = "123456789" };

        var aboData1 = new BankStatementData { StatementId = "T001", Data = "Header\nTx1", ItemCount = 1 };
        var aboData2 = new BankStatementData { StatementId = "T002", Data = "Header\nTx2", ItemCount = 1 };

        _factory.MockBankClient.Setup(x => x.GetStatementsAsync("123456789", request.StatementDate))
            .ReturnsAsync(new[] { statement1, statement2 });
        _factory.MockBankClient.Setup(x => x.GetStatementAsync("T001"))
            .ReturnsAsync(aboData1);
        _factory.MockBankClient.Setup(x => x.GetStatementAsync("T002"))
            .ReturnsAsync(aboData2);
        _factory.MockImportService.Setup(x => x.ImportStatementAsync(It.IsAny<int>(), aboData1.Data))
            .ReturnsAsync(Result<bool>.Success(true));
        _factory.MockImportService.Setup(x => x.ImportStatementAsync(It.IsAny<int>(), aboData2.Data))
            .ReturnsAsync(Result<bool>.Success(true));

        // Act
        var response = await _client.PostAsync("/api/bank/import-statement", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ImportBankStatementResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal(2, result.Statements.Count);
        Assert.Contains(result.Statements, s => s.TransferId == "T001");
        Assert.Contains(result.Statements, s => s.TransferId == "T002");
    }

    [Fact]
    public async Task ImportBankStatement_WithPartialFailure_ReturnsPartialResults()
    {
        // Arrange
        var request = new ImportBankStatementRequest("CZK", DateTime.Today);

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var successStatement = new BankStatementHeader { StatementId = "T-SUCCESS", Date = DateTime.Today, Account = "123456789" };
        var failStatement = new BankStatementHeader { StatementId = "T-FAIL", Date = DateTime.Today, Account = "123456789" };

        var successAbo = new BankStatementData { StatementId = "T-SUCCESS", Data = "Header\nSuccess", ItemCount = 1 };

        _factory.MockBankClient.Setup(x => x.GetStatementsAsync("123456789", request.StatementDate))
            .ReturnsAsync(new[] { successStatement, failStatement });
        _factory.MockBankClient.Setup(x => x.GetStatementAsync("T-SUCCESS"))
            .ReturnsAsync(successAbo);
        _factory.MockBankClient.Setup(x => x.GetStatementAsync("T-FAIL"))
            .ThrowsAsync(new HttpRequestException("API error"));
        _factory.MockImportService.Setup(x => x.ImportStatementAsync(It.IsAny<int>(), successAbo.Data))
            .ReturnsAsync(Result<bool>.Success(true));

        // Act
        var response = await _client.PostAsync("/api/bank/import-statement", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ImportBankStatementResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal(2, result.Statements.Count);

        var successResult = result.Statements.First(s => s.TransferId == "T-SUCCESS");
        var failResult = result.Statements.First(s => s.TransferId == "T-FAIL");

        Assert.Equal("OK", successResult.ImportResult);
        Assert.Contains("PROCESSING_ERROR", failResult.ImportResult);
        Assert.Contains("API error", failResult.ImportResult);
    }

    [Fact]
    public async Task ImportBankStatement_WithInvalidRequestBody_ReturnsBadRequest()
    {
        // Arrange
        var invalidJson = "{invalid json}";
        var content = new StringContent(invalidJson, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/bank/import-statement", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImportBankStatement_DatabasePersistence_SavesImportRecord()
    {
        // Arrange
        var request = new ImportBankStatementRequest("CZK", DateTime.Today);

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var statement = new BankStatementHeader
        {
            StatementId = "T-PERSIST",
            Date = DateTime.Today,
            Account = "123456789"
        };

        var aboData = new BankStatementData
        {
            StatementId = "T-PERSIST",
            Data = "Header\nPersistTest",
            ItemCount = 1
        };

        _factory.MockBankClient.Setup(x => x.GetStatementsAsync("123456789", request.StatementDate))
            .ReturnsAsync(new[] { statement });
        _factory.MockBankClient.Setup(x => x.GetStatementAsync("T-PERSIST"))
            .ReturnsAsync(aboData);
        _factory.MockImportService.Setup(x => x.ImportStatementAsync(It.IsAny<int>(), aboData.Data))
            .ReturnsAsync(Result<bool>.Success(true));

        // Act
        await _client.PostAsync("/api/bank/import-statement", content);

        // Assert - Verify record was saved to database
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Persistence.ApplicationDbContext>();
            var savedImport = context.BankStatements
                .FirstOrDefault(bs => bs.TransferId == "T-PERSIST");

            Assert.NotNull(savedImport);
            Assert.Equal("123456789", savedImport.Account);
            Assert.Equal(CurrencyCode.CZK, savedImport.Currency);
            Assert.Equal(1, savedImport.ItemCount);
            Assert.Equal("OK", savedImport.ImportResult);
        }
    }
}

/// <summary>
/// Custom test factory for bank statement import integration tests
/// </summary>
public class BankStatementImportTestFactory : HebloWebApplicationFactory
{
    public Mock<IBankClient> MockBankClient { get; }
    public Mock<IBankStatementImportService> MockImportService { get; }

    public BankStatementImportTestFactory()
    {
        MockBankClient = new Mock<IBankClient>();
        MockImportService = new Mock<IBankStatementImportService>();
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // Remove existing registrations
        var bankClientDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IBankClient));
        if (bankClientDescriptor != null)
        {
            services.Remove(bankClientDescriptor);
        }

        var importServiceDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IBankStatementImportService));
        if (importServiceDescriptor != null)
        {
            services.Remove(importServiceDescriptor);
        }

        // Configure bank account settings for tests
        services.Configure<BankAccountSettings>(options =>
        {
            options.Accounts = new List<BankAccountConfiguration>
            {
                new BankAccountConfiguration
                {
                    Name = "CZK",
                    AccountNumber = "123456789",
                    FlexiBeeId = 1
                },
                new BankAccountConfiguration
                {
                    Name = "EUR",
                    AccountNumber = "987654321",
                    FlexiBeeId = 2
                }
            };
        });

        // Register mocks
        services.AddSingleton(MockBankClient.Object);
        services.AddSingleton(MockImportService.Object);

        base.ConfigureTestServices(services);
    }
}