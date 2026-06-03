using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Common.Behaviors;

// Test request and response for validation result behavior testing
public class TestResultResponse : BaseResponse
{
    public string Message { get; set; } = string.Empty;
}

public record TestResultRequest(string Name, int Age) : IRequest<TestResultResponse>;

public class TestResultRequestValidator : AbstractValidator<TestResultRequest>
{
    public TestResultRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Name is required")
            .WithErrorCode("1")
            .WithState(x => new Dictionary<string, string> { { "field", "Name" } });

        RuleFor(x => x.Age)
            .GreaterThan(0)
            .WithMessage("Age must be greater than 0")
            .WithErrorCode("4")
            .WithState(x => new Dictionary<string, string> { { "field", "Age" }, { "minValue", "0" } })
            .LessThan(120)
            .WithMessage("Age must be less than 120")
            .WithErrorCode("5")
            .WithState(x => new Dictionary<string, string> { { "field", "Age" }, { "maxValue", "120" } });
    }
}

public class ValidationResultBehaviorTests
{
    private readonly Mock<RequestHandlerDelegate<TestResultResponse>> _nextMock;
    private readonly TestResultRequestValidator _validator;
    private readonly ValidationResultBehavior<TestResultRequest, TestResultResponse> _behavior;

    public ValidationResultBehaviorTests()
    {
        _nextMock = new Mock<RequestHandlerDelegate<TestResultResponse>>();
        _validator = new TestResultRequestValidator();
        _behavior = new ValidationResultBehavior<TestResultRequest, TestResultResponse>(
            new[] { _validator });
    }

    [Fact]
    public async Task Handle_NoValidators_CallsNext()
    {
        // Arrange
        var behaviorWithoutValidators = new ValidationResultBehavior<TestResultRequest, TestResultResponse>(
            Enumerable.Empty<IValidator<TestResultRequest>>());

        var request = new TestResultRequest("John Doe", 30);
        var expectedResponse = new TestResultResponse { Message = "Success" };

        _nextMock.Setup(x => x()).ReturnsAsync(expectedResponse);

        // Act
        var result = await behaviorWithoutValidators.Handle(request, _nextMock.Object, CancellationToken.None);

        // Assert
        result.Should().Be(expectedResponse);
        _nextMock.Verify(x => x(), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsNext()
    {
        // Arrange
        var request = new TestResultRequest("John Doe", 30);
        var expectedResponse = new TestResultResponse { Message = "Success" };

        _nextMock.Setup(x => x()).ReturnsAsync(expectedResponse);

        // Act
        var result = await _behavior.Handle(request, _nextMock.Object, CancellationToken.None);

        // Assert
        result.Should().Be(expectedResponse);
        _nextMock.Verify(x => x(), Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidRequest_ReturnsErrorResponse()
    {
        // Arrange
        var request = new TestResultRequest("", -5); // Invalid: empty name, negative age
        var expectedParams = new Dictionary<string, string> { { "field", "Name" } };

        // Act
        var result = await _behavior.Handle(request, _nextMock.Object, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError); // ErrorCode "1"
        result.Params.Should().Equal(expectedParams);
        _nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidRequest_FallsBackToValidationError_WhenErrorCodeNotParseable()
    {
        // Arrange
        var unparsableValidator = new Mock<IValidator<TestResultRequest>>();
        var validationFailure = new ValidationFailure("CustomProperty", "Custom validation error")
        {
            ErrorCode = "NonExistentErrorCode",
            CustomState = new Dictionary<string, string> { { "key", "value" } }
        };
        var validationResult = new ValidationResult(new[] { validationFailure });

        unparsableValidator.Setup(x => x.ValidateAsync(It.IsAny<ValidationContext<TestResultRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        var behavior = new ValidationResultBehavior<TestResultRequest, TestResultResponse>(
            new IValidator<TestResultRequest>[] { unparsableValidator.Object });

        var request = new TestResultRequest("John Doe", 30);

        // Act
        var result = await behavior.Handle(request, _nextMock.Object, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError); // Falls back to ValidationError
        result.Params.Should().Equal(new Dictionary<string, string> { { "key", "value" } });
        _nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_MultipleFailures_ReturnsFirstFailure()
    {
        // Arrange
        var request = new TestResultRequest("", 150); // Invalid: empty name, age too high
        var expectedParams = new Dictionary<string, string> { { "field", "Name" } };

        // Act
        var result = await _behavior.Handle(request, _nextMock.Object, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError); // First failure is Name (ErrorCode "1")
        result.Params.Should().Equal(expectedParams);
        _nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidRequest_NextNeverCalledWhenFails()
    {
        // Arrange
        var request = new TestResultRequest("", 30); // Invalid: empty name

        // Act
        var result = await _behavior.Handle(request, _nextMock.Object, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        _nextMock.Verify(x => x(), Times.Never);
    }
}
