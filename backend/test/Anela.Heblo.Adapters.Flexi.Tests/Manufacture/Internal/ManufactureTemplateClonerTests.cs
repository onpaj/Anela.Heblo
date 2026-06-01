using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture.Internal;

public class ManufactureTemplateClonerTests
{
    private static ManufactureTemplate BuildTemplate() => new()
    {
        TemplateId = 42,
        ProductCode = "MAS001001M",
        ProductName = "Hedvábný pan Jasmín",
        Amount = 10,
        OriginalAmount = 10,
        BatchSize = 100,
        ManufactureType = ManufactureType.MultiPhase,
        Ingredients = new List<Ingredient>
        {
            new()
            {
                TemplateId = 100,
                ProductCode = "AKL001",
                ProductName = "Bisabolol",
                Amount = 1.5,
                OriginalAmount = 1.5,
                Price = 12.34m,
                ProductType = ProductType.Material,
                HasLots = true,
                HasExpiration = false
            },
            new()
            {
                TemplateId = 101,
                ProductCode = "AKL003",
                ProductName = "Dermosoft Eco 1388",
                Amount = 2.0,
                OriginalAmount = 2.0,
                Price = 5.5m,
                ProductType = ProductType.Material,
                HasLots = false,
                HasExpiration = false
            }
        }
    };

    [Fact]
    public void Clone_ReturnsTemplateWithSameScalarValues()
    {
        var original = BuildTemplate();

        var clone = ManufactureTemplateCloner.Clone(original);

        clone.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Clone_ReturnsReferenceDistinctRootAndIngredientList()
    {
        var original = BuildTemplate();

        var clone = ManufactureTemplateCloner.Clone(original);

        clone.Should().NotBeSameAs(original);
        clone.Ingredients.Should().NotBeSameAs(original.Ingredients);
        for (var i = 0; i < clone.Ingredients.Count; i++)
        {
            clone.Ingredients[i].Should().NotBeSameAs(original.Ingredients[i]);
        }
    }

    [Fact]
    public void Clone_PreservesPhaseLabel()
    {
        var original = BuildTemplate();
        original.Ingredients[0].PhaseLabel = "A";
        original.Ingredients[1].PhaseLabel = null;

        var clone = ManufactureTemplateCloner.Clone(original);

        clone.Ingredients[0].PhaseLabel.Should().Be("A");
        clone.Ingredients[1].PhaseLabel.Should().BeNull();
    }

    [Fact]
    public void Clone_MutationOnCloneDoesNotAffectOriginal()
    {
        var original = BuildTemplate();
        var clone = ManufactureTemplateCloner.Clone(original);

        clone.BatchSize = 999;
        clone.Ingredients[0].Amount = 99.9;
        clone.Ingredients.Add(new Ingredient
        {
            TemplateId = 999,
            ProductCode = "X",
            ProductName = "X",
            ProductType = ProductType.Material
        });

        original.BatchSize.Should().Be(100);
        original.Ingredients[0].Amount.Should().Be(1.5);
        original.Ingredients.Count.Should().Be(2);
    }
}
