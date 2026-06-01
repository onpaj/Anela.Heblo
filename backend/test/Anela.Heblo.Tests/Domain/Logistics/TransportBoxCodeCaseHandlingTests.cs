using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;

namespace Anela.Heblo.Tests.Domain.Logistics;

public class TransportBoxCodeCaseHandlingTests
{
    private const string TestUser = "TestUser";
    private readonly DateTime _testDate = DateTime.UtcNow;

    [Theory]
    [InlineData("b001", "B001")]
    [InlineData("b123", "B123")]
    [InlineData("b999", "B999")]
    [InlineData("B001", "B001")]
    [InlineData("B123", "B123")]
    [InlineData("B999", "B999")]
    public void Open_WithAnyCase_ShouldStoreAsUppercase(string inputCode, string expectedCode)
    {
        // Arrange
        var box = new TransportBox();

        // Act
        box.Open(inputCode, _testDate, TestUser);

        // Assert
        box.Code.Should().Be(expectedCode);
        box.State.Should().Be(TransportBoxState.Opened);
    }

    [Theory]
    [InlineData("b12")]  // Too short, lowercase
    [InlineData("c001")] // Wrong prefix, lowercase  
    [InlineData("b1a1")] // Non-numeric, lowercase
    [InlineData("bxyz")] // All letters, lowercase
    public void Open_WithInvalidLowercaseCode_ShouldThrowValidationException(string invalidCode)
    {
        // Arrange
        var box = new TransportBox();

        // Act
        var act = () => box.Open(invalidCode, _testDate, TestUser);

        // Assert
        act.Should().Throw<ValidationException>()
            .WithMessage("Box code must follow format: B + 3 digits (e.g., B001, B123)");
    }

    [Theory]
    [InlineData("b001")]
    [InlineData("B001")]
    [InlineData("b123")]
    [InlineData("B123")]
    public void ConfirmTransit_WithCaseInsensitiveConfirmation_ShouldWorkWhenMatching(string confirmedCode)
    {
        // Arrange
        var box = new TransportBox();
        box.Open("B001", _testDate, TestUser); // Always stored as B001 (uppercase)
        box.AddItem("PRODUCT001", "Test Product", 1.0, _testDate, TestUser);

        // Act & Assert
        if (confirmedCode.ToUpper() == "B001")
        {
            // This should work - exact match after normalization
            var act = () => box.ConfirmTransit(confirmedCode, _testDate, TestUser);
            act.Should().NotThrow();
            box.State.Should().Be(TransportBoxState.InTransit);
        }
        else
        {
            // This should fail - different code
            var act = () => box.ConfirmTransit(confirmedCode, _testDate, TestUser);
            act.Should().Throw<ValidationException>()
                .WithMessage($"Box number mismatch: entered '{confirmedCode}' but expected 'B001'");
        }
    }

    [Fact]
    public void ConfirmTransit_WithLowercaseConfirmation_ShouldWorkAfterFix()
    {
        // Arrange
        var box = new TransportBox();
        box.Open("B001", _testDate, TestUser);
        box.AddItem("PRODUCT001", "Test Product", 1.0, _testDate, TestUser);

        // Act
        var act = () => box.ConfirmTransit("b001", _testDate, TestUser);

        // Assert - Should work after case-insensitive fix
        act.Should().NotThrow();
        box.State.Should().Be(TransportBoxState.InTransit);
    }

    [Fact]
    public void AssignBoxCodeIfAny_WithLowercase_ShouldAssignAsIs()
    {
        // Arrange
        var box = new TransportBox();

        // Act
        box.AssignBoxCodeIfAny("b001");

        // Assert - This method doesn't normalize case, it's just an assignment
        box.Code.Should().Be("b001");
        box.State.Should().Be(TransportBoxState.New);
    }

    [Fact]
    public void Open_AcceptsLowercasePrefix_AfterFix()
    {
        // Arrange
        var box = new TransportBox();

        // Act
        var act = () => box.Open("b001", _testDate, TestUser);

        // Assert - Should work after accepting both cases
        act.Should().NotThrow();
        box.Code.Should().Be("B001");
        box.State.Should().Be(TransportBoxState.Opened);
    }
}