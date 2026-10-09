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

    [Fact]
    public async Task Handle_WhenRuleExists_UpdatesAllFieldsAndSetsUpdatedByFromCurrentUser()
    {
        var existing = CreateExistingRule();
        var request = CreateRequest(existing.Id);
        _repo.Setup(r => r.GetByIdAsync(existing.Id)).ReturnsAsync(existing);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<ClassificationRule>()))
            .ReturnsAsync((ClassificationRule r) => r);
        _mapper.Setup(m => m.Map<ClassificationRuleDto>(It.IsAny<object>()))
            .Returns(new ClassificationRuleDto());

        await CreateHandler().Handle(request, CancellationToken.None);

        existing.Name.Should().Be(request.Name);
        existing.RuleTypeIdentifier.Should().Be(request.RuleTypeIdentifier);
        existing.Pattern.Should().Be(request.Pattern);
        existing.AccountingTemplateCode.Should().Be(request.AccountingTemplateCode);
        existing.Department.Should().Be(request.Department);
        existing.IsActive.Should().Be(request.IsActive);
        existing.UpdatedBy.Should().Be("Test User");
    }

    [Fact]
    public async Task Handle_WhenRuleExists_PersistsAndReturnsMappedDtoOfUpdatedRule()
    {
        var existing = CreateExistingRule();
        var updated = CreateExistingRule();
        var sentinelDto = new ClassificationRuleDto();
        _repo.Setup(r => r.GetByIdAsync(existing.Id)).ReturnsAsync(existing);
        _repo.Setup(r => r.UpdateAsync(existing)).ReturnsAsync(updated);
        _mapper.Setup(m => m.Map<ClassificationRuleDto>(updated)).Returns(sentinelDto);

        var response = await CreateHandler().Handle(CreateRequest(existing.Id), CancellationToken.None);

        response.Rule.Should().BeSameAs(sentinelDto);
        _repo.Verify(r => r.UpdateAsync(existing), Times.Once);
        _mapper.Verify(m => m.Map<ClassificationRuleDto>(updated), Times.Once);
    }

    [Fact]
    public async Task Handle_WithNullDepartmentAndInactive_PropagatesValues()
    {
        var existing = CreateExistingRule();
        var request = CreateRequest(existing.Id);
        request.Department = null;
        request.IsActive = false;
        _repo.Setup(r => r.GetByIdAsync(existing.Id)).ReturnsAsync(existing);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<ClassificationRule>()))
            .ReturnsAsync((ClassificationRule r) => r);
        _mapper.Setup(m => m.Map<ClassificationRuleDto>(It.IsAny<object>()))
            .Returns(new ClassificationRuleDto());

        await CreateHandler().Handle(request, CancellationToken.None);

        existing.Department.Should().BeNull();
        existing.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenCurrentUserNameIsNull_ThrowsArgumentNullExceptionAndDoesNotPersist()
    {
        var existing = CreateExistingRule();
        _repo.Setup(r => r.GetByIdAsync(existing.Id)).ReturnsAsync(existing);
        _currentUser.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("id", null, null, true));

        var act = () => CreateHandler().Handle(CreateRequest(existing.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentNullException>())
            .Which.ParamName.Should().Be("updatedBy");
        _repo.Verify(r => r.UpdateAsync(It.IsAny<ClassificationRule>()), Times.Never);
    }
}
