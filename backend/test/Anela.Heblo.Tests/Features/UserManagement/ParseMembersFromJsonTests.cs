using System.Collections.Generic;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using FluentAssertions;
using Xunit;
using System.Linq;

namespace Anela.Heblo.Tests.Features.UserManagement;

public class ParseMembersFromJsonTests
{
    [Fact]
    public void ReturnsEmptyList_WhenJsonHasNoValueProperty()
    {
        // Arrange
        var json = "{}";

        // Act
        var (users, totalCount) = GraphService.ParseMembersFromJson(json);

        // Assert
        users.Should().BeEmpty();
        totalCount.Should().Be(0);
    }

    [Fact]
    public void ReturnsEmptyList_WhenValueArrayIsEmpty()
    {
        // Arrange
        var json = """{"value":[]}""";

        // Act
        var (users, totalCount) = GraphService.ParseMembersFromJson(json);

        // Assert
        users.Should().BeEmpty();
        totalCount.Should().Be(0);
    }

    [Fact]
    public void MapsSingleUser_WithAllFieldsPresent()
    {
        // Arrange
        var json = """
{
  "value": [
    {
      "@odata.type": "#microsoft.graph.user",
      "id": "11111111-1111-1111-1111-111111111111",
      "displayName": "Alice Example",
      "mail": "alice@example.com",
      "userPrincipalName": "alice@example.com"
    }
  ]
}
""";

        // Act
        var (users, totalCount) = GraphService.ParseMembersFromJson(json);

        // Assert
        users.Should().HaveCount(1);
        totalCount.Should().Be(1);

        var user = users.First();
        user.Id.Should().Be("11111111-1111-1111-1111-111111111111");
        user.DisplayName.Should().Be("Alice Example");
        user.Email.Should().Be("alice@example.com");
    }

    [Fact]
    public void FallsBackToUserPrincipalName_WhenMailIsMissing()
    {
        // Arrange
        var json = """
{
  "value": [
    {
      "@odata.type": "#microsoft.graph.user",
      "id": "22222222-2222-2222-2222-222222222222",
      "displayName": "Bob Example",
      "mail": null,
      "userPrincipalName": "bob@example.com"
    }
  ]
}
""";

        // Act
        var (users, totalCount) = GraphService.ParseMembersFromJson(json);

        // Assert
        users.Should().HaveCount(1);
        totalCount.Should().Be(1);

        var user = users.First();
        user.Email.Should().Be("bob@example.com");
    }

    [Fact]
    public void SkipsGroupEntries_AndCountsThemInTotal()
    {
        // Arrange
        var json = """
{
  "value": [
    {
      "@odata.type": "#microsoft.graph.user",
      "id": "11111111-1111-1111-1111-111111111111",
      "displayName": "Alice Example",
      "mail": "alice@example.com",
      "userPrincipalName": "alice@example.com"
    },
    {
      "@odata.type": "#microsoft.graph.group",
      "id": "33333333-3333-3333-3333-333333333333",
      "displayName": "Nested Group"
    },
    {
      "@odata.type": "#microsoft.graph.user",
      "id": "22222222-2222-2222-2222-222222222222",
      "displayName": "Bob Example",
      "mail": "bob@example.com",
      "userPrincipalName": "bob@example.com"
    }
  ]
}
""";

        // Act
        var (users, totalCount) = GraphService.ParseMembersFromJson(json);

        // Assert
        users.Should().HaveCount(2);
        totalCount.Should().Be(3);
        users.Select(u => u.DisplayName).Should().ContainInOrder("Alice Example", "Bob Example");
    }

    [Fact]
    public void TolerateMissingOptionalFields_OnUserEntries()
    {
        // Arrange
        var json = """
{
  "value": [
    {
      "@odata.type": "#microsoft.graph.user",
      "id": "11111111-1111-1111-1111-111111111111"
    }
  ]
}
""";

        // Act
        var (users, totalCount) = GraphService.ParseMembersFromJson(json);

        // Assert
        users.Should().HaveCount(1);
        totalCount.Should().Be(1);

        var user = users.First();
        user.Id.Should().Be("11111111-1111-1111-1111-111111111111");
        user.DisplayName.Should().Be(string.Empty);
        user.Email.Should().Be(string.Empty);
    }

    [Fact]
    public void TreatsEntryWithUserPrincipalNameAsUser_EvenWithoutOdataType()
    {
        // Arrange
        var json = """
{
  "value": [
    {
      "id": "44444444-4444-4444-4444-444444444444",
      "displayName": "Charlie Example",
      "mail": "charlie@example.com",
      "userPrincipalName": "charlie@example.com"
    }
  ]
}
""";

        // Act
        var (users, totalCount) = GraphService.ParseMembersFromJson(json);

        // Assert
        users.Should().HaveCount(1);
        totalCount.Should().Be(1);

        var user = users.First();
        user.Id.Should().Be("44444444-4444-4444-4444-444444444444");
        user.DisplayName.Should().Be("Charlie Example");
        user.Email.Should().Be("charlie@example.com");
    }
}
