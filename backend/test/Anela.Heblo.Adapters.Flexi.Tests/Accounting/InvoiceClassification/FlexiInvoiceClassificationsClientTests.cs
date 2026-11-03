using System.Net;
using Anela.Heblo.Adapters.Flexi.Accounting.InvoiceClassification;
using Anela.Heblo.Application.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Model.Response;

namespace Anela.Heblo.Adapters.Flexi.Tests.Accounting.InvoiceClassification;

public class FlexiInvoiceClassificationsClientTests
{
    private const string InvoiceId = "INV-1";
    private const string TriggerLabel = "TRIGGER-X";
    private const string ManualLabel = "MANUAL-X";

    private readonly Mock<IAccountingTemplateClient> _templates = new();
    private readonly Mock<IReceivedInvoiceClient> _invoices = new();

    private FlexiInvoiceClassificationsClient Sut() => new(
        _templates.Object,
        _invoices.Object,
        Options.Create(new DataSourceOptions
        {
            InvoiceClassificationTriggerLabel = TriggerLabel,
            InvoiceClassificationManualReviewLabel = ManualLabel,
        }),
        NullLogger<FlexiInvoiceClassificationsClient>.Instance);

    private static OperationResult<OperationResultDetail> UpdateResult(bool ok) =>
        ok
            ? new OperationResult<OperationResultDetail>(HttpStatusCode.OK)
            : new OperationResult<OperationResultDetail>(HttpStatusCode.InternalServerError, "boom");

    private static OperationResult<ReceivedInvoiceTagsResult> TagResult(bool ok) =>
        ok
            ? new OperationResult<ReceivedInvoiceTagsResult>(HttpStatusCode.OK)
            : new OperationResult<ReceivedInvoiceTagsResult>(HttpStatusCode.InternalServerError, "boom");

    // The single-tag AddTagAsync/RemoveTagAsync are SDK extension methods that delegate to the
    // IEnumerable<string> interface overloads, so that is what must be set up and verified.
    private static IEnumerable<string> Only(string label) =>
        It.Is<IEnumerable<string>>(l => l.SequenceEqual(new[] { label }));

    [Fact]
    public async Task UpdateInvoiceClassificationAsync_WhenUpdateSucceeds_RemovesBothTagsAndReturnsTrue()
    {
        _templates.Setup(x => x.UpdateInvoiceAsync(InvoiceId, "TPL", "DEP", It.IsAny<CancellationToken>())).ReturnsAsync(UpdateResult(true));
        _invoices.Setup(x => x.RemoveTagAsync(InvoiceId, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TagResult(true));

        var result = await Sut().UpdateInvoiceClassificationAsync(InvoiceId, "TPL", "DEP");

        result.Should().BeTrue();
        _invoices.Verify(x => x.RemoveTagAsync(
            InvoiceId,
            It.Is<IEnumerable<string>>(l => l.OrderBy(s => s).SequenceEqual(new[] { ManualLabel, TriggerLabel })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateInvoiceClassificationAsync_WhenUpdateFails_DoesNotRemoveTagsAndReturnsFalse()
    {
        _templates.Setup(x => x.UpdateInvoiceAsync(InvoiceId, "TPL", null, It.IsAny<CancellationToken>())).ReturnsAsync(UpdateResult(false));

        var result = await Sut().UpdateInvoiceClassificationAsync(InvoiceId, "TPL", null);

        result.Should().BeFalse();
        _invoices.Verify(x => x.RemoveTagAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkInvoiceForManualReviewAsync_WhenBothSucceed_ReturnsTrue()
    {
        _invoices.Setup(x => x.AddTagAsync(InvoiceId, Only(ManualLabel), It.IsAny<CancellationToken>())).ReturnsAsync(TagResult(true));
        _invoices.Setup(x => x.RemoveTagAsync(InvoiceId, Only(TriggerLabel), It.IsAny<CancellationToken>())).ReturnsAsync(TagResult(true));

        var result = await Sut().MarkInvoiceForManualReviewAsync(InvoiceId, "reason");

        result.Should().BeTrue();
        _invoices.Verify(x => x.AddTagAsync(InvoiceId, Only(ManualLabel), It.IsAny<CancellationToken>()), Times.Once);
        _invoices.Verify(x => x.RemoveTagAsync(InvoiceId, Only(TriggerLabel), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkInvoiceForManualReviewAsync_WhenRemoveFails_ReturnsFalse()
    {
        _invoices.Setup(x => x.AddTagAsync(InvoiceId, Only(ManualLabel), It.IsAny<CancellationToken>())).ReturnsAsync(TagResult(true));
        _invoices.Setup(x => x.RemoveTagAsync(InvoiceId, Only(TriggerLabel), It.IsAny<CancellationToken>())).ReturnsAsync(TagResult(false));

        var result = await Sut().MarkInvoiceForManualReviewAsync(InvoiceId, "reason");

        result.Should().BeFalse();
        _invoices.Verify(x => x.AddTagAsync(InvoiceId, Only(ManualLabel), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkInvoiceForManualReviewAsync_WhenAddFails_ReturnsFalse()
    {
        _invoices.Setup(x => x.AddTagAsync(InvoiceId, Only(ManualLabel), It.IsAny<CancellationToken>())).ReturnsAsync(TagResult(false));
        _invoices.Setup(x => x.RemoveTagAsync(InvoiceId, Only(TriggerLabel), It.IsAny<CancellationToken>())).ReturnsAsync(TagResult(true));

        var result = await Sut().MarkInvoiceForManualReviewAsync(InvoiceId, "reason");

        result.Should().BeFalse();
    }
}
