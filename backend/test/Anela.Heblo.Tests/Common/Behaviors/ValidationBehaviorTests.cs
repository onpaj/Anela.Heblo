using Anela.Heblo.Application.Common.Behaviors;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Common.Behaviors;

// Test request and response for validation behavior testing
public record TestValidationRequest(string Name, int Age) : IRequest<TestValidationResponse>;
public record TestValidationResponse(string Message);

public class TestValidationRequestValidator : AbstractValidator<TestValidationRequest>
{
    public TestValidationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Name is required");
            
        RuleFor(x => x.Age)
            .GreaterThan(0)
            .WithMessage("Age must be greater than 0")
            .LessThan(120)
            .WithMessage("Age must be less than 120");
    }
}

public class ValidationBehaviorTests
{
    private readonly Mock<RequestHandlerDelegate<TestValidationResponse>> _nextMock;
    private readonly TestValidationRequestValidator _validator;
    private readonly ValidationBehavior<TestValidationRequest, TestValidationResponse> _behavior;

    public ValidationBehaviorTests()
    {
        _nextMock = new Mock<RequestHandlerDelegate<TestValidationResponse>>();
        _validator = new TestValidationRequestValidator();
        _behavior = new ValidationBehavior<TestValidationRequest, TestValidationResponse>(
            new[] { _validator });
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsNext()
    {
        // Arrange
        var request = new TestValidationRequest("John Doe", 30);
        var expectedResponse = new TestValidationResponse("Success");
        
        _nextMock.Setup(x => x()).ReturnsAsync(expectedResponse);

        // Act
        var result = await _behavior.Handle(request, _nextMock.Object, CancellationToken.None);

        // Assert
        result.Should().Be(expectedResponse);
        _nextMock.Verify(x => x(), Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidRequest_ThrowsValidationException()
    {
        // Arrange
        var request = new TestValidationRequest("", -5); // Invalid: empty name, negative age

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _behavior.Handle(request, _nextMock.Object, CancellationToken.None));

        exception.Errors.Should().HaveCount(2);
        exception.Errors.Should().Contain(e => e.ErrorMessage == "Name is required");
        exception.Errors.Should().Contain(e => e.ErrorMessage == "Age must be greater than 0");
        
        _nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_PartiallyInvalidRequest_ThrowsValidationExceptionWithRelevantErrors()
    {
        // Arrange
        var request = new TestValidationRequest("John Doe", 150); // Valid name, invalid age

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _behavior.Handle(request, _nextMock.Object, CancellationToken.None));

        exception.Errors.Should().HaveCount(1);
        exception.Errors.Single().ErrorMessage.Should().Be("Age must be less than 120");
        
        _nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_NoValidators_CallsNextWithoutValidation()
    {
        // Arrange
        var behaviorWithoutValidators = new ValidationBehavior<TestValidationRequest, TestValidationResponse>(
            Enumerable.Empty<IValidator<TestValidationRequest>>());
        
        var request = new TestValidationRequest("", -5); // Would normally be invalid
        var expectedResponse = new TestValidationResponse("Success");
        
        _nextMock.Setup(x => x()).ReturnsAsync(expectedResponse);

        // Act
        var result = await behaviorWithoutValidators.Handle(request, _nextMock.Object, CancellationToken.None);

        // Assert
        result.Should().Be(expectedResponse);
        _nextMock.Verify(x => x(), Times.Once);
    }

    [Fact]
    public async Task Handle_MultipleValidators_CombinesAllValidationErrors()
    {
        // Arrange
        var secondValidator = new Mock<IValidator<TestValidationRequest>>();
        var validationFailure = new ValidationFailure("CustomProperty", "Custom validation error");
        var validationResult = new ValidationResult(new[] { validationFailure });
        
        secondValidator.Setup(x => x.ValidateAsync(It.IsAny<ValidationContext<TestValidationRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        var behaviorWithMultipleValidators = new ValidationBehavior<TestValidationRequest, TestValidationResponse>(
            new IValidator<TestValidationRequest>[] { _validator, secondValidator.Object });

        var request = new TestValidationRequest("", -5); // Invalid for first validator

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => behaviorWithMultipleValidators.Handle(request, _nextMock.Object, CancellationToken.None));

        exception.Errors.Should().HaveCount(3); // 2 from first validator + 1 from second validator
        exception.Errors.Should().Contain(e => e.ErrorMessage == "Name is required");
        exception.Errors.Should().Contain(e => e.ErrorMessage == "Age must be greater than 0");
        exception.Errors.Should().Contain(e => e.ErrorMessage == "Custom validation error");
        
        _nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidatorThrowsException_PropagatesException()
    {
        // Arrange
        var faultyValidator = new Mock<IValidator<TestValidationRequest>>();
        var expectedException = new InvalidOperationException("Validator error");
        
        faultyValidator.Setup(x => x.ValidateAsync(It.IsAny<ValidationContext<TestValidationRequest>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var behaviorWithFaultyValidator = new ValidationBehavior<TestValidationRequest, TestValidationResponse>(
            new[] { faultyValidator.Object });

        var request = new TestValidationRequest("John Doe", 30);

        // Act & Assert
        var thrownException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => behaviorWithFaultyValidator.Handle(request, _nextMock.Object, CancellationToken.None));

        thrownException.Should().Be(expectedException);
        _nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_CancellationRequested_RespectsCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var request = new TestValidationRequest("John Doe", 30);
        
        // Set up validator to respect cancellation
        var slowValidator = new Mock<IValidator<TestValidationRequest>>();
        slowValidator.Setup(x => x.ValidateAsync(It.IsAny<ValidationContext<TestValidationRequest>>(), It.IsAny<CancellationToken>()))
            .Returns((ValidationContext<TestValidationRequest> context, CancellationToken ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new ValidationResult());
            });

        var behaviorWithSlowValidator = new ValidationBehavior<TestValidationRequest, TestValidationResponse>(
            new[] { slowValidator.Object });

        cts.Cancel(); // Cancel before execution

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => behaviorWithSlowValidator.Handle(request, _nextMock.Object, cts.Token));

        _nextMock.Verify(x => x(), Times.Never);
    }
}