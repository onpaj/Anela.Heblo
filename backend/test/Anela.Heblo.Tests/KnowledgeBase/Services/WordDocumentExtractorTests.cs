using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.Services.DocumentExtractors;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class WordDocumentExtractorTests
{
    private readonly WordDocumentExtractor _extractor = new(NullLogger<WordDocumentExtractor>.Instance);

    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/msword")]
    [InlineData("APPLICATION/VND.OPENXMLFORMATS-OFFICEDOCUMENT.WORDPROCESSINGML.DOCUMENT")]
    public void CanHandle_ReturnsTrue_ForWordContentTypes(string contentType)
    {
        Assert.True(_extractor.CanHandle(contentType));
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    [InlineData("image/png")]
    public void CanHandle_ReturnsFalse_ForNonWordContentTypes(string contentType)
    {
        Assert.False(_extractor.CanHandle(contentType));
    }

    // Note: A real Word document extraction test requires a .docx fixture file.
    // That is covered by integration tests in Task 5.x.
}
