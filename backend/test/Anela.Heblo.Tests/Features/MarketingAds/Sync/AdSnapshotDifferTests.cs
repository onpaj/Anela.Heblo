using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdSnapshotDifferTests
{
    private static AdEntityState State(string name = "Brand", AdEntityStatus status = AdEntityStatus.Enabled,
        string attributesJson = "{\"budget\":\"500\",\"bidding\":\"MaxConversions\"}") =>
        new(name, status, AdAttributesJson.Parse(attributesJson));

    [Fact]
    public void Identical_states_produce_no_change()
    {
        AdSnapshotDiffer.Diff(State(), State()).Should().BeEmpty();
    }

    [Fact]
    public void Attribute_order_and_whitespace_do_not_count_as_a_change()
    {
        // Arrange — jsonb hands back keys reordered and re-spaced compared with what was written.
        var stored = State(attributesJson: "{\"bidding\": \"MaxConversions\",   \"budget\": \"500\"}");
        var fresh = new AdEntityState("Brand", AdEntityStatus.Enabled,
            new Dictionary<string, string?> { ["budget"] = "500", ["bidding"] = "MaxConversions" });

        // Act + Assert
        AdSnapshotDiffer.Diff(stored, fresh).Should().BeEmpty();
    }

    [Fact]
    public void Status_change_carries_old_and_new_status_as_json_strings()
    {
        // Act
        var changes = AdSnapshotDiffer.Diff(State(), State(status: AdEntityStatus.Paused));

        // Assert
        var change = changes.Should().ContainSingle().Subject;
        change.ChangeType.Should().Be(AdSnapshotDiffer.StatusChange);
        JsonDocument.Parse(change.OldValueJson!).RootElement.GetString().Should().Be("Enabled");
        JsonDocument.Parse(change.NewValueJson!).RootElement.GetString().Should().Be("Paused");
    }

    [Fact]
    public void Name_and_attribute_changes_are_separate_events_with_only_changed_keys()
    {
        // Act
        var changes = AdSnapshotDiffer.Diff(State(), State(name: "Brand CZ", attributesJson: "{\"budget\":\"800\",\"bidding\":\"MaxConversions\"}"));

        // Assert
        changes.Select(c => c.ChangeType).Should().BeEquivalentTo(new[] { AdSnapshotDiffer.NameChange, AdSnapshotDiffer.AttributesChange });
        var attributes = changes.Single(c => c.ChangeType == AdSnapshotDiffer.AttributesChange);
        var after = JsonDocument.Parse(attributes.NewValueJson!).RootElement;
        after.EnumerateObject().Select(p => p.Name).Should().Equal("budget");
        after.GetProperty("budget").GetString().Should().Be("800");
    }

    [Fact]
    public void Removed_attribute_is_reported_with_a_null_new_value()
    {
        var changes = AdSnapshotDiffer.Diff(State(), State(attributesJson: "{\"budget\":\"500\"}"));

        var after = JsonDocument.Parse(changes.Single().NewValueJson!).RootElement;
        after.GetProperty("bidding").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Created_describes_the_new_entity()
    {
        var created = AdSnapshotDiffer.Created(State());

        created.ChangeType.Should().Be(AdSnapshotDiffer.CreatedChange);
        created.OldValueJson.Should().BeNull();
        JsonDocument.Parse(created.NewValueJson!).RootElement.GetProperty("status").GetString().Should().Be("Enabled");
    }
}
