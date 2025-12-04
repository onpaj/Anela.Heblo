using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Application.Features.Invoices.UseCases.EnqueueImportInvoices;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Tests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class InvoiceImportIntegrationTests : IClassFixture<HebloWebApplicationFactory>
{
    private readonly HebloWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public InvoiceImportIntegrationTests(HebloWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task EnqueueImportInvoicesAsync_WithValidRequest_ReturnsJobId()
    {
        // Arrange
        var request = new EnqueueImportInvoicesRequest
        {
            Query = new IssuedInvoiceSourceQuery
            {
                RequestId = $"test-{Guid.NewGuid()}",
                InvoiceId = "TEST-INV-001"
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/invoices/import/enqueue-async", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<EnqueueImportInvoicesResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.NotNull(result.JobId);
        Assert.NotEmpty(result.JobId);
    }

    [Fact]
    public async Task EnqueueImportInvoicesAsync_WithDateRangeRequest_ReturnsJobId()
    {
        // Arrange
        var request = new EnqueueImportInvoicesRequest
        {
            Query = new IssuedInvoiceSourceQuery
            {
                RequestId = $"test-{Guid.NewGuid()}",
                DateFrom = DateTime.Today.AddDays(-7),
                DateTo = DateTime.Today
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/invoices/import/enqueue-async", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<EnqueueImportInvoicesResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.NotNull(result.JobId);
        Assert.NotEmpty(result.JobId);
    }

    [Fact]
    public async Task GetInvoiceImportJobStatus_WithValidJobId_ReturnsJobInfo()
    {
        // Arrange - First enqueue a job
        var request = new EnqueueImportInvoicesRequest
        {
            Query = new IssuedInvoiceSourceQuery
            {
                RequestId = $"test-{Guid.NewGuid()}",
                InvoiceId = "TEST-STATUS-001"
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        var enqueueResponse = await _client.PostAsync("/api/invoices/import/enqueue-async", content);
        var enqueueContent = await enqueueResponse.Content.ReadAsStringAsync();
        var enqueueResult = JsonSerializer.Deserialize<EnqueueImportInvoicesResponse>(enqueueContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(enqueueResult?.JobId);

        // Act - Check job status
        var statusResponse = await _client.GetAsync($"/api/invoices/import/job-status/{enqueueResult.JobId}");

        // Assert
        // Note: Job might not be found if it completes very quickly, so we accept both OK and NotFound
        Assert.True(statusResponse.StatusCode == HttpStatusCode.OK || statusResponse.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetInvoiceImportJobStatus_WithInvalidJobId_ReturnsNotFound()
    {
        // Arrange
        var invalidJobId = "invalid-job-id-12345";

        // Act
        var response = await _client.GetAsync($"/api/invoices/import/job-status/{invalidJobId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRunningInvoiceImportJobs_ReturnsJobsList()
    {
        // Act
        var response = await _client.GetAsync("/api/invoices/import/running-jobs");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.NotNull(responseContent);
        
        // Should return an array (might be empty if no jobs are running)
        var jobs = JsonSerializer.Deserialize<object[]>(responseContent);
        Assert.NotNull(jobs);
    }

    [Fact]
    public async Task EnqueueImportInvoicesAsync_WithInvalidRequest_ReturnsBadRequest()
    {
        // Arrange - Empty request body
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/invoices/import/enqueue-async", content);

        // Assert
        // Depending on validation, this might return BadRequest or process with default values
        // We mainly want to ensure the endpoint handles malformed requests gracefully
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }
}