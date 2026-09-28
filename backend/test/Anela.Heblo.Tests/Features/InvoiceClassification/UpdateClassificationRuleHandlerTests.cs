using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.UpdateClassificationRule;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification;

public class UpdateClassificationRuleHandlerTests
{
    private readonly Mock<IClassificationRuleRepository> _repo = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    public UpdateClassificationRuleHandlerTests()
    {
        _currentUser.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("id", "Test User", "t@x.cz", true));
    }

    private UpdateClassificationRuleHandler CreateHandler()
        => new(_repo.Object, _mapper.Object, _currentUser.Object);

    private static ClassificationRule CreateExistingRule()
        => new("old-name", "old-type", "old-pattern", "old-template", "old-dept", "creator");

    private static UpdateClassificationRuleRequest CreateRequest(Guid id) => new()
    {
        Id = id,
        Name = "new-name",
        RuleTypeIdentifier = "new-type",
        Pattern = "new-pattern",
        AccountingTemplateCode = "new-template",
        Department = "new-dept",
        IsActive = true
    };

    [Fact]
    public async Task Handle_WhenRuleNotFound_ThrowsArgumentExceptionWithId()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((ClassificationRule?)null);

        var act = () => CreateHandler().Handle(CreateRequest(id), CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>())
            .WithMessage($"Classification rule with ID {id} not found");
    }

    [Fact]
    public async Task Handle_WhenRuleNotFound_DoesNotCallUpdateAsync()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((ClassificationRule?)null);

        try
        {
            await CreateHandler().Handle(CreateRequest(id), CancellationToken.None);
        }
        catch (ArgumentException)
        {
        }

        _repo.Verify(r => r.UpdateAsync(It.IsAny<ClassificationRule>()), Times.Never);
    }
}
